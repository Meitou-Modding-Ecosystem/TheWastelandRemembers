using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Xunit;

[Collection("settings")]
public sealed class KnockoutFightWorldTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Only_a_nearby_foe_with_a_short_knockout_keeps_the_fight_open(bool coma, bool nearby)
    {
        if (!LifecycleWatch.BuildWatches().TryGetValue("Knockout", out var watch)) return;
        var tracker = new LwDeedsSystem([], W.Data([]));
        using var world = W.World(tracker, (ITickSystem)Activator.CreateInstance(watch)!);
        var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var foe = W.Spawn(world, "Bandit", W.Bandits, new Vector3(nearby || coma ? 100 : 2000, 0, 0));
        var cold = world.Characters.Cold(leaf.Slot)!;
        var medical = world.Characters.Cold(foe.Slot)!.Medical!;
        cold.InCombat = true;
        tracker.HitResolved(world, W.Hit(world, leaf, foe));
        world.RunTicks(31);
        Assert.True(tracker.Deeds.Record.InFight);
        cold.InCombat = false;
        void Medical(string name, object value) => typeof(MedicalState).GetProperty(name)!.SetValue(medical, value);
        Medical(nameof(MedicalState.Unconscious), true);
        Medical("KoTimer", 10f);
        Medical("Coma", coma);

        world.RunTicks(180);

        Assert.Equal(nearby, tracker.Deeds.Record.InFight);
        if (!nearby) return;

        Medical(nameof(MedicalState.Unconscious), false);
        Medical("KoTimer", 0f);
        cold.InCombat = true;
        world.RunTicks(31);
        cold.InCombat = false;
        world.Characters.Place(foe.Slot, new Vector3(2000, 0, 0), 0);
        world.RunTicks(180);
        Assert.False(tracker.Deeds.Record.InFight);
        Assert.Equal(1, tracker.Deeds.Record.Members["Leaf"].Fights);
    }
}
