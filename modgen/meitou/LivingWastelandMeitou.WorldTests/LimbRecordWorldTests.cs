using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Xunit;

[Collection("settings")]
public sealed class LimbRecordWorldTests
{
    static HealthPart GiveArm(World world, CharacterId actor)
    {
        var arm = new HealthPart(new BodyPartTemplate { StringId = "left-arm", Name = "Left Arm", Type = BodyPartType.Arm }, 1, 100);
        world.Characters.Cold(actor.Slot)!.Medical = new MedicalState([arm]);
        return arm;
    }

    [Fact]
    public void The_limb_notice_records_a_new_loss_once_before_the_next_squad_look()
    {
        if (!LifecycleWatch.BuildWatches().TryGetValue("Limb", out var watch)) return;
        var rule = new TitleRule("survivor", "the Survivor", "member", "limbs_lost", null, null, 1);
        var tracker = new LwDeedsSystem([rule], W.Data([]));
        using var world = W.World(tracker, (ITickSystem)Activator.CreateInstance(watch)!);
        var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var arm = GiveArm(world, leaf);
        world.RunTicks(31);

        arm.Limb = LimbState.Stump;
        world.RunTicks(1); // state observers run now, the squad look does not
        Assert.Equal(1, tracker.Deeds.Record.Members["Leaf"].LimbsLost);
        Assert.True(tracker.Deeds.Record.HoldsTop("Leaf", rule.Id));
        world.RunTicks(30);
        Assert.Equal(1, tracker.Deeds.Record.Members["Leaf"].LimbsLost);

        arm.Limb = LimbState.Replaced;
        world.RunTicks(31);
        arm.Limb = LimbState.Stump;
        world.RunTicks(31);
        Assert.Equal(1, tracker.Deeds.Record.Members["Leaf"].LimbsLost);
    }

    [Fact]
    public void A_world_without_the_observer_still_records_limb_loss_by_sampling()
    {
        var tracker = new LwDeedsSystem([], W.Data([]));
        using var world = W.World(tracker);
        var arm = GiveArm(world, W.Spawn(world, "Leaf", W.Player, Vector3.Zero));
        world.RunTicks(31);
        arm.Limb = LimbState.Stump;
        world.RunTicks(31);
        Assert.Equal(1, tracker.Deeds.Record.Members["Leaf"].LimbsLost);
    }
}
