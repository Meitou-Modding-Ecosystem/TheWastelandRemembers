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
    public static string Rival(string grudge, int returns, int wins, int key)
    {
        if (wins > 0) return Pick(["Back for another hiding, are we?", "Last time went my way. So will this."], key);
        string[] lines =
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
