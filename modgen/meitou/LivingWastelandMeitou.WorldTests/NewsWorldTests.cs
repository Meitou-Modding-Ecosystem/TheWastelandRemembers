using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Xunit;
using SimWorld = Meitou.Simulation.World;

// News from other mods (News.cs): what the squad hears goes into History once. Proving Grounds both notes the squad's own arena wins
// (WastelandApi.Note) and reports them as news (Report) with the same words; seen in play as two identical History lines.
[Collection("settings")]
public class NewsHeardOnceWorldTests
{
    static readonly Vector3 Scratch = new(10000, 150, 10000);

    static SimWorld Town()
    {
        var town = W.Rec("town-scratch", FcsRecordType.Town, "Scratch");
        town.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        var data = W.Data([town], [new TownPlacement("i-scratch", "town-scratch", Scratch, null)]);
        var world = W.World(new LwDeedsSystem([], data), new NewsSystem(data));
        W.Spawn(world, "Knife", W.Player, Scratch + new Vector3(100, 0, 0));
        world.RunTicks(61);
        return world;
    }

    static int Lines(SimWorld world, string text) => LwDeeds.For(world)!.Record.History.Count(h => h.Text == text);

    [Fact]
    public void News_the_squads_record_already_has_is_not_noted_twice()
    {
        var world = Town();
        const string won = "Knife defeated Ressa Vane at Scratch.";
        Assert.True(WastelandApi.Note(won));                                                  // the arena notes the win itself
        Assert.True(WastelandApi.Report("arena-unique-beaten", "Scratch", "Bloodstamp", won, 65, "ProvingGroundsMeitou"));
        const string other = "Scratch's pit has a new champion.";
        Assert.True(WastelandApi.Report("arena-upset", "Scratch", "Bloodstamp", other, 30, "ProvingGroundsMeitou"));
        world.RunTicks(120);                                                                  // the squad is right there: it hears both
        Assert.Equal(1, Lines(world, won));
        Assert.Equal(1, Lines(world, other));                                                 // control: news it didn't have is noted
    }
}
