// Lookout calls (Living Wasteland only; the "Lookout calls" setting; lines: content/lookout.json, said on EV_SOUND_THE_ALARM, an event
// nothing else uses and Meitou never fires itself).
//
// Every 3 seconds: a hostile person (a faction at -50 or worse with the player's, standing, loaded) within 150 m of a squad member who is
// not fighting makes the nearest free squad member call it out; the line is chosen by the target's faction ("Dust Bandits on the ridge!"),
// and a squadmate may answer. Each enemy squad is called once per 6 game hours, never while the squad fights, and at most one call every
// 2 minutes of play, so it stays a warning and never chatter.
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public sealed class Lookout
{
    public const int AlarmEvent = 6;                // EV_SOUND_THE_ALARM
    const float Range = 1500;                        // world units: 150 m
    const double SquadAgain = 6;                     // game hours before the same enemy squad is called again
    const long Quiet = 30 * 120;                     // ticks (2 minutes) between two calls

    readonly Dictionary<int, double> called = [];    // enemy squad id -> game hours it was called
    long? lastCall;

    public void Look(World world, Meitou.Simulation.Dialogue.DialogueSystem? talk, PopulationData? data)
    {
        if (talk is null || data is null || world.Player.Faction < 0 ||
            (lastCall is long last && world.Tick >= last && world.Tick - last < Quiet)) return;
        var table = world.Characters;
        var hot = table.Previous;
        var squad = new List<(CharacterId Id, Vector3 At)>();
        for (int s = 0; s < hot.Length; s++)
        {
            if (!hot[s].Alive || table.Cold(s) is not { IsPlayer: true, Animal: null } c || c.Medical is { Dead: true } or { Incapacitated: true }) continue;
            if (c.InCombat) return;                                   // a fight is on: no calls
            var caller = table.IdOf(s);
            if (!Sleep.Is(world, caller)) squad.Add((caller, hot[s].Position));
        }
        if (squad.Count == 0) return;

        double now = world.GameHours;
        (CharacterId Enemy, int Squad, CharacterId Caller, float D)? best = null;
        for (int s = 0; s < hot.Length; s++)
        {
            if (!hot[s].Alive || table.Cold(s) is not { IsPlayer: false } c || c.Medical is { Dead: true } or { Incapacitated: true }) continue;
            if (c.Faction < 0 || c.Faction == world.Player.Faction || data.Relations.Get(world.Player.Faction, c.Faction) > -50) continue;
            if (called.TryGetValue(c.SquadId, out double when) && now - when < SquadAgain && now >= when) continue;
            var at = hot[s].Position;
            foreach (var (id, p) in squad)
            {
                float d = Vector3.Distance(p, at);
                if (!(d <= Range && (best is null || d < best.Value.D))) continue;
                if (BathhouseGuests.Contains(world, c.SquadId)) break;
                best = (table.IdOf(s), c.SquadId, id, d);
            }
        }
        if (best is not { } b) return;
        called[b.Squad] = now;
        lastCall = world.Tick;
        bool said = TalkPacer.Exempt(() => talk.FireEvent(world, b.Caller, b.Enemy, AlarmEvent));
        if (said) StorySounds.Lookout(world);
        if (LwMod.Settings.LogChanges) LwMod.Log($"lookout: {(said ? "called out" : "saw, said nothing about")} squad {b.Squad} at {b.D / 10:0} m");
        if (called.Count > 200)
            foreach (var k in called.Where(p => now - p.Value > SquadAgain).Select(p => p.Key).ToList()) called.Remove(k);
    }
}
