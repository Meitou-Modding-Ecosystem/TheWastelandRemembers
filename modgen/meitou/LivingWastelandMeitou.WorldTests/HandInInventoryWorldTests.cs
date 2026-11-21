using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Items;
using Xunit;

[Collection("settings")]
public sealed class HandInInventoryWorldTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 0)]
    public void Taking_three_items_leaves_inventory_intact_if_there_are_too_few(int offered, int remaining)
    {
        const string item = "hand-in-item";
        var data = W.Data([W.Rec(item, FcsRecordType.Item, "Quest materials")]);
        var world = W.World();
        var bag = new Inventory();
        var target = W.Spawn(world, "Leaf", W.Player, Vector3.Zero, bag);
        var speaker = W.Spawn(world, "Giver", W.Bandits, Vector3.One);
        for (int i = 0; i < offered; i++) Assert.True(InventoryCompat.Add(bag, data.Items.Create(item)!));
        world.RunTicks(2);

        var effect = new DialogueAct(world, speaker, target, default);
        typeof(LwMod).GetMethod("TakeItems", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [effect, item, 3]);

        Assert.Equal(remaining, bag.All().Where(i => i.Record == item).Sum(i => i.Quantity));
    }
}
