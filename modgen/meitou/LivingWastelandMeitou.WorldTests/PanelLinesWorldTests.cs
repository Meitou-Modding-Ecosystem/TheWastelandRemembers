using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Xunit;

// The squad record in the game's panels (Meitou 0.23+): a member's titles and record for the character panel, a keepsake's fall and
// avenger and a warrant's town for item tooltips, published by the squad look and read by the interface. On 0.23 the tooltip goes through
// the game's own UI host as registered; older clients register nothing.
[Collection("settings")]
public sealed class PanelLinesWorldTests
{
    static readonly TitleRule[] Rules = [new("butcher", "the Butcher", "member", "kills", null, null, 1)];
    sealed record PanelContext(CharacterId Id);

    [Fact]
    public void A_title_row_uses_the_installed_icon_only_when_the_preview_is_enabled()
    {
        var type = typeof(UiRegistration).Assembly.GetType("Meitou.Game.Ui.UiCharacterLine");
        var style = type?.GetProperty("Style");
        if (style?.PropertyType.GetProperty("Icon") is not { } icon) return;
        var folder = Directory.CreateTempSubdirectory("twr-panel-art-");
        var setting = typeof(LwSettings).GetProperty("PanelIcons");
        var old = setting?.GetValue(LwMod.Settings);
        const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
        var load = typeof(PanelLines).GetMethod("LoadArt", hidden);
        try
        {
            string filename = Path.Combine(folder.FullName, "twr-title.png");
            File.WriteAllBytes(filename, [137, 80, 78, 71]);
            setting?.SetValue(LwMod.Settings, true);
            load?.Invoke(null, [folder.FullName]);
            using var world = W.World();
            var actor = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            world.RunTicks(1);
            var deeds = new LwDeeds(Rules, W.Data([]));
            deeds.Record.Kill("Leaf", "Dust Bandits", false, null);
            PanelLines.Publish(deeds.Record, [(actor, "Leaf")], world);
            var provider = typeof(PanelLines).GetMethod("CharacterLines", hidden)!.MakeGenericMethod(typeof(PanelContext), type!);
            var context = new PanelContext(actor);
            object[] Lines() => ((System.Collections.IEnumerable)provider.Invoke(null, [context])!).Cast<object>().ToArray();
            var rows = Lines();
            Assert.Equal(filename, style.GetValue(rows[0]) is { } styled ? icon.GetValue(styled) : null);
            Assert.Null(style.GetValue(rows[1]));
            setting?.SetValue(LwMod.Settings, false);
            Assert.Null(style.GetValue(Lines()[0]));
        }
        finally
        {
            if (setting is not null) setting.SetValue(LwMod.Settings, old);
            folder.Delete(recursive: true);
            load?.Invoke(null, [folder.FullName]);
            PanelLines.Reset();
        }
    }

    [Fact]
    public void Loading_a_world_clears_the_previous_character_and_item_rows_before_the_first_panel_pass()
    {
        using var first = W.World();
        var deeds = new LwDeeds(Rules, W.Data([]));
        var leaf = W.Spawn(first, "Leaf", W.Player, Vector3.Zero);
        deeds.Record.Kill("Leaf", "Dust Bandits", false, null);
        deeds.Record.Died("Kite", 100, "Squin", null, null, null);
        first.RunTicks(1);

        var (slots, enabled) = (KeepsakeBook.Slots, LwMod.Settings.RecordInPanels);
        try
        {
            KeepsakeBook.Slots = ["keepsake"];
            LwMod.Settings.RecordInPanels = true;
            PanelLines.Publish(first, deeds);
            Assert.NotNull(PanelLines.ForCharacter(leaf));
            Assert.NotNull(PanelLines.ForItem("keepsake", "Keepsake of Kite", null));

            using var second = W.World();
            var recruit = W.Spawn(second, "Ruka", W.Player, Vector3.Zero);
            Assert.Equal(leaf, recruit);
            WorldReady.Ready(second, true);
            Assert.Null(PanelLines.ForCharacter(recruit));
            Assert.Null(PanelLines.ForItem("keepsake", "Keepsake of Kite", null));
        }
        finally { (KeepsakeBook.Slots, LwMod.Settings.RecordInPanels) = (slots, enabled); }
    }

    [Fact]
    public void A_members_titles_and_record_and_a_keepsakes_story_are_shown()
    {
        var world = W.World();
        var deeds = new LwDeeds(Rules, W.Data([]));
        LwDeeds.Register(world, deeds);
        var leaf = W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        world.RunTicks(1);
        var r = deeds.Record;
        r.Seen("Leaf", 0); r.Seen("Ruka", 0);
        r.Kill("Leaf", "Dust Bandits", false, null);
        r.Award("Leaf");
        r.DeathNoted("Ruka", "Bled out", null, null, null);
        r.Died("Ruka", 24 * 41 + 3, "Squin", null, null, null);
        r.Avenge("Leaf", "nobody", null, null);

        Assert.Equal([new PanelLines.Row("Known as", "the Butcher"), new PanelLines.Row("Record", "1 kill, 0 of 0 fights won")], PanelLines.MemberRows(r, "Leaf"));
        Assert.Equal([new PanelLines.Row("Bled out", "day 41 at Squin"), new PanelLines.Row("Not yet avenged", null)],
            PanelLines.FallenRows(r.FallenMembers[0], r.Display));

        var (keepsakes, warrant) = (KeepsakeBook.Slots, Trophies.Warrant);
        try
        {
            KeepsakeBook.Slots = ["keep-1"];
            Trophies.Warrant = "warrant-1";
            PanelLines.Publish(world, deeds);
            Assert.Equal("the Butcher", PanelLines.ForCharacter(leaf)![0].Value);
            Assert.Equal("Bled out", PanelLines.ForItem("keep-1", "Keepsake of Ruka", null)![0].Label);
            Assert.Null(PanelLines.ForItem("other", "Keepsake of Ruka", null));

            if (typeof(UiRegistration).GetMethod("AddItemTooltipLines") is null) { Assert.False(PanelLines.Available); return; }
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var hostType = typeof(UiRegistration).Assembly.GetType("Meitou.Game.Ui.UiHost", throwOnError: true)!;
            var host = Activator.CreateInstance(hostType, nonPublic: true)!;
            var make = typeof(UiRegistration).GetConstructors(any).Single();
            using var registration = (UiRegistration)make.Invoke([host, "LivingWastelandMeitou", .. make.GetParameters().Skip(2).Select(p => p.HasDefaultValue ? p.DefaultValue : null)]);
            var logs = new List<string>();
            Assert.True(PanelLines.Register(registration, logs.Add), string.Join(", ", logs));
            var contextType = typeof(UiRegistration).Assembly.GetType("Meitou.Game.Ui.UiItemTooltipContext", throwOnError: true)!;
            string[] Tip(string record, string name, string tag)
            {
                var context = contextType.GetConstructors().Single(c => c.GetParameters().Length == 6).Invoke([record, name, tag, 1, 0, 1f]);
                var lines = (System.Collections.IEnumerable)hostType.GetMethod("ItemTooltipLines")!.Invoke(host, [context])!;
                return lines.Cast<object>().Select(l => $"{l.GetType().GetProperty("Label")!.GetValue(l)}|{l.GetType().GetProperty("Value")!.GetValue(l)}").ToArray();
            }
            Assert.Equal(["Signed in|Squin"], Tip("warrant-1", "Bounty warrant", "Squin"));
            Assert.Equal(["Bled out|day 41 at Squin", "Not yet avenged|"], Tip("keep-1", "Keepsake of Ruka", ""));
            Assert.Empty(Tip("other", "A katana", ""));
        }
        finally { (KeepsakeBook.Slots, Trophies.Warrant) = (keepsakes, warrant); }
    }
}
