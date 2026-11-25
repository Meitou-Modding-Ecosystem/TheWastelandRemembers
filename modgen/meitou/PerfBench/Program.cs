using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Meitou.Content;
using Meitou.Modding;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Population;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Check <Meitou DLL folder> <TWR mod folder> [profile|profile-full|bench|details|held|held-transitions|review-probes]");
    return 2;
}

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string file = Path.Combine(Path.GetFullPath(args[0]), name.Name + ".dll");
    return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
};
return RunReview(args[1..]);

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static int RunReview(string[] args)
{
    string release = Path.GetFullPath(args[0]);
    string binary = Path.Combine(release, "LivingWastelandMeitou.dll");
    using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(release, "lw_codes.json")));
    var build = table.RootElement.GetProperty("build").GetString();
    var file = FcsReader.ReadFile(Path.Combine(release, "LivingWasteland.mod"));
    var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(binary);
    if (args.Length > 1 && args[1] == "bench") return Bench.Run(mod);
    if (args.Length > 1 && args[1] == "held") return Bench.ObserveHeld();
    if (args.Length > 1 && args[1] == "details") return DetailBench.Run(mod);
    if (args.Length > 1 && args[1] == "held-transitions") return DetailBench.HeldTransitions();
    if (args.Length > 1 && args[1] == "review-probes") return Bench.ReviewProbes(mod);
    var entry = mod.GetType("LivingWasteland.MeitouMod.LwMod", throwOnError: true)!;
    bool match = build == (string?)entry.GetField("HelperBuild")!.GetRawConstantValue();
    Console.WriteLine($"Build {build}, helper build matches: {match}");
    Console.WriteLine($"Workshop records: {file.Records.Count}");

    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var ctx = typeof(ModContext).GetConstructors(flags).Single();
    var ct = ctx.GetParameters()[0].ParameterType.GetConstructors(flags).Single();
    var info = new ModInfo("LivingWasteland", Path.Combine(release, "LivingWasteland.mod"), default);
    var manifest = new CodeModManifest("LivingWastelandMeitou", 2, ["LivingWastelandMeitou.dll"], entry.FullName!);
    var modInfo = ct.Invoke([info, manifest]);
    var registry = Activator.CreateInstance(ctx.GetParameters()[1].ParameterType, nonPublic: true);
    var db = new GameDatabase();
    db.Apply(file, "LivingWasteland.mod");
    var population = PopulationData.Create(db, []);
    var context = (ModContext)ctx.Invoke([modInfo, registry, db, population, (Func<World?>)(() => null), null]);
    ((IMod)Activator.CreateInstance(entry)!).Initialize(context);
    var pending = typeof(ModContext).GetProperty("Pending", flags)!.GetValue(context)!;
    if (args.Length > 1 && args[1] is "profile" or "profile-full")
        return WorldProfile.Run(mod, (IEnumerable<SystemFactory>)pending.GetType().GetProperty("Systems", flags)!.GetValue(pending)!,
            fullOnly: args[1] == "profile-full");
    HashSet<int> Registered(string property) => ((IDictionary)pending.GetType().GetProperty(property, flags)!.GetValue(pending)!).Keys.Cast<int>().ToHashSet();

    var actions = file.Records.Where(r => r.Type == (int)FcsRecordType.DialogAction).ToDictionary(r => r.StringId);
    var issues = new List<string>();
    if (!match) issues.Add("Helper build differs from lw_codes.json");
    var groups = new[] { ("conditions", "condition name", "Conditions"), ("effects", "action name", "Effects") };
    foreach (var (field, name, registrations) in groups)
    {
        var registered = Registered(registrations);
        var rows = actions.Values.Where(r => r.Ints.GetValueOrDefault(name) >= 1000).ToArray();
        var codes = rows.Select(r => r.Ints[name]).ToHashSet();
        var unused = registered.Except(codes).Order().ToArray();
        int refs = 0;
        var referenced = new HashSet<int>();
        var compare = new SortedDictionary<int, int>();
        foreach (var r in file.Records)
        {
            if (!r.References.TryGetValue(field, out var entries)) continue;
            foreach (var reference in entries)
            {
                if (!actions.TryGetValue(reference.TargetStringId, out var a))
                {
                    if (reference.TargetStringId.EndsWith("-LivingWasteland.mod", StringComparison.Ordinal))
                        issues.Add($"Missing {field} target from {r.StringId}: {reference.TargetStringId}");
                    continue;
            }
                if (!a.Ints.TryGetValue(name, out int code))
                {
                    issues.Add($"{field} target {a.StringId} does not declare {name}");
                    continue;
            }
                if (code < 1000) continue;
                refs++;
                referenced.Add(code);
                if (field == "conditions")
                {
                    int how = a.Ints.GetValueOrDefault("compare by");
                    compare[how] = compare.GetValueOrDefault(how) + 1;
                }
            }
        }
        foreach (var code in codes.Except(registered).Order()) issues.Add($"Missing registered {name} {code}");
        Console.WriteLine($"{field}: {rows.Length} custom action records, {codes.Count} defined codes, {referenced.Count} referenced codes over {refs} references, {registered.Count} registrations ({unused.Length} registered codes absent from the .mod)");
        if (compare.Count != 0) Console.WriteLine($"Comparison counts: {string.Join(", ", compare.Select(p => $"{p.Key}={p.Value}"))}");
    }
    foreach (var issue in issues) Console.WriteLine(issue);


    Console.WriteLine($"Registry and FCS structural issues: {issues.Count}");
    var api = mod.GetType("LivingWasteland.MeitouMod.WastelandApi", throwOnError: true)!;
    Console.WriteLine("TWR API version: " + api.GetField("Version")!.GetRawConstantValue());
    foreach (var name in new[] { "ReportAtNode", "Titles", "LeaveAloneId", "StrikeAnswer", "StrikableAfter" })
    {
        var method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
        Console.WriteLine($"{name}: " + (method?.ToString() ?? "absent"));
    }
    if (api.GetMethod("StrikeAnswer") is { } answer)
        Console.WriteLine("StrikeAnswer without a world: " + answer.Invoke(null, ["Not an outlaw"]));
    if (api.GetMethod("StrikableAfter") is { } after)
        Console.WriteLine("StrikableAfter without a world: " + after.Invoke(null, ["Not an outlaw"]));
    if (api.GetMethod("LeaveAloneId") is { } lie)
        Console.WriteLine("LeaveAloneId(0) without a world: " + lie.Invoke(null, [0UL, "review fixture"]));
    Console.WriteLine($"Meitou code API: {ModApi.Version}");
    var sim = typeof(World).Assembly;
    var veto = sim.GetType("Meitou.Simulation.Bodies.IRemovalVeto");
    Console.WriteLine("IRemovalVeto: " + veto?.GetMethod("Keep"));
    Console.WriteLine("Platoon tags: " + sim.GetType("Meitou.Simulation.Factions.Platoon")?.GetProperty("ModTags"));
    return issues.Count == 0 ? 0 : 1;
}
