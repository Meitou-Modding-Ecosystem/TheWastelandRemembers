using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class CharacterRemovalWorldTests
{
    [Fact]
    public void Removal_invalidates_only_the_rows_for_that_world_and_character()
    {
        if (!LifecycleWatch.BuildWatches().TryGetValue("Removed", out var watch)) return;
        var deeds = new LwDeeds([], W.Data([]));
        using var other = W.World((ITickSystem)Activator.CreateInstance(watch)!);
        using var world = W.World((ITickSystem)Activator.CreateInstance(watch)!);
        var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var kite = W.Spawn(world, "Kite", W.Player, Vector3.One);
        var unrelated = W.Spawn(other, "Elsewhere", W.Player, Vector3.Zero);
        Assert.Equal(leaf, unrelated);
        bool panels = LwMod.Settings.RecordInPanels;
        try
        {
            LwMod.Settings.RecordInPanels = true;
            deeds.Record.Kill("Leaf", "Dust Bandits", false, null);
            deeds.Record.Kill("Kite", "Dust Bandits", false, null);
            world.RunTicks(1);
            PanelLines.Publish(world, deeds);
            Assert.NotNull(PanelLines.ForCharacter(leaf));
            Assert.NotNull(PanelLines.ForCharacter(kite));

            Assert.True(other.Characters.Remove(unrelated));
            Assert.NotNull(PanelLines.ForCharacter(leaf));
            Assert.True(world.Characters.Remove(leaf));

            Assert.Null(PanelLines.ForCharacter(leaf));
            Assert.NotNull(PanelLines.ForCharacter(kite));
            var replacement = W.Spawn(world, "Replacement", W.Player, Vector3.Zero);
            Assert.Equal(leaf.Slot, replacement.Slot);
            Assert.NotEqual(leaf, replacement);
            Assert.Null(PanelLines.ForCharacter(replacement));
        }
        finally { LwMod.Settings.RecordInPanels = panels; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_named_bleed_out_claim_survives_unloading_and_yields_to_a_new_hit(bool outsideHit)
    {
        if (!LifecycleWatch.BuildWatches().TryGetValue("Removed", out var watch)) return;
        const string record = "named-outlaw";
        var tracker = new LwDeedsSystem([], W.Data([]));
        using var world = W.World(tracker, (ITickSystem)Activator.CreateInstance(watch)!);
        var player = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var npc = W.Spawn(world, "Fighter", W.Bandits, Vector3.One);
        world.Uniques.Bind(record, npc);
        world.RunTicks(31);
        tracker.HitResolved(world, W.Hit(world, player, npc));
        Assert.True(world.Characters.Remove(npc));

        if (outsideHit)
        {
            var returned = W.Spawn(world, "Fighter", W.Bandits, Vector3.One);
            var bandit = W.Spawn(world, "Bandit", W.Bandits, Vector3.One);
            world.Uniques.Bind(record, returned);
            // The first hit on the returned actor arrives before the next squad look can rebind the old claim.
            tracker.HitResolved(world, W.Hit(world, bandit, returned));
            Assert.True(world.Characters.Remove(returned));
        }
        world.Uniques.Died(record, byPlayer: false, faction: W.Bandits);
        world.RunTicks(31);

        Assert.Equal(outsideHit ? 0 : 1, tracker.Deeds.Record.Members.Values.Sum(m => m.Kills));
    }
}
