using System.Reflection;
using System.Runtime.CompilerServices;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

internal static class PlaytestLog
{
    sealed class Segment(int number)
    {
        internal readonly int Number = number;
        internal readonly Dictionary<(string System, string Key), (string Phase, string Detail)> Last = [];
    }

    static readonly ConditionalWeakTable<World, Segment> segments = new();
    static readonly PlaytestJournal journal = new();
    static readonly Guid session = Guid.NewGuid();
    static int next;
    internal static Action<string>? EngineLog { get; set; }
    internal static bool Enabled => LwMod.Settings.PlaytestDiagnostics;
    internal static PlaytestJournal.Snapshot Read() => journal.Read();

    // Only the serial simulation thread assigns world identifiers and reads world data.
    // The automatic export: while diagnostics are on, one file per launch (TWR-playtest-auto-<start>.txt) is rewritten every minute, so a
    // play test needs no Export click and a crash or a second launch loses at most a minute; Save logs for a bug report.bat collects it.
    static readonly string autoName = $"TWR-playtest-auto-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt";
    static Timer? autoTimer;
    static int autoWriting;
    internal static string AutoName => autoName;

    static void StartAuto()
    {
        if (Volatile.Read(ref autoTimer) is not null) return;
        var timer = new Timer(_ => AutoExport(), null, Timeout.Infinite, Timeout.Infinite);
        if (Interlocked.CompareExchange(ref autoTimer, timer, null) is null) timer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        else timer.Dispose();
    }

    /// <summary>Writes the automatic export now (the timer calls it each minute); false while diagnostics are off or it fails.</summary>
    internal static bool AutoExport()
    {
        if (!Enabled || Interlocked.Exchange(ref autoWriting, 1) == 1) return false;
        try { StatsUi.WritePlaytest(autoName); return true; }
        catch (Exception e) { RecordHost.Log($"playtest auto-export failed: {e.GetBaseException().Message}"); return false; }
        finally { Volatile.Write(ref autoWriting, 0); }
    }

    static Segment Of(World world)
    {
        StartAuto();
        if (segments.TryGetValue(world, out var segment)) return segment;
        segment = new(Interlocked.Increment(ref next));
        segments.Add(world, segment);
        Write(world, segment, "world", "", "observed",
            $"Helper {LwMod.HelperBuild}; data {LwMod.DataBuild}; module {typeof(LwMod).Module.ModuleVersionId}; " +
            $"client {VersionOf(typeof(Meitou.Modding.IMod).Assembly)}");
        var set = LwMod.Settings;
        Write(world, segment, "world", "", "feature switches",
            $"outposts={set.RememberOutposts} couriers={set.OutpostCouriers} handovers={set.TownHandovers} " +
            $"cases={set.PrisonerCases} deals={set.PrisonerDeals} envoys={set.HiveEnvoys} escorts={set.PrisonerEscorts} " +
            $"recovery={set.PrisonerRecovery} siege={set.ShekGateRecovery} covert={set.CovertRecovery} " +
            $"rivals={set.Rivals} recruitment={set.RecruitRivals} prices={set.ReputationPrices} recap={set.RecapMoments}");
        return segment;
    }

    internal static void Observe(World world)
    {
        if (Enabled) _ = Of(world);
    }

    internal static void Event(World world, string system, string key, string phase, string detail = "")
    {
        if (!Enabled) return;
        var segment = Of(world);
        Write(world, segment, system, key, phase, detail);
    }

    internal static void Change(World world, string system, string key, string phase, string detail = "")
    {
        if (!Enabled) return;
        var segment = Of(world);
        var scope = (system, key);
        var value = (phase, detail);
        if (segment.Last.TryGetValue(scope, out var old) && old == value) return;
        if (segment.Last.Count >= 512) segment.Last.Clear();
        segment.Last[scope] = value;
        Write(world, segment, system, key, phase, detail);
    }

    static void Write(World world, Segment segment, string system, string key, string phase, string detail) =>
        Mirror(journal.Add(segment.Number, world.Tick, world.GameHours, system, key, phase, detail));

    internal static void Message(string line)
    {
        if (!Enabled) return;
        int split = line.IndexOf(':');
        string system = split is > 0 and <= 48 ? line[..split] : "TWR";
        journal.Add(null, null, null, system, "", PlaytestJournal.ModLog, split is > 0 and <= 48 ? line[(split + 1)..].TrimStart() : line);
    }

    internal static void Control(string key, string phase, string detail = "")
    {
        StartAuto();
        Mirror(journal.Add(null, null, null, "playtest", key, phase, detail));
    }

    static void Mirror(PlaytestJournal.Event row)
    {
        try { EngineLog?.Invoke($"TWR playtest {session:N}\t{PlaytestJournal.FormatRow(row)}"); }
        catch (Exception) { /* The in-memory row remains available even if the engine's logger is unavailable. */ }
    }

    internal static string Report(string story)
    {
        const string header = "THE WASTELAND REMEMBERS — PLAYTEST LOG 1";
        string settings = string.Join(Environment.NewLine, SettingsFile.Group?.Settings.Select(s => $"{s.Key} = {s.Text}") ?? []);
        string companions = string.Join(Environment.NewLine, AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name is { } n && Companion(n)).Select(a => $"{a.GetName().Name} {VersionOf(a)}").Distinct().Order());
        return string.Join(Environment.NewLine + Environment.NewLine, header,
            $"Session {session:N}\nExported at {DateTimeOffset.UtcNow:O}\nHelper build {LwMod.HelperBuild}\nLoaded data {LwMod.DataBuild}\n" +
            $"Helper module ID {typeof(LwMod).Module.ModuleVersionId}\n" +
            $"Meitou {VersionOf(typeof(Meitou.Modding.IMod).Assembly)}\nCapture enabled {Enabled}\n" +
            $"Hooks: world ready {WorldReady.Available}, couriers {Couriers.Available}, custody {PrisonerHandover.Available}, " +
            $"covert recovery {CovertRecovery.Available}, town handovers {TownSwitchWatch.Available}",
            "SETTINGS AT EXPORT\n" + settings, "LOADED COMPANION ASSEMBLIES\n" + (companions.Length > 0 ? companions : "(none detected)"),
            "SESSION TIMELINE\n" + PlaytestJournal.Format(Read()), "SQUAD STORY AT EXPORT\n" + story);
    }

    static bool Companion(string name) => name.StartsWith("PZA", StringComparison.OrdinalIgnoreCase) ||
        name is "Recap" or "RivalryCore" or "LogisticsCore" or "Husbandry" or "AshenHand" or "Dustlife" or "SquadArena";

    static string VersionOf(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        assembly.GetName().Version?.ToString() ?? "unknown";
}
