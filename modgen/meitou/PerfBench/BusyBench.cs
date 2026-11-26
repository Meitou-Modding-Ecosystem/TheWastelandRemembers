using System.Collections;
using System.Numerics;
using System.Reflection;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;

static class BusyBench
{
    const int OutpostCount = 8, KnownPlaces = 64, Population = 1500;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string ModId = "LivingWastelandMeitou", Captive = "43159-Dialogue.mod";
    static Vector3 At(int i) => new(12000 + 3600 * (i % 8), 150, 12000 + 3600 * (i / 8));

    internal static int Run(Assembly mod, bool checkBudget = false)
    {
        Type TypeOf(string name) => mod.GetType("LivingWasteland.MeitouMod." + name, throwOnError: true)!;
        var settings = TypeOf("LwMod").GetField("Settings", Static)!.GetValue(null)!;
        void Set(string name, bool value) => settings.GetType().GetProperty(name)!.SetValue(settings, value);
        Set("LogChanges", false);
        Set("RememberOutposts", true);
        Set("PrisonerCases", true);
        Set("OtherModsNews", true);
        Set("Atlas", true);

        var file = new FcsFile();
        FcsRecord Add(string id, FcsRecordType type, string name)
        {
            var row = new FcsRecord { StringId = id, Type = (int)type, Flags = 0x10, Name = name };
            file.Records.Add(row);
            return row;
        }
        Add("204-gamedata.base", FcsRecordType.Faction, "Nameless");
        Add("60-t", FcsRecordType.Faction, "Dust Bandits");
        Add("traders", FcsRecordType.Faction, "Traders Guild");
        Add(Captive, FcsRecordType.Character, "Esata the Stone Golem");
        Add("assault", FcsRecordType.FactionCampaign, "Bandit raid").Bools["is hostile"] = true;
        Add("shack", FcsRecordType.Building, "Small Shack").References["interior"] = [new("interior-mesh", 0, 0, 0)];
        var placements = new List<TownPlacement>();
        var placeType = TypeOf("AtlasPlace");
        var personType = TypeOf("AtlasPerson");
        var places = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), placeType))!;
        var people = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), personType))!;
        for (int i = 0; i < KnownPlaces; i++)
        {
            string town = $"Town {i:D2}", id = $"town-{i}";
            Add(id, FcsRecordType.Town, town).References["faction"] = [new("traders", 0, 0, 0)];
            placements.Add(new($"site-{i}", id, At(i) + Vector3.UnitZ * 600, null));
            places.Add(town, Activator.CreateInstance(placeType, "Border Zone", "A town on the road.", new List<string>()));
            for (int j = 0; j < 5; j++)
                people.Add($"Person {i:D2}-{j}", Activator.CreateInstance(personType, $"person-{i}-{j}", "local",
                    town, "Border Zone", "A person known to the squad.", null, false, null, false));
        }
        var db = new GameDatabase();
        db.Apply(file, "BusyFixture.mod");
        var data = PopulationData.Create(db, placements);
        int player = data.FactionIndex("204-gamedata.base"), bandit = data.FactionIndex("60-t"), trader = data.FactionIndex("traders");
        var population = new PopulationSystem(data);
        ITickSystem Make(string type, params object?[] args) => (ITickSystem)Activator.CreateInstance(TypeOf(type), args)!;
        var deeds = Make("LwDeedsSystem", Array.CreateInstance(TypeOf("TitleRule"), 0), data);
        var news = Make("NewsSystem", data);
        var outposts = Make("OutpostMemories", data, population, ModId);
        var cases = Make("PrisonerCases", data, Array.CreateInstance(TypeOf("CampCode"), 0));
        var atlas = Make("AtlasSystem", Activator.CreateInstance(TypeOf("AtlasTable"), places, people, null), null, data);
        using var world = new World(new WorldSettings { Seed = 3, Threads = 1, PublishSnapshots = false },
            new OpenGroundWalkability((_, _) => 150), [deeds, news, outposts, cases, atlas]);
        world.Player.Faction = player;
        for (int i = 0; i < KnownPlaces; i++) world.Player.Know(i, TownKnowledge.Discovered | TownKnowledge.Explored);
        world.Player.Meet(trader);

        var towns = Enumerable.Range(0, OutpostCount).Select(index =>
        {
            var town = world.Buildings.AddTown(At(index), player, $"Outpost {index + 1}");
            town.Public = true;
            world.Buildings.Add(new BuildSite { Id = $"shack-{index}", Record = "shack", Owner = player,
                Position = town.Position, Town = town.Number, Materials = [], Complete = true });
            return town;
        }).ToArray();
        CharacterId Spawn(string name, int faction, Vector3 pos, int squad = -1) =>
            world.Characters.Spawn(new CharacterHot { Alive = true, Position = pos },
                new CharacterCold { Name = name, IsPlayer = faction == player, Faction = faction,
                    SquadId = squad, Medical = new MedicalState([]) }, world.Tick);
        var leaf = Spawn("Leaf", player, At(0));
        for (int i = 1; i < Population - 2; i++)
            Spawn($"Walker {i}", i < 24 ? player : trader, At(i % OutpostCount) + Vector3.UnitX * (5 + (i % 170)));

        var raid = new CampaignInstance { Id = 1, CampaignId = "assault", Faction = bandit, Enemy = player,
            Target = CampaignTown.OfPlayer(towns[0].Number), Phase = 1, Reached = true };
        var war = new WarManager { Faction = bandit };
        war.Actives.Add(raid);
        typeof(PopulationSystem).GetField("_wars", Instance)!.SetValue(population, new[] { war });
        var gang = new Squad { Id = world.Squads.NextId(), TemplateId = "raid-party", Faction = bandit };
        world.Squads.Add(gang);
        var party = new Platoon { Id = world.Platoons.NextId(), Origin = 0, Key = 1, Faction = bandit, TemplateId = gang.TemplateId, Campaign = raid,
            State = PlatoonState.Loaded, SquadId = gang.Id, Position = new Vector2(At(0).X, At(0).Z) };
        world.Platoons.Add(party);
        gang.PlatoonId = party.Id;
        var attacker = Spawn("Raider", bandit, At(0) + Vector3.UnitX * 10, gang.Id);
        var prisoner = Spawn("Esata the Stone Golem", bandit, At(0));
        world.Uniques.Bind(Captive, prisoner);
        var cageSite = new BuildSite { Id = "bench-cage-building", Record = "cage", Owner = player,
            Position = At(0), Town = towns[0].Number, Materials = [], Complete = true };
        world.Buildings.Add(cageSite);
        var cage = new Fixture { Id = "bench-cage", Building = cageSite.Id, Kind = FixtureKind.Cage, Position = At(0), Owner = player, Occupant = prisoner };
        var cold = world.Characters.Cold(prisoner.Slot)!;
        cold.InSomething = HeldState.Imprisoned;
        cold.InWhat = cage.Id;
        ((List<Fixture>)typeof(FixtureRegistry).GetField("_all", Instance)!.GetValue(world.Fixtures)!).Add(cage);
        ((Dictionary<string, Fixture>)typeof(FixtureRegistry).GetField("_byId", Instance)!.GetValue(world.Fixtures)!).Add(cage.Id, cage);

        world.RunTicks(151);
        ((ICombatHitObserver)outposts).HitResolved(world, new CombatHit(world.Tick, default, attacker, leaf,
            0, 0, 0, default, default, At(0), default, 0, 0, false, true, true, false));
        world.GameHours += 7;
        world.RunTicks(150);
        var posts = (IDictionary)outposts.GetType().GetField("outposts", Instance)!.GetValue(outposts)!;
        int defending = posts.Values.Cast<object>().Count(post => post.GetType().GetField("FirstDefence", Instance)?.GetValue(post) is not null);
        int held = ((IEnumerable)cases.GetType().GetMethod("AtlasRows", Instance)!.Invoke(cases, [])!).Cast<object>().Count();
        if (posts.Count != OutpostCount || defending != 1 || held != 1)
            throw new InvalidOperationException($"Busy fixture did not settle: {posts.Count} outposts, {defending} defences, {held} custody cases.");
        var publish = atlas.GetType().GetMethod("Publish", Instance)!.CreateDelegate<Action<World, bool>>(atlas);
        var survey = outposts.GetType().GetMethod("Survey", Instance)!;
        object record = deeds.GetType().GetProperty("Deeds", Instance)!.GetValue(deeds)!;
        publish(world, true);
        var pages = (IReadOnlyDictionary<string, string>)TypeOf("RecordHost").GetField("AtlasPages")!.GetValue(null)!;
        Console.WriteLine($"Busy fixture: {world.Characters.HighWater} actors, {posts.Count} outposts, {defending} active defence, {held} captive, {pages.Count} Atlas pages.");
        if (checkBudget)
        {
            const int repetitions = 20;
            const long ceiling = 1_000_000;
            _ = survey.Invoke(outposts, [world, record]);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < repetitions; i++) _ = survey.Invoke(outposts, [world, record]);
            long perPass = (GC.GetAllocatedBytesForCurrentThread() - start) / repetitions;
            Console.WriteLine($"Busy outpost allocation guard: {perPass} B/pass, ceiling {ceiling} B/pass.");
            return perPass <= ceiling ? 0 : 1;
        }
        Bench.Measure("Busy outpost survey", () => survey.Invoke(outposts, [world, record]), 75);
        Bench.Measure("Busy Atlas publication with full details", () => publish(world, true), 35);
        Bench.Measure("Busy Atlas publication with existing details", () => publish(world, false), 35);
        Bench.Measure("Busy world, 150 ticks per pass", () => world.RunTicks(150), 15);

        var exportFolder = Directory.CreateTempSubdirectory("twr-busy-profile-");
        try
        {
            TypeOf("StatsUi").GetField("PlaytestFolder", Static)!.SetValue(null, exportFolder.FullName);
            Set("PlaytestDiagnostics", true);
            var log = TypeOf("PlaytestLog").GetMethod("Event", Static)!.CreateDelegate<Action<World, string, string, string, string>>();
            for (int hour = 0; hour < 7 * 24; hour++)
            {
                world.GameHours += 1;
                for (int n = 0; n < 4; n++)
                    log(world, "fixture", $"{hour}:{n}", "event", "An outpost, custody, trade or news event.");
            }
            var export = TypeOf("StatsUi").GetMethod("WritePlaytest", Static)!.CreateDelegate<Func<string, string>>();
            Bench.Measure("Seven-day playtest export, including file replacement", () => export("busy.txt"), 15);
            var fileInfo = new FileInfo(Path.Combine(exportFolder.FullName, "busy.txt"));
            Console.WriteLine($"Seven-day log size {fileInfo.Length} bytes; " +
                File.ReadLines(fileInfo.FullName).First(line => line.StartsWith("Events retained", StringComparison.Ordinal)));
        }
        finally
        {
            Set("PlaytestDiagnostics", false);
            TypeOf("StatsUi").GetField("PlaytestFolder", Static)!.SetValue(null, null);
            exportFolder.Delete(recursive: true);
        }
        return 0;
    }
}
