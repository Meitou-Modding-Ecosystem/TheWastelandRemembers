// The pure part of three features built on hooks Meitou added in v0.20.2-nightly.20261117 (the engine part is NightlyHooks.cs): marks on
// the world map, "Ask for news" in a townsperson's right-click menu, and History lines when a faction changes its mind about the squad.
// No engine types: unit tested.
namespace LivingWasteland.MeitouMod;

public enum MarkKind { Rival, Outlaw, News }

/// <summary>One mark on the world map: a kind, a world point, a label (shown from map zoom 1) and a tooltip (rows split by new lines, a
/// tab between a row's label and value), and the least zoom level it shows at (0 the whole map, 2 the closest).</summary>
public sealed record MapMark(MarkKind Kind, float X, float Z, string Label, string Tooltip, int FromLevel);

/// <summary>What the squad knows, as map marks. Only what it has been told: never a rival or a camp the Atlas doesn't know.</summary>
public static class MapMarkRules
{
    public const int MaxOutlaws = 12, MaxNews = 8;

    /// <summary>The marker image group of the game's map marker set, and a tint (red, green, blue), per kind.</summary>
    public static (string Group, float R, float G, float B) Look(MarkKind kind) => kind switch
    {
        MarkKind.Rival => ("Character", 0.95f, 0.55f, 0.15f),
        MarkKind.Outlaw => ("Camp", 0.85f, 0.2f, 0.2f),
        _ => ("NewDiscovered", 0.95f, 0.85f, 0.35f),
    };

    /// <summary>
    /// Rivals still about (recovering or hunting) at their last known place; outlaw camps the squad has heard of, with what the outlaw is up
    /// to; places of news the squad heard within the last <see cref="NewsBoard.LiveDays"/> days, newest first.
    /// </summary>
    public static List<MapMark> Build(IEnumerable<(string Name, string Phase, float X, float Z, string Grudge)> rivals,
        IEnumerable<(string Name, string Doing, float X, float Z)> outlaws,
        IEnumerable<(string Text, string Place, float X, float Z, double HeardHours)> news, double hoursNow)
    {
        var marks = new List<MapMark>();
        foreach (var r in rivals)
            if (r.Phase is "recovering" or "hunting")
                marks.Add(new MapMark(MarkKind.Rival, r.X, r.Z, r.Name,
                    $"\tRival, {(r.Phase == "hunting" ? "hunting your squad" : "licking their wounds")}\n\t{Cap(r.Grudge)}", 0));
        foreach (var o in outlaws.Take(MaxOutlaws))
            marks.Add(new MapMark(MarkKind.Outlaw, o.X, o.Z, o.Name, $"\tOutlaw's camp, {o.Doing}", 1));
        foreach (var n in news.Where(n => hoursNow - n.HeardHours <= NewsBoard.LiveDays * 24 && hoursNow >= n.HeardHours - 1)
                     .OrderByDescending(n => n.HeardHours).Take(MaxNews))
            marks.Add(new MapMark(MarkKind.News, n.X, n.Z, "", $"\tNews from {n.Place}, day {(int)(n.HeardHours / 24) + 1}\n\t{n.Text}", 1));
        return marks;
    }

    // A tooltip row is "label<tab>value": the engine sets the value against the label with no gap, so they ran together (seen in play:
    // "NewsKnife defeated...", then "Rivalhunting your squad"). Every row is now a value alone, with no label.
    static string Cap(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    /// <summary>What an outlaw is up to, as the camp's tooltip says it (OutlawNews moods).</summary>
    public static string Doing(int mood) => mood switch
    {
        OutlawNews.Raiding => "raiding",
        OutlawNews.LyingLow => "lying low",
        OutlawNews.Gathering => "gathering men",
        OutlawNews.Wounded => "licking their wounds",
        _ => "about",
    };
}

/// <summary>"Ask for news": who can be asked, and what they say.</summary>
public static class NewsAskRules
{
    /// <summary>World units (0.1 m) from a town's centre within which its people can be asked: about 400 m.</summary>
    public const float AskRange = 4000;

    public const string Caption = "Ask for news";

    // Preserve the groups that faction_voices.json intentionally leaves without TWR lines.
    internal static bool NoConversationNews(string faction) => faction is "Thrall Masters" or "Skeleton Legion" or "Second Empire Exile"
        or "Fishmen" or "Skimmers" or "Okranites" or "Medics Guild" or "Cannibals" or "Fogmen";

    static readonly string[] Leads = ["Heard this: ", "Word is, ", "They're saying ", "Not long ago: "];
    static readonly string[] Nothing = ["Nothing much, these days.", "Quiet, lately.", "Haven't heard a thing.", "Same as ever."];
    static readonly string[] AboutYou = ["You'd know better than me: ", "Everyone's on about your lot. ", "That was you, wasn't it? ",
        "Folk won't shut up about you. ", "You're the ones, aren't you? ", "Your lot's the talk of the place. "];
    static readonly string[] NothingNew = ["Nothing new.", "Nothing you haven't heard.", "Same as the last one told you.", "Ask someone else, I've nothing.",
        "Nothing since the last lot.", "You've heard all I have."];

    /// <summary>Game hours before the same piece of news is told again by someone asked: with only a piece or two about, three people in a row
    /// told the squad the same thing (seen in play). News about the squad itself waits longer.</summary>
    public const double TellAgainAfter = 6, TellOursAgainAfter = 24;

    /// <summary>Whether the menu offers the row: someone not of the squad, not fighting it, near a town.</summary>
    public static bool Offer(bool isPlayer, bool fightsPlayer, float distanceToTown) => !isPlayer && !fightsPlayer && distanceToTown <= AskRange;

    /// <summary>
    /// The answer, and which piece of news it gave (-1: none): the weightiest live piece within <paramref name="reach"/> world units of the
    /// speaker, the newest of equal weight. News that names one of <paramref name="squad"/> (the squad's own deeds, e.g. an arena win) is
    /// told only when there is nothing else, and as news about them ("Everyone's on about your lot."). <paramref name="roll"/> picks the
    /// wording.
    /// </summary>
    public static (string Line, int Index) Answer(IReadOnlyList<(string Text, float X, float Z, int Weight, double Hours)> live, float x, float z,
        float reach, ulong roll, IReadOnlyCollection<string>? squad = null, IReadOnlyList<double>? toldHours = null, double now = 0)
    {
        bool anyInReach = false;
        int best = -1;
        bool bestOurs = false;
        for (int i = 0; i < live.Count; i++)
        {
            var n = live[i];
            float dx = n.X - x, dz = n.Z - z;
            if (dx * dx + dz * dz > reach * reach) continue;
            anyInReach = true;
            bool ours = Names(n.Text, squad);
            if (toldHours is not null && i < toldHours.Count && now - toldHours[i] < (ours ? TellOursAgainAfter : TellAgainAfter)) continue;   // told lately
            bool better = best < 0 || (bestOurs && !ours)
                || (bestOurs == ours && (n.Weight > live[best].Weight || (n.Weight == live[best].Weight && n.Hours > live[best].Hours)));
            if (better) { best = i; bestOurs = ours; }
        }
        if (best < 0) return anyInReach ? (NothingNew[(int)(roll % (ulong)NothingNew.Length)], -1) : (Nothing[(int)(roll % (ulong)Nothing.Length)], -1);
        var leads = bestOurs ? AboutYou : Leads;
        return (leads[(int)(roll % (ulong)leads.Length)] + (bestOurs ? live[best].Text : Uncap(live[best].Text)), best);
    }

    /// <summary>Whether a text names one of the squad (a whole word: "Knife" in "Knife defeated...", not in "Knifeman").</summary>
    public static bool Names(string text, IReadOnlyCollection<string>? squad)
    {
        if (squad is null) return false;
        foreach (var name in squad)
        {
            if (name.Length < 2) continue;
            for (int at = text.IndexOf(name, StringComparison.Ordinal); at >= 0; at = text.IndexOf(name, at + 1, StringComparison.Ordinal))
            {
                bool start = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                int end = at + name.Length;
                if (start && (end == text.Length || !char.IsLetterOrDigit(text[end]))) return true;
            }
        }
        return false;
    }

    // "Okran's Shield has fallen" keeps its capital; "A caravan of..." after "Word is, " reads "a caravan of..."
    // "The caravan" reads "the caravan", but a name keeps its capital: "The Queen", "The Hub" (play test of bg: "Not long ago: the Queen")
    static string Uncap(string s) => s.Length > 1 && s[0] == 'A' && s[1] == ' ' ? "a" + s[1..]
        : s.Length > 4 && s.StartsWith("The ", StringComparison.Ordinal) && char.IsLower(s[4]) ? "the" + s[3..] : s;
}

/// <summary>A faction changing its mind about the squad (the engine's relation change with its cause), as a History line.</summary>
public static class RelationRules
{
    /// <summary>
    /// The line for a change that crossed one of the game's thresholds (the engine's own notice said so): worse or better, and why. Null for
    /// changes made by a debug command, or a cause we can't name.
    /// </summary>
    public static string? Line(string faction, bool worse, string causeType, string deed)
    {
        string? why = Cause(causeType, deed, worse);
        if (why is null || faction.Length == 0) return null;
        string who = Cap(KeepsakeText.Faction(faction));
        return worse ? $"{who} turned against the squad, {why}." : $"{who} warmed to the squad, {why}.";
    }

    /// <summary>Why, from the engine's cause type and deed kind (as their names).</summary>
    public static string? Cause(string causeType, string deed, bool worse) => causeType switch
    {
        "Deed" => DeedCause(deed, worse),
        "Crime" => "after a crime they saw",
        "Trade" => worse ? "over a captive sold" : "over a slave bought free",
        "Dialogue" => "after words were spoken",
        "WarDeclared" => "now that war is declared",
        "WarEnded" => "now that the war is over",
        _ => null,
    };

    static string DeedCause(string deed, bool worse)
    {
        if (deed.Contains("Kill", StringComparison.OrdinalIgnoreCase)) return worse ? "after a killing" : "after a killing they were glad of";
        if (deed.Contains("Aid", StringComparison.OrdinalIgnoreCase) || deed.Contains("Heal", StringComparison.OrdinalIgnoreCase)) return "after the squad tended their wounded";
        if (deed.Contains("Knock", StringComparison.OrdinalIgnoreCase) || deed.Contains("Hit", StringComparison.OrdinalIgnoreCase))
            return worse ? "after blows were struck" : "after blows struck at their enemies";
        return "for what the squad did";
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
