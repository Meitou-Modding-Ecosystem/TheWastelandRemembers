using System.Numerics;
using System.Runtime.CompilerServices;
using LivingWasteland.MeitouMod;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Xunit;

[Collection("settings")]
public sealed class CompanionMemorialWorldTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_squad_animal_gets_one_memorial_without_a_human_keepsake(bool enabled)
    {
        var setting = typeof(LwSettings).GetProperty("AnimalCompanions");
        Assert.NotNull(setting);
        var settings = LwMod.Settings;
        var before = setting.GetValue(settings);
        try
        {
            setting.SetValue(settings, enabled);
            var state = new LwSystem(null, null, null);
            var tracker = new LwDeedsSystem([], null);
            using var world = W.World(state, tracker);
            var dog = AddAnimal(world, "Hound", 101);
            AddAnimal(world, "Hound", 102);
            world.RunTicks(31);
            var medical = world.Characters.Cold(dog.Slot)!.Medical!;
            typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!.SetValue(medical, true);
            world.RunTicks(181);

            Assert.Empty(tracker.Deeds.Record.FallenMembers);
            Assert.Equal(enabled, RecordHost.FallenText.Contains("Hound, a companion of the squad", StringComparison.Ordinal));
            Assert.Equal(enabled ? 1 : 0,
                tracker.Deeds.Record.History.Count(h => h.Text == "Hound, a companion of the squad, died in the wilds."));
            var bytes = Save(state);
            var restored = new LwSystem(null, null, null);
            Read(restored, bytes);
            Assert.Equal(bytes, Save(restored));
        }
        finally { setting.SetValue(settings, before); }
    }

    [Fact]
    public void A_companion_title_records_distinct_survived_fights_across_a_reload()
    {
        var setting = typeof(LwSettings).GetProperty("AnimalCompanions");
        Assert.NotNull(setting);
        var settings = LwMod.Settings;
        var before = setting.GetValue(settings);
        try
        {
            setting.SetValue(settings, true);
            var state = new LwSystem(null, null, null);
            var tracker = new LwDeedsSystem([], null);
            using var world = W.World(state, tracker);
            var dog = AddAnimal(world, "Hound", 101);
            var other = AddAnimal(world, "Hound", 102);
            for (int i = 0; i < 9; i++) Fight(world, dog);
            var record = tracker.Deeds.Record;
            Assert.Equal(9, record.Member(deedsKey(dog)).Fights);
            Assert.DoesNotContain("companion-battle-tested", record.Member(deedsKey(dog)).Titles);

            var resumedState = new LwSystem(null, null, null);
            var resumedTracker = new LwDeedsSystem([], null);
            Read(resumedState, Save(state));
            Read(resumedTracker, Save(tracker));
            using var reloaded = W.World(resumedState, resumedTracker);
            var otherAgain = AddAnimal(reloaded, "Hound", 102);
            var dogAgain = AddAnimal(reloaded, "Hound", 101);
            Assert.Equal(dog, otherAgain);
            Assert.Equal(other, dogAgain);
            Fight(reloaded, dogAgain);
            reloaded.RunTicks(100); // publish the next panel after the fight settles

            var retained = resumedTracker.Deeds.Record;
            Assert.Contains("companion-battle-tested", retained.Member(deedsKey(dog)).Titles);
            Assert.DoesNotContain("companion-battle-tested", retained.Member(deedsKey(other)).Titles);
            Assert.Contains("Battle-tested Companion", RecordHost.Panel);
            Assert.Single(retained.History, h => h.Text.Contains("Battle-tested Companion", StringComparison.Ordinal));

            string deedsKey(CharacterId id) => tracker.Deeds.Who(world.Characters.Cold(id.Slot)!);
        }
        finally { setting.SetValue(settings, before); }
    }

    static CharacterId AddAnimal(World world, string name, uint serial)
    {
        var id = W.Spawn(world, name, W.Player, new Vector3(500, 150, 500));
        var cold = world.Characters.Cold(id.Slot)!;
        cold.PortraitSerial = serial;
        var animal = typeof(CharacterCold).GetProperty(nameof(CharacterCold.Animal))!;
        animal.SetValue(cold, RuntimeHelpers.GetUninitializedObject(animal.PropertyType));
        return id;
    }

    static void Fight(World world, CharacterId id)
    {
        var active = world.Characters.Cold(id.Slot)!;
        active.InCombat = true;
        world.RunTicks(90);
        active.InCombat = false;
        world.RunTicks(180);
    }

    static byte[] Save(IModSaveData system)
    {
        using var stream = new MemoryStream();
        system.Write(stream);
        return stream.ToArray();
    }

    static void Read(IModSaveData system, byte[] data)
    {
        using var stream = new MemoryStream(data);
        system.Read(stream);
    }
}
