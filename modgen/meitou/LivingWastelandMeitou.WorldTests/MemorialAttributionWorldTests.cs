using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Simulation.Bodies;
using Xunit;

[Collection("settings")]
public sealed class MemorialAttributionWorldTests
{
    [Theory]
    [InlineData(DeathCause.Starvation, false)]
    [InlineData(DeathCause.BloodLoss, false)]
    [InlineData(DeathCause.VitalPart, true)]
    public void A_memorial_only_names_the_attacker_when_the_blow_killed(DeathCause cause, bool lethalHit)
    {
        var deeds = new LwDeedsSystem([], W.Data([]));
        var world = W.World(deeds);
        var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var bandit = W.Spawn(world, "Bandit", W.Bandits, Vector3.One);
        world.RunTicks(2);
        if (!lethalHit) deeds.HitResolved(world, W.Hit(world, bandit, leaf));

        var medical = world.Characters.Cold(leaf.Slot)!.Medical!;
        typeof(MedicalState).GetMethod("Die", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(medical, [cause, null]);
        if (lethalHit) deeds.HitResolved(world, W.Hit(world, bandit, leaf));
        world.RunTicks(31);

        var record = LwDeeds.For(world)!.Record;
        var fallen = Assert.Single(record.FallenMembers);
        Assert.Equal(lethalHit ? "Bandit" : null, fallen.Killer);
        Assert.Equal(lethalHit, record.Feuds.ContainsKey("Dust Bandits"));
    }
}
