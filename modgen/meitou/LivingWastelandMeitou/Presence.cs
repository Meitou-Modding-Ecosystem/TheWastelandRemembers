using System.Collections.Generic;

namespace LivingWasteland.MeitouMod;

/// <summary>
/// Whether a squad we sent (bounty crew, outlaw reprisal) that is missing from the world was beaten or was simply not saved. Meitou's save
/// keeps only squads that have loaded once (or campaign squads), so one that never came near the squad is missing after a load: place it
/// again, once. For a party seen this session, callers can give Meitou's removal notice one pass to arrive before falling back to the old rule.
/// Learnt in play: a crew killed and then re-placed came back again and again; and the record of what was seen must start again with each
/// loaded game (a game loaded in the same run must not count earlier sightings), so it is kept per world.
/// No engine types: unit tested.
/// </summary>
public sealed class Presence<TKey> where TKey : notnull
{
    public enum Verdict { Here, PlaceAgain, Gone, AwaitNotice }

    readonly Dictionary<TKey, bool> seen = [];
    object? session;

    public Verdict Check(object world, TKey key, bool found) => Check(world, key, found, waitForNotice: false);

    internal Verdict Check(object world, TKey key, bool found, bool waitForNotice)
    {
        if (!ReferenceEquals(world, session)) { seen.Clear(); session = world; }
        if (found) { seen[key] = true; return Verdict.Here; }
        if (seen.TryAdd(key, false)) return Verdict.PlaceAgain;
        if (!waitForNotice || !seen[key]) return Verdict.Gone;
        seen[key] = false;
        return Verdict.AwaitNotice;
    }

    /// <summary>Placing it again failed (no ground to stand on yet): ask again next time instead of counting it beaten.</summary>
    public void Forget(TKey key) => seen.Remove(key);

    public bool Seen(TKey key) => seen.ContainsKey(key);
}
