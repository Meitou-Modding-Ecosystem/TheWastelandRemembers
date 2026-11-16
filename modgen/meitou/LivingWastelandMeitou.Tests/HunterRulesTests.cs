using LivingWasteland.MeitouMod;
using Xunit;

public class HunterRulesTests
{
    [Fact]
    public void A_hunted_town_sends_at_most_one_crew_every_three_days()
    {
        Assert.False(HunterRules.Sends(20, 100, null));                    // only "wanted": no crew
        Assert.True(HunterRules.Sends(24, 100, null));
        Assert.False(HunterRules.Sends(30, 150, 100));
        Assert.True(HunterRules.Sends(30, 100 + HunterRules.Cooldown, 100));
        Assert.True(HunterRules.Sends(30, 50, 100));                       // a loaded older save: the clock went back
    }

    [Fact]
    public void Crews_start_out_of_sight_at_a_steady_distance()
    {
        var at = HunterRules.Start(42, "Stack", 500, 1000, 2000, (_, _) => true)!.Value;
        float d = MathF.Sqrt((at.X - 1000) * (at.X - 1000) + (at.Z - 2000) * (at.Z - 2000));
        Assert.InRange(d, HunterRules.StartMin - 1, HunterRules.StartMax + 1);
        Assert.Equal(at, HunterRules.Start(42, "Stack", 500, 1000, 2000, (_, _) => true));
        Assert.Null(HunterRules.Start(42, "Stack", 500, 1000, 2000, (_, _) => false));
        var east = HunterRules.Start(42, "Stack", 500, 1000, 2000, (x, _) => x > 1000);
        Assert.True(east is null || east.Value.X > 1000);
    }

    [Fact]
    public void ReprisalsAnswerBloodOnceWithinTheWindow()
    {
        var blood = new StruckMark(100, true);
        double delay = ReprisalRules.Delay(7, 2, 100);
        Assert.InRange(delay, 48, 95);
        Assert.Equal(delay, ReprisalRules.Delay(7, 2, 100));                              // same after a reload
        Assert.False(ReprisalRules.Due(7, 2, blood, 100 + delay - 1, null));              // too soon
        Assert.True(ReprisalRules.Due(7, 2, blood, 100 + delay, null));
        Assert.False(ReprisalRules.Due(7, 2, blood, 100 + delay, 100));                   // already answered
        Assert.False(ReprisalRules.Due(7, 2, blood, 100 + delay + ReprisalRules.Window, null)); // too late
        Assert.False(ReprisalRules.Due(7, 2, new StruckMark(100, false), 100 + delay, null));  // a scare is not answered
        Assert.False(ReprisalRules.Due(7, 2, null, 500, null));
        Assert.True(ReprisalRules.Due(7, 2, new StruckMark(300, true), 300 + ReprisalRules.Delay(7, 2, 300), 100)); // a new beating
    }

    [Fact]
    public void BookKeepsReprisalsAcrossASave()
    {
        var book = new CampBook();
        book.Strike(4, 100, true);
        var r = new Reprisal(4, 160, 12.5f, -3f);
        book.Sent(r, 100);
        book.Update(r, r with { X = 20f });
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) book.Write(w);
        ms.Position = 0;
        var back = new CampBook();
        using (var rd = new BinaryReader(ms)) back.Read(rd);
        Assert.Equal(new[] { r with { X = 20f } }, back.Reprisals.ToArray());
        Assert.Equal(100, back.AnsweredOf(4));
        Assert.Null(back.AnsweredOf(1));
        back.Done(back.Reprisals[0]);
        Assert.Empty(back.Reprisals);
    }
}
