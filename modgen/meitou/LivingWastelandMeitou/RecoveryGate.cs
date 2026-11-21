using System.Numerics;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Movement;

namespace LivingWasteland.MeitouMod;

/// <summary>Gate work for an outpost recovery. A build instance and the closed leaf identify the entrance.</summary>
internal static class RecoveryGate
{
    internal readonly record struct Crossing(string Door, Vector3 Near, Vector3 Far)
    {
        internal bool OnNearSide(Vector3 at) => Vector3.Dot(at - (Near + Far) / 2, Far - Near) < 0;

        internal Vector3 ExitToward(World world, Vector3 at) =>
            PrisonerHandover.ClearSight(world, at, Far) ? Near : Far;

        internal static Crossing[] Read(BinaryReader reader)
        {
            var route = new Crossing[reader.ReadByte()];
            for (int i = 0; i < route.Length; i++)
                route[i] = new(reader.ReadString(), ReadPoint(reader), ReadPoint(reader));
            return route;
        }

        internal static void Write(BinaryWriter writer, Crossing[] route)
        {
            writer.Write((byte)route.Length);
            foreach (var step in route)
            {
                writer.Write(step.Door);
                WritePoint(writer, step.Near);
                WritePoint(writer, step.Far);
            }
        }

        internal static void Hash(ref StateHasher hash, Crossing[] route)
        {
            hash.Add(route.Length);
            foreach (var step in route)
            {
                hash.Add(SquadRecord.StableHash(step.Door));
                AddPoint(ref hash, step.Near);
                AddPoint(ref hash, step.Far);
            }
        }

        static Vector3 ReadPoint(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        static void WritePoint(BinaryWriter writer, Vector3 at) { writer.Write(at.X); writer.Write(at.Y); writer.Write(at.Z); }
        static void AddPoint(ref StateHasher hash, Vector3 at) { hash.Add(at.X); hash.Add(at.Y); hash.Add(at.Z); }
    }

    internal static Fixture? Entrance(World world, LockSystem? locks, int town, Vector3 from, Vector3 to)
    {
        if (locks?.Regions is { } regions && regions.CodeAt(from) is >= 0 and var start &&
            regions.CodeAt(to) is >= 0 and var finish)
        {
            if (start == finish) return null;
            locks.RouteState(world, start, finish, Couriers.Flat(from), out var first);
            if (first is not { } link || world.Buildings.Find(link.Building) is not
                    { Complete: true, Destroyed: false } build || build.Owner != world.Player.Faction || build.Town != town)
                return null;

            // Prefer a passable leaf if this building has more than one.
            return world.Fixtures.OfBuilding(link.Building).Where(d => d.Kind == FixtureKind.Door && d.IsGate &&
                    world.Fixtures.DoorSpotOf(d) is not null)
                .OrderBy(d => d.Lock is { EffectivelyLocked: true } && !d.DoorBroken)
                .ThenBy(d => d.DoorIndex).ThenBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault();
        }

        return Blocking(world, town, from, to, lockedOnly: false);
    }

    internal static Crossing? Passage(World world, Fixture door, Vector3 from)
    {
        if (world.Fixtures.DoorSpotOf(door) is not { } leaf) return null;
        var mid = leaf.Centre with { Y = leaf.Foot };
        var side = leaf.Normal * (Vector3.Dot(from - mid, leaf.Normal) < 0 ? -1 : 1);
        const float offset = 30;
        var near = mid + side * offset;
        var far = mid - side * offset;
        if (!world.Walkability.TryStand(near.X, near.Z, near.Y, out var yNear) ||
            !world.Walkability.TryStand(far.X, far.Z, far.Y, out var yFar) ||
            MathF.Abs(near.Y - yNear) > 20 || MathF.Abs(far.Y - yFar) > 20) return null;
        return new(door.Id, near with { Y = yNear }, far with { Y = yFar });
    }

    internal static bool Shut(Fixture door) => !door.DoorBroken && door.Motion is not (DoorMotion.Open or DoorMotion.Opening);

    internal static Fixture? Blocking(World world, int town, Vector3 from, Vector3 to, bool lockedOnly, bool gatesOnly = true, bool interiorOnly = false)
    {
        Fixture? result = null;
        float nearest = float.PositiveInfinity;
        foreach (var door in world.Fixtures.All)
        {
            if (door is not { Kind: FixtureKind.Door, DoorBroken: false } || (gatesOnly && !door.IsGate) || (interiorOnly && door.IsGate) ||
                door.Motion is DoorMotion.Open or DoorMotion.Opening ||
                (lockedOnly && door.Lock is not { EffectivelyLocked: true }) ||
                world.Buildings.Find(door.Building) is not { Complete: true, Destroyed: false } gate ||
                gate.Town != town || gate.Owner != world.Player.Faction ||
                world.Fixtures.DoorSpotOf(door) is not { } leaf) continue;

            float start = Vector3.Dot(from - leaf.Centre, leaf.Normal);
            float end = Vector3.Dot(to - leaf.Centre, leaf.Normal);
            if ((start < 0) == (end < 0)) continue;
            float along = start / (start - end);
            var cross = Vector3.Lerp(from, to, along) - leaf.Centre;
            var side = Vector3.Cross(Vector3.UnitY, leaf.Normal);
            if (MathF.Abs(Vector3.Dot(cross, side)) > leaf.HalfWidth + 6 ||
                MathF.Abs(cross.Y) > Math.Max(40, leaf.HalfHeight) ||
                (along > nearest || (along == nearest && string.CompareOrdinal(door.Id, result?.Id) >= 0))) continue;

            result = door;
            nearest = along;
        }
        return result;
    }

    internal static bool Bash(World world, int slot, Fixture gate)
    {
        if (world.Characters.Cold(slot) is not { } fighter || gate.DoorBroken ||
            world.Fixtures.DoorSpotOf(gate) is null || !world.Systems.OfType<LockSystem>().Any()) return false;
        if (fighter.Bashing == gate.Id) return true;
        // Meitou owns damage, animation, legal target checks and timing. This is an NPC task, with no player queue.
        var request = new TaskRequest(CharacterTask.GoTo, TaskType.BashDoorPlayerOrder, TaskSource.Goal)
            { Site = gate.Id, Detail = LockForce.Smash };
        return TaskStart.Start(world, slot, request) == StartResult.Started && fighter.Bashing == gate.Id;
    }

    internal static void ClearBash(World world, Squad squad)
    {
        var table = world.Characters;
        foreach (var member in squad.Members)
        {
            if (!table.TryResolveNext(member, out int slot) || table.Cold(slot) is not
                { IsPlayer: false, BashOrdered: true, Running: { Source: TaskSource.Goal, Row: TaskType.BashDoorPlayerOrder } } actor ||
                actor.SquadId != squad.Id) continue;
            (actor.Bashing, actor.BashOrdered) = (null, false);
        }
    }
}
