using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Factions;
using Xunit;

public sealed class HiredBladesWorldTests
{
    [Fact]
    public void Only_bodyguards_on_the_players_contract_count_as_hired_blades()
    {
        using var world = W.World();
        var at = new Vector3(10000, 150, 10000);
        var player = W.Spawn(world, "Leaf", W.Player, at);
        var other = W.Spawn(world, "Merchant", W.Bandits, at);
        CharacterCold Contract(int signal, CharacterId target)
        {
            var board = new Blackboard([]);
            var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "bodyguards", Faction = 2, Ai = board };
            world.Squads.Add(squad);
            var actor = W.Spawn(world, "Bodyguard", squad.Faction, at, squad: squad.Id);
            var job = new AiPackage { Id = "test-hire", Name = "Hire", Signal = signal, Roles = [[], [], [], [], []] };
            board.StartContract(job, world.GameHours, 24, target: target);
            world.RunTicks(2);
            return world.Characters.Cold(actor.Slot)!;
        }

        Assert.False(LwDeedsSystem.IsHired(world, Contract(12, other)));
        Assert.True(LwDeedsSystem.IsHired(world, Contract(12, player)));
        Assert.False(LwDeedsSystem.IsHired(world, Contract(12, CharacterId.None)));
        Assert.False(LwDeedsSystem.IsHired(world, Contract(12, new CharacterId(player.Slot, player.Generation + 1))));
    }
}
