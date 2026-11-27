// The saved chronicle's text (StatsPanel.ChronicleHtml), engine-free so the unit tests cover it. Seen in the bv play test: History ran newest
// first, the Titles chapter listed all 72 titles not yet earned around the 3 that were, and the heading said only "Squad chronicle".
using System.Text.RegularExpressions;

namespace LivingWasteland.MeitouMod;

public static class ChronicleRules
{
    /// <summary>History oldest first (the window shows it newest first), with a line repeated back to back folded into one.</summary>
    public static string Forwards(string history)
    {
        var lines = history.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Reverse().ToList();
        var folded = new List<string>();
        for (int i = 0; i < lines.Count;)
        {
            int n = 1;
            while (i + n < lines.Count && lines[i + n] == lines[i]) n++;
            folded.Add(n == 1 ? lines[i] : $"{lines[i]} ({(n == 2 ? "twice" : $"{n} times")})");
            i += n;
        }
        return folded.Count == 0 ? history : string.Join("\n", folded);
    }

    /// <summary>The title catalogue cut to what was earned: the count line, then each section's earned titles ("* " lines) under its
    /// heading; sections with none are left out.</summary>
    public static string EarnedTitles(string catalog)
    {
        var lines = catalog.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var keep = new List<string>();
        string? section = null;
        int earned = 0;
        foreach (var line in lines)
        {
            if (keep.Count == 0 && line.StartsWith("Titles:", StringComparison.Ordinal)) { keep.Add(line); continue; }
            if (line.StartsWith("-- ", StringComparison.Ordinal) && line.EndsWith(" --", StringComparison.Ordinal)) { section = line; continue; }
            if (!line.StartsWith("* ", StringComparison.Ordinal)) continue;
            if (section is not null) { keep.Add(""); keep.Add(section); section = null; }
            keep.Add(line[2..]);
            earned++;
        }
        if (earned == 0) return keep.Count > 0 ? keep[0] + "\n\nNo titles earned yet." : "No titles earned yet.";
        return string.Join("\n", keep);
    }

    static readonly Regex Day = new(@"^Day (\d+),", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    static readonly Regex KnownAs = new(@"^Known as: (.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>"The Grave-Fillers: days 1 to 3", or "Squad chronicle: day 1" for a squad with no name yet.</summary>
    public static string Heading(string panel, string history)
    {
        string name = KnownAs.Match(panel ?? "") is { Success: true } m && m.Groups[1].Value.Trim() is { Length: > 0 } n
            && !n.StartsWith("no name", StringComparison.OrdinalIgnoreCase) ? n : "Squad chronicle";
        var days = Day.Matches(history ?? "").Select(d => int.Parse(d.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        if (days.Count == 0) return name;
        int first = days.Min(), last = days.Max();
        return first == last ? $"{name}: day {first}" : $"{name}: days {first} to {last}";
    }
}
