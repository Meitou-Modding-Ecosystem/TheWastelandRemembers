using LivingWasteland.MeitouMod;
using Xunit;

public class TheftRulesTests
{
    [Fact]
    public void Only_other_factions_goods_count()
    {
        Assert.True(TheftRules.Counts(ownerFaction: 3, playerFaction: 0));
        Assert.False(TheftRules.Counts(ownerFaction: 0, playerFaction: 0));   // the squad's own
        Assert.False(TheftRules.Counts(ownerFaction: -1, playerFaction: 0));  // nobody's
    }

    [Fact]
    public void Heat_grows_with_value_is_capped_and_halved_when_seen()
    {
        Assert.Equal(0, TheftRules.Heat(99, seen: false));
        Assert.Equal(1, TheftRules.Heat(100, seen: false), 3);
        Assert.InRange(TheftRules.Heat(1000, seen: false), 3.4, 3.5);
        Assert.InRange(TheftRules.Heat(5000, seen: false), 5.6, 5.7);
        Assert.Equal(TheftRules.MaxHeat, TheftRules.Heat(10_000_000, seen: false));
        Assert.Equal(TheftRules.Heat(5000, seen: false) / 2, TheftRules.Heat(5000, seen: true), 6);
    }

    [Fact]
    public void Goods_taken_together_from_a_town_are_missed_together_half_a_day_later()
    {
        var p = TheftRules.Add(null, "Squin", "Shek Kingdom", "Katana", 1, 3000, 10);
        p = TheftRules.Add(p, "Squin", "Shek Kingdom", "Bread", 4, 200, 11);
        Assert.Equal("Katana, 4 Bread", p.Items);
        Assert.Equal(3200, p.Value);
        Assert.False(TheftRules.Missed(p, 21.9, out _));
        Assert.True(TheftRules.Missed(p, 22, out _));
        Assert.False(TheftRules.Missed(p, 5, out bool drop));               // an earlier save: dropped
        Assert.True(drop);
        Assert.Equal("Squin is missing Katana, 4 Bread and wants to know who took it.", TheftRules.Line(p));
        Assert.Null(TheftRules.Line(p with { Value = 500 }));
    }
}
