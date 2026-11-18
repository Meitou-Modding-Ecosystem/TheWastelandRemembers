namespace LivingWasteland.MeitouMod;

/// <summary>
/// What a fight's end does to a rival (no engine types, so it is unit tested). Learnt in play: a rival who is knocked down gets back up and
/// fights on, and each time the squad knocks them down again the fight "ends" again; counting every end as a return made the rival "get away
/// again" over and over, pushed their return back each time and made them stronger each time. So:
/// * only a rival who came hunting the squad (Hunting) can lose and get away; a fight while they are still licking their wounds changes nothing;
/// * a rival who beats the squad counts a win, and goes back to recovering if they were hunting (their return is not pushed back otherwise);
/// * one fight counts once: ends within <see cref="SameFight"/> game hours of the last counted one are the same fight.
/// </summary>
public static class RivalRules
{
    public const double SameFight = 12;

    public enum Outcome { None, GotAway, BeatSquad }

    public static Outcome OnFightEnd(bool hunting, bool won, double now, double lastCounted)
    {
        if (now - lastCounted < SameFight) return Outcome.None;
        if (won) return hunting ? Outcome.GotAway : Outcome.None;
        return Outcome.BeatSquad;
    }
}

/// <summary>
/// Characters other mods have named and asked TWR to leave alone (RivalryCore #25): never made a rival, never renamed. Matched by name and
/// faction (generated names repeat across mods), case-insensitively; an empty faction covers that name in any faction. Saved with the rivals.
/// </summary>
public sealed class LeftAlone
{
    readonly List<(string Name, string Faction)> entries = [];

    public IReadOnlyList<(string Name, string Faction)> All => entries;

    /// <summary>Adds a character; false for an empty name or one already covered.</summary>
    public bool Add(string name, string faction)
    {
        name = (name ?? "").Trim();
        faction = (faction ?? "").Trim();
        if (name.Length == 0 || entries.Any(e => Same(e.Name, name) && Same(e.Faction, faction))) return false;
        entries.Add((name, faction));
        return true;
    }

    /// <summary>Whether a character of this name and faction is to be left alone.</summary>
    public bool Covers(string name, string faction) =>
        entries.Any(e => Same(e.Name, (name ?? "").Trim()) && (e.Faction.Length == 0 || Same(e.Faction, (faction ?? "").Trim())));

    public void Clear() => entries.Clear();

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
