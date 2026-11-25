using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.World;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;
using Xunit;

[Collection("display cadence")]
public sealed class AtlasPublishingWorldTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_index_stays_current_and_existing_details_follow_the_selected_cadence(bool lighter)
    {
        var settings = LwMod.Settings;
        var previous = (settings.Atlas, settings.LighterChecks, settings.TownHandovers);
        var before = (RecordHost.AtlasList, RecordHost.AtlasPages, RecordHost.AtlasPortraits);
        try
        {
            settings.Atlas = true;
            settings.LighterChecks = lighter;
            settings.TownHandovers = false;
            var at = new Vector3(14000, 150, 14000);
            var town = W.Rec("squin", FcsRecordType.Town, "Squin");
            town.References["faction"] = [new("60-t", 0, 0, 0)];
            var replacement = W.Rec("squin-new", FcsRecordType.Town, "Squin");
            replacement.References["faction"] = [new("neighbours", 0, 0, 0)];
            var data = W.Data([town, replacement, W.Rec("neighbours", FcsRecordType.Faction, "Neighbours")],
                [new("in-squin", "squin", at, null)]);
            var atlas = new AtlasSystem(new AtlasTable(new() { ["Squin"] = new("Stenn Desert", "On the trade road.") },
                new() { ["Guard"] = new(Epithet: "guard", Place: "Squin") }), null, data);
            using var world = W.World(atlas);
            world.Player.Know(0, TownKnowledge.Discovered);
            world.Player.Meet(data.FactionIndex("60-t"));
            world.Player.Meet(data.FactionIndex("neighbours"));
            world.RunTicks(8);
            string place = RecordHost.AtlasPages["p:Squin"];
            Assert.Contains("Held by Dust Bandits.", place);
            var replace = typeof(PopulationData).GetMethod("SetReplacement");
            replace?.Invoke(data, [0, TownData.From(data.Db.Find("squin-new")!)]);
            world.Player.Know(0, TownKnowledge.Explored);
            world.RunTicks(60);
            Assert.Contains("Squin  (visited", RecordHost.AtlasList);
            Assert.Equal(lighter, ReferenceEquals(place, RecordHost.AtlasPages["p:Squin"]));
            world.RunTicks(60);
            Assert.Contains("You have been there.", RecordHost.AtlasPages["p:Squin"]);
            if (replace is not null)
            {
                Assert.Contains("Held by Neighbours", RecordHost.AtlasPages["p:Squin"]);
                Assert.Contains("Their people of note you may know of: Guard.", RecordHost.AtlasPages["f:Neighbours"]);
            }
        }
        finally
        {
            (settings.Atlas, settings.LighterChecks, settings.TownHandovers) = previous;
            (RecordHost.AtlasList, RecordHost.AtlasPages, RecordHost.AtlasPortraits) = before;
        }
    }

    [Fact]
    public void Stable_person_text_is_reused_and_reload_updates_gossip_and_sightings()
    {
        var settings = LwMod.Settings;
        var previous = (settings.Atlas, settings.LighterChecks);
        var before = (RecordHost.AtlasList, RecordHost.AtlasPages, RecordHost.AtlasPortraits);
        try
        {
            settings.Atlas = true;
            settings.LighterChecks = false;
            var atlas = new AtlasSystem(new AtlasTable(People: new() { ["Guard"] = new(Epithet: "guard") }), null, null);
            using var world = W.World(atlas);

            void Load(string gossip, string place, double day)
            {
                var book = new AtlasBook();
                book.Note("Guard", new(gossip, place, day * 24));
                book.Meet("Guard");
                book.Saw("Guard", place, day * 24);
                using var stream = new MemoryStream();
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) book.Write(writer);
                stream.Position = 0;
                atlas.Read(stream);
            }

            Load("Old report", "Squin", 2);
            world.RunTicks(8);
            string original = RecordHost.AtlasPages["w:Guard"];
            Assert.Contains("Old report", original);
            world.RunTicks(60);
            Assert.Same(original, RecordHost.AtlasPages["w:Guard"]);

            // Same number of rumours as the old save, with different text.
            Load("New report", "Admag", 3);
            world.RunTicks(60);
            string reloaded = RecordHost.AtlasPages["w:Guard"];
            Assert.Contains("New report", reloaded);
            Assert.Contains("Last seen by your squad: Admag, day 3", reloaded);
            Assert.DoesNotContain("Old report", reloaded);
        }
        finally
        {
            (settings.Atlas, settings.LighterChecks) = previous;
            (RecordHost.AtlasList, RecordHost.AtlasPages, RecordHost.AtlasPortraits) = before;
        }
    }
}
