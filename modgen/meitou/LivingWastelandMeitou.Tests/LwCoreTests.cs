using LivingWasteland.MeitouMod;
using Xunit;

public class LwCoreTests
{
    const string Jobs = "LW open jobs";

    [Fact]
    public void Unset_variables_read_their_initial_value()
    {
        var c = new LwCore(new Dictionary<string, int> { ["Quest qc_hunt"] = 0, ["X"] = 7 });
        Assert.Equal(0, c.Get("Quest qc_hunt"));
        Assert.Equal(7, c.Get("X"));
        Assert.Equal(0, c.Get("never declared"));
    }

    [Fact]
    public void Set_and_add_report_before_and_after()
    {
        var c = new LwCore();
        Assert.Equal((0, 1), c.Set("Quest qc_hunt", 1));
        Assert.Equal((1, 3), c.Add("Quest qc_hunt", 2));
        Assert.Equal((3, 2), c.Add("Quest qc_hunt", -1));
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        var a = new LwCore();
        a.Set("Contract ol_teshin_ro_contract", 1);
        a.Set(Jobs, 3);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) a.Write(w);
        ms.Position = 0;
        var b = new LwCore();
        b.Set("stale", 9);
        using (var r = new BinaryReader(ms)) b.Read(r);
        Assert.Equal(1, b.Get("Contract ol_teshin_ro_contract"));
        Assert.Equal(3, b.Get(Jobs));
        Assert.Equal(0, b.Get("stale"));
        Assert.Equal(a.Ordered(), b.Ordered());
    }

    [Theory]
    [InlineData(4, 5, 5, true)]   // reaching the cap
    [InlineData(5, 4, 5, false)]  // dropping below it
    [InlineData(2, 3, 5, null)]   // nothing to say
    [InlineData(5, 6, 5, null)]   // already over
    public void Cap_messages_fire_on_crossing_only(int before, int after, int cap, bool? reached)
    {
        string? m = LwCore.CapMessage(before, after, cap);
        if (reached is null) Assert.Null(m);
        else if (reached == true) Assert.StartsWith("You carry 5 unfinished jobs", m);
        else Assert.Equal("You have room for another job.", m);
    }

    [Theory]
    [InlineData("Contract ol_x_contract", 0, false)]
    [InlineData("Contract ol_x_contract", 1, true)]
    [InlineData("Contract ol_x_contract", 2, false)]
    [InlineData("Quest qc_hunt", 1, true)]
    [InlineData("Quest qc_hunt", 4, true)]
    [InlineData("Quest qc_hunt", 99, false)]
    [InlineData("LW open jobs", 3, false)]
    [InlineData("RC flag venna", 1, false)]
    public void Open_means_taken_and_not_paid(string name, int value, bool open) => Assert.Equal(open, LwCore.IsOpen(name, value));

    [Fact]
    public void Pretty_names()
    {
        Assert.Equal("teshin ro", LwCore.Pretty("Contract ol_teshin_ro_contract"));
        Assert.Equal("qc hunt", LwCore.Pretty("Quest qc_hunt"));
    }

    [Fact]
    public void Open_jobs_are_listed_in_a_fixed_order()
    {
        var c = new LwCore();
        c.Set("Quest qc_relic", 2);
        c.Set("Contract ol_teshin_ro_contract", 1);
        c.Set("Quest qc_hunt", 99);
        Assert.Equal(new[] { ("Contract ol_teshin_ro_contract", 1), ("Quest qc_relic", 2) }, c.OpenJobs());
        Assert.Equal("2 open (limit 5): 1. teshin ro, 2. qc relic (full list in log.txt)", c.Summary(5));
    }

    [Fact]
    public void Give_up_by_number_resets_the_job_and_frees_a_slot()
    {
        var c = new LwCore();
        c.Set("Contract ol_teshin_ro_contract", 1);
        c.Set("Quest qc_relic", 2);
        c.Set(Jobs, 2);
        Assert.Equal("qc relic", c.GiveUp("2", Jobs));
        Assert.Equal(0, c.Get("Quest qc_relic"));
        Assert.Equal(1, c.Get(Jobs));
        Assert.Single(c.OpenJobs());
    }

    [Fact]
    public void Give_up_by_name_part_and_misses()
    {
        var c = new LwCore();
        c.Set("Contract ol_teshin_ro_contract", 1);
        c.Set(Jobs, 1);
        Assert.Null(c.GiveUp("9", Jobs));
        Assert.Null(c.GiveUp("nobody", Jobs));
        Assert.Null(c.GiveUp("   ", Jobs));
        Assert.Equal("teshin ro", c.GiveUp("TESHIN", Jobs));
        Assert.Equal(0, c.Get(Jobs));
        Assert.Null(c.GiveUp("1", Jobs));
    }

    [Fact]
    public void Give_up_never_drives_the_counter_below_zero()
    {
        var c = new LwCore();
        c.Set("Quest qc_hunt", 1);
        Assert.Equal("qc hunt", c.GiveUp("1", Jobs));
        Assert.Equal(0, c.Get(Jobs));
    }

    [Fact]
    public void Rumour_seen_time_is_kept_from_the_first_sighting()
    {
        var c = new LwCore();
        Assert.True(c.RecordSeen("348-x.mod", 100.5));
        Assert.False(c.RecordSeen("348-x.mod", 500));
        Assert.Equal(1.0, c.DaysSince("348-x.mod", 124), 3);
        Assert.Equal(0.0, c.DaysSince("348-x.mod", 100), 3);
    }

    [Fact]
    public void Already_true_at_the_first_look_is_old_news_and_stays_old()
    {
        var c = new LwCore();
        c.MarkOld("dead-before");
        Assert.True(c.DaysSince("dead-before", 5) > 1000);
        Assert.False(c.RecordSeen("dead-before", 30));          // a later poll must not turn it into fresh news
        Assert.True(c.DaysSince("dead-before", 31) > 1000);
        Assert.True(c.RecordSeen("dies-later", 40));            // a state that turns true later is still fresh
        c.MarkOld("dies-later");                                // and marking old never overwrites a real time
        Assert.Equal(0.5, c.DaysSince("dies-later", 52), 3);
        Assert.Empty(c.OpenJobs());
    }

    [Fact]
    public void Never_seen_reads_as_very_old_and_is_not_a_job()
    {
        var c = new LwCore();
        Assert.True(c.DaysSince("unknown", 50) > 1000);
        c.RecordSeen("s", 10);
        Assert.Empty(c.OpenJobs());
        Assert.Equal("no open jobs", c.Summary(5));
    }

    [Fact]
    public void Seen_times_survive_save_and_load()
    {
        var a = new LwCore();
        a.RecordSeen("s", 48);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) a.Write(w);
        ms.Position = 0;
        var b = new LwCore();
        using (var r = new BinaryReader(ms)) b.Read(r);
        Assert.Equal(2.0, b.DaysSince("s", 96), 3);
    }
}
