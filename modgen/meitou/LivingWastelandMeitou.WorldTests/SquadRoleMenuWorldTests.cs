using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class SquadRoleMenuWorldTests
{
    [Fact]
    public void An_escort_choice_keeps_the_target_shown_when_the_menu_was_drawn()
    {
        if (!SquadRoles.Available) return;
        static SquadRoles.Row Row(int id, string label) =>
            new(id, label, 1, false, 0, null, null, null, CharacterId.None, -1);
        var escort = Row(84000, "Escort squad");
        var first = Row(84001, "The old first option");
        var intended = Row(84002, "The intended squad");
        var other = Row(84003, "Another squad");
        var prior = SquadRoles.Squads;
        var priorContext = SquadRoles.Context;
        try
        {
            SquadRoles.Context = null;
            SquadRoles.Squads = [escort, first, intended, other];
            var option = Assert.Single(SquadRoles.Choices(escort.Id), row => row.Label == "Escort The intended squad");
            SquadRoles.Squads = [escort, intended, other];

            SquadRoles.SetRole(escort.Id, option.Index);

            Assert.StartsWith("Escort The intended squad", SquadRoles.RoleLabel(escort));
        }
        finally
        {
            SquadRoles.Squads = prior;
            SquadRoles.Context = priorContext;
        }
    }
}
