using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Xunit;

[Collection("settings")]
public sealed class KindnessAidWorldTests
{
    static readonly Type? itemUse = typeof(World).Assembly.GetType("Meitou.Simulation.Items.ItemUsed");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aid_remembers_one_critical_outsider_and_carries_the_exact_patient_through_a_save(bool squadDownedPatient)
    {
        if (itemUse is null || !LastingActors.Available) return;
        var deeds = new LwDeedsSystem([], W.Data([]));
        using var world = W.World(deeds);
        world.GameHours = 145;
        var medic = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var patient = W.Spawn(world, "Ruka", W.Bandits, Vector3.UnitX);
        var other = W.Spawn(world, "Ruka", W.Bandits, Vector3.UnitZ);
        world.RunTicks(2);
        var cold = world.Characters.Cold(patient.Slot)!;
        cold.Medical!.Blood = 12;
        typeof(MedicalState).GetProperty(nameof(MedicalState.Unconscious))!.SetValue(cold.Medical, true);
        var patientId = LastingActors.Grant(world, patient);
        var otherId = LastingActors.Grant(world, other);
        Assert.NotEqual(patientId, otherId);
        if (squadDownedPatient) deeds.HitResolved(world, W.Hit(world, medic, patient));

        world.Characters.Cold(medic.Slot)!.TreatTarget = patient;
        var kind = Enum.Parse(itemUse.GetProperty("Kind")!.PropertyType, "FirstAid");
        var use = Activator.CreateInstance(itemUse, world.Tick, kind, "kit", medic, null, 0.25f, false)!;
        AidUseWatch.Used(world, use);
        AidUseWatch.Used(world, use);
        using var save = new MemoryStream();
        deeds.Write(save);
        save.Position = 0;
        var restored = new LwDeedsSystem([], W.Data([]));
        restored.Read(save);

        var property = typeof(LwDeeds).GetProperty("Kindness", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var book = property.GetValue(restored.Deeds)!;
        var people = (System.Collections.IEnumerable)book.GetType().GetProperty("People", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;
        var rows = people.Cast<object>().ToArray();
        if (squadDownedPatient) Assert.Empty(rows);
        else
        {
            var row = Assert.Single(rows);
            Assert.Equal(patientId, row.GetType().GetProperty("Actor")!.GetValue(row));
            Assert.Equal("Leaf", row.GetType().GetProperty("Helper")!.GetValue(row));
        }
    }
}
