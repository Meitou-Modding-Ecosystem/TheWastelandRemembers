using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class CharacterTitleDisplayWorldTests
{
    [Fact]
    public void Native_name_hook_only_adds_the_title_to_the_matching_actors_panel()
    {
        var hook = typeof(UiRegistration).GetMethod("AddText");
        if (hook is null) return;
        var context = hook.GetParameters()[0].ParameterType.GenericTypeArguments[0];
        if (context.GetProperty("Character") is null) return;
        var setting = typeof(LwSettings).GetProperty("TitlesInCharacterNames");
        Assert.NotNull(setting);
        var settings = LwMod.Settings;
        var before = (setting.GetValue(settings), settings.RecordInPanels);
        try
        {
            setting.SetValue(settings, true);
            settings.RecordInPanels = false;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var hostType = typeof(UiRegistration).Assembly.GetType("Meitou.Game.Ui.UiHost", throwOnError: true)!;
            object host = Activator.CreateInstance(hostType, nonPublic: true)!;
            var ctor = typeof(UiRegistration).GetConstructors(flags).Single();
            using var registration = (UiRegistration)ctor.Invoke([host, "title.test", .. ctor.GetParameters().Skip(2).Select(p => p.HasDefaultValue ? p.DefaultValue : null)]);
            var display = typeof(LwMod).Assembly.GetType("LivingWasteland.MeitouMod.CharacterTitleDisplay")!;
            var register = display.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!;
            var names = context.Assembly.GetType("Meitou.Localisation.ShownText", throwOnError: true)!;
            var revision = names.GetProperty("Revision")!;
            setting.SetValue(settings, false);
            var original = revision.GetValue(null);
            register.Invoke(null, [registration]);
            Assert.Equal(original, revision.GetValue(null));
            setting.SetValue(settings, true);
            register.Invoke(null, [registration]);

            var tracker = new LwDeedsSystem([new("hand", "Sword Hand", "member", "kills", null, null, 1)], null);
            using var world = W.World(tracker);
            var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            var other = W.Spawn(world, "Leaf", W.Player, Vector3.UnitX);
            var actor = world.Characters.Cold(leaf.Slot)!;
            tracker.Deeds.Record.Member(tracker.Deeds.Who(actor)).Titles.Add("hand");
            world.RunTicks(95);

            var place = context.GetProperty("Place")!.PropertyType;
            var lookup = names.GetMethod("Name", [typeof(string), typeof(string), typeof(string), typeof(string), typeof(ulong), place])!;
            string Shown(ulong id, string surface) =>
                (string)lookup.Invoke(null, ["character", "r-leaf", "", "Leaf", id, Enum.Parse(place, surface)])!;
            ulong actorId = LastingActors.Of(actor);
            Assert.NotEqual(0UL, actorId);
            Assert.Equal("Leaf · Sword Hand", Shown(actorId, "Panel"));
            Assert.Equal("Leaf", Shown(actorId, "Dialogue"));
            Assert.Equal("Leaf", Shown(LastingActors.Grant(world, other), "Panel"));
            Assert.Equal("Leaf", actor.Name);

            setting.SetValue(settings, false);
            world.RunTicks(95);
            Assert.Equal("Leaf", Shown(actorId, "Panel"));
        }
        finally
        {
            setting.SetValue(settings, before.Item1);
            settings.RecordInPanels = before.RecordInPanels;
        }
    }
}
