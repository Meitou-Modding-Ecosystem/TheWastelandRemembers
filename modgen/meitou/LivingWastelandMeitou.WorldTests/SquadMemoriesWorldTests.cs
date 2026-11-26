using System.Collections;
using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class SquadMemoriesWorldTests
{
    [Fact]
    public void A_squadmate_recalls_avenging_a_friend_only_out_of_combat()
    {
        var setting = typeof(LwSettings).GetProperty("SquadMemories");
        Assert.NotNull(setting);
        var settings = LwMod.Settings;
        var old = (Memories: (bool)setting.GetValue(settings)!, settings.Memorials, settings.Talk);
        try
        {
            setting.SetValue(settings, true);
            settings.Memorials = true;
            settings.Talk = 100;
            var tracker = new LwDeedsSystem([], null);
            using var world = W.World(tracker, new LwSystem(null, null, null));
            var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            var cold = world.Characters.Cold(leaf.Slot)!;
            tracker.Deeds.Record.Died("Ruka", 30, "Squin", "Bandit", "Dust Bandits", "bandit-chief");
            tracker.Deeds.Record.Avenge("Leaf", "Bandit", "Dust Bandits", "bandit-chief");
            Assert.Equal("Leaf", Assert.Single(tracker.Deeds.Record.FallenMembers).AvengedBy);

            var barks = (IDictionary)typeof(World).GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            cold.InCombat = true;
            world.GameHours = 150;
            world.RunTicks(121);
            Assert.False(barks.Contains(leaf));

            cold.InCombat = false;
            world.RunTicks(181);
            world.GameHours += 24;
            world.RunTicks(60);
            Assert.True(barks.Contains(leaf));
            Assert.Contains("I settled the score for Ruka", barks[leaf]?.ToString());
        }
        finally
        {
            setting.SetValue(settings, old.Memories);
            settings.Memorials = old.Memorials;
            settings.Talk = old.Talk;
        }
    }
}
