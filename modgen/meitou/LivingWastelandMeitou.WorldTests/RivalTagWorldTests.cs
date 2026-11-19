using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation.Factions;
using Xunit;

[Collection("settings")]
public sealed class RivalTagWorldTests
{
    [Fact]
    public void A_rivals_platoon_keeps_the_tag_written_by_another_mod()
    {
        var tag = typeof(Platoon).GetProperty("ModTag");
        if (tag is null) return;
        var rivals = new RivalsSystem(W.Data([]));
        var world = W.World(rivals);
        W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var squad = new Squad { Id = world.Squads.NextId(), Faction = W.Bandits, TemplateId = "other-party" };
        world.Squads.Add(squad);
        var actor = W.Spawn(world, "Hollin One-Arm", W.Bandits, new Vector3(5000, 150, 0), squad: squad.Id);
        squad.Members.Add(actor);
        squad.Leader = actor;
        var party = new Platoon { Id = world.Platoons.NextId(), Origin = 0, Key = 1, TemplateId = squad.TemplateId,
            Faction = W.Bandits, SquadId = squad.Id, Position = new Vector2(5000, 0) };
        world.Platoons.Add(party);
        tag.SetValue(party, "other-mod:party-47");

        rivals.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", 1, 5000, 0));
        world.RunTicks(301);
        Assert.Equal("other-mod:party-47", tag.GetValue(party));
        Assert.Equal(Vector2.Zero, party.Point);
    }
}
