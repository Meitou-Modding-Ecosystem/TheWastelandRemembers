using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Xunit;

[Collection("settings")]
public sealed class AtlasWhereaboutsWorldTests
{
    [Fact]
    public void An_unseen_camp_move_changes_neither_the_atlas_nor_the_pin()
    {
        var settings = LwMod.Settings;
        var old = (settings.MapMarks, settings.Atlas, settings.OutlawCamps, settings.LastKnownPlaces, OutlawCampsSystem.NewsTowns);
        try
        {
            settings.MapMarks = settings.Atlas = settings.OutlawCamps = settings.LastKnownPlaces = true;
            OutlawCampsSystem.NewsTowns = ["Squin"];
            var town = W.Rec("squin-t", FcsRecordType.Town, "Squin");
            var outlaw = W.Rec("outlaw-t", FcsRecordType.Character, "Fixture outlaw");
            var data = W.Data([town, outlaw], [new TownPlacement("i-squin", town.StringId, Vector3.Zero, null)]);
            var atlas = new AtlasSystem(new AtlasTable(People: new() { [outlaw.Name] = new(Id: outlaw.StringId, Outlaw: true) }), null, data);
            var camps = new OutlawCampsSystem([new CampCode(0, outlaw.Name, outlaw.StringId, "camp-t", [])], null, data);
            var book = (CampBook)typeof(OutlawCampsSystem).GetField("book", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(camps)!;
            using var world = W.World(camps, atlas, new NewsSystem(data));
            W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            var speaker = W.Spawn(world, "Storyteller", W.Bandits, Vector3.UnitX * 100);
            book.Set(0, new CampEntry(3_000, 5_000, 1, OutlawNews.Raiding));
            world.RunTicks(80);
            Assert.DoesNotContain(NightlyHooks.Marks, m => m.Kind == MarkKind.Outlaw);

            camps.HeardCamp(world, 0, speaker);
            world.RunTicks(80);
            var reported = Assert.Single(NightlyHooks.Marks, m => m.Kind == MarkKind.Outlaw);
            Assert.Equal(CampRules.CellSize / 2, reported.X);
            Assert.Contains("Last reported near Squin", RecordHost.AtlasPages["w:" + outlaw.Name]);

            book.Set(0, new CampEntry(9_000, 7_000, 2, OutlawNews.LyingLow));
            world.GameHours += 50;
            world.RunTicks(80);
            var stale = Assert.Single(NightlyHooks.Marks, m => m.Kind == MarkKind.Outlaw);
            Assert.Equal(reported.X, stale.X);
            Assert.Contains("two days ago", stale.Tooltip);

            camps.HeardCamp(world, 0, speaker);
            world.RunTicks(80);
            Assert.Equal(CampRules.CellSize * 1.5f, Assert.Single(NightlyHooks.Marks, m => m.Kind == MarkKind.Outlaw).X);
        }
        finally { (settings.MapMarks, settings.Atlas, settings.OutlawCamps, settings.LastKnownPlaces, OutlawCampsSystem.NewsTowns) = old; }
    }

    [Fact]
    public void A_lost_rival_is_marked_unknown_at_the_last_witnessed_position()
    {
        var setting = typeof(LwSettings).GetProperty("LastKnownPlaces");
        var old = setting?.GetValue(LwMod.Settings);
        var settings = LwMod.Settings;
        var (marks, atlasOn, rivalsOn) = (settings.MapMarks, settings.Atlas, settings.Rivals);
        try
        {
            setting?.SetValue(settings, true);
            settings.MapMarks = settings.Atlas = settings.Rivals = true;
            var rivals = new RivalsSystem(null);
            var atlas = new AtlasSystem(new AtlasTable(), null, null);
            using var world = W.World(rivals, atlas, new NewsSystem(null));
            W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            var rival = W.Spawn(world, "Hollin", W.Bandits, Vector3.UnitX * 200);
            rivals.Read(W.SavedRival("Hollin", "lost an arm to your squad", 1, 200, 0));
            world.RunTicks(340);
            world.Characters.Remove(rival);
            world.GameHours += 50;
            world.RunTicks(380);
            Assert.Contains("Whereabouts unknown", RecordHost.AtlasPages["r:Hollin"]);
            Assert.Contains("two days ago", RecordHost.AtlasPages["r:Hollin"]);
            var mark = Assert.Single(NightlyHooks.Marks, m => m.Kind == MarkKind.Rival);
            Assert.Contains("Whereabouts unknown", mark.Tooltip);
            Assert.Equal(200, mark.X);

            using var save = new MemoryStream();
            atlas.Write(save); save.Position = 0;
            using var rivalSave = new MemoryStream();
            rivals.Write(rivalSave); rivalSave.Position = 0;
            var restoredAtlas = new AtlasSystem(new AtlasTable(), null, null);
            restoredAtlas.Read(save);
            var restoredRivals = new RivalsSystem(null);
            restoredRivals.Read(rivalSave);
            using var restored = W.World(restoredRivals, restoredAtlas, new NewsSystem(null));
            W.Spawn(restored, "Leaf", W.Player, Vector3.Zero);
            restored.GameHours = world.GameHours;
            restored.RunTicks(380);
            var restoredMark = Assert.Single(NightlyHooks.Marks, m => m.Kind == MarkKind.Rival);
            Assert.Equal(mark.X, restoredMark.X);
            Assert.Contains("two days ago", restoredMark.Tooltip);
        }
        finally
        {
            if (old is not null) setting?.SetValue(settings, old);
            (settings.MapMarks, settings.Atlas, settings.Rivals) = (marks, atlasOn, rivalsOn);
        }
    }
}
