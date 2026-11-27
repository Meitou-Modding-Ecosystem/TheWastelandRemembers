using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;

static class DetailBench
{
    const int TownCount = 64, PeoplePerTown = 5, FactionCount = 18;
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static Type ModType(Assembly asm, string name) => asm.GetType($"LivingWasteland.MeitouMod.{name}", throwOnError: true)!;

    public static int Run(Assembly mod)
    {
        Console.WriteLine($"Helper MVID {mod.ManifestModule.ModuleVersionId}");
        object settings = ModType(mod, "LwMod").GetField("Settings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        void Set(string name, bool value) => settings.GetType().GetProperty(name)?.SetValue(settings, value);
        Set("PlaytestDiagnostics", false);
        Set("Atlas", true);
        Set("LookoutCalls", false);
        Set("RecordInPanels", true);
        Set("LogChanges", false);
        Set("LighterChecks", false);
        Atlas(mod);
        Squad(mod, tick: 60);
        Squad(mod, tick: 90);
        return 0;
    }

    static World WorldWith(params ITickSystem[] systems)
    {
        var world = new World(new WorldSettings { Seed = 3, Threads = 1, PublishSnapshots = false, MinPartitionSize = 1 },
            new OpenGroundWalkability((_, _) => 150), systems);
        world.Player.Faction = 0;
        return world;
    }

    static Vector3 TownAt(int town) => new(10000 + 12000 * (town % 8), 150, 10000 + 12000 * (town / 8));

    static void Atlas(Assembly mod)
    {
        var fcs = new FcsFile();
        FcsRecord Add(string id, FcsRecordType type, string title)
        {
            var record = new FcsRecord { StringId = id, Type = (int)type, Flags = 0x10, Name = title };
            fcs.Records.Add(record);
            return record;
        }
        Add("204-gamedata.base", FcsRecordType.Faction, "Nameless");
        for (int faction = 0; faction < FactionCount; faction++)
            Add($"faction-{faction:D2}", FcsRecordType.Faction, $"Faction {faction:D2}");
        var placements = new List<TownPlacement>();
        Type placeType = ModType(mod, "AtlasPlace"), personType = ModType(mod, "AtlasPerson");
        IDictionary places = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), placeType))!;
        IDictionary people = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), personType))!;
        for (int town = 0; town < TownCount; town++)
        {
            string name = $"Town {town:D2}";
            string townId = $"bench-town-{town:D2}";
            var record = Add(townId, FcsRecordType.Town, name);
            record.References["faction"] = [new($"faction-{town % FactionCount:D2}", 0, 0, 0)];
            placements.Add(new($"position-{town:D2}", townId, TownAt(town), null));
            places.Add(name, Activator.CreateInstance(placeType, "Test country", "A place in the developed fixture.", new List<string> { "The squad has visited." }));
            for (int person = 0; person < PeoplePerTown; person++)
            {
                string id = $"person-{town:D2}-{person}";
                people.Add($"Person {town:D2}-{person}", Activator.CreateInstance(personType, id, "local",
                    name, "Test country", "A note about this character.", null, false, null, false));
            }
        }
        var db = new GameDatabase();
        db.Apply(fcs, "DevelopedAtlasFixture.mod");
        var data = PopulationData.Create(db, placements);
        object table = Activator.CreateInstance(ModType(mod, "AtlasTable"), places, people, null)!;
        var atlas = (ITickSystem)Activator.CreateInstance(ModType(mod, "AtlasSystem"), table, null, data)!;
        using var world = WorldWith(atlas);
        for (int town = 0; town < TownCount; town++) world.Player.Know(town, TownKnowledge.Discovered | TownKnowledge.Explored);
        for (int f = 0; f < FactionCount; f++)
        {
            int index = data.FactionIndex($"faction-{f:D2}");
            world.Player.Meet(index);
            for (int other = f + 1; other < FactionCount; other++)
                if ((f + other) % 5 == 0) data.Relations.Set(index, data.FactionIndex($"faction-{other:D2}"), -80);
        }
        Type host = ModType(mod, "RecordHost");
        string Fingerprint()
        {
            string list = (string)host.GetField("AtlasList")!.GetValue(null)!;
            var pages = (IReadOnlyDictionary<string, string>)host.GetField("AtlasPages")!.GetValue(null)!;
            var portraits = (IReadOnlyDictionary<string, string>)host.GetField("AtlasPortraits")!.GetValue(null)!;
            if (pages.Count != TownCount * (PeoplePerTown + 1) + FactionCount)
                throw new InvalidOperationException($"Unexpected Atlas size {pages.Count}.");
            return Hash(list + "\n" + string.Join("\n", pages.OrderBy(p => p.Key)) + "\n" + string.Join("\n", portraits.OrderBy(p => p.Key)));
        }

        var publish = atlas.GetType().GetMethod("Publish", Hidden)!;
        Action full;
        if (publish.GetParameters().Length == 1)
        {
            var call = publish.CreateDelegate<Action<World>>(atlas);
            full = () => call(world);
        }
        else
        {
            var call = publish.CreateDelegate<Action<World, bool>>(atlas);
            full = () => call(world, true);
            Bench.Measure("Developed Atlas existing details reused", () => call(world, false), 35);
        }
        Bench.Measure("Developed Atlas full publication", full, 35);
        string before = Fingerprint();
        Console.WriteLine($"Developed Atlas content SHA-256 {before}");
        data.Relations.Set(data.FactionIndex("faction-01"), world.Player.Faction, -70);
        full();
        string changed = Fingerprint();
        if (before == changed) throw new InvalidOperationException("The fixture relation change did not reach the Atlas.");
        Console.WriteLine($"Developed Atlas after relation change SHA-256 {changed}");
    }

    static void Squad(Assembly mod, int tick)
    {
        var rules = Array.CreateInstance(ModType(mod, "TitleRule"), 0);
        var system = (ITickSystem)Activator.CreateInstance(ModType(mod, "LwDeedsSystem"), rules, null)!;
        using var world = WorldWith(system);
        const int count = 1500, players = 30;
        for (int i = 0; i < count; i++)
        {
            bool player = i < players;
            var at = new Vector3(14000 + i * 3, 150, 14000);
            world.Characters.Spawn(new CharacterHot { Alive = true, Position = at },
                new CharacterCold { Medical = new MedicalState([]), IsPlayer = player, Faction = player ? 0 : 1, Name = $"Bench {i}" }, world.Tick);
        }
        world.RunTicks(tick);
        object state = system.GetType().GetProperty("Deeds", Hidden)!.GetValue(system)!;
        object record = state.GetType().GetProperty("Record")!.GetValue(state)!;
        for (int i = 0; i < players; i++)
            record.GetType().GetMethod("Kill")!.Invoke(record, [$"Bench {i}", "Dust Bandits", false, null]);
        Bench.Measure(tick == 90 ? "Squad sample and both panel publications" : "Ordinary sample without panel work",
            () => system.SlowWorld(world), 200);
        if (tick == 90)
            Console.WriteLine($"Squad text SHA-256 {Hash((string)ModType(mod, "RecordHost").GetField("Panel")!.GetValue(null)!)}");
    }

    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static int HeldTransitions()
    {
#if NET11_0_OR_GREATER
        var observer = new HeldObserver();
        using var world = WorldWith(observer);
        var cold = new CharacterCold { Medical = new MedicalState([]) };
        world.Characters.Spawn(new CharacterHot { Alive = true }, cold, world.Tick);
        world.RunTicks(2);
        Move(HeldState.Imprisoned, "first cage");
        Move(HeldState.Imprisoned, "second cage");
        Move(default, null);

        void Move(HeldState state, string? fixture)
        {
            cold.InSomething = state;
            cold.InWhat = fixture;
            world.RunTicks(4);
        }

        string[] expected = ["None→Imprisoned first cage", "Imprisoned→Imprisoned second cage", "Imprisoned→None second cage"];
        foreach (string row in observer.Rows) Console.WriteLine(row);
        return observer.Rows.SequenceEqual(expected) ? 0 : 1;
#else
        Console.Error.WriteLine("Held transitions need a Meitou release with held-state observers.");
        return 2;
#endif
    }

#if NET11_0_OR_GREATER
    sealed class HeldObserver : ITickSystem, IHeldStateObserver
    {
        public List<string> Rows { get; } = [];
        public void HeldStateChanged(World world, in HeldStateChange change) => Rows.Add($"{change.Before}→{change.After} {change.Fixture}");
    }
#endif

}
