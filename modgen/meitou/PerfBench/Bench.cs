using System.Diagnostics;
using System.Numerics;
using System.Linq.Expressions;
using System.Reflection;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Population;
using SimWorld = Meitou.Simulation.World;

static class Bench
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Vector3 Town = new(14000, 150, 14000);
    static Type TypeIn(Assembly mod, string name) => mod.GetType("LivingWasteland.MeitouMod." + name, throwOnError: true)!;
    static MethodInfo Method(Assembly mod, string type, string name) => TypeIn(mod, type).GetMethod(name, Members)!;

    static MethodInfo PublishPanel(Assembly mod) => TypeIn(mod, "PanelLines").GetMethods(Members)
        .Single(method => method.Name == "Publish" && method.GetParameters()[0].ParameterType == typeof(SimWorld));

    internal static void Measure(string label, Action pass, int count = 150)
    {
        for (int i = 0; i < 100; i++) pass();
        var times = new double[7];
        var allocations = new long[7];
        for (int i = 0; i < times.Length; i++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int j = 0; j < count; j++) pass();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / count;
            allocations[i] = (GC.GetAllocatedBytesForCurrentThread() - bytes) / count;
        }
        Array.Sort(times);
        Array.Sort(allocations);
        Console.WriteLine(FormattableString.Invariant($"{label}: {times[3]:0.00} us/pass ({times[0]:0.00}–{times[^1]:0.00}), {allocations[3]} B/pass"));
    }

    static PopulationData Data()
    {
        var fcs = new FcsFile();
        FcsRecord Record(string id, FcsRecordType type, string name)
        {
            var row = new FcsRecord { StringId = id, Type = (int)type, Flags = 0x10, Name = name };
            fcs.Records.Add(row);
            return row;
        }
        Record("204-gamedata.base", FcsRecordType.Faction, "Nameless");
        Record("60-t", FcsRecordType.Faction, "Dust Bandits");
        Record("locals", FcsRecordType.Faction, "Locals");
        var town = Record("market", FcsRecordType.Town, "Market");
        town.References["faction"] = [new("locals", 0, 0, 0)];
        var db = new GameDatabase();
        db.Apply(fcs, "bench.mod");
        return PopulationData.Create(db, [new TownPlacement("market-placement", "market", Town, null)]);
    }

    static void Spawn(SimWorld world, int faction, bool player, string name, Vector3 at) =>
        world.Characters.Spawn(new CharacterHot { Alive = true, Position = at },
            new CharacterCold { Name = name, Faction = faction, IsPlayer = player, Medical = new MedicalState([]) }, world.Tick);

    public static int ObserveHeld()
    {
        using var world = new SimWorld(new WorldSettings { Threads = 1, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 150), []);
        var observers = typeof(SimWorld).GetProperty("StateObservers", Members)!.GetValue(world)!;
        var check = observers.GetType().GetMethod("CheckHeld", Members)!;
        var watchType = check.GetParameters()[2].ParameterType;
        var watch = Activator.CreateInstance(watchType, nonPublic: true)!;
        var body = Expression.Call(Expression.Constant(observers), check, Expression.Constant(world),
            Expression.Constant(CharacterId.None), Expression.Constant(watch, watchType), Expression.Constant(new CharacterCold()));
        var unchanged = Expression.Lambda<Action>(body).Compile();
        byte[] il = check.GetMethodBody()!.GetILAsByteArray()!;
        if (il[0] == 0x73)
            Console.WriteLine("First IL instruction allocates " + check.Module.ResolveMethod(BitConverter.ToInt32(il, 1))?.DeclaringType);
        Measure("Meitou CheckHeld, unchanged character and no listeners", unchanged, 10000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) unchanged();
        return GC.GetAllocatedBytesForCurrentThread() == before ? 0 : 1;
    }

    public static int ReviewProbes(Assembly mod)
    {
        var data = Data();
        var rules = Array.CreateInstance(TypeIn(mod, "TitleRule"), 0);
        var deedsType = TypeIn(mod, "LwDeeds");
        var deeds = Activator.CreateInstance(deedsType, [rules, data])!;
        var record = deedsType.GetProperty("Record")!.GetValue(deeds)!;
        Type recordType = record.GetType();
        using var before = new SimWorld(new WorldSettings { Threads = 1, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 150), []);
        using var after = new SimWorld(new WorldSettings { Threads = 1, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 150), []);
        Spawn(before, 0, true, "Leaf", Town);
        Spawn(after, 0, true, "Ruka", Town);
        before.RunTicks(1);
        after.RunTicks(1);
        var settings = TypeIn(mod, "LwMod").GetField("Settings", Members)!.GetValue(null)!;
        settings.GetType().GetProperty("RecordInPanels")!.SetValue(settings, true);
        recordType.GetMethod("Kill")!.Invoke(record, ["Leaf", "Dust Bandits", false, null]);
        PublishPanel(mod).Invoke(null, [before, deeds]);
        Method(mod, "WorldReady", "Ready").Invoke(null, [after, true]);
        object? transient = Method(mod, "PanelLines", "ForCharacter").Invoke(null, [after.Characters.IdOf(0)]);
        Console.WriteLine($"Reused handle: {before.Characters.IdOf(0) == after.Characters.IdOf(0)}, after load before panel publish: {string.Join(", ", ((System.Collections.IEnumerable?)transient)?.Cast<object>() ?? [])}");

        recordType.GetMethod("DeathNoted")!.Invoke(record, ["Leaf", "Bled out", null, null, null]);
        using var save = new MemoryStream();
        using var writer = new BinaryWriter(save, System.Text.Encoding.UTF8, true);
        recordType.GetMethod("Write")!.Invoke(record, [writer]);
        writer.Flush();
        save.Position = 0;
        using var reader = new BinaryReader(save);
        var loaded = Activator.CreateInstance(recordType, [rules])!;
        recordType.GetMethod("Read")!.Invoke(loaded, [reader]);
        foreach (var (name, r) in new[] { ("uninterrupted", record), ("save between notice and memorial", loaded) })
        {
            recordType.GetMethod("Died")!.Invoke(r, ["Leaf", 27d, "Market", null, null, null]);
            var row = ((System.Collections.IEnumerable)recordType.GetProperty("FallenMembers")!.GetValue(r)!).Cast<object>().Single();
            Console.WriteLine($"Death cause ({name}): {row.GetType().GetProperty("Cause")!.GetValue(row) ?? "missing"}");
        }
        return 0;
    }

    public static int Run(Assembly mod)
    {
        object settings = TypeIn(mod, "LwMod").GetField("Settings", Members)!.GetValue(null)!;
        void Set(string key, bool value) => settings.GetType().GetProperty(key)!.SetValue(settings, value);
        var message = Method(mod, "PlaytestLog", "Message").CreateDelegate<Action<string>>();
        var report = Method(mod, "PlaytestLog", "Report").CreateDelegate<Func<string, string>>();
        var crier = Method(mod, "Criers", "Look").CreateDelegate<Action<SimWorld, DialogueSystem, PopulationData>>();
        var publishMethod = PublishPanel(mod);
        Console.WriteLine($"Helper {TypeIn(mod, "LwMod").GetField("HelperBuild")!.GetRawConstantValue()} MVID {mod.ManifestModule.ModuleVersionId}");
        Console.WriteLine($"Meitou {typeof(SimWorld).Assembly.GetName().Version}. Median of seven headless batch means. Renderer and audio are absent.");
        foreach (var (members, npcs) in new[] { (8, 240), (24, 2000) })
        {
            var d = Data();
            int enemy = d.FactionIndex("60-t"), locals = d.FactionIndex("locals");
            d.Relations.Set(0, enemy, -80);
            var talk = new DialogueSystem(d);
            var rules = Array.CreateInstance(TypeIn(mod, "TitleRule"), 0);
            var system = (ITickSystem)Activator.CreateInstance(TypeIn(mod, "LwDeedsSystem"), rules, d)!;
            var news = (ITickSystem)Activator.CreateInstance(TypeIn(mod, "NewsSystem"), d)!;
            using var world = new SimWorld(new WorldSettings { Seed = 3, Threads = 1, PublishSnapshots = false, MinPartitionSize = 1 },
                new OpenGroundWalkability((_, _) => 150), [system, news]);
            world.Player.Faction = 0;
            for (int i = 0; i < members; i++) Spawn(world, 0, true, $"Squadmate {i}", Town + new Vector3(i, 0, 0));
            for (int i = 0; i < npcs; i++)
                Spawn(world, i % 2 == 0 ? enemy : locals, false, "NPC", Town + new Vector3(1850 + (i % 31) * 2, 0, (i / 31) * 2));
            world.RunTicks(40);
            var deeds = Method(mod, "LwDeeds", "For").Invoke(null, [world])!;
            var record = deeds.GetType().GetProperty("Record")!.GetValue(deeds)!;
            var kill = record.GetType().GetMethod("Kill")!;
            for (int i = 0; i < members; i++) kill.Invoke(record, [$"Squadmate {i}", "Dust Bandits", false, null]);

            var lookoutSystem = Activator.CreateInstance(TypeIn(mod, "Lookout"))!;
            var lookout = Method(mod, "Lookout", "Look").CreateDelegate<Action<SimWorld, DialogueSystem, PopulationData>>(lookoutSystem);
            var look = Method(mod, "LwDeedsSystem", "Look");
            Func<SimWorld, HashSet<string>> squadLook;
            if (look.GetParameters().Length == 1)
                squadLook = look.CreateDelegate<Func<SimWorld, HashSet<string>>>(system);
            else
            {
                var withCandidates = look.CreateDelegate<Func<SimWorld, List<int>?, HashSet<string>>>(system);
                squadLook = w => withCandidates(w, null);
            }
            var hear = Method(mod, "NewsSystem", "Hear").CreateDelegate<Action<SimWorld>>(news);
            var refresh = Method(mod, "NewsSystem", "Refresh").CreateDelegate<Action<SimWorld>>(news);
            var unused = Method(mod, "NewsSystem", "Report").CreateDelegate<Func<string,string,string,string,int,string,bool>>(news);
            for (int i = 0; i < 40; i++) _ = unused("fixture-" + i, "Market", "", "Test report " + i, 30, "TWR review fixture");
            world.Characters.Place(world.Characters.IdOf(0).Slot, Town, 0);
            var panelArgs = new object[] { world, deeds };
            Console.WriteLine($"Fixture: {members} squadmates and {npcs} NPCs outside warning range. No crier or lookout bark.");
            Measure("Squad scan", () => _ = squadLook(world));
            Measure("Lookout scan", () => lookout(world, talk, d));
            Measure("Crier scan", () => crier(world, talk, d));
            Set("RecordInPanels", true);
            Measure("Panel snapshot, enabled (includes reflective invocation)", () => publishMethod.Invoke(null, panelArgs));
            Set("RecordInPanels", false);
            Measure("Panel snapshot, disabled (includes reflective invocation)", () => publishMethod.Invoke(null, panelArgs));
            Measure("News slot refresh (40 reports)", () => refresh(world));
            Measure("News hear (settled board)", () => hear(world));
        }
        const string sample = "bench: a trader finished and left the market.";
        Set("PlaytestDiagnostics", false);
        Measure("Diagnostics message, disabled", () => message(sample), 2000);
        Set("PlaytestDiagnostics", true);
        Measure("Diagnostics message, enabled", () => message(sample), 2000);
        Measure("Format bounded diagnostics export, no filesystem", () => _ = report("Fixture squad"), 15);
        Set("PlaytestDiagnostics", false);
        return 0;
    }
}
