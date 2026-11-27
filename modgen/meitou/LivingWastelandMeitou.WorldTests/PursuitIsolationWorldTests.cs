using System.Numerics;
using HarmonyLib;
using Meitou.Data.Gameplay.Ai;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using Xunit;

[Collection("settings")]
public class PursuitIsolationWorldTests
{
    static readonly List<(int Slot, Vector2 Goal)> Walks = [];
    static World? trackedWorld;

    static void RecordWalk(World __0, int __1, Vector2 __2)
    {
        if (ReferenceEquals(__0, trackedWorld)) Walks.Add((__1, __2));
    }

    [Fact]
    public void A_loaded_crew_without_a_challenge_line_still_walks_to_the_squad()
    {
        var data = W.Data([]);
        using var world = W.World(new AiSystem(data));
        var player = new Squad { Id = world.Squads.NextId(), TemplateId = "player", Faction = W.Player };
        world.Squads.Add(player);
        var at = new Vector3(250, 150, 400);
        player.Leader = W.Spawn(world, "Leaf", W.Player, at, squad: player.Id);
        player.Members.Add(player.Leader);
        var crew = new Squad { Id = world.Squads.NextId(), TemplateId = "crew", Faction = W.Bandits };
        world.Squads.Add(crew);
        crew.Leader = W.Spawn(world, "Hunter", W.Bandits, at + Vector3.UnitX * 100, squad: crew.Id);
        crew.Members.Add(crew.Leader);
        crew.Members.Add(W.Spawn(world, "Second hunter", W.Bandits, at + Vector3.UnitZ * 100, squad: crew.Id));
        for (int i = 0; i < crew.Members.Count; i++)
            world.Characters.Cold(crew.Members[i].Slot)!.Ai = new AiMind { Squad = crew, MemberIndex = i };
        var party = new Platoon { Id = world.Platoons.NextId(), Origin = 0, Key = 1, TemplateId = crew.TemplateId,
            Faction = W.Bandits, SquadId = crew.Id, Position = new Vector2(at.X + 100, at.Z), State = PlatoonState.Loaded };
        world.Platoons.Add(party);
        var target = new Vector2(at.X, at.Z);
        // On newer clients the engine's assault order can take precedence over a walking goal.
        // Observe the live Walk call while letting the engine make that choice.
        var walk = typeof(TaskStart).GetMethod(nameof(TaskStart.Walk),
            [typeof(World), typeof(int), typeof(Vector2), typeof(byte), typeof(TaskType), typeof(TaskSource)])!;
        var harmony = new Harmony("twr.test.pursuit-walk");
        harmony.Patch(walk, prefix: new HarmonyMethod(typeof(PursuitIsolationWorldTests), nameof(RecordWalk)));
        try
        {
            trackedWorld = world;
            Pursuit.Steer(world, party, [(at.X, at.Z)], bark: null);
            Assert.Equal("attacking", Pursuit.Stage(world, crew.Id));
            Assert.Equal(crew.Members.Select(id => (id.Slot, target)), Walks);
        }
        finally
        {
            trackedWorld = null;
            harmony.Unpatch(walk, HarmonyPatchType.Prefix, harmony.Id);
            Walks.Clear();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_named_pursuer_can_challenge_once_they_can_speak(bool carried)
    {
        using var world = W.World();
        var player = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "crew", Faction = W.Bandits };
        world.Squads.Add(squad);
        var rival = W.Spawn(world, "Hollin", W.Bandits, Vector3.UnitX * 100, squad: squad.Id);
        var backup = W.Spawn(world, "Backup", W.Bandits, Vector3.UnitX * 100, squad: squad.Id);
        squad.Members.Add(rival);
        squad.Members.Add(backup);
        squad.Leader = rival;
        var party = new Platoon { Id = world.Platoons.NextId(), Origin = 0, Key = 1, TemplateId = "crew", SquadId = squad.Id,
            Faction = W.Bandits, Position = new Vector2(100, 0), State = PlatoonState.Loaded };
        world.Platoons.Add(party);
        world.RunTicks(2);
        var actor = world.Characters.Cold(rival.Slot)!;
        var held = typeof(CharacterCold).GetProperty(nameof(CharacterCold.CarriedBy))!;
        var unconscious = typeof(MedicalState).GetProperty(nameof(MedicalState.Unconscious))!;
        if (carried) held.SetValue(actor, player);
        else unconscious.SetValue(actor.Medical, true);

        var barks = (System.Collections.IDictionary)typeof(World).GetField("_barks",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(world)!;
        void Approach() => Pursuit.Steer(world, party, [(0, 0)], "Remember me?", "Hollin", rivalCue: false);
        Approach();
        Assert.False(barks.Contains(rival));
        Assert.False(barks.Contains(backup));
        if (carried) held.SetValue(actor, CharacterId.None);
        else unconscious.SetValue(actor.Medical, false);
        Approach();
        Assert.True(barks.Contains(rival));
        Assert.Contains("Remember me?", barks[rival]!.ToString());
        barks.Clear();
        Approach();
        Assert.False(barks.Contains(rival));
    }

    [Fact]
    public void A_pursuer_in_a_new_world_does_not_move_from_the_old_platoons_coordinates()
    {
        var first = W.World();
        var second = W.World();
        static Platoon StandIn(Meitou.Simulation.World world, float x)
        {
            var platoon = new Platoon { Id = world.Platoons.NextId(), Origin = 0, TemplateId = "crew", Key = 1,
                Position = new Vector2(x, 0), State = PlatoonState.Unloaded };
            world.Platoons.Add(platoon);
            return platoon;
        }
        var old = StandIn(first, 1000);
        var reloaded = StandIn(second, 9000);
        Assert.Equal(old.Id, reloaded.Id);

        Pursuit.Steer(first, old, [(0, 0)]);
        Pursuit.Steer(second, reloaded, [(0, 0)]);
        Assert.Equal(new Vector2(9000, 0), reloaded.Position);
        Pursuit.Steer(second, reloaded, [(0, 0)]);
        Assert.Equal(new Vector2(8750, 0), reloaded.Position); // The pacing fallback still works across passes.
    }
}
