// What townsfolk have told the squad unprompted, shared by the gossip gate and "Ask for news" (seen in play, bv: one Starving Vagrant ran
// through every local rumour, then, asked for news, said "Haven't heard a thing"). Each speaker tells at most MaxPerDay rumours a game day;
// asked for news with nothing new on the board, someone repeats what they told the squad lately, or what the town has been saying.
// Engine-free (keys are strings): unit tested. Not saved: a load starts afresh.
namespace LivingWasteland.MeitouMod;

public enum RumourTopic { Any, Road, Civic }

public sealed class GossipMemory
{
    public const int MaxPerDay = 2;
    public const double OwnLineHours = 12, TownLineHours = 48;
    const int TownKeeps = 8;

    readonly Dictionary<string, List<(string Text, double Hours)>> bySpeaker = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<(string Text, double Hours, RumourTopic Topic)>> byTown = new(StringComparer.Ordinal);

    /// <summary>A rumour <paramref name="speaker"/> said in the squad's hearing, in <paramref name="town"/> (null on the road).</summary>
    public void Told(string speaker, string? town, string text, double hours) => Told(speaker, town, text, hours, RumourTopic.Any);

    internal void Told(string speaker, string? town, string text, double hours, RumourTopic topic)
    {
        if (!bySpeaker.TryGetValue(speaker, out var said)) bySpeaker[speaker] = said = [];
        said.RemoveAll(s => hours - s.Hours > 24);
        said.Add((text, hours));
        if (said.Count > MaxPerDay) said.RemoveAt(0);
        if (town is null) return;
        if (!byTown.TryGetValue(town, out var talk)) byTown[town] = talk = [];
        talk.RemoveAll(t => t.Text == text);
        talk.Add((text, hours, topic));
        if (talk.Count > TownKeeps) talk.RemoveAt(0);
        if (bySpeaker.Count > 400)
            foreach (var k in bySpeaker.Where(p => p.Value.All(s => hours - s.Hours > 24)).Select(p => p.Key).ToList()) bySpeaker.Remove(k);
    }

    /// <summary>Whether <paramref name="speaker"/> may tell another rumour: fewer than <see cref="MaxPerDay"/> in the last game day.</summary>
    public bool MayTell(string speaker, double hours)
    {
        if (!bySpeaker.TryGetValue(speaker, out var said) || said.Count < MaxPerDay) return true;
        int withinWindow = 0;
        foreach (var entry in said)
            if (hours - entry.Hours <= 24 && hours >= entry.Hours && ++withinWindow == MaxPerDay)
                return false;
        return true;
    }

    /// <summary>The last rumour <paramref name="speaker"/> told the squad within <see cref="OwnLineHours"/>, or null.</summary>
    public string? LastBy(string speaker, double hours) =>
        bySpeaker.TryGetValue(speaker, out var said) && said.LastOrDefault(s => hours - s.Hours <= OwnLineHours && hours >= s.Hours) is { Text: { } t } ? t : null;

    /// <summary>One of the rumours told in <paramref name="town"/> within <see cref="TownLineHours"/> (the roll picks), or null.</summary>
    public string? FromTown(string? town, double hours, ulong roll) => FromTown(town, hours, roll, RumourTopic.Any);

    internal string? FromTown(string? town, double hours, ulong roll, RumourTopic topic)
    {
        if (town is null || !byTown.TryGetValue(town, out var talk)) return null;
        var live = talk.Where(t => hours - t.Hours <= TownLineHours && hours >= t.Hours
            && (topic == RumourTopic.Any || t.Topic == topic)).ToList();
        return live.Count == 0 ? null : live[(int)(roll % (ulong)live.Count)].Text;
    }
}

