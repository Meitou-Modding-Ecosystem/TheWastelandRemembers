using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Xunit;

[Collection("settings")]
public sealed class PlaytestLogWorldTests
{
    [Fact]
    public void Custody_and_companion_news_share_a_timeline_across_a_save_without_changing_world_state()
    {
        var settings = LwMod.Settings;
        var before = (settings.PlaytestDiagnostics, settings.PrisonerCases, settings.OtherModsNews);
        var logger = PlaytestLog.EngineLog;
        var mirrored = new List<string>();
        long start = PlaytestLog.Read().Events.LastOrDefault()?.Number ?? 0;
        try
        {
            settings.PlaytestDiagnostics = false;
            settings.PrisonerCases = settings.OtherModsNews = true;
            PlaytestLog.EngineLog = mirrored.Add;
            var at = PrisonerDealsWorldTests.At;
            var town = W.Rec("test-town", FcsRecordType.Town, "Test Town");
            town.References["faction"] = [new("60-t", 0, 0, 0)];
            var data = W.Data([town], [new TownPlacement("test-town-location", town.StringId, at, null)]);
            var state = new LwSystem(null, null, data);
            var cases = new PrisonerCases(data, []);
            var news = new NewsSystem(data);
            using var world = W.World(state, new LwDeedsSystem([], data), news, cases);
            PlaytestLog.Event(world, "test", "off", "should not be recorded");
            settings.PlaytestDiagnostics = true;
            ulong hashBeforeTrace = world.StateHash();
            PlaytestLog.Event(world, "test", "enabled", "diagnostic only");
            Assert.Equal(hashBeforeTrace, world.StateHash());
            var (_, captive) = PrisonerDealsWorldTests.Actors(world, PrisonerCases.WesternQueen);
            PrisonerDealsWorldTests.Hold(world, captive);
            world.RunTicks(150);
            const string report = "The road opened near Test Town.";
            Assert.True(news.Report("road-opened", town.Name, "Dust Bandits", report, 70, "test companion"));
            world.RunTicks(120);
            PlaytestLog.Control("", "player marked test point");
            using var save = PrisonerDealsWorldTests.Save(state);

            var restored = new LwSystem(null, null, data);
            restored.Read(save);
            using var loaded = W.World(restored);
            WorldReady.Ready(loaded, true);

            var rows = PlaytestLog.Read().Events.Where(e => e.Number > start).ToArray();
            Assert.DoesNotContain(rows, row => row.Key == "off");
            var custody = Assert.Single(rows, row => row.System == "prisoner case" && row.Phase == "recorded");
            var heard = Assert.Single(rows, row => row.System == "news" && row.Phase == "heard" && row.Detail.Contains(report));
            var reload = Assert.Single(rows, row => row.Phase == "save loaded");
            Assert.Equal(custody.World, heard.World);
            Assert.NotEqual(heard.World, reload.World);
            Assert.True(custody.Tick < heard.Tick);
            Assert.True(heard.Number < reload.Number);
            Assert.Contains(rows, row => row.Phase == "save section written");
            Assert.Contains(rows, row => row.Phase == "save section read");
            Assert.Contains(mirrored, line => line.Contains("\theard\t", StringComparison.Ordinal));
            Assert.Contains(mirrored, line => line.Contains("\tplayer marked test point\t", StringComparison.Ordinal));

            string export = PlaytestLog.Report("Synthetic squad story");
            Assert.Contains("SESSION TIMELINE", export);
            Assert.Contains(report, export);
            Assert.Contains("Synthetic squad story", export);
        }
        finally
        {
            (settings.PlaytestDiagnostics, settings.PrisonerCases, settings.OtherModsNews) = before;
            PlaytestLog.EngineLog = logger;
        }
    }
}
