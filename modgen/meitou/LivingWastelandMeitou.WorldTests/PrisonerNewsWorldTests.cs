using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Xunit;

[Collection("settings")]
public sealed class PrisonerNewsWorldTests
{
    static readonly Vector3 Here = new(10000, 150, 10000);

    [Fact]
    public void Prisoner_bulletins_reach_the_squad_when_only_custody_news_is_enabled()
    {
        var settings = LwMod.Settings;
        var previous = (settings.PrisonerCases, settings.OtherModsNews, settings.RememberOutposts, settings.TownHandovers);
        settings.PrisonerCases = true;
        settings.OtherModsNews = settings.RememberOutposts = settings.TownHandovers = false;
        try
        {
            var town = W.Rec("town-scratch", FcsRecordType.Town, "Scratch");
            town.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
            var data = W.Data([town], [new TownPlacement("i-scratch", "town-scratch", Here, null)]);
            using var world = W.World(new LwDeedsSystem([], data), new NewsSystem(data));
            W.Spawn(world, "Leaf", W.Player, Here);
            world.RunTicks(61);
            const string bulletin = "They took the outlaw alive at Scratch.";
            NewsSystem.For(world)!.ReportPrisoner("town:i-scratch", "Scratch", 1, "caught", bulletin, 45);
            world.RunTicks(181);

            Assert.Contains(LwDeeds.For(world)!.Record.History, entry => entry.Text == bulletin);
        }
        finally
        {
            (settings.PrisonerCases, settings.OtherModsNews, settings.RememberOutposts, settings.TownHandovers) = previous;
        }
    }
}
