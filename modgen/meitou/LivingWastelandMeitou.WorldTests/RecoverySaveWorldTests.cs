using System.Collections;
using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;
using SimWorld = Meitou.Simulation.World;

[Collection("settings")]
public sealed class RecoverySaveWorldTests
{
    static readonly Vector3 At = new(14000, 150, 14000);

    [Fact]
    public void A_reloaded_report_keeps_its_cell_delay_and_single_follow_up()
    {
        if (!PrisonerHandover.Available) return;
        const string record = "sayo", original = "outpost:old", reported = "outpost:new", cell = "new-cell";
        const int episode = 7;
        var leads = new RecoveryLeads();
        leads.Dispatched(record, episode, original, followUp: false);

        using var first = W.World();
        var before = Held(first, record, episode, cell);
        W.Spawn(first, "Sand Ninja witness", W.Bandits, At + Vector3.UnitX * 40);
        first.RunTicks(1);
        Assert.False(leads.CanFollow(first, before, reported));

        using var report = new MemoryStream();
        using (var writer = new BinaryWriter(report, System.Text.Encoding.UTF8, leaveOpen: true)) leads.Write(writer);
        report.Position = 0;
        var restored = new RecoveryLeads();
        using (var reader = new BinaryReader(report, System.Text.Encoding.UTF8, leaveOpen: true)) restored.Read(reader);
        using var next = W.World();
        W.Spawn(next, "Different actor in old slot", W.Bandits, At + Vector3.UnitZ * 600);
        var after = Held(next, record, episode, cell);
        Assert.NotEqual(before.Actor, after.Actor);
        next.GameHours = first.GameHours + 5.9;
        Assert.False(restored.CanFollow(next, after, reported));
        next.GameHours = first.GameHours + 6.1;
        Assert.True(restored.CanFollow(next, after, reported));

        // A report of the previous cell is insufficient once the same captive is found in a different cell.
        var shifted = Held(next, record, episode, "other-cell");
        Assert.False(restored.CanFollow(next, shifted, reported));
        Assert.False(restored.CanFollow(next, after, reported));

        W.Spawn(next, "Second witness", W.Bandits, At + Vector3.UnitZ * 30);
        next.RunTicks(1);
        Assert.False(restored.CanFollow(next, shifted, reported));
        next.GameHours += 6.1;
        Assert.True(restored.CanFollow(next, shifted, reported));
        restored.Dispatched(record, episode, reported, followUp: true);
        Assert.False(restored.CanFollow(next, shifted, reported));
        Assert.False(restored.CanFollow(next, shifted, "outpost:third"));
    }

    [Fact]
    public void A_clue_survives_a_reload_and_only_barks_once()
    {
        if (!PrisonerHandover.Available) return;
        bool old = LwMod.Settings.PrisonerRecovery;
        LwMod.Settings.PrisonerRecovery = true;
        try
        {
            var clues = new RecoveryClues();
            using var first = W.World(clues);
            var gate = RecoveryGateWorldTests.Gate(first, "exit", 1, At);
            gate.Lock!.Locked = false;
            RecoveryClues.Notice(first, 5, gate, RecoveryClues.Kind.OpenLock);
            first.RunTicks(150);
            Assert.Empty(Barks(first));

            using var pending = PrisonerDealsWorldTests.Save(clues);
            var resumed = new RecoveryClues();
            resumed.Read(pending);
            using var second = W.World(resumed);
            var secondGate = RecoveryGateWorldTests.Gate(second, "exit", 9, At);
            var speaker = W.Spawn(second, "Guard", W.Player, At);
            second.RunTicks(150);
            Assert.Empty(Barks(second));  // The fixture is still locked in this scene.
            secondGate.Lock!.Locked = false;
            second.RunTicks(150);
            Assert.Contains("been at this lock", Barks(second)[speaker]!.ToString());

            using var heard = PrisonerDealsWorldTests.Save(resumed);
            var finished = new RecoveryClues();
            finished.Read(heard);
            using var third = W.World(finished);
            RecoveryGateWorldTests.Gate(third, "exit", 10, At).Lock!.Locked = false;
            W.Spawn(third, "Another guard", W.Player, At);
            third.RunTicks(300);
            Assert.Empty(Barks(third));
        }
        finally
        {
            LwMod.Settings.PrisonerRecovery = old;
        }
    }

    static PrisonerCases.Held Held(SimWorld world, string record, int episode, string cell)
    {
        var actor = W.Spawn(world, "Sayo", W.Bandits, At);
        var cage = PrisonerDealsWorldTests.Hold(world, actor, cell, 1, At);
        return new(episode, record, "Sayo", "the outpost", world.GameHours, actor, world.Characters.Cold(actor.Slot)!,
            cage, world.Buildings.Find(cage.Building)!);
    }

    static IDictionary Barks(SimWorld world) => (IDictionary)typeof(SimWorld).GetField("_barks",
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
}
