using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Items;
using Xunit;

[Collection("settings")]
public sealed class BaSaveWorldTests
{
    static void SetMedical(Meitou.Simulation.World world, CharacterId who, string property, bool value) =>
        typeof(MedicalState).GetProperty(property)!.SetValue(world.Characters.Cold(who.Slot)!.Medical, value);

    static MemoryStream Save(LwDeedsSystem deeds)
    {
        var save = new MemoryStream();
        deeds.Write(save);
        save.Position = 0;
        return save;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_named_fighters_bleed_out_remembers_whether_the_blow_was_sport(bool sport)
    {
        const string fighter = "unique-arena-fighter";
        var stack = new Vector3(10000, 150, 10000);
        var town = W.Rec("t-stack", FcsRecordType.Town, "Stack");
        town.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        var data = W.Data([town], [new TownPlacement("i-stack", town.StringId, stack, null)]);
        var deeds = new LwDeedsSystem([], data);
        var world = W.World(deeds);
        var leaf = W.Spawn(world, "Leaf", W.Player, stack);
        var npc = W.Spawn(world, "Fighter", W.Bandits, stack + Vector3.UnitX);
        world.Uniques.Bind(fighter, npc);
        world.RunTicks(31);
        try
        {
            if (sport) ArenaLink.Bind(id => id == leaf || id == npc, _ => false);
            deeds.HitResolved(world, W.Hit(world, leaf, npc));
        }
        finally { ArenaLink.Reset(); }

        using var save = Save(deeds);
        var loaded = new LwDeedsSystem([], data);
        loaded.Read(save);
        var next = W.World(loaded);
        next.GameHours = world.GameHours;
        next.Uniques.Died(fighter, byPlayer: false, faction: W.Bandits);
        next.RunTicks(31);
        var record = LwDeeds.For(next)!;
        Assert.Equal(1, record.Record.Members.Values.Sum(m => m.Kills));
        Assert.Equal(!sport, record.Heat.Here("Stack") > 0);
    }

    [Fact]
    public void The_pending_keepsake_survives_a_save_and_waits_for_a_standing_squadmate()
    {
        const string keepsake = "keepsake";
        var data = W.Data([W.Rec(keepsake, FcsRecordType.Item, "Keepsake of the Fallen")]);
        var deeds = new LwDeedsSystem([], data);
        var world = W.World(new LwSystem(null, null, data), deeds);
        var heir = W.Spawn(world, "Leaf", W.Player, Vector3.Zero, new Inventory());
        var body = W.Spawn(world, "Kite", W.Player, Vector3.One);
        SetMedical(world, heir, nameof(MedicalState.Unconscious), true);
        SetMedical(world, body, nameof(MedicalState.Dead), true);
        world.RunTicks(31);
        Assert.Single(LwDeeds.For(world)!.Record.FallenMembers);

        using var save = Save(deeds);
        var loaded = new LwDeedsSystem([], data);
        loaded.Read(save);
        var next = W.World(new LwSystem(null, null, data), loaded);
        next.GameHours = world.GameHours + 2;
        var returning = W.Spawn(next, "Leaf", W.Player, Vector3.Zero, new Inventory());
        SetMedical(next, returning, nameof(MedicalState.Unconscious), true);
        var old = Trophies.Keepsake;
        try
        {
            Trophies.Keepsake = keepsake;
            next.RunTicks(31);
            Assert.DoesNotContain(next.TakeMessages(), m => m.Contains("keeps something"));
            SetMedical(next, returning, nameof(MedicalState.Unconscious), false);
            next.RunTicks(31);
            Assert.Single(next.TakeMessages(), m => m.Contains("keeps something of Kite"));
            next.RunTicks(31);
            Assert.DoesNotContain(next.TakeMessages(), m => m.Contains("keeps something"));
        }
        finally { Trophies.Keepsake = old; }
    }
}
