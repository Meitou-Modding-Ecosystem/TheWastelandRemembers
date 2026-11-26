using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

internal static class LeaderStories
{
    sealed record Leader(string Name, string Faction, string[] Seats);
    static readonly Dictionary<string, Leader> leaders = new(StringComparer.Ordinal)
    {
        ["17225-Dialogue.mod"] = new("Holy Lord Phoenix", "The Holy Nation", ["Blister Hill"]),
        [PrisonerCases.Esata] = new("Esata the Stone Golem", "Shek Kingdom", ["Admag", "Squin"]),
        ["51751-Dialogue.mod"] = new("Emperor Tengu", "United Cities", ["Heft"]),
        ["59030-Dialogue.mod"] = new("Tinfist", "Anti-Slavers", ["Spring"]),
        ["56365-Dialogue.mod"] = new("Longen", "Traders Guild", ["Trader's Edge", "Traders Edge", "Heng"]),
        ["59164-Dialogue.mod"] = new("High Inquisitor Seta", "The Holy Nation", ["Stack", "Bad Teeth"]),
        ["59165-Dialogue.mod"] = new("High Inquisitor Valtena", "The Holy Nation", ["Okran's Shield"]),
        ["51333-rebirth.mod"] = new("the Bugmaster", "", ["Throne of the Bugmaster"]),
    };

    internal static bool Tracks(string? record) => record is not null && leaders.ContainsKey(record);
    static string Key(string record, bool captured) => $"@leader {record} {(captured ? "captured" : "killed")}";
    static string Line(Leader leader, bool captured) => $"The squad {(captured ? "captured" : "killed")} {leader.Name}.";

    internal static void Confirm(World world, LwDeeds deeds, string? record, bool captured, Vector3? where = null)
    {
        if (!LwMod.Settings.FallenLeaderStories || record is null || !leaders.TryGetValue(record, out var leader)) return;
        string key = Key(record, captured);
        if (deeds.Record.HasCrewStat(key)) return;
        _ = deeds.Record.SetCrewStat(key, world.GameHours);
        var site = deeds.Data?.Sites.FirstOrDefault(s => leader.Seats.Contains(s.Town.Name, StringComparer.Ordinal));
        site ??= where is { } at && deeds.Data?.TownAt(at) is >= 0 and var index ? deeds.Data.Sites[index] : null;
        string place = site?.Town.Name ?? "the wilds";
        string text = site is null ? Line(leader, captured) :
            $"Word reached {place} that the squad {(captured ? "captured" : "killed")} {leader.Name}.";
        deeds.Record.Note(text, world.GameHours, place);
        if (PlaytestLog.Enabled) PlaytestLog.Event(world, "fallen leader", record, captured ? "captured" : "killed", text);

        if (site is not null)
            (NewsSystem.For(world) ?? world.Systems.OfType<NewsSystem>().FirstOrDefault())?.ReportLeader(world, site, record, captured,
                leader.Faction, Line(leader, captured));
    }

    internal static IEnumerable<string> FactionLines(PopulationData? data, SquadRecord record, string faction)
    {
        foreach (var (id, leader) in leaders)
        {
            bool captured = record.HasCrewStat(Key(id, true)), killed = record.HasCrewStat(Key(id, false));
            if (!captured && !killed) continue;
            if (leader.Faction != faction && !CurrentSeatHeldBy(data, leader, faction)) continue;
            if (captured) yield return Line(leader, captured: true);
            if (killed) yield return Line(leader, captured: false);
        }
    }

    static bool CurrentSeatHeldBy(PopulationData? data, Leader leader, string faction)
    {
        if (data is null) return false;
        for (int i = 0; i < data.Sites.Count && i < data.SiteFactions.Count; i++)
        {
            int owner = data.SiteFactions[i];
            if (owner < 0 || owner >= data.Factions.Count || data.Factions[owner].Name != faction) continue;
            if (leader.Seats.Contains(data.Sites[i].Town.Name, StringComparer.Ordinal)) return true;
        }
        return false;
    }

    internal static string Reply(PopulationData? data, CharacterCold speaker, Vector3 at, NewsItem report, string otherwise)
    {
        if (data is null || !TryLeader(report, out var leader) || data.TownAt(at) is not (>= 0 and var town) ||
            town >= data.SiteFactions.Count || data.SiteFactions[town] != speaker.Faction ||
            !leader.Seats.Contains(data.Sites[town].Town.Name, StringComparer.Ordinal)) return otherwise;
        var previous = data.Factions.Select((faction, index) => (faction.Name, index))
            .Where(faction => faction.Name == leader.Faction).Select(faction => faction.index).DefaultIfEmpty(-1).First();
        if (previous < 0) return otherwise;
        if (speaker.Faction == previous) return $"{otherwise} We remember who did it.";
        float relations = data.Relations.Get(speaker.Faction, previous);
        if (relations <= -50) return $"{otherwise} People here won't forget what your squad did for us.";
        if (relations >= 50) return $"{otherwise} Don't expect thanks for it here.";
        return otherwise;
    }

    const string KindPrefix = "leader:";
    internal static string Kind(string record, bool captured) => KindPrefix + record + (captured ? ":taken" : ":dead");

    static bool TryLeader(NewsItem report, out Leader leader)
    {
        leader = null!;
        if (report.Source != NewsBoard.LeaderSource || !report.Kind.StartsWith(KindPrefix, StringComparison.Ordinal)) return false;
        int split = report.Kind.LastIndexOf(':');
        if (split <= KindPrefix.Length) return false;
        string id = report.Kind[KindPrefix.Length..split];
        // News kinds are normalised to lower case by the board.
        foreach (var (key, match) in leaders)
            if (string.Equals(key, id, StringComparison.OrdinalIgnoreCase)) { leader = match; return true; }
        return false;
    }
}
