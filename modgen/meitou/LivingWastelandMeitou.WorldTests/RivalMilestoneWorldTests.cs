using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation.Bodies;
using Xunit;

[Collection("settings")]
public sealed class RivalMilestoneWorldTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Three_confirmed_rival_deaths_earn_one_squad_title(bool enabled)
    {
        var settings = LwMod.Settings;
        var option = settings.GetType().GetProperty("RivalMilestones");
        var wasEnabled = option?.GetValue(settings);
        var wasRivals = settings.Rivals;
        try
        {
            option?.SetValue(settings, enabled);
            settings.Rivals = true;
            var rules = new[] { new TitleRule("lw_grudge_enders", "the Grudge-Enders", "squad", "rivals_settled", null, null, 3) };
            var deeds = new LwDeedsSystem(rules, null);
            var rivals = new RivalsSystem(null);
            using var world = W.World(rivals, deeds);
            var leaf = W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
            var stranger = W.Spawn(world, "A stranger", W.Bandits, new Vector3(500, 150, 0));
            var died = typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!;
            var other = W.Spawn(world, "Another rival", W.Bandits, new Vector3(400, 150, 0));
            rivals.Read(W.SavedRival("Another rival", "barely survived your squad", 1, 400, 0));
            world.RunTicks(301);
            deeds.HitResolved(world, W.Hit(world, leaf, other));
            died.SetValue(world.Characters.Cold(other.Slot)!.Medical, true);
            deeds.HitResolved(world, W.Hit(world, stranger, other));
            world.RunTicks(301);
            Assert.Equal(0d, deeds.Deeds.Record.CrewStat("rivals_settled"));

            for (int i = 1; i <= 3; i++)
            {
                string name = $"Rival {i}";
                var target = W.Spawn(world, name, W.Bandits, new Vector3(400, 150, 0));
                rivals.Read(W.SavedRival(name, "barely survived your squad", 1, 400, 0));
                world.RunTicks(301);

                died.SetValue(world.Characters.Cold(target.Slot)!.Medical, true);
                var finishingBlow = W.Hit(world, leaf, target);
                deeds.HitResolved(world, finishingBlow);
                deeds.HitResolved(world, finishingBlow);
                Assert.Equal(enabled ? i : 0, deeds.Deeds.Record.CrewStat("rivals_settled"));
                Assert.Equal(enabled && i == 3, deeds.Deeds.Record.CrewTitles.Contains("lw_grudge_enders"));
            }

            using var save = new MemoryStream();
            deeds.Write(save);
            save.Position = 0;
            var restored = new LwDeedsSystem(rules, null);
            restored.Read(save);
            Assert.Equal(enabled ? 3d : 0d, restored.Deeds.Record.CrewStat("rivals_settled"));
            Assert.Equal(enabled ? new[] { "lw_grudge_enders" } : [], restored.Deeds.Record.CrewTitles);
        }
        finally
        {
            option?.SetValue(settings, wasEnabled);
            settings.Rivals = wasRivals;
        }
    }
}
