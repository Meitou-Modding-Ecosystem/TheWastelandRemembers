using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;
using Xunit;
using SimWorld = Meitou.Simulation.World;

[Collection("settings")]
public sealed class ReputationWorldTests
{
    static readonly Vector3 TownAt = new(10000, 150, 10000);
    const string OldOwner = "Shek Kingdom", NewOwner = "United Cities", Town = "Squin", Node = "town:i-squin";

    static PopulationData Data()
    {
        var oldFaction = W.Rec("f-old", FcsRecordType.Faction, OldOwner);
        var newFaction = W.Rec("f-new", FcsRecordType.Faction, NewOwner);
        var town = W.Rec("t-old", FcsRecordType.Town, Town);
        var replacement = W.Rec("t-new", FcsRecordType.Town, Town);
        town.References["faction"] = [new FcsReference(oldFaction.StringId, 0, 0, 0)];
        replacement.References["faction"] = [new FcsReference(newFaction.StringId, 0, 0, 0)];
        return W.Data([oldFaction, newFaction, town, replacement], [new TownPlacement("i-squin", town.StringId, TownAt, null),
             new TownPlacement("j-squin", town.StringId, TownAt + new Vector3(4000, 0, 0), null)]);
    }

    [Fact]
    public void The_engines_quote_uses_the_current_owner_without_inheriting_the_old_owners_heat()
    {
        var engineHook = typeof(SimWorld).Assembly.GetType("Meitou.Simulation.Trading.ITradePriceFilter");
        var adapter = TradeReputation.Build();
        if (engineHook is null) { Assert.Null(adapter); return; }
        Assert.NotNull(adapter);
        var data = Data();
        int before = data.FactionIndex("f-old"), after = data.FactionIndex("f-new");
        var title = new TitleRule("test-title", "A Known Blade", "squad", "kills", null, null, 1);
        var deeds = new LwDeedsSystem([title], data);
        var companion = ObserverEmit.Build(engineHook, "Price", typeof(ReputationWorldTests).GetMethod(nameof(CompanionPrice))!, "TestTradeFilter");
        var world = W.World(deeds, (ITickSystem)Activator.CreateInstance(companion!)!, new TradeReputation(data),
            (ITickSystem)Activator.CreateInstance(adapter!)!);
        var player = W.Spawn(world, "Leaf", W.Player, TownAt);
        var trader = new Squad { Id = world.Squads.NextId(), TemplateId = "shek-shop", Faction = before, Town = 0 };
        world.Squads.Add(trader);
        var trader2 = new Squad { Id = world.Squads.NextId(), TemplateId = "cities-shop", Faction = after, Town = 0 };
        world.Squads.Add(trader2);
        var elsewhere = new Squad { Id = world.Squads.NextId(), TemplateId = "other-shek-shop", Faction = before, Town = 1 };
        world.Squads.Add(elsewhere);
        var clerk = W.Spawn(world, "Shopkeeper", after, TownAt, squad: trader2.Id);
        world.RunTicks(31);

        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var filters = typeof(SimWorld).GetProperty("TradePriceFilters", flags)!.GetValue(world)!;
        Assert.True((bool)filters.GetType().GetProperty("Active", flags)!.GetValue(filters)!);
        var pass = filters.GetType().GetMethod("Pass", flags)!;
        var dealType = engineHook.GetMethod("Price")!.GetParameters()[1].ParameterType.GetElementType()!;
        long Quote(Squad squad, bool sell = false, long @base = 100) =>
            (long)pass.Invoke(filters, [world, Activator.CreateInstance(dealType, world.Tick, "item", 1, sell, player, squad.Id), @base])!;
        var oldViews = LwMod.TitleViews;
        bool previous = LwMod.Settings.ReputationPrices;
        try
        {
            LwMod.TitleViews = new() { [OldOwner] = new() { ["fears"] = [title.Name] } };
            var record = LwDeeds.For(world)!;
            record.Record.SetCrewStat("kills", 1);
            Assert.True(WastelandApi.AddHeat(Town, 40, "ambiguous name"));
            Assert.Equal(0, record.Heat.HereOwned(Node, OldOwner));
            record.Heat.AddCrime(Town, OldOwner, 40, Node);
            LwMod.Settings.ReputationPrices = false;
            Assert.Equal(100, Quote(trader));
            LwMod.Settings.ReputationPrices = true;
            Assert.Equal(before, data.SiteFactions[0]);
            Assert.True(world.Characters.Cold(player.Slot)!.IsPlayer);
            Assert.Equal(40, record.Heat.HereOwned(Node, OldOwner));
            Assert.True(record.Holds(world, player, title.Id));
            Assert.Equal(130, Quote(trader));
            Assert.Equal(273, Quote(trader, @base: 200));
            Assert.Equal(108, Quote(elsewhere));  // the title travels, local heat does not
            Assert.Equal(long.MaxValue, Quote(trader, @base: long.MaxValue));
            Assert.Equal(100, Quote(trader, sell: true));

            typeof(PopulationData).GetMethod("SetReplacement")!.Invoke(data, [0, TownData.From(data.Db.Find("t-new")!)]);
            Assert.Equal("town:i-squin", WastelandApi.TownNode(Town));
            data.Relations.Add(after, W.Player, 40);
            record.Heat.AddCrime(Town, NewOwner, 6, Node);
            Assert.Equal(100, Quote(trader));
            Assert.Equal(92, Quote(trader2));
            Assert.True(TradeReputation.Remark(world, clerk, player));
            Assert.False(TradeReputation.Remark(world, clerk, player));
        }
        finally
        {
            LwMod.Settings.ReputationPrices = previous;
            LwMod.TitleViews = oldViews;
        }
    }

    public static long CompanionPrice(SimWorld _, object __, long price) => price == 200 ? 210 : price;

    [Fact]
    public void The_heat_extension_round_trips_both_owners_and_reads_a_save_without_it()
    {
        var data = Data();
        var deeds = new LwDeedsSystem([], data);
        var world = W.World(deeds);
        world.RunTicks(31);
        var old = LwDeeds.For(world)!;
        old.Heat.AddCrime(Town, OldOwner, 28, Node);
        old.Heat.AddBlood(Town, NewOwner, 7, Node);
        old.Heat.Forgive(NewOwner, 1);

        using var modern = new MemoryStream();
        deeds.Write(modern);
        modern.Position = 0;
        var loaded = new LwDeedsSystem([], data);
        loaded.Read(modern);
        var next = W.World(loaded);
        next.RunTicks(31);
        Assert.Equal(28, LwDeeds.For(next)!.Heat.HereOwned(Node, OldOwner));
        Assert.Equal(6, LwDeeds.For(next)!.Heat.HereOwned(Node, NewOwner));

        using var legacy = new MemoryStream();
        using (var writer = new BinaryWriter(legacy, System.Text.Encoding.UTF8, true))
        {
            old.Record.Write(writer);       // The older layout has no owner heat extension.
            old.Heat.Write(writer);
            writer.Write(2); writer.Write(0);
            writer.Write(0x4C4D524E); writer.Write(0);
        }
        legacy.Position = 0;
        var legacyReader = new LwDeedsSystem([], data);
        legacyReader.Read(legacy);
        var older = W.World(legacyReader);
        older.RunTicks(31);
        Assert.Equal(34, LwDeeds.For(older)!.Heat.Here(Town));
        Assert.Equal(0, LwDeeds.For(older)!.Heat.HereOwned(Node, NewOwner));
    }
}
