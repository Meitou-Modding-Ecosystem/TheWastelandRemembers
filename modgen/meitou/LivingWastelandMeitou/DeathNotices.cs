// The engine's death notice (Meitou 0.22.2+, ICharacterDeathObserver: every death, whatever killed it, with its cause, where it happened
// and the killer of a killing blow). For a squad member it tells the squad record how they died ("Bled out", "Executed", "Starved"), which
// the Fallen page, the epitaph and the keepsake then say instead of "Cause of death unknown", and names the killer of a blow the hit feed
// did not see (a bolt, an execution). Built by reflection (ObserverEmit, as PrisonerCases' watch) so the 0.17.3 build still loads; older
// clients have no notice and keep what the hit feed sees.
using System.Numerics;
using System.Reflection;
using Meitou.Data.Gameplay;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace LivingWasteland.MeitouMod;

public static class DeathNotices
{
    static readonly Type? observer = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.ICharacterDeathObserver");
    static readonly Type? payload = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.CharacterDeath");
    static readonly PropertyInfo? victimOf = payload?.GetProperty("Victim"), killerOf = payload?.GetProperty("Killer"),
        killerFactionOf = payload?.GetProperty("KillerFaction"), recordOf = payload?.GetProperty("Record"),
        platoonOf = payload?.GetProperty("Platoon"), causeOf = payload?.GetProperty("Cause"), siteOf = payload?.GetProperty("Site");
    static readonly object? offscreenFight = siteOf?.PropertyType is { IsEnum: true } kind &&
        Enum.TryParse(kind, "OffScreenFight", out object? value) ? value : null;

    /// <summary>Whether this client has the notice in the shape this mod reads.</summary>
    internal static bool Available => observer is not null && victimOf?.PropertyType == typeof(CharacterId) &&
        killerOf?.PropertyType == typeof(CharacterId) && killerFactionOf?.PropertyType == typeof(int) && causeOf is not null && siteOf is not null;

    /// <summary>The emitted observer system's type, or null on a client without the notice.</summary>
    internal static Type? BuildWatch() => Available
        ? ObserverEmit.Build(observer!, "Died", typeof(DeathNotices).GetMethod(nameof(Died))!, "SquadDeathWatch") : null;

    internal static void Register(ModContext c)
    {
        if (BuildWatch() is not { } type)
        {
            c.Log("death notices: this Meitou has none (0.22.2 and later do); the Fallen keep what the squad saw");
            return;
        }
        c.AddSystem("lw-death-notices", _ => (ITickSystem)Activator.CreateInstance(type)!, SystemAnchor.End);
        c.Log("death notices: the Fallen say how they died");
    }

    /// <summary>Passes a death to the squad record or the systems tracking a confirmed outside kill.</summary>
    public static void Died(World world, object death)
    {
        try
        {
            if (victimOf!.GetValue(death) is not CharacterId victim || LwDeeds.For(world) is not { } deeds) return;
            object? site = siteOf!.GetValue(death);
            if (offscreenFight is not null && offscreenFight.Equals(site))
            {
                OffscreenDeath(world, death, deeds);
                return;
            }
            var table = world.Characters;
            if (!table.TryResolveNext(victim, out int v) || table.Cold(v) is not { } cold
                || OtherModsPeople.Runs(world, cold)) return;
            if (cold.Animal is not null)
            {
                if (cold.IsPlayer && LwMod.Settings.AnimalCompanions)
                    CompanionMemorials.Died(world, deeds.Record, cold, deeds.TownAt(table.Next[v].Position).Town);
                return;
            }
            if (!cold.IsPlayer)
            {
                if (deeds.Kindness.People.Count > 0)
                    KindnessWatch.Death(world, deeds, victim, cold, killerFactionOf!.GetValue(death) is int factionOfKiller ? factionOfKiller : -1);
                if (killerOf!.GetValue(death) is CharacterId other && !other.IsNone && !ArenaLink.Sport(other, victim) &&
                    table.TryResolveNext(other, out int attackerSlot) && table.Cold(attackerSlot) is { IsPlayer: false } attacker &&
                    !LwDeedsSystem.IsHired(world, attacker))
                {
                    deeds.Tracker?.OutsideDeath(victim, world.Uniques.RecordOf(victim));
                    int otherFaction = killerFactionOf!.GetValue(death) is int reported && reported >= 0 ? reported : attacker.Faction;
                    RivalsSystem.For(world)?.OutsideDeath(world, victim, cold, table.Next[v].Position, otherFaction);
                }
                return;
            }
            string? cause = SquadRecord.CausePhrase(causeOf!.GetValue(death)?.ToString() ?? "", site?.ToString() ?? "");
            string? killer = null, faction = null, record = null;
            if (killerOf!.GetValue(death) is CharacterId k && !k.IsNone && !ArenaLink.Sport(k, victim)
                && table.TryResolveNext(k, out int ks) && table.Cold(ks) is { IsPlayer: false } by)
            {
                killer = by.Name;
                faction = deeds.FactionName(killerFactionOf!.GetValue(death) is int f && f >= 0 ? f : by.Faction);
                record = world.Uniques.RecordOf(k);
            }
            if (cause is null && killer is null) return;
            deeds.Record.DeathNoted(deeds.Who(cold), cause, killer, faction, record);
            if (LwMod.Settings.LogChanges) LwMod.Log($"death notice: {cold.Name}: {cause ?? "a blow"}{(killer is null ? "" : $", by {killer}")}");
        }
        catch (Exception e) { LwMod.Log($"death notices: {e.GetBaseException().Message}"); }
    }

    static void OffscreenDeath(World world, object death, LwDeeds deeds)
    {
        // The winner's faction is explicit, but an unloaded actor has no handle. Only a unique record identifies an individual here.
        if (killerFactionOf!.GetValue(death) is not int killerFaction || killerFaction < 0 || killerFaction == world.Player.Faction ||
            recordOf?.GetValue(death) is not string { Length: > 0 } record || !UniqueCharacters.IsUnique(deeds.Data?.Db.Find(record))) return;

        deeds.Tracker?.OutsideDeath(CharacterId.None, record);
        Vector3? at = platoonOf?.GetValue(death) is int id && id >= 0 && world.Platoons.Find(id) is { } party
            ? new Vector3(party.Position.X, 0, party.Position.Y) : null;
        OutlawCampsSystem.For(world)?.OutsideDeath(world, record, deeds, killerFaction, at);
    }
}
