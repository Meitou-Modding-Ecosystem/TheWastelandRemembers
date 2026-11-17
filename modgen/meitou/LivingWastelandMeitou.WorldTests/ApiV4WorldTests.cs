using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Xunit;
using SimWorld = Meitou.Simulation.World;

// WastelandApi version 4: TWR's outlaws for other mods (Outlaws, HideoutStruck) and towns' stable ids (TownNode). One outlaw, Bad Pete,
// made in memory with his gang's squad template, in a synthetic world.
[Collection("settings")]
public class ApiV4WorldTests
{
    static (SimWorld World, OutlawCampsSystem Camps) Make()
    {
        var pete = W.Rec("pete-t", FcsRecordType.Character, "Bad Pete");
        pete.Ints["bounty amount"] = 3000;
        var gang = W.Rec("gang-t", FcsRecordType.SquadTemplate, "Pete's gang");
        gang.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        var town = W.Rec("town-squin", FcsRecordType.Town, "Squin");
        var data = W.Data([pete, gang, town], [new TownPlacement("i-squin", "town-squin", new Vector3(1000, 150, 2000), null)]);
        var camps = new OutlawCampsSystem([new CampCode(0, "Bad Pete", "pete-t", "gang-t", ["bg-north"])], null, data);
        var world = W.World(new LwDeedsSystem([], data), camps);
        W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        world.RunTicks(31);
        return (world, camps);
    }

    [Fact]
    public void Outlaws_lists_each_named_outlaw_with_plain_fields()
    {
        var (world, _) = Make();
        var pete = Assert.Single(WastelandApi.Outlaws());
        Assert.Equal("Bad Pete", pete["Name"]);
        Assert.Equal("Dust Bandits", pete["Faction"]);
        Assert.Equal("bg-north", pete["Regions"]);
        Assert.Equal(false, pete["HasCamp"]);
        Assert.Equal(false, pete["Known"]);
        Assert.Equal("alive", pete["Status"]);
        Assert.Equal("", pete["Successor"]);
        Assert.Contains((string)pete["Mood"], new[] { "raiding", "lying-low", "gathering", "wounded" });
        Assert.True((int)pete["Bounty"] >= 3000);
    }

    [Fact]
    public void A_camp_struck_by_another_mod_lies_low_and_sends_no_reprisal()
    {
        var (world, _) = Make();
        Assert.Equal("alive", WastelandApi.Outlaws()[0]["Status"]);
        Assert.True(WastelandApi.HideoutStruck("bad pete", "a Holy Nation war band", bloodied: true, "test"));
        Assert.Equal("wounded", WastelandApi.Outlaws()[0]["Mood"]);
        Assert.False(WastelandApi.HideoutStruck("Nobody", "anyone", false, "test"));
    }

    [Fact]
    public void A_town_has_its_stable_node_id()
    {
        Make();
        Assert.Equal("town:i-squin", WastelandApi.TownNode("squin"));
        Assert.Equal("", WastelandApi.TownNode("Nowhere"));
    }
}
