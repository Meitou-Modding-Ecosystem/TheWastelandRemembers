using System.Collections;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class StorySoundWorldTests
{
    [Fact]
    public void Both_alerts_use_native_notification_events_and_respect_the_mute_setting()
    {
        if (typeof(World).GetMethod("PostInterfaceSound", [typeof(string), typeof(string), typeof(string)]) is null) return;
        var settings = LwMod.Settings;
        var option = settings.GetType().GetProperty("StoryAlertSounds");
        Assert.NotNull(option);
        Assert.Equal(false, option.GetValue(new LwSettings()));
        var sound = typeof(LwMod).Assembly.GetType("LivingWasteland.MeitouMod.StorySounds")!;
        var lookout = sound.GetMethod("Lookout", BindingFlags.NonPublic | BindingFlags.Static)!;
        var rival = sound.GetMethod("Rival", BindingFlags.NonPublic | BindingFlags.Static)!;

        using var world = W.World();
        var property = typeof(World).GetProperty("Sounds")!;
        var queue = Activator.CreateInstance(property.PropertyType)!;
        property.SetValue(world, queue);
        var before = option.GetValue(settings);
        try
        {
            option.SetValue(settings, false);
            lookout.Invoke(null, [world]);
            rival.Invoke(null, [world]);
            Assert.Empty((IEnumerable)queue);
            option.SetValue(settings, true);
            lookout.Invoke(null, [world]);
            rival.Invoke(null, [world]);

            var events = ((IEnumerable)queue).Cast<object>().ToArray();
            Assert.Equal(2, events.Length);
            var posted = new List<string>();
            foreach (var row in events)
            {
                Assert.Equal("PostedInterface", row.GetType().GetProperty("Kind")!.GetValue(row)!.ToString());
                var cue = row.GetType().GetProperty("Posted")!.GetValue(row)!;
                Assert.Equal("Notifications", cue.GetType().GetProperty("Event")!.GetValue(cue));
                Assert.Equal("Notifications", cue.GetType().GetProperty("SwitchGroup")!.GetValue(cue));
                posted.Add((string)cue.GetType().GetProperty("SwitchValue")!.GetValue(cue)!);
            }
            Assert.Equal(new[] { "Under_Attack", "Raid" }, posted);
        }
        finally { option.SetValue(settings, before); }
    }
}
