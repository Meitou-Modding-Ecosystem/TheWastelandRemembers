using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Xunit;

[Collection("settings")]
public sealed class AtlasPortraitWorldTests
{
    [Fact]
    public void Named_atlas_portraits_follow_a_verified_live_actor_and_keep_the_record_fallback()
    {
        if (typeof(UiNode).GetProperty("PortraitOf") is null) return;
        var settings = LwMod.Settings;
        var enabled = typeof(LwSettings).GetProperty("LiveAtlasPortraits");
        Assert.NotNull(enabled);
        var before = (settings.Atlas, enabled.GetValue(settings), RecordHost.AtlasPortraits);
        try
        {
            settings.Atlas = true;
            enabled.SetValue(settings, true);
            var atlas = new AtlasSystem(new AtlasTable(People: new() { ["Guard"] = new(Id: "named-guard", Epithet: "guard") }), null, null);
            using var world = W.World(atlas);
            W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            var actor = W.Spawn(world, "Guard", W.Bandits, Vector3.UnitX * 50);
            world.Characters.Cold(actor.Slot)!.RecordId = "named-guard";
            W.Spawn(world, "Guard", W.Bandits, Vector3.UnitX * 100);
            world.Uniques.Bind("named-guard", actor);
            world.RunTicks(160);

            var faceType = typeof(LwMod).Assembly.GetType("LivingWasteland.MeitouMod.AtlasLiveFaces")!;
            var find = faceType.GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.Equal(actor, find.Invoke(null, ["w:Guard"]));
            Assert.Equal("named-guard", RecordHost.AtlasPortraits["w:Guard"]);

            typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!.SetValue(world.Characters.Cold(actor.Slot)!.Medical, true);
            world.RunTicks(65);
            Assert.Null(find.Invoke(null, ["w:Guard"]));
            Assert.Equal("named-guard", RecordHost.AtlasPortraits["w:Guard"]);
        }
        finally
        {
            settings.Atlas = before.Atlas;
            enabled.SetValue(settings, before.Item2);
            RecordHost.AtlasPortraits = before.AtlasPortraits;
        }
    }
}
