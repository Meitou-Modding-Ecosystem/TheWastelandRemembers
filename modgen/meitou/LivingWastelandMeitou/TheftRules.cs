// Towns miss stolen goods, the pure part (no engine types; unit-tested). The engine part is Thefts.cs, fed by Meitou 0.21's goods-taken
// observer. A theft somebody saw already gives crime heat through the "I saw that" lines; this adds what the goods were worth. A theft
// nobody saw is missed some hours later, and the town's heat rises then.
namespace LivingWasteland.MeitouMod;

/// <summary>Goods taken unseen from a town, waiting to be missed: the town, its owner faction, what was taken (summed), when first taken.</summary>
public sealed record PendingTheft(string Town, string Faction, string Items, double Value, double TakenHours);

public static class TheftRules
{
    /// <summary>Game hours before an unseen theft is missed.</summary>
    public const double MissedAfter = 12;

    /// <summary>Game hours within which takes from one town and owner count as one theft (missed together, on the first take's clock).</summary>
    public const double SameTheft = 1;

    /// <summary>Goods worth less than this (cats) are not missed or counted.</summary>
    public const double MinValue = 100;

    /// <summary>Most heat one theft (or one town's missed goods) adds.</summary>
    public const double MaxHeat = 8;

    /// <summary>A missed theft worth this much (cats) goes into History.</summary>
    public const double Notable = 2000;

    /// <summary>Whether a take counts: goods of a faction that is not the player's (the player's own and ownerless goods are not theft).</summary>
    public static bool Counts(int ownerFaction, int playerFaction) => ownerFaction >= 0 && ownerFaction != playerFaction;

    /// <summary>
    /// Heat for goods worth <paramref name="value"/> cats: 1 at 100 cats, about 3.5 at 1000, 5.7 at 5000, at most <see cref="MaxHeat"/>.
    /// Seen, half of it: the crime itself already heats the town through the guards' and townsfolk's lines.
    /// </summary>
    public static double Heat(double value, bool seen)
    {
        if (value < MinValue) return 0;
        double h = Math.Min(MaxHeat, Math.Log2(value / MinValue + 1));
        return seen ? h / 2 : h;
    }

    /// <summary>Adds an unseen take to the town's pending theft (one per town: goods taken together are missed together).</summary>
    public static PendingTheft Add(PendingTheft? pending, string town, string faction, string item, int units, double value, double hours)
    {
        string what = units > 1 ? $"{units} {item}" : item;
        if (pending is null) return new PendingTheft(town, faction, what, value, hours);
        string items = pending.Items.Contains(what, StringComparison.Ordinal) || pending.Items.Length > 120 ? pending.Items : pending.Items + ", " + what;
        return pending with { Items = items, Value = pending.Value + value };
    }

    /// <summary>Whether a pending theft is missed now; false and dropped (<paramref name="drop"/>) when the clock went back (an earlier save).</summary>
    public static bool Missed(PendingTheft p, double now, out bool drop)
    {
        drop = now < p.TakenHours - 1;
        return !drop && now - p.TakenHours >= MissedAfter;
    }

    /// <summary>The History line for a notable missed theft, or null.</summary>
    public static string? Line(PendingTheft p) =>
        p.Value >= Notable ? $"{p.Town} is missing {p.Items} and wants to know who took it." : null;
}
