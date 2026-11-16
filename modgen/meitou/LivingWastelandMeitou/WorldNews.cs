// News the helper answers for the rumour lines (Meitou only): what each living named outlaw is up to, and which factions are at war.
//
// Outlaws: every outlaw is always doing one thing (1 raiding, 2 lying low, 3 gathering men, 4 licking wounds), picked from the world's seed,
// the outlaw and the current stretch of days, so it changes every few game days, the same in every run of the same world, and needs no save.
// The rumour lines check it with a condition code (value = the activity) and also need the outlaw's "killed by player" world state false.
// Wars: Meitou's faction campaigns (PopulationSystem.Wars, phase 0 marching, 1 assault, 2 ended): a condition per faction is true while that
// faction runs a campaign against a town (an NPC town or one of the player's), so townsfolk can talk about it.
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

/// <summary>The engine part: answers the war conditions from the world's population system.</summary>
public static class WarNews
{
    /// <summary>Whether <paramref name="faction"/> (by name) runs a campaign that is not over, against an NPC town (player: false) or one of the
    /// player's towns (player: true).</summary>
    public static bool AtWar(PopulationSystem? population, PopulationData? data, string faction, bool player)
    {
        if (population is null || data is null) return false;
        foreach (var war in population.Wars)
        {
            if (war.Faction < 0 || war.Faction >= data.Factions.Count || data.Factions[war.Faction].Name != faction) continue;
            foreach (var c in war.Actives)
            {
                if (c.Phase >= 2) continue;
                if (player ? c.Target.PlayerTown > 0 : c.Target.Site >= 0) return true;
            }
        }
        return false;
    }

    /// <summary>Whether <paramref name="faction"/> runs a campaign that is not over against the NPC town named <paramref name="town"/>.</summary>
    public static bool AttackedBy(PopulationSystem? population, PopulationData? data, string faction, string town)
    {
        if (population is null || data is null) return false;
        foreach (var war in population.Wars)
        {
            if (war.Faction < 0 || war.Faction >= data.Factions.Count || data.Factions[war.Faction].Name != faction) continue;
            foreach (var c in war.Actives)
            {
                int site = c.Target.Site;
                if (c.Phase < 2 && site >= 0 && site < data.Sites.Count
                    && string.Equals(TownNames.Clean(data.Sites[site].Town.Name), town, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }
}
