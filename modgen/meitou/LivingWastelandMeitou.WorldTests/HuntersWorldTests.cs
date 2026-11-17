using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation.Population;
using Xunit;
using SimWorld = Meitou.Simulation.World;

// Bounty crews made for real: the game's population system in a synthetic world (a race, a crewman, the crew's squad template, one town),
// so a crew is planned by the engine's SquadFactory and added as a platoon, as in play. The recipe follows MeitouClient's own population
// tests (SyntheticTown): records made in memory, no game files.
public class HuntersWorldTests
{
    const string Crew = "crew-t", Town = "Squin";

    static readonly Vector3 TownAt = new(1000, 150, 2000);

    static FcsReference Ref(string id, int v0 = 0, int v1 = 0, int v2 = 0) => new(id, v0, v1, v2);

    static PopulationData Data()
    {
        var race = W.Rec("race-t", FcsRecordType.Race, "Testers");
        race.Ints["speed min skill"] = 70;
        race.Ints["speed max skill"] = 120;
        race.Floats["walk speed"] = 15;
        var man = W.Rec("man-t", FcsRecordType.Character, "Bounty hunter");
        man.References["race"] = [Ref("race-t", 100)];
        var crew = W.Rec(Crew, FcsRecordType.SquadTemplate, "Bounty crew");
        crew.References["leader"] = [Ref("man-t", 1)];
        crew.References["squad"] = [Ref("man-t", 3)];
        crew.References["faction"] = [Ref("60-t")];
        var town = W.Rec("town-t", FcsRecordType.Town, Town);
        town.Ints["type"] = 2;
        town.Floats["size radius"] = 300;
        town.Floats["town radius mult"] = 1;
        town.References["faction"] = [Ref("60-t")];
        return W.Data([race, man, crew, town], [new TownPlacement("i-squin", "town-t", TownAt, null)]);
    }

    static (SimWorld World, HuntersSystem Hunters) World(PopulationData data)
    {
        var pop = new PopulationSystem(data);
        var hunters = new HuntersSystem(Crew, pop, data);
        var world = W.World(pop, hunters);
        W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        return (world, hunters);
    }

    static List<Meitou.Simulation.Factions.Platoon> Crews(SimWorld world) =>
        world.Platoons.All.Where(p => p is not null && p.TemplateId == Crew).ToList()!;

    [Fact]
    public void A_crew_asked_for_by_another_mod_is_made_by_the_engine_and_hunts()
    {
        var (world, hunters) = World(Data());
        Assert.True(hunters.Request(Town, "unpaid loan"));
        world.RunTicks(301);                                              // one pass (tick 200 of every 300)

        var crew = Assert.Single(Crews(world));
        Assert.True(crew.Persistent);
        Assert.Equal(Town, Assert.Single(hunters.Crews).Town);
        float far = Vector2.Distance(crew.Position, Vector2.Zero);
        Assert.InRange(far, HunterRules.StartMin * 0.9f, HunterRules.StartMax * 1.1f);   // out of sight, 2.5 to 3.5 km off
        Assert.False(hunters.Request(Town, "again"));                     // one crew from a town at a time
    }

    [Fact]
    public void A_crew_killed_this_session_is_beaten_and_not_placed_again()
    {
        var (world, hunters) = World(Data());
        hunters.Request(Town, "unpaid loan");
        world.RunTicks(301);
        Assert.Single(Crews(world)).Dead = true;                         // the engine declares a squad dead when every member is
        world.RunTicks(300);
        Assert.Empty(hunters.Crews);
        Assert.Single(Crews(world));                                      // still the one dead platoon: no new crew made
    }

    [Fact]
    public void A_crew_killed_just_before_a_save_stays_dead_after_the_load()
    {
        // Meitou #860 (v0.20.2-nightly.20261117): squads of NPCs the player killed are saved, declared dead. A crew killed in the seconds
        // between its last pass and a save is still on our list, and is now in the save as a dead platoon: it must count as beaten,
        // not be placed again as if it had never loaded.
        var data = Data();
        var (world, hunters) = World(data);
        hunters.Request(Town, "unpaid loan");
        world.RunTicks(301);
        Assert.Single(Crews(world)).Dead = true;
        var saved = new MemoryStream();
        hunters.Write(saved);
        saved.Position = 0;

        var pop = new PopulationSystem(data);
        var loaded = new HuntersSystem(Crew, pop, data);
        loaded.Read(saved);
        var world2 = W.World(pop, loaded);
        W.Spawn(world2, "Leaf", W.Player, new Vector3(0, 150, 0));
        var dead = Crews(world).Single();
        world2.Platoons.Add(new Meitou.Simulation.Factions.Platoon
        {
            Id = world2.Platoons.NextId(), Origin = dead.Origin, TemplateId = Crew, Faction = dead.Faction, Key = dead.Key, Size = dead.Size, Position = dead.Position,
            Dead = true,
        });
        world2.RunTicks(301);
        Assert.Empty(loaded.Crews);
        Assert.Single(Crews(world2));                                     // nothing placed again beside the dead one
    }
}
