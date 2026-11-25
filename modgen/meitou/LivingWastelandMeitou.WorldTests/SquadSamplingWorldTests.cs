using System.Numerics;
using LivingWasteland.MeitouMod;
using Xunit;

[Collection("display cadence")]
public sealed class SquadSamplingWorldTests
{
    [Fact]
    public void Both_panels_match_their_full_scan_after_the_squad_membership_changes()
    {
        var settings = LwMod.Settings;
        var previous = (settings.LighterChecks, settings.RecordInPanels);
        try
        {
            settings.LighterChecks = false;
            settings.RecordInPanels = true;
            var system = new LwDeedsSystem([], null);
            using var world = W.World(system);
            var at = new Vector3(0, 150, 0);
            var leaf = W.Spawn(world, "Leaf", W.Player, at);
            W.Spawn(world, "Outsider", W.Bandits, at);
            var ruka = W.Spawn(world, "Ruka", W.Player, at);
            var record = system.Deeds.Record;
            record.Kill("Leaf", "Dust Bandits", false, null);
            record.Kill("Ruka", "Dust Bandits", false, null);
            world.RunTicks(1);
            Assert.NotNull(PanelLines.ForCharacter(leaf));

            world.Characters.Cold(leaf.Slot)!.IsPlayer = false;
            var beep = W.Spawn(world, "Beep", W.Player, at);
            record.Kill("Beep", "Dust Bandits", false, null);
            world.RunTicks(90);

            Assert.Equal(StatsPanel.Text(system.Deeds, world, new HashSet<string>()), RecordHost.Panel);
            Assert.DoesNotContain("-- Leaf --", RecordHost.Panel);
            Assert.Contains("-- Ruka --", RecordHost.Panel);
            Assert.Contains("-- Beep --", RecordHost.Panel);
            Assert.Null(PanelLines.ForCharacter(leaf));
            var rukaRows = PanelLines.ForCharacter(ruka);
            var beepRows = PanelLines.ForCharacter(beep);
            Assert.NotNull(rukaRows);
            Assert.NotNull(beepRows);
            PanelLines.Publish(world, system.Deeds);
            Assert.Equal(rukaRows, PanelLines.ForCharacter(ruka));
            Assert.Equal(beepRows, PanelLines.ForCharacter(beep));
        }
        finally
        {
            (settings.LighterChecks, settings.RecordInPanels) = previous;
        }
    }
}
