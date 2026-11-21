using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Population;
using Xunit;

[Collection("settings")]
public sealed class TownHandoverWorldTests
{
    static readonly Vector3 Gate = new(10000, 150, 10000);
    static readonly Vector3 FarGate = new(210000, 150, 210000);

    static PopulationData Map()
    {
        FcsRecord Town(string id, string faction)
        {
            var town = W.Rec(id, FcsRecordType.Town, "Stack");
            town.Ints["type"] = (int)TownType.Town;
            town.References["faction"] = [new(faction, 0, 0, 0)];
            return town;
        }
        return W.Data([Town("home", "60-t"), Town("other", "60-t"), Town("version", "shek"), W.Rec("shek", FcsRecordType.Faction, "Shek Kingdom")],
            [new("home-placement", "home", Gate, null), new("other-placement", "other", FarGate, null)]);
    }

    [Fact]
    public void Reports_and_couriers_use_the_exact_town_id_and_older_news_loads()
    {
        var data = Map();
        var news = new NewsSystem(data);
        using var world = W.World(news);
        world.RunTicks(2);
        Assert.True(WastelandApi.ReportAtNode("town:home-placement", "caravan-lost", "", "A caravan near Stack was lost.", 60, "test"));
        Assert.True(WastelandApi.ReportAtNode("town:other-placement", "caravan-lost", "", "A caravan near Stack was lost.", 80, "test"));
        Assert.False(WastelandApi.ReportAtNode("town:missing", "caravan-lost", "", "", 90, "test"));
        Assert.Equal("other-placement", data.Sites[news.CourierOrigin(news.CourierReport(world.GameHours)!)].InstanceId);

        using var saved = new MemoryStream();
        using var w = new BinaryWriter(saved);
        w.Write(1); w.Write(2); w.Write(1);
        w.Write(1); w.Write("caravan-lost"); w.Write("Stack"); w.Write(""); w.Write("The old report."); w.Write("test");
        w.Write(world.GameHours); w.Write(FarGate.X); w.Write(FarGate.Z); w.Write(70); w.Write(false); w.Write(0d);
        saved.Position = 0;
        news.Read(saved);
        var legacy = news.CourierReport(world.GameHours)!;
        Assert.Empty(legacy.Node);
        Assert.Equal("other-placement", data.Sites[news.CourierOrigin(legacy)].InstanceId);
    }

    [Fact]
    public void Two_handover_stories_survive_reload_and_each_is_learned_once()
    {
        if (!TownSwitchWatch.Available) return;
        bool wasEnabled = LwMod.Settings.TownHandovers;
        LwMod.Settings.TownHandovers = true;
        try
        {
            var data = Map();
            var pop = new PopulationSystem(data, new PopulationSettings());
            var news = new NewsSystem(data);
            using var world = W.World(pop, news, BuildObserver());
            W.Spawn(world, "Leaf", W.Player, FarGate);
            world.RunTicks(2);
            int index = Enumerable.Range(0, data.Sites.Count).Single(i => data.Sites[i].InstanceId == "home-placement");

            Switch(pop, world, index, "version");
            Assert.Empty(news.TownHistory());
            var speaker = W.Spawn(world, "Local", W.Bandits, Gate);
            world.RunTicks(2);
            news.AnswerAsk(world, speaker);
            news.AnswerAsk(world, speaker);
            Assert.Contains("Shek Kingdom", Assert.Single(Assert.Single(news.TownHistory()).History));

            using var saved = new MemoryStream();
            news.Write(saved);
            saved.Position = 0;
            var restored = new NewsSystem(data);
            restored.Read(saved);
            var nextPop = new PopulationSystem(data, new PopulationSettings());
            using var next = W.World(nextPop, restored, BuildObserver());
            W.Spawn(next, "Leaf", W.Player, FarGate);
            next.GameHours = world.GameHours + 1;
            next.RunTicks(2);
            Switch(nextPop, next, index, null);
            Assert.Single(Assert.Single(restored.TownHistory()).History);
            var secondSpeaker = W.Spawn(next, "Other local", W.Bandits, Gate);
            next.RunTicks(2);
            restored.AnswerAsk(next, secondSpeaker);
            var entry = Assert.Single(restored.TownHistory());
            Assert.Equal("town:home-placement", entry.Node);
            Assert.Collection(entry.History, row => Assert.Contains("Dust Bandits", row), row => Assert.Contains("Shek Kingdom", row));
        }
        finally { LwMod.Settings.TownHandovers = wasEnabled; }
    }

    static ITickSystem BuildObserver()
    {
        var observer = typeof(World).Assembly.GetType("Meitou.Simulation.Population.ITownSwitchObserver")!;
        return (ITickSystem)Activator.CreateInstance(ObserverEmit.Build(observer, "TownSwitched",
            typeof(TownSwitchWatch).GetMethod(nameof(TownSwitchWatch.Changed))!, "TownSwitchTest")!)!;
    }

    static void Switch(PopulationSystem population, World world, int index, string? record) =>
        Assert.Equal(true, typeof(PopulationSystem).GetMethod("ApplyTownVersion")!.Invoke(population, [world, index, record]));
}
