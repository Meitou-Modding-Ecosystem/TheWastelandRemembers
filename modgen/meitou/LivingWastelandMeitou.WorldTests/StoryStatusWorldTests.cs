using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Items;
using Xunit;

[Collection("settings")]
public sealed class StoryStatusWorldTests
{
    [Fact]
    public void A_keepsake_heir_and_a_returning_rival_get_timed_status_with_no_stat_modifiers()
    {
        var engine = typeof(World).Assembly;
        var effect = engine.GetType("Meitou.Simulation.Bodies.StatusEffect");
        if (effect is null) return;
        var tool = typeof(LwMod).Assembly.GetType("LivingWasteland.MeitouMod.StoryStatuses");
        Assert.NotNull(tool);
        var registry = engine.GetType("Meitou.Simulation.Bodies.StatusEffects", throwOnError: true)!;
        var add = registry.GetMethod("Add", [effect])!;
        var clear = registry.GetMethod("Clear", BindingFlags.Static | BindingFlags.NonPublic)!;
        var configure = tool.GetMethod("Configure", BindingFlags.NonPublic | BindingFlags.Static)!;

        const string keepsake = "keepsake-test";
        string? previous = Trophies.Keepsake;
        bool trophies = LwMod.Settings.Trophies, rivalsEnabled = LwMod.Settings.Rivals;
        try
        {
            configure.Invoke(null, [effect, (Action<object>)(status => add.Invoke(null, [status]))]);
            Trophies.Keepsake = keepsake;
            LwMod.Settings.Trophies = LwMod.Settings.Rivals = true;
            var data = W.Data([W.Rec(keepsake, FcsRecordType.Item, "Keepsake of the Fallen")]);
            var rivals = new RivalsSystem(data);
            using var world = W.World(new LwSystem(null, null, data), rivals);
            var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero, new Inventory());
            var hollin = W.Spawn(world, "Hollin", W.Bandits, Vector3.UnitX * 500);
            world.Characters.Cold(leaf.Slot)!.Stats = new CharacterStats();
            world.Characters.Cold(hollin.Slot)!.Stats = new CharacterStats();
            using var save = W.SavedRival("Hollin", "lost an arm", 1, 500, 0, returns: 1, boosted: 1);
            rivals.Read(save);
            world.RunTicks(301);
            Trophies.GiveKeepsake(world, leaf, "Leaf", "Kite", "Kite");

            void Check(string id, CharacterId on)
            {
                var status = registry.GetMethod("Find")!.Invoke(null, [id]);
                Assert.NotNull(status);
                var active = effect.GetMethod("On", [typeof(World), typeof(CharacterId)])!.Invoke(status, [world, on]);
                Assert.NotNull(active);
                Assert.True((long)active.GetType().GetProperty("TicksLeft")!.GetValue(active)! > 0);
                Assert.Empty((System.Collections.IEnumerable)effect.GetProperty("Modifiers")!.GetValue(status)!);
            }
            Check("twr.grieving", leaf);
            Check("twr.old-wound", hollin);
        }
        finally
        {
            clear.Invoke(null, null);
            tool.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
            Trophies.Keepsake = previous;
            LwMod.Settings.Trophies = trophies;
            LwMod.Settings.Rivals = rivalsEnabled;
        }
    }
}
