using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation.Factions;
using Xunit;

// Fiefdom (and mods like it) give the player's faction townspeople in squads of their own, tagged with the mod's key: they are not the
// squad, so the squad record leaves them out (OtherModsPeople). TWR's own tag on a squad of the player's does not count.
public class OtherModsPeopleWorldTests
{
    [Fact]
    public void A_player_faction_squad_another_mod_tagged_is_not_the_squad_and_ours_is()
    {
        var world = W.World();
        int Squad(string? tagKey)
        {
            var squad = new Squad { Id = world.Squads.NextId(), Name = "s", TemplateId = "test", Faction = W.Player };
            world.Squads.Add(squad);
            if (tagKey is not null && typeof(Squad).GetProperty("ModTags")?.GetValue(squad) is IDictionary<string, string> tags) tags[tagKey] = "x";
            return squad.Id;
        }
        var mine = W.Spawn(world, "Zill", W.Player, Vector3.Zero, squad: Squad(null));
        var ourTag = W.Spawn(world, "Escorted", W.Player, Vector3.Zero, squad: Squad("LivingWastelandMeitou"));
        var resident = W.Spawn(world, "Resident", W.Player, Vector3.Zero, squad: Squad("fiefdom"));
        bool Runs(Meitou.Simulation.CharacterId id) => world.Characters.TryResolveNext(id, out int s) && OtherModsPeople.Runs(world, world.Characters.Cold(s)!);

        Assert.False(Runs(mine));
        Assert.False(Runs(ourTag));
        // clients before Squad.ModTags (0.21) have no way to tell: nobody is left out there
        Assert.Equal(typeof(Squad).GetProperty("ModTags") is not null, Runs(resident));
    }
}
