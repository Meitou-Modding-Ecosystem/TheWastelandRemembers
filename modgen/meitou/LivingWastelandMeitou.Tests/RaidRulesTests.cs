using LivingWasteland.MeitouMod;
using Xunit;

public class RaidRulesTests
{
    [Fact]
    public void RaidsComeEveryFewDaysTheSameAfterAReload()
    {
        double first = RaidRules.NextStart(9, 0, 0);
        Assert.InRange(first, RaidRules.GapMin, RaidRules.GapMax);
        Assert.Equal(first, RaidRules.NextStart(9, 0, 0));
        double next = RaidRules.NextStart(9, 0, 300);
        Assert.InRange(next, 300 + RaidRules.GapMin, 300 + RaidRules.GapMax);
        Assert.NotEqual(RaidRules.NextStart(9, 0, 0), RaidRules.NextStart(9, 1, 0));     // the factions do not move together (this seed)
    }

    [Fact]
    public void TownPickStaysInRange()
    {
        for (int i = 0; i < 50; i++) Assert.InRange(RaidRules.PickTown(5, i % 4, i * 37.0, 7), 0, 6);
        Assert.Equal(-1, RaidRules.PickTown(5, 0, 10, 0));
    }

    [Fact]
    public void GossipFollowsTheOutcome()
    {
        Assert.Equal(0, RaidRules.State(null, 50));
        var r = new RaidRecord(3, 100, 0, 0);
        Assert.Equal(1, RaidRules.State(r, 120));
        Assert.Equal(2, RaidRules.State(r with { EndedHours = 130, Caught = true }, 140));
        Assert.Equal(3, RaidRules.State(r with { EndedHours = 148 }, 200));
        Assert.Equal(0, RaidRules.State(r with { EndedHours = 148 }, 148 + RaidRules.Talked));     // old news
    }

    [Fact]
    public void TheSquadsPartIsThankedOnce()
    {
        var r = new RaidRecord(0, 10, 0, 0, 60);
        Assert.Equal((0, 0), RaidRules.Settle(r));
        Assert.Equal((RaidRules.Thanks, 0), RaidRules.Settle(r with { Helped = true }));
        Assert.Equal((0, RaidRules.Thanks), RaidRules.Settle(r with { Broke = true }));
        Assert.Equal((0, RaidRules.Thanks), RaidRules.Settle(r with { Helped = true, Broke = true }));   // turning on the raiders is what counts
    }
}
