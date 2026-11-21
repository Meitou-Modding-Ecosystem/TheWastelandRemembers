using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Locks;
using Xunit;
using SimWorld = Meitou.Simulation.World;

// Towns miss stolen goods (Thefts.cs). Takes are fed in as the engine's GoodsTaken would be (an object with the same property names), so
// the town, value and heat rules run on every Meitou version; where Meitou has the observer (0.21+), the emitted system is checked to
// forward a real GoodsTaken.
public sealed record FakeTake(CharacterId Taker, int OwnerFaction, CharacterId Owner, string Site, string Record, int Units, bool Caught);

[Collection("settings")]
public class TheftsWorldTests
{
    static readonly Vector3 Squin = new(10000, 150, 10000);

    static (SimWorld World, TheftsSystem Thefts, CharacterId Leaf) Town(Vector3? takerAt = null)
    {
        var sword = W.Rec("sword-t", FcsRecordType.Item, "Katana");
        sword.Ints["value"] = 3000;
        var bread = W.Rec("bread-t", FcsRecordType.Item, "Bread");
        bread.Ints["value"] = 20;
        var town = W.Rec("town-squin", FcsRecordType.Town, "Squin");
        town.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        var data = W.Data([sword, bread, town], [new TownPlacement("i-squin", "town-squin", Squin, null)]);
        var thefts = new TheftsSystem();
        var world = W.World(new LwDeedsSystem([], data), thefts);
        var leaf = W.Spawn(world, "Leaf", W.Player, takerAt ?? Squin + new Vector3(100, 0, 0));
        world.RunTicks(31);
        return (world, thefts, leaf);
    }

    static double Heat(SimWorld world) => LwDeeds.For(world)!.Heat.Here("Squin");

    [Fact]
    public void An_unseen_theft_is_missed_half_a_day_later_and_a_seen_one_heats_at_once()
    {
        var (world, thefts, leaf) = Town();
        try
        {
            LwMod.Settings.TheftHeat = true;
            Thefts.Taken(world, new FakeTake(leaf, W.Bandits, CharacterId.None, "", "sword-t", 1, Caught: false));
            Assert.Equal(0, Heat(world));                                    // nobody knows yet
            Assert.Single(thefts.Waiting);
            thefts.Settle(world, world.GameHours + TheftRules.MissedAfter);
            Assert.Equal(TheftRules.Heat(3000, seen: false), Heat(world), 3);
            Assert.Contains(LwDeeds.For(world)!.Record.History, h => h.Text == "Squin is missing Katana and wants to know who took it.");
            Assert.Empty(thefts.Waiting);

            double before = Heat(world);
            Thefts.Taken(world, new FakeTake(leaf, W.Bandits, CharacterId.None, "", "sword-t", 1, Caught: true));
            Assert.Equal(before + TheftRules.Heat(3000, seen: true), Heat(world), 3);
        }
        finally { LwMod.Settings.TheftHeat = true; }
    }

    [Fact]
    public void The_squads_own_goods_cheap_goods_and_the_setting_off_count_for_nothing()
    {
        var (world, thefts, leaf) = Town();
        try
        {
            LwMod.Settings.TheftHeat = true;
            Thefts.Taken(world, new FakeTake(leaf, W.Player, CharacterId.None, "", "sword-t", 1, false));   // its own
            Thefts.Taken(world, new FakeTake(leaf, -1, CharacterId.None, "", "sword-t", 1, false));         // nobody's
            Thefts.Taken(world, new FakeTake(leaf, W.Bandits, CharacterId.None, "", "bread-t", 2, false));  // 40 cats
            LwMod.Settings.TheftHeat = false;
            Thefts.Taken(world, new FakeTake(leaf, W.Bandits, CharacterId.None, "", "sword-t", 1, false));
            Assert.Empty(thefts.Waiting);
        }
        finally { LwMod.Settings.TheftHeat = true; }
    }

    [Fact]
    public void Waiting_thefts_survive_a_save()
    {
        var (world, thefts, leaf) = Town();
        LwMod.Settings.TheftHeat = true;
        Thefts.Taken(world, new FakeTake(leaf, W.Bandits, CharacterId.None, "", "sword-t", 2, false));
        var saved = new MemoryStream();
        thefts.Write(saved);
        saved.Position = 0;
        var loaded = new TheftsSystem();
        loaded.Read(saved);
        var p = Assert.Single(loaded.Waiting);
        Assert.Equal(("Squin", "2 Katana", 6000.0, "town:i-squin"), (p.Town, p.Items, p.Value, p.Node));
    }

    [Fact]
    public void The_emitted_observer_forwards_the_engines_GoodsTaken()
    {
        var observer = typeof(SimWorld).Assembly.GetType("Meitou.Simulation.Crime.IGoodsTakenObserver");
        var takeType = typeof(SimWorld).Assembly.GetType("Meitou.Simulation.Crime.GoodsTaken");
        if (observer is null || takeType is null) return;                // Meitou before 0.21: nothing to observe
        var type = ObserverEmit.Build(observer, "GoodsTaken", typeof(Thefts).GetMethod(nameof(Thefts.Taken))!, "TheftWatchSystem");
        Assert.NotNull(type);
        var (world, thefts, leaf) = Town();
        LwMod.Settings.TheftHeat = true;
        var take = Activator.CreateInstance(takeType, 1L, leaf, W.Bandits, CharacterId.None, "", "sword-t", 1, false)!;
        var watcher = Activator.CreateInstance(type!)!;
        observer.GetMethod("GoodsTaken")!.Invoke(watcher, [world, take]);
        Assert.Equal("Katana", Assert.Single(thefts.Waiting).Items);
    }

    [Fact]
    public void A_later_theft_waits_on_its_own_clock()
    {
        // from a review of the code: one batch per town, so a take nearly 12 hours after the first was missed with it, at once
        var (world, thefts, leaf) = Town();
        try
        {
            LwMod.Settings.TheftHeat = true;
            double t0 = world.GameHours;
            Thefts.Taken(world, new FakeTake(leaf, W.Bandits, CharacterId.None, "", "sword-t", 1, Caught: false));
            world.GameHours = t0 + 11;
            Thefts.Taken(world, new FakeTake(leaf, W.Bandits, CharacterId.None, "", "sword-t", 1, Caught: false));
            Assert.Equal(2, thefts.Waiting.Count);
            thefts.Settle(world, t0 + TheftRules.MissedAfter);
            Assert.Equal(TheftRules.Heat(3000, seen: false), Heat(world), 3);              // only the first is missed yet
            Assert.Single(thefts.Waiting);
            world.GameHours = t0 + 11.5;
            Thefts.Taken(world, new FakeTake(leaf, W.Bandits, CharacterId.None, "", "sword-t", 1, Caught: false));
            Assert.Single(thefts.Waiting);                                                  // within the hour: the same theft
        }
        finally { LwMod.Settings.TheftHeat = true; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_container_take_is_attributed_to_the_goods_site_even_when_the_taker_is_elsewhere(bool caught)
    {
        var (world, thefts, leaf) = Town(Squin + new Vector3(20000, 0, 0));
        const string site = "shop-instance@storage3";
        var registry = typeof(FixtureRegistry).GetField("_byId", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fixtures = (Dictionary<string, Fixture>)registry.GetValue(world.Fixtures)!;
        fixtures.Add(site, new Fixture { Id = site, Building = "shop-instance", Kind = FixtureKind.Container,
            Owner = W.Bandits, Site = 0, Position = Squin, Contents = new Inventory() });
        LwMod.Settings.TheftHeat = true;

        var observer = typeof(SimWorld).Assembly.GetType("Meitou.Simulation.Crime.IGoodsTakenObserver");
        if (observer is not null)
        {
            var type = ObserverEmit.Build(observer, "GoodsTaken", typeof(Thefts).GetMethod(nameof(Thefts.Taken))!, "TheftAtSite");
            var method = observer.GetMethod("GoodsTaken")!;
            var payload = method.GetParameters()[1].ParameterType.GetElementType()!;
            var take = Activator.CreateInstance(payload, world.Tick, leaf, W.Bandits, CharacterId.None, site, "sword-t", 1, caught);
            method.Invoke(Activator.CreateInstance(type!), [world, take]);
        }
        else
            Thefts.Taken(world, new FakeTake(leaf, W.Bandits, CharacterId.None, site, "sword-t", 1, caught));

        if (!caught)
        {
            Assert.Equal("Squin", Assert.Single(thefts.Waiting).Town);
            thefts.Settle(world, world.GameHours + TheftRules.MissedAfter);
        }
        Assert.Equal(TheftRules.Heat(3000, caught), Heat(world), 3);
        var deeds = LwDeeds.For(world)!;
        Assert.Equal(TheftRules.Heat(3000, caught), deeds.Heat.HereOwned("town:i-squin", deeds.FactionName(W.Bandits)), 3);
    }
}
