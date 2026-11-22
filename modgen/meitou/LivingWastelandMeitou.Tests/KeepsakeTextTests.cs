using LivingWasteland.MeitouMod;
using Xunit;

public class KeepsakeTextTests
{
    [Fact]
    public void A_veteran_keepsake_tells_their_life_death_and_record()
    {
        var m = new MemberRecord { Kills = 23, Fights = 17, FightsWon = 14, LastStands = 1, LimbsLost = 1, Distance = 4_120_000 };
        m.KillsBy["Dust Bandits"] = 15; m.KillsBy["Hungry Bandits"] = 8;
        m.Named.Add("Dolan Pike");
        var f = new Fallen("Hollin", 24 * 41 + 3, "Squin", "Kral's Chosen Warrior", "Kral's Chosen", null, 23, 40, ["the Hard Hand", "the Walker"], "Beep");
        var (name, text) = KeepsakeText.For(f, m, "Ruka", s => s);
        Assert.Equal("Keepsake of Hollin", name);
        Assert.Contains("It belonged to Hollin; Ruka has kept it since.", text);
        Assert.Contains("Hollin was with the squad 40 days. Known as the Hard Hand and the Walker. Killed Dolan Pike.", text);
        Assert.Contains("Most of their kills were Dust Bandits.", text);
        Assert.Contains("Once stood alone when the rest were down.", text);
        Assert.Contains("Fell on day 41 at Squin, killed by Kral's Chosen Warrior of Kral's Chosen. Avenged by Beep.", text);
        Assert.Contains("Their record: 23 kills, 14 of 17 fights won, 412 km walked.", text);
        Assert.Equal(text, KeepsakeText.For(f, m, "Ruka", s => s).Description);          // the same record, the same text
    }

    [Fact]
    public void Faction_names_read_naturally()
    {
        Assert.Equal("the Dust Bandits", KeepsakeText.Faction("Dust Bandits"));
        Assert.Equal("the Holy Nation", KeepsakeText.Faction("The Holy Nation"));
        Assert.Equal("Kral's Chosen", KeepsakeText.Faction("Kral's Chosen"));
    }

    [Fact]
    public void A_short_life_still_gets_a_keepsake()
    {
        var f = new Fallen("Shaku", 30, "the Hub", null, null, null, 0, 1.2, [], null);
        var (_, text) = KeepsakeText.For(f, null, "Leaf", s => s);
        Assert.Contains("Shaku was with the squad only 1 day.", text);
        Assert.Contains("Died on day 1 at the Hub. Not yet avenged.", text);      // hour 30: the game's Day 1, as History says
        Assert.Contains("Their record: 0 kills.", text);
    }
}

public class BarkLinesTests
{
    [Fact]
    public void Pursuers_say_lines_that_fit_who_sent_them()
    {
        Assert.Contains("Squin", BarkLines.Crew("Squin", 7));
        Assert.Contains("Aldous Peel", BarkLines.Reprisal("Aldous Peel", 3));
        Assert.Contains(BarkLines.Rival("lost an arm to your squad", 0, 0, 0), new[] { "Remember me? You took my arm.", "One arm's enough for you lot." });
        Assert.Contains(BarkLines.Rival("lost a leg to your squad", 0, 0, 0), new[] { "Walked a long way on this iron leg to find you.", "Remember me? I'll remind you." });
        Assert.StartsWith("Back for another", BarkLines.Rival("lost an arm to your squad", 1, 1, 0));
        Assert.Equal(BarkLines.Crew("Squin", 7), BarkLines.Crew("Squin", 7));
    }
}

public class ItemUseTextTests
{
    static readonly Fallen Hollin = new("Hollin", 24 * 41 + 3, "Squin", "Bad Pete", "Dust Bandits", null, 23, 40, [], null);

    [Fact]
    public void Turning_a_keepsake_over_says_a_line_for_the_fallen()
    {
        var lines = Enumerable.Range(0, 8).Select(i => KeepsakeText.Remember(Hollin, "Ruka", s => s, i).Bark).Distinct().ToList();
        Assert.Contains("For Hollin.", lines);
        Assert.Contains("Bad Pete is still out there, Hollin. Not for long.", lines);
        var (_, message) = KeepsakeText.Remember(Hollin, "Ruka", s => s, 0);
        Assert.Equal("Ruka turns over the keepsake of Hollin. Fell on day 41 at Squin, killed by Bad Pete of the Dust Bandits. Not yet avenged.", message);
        var avenged = Enumerable.Range(0, 8).Select(i => KeepsakeText.Remember(Hollin with { AvengedBy = "Ruka" }, "Ruka", s => s, i).Bark).ToList();
        Assert.Contains("I settled it for you, Hollin.", avenged);
        Assert.DoesNotContain(avenged, l => l.Contains("still out there"));
    }

    [Fact]
    public void A_warrant_says_who_sent_it_and_how_they_feel_now()
    {
        Assert.Equal("A warrant from Squin, your squad's name on it and a price beside it. Squin still wants you badly and has the money to hire more."
            + " Another crew from Squin is on the road now.", WarrantText.Read("Squin", "hunted", true, []));
        Assert.EndsWith("The ink's old. Squin has mostly let it go.", WarrantText.Read("Squin", "noticed", false, []));
        Assert.EndsWith("Towns that want you now: Stack, Heft.", WarrantText.Read(null, "quiet", false, ["Stack", "Heft"]));
    }
}
