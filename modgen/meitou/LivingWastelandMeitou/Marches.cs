// Armies on the march (Living Wasteland only): Meitou's own faction campaigns (PopulationSystem.Wars, one CampaignInstance per march: phase 0
// marching, 1 at the town, 2 over), which is also what Kenshi Ultimate Overhaul's called assaults run on. Only the engine's public data is
// read; nothing is changed, and no other mod is read.
//
// * Lookout lines: a squad marching in a campaign that is not over answers the MARCH condition, so the lookout's army lines win over the
//   faction's usual call ("That's not a patrol. That's an army.").
// * Broken marches: the engine marks a campaign whose leading platoon fought within 300 of the player's people (FoughtPlayer). When such a
//   march on someone's town ends and the town is not the marcher's, the squad broke it: History, Recap and the squad's "marches_broken".
//   A march that took its town, or one that never met the squad, is not ours to tell. Each is noted once (its key in the record).
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public static class Marches
{
    static long cachedTick = long.MinValue;
    static World? cachedWorld;
    static readonly HashSet<int> marching = [];
    static readonly Dictionary<string, (int Faction, int Site, bool Reached)> watched = new(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="who"/> is in a squad marching in a campaign that is not over (the lookout's army lines).</summary>
    public static bool IsMarching(World world, PopulationSystem? population, CharacterId who)
    {
        if (population is null || !world.Characters.TryResolveNext(who, out int slot) || world.Characters.Cold(slot) is not { } c) return false;
        if (cachedTick != world.Tick || !ReferenceEquals(cachedWorld, world))
        {
            cachedTick = world.Tick;
            cachedWorld = world;
            marching.Clear();
            foreach (var war in population.Wars)
                foreach (var inst in war.Actives)
                {
                    if (inst.Phase >= 2 || inst.Target.Site < 0) continue;
                    foreach (int id in inst.Platoons)
                        if (world.Platoons.Find(id) is { } p) marching.Add(p.SquadId);
                }
        }
        return marching.Contains(c.SquadId);
    }

    /// <summary>Looks over the campaigns (every few seconds) and notes a march the squad broke.</summary>
    public static void Watch(World world, PopulationSystem? population, PopulationData? data)
    {
        if (population is null || data is null || LwDeeds.For(world) is not { } deeds) return;
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var war in population.Wars)
            foreach (var inst in war.Actives)
            {
                int site = inst.Target.Site;
                if (site < 0 || inst.Target.PlayerTown > 0) continue;               // a raid on the player's own base is another story
                string key = $"march:{inst.Faction}:{inst.Id}:{inst.CampaignId}";
                live.Add(key);
                if (inst.Phase < 2)
                {
                    if (inst.FoughtPlayer) watched[key] = (inst.Faction, site, inst.Reached);
                    continue;
                }
                if (inst.FoughtPlayer) Ended(world, data, deeds, key, inst.Faction, site, inst.Reached);
                watched.Remove(key);
            }
        foreach (var key in watched.Keys.Where(k => !live.Contains(k)).ToList())
        {
            var (f, s, r) = watched[key];                                         // gone before we saw it end: the town tells how it went
            Ended(world, data, deeds, key, f, s, r);
            watched.Remove(key);
        }
    }

    static void Ended(World world, PopulationData data, LwDeeds deeds, string key, int faction, int site, bool reached)
    {
        if (site >= data.Sites.Count || faction < 0 || faction >= data.Factions.Count) return;
        if (site < data.SiteFactions.Count && data.SiteFactions[site] == faction) return;      // it took the town
        var record = deeds.Record;
        if (!record.NewEvent(key)) return;
        string nation = data.Factions[faction].Name;
        if (nation.StartsWith("The ", StringComparison.Ordinal)) nation = "the " + nation[4..];
        string town = data.Sites[site].Town.Name;
        string text = reached ? $"The squad threw {nation} back from the walls of {town}." : $"The squad broke {nation}'s march on {town}.";
        record.Note(text);
        record.OnMoment?.Invoke(text, 75);
        foreach (var said in record.SetCrewStat("marches_broken", record.CrewStat("marches_broken") + 1)) world.Post(said);
        if (LwMod.Settings.LogChanges) LwMod.Log($"marches: {text}");
    }
}
