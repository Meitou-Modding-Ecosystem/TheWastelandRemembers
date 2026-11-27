using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;
using Xunit;

[Collection("settings")]
public sealed class LeaderStoryWorldTests
{
    const string Phoenix = "17225-Dialogue.mod";
    const string Town = "Blister Hill";
    static readonly Vector3 At = new(14000, 150, 14000);

    [Fact]
    public void An_unattributed_death_after_the_squads_last_hit_does_not_become_a_leader_story()
    {
        var settings = LwMod.Settings;
        bool old = settings.FallenLeaderStories;
        try
        {
            settings.FallenLeaderStories = true;
            var data = W.Data([W.Rec(Phoenix, FcsRecordType.Character, "Holy Lord Phoenix")]);
            var tracker = new LwDeedsSystem([], data);
            using var world = W.World(tracker);
            var leaf = W.Spawn(world, "Leaf", W.Player, At);
            var ruler = W.Spawn(world, "Holy Lord Phoenix", W.Bandits, At);
            world.Uniques.Bind(Phoenix, ruler);
            world.RunTicks(31);
            tracker.HitResolved(world, W.Hit(world, leaf, ruler));
            var medical = world.Characters.Cold(ruler.Slot)!.Medical!;
            typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!.SetValue(medical, true);
            world.RunTicks(61);

            Assert.Equal(1, tracker.Deeds.Record.Member("Leaf").Kills);
            Assert.DoesNotContain(tracker.Deeds.Record.History, h => h.Text == "The squad killed Holy Lord Phoenix.");
        }
        finally { settings.FallenLeaderStories = old; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_verified_player_custody_or_a_player_kill_becomes_a_leader_story(bool captured)
    {
        var settings = LwMod.Settings;
        var property = typeof(LwSettings).GetProperty("FallenLeaderStories");
        Assert.NotNull(property);
        bool before = (bool)property.GetValue(settings)!, cases = settings.PrisonerCases;
        try
        {
            property.SetValue(settings, true);
            settings.PrisonerCases = false; // verified leader custody works on its own
            var town = W.Rec("blister-hill", FcsRecordType.Town, Town);
            town.References["faction"] = [new FcsReference("holy", 0, 0, 0)];
            var data = W.Data([town, W.Rec(Phoenix, FcsRecordType.Character, "Holy Lord Phoenix"),
                W.Rec("holy", FcsRecordType.Faction, "The Holy Nation")],
                [new TownPlacement("town-instance", town.StringId, At, null)]);
            var tracker = new LwDeedsSystem([], data);
            var news = new NewsSystem(data);
            using var world = W.World(tracker, news, new PrisonerCases(data, []), new PopulationSystem(data));
            var leaf = W.Spawn(world, "Leaf", W.Player, At);
            int holy = data.FactionIndex("holy");
            var guard = new Squad { Id = world.Squads.NextId(), TemplateId = "holy-guard", Faction = holy };
            world.Squads.Add(guard);
            var ruler = W.Spawn(world, "Holy Lord Phoenix", holy, At, squad: guard.Id);
            guard.Leader = ruler;
            guard.Members.Add(ruler);
            guard.Members.Add(W.Spawn(world, "Guard", holy, At + Vector3.UnitX, squad: guard.Id));
            world.Uniques.Bind(Phoenix, ruler);
            world.RunTicks(31);
            Assert.Empty(tracker.Deeds.Record.History);
            if (captured)
            {
                var site = new BuildSite { Id = "player-cage", Record = "cage-building", Position = At, Materials = [], Complete = true, Owner = W.Player };
                world.Buildings.Add(site);
                var cage = new Fixture { Id = "cage", Building = site.Id, Kind = FixtureKind.Cage, Position = At, Owner = W.Player };
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                ((List<Fixture>)typeof(FixtureRegistry).GetField("_all", flags)!.GetValue(world.Fixtures)!).Add(cage);
                ((Dictionary<string, Fixture>)typeof(FixtureRegistry).GetField("_byId", flags)!.GetValue(world.Fixtures)!).Add(cage.Id, cage);
                Assert.True(world.Fixtures.Imprison(world, ruler, cage));
                if (typeof(PopulationSystem).GetMethod("SplitOnImprisonment", flags) is not null)
                    Assert.DoesNotContain(ruler, guard.Members);
            }
            else
            {
                var killed = world.Characters.Cold(ruler.Slot)!.Medical!;
                typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!.SetValue(killed, true);
                tracker.HitResolved(world, W.Hit(world, leaf, ruler));
            }
            world.RunTicks(360);

            string verb = captured ? "captured" : "killed";
            Assert.Single(tracker.Deeds.Record.History, row => row.Text == $"Word reached Blister Hill that the squad {verb} Holy Lord Phoenix.");
            using var save = new MemoryStream();
            tracker.Write(save);
            save.Position = 0;
            var restored = new LwDeedsSystem([], data);
            restored.Read(save);
            Assert.Single(restored.Deeds.Record.History, row => row.Text.Contains("Word reached Blister Hill"));
            // Two game weeks later the road report is still eligible. Its text has no calendar number.
            world.GameHours += 14 * 24;
            world.RunTicks(120);
            Assert.Contains(news.AtlasRows(), row => row.Row.Contains($"squad {verb} Holy Lord Phoenix"));
        }
        finally
        {
            property.SetValue(settings, before);
            settings.PrisonerCases = cases;
        }
    }
}
