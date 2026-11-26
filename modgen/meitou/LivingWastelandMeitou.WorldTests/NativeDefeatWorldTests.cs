using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Dialogue;
using Xunit;

[Collection("settings")]
public sealed class NativeDefeatWorldTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Victory_fallback_yields_to_native_dialogue_for_the_same_opponent(bool native, bool otherTarget)
    {
        if (native && typeof(World).Assembly.GetType("Meitou.Simulation.Combat.SquadDefeat") is null) return;
        var settings = LwMod.Settings;
        var (old, log, changes) = (settings.VictoryLines, RecordHost.Log, RecordHost.LogChanges);
        var lines = new List<string>();
        try
        {
            settings.VictoryLines = true;
            RecordHost.Log = lines.Add;
            RecordHost.LogChanges = () => true;
            var data = W.Data([]);
            var tracker = new LwDeedsSystem([], data) { Talk = new DialogueSystem(data) };
            using var world = W.World(tracker);
            var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            var foe = W.Spawn(world, "Raider", W.Bandits, Vector3.UnitX * 100);
            var bystander = W.Spawn(world, "Other raider", W.Bandits, Vector3.UnitX * 200);
            world.Characters.Cold(leaf.Slot)!.InCombat = true;
            world.RunTicks(31);
            var dead = world.Characters.Cold(foe.Slot)!.Medical!;
            typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!.SetValue(dead, true);
            tracker.HitResolved(world, W.Hit(world, leaf, foe) with { Source = native ? CombatHitSource.Melee : CombatHitSource.Projectile });
            if (native) world.Defer(new DialogueEvent(leaf, otherTarget ? bystander : foe, 15) { Tick = world.Tick + 1 });
            world.Characters.Cold(leaf.Slot)!.InCombat = false;
            world.RunTicks(215);

            Assert.Equal(1, tracker.Deeds.Record.Member("Leaf").FightsWon);
            Assert.Equal(native && !otherTarget ? 0 : 1, lines.Count(line => line.StartsWith("victory line:")));
        }
        finally
        {
            settings.VictoryLines = old;
            RecordHost.Log = log;
            RecordHost.LogChanges = changes;
        }
    }
}
