using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation.Factions;
using Xunit;

[Collection("settings")]
public sealed class RoleCommandWorldTests
{
    [Fact]
    public void Choosing_an_autonomous_role_uses_the_engines_current_command_constructor()
    {
        if (!SquadRoles.Available) return;
        var world = W.World();
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "test", Faction = W.Player, Name = "Guard" };
        world.Squads.Add(squad);
        squad.Leader = W.Spawn(world, "Leaf", W.Player, Vector3.Zero, squad: squad.Id);
        squad.Members.Add(squad.Leader);
        world.RunTicks(1);

        SquadRoles.Refresh(world, new LwCore());
        Assert.False(Assert.Single(SquadRoles.Squads).Autonomous);
        // This is the same path as picking Guard home in the Squads tab.
        SquadRoles.SetRole(squad.Id, 1);
        SquadRoles.SetRole(squad.Id, 0);
    }
}
