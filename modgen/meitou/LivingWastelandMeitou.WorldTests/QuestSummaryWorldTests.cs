using System.Reflection;
using LivingWasteland.MeitouMod;
using Xunit;

[Collection("settings")]
public sealed class QuestSummaryWorldTests
{
    [Fact]
    public void The_quest_row_reads_only_the_last_published_tick()
    {
        var world = W.World(new LwSystem(null, null, null));
        world.RunTicks(60);
        var state = LwState.For(world);
        Assert.Equal("no open jobs", state.Summary());
        state.Core.Set("Quest Help Squin", 1);
        Assert.Equal("no open jobs", state.Summary());
        world.RunTicks(60);
        Assert.Contains("Help Squin", state.Summary());
    }

    [Fact]
    public void The_raid_row_reads_only_the_last_published_tick()
    {
        var raids = new RaidsSystem([new RaidCode("flotsam", "party", "The Holy Nation", 1, 2, "Flotsam Ninjas")], null, null);
        var world = W.World(raids);
        world.RunTicks(60);
        string prior = raids.Shown.Text;
        var list = (RaidRecord?[])typeof(RaidsSystem).GetField("last", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(raids)!;
        list[0] = new RaidRecord(0, 0, 0, 0);
        Assert.Equal(prior, raids.Shown.Text);
        world.RunTicks(60);
        Assert.NotEqual(prior, raids.Shown.Text);
    }
}
