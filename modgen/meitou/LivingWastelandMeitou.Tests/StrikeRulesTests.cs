using LivingWasteland.MeitouMod;
using Xunit;

// When another mod may strike a named outlaw (WastelandApi version 6; RivalryCore#59 decision 17, #126): 7 game days after they rose,
// at most once per 30 game days by other mods, never while wounded or while their men are out after the squad, and never unknown to it.
public class StrikeRulesTests
{
    const double Day = 24;

    [Fact]
    public void Strikable_after_the_later_of_the_rise_wait_and_the_cooldown()
    {
        Assert.Equal(7 * Day, CampRules.StrikableAfter("alive", 0, null));
        Assert.Equal(10 * Day + 7 * Day, CampRules.StrikableAfter("succeeded", 10 * Day, null));
        Assert.Equal(20 * Day + 30 * Day, CampRules.StrikableAfter("alive", 0, 20 * Day));
        Assert.Equal(-1, CampRules.StrikableAfter("dead", 0, null));
        Assert.Equal(-1, CampRules.StrikableAfter("joined", 0, null));
    }

    [Theory]
    [InlineData("dead", true, "", false, 100, 0, -1, "dead")]
    [InlineData("jailed", true, "", false, 100, 0, -1, "jailed")]
    [InlineData("joined", true, "", false, 100, 0, -1, "players")]
    [InlineData("alive", false, "raiding", false, 100, 0, -1, "not-known")]
    [InlineData("alive", true, "raiding", false, 6, 0, -1, "too-soon")]
    [InlineData("succeeded", true, "raiding", false, 15, 10, -1, "too-soon")]
    [InlineData("alive", true, "raiding", false, 40, 0, 20, "cooldown")]
    [InlineData("alive", true, "wounded", false, 100, 0, -1, "wounded")]
    [InlineData("alive", true, "raiding", true, 100, 0, -1, "story")]
    [InlineData("alive", true, "lying-low", false, 100, 0, 20, "yes")]
    public void The_answer_says_yes_or_why_not(string status, bool known, string mood, bool story, double days, double roseDays, double lastDays, string answer) =>
        Assert.Equal(answer, CampRules.StrikeAnswer(status, known, mood, story, days * Day, roseDays * Day, lastDays < 0 ? null : lastDays * Day));

    [Fact]
    public void A_character_left_alone_by_id_is_covered_and_reads_as_no_ones_name()
    {
        var left = new LeftAlone();
        Assert.True(left.AddId(123456789012UL));
        Assert.False(left.AddId(123456789012UL));
        Assert.False(left.AddId(0));
        Assert.True(left.CoversId(123456789012UL));
        Assert.False(left.CoversId(42));
        Assert.False(left.CoversId(0));
        Assert.Equal("#lasting:123456789012", Assert.Single(left.All).Name);         // older builds read it as a name nobody has
        Assert.False(left.Covers("Bayan", ""));
    }
}
