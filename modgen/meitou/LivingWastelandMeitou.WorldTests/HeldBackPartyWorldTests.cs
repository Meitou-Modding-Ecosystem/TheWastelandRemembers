using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;
using Xunit;

// Seen in the bv play test (Meitou 0.24.0): a hunting rival stood still for game hours, then was "off screen" 25 m from the squad with nobody
// there, and Rebirth's caged prisoners filled the log with 2,776 cage changes.
[Collection("settings")]
public sealed class HeldBackPartyWorldTests
{
    [Fact]
    public void A_stand_in_the_game_holds_back_until_the_squad_leaves_is_not_walked_on()
    {
        var awaits = typeof(Platoon).GetProperty("AwaitsLeave");
        if (awaits is null) return;                                   // Meitou before 0.24 has no such hold
        var data = W.Data([]);
        using var world = W.World(new PopulationSystem(data));
        W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        var p = new Platoon { Id = world.Platoons.NextId(), Origin = -1, Key = 1, TemplateId = "t", Position = new Vector2(20000, 0), Persistent = true };
        world.Platoons.Add(p);
        awaits.SetValue(p, true);
        var lines = new List<string>();
        var log = LwMod.Log;
        try
        {
            LwMod.Log = lines.Add;
            for (int i = 0; i < 4; i++) Pursuit.Steer(world, p, [(0f, 0f)]);
            Assert.Equal(new Vector2(20000, 0), p.Position);                      // the old code walked it 25 m nearer each pass
            Assert.Single(lines, l => l.Contains("waits for the squad to leave"));
        }
        finally { LwMod.Log = log; }
    }

    [Fact]
    public void A_rival_is_steered_only_through_a_party_that_holds_them()
    {
        var data = W.Data([]);
        using var world = W.World(new PopulationSystem(data));
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "e", Faction = W.Bandits };
        world.Squads.Add(squad);
        var mate = world.Characters.Spawn(new CharacterHot { Alive = true, Position = new Vector3(100, 150, 0) },
            new CharacterCold { Name = "Hungry bandit", Faction = W.Bandits, RecordId = "r", Medical = new MedicalState([]), SquadId = squad.Id }, world.Tick);
        squad.Members.Add(mate);
        var rival = new RivalsSystem.Rival { Name = "Brenn Twice-Born", Slot = 0 };

        var without = new Platoon { Id = world.Platoons.NextId(), Origin = -1, Key = 1, TemplateId = "e", SquadId = squad.Id, State = PlatoonState.Loaded };
        world.Platoons.Add(without);
        Assert.Equal("is in the world without them", RivalsSystem.Holding(world, without, rival));

        var emptied = new Platoon { Id = world.Platoons.NextId(), Origin = -1, Key = 2, TemplateId = "e", Stored = new StoredSquad { Name = "e" } };
        world.Platoons.Add(emptied);
        Assert.Equal("is off screen without them", RivalsSystem.Holding(world, emptied, rival));
        emptied.Regenerate = true;
        Assert.StartsWith("is to be made afresh", RivalsSystem.Holding(world, emptied, rival));

        var holding = new Platoon { Id = world.Platoons.NextId(), Origin = -1, Key = 3, TemplateId = "e", Stored = new StoredSquad { Name = "e" } };
        holding.Stored.Members.Add(new StoredMember(new CharacterCold { Name = "Brenn Twice-Born", Medical = new MedicalState([]) }, Vector3.Zero, 0, true));
        world.Platoons.Add(holding);
        Assert.Null(RivalsSystem.Holding(world, holding, rival));
        Assert.Same(holding, RivalsSystem.FindHolding(world, rival));
    }

    [Fact]
    public void A_prisoner_flickering_in_and_out_of_a_cage_logs_a_few_lines_not_one_a_tick()
    {
        if (!LifecycleWatch.BuildWatches().TryGetValue("Held", out var adapter)) return;
        using var world = W.World((ITickSystem)Activator.CreateInstance(adapter)!);
        var id = W.Spawn(world, "Slowline", W.Bandits, Vector3.Zero);
        var captive = world.Characters.Cold(id.Slot)!;
        var log = LwMod.Log;
        bool logChanges = LwMod.Settings.LogChanges, diagnostics = LwMod.Settings.PlaytestDiagnostics;
        var lines = new List<string>();
        try
        {
            LwMod.Log = lines.Add;
            LwMod.Settings.LogChanges = true;
            LwMod.Settings.PlaytestDiagnostics = false;
            world.RunTicks(2);
            for (int i = 0; i < 20; i++)
            {
                captive.InSomething = i % 2 == 0 ? HeldState.Imprisoned : HeldState.None;
                captive.InWhat = i % 2 == 0 ? "cell" : null;
                world.RunTicks(3);
            }
            Assert.Equal(2, lines.Count(l => l.Contains("Slowline")));                      // the old code logged all 20
            world.RunTicks(700);
            captive.InSomething = HeldState.Imprisoned;
            captive.InWhat = "cell";
            world.RunTicks(3);
            Assert.Contains(lines, l => l.Contains("Slowline") && l.Contains("after 18 quick changes not logged"));
        }
        finally
        {
            LwMod.Log = log;
            LwMod.Settings.LogChanges = logChanges;
            LwMod.Settings.PlaytestDiagnostics = diagnostics;
        }
    }
}
