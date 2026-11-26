// Why a squad this mod sent left the world (Meitou 0.22.2+, IPlatoonObserver's Removed notice): beaten in a fight far from the squad
// (LostFight, by another platoon: never the player's, whose fights are loaded), emptied (NoMembers), or taken out by the engine for its own
// reasons (Despawned, Dropped, Other). Before it, every bounty crew that vanished counted as beaten by the squad: "The bounty hunters
// Squin hired are finished" for a crew the engine had despawned, or one bandits killed on the road. Kept per world by the mod's tag on the
// platoon (Pursuit.ReadTag), and taken once. Built by reflection (ObserverEmit) so the 0.17.3 build still loads; older clients have no
// notice and keep the old rule.
using System.Reflection;
using System.Runtime.CompilerServices;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace LivingWasteland.MeitouMod;

public static class PlatoonFates
{
    static readonly Type? observer = typeof(World).Assembly.GetType("Meitou.Simulation.Factions.IPlatoonObserver");
    static readonly Type? payload = typeof(World).Assembly.GetType("Meitou.Simulation.Factions.PlatoonNotice");
    static readonly PropertyInfo? kindOf = payload?.GetProperty("Kind"), platoonOf = payload?.GetProperty("Platoon"), goneOf = payload?.GetProperty("Gone"), byOf = payload?.GetProperty("By");
    static readonly ConditionalWeakTable<World, Dictionary<string, string>> fates = new();

    /// <summary>Whether this client tells why a platoon left the world.</summary>
    internal static bool Available => observer is not null && kindOf is not null && goneOf is not null && platoonOf?.PropertyType == typeof(Platoon);

    /// <summary>The emitted observer system's type, or null on a client without the notice.</summary>
    internal static Type? BuildWatch() => watch.Value;

    // Both hooks use the same registered system. A second idle system still costs work in Meitou's tick driver.
    static readonly Lazy<Type?> watch = new(() => Available
        ? ObserverEmit.Build(observer!, "PlatoonChanged", typeof(PlatoonFates).GetMethod(nameof(Changed))!, "SquadFateWatch", PlatoonRetention.Hook)
        : null);

    internal static void Register(ModContext c)
    {
        if (BuildWatch() is not { } type)
        {
            c.Log("platoon notices: this Meitou has none (0.22.2 and later do); a crew that vanishes counts as beaten");
            return;
        }
        c.AddSystem("lw-platoon-fates", _ => (ITickSystem)Activator.CreateInstance(type)!, SystemAnchor.End);
        c.Log("platoon notices: a vanished crew is told apart from a beaten one");
    }

    /// <summary>One platoon change: a removed platoon carrying this mod's tag has its reason kept.</summary>
    public static void Changed(World world, object notice)
    {
        try
        {
            if (kindOf!.GetValue(notice)?.ToString() is not { } kind || platoonOf!.GetValue(notice) is not Platoon platoon) return;
            NewsSystem.For(world)?.BathhouseParty(world, platoon, kind);
            if (kind != "Removed") return;
            if (Pursuit.ReadTag(platoon) is not { } tag || !Pursuit.IsTwrTag(tag)) return;
            string gone = goneOf!.GetValue(notice)?.ToString() ?? "Other";
            if (tag!.StartsWith("lw:rival:", StringComparison.Ordinal) && RivalsSystem.For(world) is { } rivals &&
                rivals.PartyRemoved(world, platoon, tag, gone, byOf?.GetValue(notice) is int winner ? winner : -1)) return;
            if (KilledByOthers(gone))
            {
                if (tag.StartsWith("lw:raid:", StringComparison.Ordinal) && RaidsSystem.For(world)?.PartyWipedOut(world, platoon, tag) == true) return;
                if (tag.StartsWith("lw:reprisal:", StringComparison.Ordinal) && OutlawCampsSystem.For(world)?.ReprisalWipedOut(world, platoon, tag) == true) return;
            }
            lock (fates) fates.GetOrCreateValue(world)[tag] = gone;
            LwMod.Log($"platoon notices: {tag} left the world ({gone})");
        }
        catch (Exception e) { LwMod.Log($"platoon notices: {e.GetBaseException().Message}"); }
    }

    /// <summary>Why the platoon tagged <paramref name="tag"/> left this world, once (null: not told, or an older client).</summary>
    internal static string? Take(World world, string tag)
    {
        lock (fates)
            return fates.TryGetValue(world, out var seen) && seen.Remove(tag, out var gone) ? gone : null;
    }

    /// <summary>The engine took it out for its own reasons: not beaten, so it may be placed again.</summary>
    internal static bool NotBeaten(string gone) => gone is "Despawned" or "Dropped" or "Other";

    /// <summary>Beaten in a fight far from the squad, by someone else.</summary>
    internal static bool KilledByOthers(string gone) => gone == "LostFight";

    /// <summary>For tests: tell a fate as the engine would.</summary>
    internal static void Tell(World world, string tag, string gone)
    {
        lock (fates) fates.GetOrCreateValue(world)[tag] = gone;
    }
}
