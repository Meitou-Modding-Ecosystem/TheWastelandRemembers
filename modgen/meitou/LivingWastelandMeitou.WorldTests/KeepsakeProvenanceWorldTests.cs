using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Saving;
using Xunit;

[Collection("settings")]
public sealed class KeepsakeProvenanceWorldTests
{
    [Fact]
    public void A_keepsakes_story_survives_a_transfer_slot_reuse_and_an_inventory_reload()
    {
        if (typeof(ItemInstance).GetProperty("ModTag") is null) return;
        const string template = "keepsake-t";
        var slots = KeepsakeBook.Slots;
        var previousTemplate = Trophies.Keepsake;
        bool enabled = LwMod.Settings.KeepsakeProvenance, trophies = LwMod.Settings.Trophies, tags = LwMod.Settings.SavedCharacterTags;
        try
        {
            KeepsakeBook.Slots = [template];
            Trophies.Keepsake = template;
            LwMod.Settings.Trophies = LwMod.Settings.KeepsakeProvenance = true;
            LwMod.Settings.SavedCharacterTags = true;
            var data = W.Data([W.Rec(template, FcsRecordType.Item, "Keepsake of the Fallen")]);
            var deeds = new LwDeedsSystem([], data);
            var book = new KeepsakeBook(data);
            var world = W.World(new LwSystem(null, null, data), deeds, book);
            var bag = new Inventory();
            var heir = W.Spawn(world, "Leaf", W.Player, Vector3.Zero, bag);
            world.RunTicks(31);
            var record = LwDeeds.For(world)!.Record;
            record.Died("Hollin", 5, "Stack", null, null, null);
            Trophies.GiveKeepsake(world, heir, "Leaf", "Hollin", "Hollin");
            var hollin = Assert.Single(bag.Items);
            if (typeof(World).GetProperty("CharacterTags") is { } registry)
                Assert.Equal("Hollin", registry.PropertyType.GetMethod("Get", [typeof(CharacterId), typeof(string)])!
                    .Invoke(registry.GetValue(world), [heir, "LivingWastelandMeitou.keepsake"]));

            world.GameHours += 2;
            record.Died("Kite", 7, "Squin", null, null, null);
            Trophies.GiveKeepsake(world, heir, "Leaf", "Kite", "Kite");
            var kite = Assert.Single(bag.Items, i => i != hollin);
            Assert.NotNull(ItemUses.Tag(hollin));
            Assert.NotEqual(ItemUses.Tag(kite), ItemUses.Tag(hollin));
            Assert.Equal("Keepsake of Kite", data.Db.Find(template)!.Name);

            var storage = new Inventory();
            Assert.True(bag.Items.Remove(hollin));
            Assert.True(InventoryCompat.Add(storage, hollin));
            using var savedDeeds = new MemoryStream();
            using var savedBook = new MemoryStream();
            deeds.Write(savedDeeds);
            book.Write(savedBook);
            savedDeeds.Position = savedBook.Position = 0;

            var restoredDeeds = new LwDeedsSystem([], data);
            restoredDeeds.Read(savedDeeds);
            var restoredBook = new KeepsakeBook(data);
            restoredBook.Read(savedBook);
            var otherWorld = W.World(restoredDeeds, restoredBook);
            var restoredStorage = SavedKit.ToInventory(SavedKit.FromInventory(storage), data.Items);
            var passedOn = Assert.Single(restoredStorage.Items);
            Assert.Equal(ItemUses.Tag(hollin), ItemUses.Tag(passedOn));
            Assert.True(restoredStorage.Items.Remove(passedOn));
            var newBag = new Inventory();
            Assert.True(InventoryCompat.Add(newBag, passedOn));
            var otherHeir = W.Spawn(otherWorld, "Ruka", W.Player, Vector3.One, newBag);
            otherWorld.RunTicks(31);
            Assert.Equal("Keepsake of Hollin", passedOn.Name);
            LwMod.Settings.KeepsakeProvenance = false;   // a tagged item remains readable after switching issuance off
            otherWorld.TakeMessages();
            Assert.True(ItemUses.Use(otherWorld, otherHeir, passedOn));
            Assert.Contains(otherWorld.TakeMessages(), m => m.Contains("It belonged to Hollin") &&
                m.Contains("Ruka has kept it since") && m.Contains("at Stack"));
            Assert.Equal("Keepsake of Kite", data.Db.Find(template)!.Name);

            var untagged = data.Items.Create(template)!;
            Assert.True(ItemUses.Use(otherWorld, otherHeir, untagged));
            Assert.Contains(otherWorld.TakeMessages(), m => m.Contains("the keepsake of Kite"));

            ItemUses.SetTag(untagged, "lw:keepsake:v1:Missing");
            Assert.False(ItemUses.Use(otherWorld, otherHeir, untagged));
            Assert.Empty(otherWorld.TakeMessages());
        }
        finally
        {
            KeepsakeBook.Slots = slots;
            Trophies.Keepsake = previousTemplate;
            LwMod.Settings.Trophies = trophies;
            LwMod.Settings.KeepsakeProvenance = enabled;
            LwMod.Settings.SavedCharacterTags = tags;
        }
    }
}
