using LivingWasteland.MeitouMod;
using Xunit;

// How a squadmate died, from the engine's death notice (Meitou 0.22.2+; DeathNotices.cs): before it, one who bled out after the fight, or
// was shot or executed out of the hit feed's sight, was "Cause of death unknown" on the Fallen page, their grave and their keepsake.
public class DeathCauseTests
{
    static SquadRecord Squad() => new([]);

    [Theory]
    [InlineData("BloodLoss", "Other", "Bled out")]
    [InlineData("Starvation", "Other", "Starved")]
    [InlineData("VitalPart", "Execution", "Executed")]
    [InlineData("VitalPart", "Bolt", "Shot dead")]
    [InlineData("Eaten", "Eaten", "Eaten alive")]
    [InlineData("VitalPart", "Rack", "Died on the rack")]
    [InlineData("VitalPart", "Other", "Died of their wounds")]
    [InlineData("VitalPart", "Melee", null)]                         // a plain blow: the killer says it
    [InlineData("SomethingNew", "AlsoNew", null)]                    // a cause a later Meitou adds: nothing invented
    public void The_engines_cause_reads_as_a_phrase(string cause, string site, string? phrase) =>
        Assert.Equal(phrase, SquadRecord.CausePhrase(cause, site));

    [Fact]
    public void A_notice_before_the_death_is_seen_gives_the_cause()
    {
        var r = Squad();
        r.Seen("Ruka", 0);                                            // with the squad over three days: the epitaph is the cause
        r.DeathNoted("Ruka", "Bled out", null, null, null);
        r.Died("Ruka", 24 * 3 + 5, "Squin", null, null, null);
        var f = Assert.Single(r.FallenMembers);
        Assert.Equal("Bled out", f.Cause);
        Assert.Equal("Bled out.", SquadRecord.Epitaph(f));
        Assert.Contains(r.FallenLines(), l => l == "Ruka: bled out on day 3 at Squin.");
        Assert.Contains("Bled out on day 3 at Squin. Not yet avenged.", KeepsakeText.For(f, null, "Leaf", s => s).Description);
    }

    [Fact]
    public void A_notice_after_the_death_names_an_unseen_killer_but_never_replaces_one()
    {
        var r = Squad();
        r.Died("Ruka", 48, "Stack", null, null, null);
        r.DeathNoted("Ruka", "Shot dead", "Paladin", "The Holy Nation", "77-x.mod");
        var f = Assert.Single(r.FallenMembers);
        Assert.Equal(("Paladin", "The Holy Nation", "77-x.mod", "Shot dead"), (f.Killer, f.KillerFaction, f.KillerRecord, f.Cause));
        Assert.Contains("The Holy Nation", r.Feuds.Keys);
        Assert.Equal(["Ruka"], r.Avenge("Beep", "Paladin", "The Holy Nation", "77-x.mod"));

        r.Died("Hollin", 50, "Stack", "Dust Bandit", "Dust Bandits", null);
        r.DeathNoted("Hollin", "Shot dead", "Someone Else", "Hounds", null);
        Assert.Equal("Dust Bandit", r.FallenMembers[1].Killer);
    }

    [Fact]
    public void The_cause_survives_a_save()
    {
        var r = Squad();
        r.DeathNoted("Ruka", "Starved", null, null, null);
        r.Died("Ruka", 48, "the wilds", null, null, null);
        r.Died("Hollin", 50, "Stack", "Dust Bandit", "Dust Bandits", null);
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) r.Write(w);
        ms.Position = 0;
        var back = Squad();
        using (var rd = new BinaryReader(ms)) Assert.True(back.Read(rd));
        Assert.Equal("Starved", back.FallenMembers[0].Cause);
        Assert.Null(back.FallenMembers[1].Cause);
        Assert.Equal("Dust Bandit", back.FallenMembers[1].Killer);
    }
}
