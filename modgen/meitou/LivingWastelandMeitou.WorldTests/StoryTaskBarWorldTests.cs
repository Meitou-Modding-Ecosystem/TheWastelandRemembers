using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Items;
using Xunit;

[Collection("settings")]
public sealed class StoryTaskBarWorldTests
{
    static readonly PropertyInfo? bar = typeof(CharacterCold).GetProperty("ModTaskBar");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_mourner_gets_one_bar_and_a_foreign_bar_is_left_alone(bool foreignBar)
    {
        if (bar is null) return;
        const string keepsake = "keepsake-test";
        bool trophies = LwMod.Settings.Trophies, memorials = LwMod.Settings.Memorials;
        var previousKeepsake = Trophies.Keepsake;
        try
        {
            Trophies.Keepsake = keepsake;
            LwMod.Settings.Trophies = LwMod.Settings.Memorials = true;
            var data = W.Data([W.Rec(keepsake, FcsRecordType.Item, "Keepsake of the Fallen")]);
            using var world = W.World(new LwSystem(null, null, data), new LwDeedsSystem([], data));
            var bag = new Inventory();
            var heir = W.Spawn(world, "Leaf", W.Player, Vector3.Zero, bag);
            var fallen = W.Spawn(world, "Hollin", W.Player, Vector3.UnitX);
            var cold = world.Characters.Cold(heir.Slot)!;
            world.RunTicks(2);
            var die = typeof(MedicalState).GetMethod("Die", BindingFlags.Instance | BindingFlags.NonPublic)!;
            die.Invoke(world.Characters.Cold(fallen.Slot)!.Medical!, [DeathCause.Starvation, null]);

            object other = Activator.CreateInstance(Nullable.GetUnderlyingType(bar.PropertyType)!, ["Another mod", .4f])!;
            if (foreignBar) bar.SetValue(cold, other);
            world.RunTicks(29);
            Assert.Single(LwDeeds.For(world)!.Record.FallenMembers);
            if (foreignBar) Assert.Equal(other, bar.GetValue(cold));
            else Assert.Contains("Remembering Hollin", bar.GetValue(cold)?.ToString() ?? "");

            Assert.Equal(foreignBar ? 1 : 0, bag.Items.Count);
            world.RunTicks(30);
            Assert.Equal(foreignBar ? other : null, bar.GetValue(cold));
            Assert.Equal(keepsake, Assert.Single(bag.Items).Record);
        }
        finally
        {
            LwMod.Settings.Trophies = trophies;
            LwMod.Settings.Memorials = memorials;
            Trophies.Keepsake = previousKeepsake;
        }
    }
}
