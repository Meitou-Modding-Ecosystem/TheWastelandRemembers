// The engine's character lifecycle notices (Meitou after 0.22.2-nightly.20261124.3, VlcekM/MeitouClient#1370): knockouts, limbs lost,
// cages and beds entered and left, joining or leaving a faction, a character leaving the world. TWR reads them for its play-test log
// (CLAUDE.md: everything a system does should be logged), so a play test on the nightly shows what happened to the squad and its captives
// in the engine's own words, and for one rule: a character who joins the player's faction is first seen by the squad record at that moment.
// Each is built by reflection (ObserverEmit) and added only where the client has it; the 0.17.3 build loads everywhere.
// Logged: the squad's knockouts and limbs, and named characters leaving the world, with Collect playtest diagnostics on; cages entered or
// left, and joins and departures, with Log quest changes (on by default: both are rare).
using System.Reflection;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Locks;

namespace LivingWasteland.MeitouMod;

public static class LifecycleWatch
{
    static readonly Assembly engine = typeof(World).Assembly;
    static readonly Type? knockoutPayload = engine.GetType("Meitou.Simulation.Bodies.KnockoutChange");
    static readonly PropertyInfo? knockedCharacter = knockoutPayload?.GetProperty("Character"),
        unconscious = knockoutPayload?.GetProperty("Unconscious"), koTimer = knockoutPayload?.GetProperty("KoTimer"), coma = knockoutPayload?.GetProperty("Coma");
    static readonly Type? limbPayload = engine.GetType("Meitou.Simulation.Bodies.LimbChange");
    static readonly PropertyInfo? limbCharacter = limbPayload?.GetProperty("Character"), limbPart = limbPayload?.GetProperty("Part"),
        limbBefore = limbPayload?.GetProperty("Before"), limbAfter = limbPayload?.GetProperty("After");
    static readonly PropertyInfo? removedCharacter = engine.GetType("Meitou.Simulation.Bodies.CharacterRemoved")?.GetProperty("Character");

    /// <summary>The notices this mod reads: interface, its method, the payload type, and this class's handler.</summary>
    static readonly (string Interface, string Method, string Payload, string Handler)[] hooks =
    [
        ("Meitou.Simulation.Bodies.IKnockoutObserver", "KnockoutChanged", "Meitou.Simulation.Bodies.KnockoutChange", nameof(Knockout)),
        ("Meitou.Simulation.Bodies.ILimbObserver", "LimbChanged", "Meitou.Simulation.Bodies.LimbChange", nameof(Limb)),
        ("Meitou.Simulation.Locks.IHeldStateObserver", "HeldStateChanged", "Meitou.Simulation.Locks.HeldStateChange", nameof(Held)),
        ("Meitou.Simulation.Factions.IRecruitObserver", "RecruitChanged", "Meitou.Simulation.Factions.RecruitChange", nameof(Recruit)),
        ("Meitou.Simulation.Bodies.ICharacterRemovedObserver", "Removed", "Meitou.Simulation.Bodies.CharacterRemoved", nameof(Removed)),
    ];

    /// <summary>The emitted systems for the notices this client has (by the handler's name), none on older clients.</summary>
    internal static IReadOnlyDictionary<string, Type> BuildWatches() => watches.Value;

    static readonly Lazy<Dictionary<string, Type>> watches = new(BuildAdapters);

    static Dictionary<string, Type> BuildAdapters()
    {
        var built = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var (iface, method, payload, handler) in hooks)
        {
            if (engine.GetType(iface) is not { } observer || engine.GetType(payload) is null) continue;
            if (handler == nameof(Limb) && (limbCharacter?.PropertyType != typeof(CharacterId) || limbPart?.PropertyType != typeof(int)
                || limbBefore?.PropertyType != typeof(LimbState) || limbAfter?.PropertyType != typeof(LimbState))) continue;
            var target = typeof(LifecycleWatch).GetMethod(handler)!;
            Type? combined = handler == nameof(Held) && KindnessReleases.Observer is { } crime
                ? ObserverEmit.BuildPair(observer, method, target, "LifecycleCustody", crime, "CrimeNoted", typeof(LifecycleWatch).GetMethod(nameof(Crime))!)
                : null;
            if ((combined ?? ObserverEmit.Build(observer, method, target, "Lifecycle" + handler)) is { } type)
                built[handler] = type;
        }
        return built;
    }

    internal static bool TracksLimbs(World world)
    {
        if (!watches.IsValueCreated || !watches.Value.TryGetValue(nameof(Limb), out var adapter)) return false;
        foreach (var system in world.Systems)
            if (system.GetType() == adapter) return true;
        return false;
    }

    internal static void Register(ModContext c)
    {
        var built = BuildWatches();
        foreach (var (handler, type) in built)
            c.AddSystem("lw-lifecycle-" + handler.ToLowerInvariant(), _ => (ITickSystem)Activator.CreateInstance(type)!, SystemAnchor.End);
        c.Log(built.Count == 0 ? "lifecycle notices: this Meitou has none (nightlies after 2026-11-24 do)"
            : $"lifecycle notices: {string.Join(", ", built.Keys).ToLowerInvariant()}");
    }

    static T? Read<T>(object o, string property) => o.GetType().GetProperty(property)?.GetValue(o) is T v ? v : default;

    static CharacterCold? Cold(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int s) ? world.Characters.Cold(s) : null;

    static string NameOf(World world, CharacterId id, CharacterCold c) =>
        c.IsPlayer && LwDeeds.For(world) is { } deeds ? deeds.Who(c) : c.Name;

    static bool Squad(World world, CharacterCold c) => c is { IsPlayer: true, Animal: null } && !OtherModsPeople.Runs(world, c);

    static void Log(string line, bool rare)
    {
        if (rare ? LwMod.Settings.LogChanges || LwMod.Settings.PlaytestDiagnostics : LwMod.Settings.PlaytestDiagnostics) LwMod.Log("lifecycle: " + line);
    }

    /// <summary>Tracks brief knockouts of nearby foes and logs the squad's own transitions.</summary>
    public static void Knockout(World world, object change)
    {
        try
        {
            if (knockedCharacter?.GetValue(change) is not CharacterId id || Cold(world, id) is not { } c) return;
            bool down = unconscious?.GetValue(change) is true;
            if (!c.IsPlayer)
            {
                LwDeeds.For(world)?.Tracker?.FoeKnockout(id, down && coma?.GetValue(change) is false && koTimer?.GetValue(change) is float and > 0f);
                return;
            }
            if (!LwMod.Settings.PlaytestDiagnostics || !Squad(world, c)) return;
            Log(down ? $"{NameOf(world, id, c)} knocked out{(coma?.GetValue(change) is true ? " (in a coma)" : $" for {koTimer?.GetValue(change):0} s")}"
                : $"{NameOf(world, id, c)} came round", rare: false);
        }
        catch (Exception e) { LwMod.Log($"lifecycle: knockout notice: {e.GetBaseException().Message}"); }
    }

    /// <summary>A limb lost or replaced (the squad's own, and named characters').</summary>
    public static void Limb(World world, object change)
    {
        try
        {
            if (limbCharacter?.GetValue(change) is not CharacterId id || Cold(world, id) is not { } c) return;
            if (limbPart?.GetValue(change) is int part && c.Medical is { Dead: false } med && part >= 0 && part < med.Parts.Count &&
                limbBefore?.GetValue(change) is LimbState.Original or LimbState.Crushed &&
                limbAfter?.GetValue(change) is LimbState.Stump)
            {
                string? kind = med.Parts[part].Template.Type switch
                {
                    BodyPartType.Arm => "arm",
                    BodyPartType.Leg => "leg",
                    _ => null,
                };
                if (kind is not null)
                {
                    if (Squad(world, c)) LwDeeds.For(world)?.Tracker?.LimbLost(world, id, c, kind);
                    else if (!c.IsPlayer) RivalsSystem.For(world)?.LimbObserved(world, id, kind);
                }
            }
            if (!LwMod.Settings.PlaytestDiagnostics || !Squad(world, c) && world.Uniques.RecordOf(id) is null) return;
            string name = Read<string>(change, "PartName") ?? "a limb";
            string after = limbAfter?.GetValue(change)?.ToString() ?? "";
            Log($"{NameOf(world, id, c)}: {name} {after.ToLowerInvariant()}", rare: false);
        }
        catch (Exception e) { LwMod.Log($"lifecycle: limb notice: {e.GetBaseException().Message}"); }
    }

    /// <summary>Shares the native crime notice between the independent release and eyewitness records.</summary>
    public static void Crime(World world, object crime)
    {
        KindnessReleases.Crime(world, crime);
        WitnessMemories.Crime(world, crime);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, Dictionary<CharacterId, (long Tick, int Run, int Skipped)>> heldSeen = new();

    /// <summary>A cage, bed or restraint entered or left: logged for cages (captives: Meitou#1271), with the fixture.</summary>
    public static void Held(World world, object change)
    {
        try
        {
            if (!LwMod.Settings.LogChanges && !LwMod.Settings.PlaytestDiagnostics && !KindnessReleases.Enabled) return;
            var before = Read<HeldState>(change, "Before");
            var after = Read<HeldState>(change, "After");
            if (before != HeldState.Imprisoned && after != HeldState.Imprisoned) return;
            var id = Read<CharacterId>(change, "Character");
            if (Cold(world, id) is not { } c) return;
            string? fixtureId = Read<string>(change, "Fixture");
            if (before == HeldState.Imprisoned && after == HeldState.None && fixtureId is not null)
                KindnessReleases.Released(world, id, c, fixtureId);
            if (!LwMod.Settings.LogChanges && !LwMod.Settings.PlaytestDiagnostics) return;
            // seen in play (bv, 0.24): prisoners in Rebirth's cages reported leaving and re-entering them nearly every tick (2,776 lines,
            // 516 for one man). Two changes within 600 ticks log as ever (in, then out); more in that run are counted, and the count goes on
            // the next line logged once the run ends.
            var flips = heldSeen.GetOrCreateValue(world);
            int skipped = 0;
            if (flips.TryGetValue(id, out var last) && world.Tick - last.Tick < 600)
            {
                if (last.Run >= 2) { flips[id] = (world.Tick, last.Run + 1, last.Skipped + 1); return; }
                flips[id] = (world.Tick, last.Run + 1, last.Skipped);
            }
            else
            {
                skipped = last.Skipped;
                flips[id] = (world.Tick, 1, 0);
            }
            var unique = world.Uniques.RecordOf(id);
            string fixture = fixtureId ?? "no fixture";
            string detail = $"{NameOf(world, id, c)}{(unique is null ? "" : " (named)")}: {before} to {after} at {fixture}" +
                (skipped > 0 ? $" (after {skipped} quick changes not logged)" : "");
            if (PlaytestLog.Enabled) PlaytestLog.Event(world, "custody", unique ?? id.ToString(), "held state changed", detail);
            else Log(detail, rare: true);
        }
        catch (Exception e) { LwMod.Log($"lifecycle: held notice: {e.GetBaseException().Message}"); }
    }

    /// <summary>Someone joined or left the player's faction: a joiner is first seen by the squad record now ("served N days").</summary>
    public static void Recruit(World world, object change)
    {
        try
        {
            var id = Read<CharacterId>(change, "Character");
            if (Cold(world, id) is not { Animal: null } c) return;
            bool joined = Read<bool>(change, "Joined");
            if (joined && LwDeeds.For(world) is { } deeds) deeds.Record.Seen(deeds.Who(c), world.GameHours);
            Log($"{NameOf(world, id, c)} {(joined ? "joined the squad" : "left the squad")}", rare: true);
        }
        catch (Exception e) { LwMod.Log($"lifecycle: recruit notice: {e.GetBaseException().Message}"); }
    }

    /// <summary>Drop live handles as a character leaves the table, while retaining records keyed by lasting identity.</summary>
    public static void Removed(World world, object removed)
    {
        try
        {
            if (removedCharacter?.GetValue(removed) is not CharacterId id || id.IsNone) return;
            LwDeeds.For(world)?.Tracker?.ForgetCharacter(id);
            PanelLines.Forget(world, id);
            RivalsSystem.For(world)?.ForgetCharacter(id);

            if (!LwMod.Settings.PlaytestDiagnostics || Read<string>(removed, "Record") is not { Length: > 0 }) return;
            if (world.Uniques.RecordOf(id) is null || Cold(world, id) is not { } c) return;
            Log($"{c.Name} left the world{(Read<bool>(removed, "Dead") ? " (dead)" : "")}", rare: false);
        }
        catch (Exception e) { LwMod.Log($"lifecycle: removal notice: {e.GetBaseException().Message}"); }
    }
}
