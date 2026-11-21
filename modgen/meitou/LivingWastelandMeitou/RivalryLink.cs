// Rivalry Core's party caps (Rivalry Core 0.6+, docs/rivalry-core.md "Party caps"; RivalryCore #24): at most one hostile party is
// out against the player at once, whichever mod sends it. A mod that runs its own parties counts them with IRivalryState.ClaimParty and
// ReleaseParty. When Rivalry Core runs in the world, our bounty crews, outlaw reprisals and hunting rivals claim the slot before they set out
// (refused: nothing is sent, asked again at the next pass), renew it every pass while they are out, and release it when they end. Without
// Rivalry Core nothing changes.
//
// Found by reflection, by type and member names (like RecapLink), so this mod needs no reference to Rivalry Core and loads the same with or
// without it. Anything that does not match switches the link off with one log line, and we then send as before.
using System.Collections;
using System.Reflection;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class RivalryLink
{
    /// <summary>The claim source Rivalry Core shows for our parties (F10 "Rivalry: parties").</summary>
    public const string Source = "LivingWastelandMeitou.hunters";

    /// <summary>A claim lapses this long after it was made or renewed: passes renew it every few game seconds, so it only matters when this
    /// mod stops (a crash, or the mod removed), freeing the slot for others.</summary>
    public const double ClaimHours = 6;

    /// <summary>How to call one Rivalry Core build's state (its interface's members, found by name). One per state type: in a game there is
    /// one; the tests meet a stand-in and the real build in one run.</summary>
    sealed record Binding(MethodInfo Claim, MethodInfo Release, PropertyInfo? Parties, ConstructorInfo NewClaim, object Hostile);

    static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Binding?> bindings = new();
    static readonly HashSet<string> refusedLogged = new(StringComparer.Ordinal);

    /// <summary>Rivalry Core's state in this world and how to call it, or null: not installed, not running in this world, or the link to
    /// that build is off.</summary>
    static (object State, Binding B)? State(World world)
    {
        foreach (var s in world.Systems)
        {
            var type = s.GetType();
            if (type.GetInterface("Rivalry.IRivalryState") is not { } api) continue;
            return bindings.GetOrAdd(type, _ => Bind(api)) is { } b ? (s, b) : null;
        }
        return null;
    }

    static Binding? Bind(Type api)
    {
        var claim = api.GetMethod("ClaimParty");
        var release = api.GetMethod("ReleaseParty", [typeof(string), typeof(string)]);
        var claimType = claim?.GetParameters() is [{ } p] ? p.ParameterType : null;
        var newClaim = claimType?.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 5);
        var kindType = newClaim?.GetParameters()[1].ParameterType;
        if (claim is null || release is null || newClaim is null || kindType is not { IsEnum: true } || !Enum.IsDefined(kindType, "Hostile")
            || newClaim.GetParameters() is not [{ ParameterType: var a }, _, { ParameterType: var k }, { ParameterType: var h }, { ParameterType: var f }]
            || a != typeof(string) || k != typeof(string) || h != typeof(double) || f != typeof(int))
        {
            RecordHost.Log("Rivalry Core link: Rivalry Core is running but its ClaimParty does not look as expected; our hunters are sent as before");
            return null;
        }
        RecordHost.Log($"Rivalry Core {api.Assembly.GetName().Version} found: our hunters count under its one-hostile-party cap");
        return new Binding(claim, release, api.GetProperty("Parties"), newClaim, Enum.Parse(kindType, "Hostile"));
    }

    static void Off(object state, string why)
    {
        bindings[state.GetType()] = null;
        RecordHost.Log($"Rivalry Core link: {why}; our hunters are sent as before");
    }

    static object NewClaim(Binding b, string key) => b.NewClaim.Invoke([Source, b.Hostile, key, ClaimHours, -1]);

    /// <summary>
    /// Asks for the hostile slot before a party sets out (or renews a claim we hold): true when it may go, always true without Rivalry Core.
    /// False: another party is out, send nothing now.
    /// </summary>
    public static bool MaySend(World world, string key)
    {
        if (State(world) is not var (state, b)) return true;
        try
        {
            bool held = b.Claim.Invoke(state, [NewClaim(b, key)]) is true;
            PlaytestLog.Change(world, "hostile slot", key, held ? "claimed" : "held by another party");
            if (!held && refusedLogged.Add(key))
                RecordHost.Log($"Rivalry Core: {key} waits, another hostile party is out against the player");
            if (held) refusedLogged.Remove(key);
            return held;
        }
        catch (Exception e)
        {
            Off(state, $"calling Rivalry Core failed ({e.GetBaseException().Message})");
            return true;
        }
    }

    /// <summary>
    /// Every pass, for one kind of our parties (keys starting with <paramref name="prefix"/>): renews the claims of those still out and
    /// releases ours that have ended. Renewing never stops a party already out, even when its claim had lapsed and the slot is taken.
    /// </summary>
    public static void Keep(World world, string prefix, IReadOnlyCollection<string> outNow)
    {
        if (State(world) is not var (state, b)) return;
        try
        {
            foreach (var key in outNow) b.Claim.Invoke(state, [NewClaim(b, key)]);
            foreach (var key in PartyClaims.ToRelease(Ours(state, b), prefix, outNow))
            {
                b.Release.Invoke(state, [Source, key]);
                PlaytestLog.Change(world, "hostile slot", key, "release requested");
            }
        }
        catch (Exception e)
        {
            Off(state, $"calling Rivalry Core failed ({e.GetBaseException().Message})");
        }
    }

    /// <summary>The keys of the claims Rivalry Core holds for us (its Parties copy: Source, What "claim", Key).</summary>
    static List<string> Ours(object state, Binding b)
    {
        var keys = new List<string>();
        if (b.Parties?.GetValue(state) is not IEnumerable list) return keys;
        foreach (var v in list)
        {
            var t = v.GetType();
            if (t.GetProperty("Source")?.GetValue(v) as string == Source && t.GetProperty("What")?.GetValue(v) as string == "claim"
                && t.GetProperty("Key")?.GetValue(v) is string key)
                keys.Add(key);
        }
        return keys;
    }
}
