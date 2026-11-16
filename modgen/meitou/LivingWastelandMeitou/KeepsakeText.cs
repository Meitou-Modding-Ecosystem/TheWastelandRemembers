using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingWasteland.MeitouMod;

/// <summary>
/// The Keepsake of the Fallen written for the one it belonged to: what it is, their life with the squad, how they fell, and their record,
/// from the squad record at the moment of death. The same record always gives the same text. No engine types: unit tested.
/// </summary>
public static class KeepsakeText
{
    static readonly string[] Things =
    [
        "a leather cord worn smooth at the knot",
        "a dented water flask with initials scratched into the cap",
        "a bone charm, whittled badly and carried for years",
        "a strip torn from an old squad banner",
        "a cracked hand mirror, still in its cloth",
        "a stub of pencil wrapped in a folded note",
        "a ring hammered out of a scrap of armour plate",
        "a lucky coin, rubbed nearly blank",
    ];

    /// <summary>The item's name and description. <paramref name="display"/> turns record keys into the names the player sees.</summary>
    public static (string Name, string Description) For(Fallen f, MemberRecord? m, string mourner, Func<string, string> display)
    {
        string who = display(f.Name);
        int pick = (int)((uint)SquadRecord.StableHash(f.Name) % (uint)Things.Length);
        var parts = new List<string>
        {
            $"{Cap(Things[pick])}. It belonged to {who}; {mourner} has kept it since.",
            Life(f, m, who),
            Death(f, display),
        };
        if (Record(f, m) is { Length: > 0 } rec) parts.Add(rec);
        parts.Add("Worthless to anyone else. Worn in the neck slot.");
        return ($"Keepsake of {who}", string.Join("\n\n", parts));
    }

    static string Life(Fallen f, MemberRecord? m, string who)
    {
        int days = Math.Max(1, (int)Math.Round(f.DaysServed));
        var s = new List<string> { days < 3 ? $"{who} was with the squad only {days} day{(days == 1 ? "" : "s")}." : $"{who} was with the squad {days} days." };
        if (f.Titles.Count > 0) s.Add($"Known as {Join(f.Titles)}.");
        if (m is not null)
        {
            if (m.Named.Count > 0) s.Add($"Killed {Join(m.Named.Take(3).ToList())}.");
            if (m.KillsBy.Count > 0 && f.Kills >= 5)
            {
                var top = m.KillsBy.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).First();
                s.Add(top.Value * 2 >= f.Kills ? $"Most of their kills were {top.Key}." : $"Fought everyone, the {top.Key} most of all.");
            }
            if (m.LastStands > 0) s.Add(m.LastStands == 1 ? "Once stood alone when the rest were down." : $"Stood alone {m.LastStands} times when the rest were down.");
            if (m.LimbsLost > 0) s.Add(m.LimbsLost == 1 ? "Lost a limb and kept going." : $"Lost {m.LimbsLost} limbs and kept going.");
            if (m.TimesDowned >= 5) s.Add($"Knocked down {m.TimesDowned} times, and got up every time but the last.");
        }
        return string.Join(" ", s);
    }

    static string Death(Fallen f, Func<string, string> display)
    {
        int day = (int)(f.Hours / 24) + 1;
        string how = f.Killer is null ? $"Died on day {day} at {f.Place}." :
            $"Fell on day {day} at {f.Place}, killed by {f.Killer}{(string.IsNullOrEmpty(f.KillerFaction) ? "" : " of " + Faction(f.KillerFaction))}.";
        return how + (f.AvengedBy is null ? " Not yet avenged." : $" Avenged by {display(f.AvengedBy)}.");
    }

    static string Record(Fallen f, MemberRecord? m)
    {
        var r = new List<string> { $"{f.Kills} kill{(f.Kills == 1 ? "" : "s")}" };
        if (m is not null)
        {
            if (m.Fights > 0) r.Add($"{m.FightsWon} of {m.Fights} fights won");
            if (m.Knockouts > 0) r.Add($"{m.Knockouts} knocked out");
            if (m.LimbsCut > 0) r.Add($"{m.LimbsCut} limbs taken");
            if (m.Distance >= 10000) r.Add($"{m.Distance / 10000:0} km walked");
        }
        return "Their record: " + string.Join(", ", r) + ".";
    }

    static string Join(IReadOnlyList<string> xs) => xs.Count switch
    {
        0 => "",
        1 => xs[0],
        _ => string.Join(", ", xs.Take(xs.Count - 1)) + " and " + xs[^1],
    };

    /// <summary>"the Dust Bandits", "the Holy Nation", "Kral's Chosen" (a possessive name takes no article).</summary>
    public static string Faction(string f) =>
        f.StartsWith("The ", StringComparison.Ordinal) ? "the " + f[4..] : f.Contains("'s ", StringComparison.Ordinal) ? f : "the " + f;

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
