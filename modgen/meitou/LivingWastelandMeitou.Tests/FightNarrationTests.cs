using LivingWasteland.MeitouMod;
using Xunit;

public sealed class FightNarrationTests
{
    [Fact]
    public void A_fight_without_a_win_does_not_say_the_fighter_who_stood_went_down()
    {
        var record = new SquadRecord([]);
        record.FightLook(["Ana", "Bo"], []);
        record.Downed("Bo");
        for (int i = 0; i < 5; i++) record.FightLook([], ["Bo"]);
        Assert.Equal((1, 0), (record.Member("Ana").Fights, record.Member("Ana").FightsWon));
        var entry = Assert.Single(record.History, h => h.Text.StartsWith("A fight went badly"));
        Assert.Contains("Bo", entry.Text);
        Assert.DoesNotContain("Ana", entry.Text);
    }

}
