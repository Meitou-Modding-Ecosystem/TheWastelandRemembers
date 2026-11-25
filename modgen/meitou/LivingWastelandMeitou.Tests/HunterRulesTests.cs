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
        var r = new Reprisal(4, 160, 12.5f, -3f) { Tagged = true };
        book.Sent(r, 100);
        book.Update(r, r with { X = 20f });
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) book.Write(w);
        using (var old = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            ms.Position = 0;
            Assert.Equal(3, old.ReadInt32());
            Assert.Equal(0, old.ReadInt32()); // camps
            Assert.Equal(1, old.ReadInt32()); // struck
            Assert.Equal(4, old.ReadInt32());
            Assert.Equal(100, old.ReadDouble());
            Assert.True(old.ReadBoolean());
            Assert.Equal(1, old.ReadInt32()); // reprisals
            Assert.Equal(4, old.ReadInt32());
            Assert.Equal(160, old.ReadDouble());
            Assert.Equal(20f, old.ReadSingle());
            Assert.Equal(-3f, old.ReadSingle());
            Assert.Equal(1, old.ReadInt32()); // answered
            Assert.Equal(4, old.ReadInt32());
            Assert.Equal(100, old.ReadDouble());
        }
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

public class PartyClaimsTests
{
    [Fact]
    public void Only_our_ended_parties_of_the_kind_are_released()
    {
        string crew = PartyClaims.Crew("Squin", 10), gone = PartyClaims.Crew("Stack", 4), rival = PartyClaims.Rival("Hollin One-Arm");
        Assert.Equal([gone], PartyClaims.ToRelease([crew, gone, rival], "crew:", [crew]));
        Assert.Empty(PartyClaims.ToRelease([crew, rival], "reprisal:", []));
        Assert.Equal([rival], PartyClaims.ToRelease([crew, rival, rival], "rival:", []));
    }

    [Fact]
    public void Keys_name_the_party_the_same_way_in_every_culture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("crew:Squin:10.50", PartyClaims.Crew("Squin", 10.5));
            Assert.Equal("reprisal:Pirate Captain:3.00", PartyClaims.Reprisal("Pirate Captain", 3));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }
}

public class StruckByOthersTests
{
    [Fact]
    public void A_camp_struck_by_another_mods_band_brings_no_reprisal_on_the_squad()
    {
        var book = new CampBook();
        Assert.True(book.Strike(7, 10, bloodied: true));
        book.Answer(7, 10);
        for (double now = 10; now < 300; now += 6) Assert.False(ReprisalRules.Due(1, 7, book.StruckOf(7), now, book.AnsweredOf(7)));

        var squad = new CampBook();                                             // the squad's own blows: answered in time
        Assert.True(squad.Strike(7, 10, bloodied: true));
        Assert.Contains(Enumerable.Range(0, 50).Select(i => 10.0 + i * 6), now => ReprisalRules.Due(1, 7, squad.StruckOf(7), now, squad.AnsweredOf(7)));
    }
}
