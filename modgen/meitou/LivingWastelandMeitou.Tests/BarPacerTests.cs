using LivingWasteland.MeitouMod;
using Xunit;

public class BarPacerTests
{
    [Fact]
    public void BuildingWaitsAfterALine()
    {
        var p = new BarPacer<int>();
        Assert.True(p.BuildingReady("bar", 0));
        p.Said("bar", 7, 100, 50, 200);
        Assert.False(p.BuildingReady("bar", 149));
        Assert.True(p.BuildingReady("bar", 150));
        Assert.True(p.BuildingReady("other bar", 120));
    }

    [Fact]
    public void SpeakerWaitsLongerThanTheBar()
    {
        var p = new BarPacer<int>();
        p.Said("bar", 7, 100, 50, 200);
        Assert.False(p.NpcReady(7, 250));
        Assert.True(p.NpcReady(8, 250));
        Assert.True(p.NpcReady(7, 300));
    }

    [Fact]
    public void QuietBarIsRetriedLater()
    {
        var p = new BarPacer<int>();
        p.Nothing("bar", 10, 300);
        Assert.False(p.BuildingReady("bar", 309));
        Assert.True(p.BuildingReady("bar", 310));
    }

    [Fact]
    public void PruneDropsOnlyExpired()
    {
        var p = new BarPacer<int>();
        p.Said("a", 1, 0, 10, 20);
        p.Said("b", 2, 0, 100, 200);
        p.Prune(50);
        Assert.Equal(2, p.Tracked);
        Assert.False(p.BuildingReady("b", 50));
    }

    [Fact]
    public void OrderRotatesAndKeepsEveryone()
    {
        int[] c = [1, 2, 3, 4];
        var first = BarPacer<int>.Order(c, 0, 0);
        var later = BarPacer<int>.Order(c, 30, 0);
        Assert.Equal(c.OrderBy(x => x), first.OrderBy(x => x));
        Assert.Equal(c.OrderBy(x => x), later.OrderBy(x => x));
        Assert.NotEqual(first[0], later[0]);
        Assert.Equal(first, BarPacer<int>.Order(c, 0, 0));
        Assert.Empty(BarPacer<int>.Order(Array.Empty<int>(), 5, 1));
    }
}
