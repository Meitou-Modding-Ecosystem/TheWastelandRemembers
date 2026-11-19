using LivingWasteland.MeitouMod;
using Xunit;

public class FightHistoryTests
{
    static readonly TitleRule[] Rules = [new("vet", "the Veteran", "member", "fights", null, null, 2), new("last", "Last One Standing", "member", "last_stands", null, null, 1)];

    static void Quiet(SquadRecord r, params string[] down)
    {
        for (int i = 0; i < 5; i++) r.FightLook([], down);
    }

    [Fact]
    public void AFightCountsForEveryoneWhoFoughtWhenItEnds()
    {
        var r = new SquadRecord(Rules);
        r.FightLook(["Ana", "Bo"], []);
        r.Knockout("Ana");
        Assert.True(r.InFight);
        r.FightLook([], []);
        Assert.Equal(0, r.Member("Ana").Fights);
        Quiet(r);
        Assert.False(r.InFight);
        Assert.Equal((1, 1), (r.Member("Ana").Fights, r.Member("Ana").FightsWon));
        Assert.Equal((1, 1), (r.Member("Bo").Fights, r.Member("Bo").FightsWon));
    }

    [Fact]
    public void ALostFightIsWrittenDownAndNotWon()
    {
        var r = new SquadRecord(Rules);
        r.FightLook(["Ana"], []);
        r.Downed("Ana");
        Quiet(r, "Ana");
        Assert.Equal((1, 0), (r.Member("Ana").Fights, r.Member("Ana").FightsWon));
        Assert.Contains(r.History, h => h.Text.StartsWith("A fight went badly"));
    }

    [Fact]
    public void TheOnlyOneStandingInAWonFightOfThreeGetsALastStand()
    {
        var r = new SquadRecord(Rules);
        r.FightLook(["Ana", "Bo", "Cy"], []);
        r.Kill("Ana", "Dust Bandits", false, null);
        r.Downed("Bo");
        r.Downed("Cy");
        r.FightLook([], ["Bo", "Cy"]);
        var said = new List<string>();
        for (int i = 0; i < 4; i++) said.AddRange(r.FightLook([], ["Bo", "Cy"]));
        Assert.Equal(1, r.Member("Ana").LastStands);
        Assert.Contains("Ana is now known as Last One Standing.", said);
        Assert.Contains(r.History, h => h.Text == "Ana was the last one standing.");
    }

    [Fact]
    public void HistoryKeepsPlaceAndDayAndShowsNewestFirst()
    {
        var r = new SquadRecord(Rules);
        r.Hours = 30;
        r.Place = "Squin";
        r.Kill("Ana", "Dust Bandits", false, null);
        r.Hours = 80;
        r.Place = "the wilds";
        r.LimbLost("Bo");
        var lines = r.HistoryLines();
        Assert.Equal("Day 3, the wilds: Bo lost a limb.", lines[0]);
        Assert.Equal("Day 1, Squin: Ana's first kill.", lines[1]);
    }

    [Fact]
    public void HistoryAndFightsSurviveSaveAndLoad()
    {
        var r = new SquadRecord(Rules);
        r.FightLook(["Ana"], []);
        r.Knockout("Ana");
        Quiet(r);
        r.Note("Stack now has you marked as wanted.");
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) r.Write(w);
        ms.Position = 0;
        var back = new SquadRecord(Rules);
        using (var rd = new BinaryReader(ms)) back.Read(rd);
        Assert.Equal(r.History, back.History);
        Assert.Equal(1, back.Member("Ana").Fights);
    }

    [Fact]
    public void HistoryIsCapped()
    {
        var r = new SquadRecord(Rules);
        for (int i = 0; i < SquadRecord.MaxHistory + 20; i++) r.Note($"n{i}");
        Assert.Equal(SquadRecord.MaxHistory, r.History.Count);
        Assert.Equal("n20", r.History[0].Text);
    }

    [Fact]
    public void A_squadmate_dying_elsewhere_does_not_turn_a_won_fight_into_a_loss()
    {
        // from a review of the code: Died puts the dead in the fight's Down list even if they never fought, and comparing counts made one
        // fighter standing plus one unrelated death read as a loss
        var r = new SquadRecord(Rules);
        r.FightLook(["Ana"], []);
        r.Knockout("Ana");
        r.Died("Bo", 10, "the wilds", null, null, null);            // Bo was elsewhere
        Quiet(r);
        Assert.Equal((1, 1), (r.Member("Ana").Fights, r.Member("Ana").FightsWon));
        Assert.DoesNotContain(r.History, h => h.Text.StartsWith("A fight went badly"));
    }

    [Fact]
    public void Everyone_who_fought_down_is_still_a_loss()
    {
        var r = new SquadRecord(Rules);
        r.FightLook(["Ana"], []);
        r.Knockout("Ana");
        r.Died("Ana", 10, "the wilds", null, null, null);
        Quiet(r, "Ana");
        Assert.Equal((1, 0), (r.Member("Ana").Fights, r.Member("Ana").FightsWon));
    }
}
