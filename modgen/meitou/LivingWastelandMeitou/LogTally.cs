using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingWasteland.MeitouMod;

/// <summary>
/// Counts what our systems did, and writes one line a game day ("day 3: kills 12, atlas heard 4, ..."), so a play log reads at a glance
/// (yesterday's log reading needed a grep per system). Counts by the start of each logged line. No engine types: unit tested.
/// </summary>
public static class LogTally
{
    // label, line start, and (optional) a part the line must hold
    static readonly (string Label, string Start, string? Has)[] Kinds =
    [
        ("kills", "kill:", null), ("atlas heard", "atlas: heard", null), ("crier spoke", "crier: called", null),
        ("crier silent", "crier: had nothing", null), ("victory lines", "victory line:", "spoke"), ("rivals", "rivals:", null),
        ("bounty crews", "bounty hunters:", null), ("reprisals", "outlaw reprisal:", null), ("successors", "outlaw successors:", null),
        ("heat", "heat:", null), ("memorials", "memorial:", null), ("outlaw grace", "outlaw grace:", null), ("news", "news:", "reports"),
    ];

    static readonly Dictionary<string, int> counts = [];
    static int day = -1;

    public static void Count(string line)
    {
        foreach (var (label, start, has) in Kinds)
            if (line.StartsWith(start, StringComparison.Ordinal) && (has is null || line.Contains(has, StringComparison.Ordinal)))
            {
                Add(label);
                return;
            }
    }

    public static void Add(string label, int n = 1) => counts[label] = counts.GetValueOrDefault(label) + n;

    /// <summary>The day's line when a new game day has begun (null otherwise); counting starts again for the new day.</summary>
    public static string? Day(double gameHours)
    {
        int now = (int)(gameHours / 24);
        if (day < 0 || now < day) { day = now; counts.Clear(); return null; }   // first look, or an earlier save loaded
        if (now == day) return null;
        string line = $"summary of day {day}: " + (counts.Count == 0 ? "nothing logged"
            : string.Join(", ", Kinds.Select(k => k.Label).Concat(counts.Keys.Except(Kinds.Select(k => k.Label)))
                .Where(counts.ContainsKey).Select(l => $"{l} {counts[l]}")));
        day = now;
        counts.Clear();
        return line;
    }
}
