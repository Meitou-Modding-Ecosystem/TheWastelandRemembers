using LivingWasteland.MeitouMod;
using Xunit;

// The saved chronicle as a story (seen in the bv play test: History newest first, every unearned title listed, no heading).
public class ChronicleRulesTests
{
    [Fact]
    public void History_reads_from_day_one_forwards_with_repeats_folded()
    {
        string window = "Day 3, The Hub: A fight with 6 down.\nDay 1, the wilds: The squad traded blows.\nDay 1, the wilds: The squad traded blows.";
        Assert.Equal("Day 1, the wilds: The squad traded blows. (twice)\nDay 3, The Hub: A fight with 6 down.", ChronicleRules.Forwards(window));
    }

    [Fact]
    public void Titles_keep_only_the_earned_ones_under_their_headings()
    {
        string catalog = "Titles: 2 of 75 earned\n\n-- Members --\n* the Hard Hand: 75 kills  [Cat]\n  Bonepicker: 250 kills\n" +
            "-- Squad --\n  the Headtakers: 25 squad leaders killed\n-- Animal companions --\nBattle-tested Companion: survive 10 fights\n" +
            "-- Legendary --\n* the Long Night: 1500 kills  [Cat]";
        Assert.Equal("Titles: 2 of 75 earned\n\n-- Members --\nthe Hard Hand: 75 kills  [Cat]\n\n-- Legendary --\nthe Long Night: 1500 kills  [Cat]",
            ChronicleRules.EarnedTitles(catalog));
        Assert.Equal("Titles: 0 of 75 earned\n\nNo titles earned yet.", ChronicleRules.EarnedTitles("Titles: 0 of 75 earned\n\n-- Members --\n  Bonepicker: 250 kills"));
    }

    [Theory]
    [InlineData("Known as: no name yet", "Squad chronicle: days 1 to 3")]
    [InlineData("Known as: the Grave-Fillers", "the Grave-Fillers: days 1 to 3")]
    public void The_heading_names_the_squad_and_the_days(string panel, string heading) =>
        Assert.Equal(heading, ChronicleRules.Heading("-- The squad --\n" + panel, "Day 3, The Hub: x\nDay 1, Squin: y"));
}
