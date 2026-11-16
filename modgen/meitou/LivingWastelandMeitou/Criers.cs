// Town criers (Living Wasteland only; the "Town criers" setting; lines: content/criers.json and the crier copies of every news line,
// content.py). While the squad is in a town that is not hostile to it, about every six minutes of play (per town) the helper picks a
// local of the town's own faction 8 to 60 m from the nearest squad member (on foot, not fighting, not a slave or prisoner, not one of the
// squad) and fires EV_I_SEE_PLAYER_NICE_BUILDING on them: the crier calls out real news ("Hear the news! ... the Holy Nation marches on
// Stack"), else the faction's own proclamation. Nobody new is spawned: the crier is a townsperson already walking the streets.
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public static class Criers
{
    public const int CrierEvent = 72;               // EV_I_SEE_PLAYER_NICE_BUILDING: neither vanilla data nor Meitou uses it
    const float Near = 80, Far = 600;               // world units (8 to 60 m)
    const long Every = 30 * 360;                    // ticks (6 minutes) between two calls in one town
    static readonly Dictionary<int, long> lastCall = [];
    static World? seen;

    public static void Look(World world, Meitou.Simulation.Dialogue.DialogueSystem? talk, PopulationData? data)
    {
        if (talk is null || data is null || world.Player.Faction < 0) return;
        if (!ReferenceEquals(seen, world)) { seen = world; lastCall.Clear(); }
        var table = world.Characters;
        var hot = table.Previous;

        // the squad: where it stands, and which town that is
        var squad = new List<(CharacterId Id, Vector3 At)>();
        for (int s = 0; s < hot.Length; s++)
        {
            if (!hot[s].Alive || table.Cold(s) is not { IsPlayer: true, Animal: null } c || c.Medical is { Incapacitated: true }) continue;
            if (c.InCombat) return;
            squad.Add((table.IdOf(s), hot[s].Position));
        }
        if (squad.Count == 0) return;
        int town = -1;
        foreach (var (_, at) in squad) if (data.TownAt(at) is >= 0 and var t) { town = t; break; }
        if (town < 0 || town >= data.SiteFactions.Count) return;
        int owner = data.SiteFactions[town];
        if (owner < 0 || owner == world.Player.Faction || data.Relations.Get(world.Player.Faction, owner) <= -30) return;
        if (lastCall.TryGetValue(town, out long last) && world.Tick - last < Every) return;

        // the crier: a local of the owner's faction, the nearest one in reach
        (CharacterId Crier, CharacterId Listener, float D)? best = null;
        for (int s = 0; s < hot.Length; s++)
        {
            if (!hot[s].Alive || table.Cold(s) is not { IsPlayer: false, Animal: null } c || c.Faction != owner) continue;
            if (c.InCombat || c.IsImprisoned || !c.CarriedBy.IsNone || c.Medical is { Incapacitated: true }) continue;
            if (Sleep.Is(world, table.IdOf(s))) continue;      // asleep (Meitou 0.19.1 lets no sleeper talk): pick someone awake
            var at = hot[s].Position;
            foreach (var (id, p) in squad)
            {
                float d = Vector3.Distance(p, at);
                if (d >= Near && d <= Far && (best is null || d < best.Value.D)) best = (table.IdOf(s), id, d);
            }
        }
        if (best is not { } b) return;
        lastCall[town] = world.Tick;
        bool said = TalkPacer.Exempt(() => talk.FireEvent(world, b.Crier, b.Listener, CrierEvent));
        if (LwMod.Settings.LogChanges) LwMod.Log($"crier: {(said ? "called out" : "had nothing to say")} in {data.Sites[town].Town.Name}");
    }
}
