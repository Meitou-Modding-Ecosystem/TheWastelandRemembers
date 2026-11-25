using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation.Movement;
using Xunit;

[CollectionDefinition("display cadence", DisableParallelization = true)]
public sealed class DisplayCadenceCollection { }

[Collection("display cadence")]
public sealed class LighterChecksWorldTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_the_display_refresh_interval_changes(bool lighter)
    {
        Assert.False(new LwSettings().LighterChecks);
        var settings = LwMod.Settings;
        var previous = (settings.LighterChecks, settings.Atlas, RecordHost.AtlasList, RecordHost.HistoryText);
        var previousAtlas = (RecordHost.AtlasPages, RecordHost.AtlasPortraits);
        var previousRecord = (RecordHost.Panel, RecordHost.CatalogText, RecordHost.FallenText);
        try
        {
            settings.LighterChecks = lighter;
            settings.Atlas = true;
            var data = W.Data([]);
            var deeds = new LwDeedsSystem([], data);
            using var world = W.World(deeds, new AtlasSystem(new AtlasTable(), null, data));
            world.RunTicks(8);
            const string awaitingRefresh = "Awaiting refresh";
            RecordHost.AtlasList = RecordHost.HistoryText = awaitingRefresh;
            var at = new Vector3(0, 150, 0);
            var leaf = W.Spawn(world, "Leaf", W.Player, at);
            world.Characters.Place(leaf.Slot, at, 0, CharacterTask.Rest);

            world.RunTicks(83); // through tick 90
            Assert.Contains("Leaf", deeds.Deeds.Record.Members.Keys);
            Assert.NotEqual(awaitingRefresh, RecordHost.AtlasList);
            Assert.Equal(lighter, RecordHost.HistoryText == awaitingRefresh);

            world.RunTicks(90); // through tick 180
            Assert.NotEqual(awaitingRefresh, RecordHost.AtlasList);
            Assert.NotEqual(awaitingRefresh, RecordHost.HistoryText);
        }
        finally
        {
            (settings.LighterChecks, settings.Atlas, RecordHost.AtlasList, RecordHost.HistoryText) = previous;
            (RecordHost.AtlasPages, RecordHost.AtlasPortraits) = previousAtlas;
            (RecordHost.Panel, RecordHost.CatalogText, RecordHost.FallenText) = previousRecord;
        }
    }
}
