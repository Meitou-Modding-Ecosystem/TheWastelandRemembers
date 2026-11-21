// Engine AI calls whose shape changed in Meitou's AI rewrite (code-mod API 3, first in the 0.22.2 nightlies of 2026-11-21). The DLL is built
// against 0.17.3 and must still bind on every client the manifest admits, so these are found by reflection once, as InventoryCompat does:
//   PlatoonJobs.For(ai, template, savedPackage)                  0.17.3 .. 0.22.2-nightly.20261119
//   PlatoonJobs.For(ai, template, factionId, restored, saved)    API 3 (the faction's AI fallback packages join the platoon's)
//   AiMind.ForcedAssault, AiSystem.OrderAssault                  gone in API 3: callers check CanAssault and do without
// A call compiled straight against the old signature fails on API 3 with MissingMethodException the first time its method runs (seen by
// RefCheck on 0.22.2-nightly.20261121.2: hunters, couriers and outlaw camps would never have got a package).
using System.Reflection;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Ai;

namespace LivingWasteland.MeitouMod;

internal static class AiCompat
{
    static readonly MethodInfo? jobsNew, jobsOld;
    static readonly PropertyInfo? forcedAssault = typeof(AiMind).GetProperty("ForcedAssault", BindingFlags.Public | BindingFlags.Instance);
    static readonly MethodInfo? orderAssault = typeof(AiSystem).GetMethod("OrderAssault", BindingFlags.Public | BindingFlags.Instance, null,
        [typeof(World), typeof(CharacterId), typeof(CharacterId)], null);
    static int told;

    static AiCompat()
    {
        foreach (var m in typeof(PlatoonJobs).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name != "For") continue;
            var p = m.GetParameters();
            if (p.Length < 2 || p[0].ParameterType != typeof(AiDatabase) || p[1].ParameterType != typeof(SquadTemplate)) continue;
            if (p.Length == 5 && p[2].ParameterType == typeof(string) && p[3].ParameterType == typeof(bool) && p[4].ParameterType == typeof(string))
                jobsNew = m;
            else if (p.Length == 3 && p[2].ParameterType == typeof(string))
                jobsOld = m;
        }
    }

    /// <summary>Which shape of PlatoonJobs.For this client has ("faction-aware", "classic" or "none"), for the load log line.</summary>
    internal static string JobsShape => jobsNew is not null ? "faction-aware" : jobsOld is not null ? "classic" : "none";

    /// <summary>The AI package and off-screen job a new platoon of <paramref name="template"/> starts with, on any client.</summary>
    internal static (AiPackage? Package, PlatoonJob Job) JobsFor(AiDatabase ai, SquadTemplate template)
    {
        try
        {
            object? r = jobsNew is not null ? jobsNew.Invoke(null, [ai, template, FactionOf(template), false, null])
                : jobsOld?.Invoke(null, [ai, template, null]);
            if (r is ValueTuple<AiPackage?, PlatoonJob> t) return t;
        }
        catch (Exception e) { Tell($"PlatoonJobs.For threw ({e.GetBaseException().Message})"); return (null, default); }
        Tell($"PlatoonJobs.For has no shape this mod knows (classic or faction-aware)");
        return (null, default);
    }

    static string? FactionOf(SquadTemplate template) =>
        typeof(SquadTemplate).GetProperty("Faction")?.GetValue(template) as string;

    /// <summary>Whether this client still has the per-character assault order (gone in API 3).</summary>
    internal static bool CanAssault => forcedAssault is not null && orderAssault is not null;

    /// <summary>The character a mind has been told to assault, or none (always none on API 3).</summary>
    internal static CharacterId ForcedAssault(AiMind mind) =>
        forcedAssault?.GetValue(mind) is CharacterId id ? id : CharacterId.None;

    internal static void ClearForcedAssault(AiMind mind)
    {
        if (forcedAssault?.CanWrite == true) forcedAssault.SetValue(mind, CharacterId.None);
    }

    /// <summary>Orders one character to attack another (vanilla dialogue's Assault); false where the order is gone or refused.</summary>
    internal static bool OrderAssault(AiSystem ai, World world, CharacterId who, CharacterId target)
    {
        if (orderAssault is null) { Tell("this client has no AiSystem.OrderAssault (API 3): stalled crews and siege cover do without it"); return false; }
        try { return orderAssault.Invoke(ai, [world, who, target]) is true; }
        catch (Exception e) { Tell($"OrderAssault threw ({e.GetBaseException().Message})"); return false; }
    }

    static void Tell(string what)
    {
        if (Interlocked.Exchange(ref told, 1) == 0) RecordHost.Log($"engine AI: {what}");
    }
}
