using LivingWasteland.MeitouMod;
using Xunit;

// Seen in bv: one Starving Vagrant told every local rumour, then, asked for news, said "Haven't heard a thing".
public class GossipMemoryTests
{
    [Fact]
    public void One_person_tells_at_most_two_rumours_a_day()
    {
        var g = new GossipMemory();
        Assert.True(g.MayTell("v", 10));
        g.Told("v", "Squin", "Borun Vahl hit a caravan.", 10);
        g.Told("v", "Squin", "Sayo's gang got mauled.", 11);
        Assert.False(g.MayTell("v", 12));
        Assert.True(g.MayTell("someone else", 12));
        Assert.True(g.MayTell("v", 36));
    }

    [Fact]
    public void A_repeated_rumour_counts_as_another_telling()
    {
        var g = new GossipMemory();
        const string rumour = "Borun Vahl hit a caravan.";
        g.Told("v", "Squin", rumour, 10);
        g.Told("v", "Squin", rumour, 17);
        Assert.False(g.MayTell("v", 18));
        Assert.Equal(rumour, g.LastBy("v", 18));
        Assert.Equal(rumour, g.FromTown("Squin", 18, 1));
        Assert.True(g.MayTell("v", 34.1));
    }

    [Fact]
    public void Dialogue_gossip_gate_checks_the_window_without_allocating()
    {
        var g = new GossipMemory();
        g.Told("v", null, "Road's bad.", 10);
        g.Told("v", null, "Hungry out here.", 11);
        for (int i = 0; i < 1000; i++) g.MayTell("v", 34);

        int allowed = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            if (g.MayTell("v", 34)) allowed++;
            if (g.MayTell("v", 34.01)) allowed++;
            if (g.MayTell("v", 10)) allowed++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(20000, allowed);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Asked_for_news_they_repeat_what_they_told_or_what_the_town_says()
    {
        var g = new GossipMemory();
        g.Told("v", "Squin", "Sayo's people are holed up south-west of Admag.", 10);
        Assert.Equal("Like I said, Sayo's people are holed up south-west of Admag.", NewsAskRules.Fallback(g.LastBy("v", 11), null, 0));
        Assert.Equal("Folk round here say Sayo's people are holed up south-west of Admag.",
            NewsAskRules.Fallback(g.LastBy("guard", 11), g.FromTown("Squin", 11, 0), 0));
        Assert.Null(NewsAskRules.Fallback(g.LastBy("v", 30), g.FromTown("Squin", 70, 0), 0));
        Assert.Null(g.FromTown("Admag", 11, 0));
    }
}
