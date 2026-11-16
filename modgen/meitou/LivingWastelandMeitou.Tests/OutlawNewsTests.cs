using LivingWasteland.MeitouMod;
using Xunit;

public class OutlawNewsTests
{
    [Fact]
    public void SameWorldSameAnswer()
    {
        Assert.Equal(OutlawNews.Doing(42, 3, 500), OutlawNews.Doing(42, 3, 500));
        Assert.Equal(OutlawNews.Doing(42, 3, 500), OutlawNews.Doing(42, 3, 500.5));
    }

    [Fact]
    public void ActivitiesChangeOverTimeAndCoverAllFour()
    {
        var seen = new HashSet<int>();
        int changes = 0, last = OutlawNews.Doing(7, 0, 0);
        for (double h = 0; h < 24 * 200; h += OutlawNews.Stretch)
        {
            int d = OutlawNews.Doing(7, 0, h);
            seen.Add(d);
            if (d != last) changes++;
            last = d;
        }
        Assert.Equal(new HashSet<int> { 1, 2, 3, 4 }, seen);
        Assert.True(changes > 20);
    }

    [Fact]
    public void OutlawsDoNotAllMoveTogether()
    {
        var now = Enumerable.Range(0, 24).Select(i => OutlawNews.Doing(99, i, 1000)).ToList();
        Assert.True(now.Distinct().Count() >= 3);
    }

    [Fact]
    public void RaidingIsTheMostCommon()
    {
        var counts = new int[5];
        for (int i = 0; i < 24; i++)
            for (double h = 0; h < 24 * 100; h += OutlawNews.Stretch) counts[OutlawNews.Doing(5, i, h)]++;
        Assert.True(counts[1] > counts[2] && counts[1] > counts[3] && counts[1] > counts[4]);
    }
}

public class TownNameAndNamedKillTests
{
    [Theory]
    [InlineData("Bad Teeth (override) takeover UC", "Bad Teeth")]
    [InlineData("Bark (override prosperous) 2", "Bark")]
    [InlineData("Stack", "Stack")]
    public void OverrideRecordsKeepTheTownsName(string record, string town) => Assert.Equal(town, TownNames.Clean(record));

    [Fact]
    public void NamedKillsKnowWhenTheyHappened()
    {
        var r = new SquadRecord([]);
        Assert.False(r.KilledNamed("Orsa Quillon"));
        Assert.Equal(-1, r.DaysSinceNamed("Orsa Quillon", 100));
        r.Hours = 48;
        r.Kill("Ana", "Dust Bandits", true, "Orsa Quillon");
        Assert.True(r.KilledNamed("Orsa Quillon"));
        Assert.Equal(2, r.DaysSinceNamed("Orsa Quillon", 96), 3);
        Assert.True(r.KilledNamed("Fog Prince|Orsa Quillon"));
    }

    [Fact]
    public void KillTimesSurviveSaveAndLoad()
    {
        var r = new SquadRecord([]);
        r.Hours = 24;
        r.Kill("Ana", "Dust Bandits", true, "Orsa Quillon");
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) r.Write(w);
        ms.Position = 0;
        var back = new SquadRecord([]);
        using (var rd = new BinaryReader(ms)) back.Read(rd);
        Assert.Equal(1, back.DaysSinceNamed("Orsa Quillon", 48), 3);
    }
}
