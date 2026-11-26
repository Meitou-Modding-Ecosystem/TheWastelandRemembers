using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class FightResumeWorldTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void The_active_fight_keeps_participants_downs_and_remaining_quiet_looks(int quiet)
    {
        var tracker = new LwDeedsSystem([], null);
        using var world = W.World(tracker);
        W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        W.Spawn(world, "Ruka", W.Player, Vector3.Zero);
        var record = tracker.Deeds.Record;
        record.FightLook(["Leaf", "Ruka"], []);
        record.Knockout("Leaf");
        record.Downed("Ruka");
        for (int i = 0; i < quiet; i++) record.FightLook([], []);
        var loaded = Restore(tracker);
        using var next = W.World(loaded);
        W.Spawn(next, "Leaf", W.Player, Vector3.Zero);
        W.Spawn(next, "Ruka", W.Player, Vector3.Zero);

        Assert.True(loaded.Deeds.Record.InFight);
        for (int i = 0; i < 4 - quiet; i++) loaded.Deeds.Record.FightLook([], []);
        Assert.True(loaded.Deeds.Record.InFight);
        loaded.Deeds.Record.FightLook([], []);

        Assert.Equal(1, loaded.Deeds.Record.Member("Leaf").Knockouts);
        Assert.Equal(1, loaded.Deeds.Record.Member("Leaf").FightsWon);
        Assert.Equal(1, loaded.Deeds.Record.Member("Ruka").Fights);
        Assert.Equal(new[] { "Leaf" }, loaded.Deeds.Record.TakeFightEnd()!.Value.Standing);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_nearby_saved_foe_holds_the_fight_open_and_a_reused_slot_does_not(bool same)
    {
        var tracker = new LwDeedsSystem([], null);
        using var world = W.World(tracker);
        var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var foe = W.Spawn(world, "Bandit", W.Bandits, Vector3.UnitX * 100);
        world.Characters.Cold(foe.Slot)!.PortraitSerial = 901;
        world.Characters.Cold(leaf.Slot)!.InCombat = true;
        world.RunTicks(31);
        tracker.HitResolved(world, W.Hit(world, leaf, foe));
        var lasting = LastingActors.Of(world.Characters.Cold(foe.Slot)!);

        var loaded = Restore(tracker);
        using var next = W.World(loaded);
        W.Spawn(next, "Leaf", W.Player, Vector3.Zero);
        var wrong = W.Spawn(next, "Different", W.Bandits, Vector3.UnitX * 100);
        Assert.Equal(foe, wrong);
        if (same)
        {
            var resumed = W.Spawn(next, "Bandit", W.Bandits, Vector3.UnitX * 100);
            var cold = next.Characters.Cold(resumed.Slot)!;
            cold.PortraitSerial = 901;
            if (lasting != 0) typeof(CharacterCold).GetProperty("LastingId")!.SetValue(cold,
                typeof(CharacterCold).GetProperty("LastingId")!.GetValue(world.Characters.Cold(foe.Slot)!));
        }
        next.RunTicks(181);

        Assert.Equal(same, loaded.Deeds.Record.InFight);
        Assert.Equal(same ? 0 : 1, loaded.Deeds.Record.Member("Leaf").Fights);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_new_rival_fight_only_counts_knockouts_after_the_load(bool freshKnockout)
    {
        var tracker = new LwDeedsSystem([], null);
        tracker.Deeds.Record.FightLook(["Leaf"], []);
        tracker.Deeds.Record.Knockout("Leaf");

        var record = Restore(tracker).Deeds.Record;
        record.FightLook(["Leaf"], []);
        Assert.Null(record.FightLookSinceLoad(["Leaf"], [], 0));
        if (freshKnockout) record.Knockout("Leaf");
        for (int i = 0; i < 4; i++)
        {
            record.FightLook([], []);
            Assert.Null(record.FightLookSinceLoad([], [], 0));
        }
        record.FightLook([], []);
        Assert.Equal(freshKnockout, record.FightLookSinceLoad([], [], 0));
        Assert.True(record.TakeFightEnd()!.Value.Won);
    }

    [Fact]
    public void A_saved_foe_that_loads_after_the_first_look_still_holds_the_fight_open()
    {
        var tracker = new LwDeedsSystem([], null);
        using var world = W.World(tracker);
        var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var foe = W.Spawn(world, "Bandit", W.Bandits, Vector3.UnitX * 100);
        var original = world.Characters.Cold(foe.Slot)!;
        original.PortraitSerial = 902;
        world.Characters.Cold(leaf.Slot)!.InCombat = true;
        world.RunTicks(31);
        tracker.HitResolved(world, W.Hit(world, leaf, foe));

        var loaded = Restore(tracker);
        using var next = W.World(loaded);
        W.Spawn(next, "Leaf", W.Player, Vector3.Zero);
        next.RunTicks(61);
        Assert.True(loaded.Deeds.Record.InFight);
        var resumed = W.Spawn(next, "Bandit", W.Bandits, Vector3.UnitX * 100);
        var cold = next.Characters.Cold(resumed.Slot)!;
        cold.PortraitSerial = 902;
        if (typeof(CharacterCold).GetProperty("LastingId") is { } lasting)
            lasting.SetValue(cold, lasting.GetValue(original));
        next.RunTicks(150);

        Assert.True(loaded.Deeds.Record.InFight);
        Assert.Equal(0, loaded.Deeds.Record.Member("Leaf").Fights);
    }

    static LwDeedsSystem Restore(LwDeedsSystem tracker)
    {
        using var stream = new MemoryStream();
        tracker.Write(stream);
        stream.Position = 0;
        var loaded = new LwDeedsSystem([], null);
        loaded.Read(stream);
        return loaded;
    }
}
