// What a captive says when the squad that caged them asks for news (News.AnswerAsk): nothing useful, in their own people's voice
// (AUTHORING.md "Voice and tone"). A Hive Queen doesn't bargain in words: she screams, as in her own vanilla lines. Pure: no engine.
namespace LivingWasteland.MeitouMod;

public static class CaptiveTalk
{
    /// <summary>The two Hive Queens' records (Western, the one TWR's cases follow; and the south's).</summary>
    public static readonly string[] Queens = ["96351-Dialogue.mod", "57334-Newwworld.mod"];

    static readonly (string[] Factions, string[] Lines)[] voices =
    [
        (["Shek Kingdom", "Kral's Chosen"],
            ["A Shek in a cage still owes you nothing.", "Kral would have broken these bars by now. I'll wait."]),
        (["The Holy Nation", "Holy Nation Outlaws", "Inquisition"],
            ["Okran sees you, sinner. That's all the news you'll get.", "The Flame keeps its counsel from heretics."]),
        (["United Cities", "Traders Guild", "Nobles", "Slave Traders"],
            ["News costs cats, and you've taken mine.", "I'll speak to a magistrate. Not to you."]),
        (["Starving Bandits", "Hungry Bandits"],
            ["Feed me and I might remember something.", "Can't think on an empty belly."]),
        (["Dust Bandits", "Bloodraiders", "Black Dragon Ninjas", "Hounds"],
            ["Cut me loose and maybe we'll talk.", "Ask your mother for news."]),
        (["Cannibals", "Cannibal Hunters"],
            ["Hrrh. No talk. Hungry.", "Soft one asks. Soft one gets nothing."]),
        (["Skin Bandits", "Skeleton Bandits"],
            ["Request refused. This unit is your prisoner.", "This unit has no news for its captor."]),
        (["Sand Ninjas", "Shinobi Thieves", "Flotsam Ninjas", "Trade Ninjas"],
            ["My sisters will be along. Ask them.", "A ninja in a cage keeps quiet. Remember that."]),
        (["Western Hive", "Southern Hive", "Hive Diaspora"],
            ["The hive does not speak to those who cage it.", "Let this one go home. Then it might remember."]),
        (["Tech Hunters", "Mercenary Guild"],
            ["I'm paid to talk. Nobody's paying.", "The Guild pays for what I know. You don't."]),
        (["Fogmen"], ["Hrrrh."]),
    ];

    const string Default = "Open the cage and I might remember something.";

    /// <summary>The line for a captive of <paramref name="faction"/> (the record decides for the Queens); <paramref name="roll"/> picks one.</summary>
    public static string Line(string? record, string? faction, ulong roll)
    {
        if (record is not null && Queens.Contains(record)) return "EEEEEH!";
        foreach (var (factions, lines) in voices)
            if (faction is not null && factions.Contains(faction, StringComparer.OrdinalIgnoreCase)) return lines[(int)(roll % (ulong)lines.Length)];
        return Default;
    }
}
