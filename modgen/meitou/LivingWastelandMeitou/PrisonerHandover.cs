using System.Numerics;
using System.Reflection;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Locks;

namespace LivingWasteland.MeitouMod;

/// <summary>The captive leaves through the normal fixture release at their cage. The game retains the actor and faction.</summary>
internal static class PrisonerHandover
{
    static readonly MethodInfo? release = typeof(FixtureRegistry).GetMethod("Release",
        [typeof(World), typeof(CharacterId), typeof(int), typeof(CrimeSystem), typeof(bool)]);

    static readonly MethodInfo? sight = typeof(IWalkability).GetMethod("LineOfSight", [typeof(Vector3), typeof(Vector3)]);
    internal static bool Available { get; } = release?.ReturnType == typeof(bool) && sight?.ReturnType == typeof(bool);
    internal static Vector3 Exit(Fixture cage) => FixtureRegistry.ExitSpot(cage) ?? cage.Position;

    internal static bool AtExit(World world, int slot, Fixture cage)
    {
        var from = world.Characters.Next[slot].Position;
        var exit = Exit(cage);
        return MathF.Abs(from.Y - exit.Y) <= 20 && Vector3.DistanceSquared(from, exit) <= 45 * 45 &&
            ClearSight(world, from, exit);
    }

    internal static bool ClearSight(World world, Vector3 from, Vector3 to) =>
        sight?.Invoke(world.Walkability, [from + Vector3.UnitY * 10, to + Vector3.UnitY * 10]) is true;

    internal static void Approach(World world, int slot, Fixture cage) => WalkTo(world, slot, Exit(cage));

    internal static void WalkTo(World world, int slot, Vector3 at)
    {
        Vector2 goal = new(at.X, at.Z);
        var hot = world.Characters.Next[slot];
        if (hot.Goal == goal && TaskStart.OnAiWalk(world.Characters, slot)) return;
        TaskStart.Walk(world, slot, at, 0, TaskType.TravelToTargetPackage, TaskSource.Goal);
    }

    internal static bool Free(World world, PrisonerCases.Held held)
    {
        if (!Available || !world.Characters.TryResolveNext(held.Actor, out int slot) ||
            world.Characters.Cold(slot) != held.Captive || held.Captive.Medical is not { Dead: false } ||
            held.Captive.InWhat != held.Cage.Id || held.Cage.Occupant != held.Actor ||
            world.Uniques.RecordOf(held.Actor) != held.Record) return false;
        if (release!.Invoke(world.Fixtures, [world, held.Actor, -1, null, false]) is not true) return false;
        world.Uniques.Freed(held.Record);
        return true;
    }
}
