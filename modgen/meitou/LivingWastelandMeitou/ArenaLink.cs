// Proving Grounds for Meitou (BeatTheCount's arena port, 0.2.0 and later): its public ProvingGrounds.ProvingGroundsApi, read by reflection
// so this mod loads the same with it or without it. InBout(id) is true while someone is in one of the arena's bouts or being walked to or
// from one (and for 30 s of game time after); a blow between two characters both InBout is sport, not a crime: no town heat, no rival, no
// outlaw struck, no fight for the record's fight count. IsTownFighter(name): Scratch's arena fighters, never made rivals or renamed (Proving
// Grounds also marks them through our LeaveAlone). Its calls are safe from any thread, never throw, and answer false before a game loads.
// Looked up on first use, not at load (mods load in any order); looked for again only when another assembly has loaded since.
using System.Reflection;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class ArenaLink
{
    public const string AssemblyName = "ProvingGroundsMeitou", TypeName = "ProvingGrounds.ProvingGroundsApi";

    static readonly object gate = new();
    static int assembliesSeen = -1;
    static bool off, told;
    static Func<CharacterId, bool>? inBout;
    static Func<string, bool>? townFighter;

    /// <summary>For tests: binds stand-ins (or nothing) instead of looking the arena up.</summary>
    internal static void Bind(Func<CharacterId, bool>? bout, Func<string, bool>? fighter)
    {
        lock (gate) { inBout = bout; townFighter = fighter; off = false; assembliesSeen = int.MaxValue; }
    }

    /// <summary>For tests: forgets the binding, so the next call looks the arena up again.</summary>
    internal static void Reset()
    {
        lock (gate) { inBout = null; townFighter = null; off = false; told = false; assembliesSeen = -1; }
    }

    /// <summary>Whether a blow from <paramref name="striker"/> on <paramref name="victim"/> is part of an arena bout.</summary>
    public static bool Sport(CharacterId striker, CharacterId victim)
    {
        if (striker.IsNone || victim.IsNone || !Find() || inBout is not { } f) return false;
        try { return f(striker) && f(victim); }
        catch (Exception e) { Off(e); return false; }
    }

    /// <summary>Whether a character is in a bout (or just out of one).</summary>
    public static bool InBout(CharacterId id)
    {
        if (id.IsNone || !Find() || inBout is not { } f) return false;
        try { return f(id); }
        catch (Exception e) { Off(e); return false; }
    }

    /// <summary>Whether a name is one of Scratch's arena fighters.</summary>
    public static bool IsTownFighter(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !Find() || townFighter is not { } f) return false;
        try { return f(name); }
        catch (Exception e) { Off(e); return false; }
    }

    static bool Find()
    {
        if (inBout is not null) return !off;
        if (off) return false;
        var all = AppDomain.CurrentDomain.GetAssemblies();
        if (all.Length == Volatile.Read(ref assembliesSeen)) return false;
        lock (gate)
        {
            if (inBout is not null || off) return !off && inBout is not null;
            assembliesSeen = all.Length;
            var api = all.FirstOrDefault(a => a.GetName().Name == AssemblyName)?.GetType(TypeName, false);
            if (api is null) return false;
            try
            {
                int version = api.GetField("Version", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is int v ? v : 0;
                var bout = api.GetMethod("InBout", BindingFlags.Public | BindingFlags.Static, null, [typeof(CharacterId)], null);
                var fighter = api.GetMethod("IsTownFighter", BindingFlags.Public | BindingFlags.Static, null, [typeof(string)], null);
                if (version < 1 || bout?.ReturnType != typeof(bool))
                {
                    off = true;
                    RecordHost.Log($"Proving Grounds: its API (version {version}) has no InBout we can call; arena blows count as any other");
                    return false;
                }
                townFighter = fighter?.ReturnType == typeof(bool) ? fighter.CreateDelegate<Func<string, bool>>() : null;
                inBout = bout.CreateDelegate<Func<CharacterId, bool>>();
                if (!told) { told = true; RecordHost.Log($"Proving Grounds: API version {version}; blows in its bouts are sport (no heat, rivals or outlaw strikes)"); }
                return true;
            }
            catch (Exception e)
            {
                off = true;
                RecordHost.Log($"Proving Grounds: its API could not be read ({e.GetBaseException().Message}); arena blows count as any other");
                return false;
            }
        }
    }

    static void Off(Exception e)
    {
        lock (gate) { if (off) return; off = true; }
        RecordHost.Log($"Proving Grounds: InBout threw ({e.GetBaseException().Message}); the link is off");
    }
}
