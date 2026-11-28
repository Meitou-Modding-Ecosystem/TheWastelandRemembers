using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;
using Xunit;

[Collection("settings")]
public sealed class OutlawCampArrivalWorldTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_camp_changes_stretch_without_jumping_to_a_distant_part_of_the_map(bool nearby)
    {
        var data = W.Data([]);
        var pop = new PopulationSystem(data);
        using var world = W.World(pop);
        world.GameHours = 300;
        var code = new CampCode(0, "Fixture outlaw", "outlaw-t", "camp-t", []);
        var camps = new OutlawCampsSystem([code], pop, data);
        const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
        var book = (CampBook)typeof(OutlawCampsSystem).GetField("book", instance)!.GetValue(camps)!;
        var cells = (Dictionary<int, List<(int X, int Z)>>)typeof(OutlawCampsSystem).GetField("cellsOf", instance)!.GetValue(camps)!;
        cells[0] = nearby ? [(33, 33)] : [(53, 53)];
        var start = new Vector2(2000, 2000);
        book.Set(0, new CampEntry(start.X, start.Y, -100, OutlawNews.Raiding));
        var party = new Platoon { Id = world.Platoons.NextId(), Origin = -1, Key = 1, TemplateId = code.Squad, Persistent = true, Position = start };
        world.Platoons.Add(party);

        typeof(OutlawCampsSystem).GetMethod("Pass", instance)!.Invoke(camps, [world, pop, data]);

        if (nearby) Assert.NotEqual(start, party.Position);
        else Assert.Equal(start, party.Position);
        Assert.InRange(Vector2.Distance(start, party.Position), 0, 20000);
        var current = book.Get(0)!;
        Assert.NotEqual(-100, current.Period);
        Assert.Equal(new Vector2(current.X, current.Z), party.Position);
    }

    [Fact]
    public void A_remote_camp_does_not_follow_the_template_trip_and_loads_at_its_marker()
    {
        var race = W.Rec("race-t", FcsRecordType.Race, "Testers");
        race.Ints["speed min skill"] = 70;
        race.Ints["speed max skill"] = 120;
        race.Floats["walk speed"] = 15;
        var outlaw = W.Rec("outlaw-t", FcsRecordType.Character, "Fixture outlaw");
        outlaw.Bools["unique"] = true;
        outlaw.References["race"] = [new FcsReference(race.StringId, 100, 0, 0)];
        var package = W.Rec("travel-t", FcsRecordType.AiPackage, "Travel to town");
        package.Ints["unloaded func"] = 5;
        var camp = W.Rec("camp-t", FcsRecordType.SquadTemplate, "Fixture camp");
        camp.References["leader"] = [new FcsReference(outlaw.StringId, 1, 0, 0)];
        camp.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        camp.References["AI packages"] = [new FcsReference(package.StringId, 1, 0, 0)];
        var town = W.Rec("town-t", FcsRecordType.Town, "Fixture town");
        town.Ints["type"] = 2;
        town.Floats["size radius"] = 300;
        town.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        var data = W.Data([race, outlaw, package, camp, town],
            [new TownPlacement("i-town", town.StringId, new Vector3(-35000, 150, 2000), null)]);
        var pop = new PopulationSystem(data);
        using var world = W.World(pop);
        var at = new Vector2(2000, 2000);
        var walker = W.Spawn(world, "Leaf", W.Player, new Vector3(46000, 150, 2000));
        var make = typeof(OutlawCampsSystem).GetMethod("Make", BindingFlags.Static | BindingFlags.NonPublic)!;
        var code = new CampCode(0, outlaw.Name, outlaw.StringId, camp.StringId, []);
        var party = Assert.IsType<Platoon>(make.Invoke(null, [world, pop, data, code, (at.X, at.Y), OutlawNews.LyingLow]));
        Assert.Equal(package.StringId, party.Package);
        Assert.Equal(PlatoonState.Unloaded, party.State);
        world.RunTicks(180);
        Assert.Equal(at, party.Position);

        // Arrive in one tick as a map teleport would, then allow native activation dwell and character creation.
        world.Characters.Place(walker.Slot, new Vector3(at.X, 150, at.Y), 0);
        // Stop on activation. Subsequent loaded movement is outside this fixture and needs physics on older clients.
        for (int i = 0; i < 450 && party.State != PlatoonState.Loaded; i++) world.RunTicks(1);
        Assert.Equal(PlatoonState.Loaded, party.State);
        var squad = world.Squads.Find(party.SquadId)!;
        var leader = world.Characters.Cold(squad.Leader.Slot)!;
        Assert.Equal(outlaw.StringId, leader.RecordId);
    }
}
