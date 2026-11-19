using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation.Population;
using Meitou.Simulation.Factions;
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
    public void A_crew_pulled_the_wrong_way_off_screen_still_closes_in()
    {
        // seen in play (build au): crews off screen drifted away from a squad that waited two game hours. Here something moves the stand-in
        // 40 m away from the squad before every pass, as the engine's own travel did; the crew must still get nearer.
        var (world, hunters) = World(Data());
        Assert.True(hunters.Request(Town, "test"));
        world.RunTicks(301);
        var crew = Assert.Single(Crews(world));
        Assert.Equal(Meitou.Simulation.Factions.PlatoonState.Unloaded, crew.State);
        float start = Vector2.Distance(crew.Position, Vector2.Zero);
        for (int pass = 0; pass < 20; pass++)
        {
            crew.Position += Vector2.Normalize(crew.Position) * 400f;   // dragged away from the squad (at 0, 0)
            world.RunTicks(300);
        }
        float end = Vector2.Distance(crew.Position, Vector2.Zero);
        Assert.True(end < start - 3000f, $"crew went from {start:0} to {end:0}");   // 20 passes at walking pace: about 5000 nearer
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
        var restored = new Platoon
        {
            Id = world2.Platoons.NextId(), Origin = dead.Origin, TemplateId = Crew, Faction = dead.Faction,
            Key = dead.Key, Size = dead.Size, Position = dead.Position, Dead = true,
        };
        if (typeof(Platoon).GetProperty("ModTag") is { } tag) tag.SetValue(restored, tag.GetValue(dead));
        world2.Platoons.Add(restored);
        world2.RunTicks(301);
        Assert.Empty(loaded.Crews);
        Assert.Single(Crews(world2));                                     // nothing placed again beside the dead one
    }

    /// <summary>A crew sent and saved before it ever loaded: missing from the reloaded world, as in play (Meitou saves a squad once it loads).</summary>
    static (SimWorld World, HuntersSystem Hunters) Reloaded()
    {
        var data = Data();
        var (world, hunters) = World(data);
        hunters.Request(Town, "test");
        world.RunTicks(301);
        Assert.Single(Crews(world));
        var saved = new MemoryStream();
        hunters.Write(saved);
        saved.Position = 0;
        var pop = new PopulationSystem(data);
        var loaded = new HuntersSystem(Crew, pop, data);
        loaded.Read(saved);
        var world2 = W.World(pop, loaded);
        world2.GameHours = world.GameHours;                               // a save brings its clock back
        W.Spawn(world2, "Leaf", W.Player, new Vector3(0, 150, 0));
        return (world2, loaded);
    }

    [Fact]
    public void Told_a_save_has_loaded_a_crew_the_save_left_out_is_placed_on_the_first_tick()
    {
        var (world, _) = Reloaded();                                      // control: no signal, nothing until the next pass (tick 200)
        world.RunTicks(30);
        Assert.Empty(Crews(world));

        var (world2, _) = Reloaded();
        WorldReady.Ready(world2, true);                                   // the engine's IWorldReady.Ready(world, fromSave: true)
        world2.RunTicks(30);
        Assert.Single(Crews(world2));
        world2.RunTicks(400);                                             // and the usual pass doesn't place a second one
        Assert.Single(Crews(world2));
    }

    [Fact]
    public void The_emitted_system_forwards_the_engines_world_ready()
    {
        var observer = WorldReady.Find();
        if (observer is null) return;                                     // Meitou before 0.21
        var type = ObserverEmit.Build(observer, "Ready", typeof(WorldReady).GetMethod(nameof(WorldReady.Ready))!, "WorldReadySystem");
        Assert.NotNull(type);
        var (world, _) = Reloaded();
        observer.GetMethod("Ready")!.Invoke(Activator.CreateInstance(type!), [world, true]);
        world.RunTicks(30);
        Assert.Single(Crews(world));
    }

    [Fact]
    public void Two_crews_after_a_load_each_find_their_own_platoon()
    {
        // from a review of the code: crews share one template and were matched one at a time to the nearest platoon, so a crew whose platoon
        // was missing (never loaded, not saved) could take the other crew's, and that crew was placed again beside its own
        var data = Data();
        var (world, hunters) = World(data);
        hunters.Request(Town, "test");
        world.RunTicks(301);
        var survivor = Assert.Single(Crews(world));                       // Squin's crew, saved by the engine
        var sent = Assert.Single(hunters.Crews).SentHours;
        var far = survivor.Position + new Vector2(0, 40000);              // Stack's crew, last seen 4 km further off, never loaded

        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(1); w.Write(2);
        w.Write("Stack"); w.Write(sent); w.Write(far.X); w.Write(far.Y);  // first in the list: it used to grab Squin's platoon
        w.Write(Town); w.Write(sent); w.Write(survivor.Position.X); w.Write(survivor.Position.Y);
        w.Write(0);
        w.Flush(); ms.Position = 0;

        var pop = new PopulationSystem(data);
        var loaded = new HuntersSystem(Crew, pop, data);
        loaded.Read(ms);
        var world2 = W.World(pop, loaded);
        world2.GameHours = world.GameHours;
        W.Spawn(world2, "Leaf", W.Player, new Vector3(0, 150, 0));
        world2.Platoons.Add(new Meitou.Simulation.Factions.Platoon
        {
            Id = world2.Platoons.NextId(), Origin = survivor.Origin, TemplateId = Crew, Faction = survivor.Faction, Key = survivor.Key, Size = survivor.Size,
            Position = survivor.Position,
        });
        world2.RunTicks(301);
        var crews = Crews(world2);
        Assert.Equal(2, crews.Count);
        Assert.Contains(crews, p => Vector2.Distance(p.Position, far) < 3000);            // Stack's placed again where it was
        Assert.Single(crews, p => Vector2.Distance(p.Position, survivor.Position) < 3000);  // and Squin's not doubled
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_saved_crew_is_not_confused_with_a_different_towns_missing_crew(bool survivorDied)
    {
        var tag = typeof(Platoon).GetProperty("ModTag");
        if (tag is null) return; // Without tags, old saves continue to use the checked proximity fallback.
        var data = Data();
        var (first, hunters) = World(data);
        hunters.Request(Town, "first test crew");
        first.RunTicks(301);
        var actualSquin = Assert.Single(Crews(first));
        hunters.Request("Stack", "second test crew");
        first.RunTicks(300);
        Assert.Equal(2, hunters.Crews.Count);
        var stack = hunters.Crews.Single(c => c.Town == "Stack");

        using var save = new MemoryStream();
        hunters.Write(save);
        save.Position = 0;
        var (next, loaded) = World(data);
        next.GameHours = first.GameHours;
        loaded.Read(save);
        // The only surviving engine record belongs to Squin even though it is now at Stack's last point.
        var restoredSquin = new Platoon { Id = next.Platoons.NextId(), Origin = actualSquin.Origin, TemplateId = Crew,
            Faction = actualSquin.Faction, Key = actualSquin.Key, Size = actualSquin.Size,
            Position = new Vector2(stack.X, stack.Z), Dead = survivorDied };
        tag.SetValue(restoredSquin, tag.GetValue(actualSquin));
        var originalTag = tag.GetValue(restoredSquin);
        next.Platoons.Add(restoredSquin);
        next.RunTicks(301);

        if (survivorDied)
            Assert.DoesNotContain(loaded.Crews, c => c.Town == Town);
        else
        {
            var row = Assert.Single(loaded.Crews, c => c.Town == Town);
            Assert.InRange(Vector2.Distance(restoredSquin.Position, new Vector2(row.X, row.Z)), 0, 150);
        }
        Assert.Contains(loaded.Crews, c => c.Town == "Stack");
        Assert.Equal(originalTag, tag.GetValue(restoredSquin));
        Assert.Single(Crews(next), p => p.Id != restoredSquin.Id);
    }
}
