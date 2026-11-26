using System.Reflection;
using System.Runtime.Loader;
using LivingWasteland.MeitouMod;
using Meitou.Modding;
using Xunit;

[Collection("settings")]
public sealed class SteamRollbackWorldTests
{
    static readonly Assembly Current = typeof(LwMod).Assembly;
    static readonly Lazy<Assembly?> Previous = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("TWR_PREVIOUS_STEAM_DLL");
        if (string.IsNullOrWhiteSpace(path)) return null;
        return new PreviousBuildContext().LoadFromAssemblyPath(Path.GetFullPath(path));
    });

    // Resolve the engine and Harmony in the test's normal context. Only the old TWR helper gets its own statics.
    sealed class PreviousBuildContext() : AssemblyLoadContext("Last Steam TWR")
    {
        protected override Assembly? Load(AssemblyName name) => null;
    }

    public static IEnumerable<object[]> Systems => SteamSaveSamples.Names.Select(name => new object[] { name });

    [Fact]
    public void The_rollback_cache_contains_the_expected_Steam_helper()
    {
        if (Previous.Value is not { } old) return;
        var expected = Environment.GetEnvironmentVariable("TWR_PREVIOUS_STEAM_BUILD") ?? "20261125bo";
        var version = old.GetType(typeof(LwMod).FullName!, throwOnError: true)!
            .GetField(nameof(LwMod.HelperBuild), BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue();
        Assert.Equal(expected, version);
        Assert.NotEqual(Current.ManifestModule.ModuleVersionId, old.ManifestModule.ModuleVersionId);
    }

    [Fact]
    public void Every_saved_system_has_a_representative_nonempty_fixture()
    {
        static string[] Savers(Assembly assembly) => assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IModSaveData).IsAssignableFrom(t))
            .Select(t => t.Name).Order(StringComparer.Ordinal).ToArray();

        var covered = SteamSaveSamples.Names.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(Savers(Current), covered);
        if (Previous.Value is { } old)
            Assert.Empty(Savers(old).Except(covered, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void Save_round_trips_without_dropping_state_understood_by_either_build(string name)
    {
        var seed = SteamSaveSamples.For(name);
        var currentSample = Create(Current, name);
        Read(currentSample, seed);
        var currentSave = Write(currentSample);
        Assert.False(Write(Create(Current, name)).SequenceEqual(currentSave), "The fixture produced an empty save.");

        // Always exercise the current reader, including when the local developer does not have the release cache.
        var currentAgain = Create(Current, name);
        Read(currentAgain, currentSave);
        Assert.Equal(currentSave, Write(currentAgain));

        if (Previous.Value is not { } old || old.GetType(currentSample.GetType().FullName!) is null) return;

        var previousSample = Create(old, name);
        Read(previousSample, seed);
        var previousSave = Write(previousSample);
        Assert.False(Write(Create(old, name)).SequenceEqual(previousSave), "The Steam reader discarded the fixture.");

        var rolledBack = Create(old, name);
        Read(rolledBack, currentSave);
        Assert.Equal(previousSave, Write(rolledBack));                   // branch -> last Steam release

        var upgraded = Create(Current, name);
        Read(upgraded, previousSave);                                 // last Steam release -> branch
        Assert.False(Write(Create(Current, name)).SequenceEqual(Write(upgraded)), "The branch reader discarded the Steam save.");
        var legacyRoundTrip = Create(old, name);
        Read(legacyRoundTrip, Write(upgraded));
        Assert.Equal(previousSave, Write(legacyRoundTrip));
    }

    [Fact]
    public void A_new_reprisal_flag_is_kept_by_this_build_and_ignored_by_the_Steam_reader()
    {
        const string name = nameof(OutlawCampsSystem);
        var extended = SteamSaveSamples.For(name, taggedReprisal: true);
        var branch = Create(Current, name);
        Read(branch, extended);
        Assert.Equal(extended, Write(branch));                          // the branch keeps the new fact

        if (Previous.Value is not { } old) return;
        var baseline = Create(old, name);
        var original = SteamSaveSamples.For(name);
        Read(baseline, original);
        var rollback = Create(old, name);
        Read(rollback, Write(branch));
        Assert.Equal(Write(baseline), Write(rollback));                 // old fields and pursuit survive the extra trailer
    }

    [Fact]
    public void Kindness_trailer_preserves_the_existing_squad_and_town_records_when_rolled_back()
    {
        var current = new LwDeedsSystem([], null);
        var seed = SteamSaveSamples.For(nameof(LwDeedsSystem));
        Read(current, seed);
        current.Deeds.Kindness.Add(new KindnessDeed(23, KindnessKind.Saved, "Leaf", "wanderer", "Nomads",
            "Squin", "Ruka", 150, new(4, 17, 9), true, false));
        var saved = Write(current);
        var restored = new LwDeedsSystem([], null);
        Read(restored, saved);
        Assert.Equal(23UL, Assert.Single(restored.Deeds.Kindness.People).Actor);
        Assert.Equal(27d, restored.Deeds.Heat.Here("Squin"));

        if (Previous.Value is not { } old) return;
        var expected = Create(old, nameof(LwDeedsSystem));
        Read(expected, seed);
        var rolledBack = Create(old, nameof(LwDeedsSystem));
        Read(rolledBack, saved);
        Assert.Equal(Write(expected), Write(rolledBack));
    }

    [Fact]
    public void A_fight_trailer_keeps_the_old_squad_record_when_rolled_back()
    {
        var seed = SteamSaveSamples.For(nameof(LwDeedsSystem));
        var branch = new LwDeedsSystem([], null);
        Read(branch, seed);
        branch.Deeds.Record.FightLook(["Leaf"], []);
        branch.Deeds.Record.Knockout("Leaf");
        var saved = Write(branch);
        var current = new LwDeedsSystem([], null);
        Read(current, saved);
        Assert.True(current.Deeds.Record.InFight);
        Assert.Equal(saved, Write(current));

        if (Previous.Value is not { } old) return;
        var rollback = Create(old, nameof(LwDeedsSystem));
        Read(rollback, saved);
        var expected = Create(old, nameof(LwDeedsSystem));
        Read(expected, seed);
        var deed = expected.GetType().GetProperty("Deeds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(expected)!;
        var record = deed.GetType().GetProperty("Record")!.GetValue(deed)!;
        record.GetType().GetMethod("Knockout")!.Invoke(record, ["Leaf"]);
        Assert.Equal(Write(expected), Write(rollback));
    }

    [Fact]
    public void A_witness_trailer_is_ignored_by_the_Steam_quest_reader()
    {
        const string name = nameof(LwSystem);
        var seed = SteamSaveSamples.For(name);
        using var appended = new MemoryStream();
        appended.Write(seed);
        using (var writer = new BinaryWriter(appended, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x31544957); // WIT1
            writer.Write(1);
            writer.Write("sample-town"); writer.Write(3UL); writer.Write(7UL); writer.Write(75.0); writer.Write(true);
        }

        var current = Create(Current, name);
        Read(current, appended.ToArray());
        Assert.Equal(appended.ToArray(), Write(current));
        if (Previous.Value is not { } old) return;
        var rollback = Create(old, name);
        Read(rollback, Write(current));
        var expected = Create(old, name);
        Read(expected, seed);
        Assert.Equal(Write(expected), Write(rollback));
    }

    [Fact]
    public void The_animal_memorial_trailer_can_follow_witness_memory_without_changing_old_quests()
    {
        const string name = nameof(LwSystem);
        var seed = SteamSaveSamples.For(name);
        using var appended = new MemoryStream();
        appended.Write(seed);
        using (var writer = new BinaryWriter(appended, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x31544957); // WIT1
            writer.Write(1);
            writer.Write("sample-town"); writer.Write(3UL); writer.Write(7UL); writer.Write(75.0); writer.Write(true);
            writer.Write(0x31544550); // PET1
            writer.Write(1);
            writer.Write("Hound"); writer.Write(21.0); writer.Write("Squin"); writer.Write(true);
        }

        var current = Create(Current, name);
        Read(current, appended.ToArray());
        Assert.Equal(appended.ToArray(), Write(current));
        if (Previous.Value is not { } old) return;
        var rollback = Create(old, name);
        Read(rollback, Write(current));
        var expected = Create(old, name);
        Read(expected, seed);
        Assert.Equal(Write(expected), Write(rollback));
    }

    [Fact]
    public void The_raid_record_trailer_keeps_the_old_outpost_records_when_rolled_back()
    {
        const string name = nameof(OutpostMemories);
        byte[] seed = SteamSaveSamples.For(name);
        using var appended = new MemoryStream();
        appended.Write(seed);
        using (var writer = new BinaryWriter(appended, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x31415244); // DRA1
            writer.Write(1); writer.Write("outpost:house"); writer.Write(1); writer.Write(1);
            writer.Write("60-t"); writer.Write("Dust Bandits"); writer.Write("dust-assault"); writer.Write(3);
            writer.Write(140d); writer.Write(false); writer.Write(0);
        }

        var current = Create(Current, name);
        Read(current, appended.ToArray());
        Assert.Equal(appended.ToArray(), Write(current));

        if (Previous.Value is not { } old || old.GetType(current.GetType().FullName!) is null) return;
        var expected = Create(old, name);
        Read(expected, seed);
        var rollback = Create(old, name);
        Read(rollback, Write(current));
        Assert.Equal(Write(expected), Write(rollback));
    }

    static IModSaveData Create(Assembly assembly, string name)
    {
        var type = assembly.GetType($"LivingWasteland.MeitouMod.{name}", throwOnError: true)!;
        var constructor = Assert.Single(type.GetConstructors());
        var args = constructor.GetParameters().Select(parameter =>
        {
            Type target = parameter.ParameterType;
            if (target == typeof(string)) return (object?)"rollback-sample";
            if (target.Name == nameof(AtlasTable)) return Activator.CreateInstance(target, [null, null, null]);
            if (!target.IsGenericType || target.GetGenericTypeDefinition() != typeof(IReadOnlyList<>)) return null;
            var element = target.GetGenericArguments()[0];
            var list = Array.CreateInstance(element, element.Name == nameof(RaidCode) ? 1 : 0);
            if (list.Length == 1)
                list.SetValue(Activator.CreateInstance(element, "Sample raid", "crew", "shek", 4, 0, ""), 0);
            return list;
        }).ToArray();
        return (IModSaveData)constructor.Invoke(args);
    }

    static void Read(IModSaveData saver, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        saver.Read(stream);
    }

    static byte[] Write(IModSaveData saver)
    {
        using var stream = new MemoryStream();
        saver.Write(stream);
        return stream.ToArray();
    }
}
