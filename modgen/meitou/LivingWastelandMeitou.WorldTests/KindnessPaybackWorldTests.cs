using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Xunit;

[Collection("settings")]
public sealed class KindnessPaybackWorldTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_reunion_gives_one_nearby_report_or_a_small_gift_from_the_neighbours_own_purse(bool report)
    {
        if (!KindnessWatch.Available) return;
        var oldVoices = KindnessVoices.Current;
        bool oldNews = LwMod.Settings.OtherModsNews;
        int oldReach = LwMod.Settings.NewsReach;
        try
        {
            KindnessVoices.Load(AppContext.BaseDirectory, _ => { });
            LwMod.Settings.OtherModsNews = true;
            LwMod.Settings.NewsReach = 1;
            var town = W.Rec("t-stack", FcsRecordType.Town, "Stack");
            var data = W.Data([town], [new TownPlacement("i-stack", town.StringId, new Vector3(15500, 150, 0), null)]);
            var news = new NewsSystem(data);
            var tracker = new LwDeedsSystem([], data);
            using var world = W.World(tracker, news);
            W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "travellers", Faction = W.Bandits, Money = 100 };
            world.Squads.Add(squad);
            var whom = W.Spawn(world, "Ruka", W.Bandits, Vector3.UnitX, squad: squad.Id);
            squad.Members.Add(whom);
            world.GameHours = 151;
            world.RunTicks(2);
            if (report)
                Assert.True(news.Report("caravan-lost", "Stack", "", "A caravan went missing outside Stack.", 75, "test ledger"));
            ulong id = LastingActors.Grant(world, whom);
            var book = tracker.Deeds.Kindness;
            book.Add(new KindnessDeed(id, KindnessKind.Saved, "Ruka", "r-Ruka", "Dust Bandits", "Stack", "Leaf", 100,
                default, true, false));
            book.SetHome(id, new KindnessHome("Market", 0, 0, 3500, 132, true, true));
            long wallet = world.Player.Money;
            world.RunTicks(151);
            var barks = (System.Collections.IDictionary)typeof(World).GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            if (report) Assert.Contains("A caravan went missing", barks[whom]!.ToString());
            Assert.Equal(report ? 1 : 0, news.AtlasRows().Count());
            Assert.Equal(wallet + (report ? 0 : 30), world.Player.Money);
            Assert.Equal(report ? 100L : 70L, squad.Money);

            world.GameHours += 120;
            world.RunTicks(301);
            Assert.Equal(wallet + (report ? 0 : 30), world.Player.Money);
            Assert.Equal(report ? 100L : 70L, squad.Money);
        }
        finally
        {
            KindnessVoices.Current = oldVoices;
            LwMod.Settings.OtherModsNews = oldNews;
            LwMod.Settings.NewsReach = oldReach;
        }
    }
}
