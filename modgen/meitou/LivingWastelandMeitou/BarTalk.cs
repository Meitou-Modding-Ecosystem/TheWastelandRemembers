// Bar talk on Meitou. Meitou 0.17 never fires event 12 (EV_BAR_TALK), so every bar patron line in the data stays silent there. This drives it:
// on a slow cadence, for each building a player character is inside, NPCs in that same building are offered event 12 one at a time until one
// of them says something; the building then stays quiet for a while. The dialogue system does the rest as for any other event (packages,
// conditions, chance, repetition, "one at a time", bubbles), so only patrons whose packages have bar-talk lines ever speak.
//
// The pacing is kept apart from the engine (BarPacer) so it can be tested without a world.
namespace LivingWasteland.MeitouMod;

/// <summary>Cooldowns for the bar-talk driver: a pause per building after a line, and a longer one per speaker.</summary>
public sealed class BarPacer<TNpc> where TNpc : notnull
{
    readonly Dictionary<string, long> buildingUntil = [];
    readonly Dictionary<TNpc, long> npcUntil = [];

    public bool BuildingReady(string building, long tick) => !buildingUntil.TryGetValue(building, out long t) || tick >= t;

    public bool NpcReady(TNpc npc, long tick) => !npcUntil.TryGetValue(npc, out long t) || tick >= t;

    /// <summary>A line was said: the building waits <paramref name="buildingPause"/> ticks, the speaker <paramref name="npcPause"/>.</summary>
    public void Said(string building, TNpc npc, long tick, long buildingPause, long npcPause)
    {
        buildingUntil[building] = tick + buildingPause;
        npcUntil[npc] = tick + npcPause;
    }

    /// <summary>Nobody wanted to talk: try this building again a little later rather than on every pass.</summary>
    public void Nothing(string building, long tick, long retry) => buildingUntil[building] = tick + retry;

    /// <summary>Drops cooldowns that have run out (keeps the tables small over a long game).</summary>
    public void Prune(long tick)
    {
        foreach (var k in buildingUntil.Where(p => p.Value <= tick).Select(p => p.Key).ToList()) buildingUntil.Remove(k);
        foreach (var k in npcUntil.Where(p => p.Value <= tick).Select(p => p.Key).ToList()) npcUntil.Remove(k);
    }

    public int Tracked => buildingUntil.Count + npcUntil.Count;

    /// <summary>
    /// The order to offer the candidates in: a rotation that moves on every pass, so the same patron does not always get the first try. Stable
    /// for the same tick (deterministic, no random source).
    /// </summary>
    public static List<T> Order<T>(IReadOnlyList<T> candidates, long tick, int salt)
    {
        var list = new List<T>(candidates.Count);
        if (candidates.Count == 0) return list;
        int start = (int)((ulong)(tick / 30 + (uint)salt) % (ulong)candidates.Count);
        for (int i = 0; i < candidates.Count; i++) list.Add(candidates[(start + i) % candidates.Count]);
        return list;
    }
}
