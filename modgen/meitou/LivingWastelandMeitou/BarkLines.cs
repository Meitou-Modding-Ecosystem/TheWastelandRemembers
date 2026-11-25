using System;

namespace LivingWasteland.MeitouMod;

/// <summary>
/// What pursuers say over their heads when they close in on the squad (World.Bark, in every Meitou since 0.17): a bounty crew, an outlaw's
/// reprisal party, a rival come back. Picked by a key, so the same squad says the same thing. No engine types: unit tested.
/// </summary>
public static class BarkLines
{
    static readonly string[] CrewLines =
    [
        "{town} sends its regards.",
        "There's a price on your heads in {town}. We've come to collect.",
        "{town} wants a word with you. Hold still.",
        "You're worth more to {town} than you'd think.",
    ];

    static readonly string[] ReprisalLines =
    [
        "{outlaw} hasn't forgotten you.",
        "{outlaw} sent us. You know why.",
        "This one's for {outlaw}'s lads.",
    ];

    public static string Crew(string town, int key) => Pick(CrewLines, key).Replace("{town}", town);

    public static string Reprisal(string outlaw, int key) => Pick(ReprisalLines, key).Replace("{outlaw}", outlaw);

    /// <summary>A rival's opening words: from how the squad wronged them (their grudge), or a boast if they have beaten the squad before.</summary>
    /// <summary>
    /// What a wealthy rival says about the limb their people paid for, in their own faction's voice (ProstheticRules: only the wealthy get a
    /// skeleton limb). Factions without lines of their own say nothing about it.
    /// </summary>
    static readonly Dictionary<string, string[]> wealthyLimb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tech Hunters"] = ["World's End fitted me with ancient work. It hits harder than the {limb} you took.",
            "Skeleton make, dug out of a ruin. Better than the one you cost me."],
        ["Traders Guild"] = ["The Guild paid for this {limb}. You'll be paying it back.", "Finest {limb} money buys. It's going on your account."],
        ["Trade Ninjas"] = ["The Guild looks after its own. New {limb}, same debt.", "Paid in full for this. Now it's your turn to pay."],
        ["Nobles"] = ["A noble keeps up appearances. This {limb} cost more than your whole life.", "My family bought me a new {limb}. They'll buy your head next."],
        ["Mercenary Guild"] = ["Best {limb} the Guild's purse could buy. The bill's in your name.", "Skeleton-made now. I can fight all day."],
        ["Manhunters"] = ["New {limb}, paid for out of the last bounty. You're the next one.", "Bought this {limb} with bounty money. You're on the books."],
        ["Slave Traders"] = ["Sold three of your sort to pay for this. Make it four.", "Best {limb} in the market. You'll fetch less."],
    };

    /// <summary>
    /// A returning rival's line as they close in. <paramref name="faction"/>, <paramref name="tier"/> and <paramref name="armFitted"/> say
    /// what they came back with (Rivals.Mend): a wealthy rival with a new limb talks about it in their faction's voice (naming the arm or leg they were fitted; the one they lost to the squad if both); one whose arm was
    /// replaced no longer says they have one arm.
    /// </summary>
    public static string Rival(string grudge, int returns, int wins, int key, string? faction = null,
        ProstheticRules.Tier tier = ProstheticRules.Tier.Poor, bool armFitted = false, bool legFitted = false)
    {
        if (wins > 0) return Pick(["Back for another hiding, are we?", "Last time went my way. So will this."], key);
        if (tier == ProstheticRules.Tier.Wealthy && (armFitted || legFitted) && faction is not null && wealthyLimb.TryGetValue(faction, out var paid))
            return Pick(paid, key).Replace("{limb}", armFitted && (!legFitted || grudge.Contains("arm", StringComparison.Ordinal)) ? "arm" : "leg");
        string[] lines =
            grudge.Contains("arm", StringComparison.Ordinal) && armFitted ? ["Remember me? You took my arm. I've a new one.", "New arm. Same grudge."] :
            grudge.Contains("arm", StringComparison.Ordinal) ? ["Remember me? You took my arm.", "One arm's enough for you lot."] :
            grudge.Contains("leg", StringComparison.Ordinal) ? ["Walked a long way on this iron leg to find you.", "Remember me? I'll remind you."] :
            grudge.Contains("crew", StringComparison.Ordinal) ? ["You broke my crew. I've come for the rest of it.", "My lads are dead. Your turn."] :
            grudge.Contains("left for dead", StringComparison.Ordinal) ? ["You should have finished the job.", "You left me in the dirt. Big mistake."] :
            ["I said I'd come back for you.", "Thought you'd seen the last of me?"];
        if (returns > 0) lines = [.. lines, "Again. As many times as it takes."];
        return Pick(lines, key);
    }

    static string Pick(string[] lines, int key) => lines[(int)((uint)key % (uint)lines.Length)];
}
