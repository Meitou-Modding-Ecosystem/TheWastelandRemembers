using System.Reflection;
using System.Runtime.Loader;
using LivingWasteland.MeitouMod;
using Meitou.Content;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Dialogue;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;
using Xunit;

[Collection("settings")]
public sealed class SystemRegistrationWorldTests
{
    const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    const int ContractGiver = 19421;
    static readonly string[] Common =
    [
        "lw-atlas", "lw-camps", "lw-deeds", "lw-hunters", "lw-keepsakes", "lw-news",
        "lw-prisoner-cases", "lw-raids", "lw-rivals", "lw-state", "lw-thefts"
    ];
    static readonly string[] Modern =
    [
        "lw-aid-use", "lw-couriers", "lw-death-notices", "lw-hive-envoys", "lw-lifecycle-held", "lw-lifecycle-knockout", "lw-lifecycle-limb",
        "lw-lifecycle-recruit", "lw-lifecycle-removed", "lw-neck-meshes", "lw-outpost-memories", "lw-platoon-fates", "lw-prisoner-deaths", "lw-prisoner-escorts",
        "lw-prisoner-recovery", "lw-recovery-clues", "lw-relations", "lw-reputation", "lw-reputation-price", "lw-theft-watch",
        "lw-town-switch", "lw-wall-construction", "lw-world-ready"
    ];

    // Register through the real entry point, without changing the helpers' process-wide state in other fixtures.
    sealed class RegistrationContext(string helperPath) : AssemblyLoadContext(isCollectible: false)
    {
        internal IMod Create()
        {
            var entry = LoadFromAssemblyPath(helperPath).GetType(typeof(LwMod).FullName!, throwOnError: true)!;
            return (IMod)Activator.CreateInstance(entry)!;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_release_client_registers_and_creates_every_expected_twr_system(bool shipped)
    {
        var helper = shipped ? Environment.GetEnvironmentVariable("TWR_SHIPPED_HELPER_DLL") : typeof(LwMod).Assembly.Location;
        if (string.IsNullOrWhiteSpace(helper)) return; // A standalone test run has no release artefact.
        using var world = StartWorld(Path.GetFullPath(helper), out var conditions);
        var names = world.Systems.Select(SimulationExtensions.FactoryOf).OfType<SystemFactory>().Select(f => f.Name).ToArray();
        var version = typeof(World).Assembly.GetName().Version!;
        var expected = version is { Major: 0, Minor: >= 23 } ? Common.Concat(Modern).ToArray() : Common;
        var missing = expected.Except(names, StringComparer.Ordinal);
        var extra = version is { Major: 0, Minor: 17 or >= 23 } ? names.Except(expected, StringComparer.Ordinal) : [];
        Assert.True(!missing.Any() && !extra.Any() && names.Distinct().Count() == names.Length,
            $"Meitou {version}. Missing: {string.Join(", ", missing)}. Unexpected: {string.Join(", ", extra)}. Actual: {string.Join(", ", names.Order())}.");
        var state = Assert.Single(world.Systems, system => SimulationExtensions.FactoryOf(system)?.Name == "lw-state");
        state.Inputs(world, []);
        var features = state.GetType().Assembly.GetType("LivingWasteland.MeitouMod.ActiveFeatures")!;
        var lines = (string[])features.GetMethod("Lines", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [])!;
        Assert.Contains("Squad Record, titles and History · On", lines);

        // Exercise a registered dialogue condition using the speaker's data record, not their display name.
        var vagrant = W.Spawn(world, "Fixture speaker one", W.Bandits, default);
        var townsperson = W.Spawn(world, "Fixture speaker two", W.Bandits, default);
        var player = W.Spawn(world, "Leaf", W.Player, default);
        var check = new DialogueCondition(ContractGiver, 0, 0, 1);
        Assert.True(conditions.TryGetValue(ContractGiver, out var giver));
        Assert.False(giver(new DialogueCheck(world, vagrant, player, check)));
        Assert.True(giver(new DialogueCheck(world, townsperson, player, check)));
    }

    [Fact]
    public void The_active_page_distinguishes_disabled_available_and_missing_features()
    {
        var settings = new LwSettings { OutpostCouriers = true, RememberOutposts = true, OtherModsNews = true };
        var systems = Common.ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Outpost couriers · Warning: needs a newer Meitou for platoon dispatch",
            ActiveFeatures.Describe(systems, settings, early: true));
        Assert.Contains("Outpost couriers · Warning: unavailable on this Meitou, missing platoon dispatch",
            ActiveFeatures.Describe(systems, settings, early: false));
        systems.Add("lw-couriers");
        Assert.Contains("Outpost couriers · On", ActiveFeatures.Describe(systems, settings, early: false));
        settings.RememberOutposts = false;
        Assert.Contains("Outpost couriers · Off, turn on Outpost history and News from other mods",
            ActiveFeatures.Describe(systems, settings, early: false));
        settings.OutpostCouriers = false;
        Assert.Contains("Outpost couriers · Off in Settings", ActiveFeatures.Describe(systems, settings, early: false));
        Assert.Contains("active", (string[])typeof(StatsUi).GetField("TabArgs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!);
    }

    static World StartWorld(string helperPath, out IReadOnlyDictionary<int, DialogueConditionHandler> conditions)
    {
        var folder = Directory.CreateTempSubdirectory("twr-registration-");
        try
        {
            // Exercise the same data-gated registrations as a full TWR installation, without any game files.
            File.WriteAllText(Path.Combine(folder.FullName, "lw_codes.json"), """
                {
                  "variables": [], "takeItems": [], "build": "fixture",
                  "camps": [{ "index": 0, "name": "Fixture outlaw", "character": "camp-leader", "squad": "camp-squad", "biomes": [] }],
                  "atlas": {}, "hunters": { "squad": "hunt-fixture" },
                  "notoriety": { "titles": [], "heatCondition": 19100, "heatEffect": 19101 },
                  "temperaments": { "once": 19400, "mark": 19401, "despised": 19402, "giver": 19421 },
                  "raids": [ { "name": "Fixture", "squad": "raid-fixture", "target": "Fixture", "state": 19102, "town": 19103 } ],
                  "trophies": { "keepsake": "neck-fixture", "keepsakes": ["neck-fixture"] }
                }
                """);
            File.WriteAllText(Path.Combine(folder.FullName, "lw_neck_meshes.json"), "[]");
            var data = W.Data([
                W.Rec("r-Fixture speaker one", FcsRecordType.Character, "Starving Vagrant"),
                W.Rec("r-Fixture speaker two", FcsRecordType.Character, "Town Trader"),
            ]);
            var ctor = Assert.Single(typeof(ModContext).GetConstructors(Instance));
            var args = ctor.GetParameters();
            var info = new ModInfo("LivingWasteland", Path.Combine(folder.FullName, "LivingWasteland.mod"), default);
            var manifest = new CodeModManifest("LivingWastelandMeitou", 2, ["LivingWastelandMeitou.dll"], typeof(LwMod).FullName!);
            var mod = Assert.Single(args[0].ParameterType.GetConstructors(Instance)).Invoke([info, manifest]);
            var registry = Activator.CreateInstance(args[1].ParameterType, nonPublic: true);
            var context = (ModContext)ctor.Invoke([mod, registry, data.Db, data, (Func<World?>)(() => null), null]);

            new RegistrationContext(helperPath).Create().Initialize(context);
            var pending = typeof(ModContext).GetProperty("Pending", Instance)!.GetValue(context)!;
            conditions = (IReadOnlyDictionary<int, DialogueConditionHandler>)pending.GetType().GetProperty("Conditions", Instance)!.GetValue(pending)!;
            var factories = (IEnumerable<SystemFactory>)pending.GetType().GetProperty("Systems", Instance)!.GetValue(pending)!;
            // Use the real engine build context with the native population system. A physics/rendering
            // installation is unnecessary here. Match the engine's behaviour of leaving out null factories.
            var pop = new PopulationSystem(data);
            var build = new SystemBuildContext(data, [pop]);
            var made = typeof(SimulationExtensions).GetMethod("Made", BindingFlags.Static | BindingFlags.NonPublic)!;
            var systems = new List<ITickSystem>();
            foreach (var factory in factories)
                if (factory.Create(build) is { } system)
                {
                    made.Invoke(null, [system, factory]);
                    systems.Add(system);
                }
            return W.World([.. systems]);
        }
        finally { folder.Delete(recursive: true); }
    }
}
