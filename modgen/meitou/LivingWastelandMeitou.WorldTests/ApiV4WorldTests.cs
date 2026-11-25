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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_invalid_amount_cannot_poison_a_towns_heat(bool forgive)
    {
        var (world, _) = Make();
        var heat = LwDeeds.For(world)!.Heat;
        heat.AddCrime("Squin", "Shek Kingdom", 12);

        Assert.False(forgive ? WastelandApi.Forgive("Shek Kingdom", double.NaN, "test")
            : WastelandApi.AddHeat("Squin", double.NaN, "test"));
        Assert.Equal(12d, heat.Here("Squin"));
        Assert.True(WastelandApi.Forgive("Shek Kingdom", 2, "test"));
        Assert.Equal(10d, heat.Here("Squin"));
    }

    // Version 6: when another mod may strike an outlaw (RivalryCore#59 decision 17). Unknown to the squad: no. Known, under 7 days since
    // the game began: too soon. After 7 days: yes; struck once by another mod: cooldown for 30 days (and wounded, when bloodied).
    [Fact]
    public void Strike_answers_follow_the_wait_the_cooldown_and_what_the_squad_knows()
    {
        var (world, _) = Make();
        LwState.Register(world, new LwState());                           // the store the game's main system registers (strike times live there)
        string atlas = RecordHost.AtlasList;
        try
        {
            Assert.Equal("not-known", WastelandApi.StrikeAnswer("Bad Pete"));
            RecordHost.AtlasList = "@@w:Bad Pete|";
            Assert.Equal("too-soon", WastelandApi.StrikeAnswer("bad pete"));
            Assert.Equal(CampRules.StrikeWaitHours, WastelandApi.StrikableAfter("Bad Pete"));
            world.GameHours = 8 * 24;
            Assert.Contains(WastelandApi.StrikeAnswer("Bad Pete"), new[] { "yes", "wounded" });   // mood is drawn; struck yet by no one
            Assert.True(WastelandApi.HideoutStruck("Bad Pete", "a war band", bloodied: false, "test"));
            Assert.Equal("cooldown", WastelandApi.StrikeAnswer("Bad Pete"));
            Assert.Equal(8 * 24 + CampRules.StrikeCooldownHours, WastelandApi.StrikableAfter("Bad Pete"), 0);
            Assert.Equal("unknown", WastelandApi.StrikeAnswer("Nobody"));
            Assert.Equal(-1, WastelandApi.StrikableAfter("Nobody"));
        }
        finally { RecordHost.AtlasList = atlas; }
    }
}
