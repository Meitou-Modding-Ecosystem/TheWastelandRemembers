using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Factions;

namespace LivingWasteland.MeitouMod;

/// <summary>The siege sends a few fighters to the gate and keeps the remainder available to intercept defenders.</summary>
internal sealed class PrisonerSiege
{
    const float DefenderReach = 500;
    readonly Dictionary<CharacterId, CharacterId> orders = [];

    internal static bool Fit(World world, Squad squad, CharacterId member, out int slot) =>
        world.Characters.TryResolveNext(member, out slot) && world.Characters.Next[slot] is { Alive: true, Dormant: false } &&
        world.Characters.Cold(slot) is { IsPlayer: false, IsImprisoned: false, Medical: { Dead: false, Incapacitated: false },
            CarriedBy.IsNone: true } actor && actor.Faction == squad.Faction && actor.SquadId == squad.Id;

    internal static HashSet<int> Bashers(World world, Squad squad)
    {
        var ready = new List<int>();
        foreach (var member in squad.Members)
            if (Fit(world, squad, member, out int slot) && world.Characters.Cold(slot) is { InCombat: false }) ready.Add(slot);
        return ready.Take(ready.Count >= 4 ? 2 : 1).ToHashSet();
    }

    internal static bool HeavyLosses(World world, Squad squad, int original) =>
        original > 1 && 2 * squad.Members.Count(m => Fit(world, squad, m, out _)) <= original;

    internal bool Cover(World world, int slot)
    {
        var table = world.Characters;
        if (world.Systems.OfType<AiSystem>().FirstOrDefault() is not { } ai || table.Cold(slot)?.Ai is not { } mind)
            return false;
        var fighter = table.IdOf(slot);
        if (!AiCompat.CanAssault) return false;                     // API 3 has no per-character assault order: no cover fights
        var forced = AiCompat.ForcedAssault(mind);
        if (!forced.IsNone)
            return orders.GetValueOrDefault(fighter, CharacterId.None) == forced;

        var at = table.Next[slot].Position;
        int best = -1;
        float nearest = DefenderReach * DefenderReach;
        for (int i = 0; i < table.HighWater; i++)
        {
            var enemy = table.Next[i];
            if (!enemy.Alive || enemy.Dormant || table.Cold(i) is not
                { IsPlayer: true, IsImprisoned: false, Medical: { Dead: false, Incapacitated: false }, CarriedBy.IsNone: true }) continue;
            float distance = Vector3.DistanceSquared(at, enemy.Position);
            if (distance >= nearest || !PrisonerHandover.ClearSight(world, at, enemy.Position)) continue;
            best = i;
            nearest = distance;
        }

        if (best < 0 || !AiCompat.OrderAssault(ai, world, fighter, table.IdOf(best))) return false;
        orders[fighter] = table.IdOf(best);
        return true;
    }

    internal void Clear(World world, Squad squad)
    {
        RecoveryGate.ClearBash(world, squad);
        var table = world.Characters;
        foreach (var (fighter, target) in orders)
            if (table.TryResolveNext(fighter, out int slot) && table.Cold(slot) is
                { IsPlayer: false, Ai: { } mind } actor && actor.SquadId == squad.Id && AiCompat.ForcedAssault(mind) == target)
                AiCompat.ClearForcedAssault(mind);
        orders.Clear();
    }

    internal void Forget() => orders.Clear();

    internal static bool FallBack(World world, Squad squad, Vector3 rally)
    {
        var table = world.Characters;
        bool going = false;
        foreach (var person in squad.Members)
        {
            if (!Fit(world, squad, person, out int slot) ||
                Vector3.DistanceSquared(table.Next[slot].Position, rally) <= 300 * 300) continue;
            if (table.Cold(slot) is { InCombat: false }) PrisonerHandover.WalkTo(world, slot, rally);
            going = true;
        }
        return going;
    }
}
