using System.Numerics;
using LivingWasteland.MeitouMod;
using Xunit;

[Collection("settings")]
public sealed class ChronicleChapterWorldTests
{
    [Fact]
    public void The_export_includes_live_and_fallen_chapters_and_actual_title_progress()
    {
        var setting = typeof(LwSettings).GetProperty("ChronicleChapters");
        var old = setting?.GetValue(LwMod.Settings);
        try
        {
            setting?.SetValue(LwMod.Settings, true);
            var deeds = new LwDeedsSystem([
                new TitleRule("near", "the Seasoned", "member", "kills", null, null, 3),
                new TitleRule("far", "the Bonepicker", "member", "kills", null, null, 20),
                new TitleRule("shared", "the Headtakers", "squad", "leaders", null, null, 5),
            ], null);
            using var world = W.World(deeds);
            W.Spawn(world, "Leaf & Friends", W.Player, Vector3.Zero);
            deeds.Deeds.Record.Member("Leaf & Friends").Kills = 2;
            deeds.Deeds.Record.Member("Hollin").Kills = 1;
            deeds.Deeds.Record.Member("Hollin").LeadersKilled = 1;
            deeds.Deeds.Record.Died("Hollin", 20, "the wilds", null, null, null);
            world.RunTicks(300);
            string html = StatsUi.ChronicleHtml();
            Assert.Contains("<h2>Leaf &amp; Friends</h2>", html);
            Assert.Contains("<h2>Hollin</h2>", html);
            Assert.Contains("the Seasoned (2 of 3)", html);
            Assert.Contains("the Headtakers (1 of 5)", html);
            Assert.Contains("Fell in the wilds", html);
            Assert.DoesNotContain("the Bonepicker (2 of 20)", html);
        }
        finally
        {
            if (old is not null) setting?.SetValue(LwMod.Settings, old);
        }
    }
}
