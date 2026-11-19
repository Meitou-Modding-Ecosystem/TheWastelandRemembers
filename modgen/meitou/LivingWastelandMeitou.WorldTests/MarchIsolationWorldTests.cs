using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation.Population;
using Xunit;

[Collection("settings")]
public sealed class MarchIsolationWorldTests
{
    [Fact]
    public void Loading_another_world_does_not_award_a_march_fought_in_the_old_world()
    {
        var stack = new Vector3(10000, 150, 10000);
        var town = W.Rec("town-stack", FcsRecordType.Town, "Stack");
        town.References["faction"] = [new FcsReference("204-gamedata.base", 0, 0, 0)];
        var data = W.Data([town], [new TownPlacement("i-stack", "town-stack", stack, null)]);
        var firstDeeds = new LwDeedsSystem([], data);
        var secondDeeds = new LwDeedsSystem([], data);
        var first = W.World(firstDeeds);
        var second = W.World(secondDeeds);
        first.RunTicks(31);
        second.RunTicks(31);

        var oldWar = new WarManager { Faction = W.Bandits };
        oldWar.Actives.Add(new CampaignInstance { Faction = W.Bandits, Id = 0, CampaignId = "campaign", Target = CampaignTown.OfSite(0),
            Phase = 1, Reached = true, FoughtPlayer = true });
        var oldPopulation = new PopulationSystem(data);
        var wars = typeof(PopulationSystem).GetField("_wars", BindingFlags.Instance | BindingFlags.NonPublic)!;
        wars.SetValue(oldPopulation, new WarManager[] { oldWar });
        Marches.Watch(first, oldPopulation, data);

        Marches.Watch(second, new PopulationSystem(data), data);
        Assert.Equal(0, LwDeeds.For(second)!.Record.CrewStat("marches_broken"));
        Assert.DoesNotContain(LwDeeds.For(second)!.Record.History, h => h.Text.Contains("back from the walls"));
        oldWar.Actives.Clear();
        Marches.Watch(first, oldPopulation, data);
        Assert.Equal(1, LwDeeds.For(first)!.Record.CrewStat("marches_broken"));
    }
}
