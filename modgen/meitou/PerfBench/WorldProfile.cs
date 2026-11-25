using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

static class WorldProfile
{
    static readonly Vector3 Market = new(14000, 150, 14000);
    const int Squads = 100, Members = 15, Warmup = 900, Batch = 1800, Repeats = 5;

    public static int Run(Assembly mod, IEnumerable<SystemFactory> factories, bool fullOnly = false)
    {
        var registered = factories.ToArray();
        Console.WriteLine($"Meitou {typeof(World).Assembly.GetName().Version}, helper MVID {mod.ManifestModule.ModuleVersionId}");
        Console.WriteLine($"{Squads * Members} characters, {Squads} squads, {Squads - 1} loaded NPC platoons, seed 3, one worker.");
        Console.WriteLine($"Warmup {Warmup} ticks, median of {Repeats} batches of {Batch} ticks. p99/max = median across batches.");
        const string crowded = "crowded market, out of bark range";
        if (fullOnly)
        {
            RunScenario(registered, crowded, "full TWR");
            return 0;
        }
        RunScenario([], crowded, "no TWR");
        RunScenario(registered, crowded, "full TWR");
        RunScenario(registered.Where(f => !f.Name.StartsWith("lw-lifecycle-", StringComparison.Ordinal)).ToArray(), crowded, "without TWR lifecycle observers");
        RunScenario(registered.Where(f => f.Name != "lw-lifecycle-held").ToArray(), crowded, "without TWR held observer");
        RunScenario(registered, "away from town", "full TWR");
        RunScenario(registered, "market with a nearby crier", "full TWR");
        object settings = mod.GetType("LivingWasteland.MeitouMod.LwMod", throwOnError: true)!
            .GetField("Settings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        if (settings.GetType().GetProperty("LighterChecks") is { } lighter)
        {
            lighter.SetValue(settings, true);
            RunScenario(registered, crowded, "Lighter checks");
        }
        return 0;
    }

    static void RunScenario(SystemFactory[] registered, string scenario, string configuration)
    {
        var walk = new OpenGroundWalkability((_, _) => 150);
        var extensions = new SimulationExtensions();
        extensions.Systems.AddRange(registered);
        var data = FixtureData();
        var systems = StandardSystems.Build(data, walk, new StandardSystemOptions
        {
            Parts = StandardParts.Population | StandardParts.Dialogue,
            Extensions = extensions,
        });
        using var world = new World(new WorldSettings { Seed = 3, Threads = 1, PublishSnapshots = false, MinPartitionSize = 1 }, walk, systems.Systems);
        world.Player.Faction = data.FactionIndex("204-gamedata.base");
        int local = data.FactionIndex("locals"), bandit = data.FactionIndex("60-t");
        data.Relations.Set(world.Player.Faction, bandit, -80);
        Vector3 centre = scenario == "away from town" ? Market + new Vector3(10000, 0, 10000) : Market;
        for (int i = 0; i < Squads; i++)
        {
            bool player = i == 0;
            int faction = player ? world.Player.Faction : i % 2 == 0 ? bandit : local;
            float x = player ? 0 : i == 1 && scenario == "market with a nearby crier" ? 300 : 1850 + (i % 11) * 40;
            Vector3 at = centre + new Vector3(x, 0, player ? 0 : (i / 11) * 40);
            var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "bench-squad", Faction = faction };
            for (int j = 0; j < Members; j++)
            {
                var member = world.Characters.Spawn(new CharacterHot { Alive = true, Position = at + new Vector3(j, 0, 0) },
                    new CharacterCold { Name = $"Bench {i}-{j}", RecordId = "bench-character", IsPlayer = player,
                        SquadId = squad.Id, Faction = faction, Medical = new MedicalState([]) }, world.Tick);
                squad.Members.Add(member);
            }
            squad.Leader = squad.Members[0];
            world.Squads.Add(squad);
            if (player) continue;
            world.Platoons.Add(new Platoon { Id = world.Platoons.NextId(), SquadId = squad.Id, TemplateId = squad.TemplateId,
                Origin = 0, Key = (ulong)i, Faction = faction, State = PlatoonState.Loaded, Position = new Vector2(at.X, at.Z) });
        }
        world.TellReady(false);
        world.RunTicks(1);
        var news = world.Systems.FirstOrDefault(s => SimulationExtensions.FactoryOf(s)?.Name == "lw-news");
        if (news is not null)
        {
            var report = news.GetType().GetMethod("Report")!.CreateDelegate<Func<string, string, string, string, int, string, bool>>(news);
            for (int i = 0; i < 40; i++)
                if (!report($"bench-{i}", "Market", "", $"Fixture report {i}", 30, "Performance world"))
                    throw new InvalidOperationException("Fixture report was refused.");
        }
        var seeds = world.TakeMessages().ToArray();
        world.RunTicks(Warmup - 1);
        RequireUnfaulted(world, seeds.Concat(world.TakeMessages()));

        var modRows = world.Systems.Select((s, i) => (Factory: SimulationExtensions.FactoryOf(s), Index: i))
            .Where(row => row.Factory is not null).ToArray();
        if (modRows.Length != registered.Length)
            throw new InvalidOperationException($"Only {modRows.Length} of {registered.Length} mod systems were created.");
        var timings = new double[world.Systems.Count, Repeats];
        var ticks = new double[Batch];
        var total = new double[Repeats];
        var p99 = new double[Repeats];
        var max = new double[Repeats];
        var alloc = new double[Repeats];
        world.Profile.Enabled = true;
        for (int round = 0; round < Repeats; round++)
        {
            world.Profile.Clear();
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < Batch; tick++)
            {
                long start = Stopwatch.GetTimestamp();
                world.RunTicks(1);
                ticks[tick] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }
            alloc[round] = (GC.GetAllocatedBytesForCurrentThread() - bytes) / (double)Batch;
            if (world.Profile.Ticks != Batch)
                throw new InvalidOperationException("Wrong number of profiled ticks.");
            total[round] = ticks.Average();
            Array.Sort(ticks);
            p99[round] = ticks[(int)Math.Ceiling(Batch * .99) - 1];
            max[round] = ticks[^1];
            for (int i = 0; i < world.Systems.Count; i++)
                timings[i, round] = world.Profile.SystemMs[i] * 1000 / Batch;
        }
        RequireUnfaulted(world, world.TakeMessages());
        int alive = 0;
        foreach (ref readonly var hot in world.Characters.Previous)
            if (hot.Alive) alive++;
        if (alive != Squads * Members || world.Squads.All.Count() != Squads)
            throw new InvalidOperationException($"Fixture membership changed: {alive} characters, {world.Squads.All.Count()} squads.");
        Console.WriteLine();
        Console.WriteLine($"{scenario} ({configuration})");
        Console.WriteLine(FormattableString.Invariant($"Total: {Median(total):F2} us/tick, p99 {Median(p99):F2}, max {Median(max):F2}, {Median(alloc):F0} B/tick across the whole world"));
        var rows = modRows.Select(row => (Name: row.Factory!.Name, Mean: Median(Enumerable.Range(0, Repeats).Select(k => timings[row.Index, k]))))
            .OrderByDescending(row => row.Mean).ThenBy(row => row.Name).ToArray();
        Console.WriteLine(FormattableString.Invariant($"TWR (sum of system medians): {rows.Sum(row => row.Mean):F3} us/tick across {rows.Length} systems"));
        foreach (var (name, mean) in rows)
            Console.WriteLine(FormattableString.Invariant($"  {name,-26} {mean,7:F3} us/tick"));
    }

    static void RequireUnfaulted(World world, IEnumerable<string> messages)
    {
        foreach (var message in messages)
            if (message.Contains("stopped", StringComparison.OrdinalIgnoreCase)
                || message.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                || message.Contains("faulted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"World at {world.Tick}: {message}");
    }

    static double Median(IEnumerable<double> values) => values.Order().ElementAt(Repeats / 2);

    static PopulationData FixtureData()
    {
        var fcs = new FcsFile();
        FcsRecord Record(string id, FcsRecordType type, string name)
        {
            var record = new FcsRecord { StringId = id, Type = (int)type, Flags = 0x10, Name = name };
            fcs.Records.Add(record);
            return record;
        }
        Record("204-gamedata.base", FcsRecordType.Faction, "Nameless");
        Record("60-t", FcsRecordType.Faction, "Dust Bandits");
        Record("locals", FcsRecordType.Faction, "Locals");
        var town = Record("market", FcsRecordType.Town, "Market");
        town.References["faction"] = [new("locals", 0, 0, 0)];
        var db = new GameDatabase();
        db.Apply(fcs, "TwrBenchmark.mod");
        return PopulationData.Create(db, [new TownPlacement("market-placement", "market", Market, null)]);
    }
}
