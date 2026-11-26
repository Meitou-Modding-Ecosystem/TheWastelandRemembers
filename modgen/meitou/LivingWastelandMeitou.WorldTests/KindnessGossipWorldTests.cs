using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class KindnessGossipWorldTests
{
    [Fact]
    public void The_asked_townsperson_can_recall_a_deed_only_in_its_exact_home_town()
    {
        if (!KindnessWatch.Available) return;
        var oldVoices = KindnessVoices.Current;
        bool oldSetting = LwMod.Settings.KindnessRemembered;
        try
        {
            LwMod.Settings.KindnessRemembered = true;
            KindnessVoices.Load(AppContext.BaseDirectory, _ => { });
            var town = W.Rec("t-market", FcsRecordType.Town, "Market");
            town.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
            var here = new Vector3(10000, 150, 10000);
            var elsewhere = here + Vector3.UnitX * 60000;
            var data = W.Data([town], [
                new TownPlacement("home-market", town.StringId, here, null),
                new TownPlacement("other-market", town.StringId, elsewhere, null),
            ]);
            var news = new NewsSystem(data);
            var tracker = new LwDeedsSystem([], data);
            using var world = W.World(tracker, news);
            var neighbour = W.Spawn(world, "Ruka", W.Bandits, here);
            var local = W.Spawn(world, "Local", W.Bandits, here + Vector3.UnitX);
            var second = W.Spawn(world, "Another local", W.Bandits, here + Vector3.UnitZ);
            var far = W.Spawn(world, "Distant local", W.Bandits, elsewhere);
            world.GameHours = 150;
            world.RunTicks(2);
            ulong actor = LastingActors.Grant(world, neighbour);
            var book = tracker.Deeds.Kindness;
            book.Add(new KindnessDeed(actor, KindnessKind.Saved, "Ruka", "r-Ruka", "Dust Bandits", "the wilds", "Leaf", 100,
                default, true, false));
            book.SetHome(actor, new KindnessHome("Market", here.X, here.Z, 3500, 125, true, true));
            book.MarkReunited(actor, 220);
            var barks = (System.Collections.IDictionary)typeof(World)
                .GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            news.AnswerAsk(world, neighbour);
            news.AnswerAsk(world, far);
            news.AnswerAsk(world, local);
            news.AnswerAsk(world, second);
            Assert.Contains("patched up Ruka", barks[local]!.ToString());
            Assert.DoesNotContain("patched up Ruka", barks[second]!.ToString());
            Assert.DoesNotContain("patched up Ruka", barks[far]!.ToString());
            Assert.DoesNotContain("patched up Ruka", barks[neighbour]!.ToString());
            Assert.Empty(news.AtlasRows());
        }
        finally
        {
            KindnessVoices.Current = oldVoices;
            LwMod.Settings.KindnessRemembered = oldSetting;
        }
    }
}
