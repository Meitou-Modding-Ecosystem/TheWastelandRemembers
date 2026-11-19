using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;
using Xunit;
using SimWorld = Meitou.Simulation.World;

[Collection("settings")]
public sealed class RaidRestoreWorldTests : IDisposable
{
    const string RaidSquad = "test-raiders";
    static readonly RaidCode[] Codes = [new("flotsam", RaidSquad, "Dust Bandits", 1, 2, "Flotsam Ninjas")];
    readonly List<string> towns = OutlawCampsSystem.NewsTowns;
    readonly bool setting = LwMod.Settings.HarassmentRaids;

    public void Dispose()
    {
        OutlawCampsSystem.NewsTowns = towns;
        LwMod.Settings.HarassmentRaids = setting;
    }

    static PopulationData Data()
    {
        var squad = W.Rec(RaidSquad, FcsRecordType.SquadTemplate, "Raiders");
        squad.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        var town = W.Rec("t-squin", FcsRecordType.Town, "Squin");
        town.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        return W.Data([squad, town], [new TownPlacement("squin", "t-squin", new Vector3(50000, 150, 50000), null)]);
    }

    static (SimWorld World, RaidsSystem Raids) Scene(PopulationData data)
    {
        var raids = new RaidsSystem(Codes, new PopulationSystem(data), data);
        var world = W.World(raids);
        return (world, raids);
    }

    static IEnumerable<Platoon> Parties(SimWorld world) => world.Platoons.All.Where(p => p is not null && p.TemplateId == RaidSquad);

    [Fact]
    public void A_raid_missing_before_it_ever_loaded_is_restored_without_being_reported_caught()
    {
        OutlawCampsSystem.NewsTowns = ["Squin"];
        LwMod.Settings.HarassmentRaids = true;
        var data = Data();
        var (first, raids) = Scene(data);
        W.Spawn(first, "Leaf", W.Player, new Vector3(0, 150, 0));
        first.GameHours = RaidRules.NextStart(first.Seed, 0, 0) + 1;
        first.RunTicks(301);
        var party = Assert.Single(Parties(first));
        Assert.Equal(PlatoonState.Unloaded, party.State);
        Assert.Equal(1, raids.StateOf(first, 0));

        using var save = new MemoryStream();
        raids.Write(save);
        save.Position = 0;
        var (next, loaded) = Scene(data);
        loaded.Read(save);
        next.GameHours = first.GameHours;
        // Engine saves omit the off-screen party that never loaded. A nearby player initially blocks its old position.
        var scout = W.Spawn(next, "Scout", W.Player, new Vector3(party.Position.X, 150, party.Position.Y));
        next.RunTicks(301);
        Assert.Equal(1, loaded.StateOf(next, 0));
        Assert.Empty(Parties(next));

        // A later pass can restore it once without restarting the raid's clock.
        next.Characters.Cold(scout.Slot)!.IsPlayer = false;
        W.Spawn(next, "Leaf", W.Player, new Vector3(0, 150, 0));
        next.RunTicks(300);
        var restored = Assert.Single(Parties(next));
        Assert.Equal(1, loaded.StateOf(next, 0));
        next.RunTicks(300);
        Assert.Single(Parties(next));

        restored.Dead = true;
        next.RunTicks(300);
        Assert.Equal(2, loaded.StateOf(next, 0));
        Assert.Single(Parties(next));
    }
}
