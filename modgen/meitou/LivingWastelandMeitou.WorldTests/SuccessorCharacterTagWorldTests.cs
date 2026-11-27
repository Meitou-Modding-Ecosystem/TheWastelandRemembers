using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class SuccessorCharacterTagWorldTests
{
    [Fact]
    public void A_named_outlaws_record_not_their_name_earns_the_optional_tag()
    {
        var tags = typeof(World).GetProperty("CharacterTags");
        if (tags is null) return;
        var settings = LwMod.Settings;
        bool before = settings.SavedCharacterTags;
        try
        {
            settings.SavedCharacterTags = true;
            var data = W.Data([W.Rec("old", FcsRecordType.Character, "Hollin")]);
            var camp = new CampCode(0, "Hollin", "old", "old-squad", []);
            using var world = W.World(new LwSystem(null, null, data), new OutlawCampsSystem([camp], null, data));
            var actor = W.Spawn(world, "Renamed Hollin", W.Bandits, Vector3.Zero);
            var namesake = W.Spawn(world, "Hollin", W.Bandits, Vector3.UnitX);
            world.Characters.Cold(actor.Slot)!.RecordId = "old";
            world.Uniques.Bind("old", actor);
            world.RunTicks(155);

            var get = tags.PropertyType.GetMethod("Get", [typeof(CharacterId), typeof(string)])!;
            var registry = tags.GetValue(world)!;
            Assert.Equal("old", get.Invoke(registry, [actor, "LivingWastelandMeitou.outlaw"]));
            Assert.Null(get.Invoke(registry, [namesake, "LivingWastelandMeitou.outlaw"]));
        }
        finally { settings.SavedCharacterTags = before; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_recognised_successor_gets_a_saved_character_tag_without_claiming_their_namesake(bool growingBounties)
    {
        var registry = typeof(World).GetProperty("CharacterTags");
        if (registry is null) return;
        var set = registry.PropertyType.GetMethod("Set")!;
        var get = registry.PropertyType.GetMethod("Get", [typeof(CharacterId), typeof(string)])!;
        var settings = LwMod.Settings;
        var before = (settings.GrowingBounties, settings.OutlawSuccessors);
        try
        {
            settings.GrowingBounties = growingBounties;
            settings.OutlawSuccessors = true;
            var data = W.Data([W.Rec("successor", FcsRecordType.Character, "Hollin")]);
            var camp = new CampCode(0, "Old outlaw", "old", "old-squad", [], Successor: new("Hollin", "successor", "successor-squad"));
            var state = new LwSystem(null, null, data);
            using var world = W.World(state, new OutlawCampsSystem([camp], null, data));
            var actor = W.Spawn(world, "Hollin", W.Bandits, Vector3.Zero);
            var other = W.Spawn(world, "Hollin", W.Bandits, Vector3.UnitX);
            world.Characters.Cold(actor.Slot)!.RecordId = "successor";
            world.Uniques.Bind("successor", actor);
            world.RunTicks(2);
            LwState.For(world).Core.Set("@succup 0", 1);

            var tags = registry.GetValue(world)!;
            Assert.True(set.Invoke(tags, [actor, "test.foreign", "traveller"]) is true);
            world.RunTicks(305);
            Assert.Equal("successor:successor", get.Invoke(tags, [actor, "LivingWastelandMeitou"]));
            Assert.Equal("traveller", get.Invoke(tags, [actor, "test.foreign"]));
            Assert.Null(get.Invoke(tags, [other, "LivingWastelandMeitou"]));
            Assert.NotEqual(0UL, LastingActors.Of(world.Characters.Cold(actor.Slot)!));
        }
        finally { (settings.GrowingBounties, settings.OutlawSuccessors) = before; }
    }
}
