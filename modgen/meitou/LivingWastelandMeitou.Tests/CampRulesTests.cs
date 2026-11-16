using LivingWasteland.MeitouMod;
using Xunit;

public class CampRulesTests
{
    // a small world: cells 27..37 x 27..37 (about 50 km across) are the outlaw's region, one town at the origin, another far east
    static readonly List<(int X, int Z)> Region = [.. from x in Enumerable.Range(27, 11) from z in Enumerable.Range(27, 11) select (x, z)];
    static readonly List<(float X, float Z)> Towns = [(0f, 0f), (60000f, 0f)];
    static bool AllDry(float x, float z) => true;

    [Fact]
    public void SameInputsSameSpot()
    {
        var a = CampRules.Spot(42, 3, 10, OutlawNews.Raiding, Region, Towns, AllDry);
        var b = CampRules.Spot(42, 3, 10, OutlawNews.Raiding, Region, Towns, AllDry);
        Assert.NotNull(a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void NewStretchUsuallyNewSpot()
    {
        var spots = Enumerable.Range(0, 10).Select(p => CampRules.Spot(42, 3, p, OutlawNews.Gathering, Region, Towns, AllDry)).Distinct().Count();
        Assert.True(spots >= 8);
        Assert.True(CampRules.Spot(42, 3, 1, OutlawNews.Raiding, Region, Towns, AllDry) != CampRules.Spot(42, 4, 1, OutlawNews.Raiding, Region, Towns, AllDry));
    }

    [Fact]
    public void SpotsStayInTheRegion()
    {
        for (int p = 0; p < 50; p++)
        {
            var s = CampRules.Spot(7, 1, p, OutlawNews.LyingLow, Region, Towns, AllDry);
            Assert.NotNull(s);
            Assert.Contains(CampRules.CellOf(s!.Value.X, s.Value.Z), Region);
        }
    }

    [Fact]
    public void RaidersNearTownsTheRestFarther()
    {
        for (int p = 0; p < 30; p++)
        {
            var raid = CampRules.Spot(9, 2, p, OutlawNews.Raiding, Region, Towns, AllDry)!.Value;
            var low = CampRules.Spot(9, 2, p, OutlawNews.LyingLow, Region, Towns, AllDry)!.Value;
            float r = CampRules.NearestTown(raid.X, raid.Z, Towns), l = CampRules.NearestTown(low.X, low.Z, Towns);
            Assert.InRange(r, CampRules.TownDistance(OutlawNews.Raiding).Min, CampRules.TownDistance(OutlawNews.Raiding).Max);
            Assert.True(l >= CampRules.TownDistance(OutlawNews.LyingLow).Min);
        }
    }

    [Fact]
    public void FallsBackWhenNoSpotFitsButNeverInATown()
    {
        // every cell within 9000 of a town: "lying low" cannot fit, so the fallback is used, still at least 3000 from the town
        List<(int, int)> tight = [(31, 31), (32, 32), (31, 32), (32, 31)];
        var s = CampRules.Spot(1, 1, 1, OutlawNews.LyingLow, tight, Towns, AllDry);
        Assert.NotNull(s);
        Assert.True(CampRules.NearestTown(s!.Value.X, s.Value.Z, Towns) >= 3000f);
    }

    [Fact]
    public void NoDryGroundNoSpot()
    {
        Assert.Null(CampRules.Spot(1, 1, 1, OutlawNews.Raiding, Region, Towns, (_, _) => false));
        Assert.Null(CampRules.Spot(1, 1, 1, OutlawNews.Raiding, [], Towns, AllDry));
    }

    [Fact]
    public void OnlyDrySpotsAreChosen()
    {
        static bool EastOnly(float x, float z) => x > 0;
        for (int p = 0; p < 20; p++)
        {
            var s = CampRules.Spot(5, 4, p, OutlawNews.Gathering, Region, Towns, EastOnly);
            if (s is { } v) Assert.True(v.X > 0);
        }
    }

    [Fact]
    public void SizeRule()
    {
        Assert.Equal(7, CampRules.Size(5, OutlawNews.Gathering));
        Assert.Equal(4, CampRules.Size(5, OutlawNews.Wounded));
        Assert.Equal(2, CampRules.Size(2, OutlawNews.Wounded));
        Assert.Equal(5, CampRules.Size(5, OutlawNews.Raiding));
        Assert.Equal(1, CampRules.Size(0, OutlawNews.LyingLow));
    }

    [Fact]
    public void NeverMovesNearAPlayer()
    {
        (float, float) a = (0f, 0f), b = (50000f, 0f);
        Assert.True(CampRules.MayMove(a, b, []));
        Assert.True(CampRules.MayMove(a, b, [(25000f, 0f)]));
        Assert.False(CampRules.MayMove(a, b, [(1000f, 0f)]));
        Assert.False(CampRules.MayMove(a, b, [(49000f, 500f)]));
    }

    [Fact]
    public void CellMathMatchesMeitou()
    {
        // Meitou: CellOf = floor(x / 4608) + 32; CellRect min = (cx - 32) * 4608
        Assert.Equal((32, 32), CampRules.CellOf(0f, 0f));
        Assert.Equal((31, 33), CampRules.CellOf(-1f, 4608f));
        Assert.Equal((-4608f, 4608f), CampRules.CellCorner(31, 33));
    }

    [Fact]
    public void BookRoundTripAndOldSaves()
    {
        var book = new CampBook();
        book.Set(3, new CampEntry(1.5f, -2f, 77, OutlawNews.Wounded));
        book.Set(1, new CampEntry(10f, 20f, 5, OutlawNews.Raiding));
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) book.Write(w);
        ms.Position = 0;
        var back = new CampBook();
        using (var r = new BinaryReader(ms)) back.Read(r);
        Assert.Equal(book.All.ToList(), back.All.ToList());
        Assert.Equal(new[] { 1, 3 }, back.All.Keys.ToArray());

        var empty = new CampBook();
        using (var r = new BinaryReader(new MemoryStream())) empty.Read(r);   // a save from before camps
        Assert.Empty(empty.All);
    }

    [Fact]
    public void Struck_outlaws_lie_low_or_lick_wounds_for_two_stretches_then_the_news_rolls_again()
    {
        var scare = new StruckMark(100, Bloodied: false);
        var blood = new StruckMark(100, Bloodied: true);
        Assert.Equal(OutlawNews.LyingLow, CampRules.Doing(42, 3, 110, scare));
        Assert.Equal(OutlawNews.Wounded, CampRules.Doing(42, 3, 110, blood));
        Assert.Equal(OutlawNews.Doing(42, 3, 100 + CampRules.ReactFor + 1), CampRules.Doing(42, 3, 100 + CampRules.ReactFor + 1, blood));
        Assert.Equal(OutlawNews.Doing(42, 3, 50), CampRules.Doing(42, 3, 50, blood));             // a blow in the future (a reloaded save) is ignored
    }

    [Fact]
    public void A_blow_changes_the_camp_key_so_the_camp_moves_and_the_key_stays_while_shaken()
    {
        long before = CampRules.Key(2, 99, null);
        var s = new StruckMark(100, true);
        long shaken = CampRules.Key(2, 101, s);
        Assert.NotEqual(before, shaken);
        Assert.Equal(shaken, CampRules.Key(2, 150, s));
        Assert.Equal(OutlawNews.Period(2, 100 + CampRules.ReactFor + 5), CampRules.Key(2, 100 + CampRules.ReactFor + 5, s));
    }

    [Fact]
    public void Book_keeps_the_first_blow_of_a_fight_and_adds_blood()
    {
        var b = new CampBook();
        Assert.True(b.Strike(1, 100, false));
        Assert.False(b.Strike(1, 101, true));
        Assert.Equal(new StruckMark(100, true), b.StruckOf(1));
        Assert.True(b.Strike(1, 100 + CampRules.ReactFor + 1, false));       // a new reaction later
        Assert.Equal(1e6, CampRules.DaysSince(b.StruckOf(9), 50));
        Assert.Equal(1.0, CampRules.DaysSince(new StruckMark(24, false), 48), 3);
    }

    [Fact]
    public void Book_saves_blows_and_reads_old_saves()
    {
        var b = new CampBook();
        b.Set(3, new CampEntry(1, 2, 5, 1));
        b.Strike(3, 40, true);
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) b.Write(w);
        ms.Position = 0;
        var back = new CampBook();
        using (var r = new BinaryReader(ms)) back.Read(r);
        Assert.Equal(new StruckMark(40, true), back.StruckOf(3));
        Assert.Equal(b.All.ToList(), back.All.ToList());
    }

    [Theory]
    [InlineData(0, -5000, 1)]      // north is -z
    [InlineData(5000, -5000, 2)]
    [InlineData(5000, 0, 3)]       // east is +x
    [InlineData(5000, 5000, 4)]
    [InlineData(0, 5000, 5)]
    [InlineData(-5000, 5000, 6)]
    [InlineData(-5000, 0, 7)]
    [InlineData(-5000, -5000, 8)]
    [InlineData(300, 200, 9)]      // within 150 m: right outside
    public void Directions_from_a_town(float x, float z, int expected) => Assert.Equal(expected, CampRules.Direction(0, 0, x, z));

    [Fact]
    public void Nearest_town()
    {
        List<(float, float)> towns = [(0, 0), (10000, 0), (0, 10000)];
        Assert.Equal(1, CampRules.NearestIndex(9000, 500, towns));
        Assert.Equal(-1, CampRules.NearestIndex(1, 1, []));
    }

    [Fact]
    public void Distance_pay_nothing_near_double_far()
    {
        Assert.Equal(0, CampRules.DistanceBonus(2000, 4 * CampRules.Km));
        Assert.Equal(1000, CampRules.DistanceBonus(2000, 15 * CampRules.Km));
        Assert.Equal(2000, CampRules.DistanceBonus(2000, 40 * CampRules.Km));
        Assert.True(CampRules.InReach(11 * CampRules.Km, 12));
        Assert.False(CampRules.InReach(13 * CampRules.Km, 12));
        Assert.True(CampRules.InReach(99 * CampRules.Km, 0));
        Assert.Equal((-4608f + 2304f, 2304f), CampRules.CellCentre(31, 32));
    }

    [Fact]
    public void Bounties_grow_with_raiding_and_cap_at_double()
    {
        Assert.Equal(0, CampRules.RaidingStretches(7, 2, 0));
        for (int outlaw = 0; outlaw < 40; outlaw++)                       // a new world: no price has grown yet on day 1
            Assert.True(CampRules.RaidingStretches(7, outlaw, 20) <= 1, $"outlaw {outlaw} starts grown");
        int early = CampRules.RaidingStretches(7, 2, 24 * 30), late = CampRules.RaidingStretches(7, 2, 24 * 300);
        Assert.True(late >= early);
        int counted = 0;                                                     // matches the news roll stretch by stretch
        for (long p = 0; p < OutlawNews.Period(2, 24 * 30); p++) if (OutlawNews.DoingIn(7, 2, p) == OutlawNews.Raiding) counted++;
        Assert.Equal(counted, early);
        Assert.Equal(0.3, CampRules.Growth(3), 3);
        Assert.Equal(1.0, CampRules.Growth(40));
        Assert.Equal(0, CampRules.Tier(0.1));
        Assert.Equal(1, CampRules.Tier(0.2));
        Assert.Equal(2, CampRules.Tier(0.6));
        Assert.Equal(3, CampRules.Tier(1.0));
        Assert.Equal(OutlawNews.Doing(7, 2, 500), OutlawNews.DoingIn(7, 2, OutlawNews.Period(2, 500)));
    }
}
