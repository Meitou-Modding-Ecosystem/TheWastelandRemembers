using LivingWasteland.MeitouMod;
using Xunit;
using O = LivingWasteland.MeitouMod.RivalRules.Outcome;

public class RivalRulesTests
{
    [Fact]
    public void A_recovering_rival_who_gets_up_and_is_knocked_down_again_is_not_counted()
    {
        // seen in play: the rival's timer reset and "got away again" after each knock-down while they were still licking their wounds
        double last = double.MinValue;
        for (int i = 0; i < 5; i++) Assert.Equal(O.None, RivalRules.OnFightEnd(hunting: false, won: true, now: 100 + i * 0.3, last));
    }

    [Fact]
    public void A_hunting_rival_who_loses_gets_away_once_per_fight()
    {
        double last = double.MinValue;
        Assert.Equal(O.GotAway, RivalRules.OnFightEnd(true, true, 200, last));
        last = 200;
        Assert.Equal(O.None, RivalRules.OnFightEnd(true, true, 200.5, last));   // they got up again: the same fight
        Assert.Equal(O.None, RivalRules.OnFightEnd(true, true, 211, last));
        Assert.Equal(O.GotAway, RivalRules.OnFightEnd(true, true, 213, last));  // a new meeting
    }

    [Fact]
    public void Beating_the_squad_counts_in_either_phase_but_once_per_fight()
    {
        Assert.Equal(O.BeatSquad, RivalRules.OnFightEnd(false, false, 50, double.MinValue));
        Assert.Equal(O.BeatSquad, RivalRules.OnFightEnd(true, false, 50, double.MinValue));
        Assert.Equal(O.None, RivalRules.OnFightEnd(true, false, 51, 50));
    }
}
