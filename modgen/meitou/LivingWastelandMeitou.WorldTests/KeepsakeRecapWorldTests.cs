using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation.Items;
using Xunit;

[Collection("settings")]
public sealed class KeepsakeRecapWorldTests
{
    [Fact]
    public void A_keepsake_does_not_offer_a_photo_until_its_heir_receives_it()
    {
        bool trophies = LwMod.Settings.Trophies;
        var previousKeepsake = Trophies.Keepsake;
        var recapEnabled = RecapLink.Enabled;
        try
        {
            const string template = "keepsake-recap";
            Trophies.Keepsake = template;
            LwMod.Settings.Trophies = true;
            RecapLink.Enabled = () => true;
            var data = W.Data([W.Rec(template, FcsRecordType.Item, "Keepsake")]);
            using var world = W.World(new LwSystem(null, null, data));
            var at = new Vector3(600, 150, 300);
            var bag = new Inventory();
            var heir = W.Spawn(world, "Leaf", W.Player, at, bag);
            var noBag = W.Spawn(world, "Kite", W.Player, at + Vector3.UnitX);
            world.RunTicks(2);
            var captured = Recap.RecapApi.Captured = [];

            Trophies.GiveKeepsake(world, noBag, "Kite", "Hollin", "Hollin");
            Assert.Empty(captured);
            Trophies.GiveKeepsake(world, heir, "Leaf", "Hollin", "Hollin");
            Assert.Equal(template, Assert.Single(bag.Items).Record);
            Assert.Equal((at, true), Assert.Single(captured));
        }
        finally
        {
            Recap.RecapApi.Captured = null;
            RecapLink.Enabled = recapEnabled;
            LwMod.Settings.Trophies = trophies;
            Trophies.Keepsake = previousKeepsake;
        }
    }
}
