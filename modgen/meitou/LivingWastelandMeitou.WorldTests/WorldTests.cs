using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Items;
using Meitou.Simulation.Population;
using Xunit;
using SimWorld = Meitou.Simulation.World;

/// <summary>Small synthetic worlds: a few records made in memory, open ground, the systems under test. No game files.</summary>
static class W
{
    public const int Player = 0, Bandits = 1;

    public static FcsRecord Rec(string id, FcsRecordType type, string name) => new() { StringId = id, Flags = 0x10, Name = name, Type = (int)type };

    public static PopulationData Data(IEnumerable<FcsRecord> records, IEnumerable<TownPlacement>? towns = null)
    {
        var file = new FcsFile();
        file.Records.AddRange([Rec("204-gamedata.base", FcsRecordType.Faction, "Nameless"), Rec("60-t", FcsRecordType.Faction, "Dust Bandits"), .. records]);
        var db = new GameDatabase();
        db.Apply(file, "test.mod");
        return PopulationData.Create(db, towns ?? []);
    }

    public static SimWorld World(params ITickSystem[] systems)
    {
        var world = new SimWorld(new WorldSettings { Seed = 3, Threads = 1, PublishSnapshots = false, MinPartitionSize = 1 },
            new OpenGroundWalkability((_, _) => 150), systems);
        world.Player.Faction = Player;
        return world;
    }

    public static bool RemovePlatoon(SimWorld world, int id, string reason, int winner = -1)
    {
        var gone = typeof(Platoon).Assembly.GetType("Meitou.Simulation.Factions.PlatoonGone")!;
        var remove = world.Platoons.GetType().GetMethod("Remove", [typeof(int), gone, typeof(int)])!;
        return remove.Invoke(world.Platoons, [id, Enum.Parse(gone, reason), winner]) is true;
    }

    public static CharacterId Spawn(SimWorld world, string name, int faction, Vector3 at, Inventory? bag = null, int squad = -1) =>
        world.Characters.Spawn(new CharacterHot { Alive = true, Position = at },
            new CharacterCold { Name = name, Faction = faction, IsPlayer = faction == Player, RecordId = "r-" + name, Medical = new MedicalState([]),
                Inventory = bag, SquadId = squad }, world.Tick);

    public static CharacterId Find(SimWorld world, string name)
    {
        var t = world.Characters;
        for (int i = 0; i < t.HighWater; i++)
            if (t.Cold(i)?.Name == name) return t.IdOf(i);
        return CharacterId.None;
    }

    public static CombatHit Hit(SimWorld world, CharacterId attacker, CharacterId victim) =>
        new(world.Tick, default, attacker, victim, 0, 0, 0, default, default, Vector3.Zero, Vector3.Zero, 0, 0, false, true, true, false);

    /// <summary>A rival as RivalsSystem saves it (format 3), so a test can start from a rival already made.</summary>
    public static MemoryStream SavedRival(string name, string grudge, int phase, float x, float z, int returns = 0, int boosted = 1, double next = 1e6,
        bool told = true, int platoonId = -1)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(3); w.Write(1); w.Write(-1e9); w.Write(1);
        w.Write(0); w.Write(name); w.Write("Dust Bandit"); w.Write("Dust Bandits"); w.Write(grudge); w.Write("Leaf");
        w.Write(0.0); w.Write(next); w.Write(returns); w.Write(0); w.Write(phase); w.Write(x); w.Write(z);
        w.Write(boosted); w.Write(platoonId); w.Write(true); w.Write(told);
        w.Flush();
        ms.Position = 0;
        return ms;
    }

    public static List<string> Page(RivalsSystem rivals) => rivals.AtlasRows().SelectMany(r => r.Page).ToList();
}

public class RivalWorldTests
{
    const int Hunting = 1;

    [Fact]
    public void A_hunting_rival_who_loses_gets_away_once_and_it_survives_a_save()
    {
        var rivals = new RivalsSystem(null);
        var world = W.World(rivals);
        var leaf = W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        W.Spawn(world, "Hollin One-Arm", W.Bandits, new Vector3(500, 150, 0));
        rivals.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", Hunting, 500, 0));
        world.RunTicks(301);                                             // a pass finds the rival again by name
        var hollin = W.Find(world, "Hollin One-Arm");

        for (int fight = 0; fight < 3; fight++)                          // knocked down, up again, down again: one fight
        {
            rivals.Hit(world, W.Hit(world, leaf, hollin), world.Characters.Cold(hollin.Slot)!, world.Characters.Cold(leaf.Slot), "Leaf", false);
            rivals.FightEnded(world, won: true);
        }
        Assert.Contains("Has come back for your squad once and got away.", W.Page(rivals));
        Assert.Contains(world.TakeMessages(), m => m.Contains("got away again"));

        var saved = new MemoryStream();
        rivals.Write(saved);
        saved.Position = 0;
        var loaded = new RivalsSystem(null);
        loaded.Read(saved);
        Assert.Contains("Has come back for your squad once and got away.", W.Page(loaded));
    }

    [Fact]
    public void Someone_else_on_the_rivals_old_handle_is_not_the_rival()
    {
        // seen in play: an unloaded rival's handle went to a respawned crew member, who then "got away again" each time he died
        var rivals = new RivalsSystem(null);
        var world = W.World(rivals);
        var leaf = W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        var crew = W.Spawn(world, "Mercenary", W.Bandits, new Vector3(500, 150, 0));
        rivals.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", Hunting, 500, 0));
        world.RunTicks(301);
        rivals.Hit(world, W.Hit(world, leaf, crew), world.Characters.Cold(crew.Slot)!, world.Characters.Cold(leaf.Slot), "Leaf", false);
        rivals.FightEnded(world, won: true);
        Assert.DoesNotContain(W.Page(rivals), l => l.StartsWith("Has come back", StringComparison.Ordinal));
    }
}

public class KeepsakeWorldTests
{
    [Fact]
    public void A_keepsake_story_is_written_into_its_record_and_carried_keepsakes_take_the_name()
    {
        var data = W.Data([W.Rec("ks-1", FcsRecordType.Item, "Keepsake of the Fallen"), W.Rec("ks-2", FcsRecordType.Item, "Keepsake of the Fallen (2)")]);
        KeepsakeBook.Slots = ["ks-1", "ks-2"];
        var book = new KeepsakeBook(data);
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(1); w.Write(1); w.Write(0); w.Write("Keepsake of Hollin"); w.Write("It belonged to Hollin."); w.Write(5.0);   // format 1 still reads
        w.Flush();
        ms.Position = 0;
        book.Read(ms);
        var rec = data.Db.Find("ks-1")!;
        Assert.Equal("Keepsake of Hollin", rec.Name);
        Assert.Equal("It belonged to Hollin.", rec.GetString("description"));

        var bag = new Inventory();
        var item = new ItemInstance { Record = "ks-1", Name = "Keepsake of the Fallen" };
        bag.Items.Add(item);
        var world = W.World(book);
        W.Spawn(world, "Ruka", W.Player, Vector3.Zero, bag);
        world.RunTicks(2);
        Assert.Equal("Keepsake of Hollin", item.Name);
    }
}

public class NewsWorldTests
{
    [Fact]
    public void News_is_taken_once_talked_about_near_the_place_and_heard_when_the_squad_comes()
    {
        var stack = new Vector3(10000, 150, 10000);
        var data = W.Data([W.Rec("town-stack", FcsRecordType.Town, "Stack")], [new TownPlacement("i-stack", "town-stack", stack, null)]);
        var news = new NewsSystem(data);
        var world = W.World(news);
        W.Spawn(world, "Leaf", W.Player, new Vector3(300000, 150, 300000));
        world.RunTicks(2);

        Assert.True(news.Report("siege-began", "Stack", "Shek Kingdom", "", 70, "test"));
        Assert.False(news.Report("siege-began", "stack", "Shek Kingdom", "", 70, "other mod"));   // the same news: told once
        Assert.False(news.Report("siege-began", "Nowhere", "", "", 70, "test"));                // no such town

        var local = W.Spawn(world, "Stack local", W.Bandits, stack + new Vector3(2000, 0, 0));
        var faraway = W.Spawn(world, "Far local", W.Bandits, new Vector3(400000, 150, 400000));
        world.RunTicks(1);
        Assert.Equal(1, news.SlotValue(world, 0, local));
        Assert.Equal(0, news.SlotValue(world, 0, faraway));
        Assert.Empty(news.AtlasRows());                                  // the squad has not heard it yet

        W.Spawn(world, "Ruka", W.Player, stack + new Vector3(500, 0, 0));
        world.TakeMessages();
        world.RunTicks(61);
        Assert.Contains("Word on the road: The Shek Kingdom have laid siege to Stack.", world.TakeMessages());
        Assert.Single(news.AtlasRows());
    }
}

public class BarkWorldTests
{
    [Fact]
    public void A_pursuer_closing_in_says_its_line_once()
    {
        var world = W.World();
        W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "t", Faction = W.Bandits };
        var boss = W.Spawn(world, "Crew Boss", W.Bandits, new Vector3(300, 150, 0), squad: squad.Id);
        squad.Members.Add(boss);
        squad.Leader = boss;
        world.Squads.Add(squad);
        var platoon = new Platoon { Id = world.Platoons.NextId(), Origin = 0, TemplateId = "t", Key = 1, SquadId = squad.Id, Faction = W.Bandits, State = PlatoonState.Loaded,
            Position = new Vector2(300, 0) };
        world.Platoons.Add(platoon);
        world.RunTicks(1);

        var players = Pursuit.Players(world);
        Pursuit.Steer(world, platoon, players, BarkLines.Crew("Squin", 1));
        Pursuit.Steer(world, platoon, players, BarkLines.Crew("Squin", 2));
        var barks = (System.Collections.IDictionary)typeof(SimWorld).GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
        Assert.Single(barks);
        Assert.Contains("Squin", barks[boss]!.ToString());
    }
}

public class ItemUseWorldTests
{
    [Fact]
    public void A_right_click_on_a_warrant_reads_it_through_the_engines_own_handler_type()
    {
        var handlerType = typeof(SimWorld).Assembly.GetType("Meitou.Simulation.ItemUseHandler");
        if (handlerType is null) return;                                 // Meitou before 0.20.2: no item uses, nothing to check
        Trophies.Warrant = "warrant";
        var world = W.World();
        var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var item = new ItemInstance { Record = "warrant", Name = "Warrant" };
        Assert.True(ItemUses.SetTag(item, "Squin"));
        Assert.Equal("Squin", ItemUses.Tag(item));
        var handler = ItemUses.Handler(handlerType)!;
        var useType = typeof(SimWorld).Assembly.GetType("Meitou.Simulation.ItemUse")!;
        var use = Activator.CreateInstance(useType, world, leaf, item, new Inventory())!;
        world.TakeMessages();
        Assert.True((bool)handler.DynamicInvoke(use)!);
        Assert.Contains(world.TakeMessages(), m => m.StartsWith("A warrant from Squin", StringComparison.Ordinal));
        Assert.False((bool)handler.DynamicInvoke(Activator.CreateInstance(useType, world, leaf, new ItemInstance { Record = "rock", Name = "Rock" }, new Inventory()))!);
    }
}

// Inventory.Add across Meitou versions (InventoryCompat, Trophies.cs): 0.21 added an optional parameter, and the DLL built against 0.17.3
// failed to find the old method (no keepsake, record stopped).
public class InventoryCompatWorldTests
{
    [Fact]
    public void An_item_goes_into_a_bag_on_this_Meitou()
    {
        var bag = new Inventory();
        var item = new ItemInstance { Record = "ks-1", Name = "Keepsake of the Fallen" };
        Assert.True(InventoryCompat.Add(bag, item));
        Assert.Contains(item, bag.Items);
    }
}
