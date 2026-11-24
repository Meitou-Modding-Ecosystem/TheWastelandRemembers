// The engine's death notice (Meitou 0.22.2+, ICharacterDeathObserver: every death, whatever killed it, with its cause, where it happened
// and the killer of a killing blow). For a squad member it tells the squad record how they died ("Bled out", "Executed", "Starved"), which
// the Fallen page, the epitaph and the keepsake then say instead of "Cause of death unknown", and names the killer of a blow the hit feed
// did not see (a bolt, an execution). Built by reflection (ObserverEmit, as PrisonerCases' watch) so the 0.17.3 build still loads; older
// clients have no notice and keep what the hit feed sees.
using System.Reflection;
using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class DeathNotices
{
    static readonly Type? observer = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.ICharacterDeathObserver");
    static readonly Type? payload = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.CharacterDeath");
    static readonly PropertyInfo? victimOf = payload?.GetProperty("Victim"), killerOf = payload?.GetProperty("Killer"),
        killerFactionOf = payload?.GetProperty("KillerFaction"), causeOf = payload?.GetProperty("Cause"), siteOf = payload?.GetProperty("Site");

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

    /// <summary>One death (any character): passed on to the squad record when it was one of the squad's.</summary>
    public static void Died(World world, object death)
    {
        try
        {
            if (victimOf!.GetValue(death) is not CharacterId victim || LwDeeds.For(world) is not { } deeds) return;
            var table = world.Characters;
            if (!table.TryResolveNext(victim, out int v) || table.Cold(v) is not { IsPlayer: true, Animal: null } cold
                || OtherModsPeople.Runs(world, cold)) return;
            string? cause = SquadRecord.CausePhrase(causeOf!.GetValue(death)?.ToString() ?? "", siteOf!.GetValue(death)?.ToString() ?? "");
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
}
