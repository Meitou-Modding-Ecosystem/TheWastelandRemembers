using System.Numerics;
using System.Reflection;
using Meitou.Simulation;
using Meitou.Simulation.Aggro;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Player;

namespace LivingWasteland.MeitouMod;

/// <summary>Quiet work for TWR's dedicated scouts. Each job must still be reachable, undiscovered and tied to the active captive.</summary>
internal static class CovertRecovery
{
    internal const int Cadence = 10;
    // Internal on the build floor, public on newer clients.
    static readonly MethodInfo? workSpot = typeof(LockSystem).GetMethod("WorkSpot", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
        null, [typeof(World), typeof(Fixture), typeof(Vector2)], null);
    static readonly MethodInfo? spotted = typeof(AggroSystem).GetMethod("Spotted", [typeof(int), typeof(CharacterId)]);
    internal static bool Available => workSpot?.ReturnType == typeof(ValueTuple<Vector2, float>);

    internal static bool Interrupted(World world, Squad squad)
    {
        var table = world.Characters;
        var aggro = spotted is null ? null : world.Systems.OfType<AggroSystem>().FirstOrDefault();
        foreach (var member in squad.Members)
        {
            if (!table.TryResolveNext(member, out int slot) || table.Cold(slot) is not { IsPlayer: false } scout ||
                scout.SquadId != squad.Id || scout.Faction != squad.Faction || !table.Next[slot].Alive) continue;
            if (scout.InCombat) return true;
            if (scout.Medical is not { Dead: false, Incapacitated: false } || table.Next[slot].Dormant) continue;

            var at = table.Next[slot].Position;
            // A sentry within arm's reach can interrupt even if the host doesn't expose a player spot meter.
            for (int i = 0; i < table.HighWater; i++)
            {
                ref readonly var watch = ref table.Next[i];
                if (!watch.Alive || watch.Dormant || table.Cold(i) is not
                    { IsPlayer: true, IsImprisoned: false, Medical: { Incapacitated: false, Dead: false }, CarriedBy.IsNone: true }) continue;

                if ((aggro is not null && spotted!.Invoke(aggro, [i, member]) is true) ||
                    (Vector3.DistanceSquared(at, watch.Position) <= 35 * 35 &&
                     PrisonerHandover.ClearSight(world, watch.Position, at))) return true;
            }
        }
        return false;
    }

    internal static void SetSneak(World world, Squad squad, bool on)
    {
        var table = world.Characters;
        foreach (var member in squad.Members)
        {
            if (!table.TryResolveNext(member, out int slot) || table.Cold(slot) is not { IsPlayer: false } scout ||
                scout.SquadId != squad.Id || scout.Faction != squad.Faction) continue;
            scout.Orders = on ? scout.Orders | CharacterOrders.Sneak : scout.Orders & ~CharacterOrders.Sneak;
        }
    }

    internal static int Picker(World world, Squad squad)
    {
        var table = world.Characters;
        int best = -1;
        float skill = -1;
        foreach (var member in squad.Members)
        {
            if (!table.TryResolveNext(member, out int slot) || !table.Next[slot].Alive ||
                table.Cold(slot) is not { IsPlayer: false, IsImprisoned: false, InCombat: false,
                    Medical: { Dead: false, Incapacitated: false }, CarriedBy.IsNone: true, Carrying.IsNone: true } actor ||
                actor.SquadId != squad.Id || actor.Faction != squad.Faction || Lockpicking.Skill(actor) <= skill) continue;
            best = slot;
            skill = Lockpicking.Skill(actor);
        }
        return best;
    }

    internal static void KeepWatch(World world, int slot, RecoveryGate.Crossing[] entry)
    {
        if (entry.Length > 0 && Vector3.DistanceSquared(world.Characters.Next[slot].Position, entry[0].Near) > 45 * 45)
            PrisonerHandover.WalkTo(world, slot, entry[0].Near);
        else if (TaskStart.OnAiWalk(world.Characters, slot)) TaskStart.Stand(world.Characters, slot);
    }

    internal static bool Withdraw(World world, Squad squad, RecoveryGate.Crossing[] route)
    {
        bool underway = false;
        var table = world.Characters;
        foreach (var member in squad.Members)
        {
            if (!table.TryResolveNext(member, out int slot) || !table.Next[slot].Alive ||
                table.Cold(slot) is not { IsPlayer: false, IsImprisoned: false, Medical: { Dead: false, Incapacitated: false },
                    CarriedBy.IsNone: true } scout || scout.SquadId != squad.Id || scout.Faction != squad.Faction) continue;
            var at = table.Next[slot].Position;
            for (int i = route.Length - 1; i >= 0; i--)
            {
                var crossing = route[i];
                if (crossing.OnNearSide(at)) continue;
                if (!scout.InCombat)
                    PrisonerHandover.WalkTo(world, slot, crossing.ExitToward(world, at));
                underway = true;
                break;
            }
        }
        return underway;
    }

    internal enum LockAttempt : byte { Picked, Approaching, BlockedApproach, NoWorkSpot, Working, Unable }

    // Approach the near face of the closed leaf. Sight is tested to that face, so an intact gate can occlude the courtyard.
    internal static LockAttempt PickDoor(World world, LockSystem locks, int slot, Fixture door)
    {
        var from = world.Characters.Next[slot].Position;
        if (workSpot?.Invoke(null, [world, door, Couriers.Flat(from)]) is not ValueTuple<Vector2, float> work ||
            world.Fixtures.DoorSpotOf(door) is not { } leaf) return LockAttempt.NoWorkSpot;
        var at = new Vector3(work.Item1.X, leaf.Foot, work.Item1.Y);
        if (!AtWork(world, from, at, work.Item2)) return Approach(world, slot, at);

        var result = Pick(world, locks, slot, door);
        if (result == LockAttempt.Picked && FixtureRegistry.OpenDoor(door)) world.Fixtures.Touch(door);
        return result;
    }

    internal static LockAttempt PickCage(World world, LockSystem locks, int slot, Fixture cage)
    {
        var from = world.Characters.Next[slot].Position;
        return PrisonerHandover.AtExit(world, slot, cage) && AtWork(world, from, cage.Position, cage.UseRange + LockSystem.Reach)
            ? Pick(world, locks, slot, cage) : LockAttempt.NoWorkSpot;
    }

    internal static LockAttempt Approach(World world, int slot, Vector3 at)
    {
        var table = world.Characters;
        bool failed = table.Cold(slot)?.WalkEnd == WalkEnd.Failed && table.Next[slot].Goal == Couriers.Flat(at);
        PrisonerHandover.WalkTo(world, slot, at);
        return failed || table.Cold(slot)?.WalkEnd == WalkEnd.Failed ? LockAttempt.BlockedApproach : LockAttempt.Approaching;
    }

    static bool AtWork(World world, Vector3 from, Vector3 at, float reach) =>
        MathF.Abs(from.Y - at.Y) <= 20 && Vector3.DistanceSquared(from, at) <= reach * reach &&
        PrisonerHandover.ClearSight(world, from, at);

    static LockAttempt Pick(World world, LockSystem locks, int slot, Fixture fixture)
    {
        var table = world.Characters;
        if (fixture.Lock is not { EffectivelyLocked: true }) return LockAttempt.Picked;
        if (table.Cold(slot) is not { } picker || picker.StaggeredUntil > world.Tick)
            return LockAttempt.Working;
        if (TaskStart.OnAiWalk(table, slot)) TaskStart.Stand(table, slot);
        // Work advances once per cadence from one scout. Native lock rolls, XP and pending crime remain authoritative.
        return locks.Picking.Step(world, slot, fixture, world.TickSeconds * Cadence, locks.Crimes) switch
        {
            PickOutcome.Success or PickOutcome.Open => LockAttempt.Picked,
            PickOutcome.NotAllowed => LockAttempt.Unable,
            _ => LockAttempt.Working,
        };
    }
}
