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
using Meitou.Simulation.Factions;
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

    internal static int PartyRetention(Assembly mod)
    {
        var hook = mod.GetType("LivingWasteland.MeitouMod.PlatoonRetention");
        if (hook is null) { Console.WriteLine("No TWR platoon retention in this helper."); return 0; }
        var type = (Type)hook.GetMethod("BuildWatch", Members)!.Invoke(null, null)!;
        var settings = TypeIn(mod, "LwMod").GetField("Settings", Members)!.GetValue(null)!;
        settings.GetType().GetProperty("BountyHunters")!.SetValue(settings, true);
        using var world = new SimWorld(new WorldSettings { Seed = 3, Threads = 1, PublishSnapshots = false },
            new OpenGroundWalkability((_, _) => 150), [(ITickSystem)Activator.CreateInstance(type)!]);
        Platoon Add()
        {
            var party = new Platoon { Id = world.Platoons.NextId(), TemplateId = "crew", Origin = 0, Key = 1 };
            world.Platoons.Add(party);
            return party;
        }

        var crew = Add();
        var other = Add();
        const string tag = "lw:bounty:Squin:138";
        Method(mod, "Pursuit", "Mark").Invoke(null, [crew, tag]);
        hook.GetMethod("Track", Members)!.Invoke(null, [world, crew, tag, Enum.Parse(hook.GetNestedType("Kind", Members)!, "Crew"), 138d, 210d, null]);
        world.GameHours = 150;
        world.RunTicks(1);

        // Call the engine dispatcher without timing reflection or boxing introduced by this fixture.
        var receiver = typeof(SimWorld).GetProperty("RemovalVetoes", Members)!;
        var method = receiver.PropertyType.GetMethod("KeepsPlatoon", Members)!;
        var sim = Expression.Parameter(typeof(SimWorld));
        var partyId = Expression.Parameter(typeof(int));
        var use = Expression.Lambda<Func<SimWorld, int, bool>>(
            Expression.Call(Expression.Property(sim, receiver), method, sim, partyId,
                Expression.Constant(RemovalKind.PlatoonOrphaned)), sim, partyId).Compile();
        if (!use(world, crew.Id) || use(world, other.Id)) throw new InvalidOperationException("Wrong retention fixture decision.");
        Measure("Engine orphan veto, unrelated platoon", () => _ = use(world, other.Id), 100_000);
        Measure("Engine orphan veto, live tracked party", () => _ = use(world, crew.Id), 100_000);
        world.GameHours = 300;
        if (use(world, crew.Id)) throw new InvalidOperationException("The expired job vetoed removal.");
        Measure("Engine orphan veto, expired party", () => _ = use(world, crew.Id), 100_000);
        return 0;
    }

    internal static int RememberedPeople(Assembly mod)
    {
        var homes = mod.GetType("LivingWasteland.MeitouMod.KindnessHomes");
        if (homes is null) { Console.WriteLine("No kindness home checks in this helper."); return 0; }
        var data = Data();
        using var world = new SimWorld(new WorldSettings { Seed = 3, Threads = 1, PublishSnapshots = false },
            new OpenGroundWalkability((_, _) => 150), []);
        world.Player.Faction = data.FactionIndex("204-gamedata.base");
        int faction = data.FactionIndex("locals");
        var tracker = TypeIn(mod, "LwDeedsSystem");
        var rules = Array.CreateInstance(TypeIn(mod, "TitleRule"), 0);
        var system = Activator.CreateInstance(tracker, rules, data)!;
        var deeds = tracker.GetProperty("Deeds", Members)!.GetValue(system)!;
        var book = deeds.GetType().GetProperty("Kindness", Members)!.GetValue(deeds)!;
        var homesSystem = deeds.GetType().GetProperty("KindnessWhere", Members)!.GetValue(deeds)!;
        var deedType = TypeIn(mod, "KindnessDeed");
        var saved = Enum.Parse(TypeIn(mod, "KindnessKind"), "Saved");
        var skills = Activator.CreateInstance(TypeIn(mod, "KindnessSkills"))!;
        var remembered = new List<ulong>();
        var add = book.GetType().GetMethod("Add", Members)!;
        const int Players = 25, Remembered = 6, Other = 1500;
        var squad = new List<(string Name, CharacterId Id, Vector3 At, bool Down)>();
        for (int i = 0; i < Players; i++)
        {
            var at = Town + new Vector3(7000 + i, 0, 0);
            var player = world.Characters.Spawn(new CharacterHot { Alive = true, Position = at },
                new CharacterCold { Name = $"Squadmate {i}", IsPlayer = true, Faction = world.Player.Faction, Medical = new MedicalState([]) }, world.Tick);
            squad.Add(($"Squadmate {i}", player, at, false));
        }
        for (int i = 0; i < Other; i++)
        {
            var id = world.Characters.Spawn(new CharacterHot { Alive = true, Position = Town + Vector3.UnitX * i },
                new CharacterCold { Name = $"Local {i}", Faction = faction, Medical = new MedicalState([]) }, world.Tick);
            if (i >= Remembered) continue;
            var actor = world.LastingIds.GetOrGive(id);
            remembered.Add(actor.Value);
            var deed = Activator.CreateInstance(deedType, actor.Value, saved, $"Local {i}", "local", "Locals", "Market",
                "Leaf", 70d, skills, true, false, 0d, false, "")!;
            if (add.Invoke(book, [deed]) is not true) throw new InvalidOperationException("Remembered fixture refused a deed.");
        }
        world.RunTicks(1);
        var target = homes.GetMethod("Look", Members)!;
        var simulation = Expression.Parameter(typeof(SimWorld));
        var call = Expression.Lambda<Action<SimWorld>>(
            Expression.Call(Expression.Constant(homesSystem), target, simulation, Expression.Constant(deeds), Expression.Constant(squad)), simulation).Compile();
        world.GameHours = 100;
        Measure("Six remembered people looking for home", () => call(world), 2000);
        world.GameHours = 108;
        call(world);
        Measure("Six settled homes, 25 squad positions", () => call(world), 2000);

        if (mod.GetType("LivingWasteland.MeitouMod.KindnessVoices") is { } voices)
        {
            voices.GetMethod("Load", Members)!.Invoke(null, [Path.GetDirectoryName(mod.Location)!, (Action<string>)Console.WriteLine]);
            if (voices.GetProperty("Current", Members)!.GetValue(null) is null)
                throw new InvalidOperationException("Copy kindness_lines.json into the benchmark's mod folder.");
            var home = Activator.CreateInstance(TypeIn(mod, "KindnessHome"), "Market", Town.X, Town.Z, 3500f, 108d, true, true)!;
            var set = book.GetType().GetMethod("SetHome", Members)!;
            foreach (ulong actor in remembered) set.Invoke(book, [actor, home]);
            Measure("Six ready reunions out of speaking range", () => call(world), 2000);
        }
        return 0;
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

    internal static int TradeQuotes(Assembly mod)
    {
        const int ruleCount = 75;
        var rules = Array.CreateInstance(TypeIn(mod, "TitleRule"), ruleCount);
        for (int i = 0; i < ruleCount; i++)
            rules.SetValue(Activator.CreateInstance(rules.GetType().GetElementType()!,
                $"title-{i}", $"Title {i}", "squad", "kills", null, null, i + 1, false), i);
        var data = Data();
        const string owner = "Locals";
        int faction = data.FactionIndex("locals");
        var modEntry = TypeIn(mod, "LwMod");
        modEntry.GetField("TitleViews", Members)!.SetValue(null, new Dictionary<string, Dictionary<string, List<string>>>
        {
            [owner] = new() { ["fears"] = [$"Title {ruleCount - 1}"] },
        });
        var settings = modEntry.GetField("Settings", Members)!.GetValue(null)!;
        settings.GetType().GetProperty("ReputationPrices")!.SetValue(settings, true);
        settings.GetType().GetProperty("TitleReactions")!.SetValue(settings, true);
        settings.GetType().GetProperty("PlaytestDiagnostics")!.SetValue(settings, false);

        var adapter = (Type)Method(mod, "TradeReputation", "Build").Invoke(null, null)!;
        using var world = new SimWorld(new WorldSettings { Seed = 3, Threads = 1, PublishSnapshots = false },
            new OpenGroundWalkability((_, _) => 150),
            [(ITickSystem)Activator.CreateInstance(TypeIn(mod, "LwDeedsSystem"), rules, data)!,
             (ITickSystem)Activator.CreateInstance(TypeIn(mod, "TradeReputation"), data)!,
             (ITickSystem)Activator.CreateInstance(adapter)!]);
        world.Player.Faction = 0;
        Spawn(world, 0, true, "Leaf", Town);
        var shop = new Squad { Id = world.Squads.NextId(), TemplateId = "market-shop", Faction = faction, Town = 0 };
        world.Squads.Add(shop);
        world.RunTicks(31);
        var deeds = Method(mod, "LwDeeds", "For").Invoke(null, [world])!;
        var record = deeds.GetType().GetProperty("Record", Members)!.GetValue(deeds)!;
        Method(mod, "SquadRecord", "SetCrewStat").Invoke(record, ["kills", (double)ruleCount]);
        var hook = typeof(World).Assembly.GetType("Meitou.Simulation.Trading.ITradePriceFilter", throwOnError: true)!;
        var payload = hook.GetMethod("Price")!.GetParameters()[1].ParameterType.GetElementType()!;
        object deal = Activator.CreateInstance(payload, world.Tick, "item", 1, false, world.Characters.IdOf(0), shop.Id)!;
        var quote = Method(mod, "TradeReputation", "Filter").CreateDelegate<Func<World, object, long, long>>();
        if (quote(world, deal, 100) != 108) throw new InvalidOperationException("Trade fixture did not apply the known title.");
        Console.WriteLine($"{ruleCount} configured titles, one feared by the current shop owner. Quote: 108 cats.");
        Measure("Repeated price quotes", () => quote(world, deal, 100), count: 100_000);
        return 0;
    }

    internal static int HeatEcho(Assembly mod)
    {
        var type = TypeIn(mod, "HeatMap");
        var heat = Activator.CreateInstance(type)!;
        var add = type.GetMethod("AddCrime", [typeof(string), typeof(string), typeof(double)])!
            .CreateDelegate<Action<string, string, double>>(heat);
        var read = type.GetMethod("For")!.CreateDelegate<Func<string?, string, double>>(heat);
        var fade = type.GetMethod("Fade", [typeof(double), typeof(ISet<string>)])!
            .CreateDelegate<Action<double, ISet<string>>>(heat);
        for (int i = 0; i < 100; i++) add($"Town {i:D2}", "Shek Kingdom", 24);
        const string faction = "Shek Kingdom";
        if (read(null, faction) != 12 || read("Town 00", faction) != 24)
            throw new InvalidOperationException("Heat fixture gave a different local or faction value.");
        Console.WriteLine("100 heated towns. Faction echo: 12. Local heat: 24.");
        Measure("Faction heat queries", () => read(null, faction), count: 100_000);
        var occupied = new HashSet<string> { "Town 00" };
        Measure("Fade after leaving 99 towns", () => fade(0.0001, occupied), count: 500);
        return 0;
    }

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
