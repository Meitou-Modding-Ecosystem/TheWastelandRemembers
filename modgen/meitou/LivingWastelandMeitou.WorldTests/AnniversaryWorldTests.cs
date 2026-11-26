using System.Numerics;
using LivingWasteland.MeitouMod;
using Xunit;

[Collection("settings")]
public sealed class AnniversaryWorldTests
{
    [Fact]
    public void A_founding_anniversary_uses_the_kenshi_year_and_is_not_duplicated_on_load()
    {
        var setting = typeof(LwSettings).GetProperty("Anniversaries");
        Assert.NotNull(setting);
        bool previous = (bool)setting.GetValue(LwMod.Settings)!;
        try
        {
            setting.SetValue(LwMod.Settings, true);
            var tracker = new LwDeedsSystem([], null);
            using var world = W.World(tracker, new LwSystem(null, null, null));
            W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            tracker.Deeds.Record.Seen("Leaf", 48);
            const string anniversary = "A year since your squad first set out.";
            world.GameHours = 48 + 99 * 24;
            world.RunTicks(121);
            Assert.DoesNotContain(tracker.Deeds.Record.History, h => h.Text == anniversary);

            world.GameHours += 24;
            world.RunTicks(121);
            Assert.Single(tracker.Deeds.Record.History, h => h.Text == anniversary);
            using var stream = new MemoryStream();
            tracker.Write(stream);
            stream.Position = 0;
            var loaded = new LwDeedsSystem([], null);
            loaded.Read(stream);
            using var next = W.World(loaded, new LwSystem(null, null, null));
            next.GameHours = world.GameHours;
            next.RunTicks(121);
            Assert.Single(loaded.Deeds.Record.History, h => h.Text == anniversary);
        }
        finally { setting.SetValue(LwMod.Settings, previous); }
    }
}
