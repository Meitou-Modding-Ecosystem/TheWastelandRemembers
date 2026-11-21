// Harassment raids (Meitou only, behind "Harassment raids (test)", off by default). Data: content/x_raids.json (gen_raids.py).
//
// A small faction (the Flotsam Ninjas, the Anti-Slavers, the rebel farmers, the Cannibal Hunters) now and then sends a party to lurk for two
// days a kilometre or so outside a town of the big one it fights. The party is a persistent stand-in made 2.5 to 3.5 km from the town where
// no squad member can see it, walked by the population system to its lurking point (Pursuit.Make), and fights whatever comes by. If it is
// gone before the two days are up it was caught; otherwise it got away and is let go. The gossip codes read that (RaidRules.State) and the
// town (1 + its place in campNews.towns, the word swap /LWRAID_x/). No towns change hands.
using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public sealed record RaidCode(string Name, string Squad, string Target, int State, int Town, string Raider = "");

public sealed class RaidsSystem(IReadOnlyList<RaidCode> raids, PopulationSystem? population, PopulationData? data) : ICombatHitObserver, IModSaveData, IStateHashed
{
    const int PassEvery = 300, SaveFormat = 3;
    readonly RaidRecord?[] last = new RaidRecord?[raids.Count];
    readonly Presence<(int Raid, double Sent)> parties = new();
    bool failed;

    /// <summary>
    /// Kenshi Ultimate Overhaul 1.1+ (its code part, KuoCode) runs the same thing in its war layer: the Flotsam, the Anti-Slavers and the
    /// rebel farmers raid and ambush instead of storming towns. With it loaded our raids stand aside (once, with a log line).
    /// </summary>
    static bool KuoWars => kuo ??= Detect();
    static bool? kuo;
    static bool Detect()
    {
        bool found = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith("KuoCode", StringComparison.Ordinal) == true);
        if (found) LwMod.Log("raids: Kenshi Ultimate Overhaul's war layer is loaded and raids and ambushes there; ours stand aside");
        return found;
    }

    // ---- the squad's part: a blow on a raider breaks the raid; a blow on the target's people near a raid party helps it ----

    readonly Dictionary<int, int> partySquads = [];     // squad id of a loaded raid party -> raid (rebuilt every pass)

    public void HitResolved(World world, in CombatHit hit)
    {
        if (failed || !LwMod.Settings.HarassmentRaids || data is null) return;
        var table = world.Characters;
        if (!table.TryResolveNext(hit.Attacker, out int a) || table.Cold(a) is not { IsPlayer: true }
            || !table.TryResolveNext(hit.Victim, out int v) || table.Cold(v) is not { } victim) return;
        if (ArenaLink.Sport(hit.Attacker, hit.Victim)) return;          // arena sport (Proving Grounds) breaks no party
        if (partySquads.TryGetValue(victim.SquadId, out int broken) && last[broken] is { Out: true } rb)
        {
            if (!rb.Broke) LwMod.Log($"raids: the squad struck the {raids[broken].Name} party");
            last[broken] = rb with { Broke = true };
            return;
        }
        string faction = victim.Faction >= 0 && victim.Faction < data.Factions.Count ? data.Factions[victim.Faction].Name : "";
        var at = table.Next[v].Position;
        for (int i = 0; i < raids.Count; i++)
        {
            if (raids[i].Target != faction || last[i] is not { Out: true, Helped: false } r) continue;
            if ((r.X - at.X) * (r.X - at.X) + (r.Z - at.Z) * (r.Z - at.Z) > RaidRules.Beside * RaidRules.Beside) continue;
            last[i] = r with { Helped = true };
            LwMod.Log($"raids: the squad fought {faction} beside the {raids[i].Name} party");
        }
    }

    /// <summary>Thanks the factions for the squad's part in a raid that just ended (once), with a history line.</summary>
    void Settle(World world, PopulationData d, int i, RaidRecord r)
    {
        var (raider, target) = RaidRules.Settle(r);
        string town = TownName(r.Town), who = raids[i].Raider.Length > 0 ? raids[i].Raider : raids[i].Name;
        if (raider > 0) Thank(world, d, raids[i].Raider, raider);
        if (target > 0) Thank(world, d, raids[i].Target, target);
        string? note = r.Broke ? $"Your squad broke a {who} raid near {town}." : r.Helped ? $"Your squad fought beside {who} raiders near {town}." : null;
        if (note is not null && LwDeeds.For(world) is { } deeds) deeds.Record.Note(note);
        if (note is not null) RecapLink.Moment(note, town, 65);
    }

    static void Thank(World world, PopulationData d, string faction, int amount)
    {
        int player = world.Player.Faction;
        if (player < 0 || player >= d.Factions.Count) return;
        var receiver = d.Factions.FirstOrDefault(f => string.Equals(f.Name, faction, StringComparison.OrdinalIgnoreCase));
        if (receiver is null || d.FactionIndex(receiver.Id) is not (>= 0 and var f) || f == player) return;
        var (before, after) = d.Relations.Add(f, player, amount);
        if (Meitou.Simulation.Factions.PlayerRelations.Notice(faction, before, after) is { } text) world.Post(text);
        LwMod.Log($"raids: {faction} {before:0} -> {after:0}");
    }

    public readonly Published Shown = new();

    public void SlowWorld(World world)
    {
        Latest = this;
        if (Published.Due(world.Tick, 16)) Shown.Set(() => Summary(null));
        byWorld.AddOrUpdate(world, this);
        if (failed || !LwMod.Settings.HarassmentRaids || world.Tick % PassEvery != 280 || population is null || data is null || KuoWars) return;
        try
        {
            Pass(world, population, data);
        }
        catch (Exception e)
        {
            failed = true;
            LwMod.Log($"raids: stopped after an error: {e.GetBaseException().Message}");
        }
    }

    void Pass(World world, PopulationSystem pop, PopulationData d)
    {
        double now = world.GameHours;
        var players = Pursuit.Players(world);
        var claimed = new HashSet<int>();
        var squads = new Dictionary<int, int>();
        for (int i = 0; i < raids.Count; i++)
        {
            var r = raids[i];
            var rec = last[i];
            if (rec is { Out: true })
            {
                Platoon? p = null;
                string tag = Tag(r, rec);
                if (Pursuit.HasTags)
                {
                    var exact = world.Platoons.All
                        .Where(candidate => candidate is not null && candidate.TemplateId == r.Squad && Pursuit.ReadTag(candidate) == tag)
                        .Take(2).ToArray();
                    if (exact.Length > 1) continue;
                    p = exact.SingleOrDefault();
                    if (p is not null && claimed.Contains(p.Id)) continue;
                }
                bool tagged = rec.Tagged || p is not null;
                bool legacy = !Pursuit.HasTags || !tagged;
                if (legacy) p = Pursuit.Find(world, r.Squad, rec.X, rec.Z, claimed, Pursuit.IsUntagged);
                bool over = now - rec.SentHours >= RaidRules.Lurk || now < rec.SentHours;
                var key = (i, rec.SentHours);
                var verdict = parties.Check(world, key, p is not null);
                bool caught = verdict == Presence<(int, double)>.Verdict.Gone || p is { Dead: true } ||
                    (legacy && p is null && Pursuit.DeadNear(world, r.Squad, rec.X, rec.Z, eligible: Pursuit.IsUntagged));
                if (p is null && !caught && !over)
                {
                    p = RestoreParty(world, pop, d, r, rec, players);
                    if (p is null) { parties.Forget(key); continue; }
                    tagged = Pursuit.Mark(p, tag);
                    parties.Check(world, key, found: true);
                    LwMod.Log($"raids: the {r.Name} party missing from the save was placed again near {TownName(rec.Town)}");
                }
                if (caught || over)
                {
                    if (p is { Dead: false }) Pursuit.Release(p);
                    last[i] = rec with { EndedHours = now, Caught = caught, Tagged = tagged };
                    parties.Forget(key);
                    Settle(world, d, i, last[i]!);
                    LwMod.Log($"raids: the {r.Name} party near {TownName(rec.Town)} {(caught ? "was caught" : "got away")}");
                }
                else
                {
                    claimed.Add(p!.Id);
                    last[i] = rec with { X = p.Position.X, Z = p.Position.Y, Tagged = tagged };
                    if (p.State == PlatoonState.Loaded && p.SquadId >= 0) squads[p.SquadId] = i;
                }
                continue;
            }
            if (now < RaidRules.NextStart(world.Seed, i, rec?.EndedHours ?? 0)) continue;

            var towns = TargetTowns(d, r.Target);
            int pick = RaidRules.PickTown(world.Seed, i, Math.Floor(now), towns.Count);
            if (pick < 0) continue;
            var (townIndex, site) = towns[pick];
            var walk = world.Walkability;
            if (HunterRules.Start(world.Seed, $"raid:{r.Name}", now, site.X, site.Z,
                    (x, z) => walk.IsWalkable(x, z) && walk.GroundHeight(x, z) - walk.TerrainHeight(x, z) < 5f && CampRules.MayMove((x, z), (x, z), players))
                is not { } at) continue;
            var lurk = (X: site.X + (at.X - site.X) * 0.4f, Z: site.Z + (at.Z - site.Z) * 0.4f);    // about 1.2 km out, on the side it came from
            var next = new RaidRecord(townIndex, now, at.X, at.Z);
            string tagAtSend = Tag(r, next);
            var spawned = Pursuit.Make(world, pop, d, r.Squad, tagAtSend, at, lurk);
            if (spawned is null) continue;
            last[i] = next with { Tagged = Pursuit.Mark(spawned, tagAtSend) };
            parties.Check(world, (i, now), found: true);
            LwMod.Log($"raids: a {r.Name} party sets out for {TownName(townIndex)}, day {now / 24:0.0}");
        }
        partySquads.Clear();
        foreach (var (k, v) in squads) partySquads[k] = v;
    }

    static string Tag(RaidCode raid, RaidRecord record) =>
        FormattableString.Invariant($"lw:raid:{raid.Name}:{record.SentHours:R}");

    static Platoon? RestoreParty(World world, PopulationSystem pop, PopulationData data, RaidCode raid, RaidRecord record,
        List<(float X, float Z)> players)
    {
        var at = (record.X, record.Z);
        if (players.Count == 0 || !CampRules.MayMove(at, at, players) ||
            !world.Walkability.IsWalkable(record.X, record.Z) ||
            world.Walkability.GroundHeight(record.X, record.Z) - world.Walkability.TerrainHeight(record.X, record.Z) >= 5f)
            return null;

        foreach (var (town, site) in TargetTowns(data, raid.Target))
        {
            if (town != record.Town) continue;
            var delta = new Vector2(record.X - site.X, record.Z - site.Z);
            float distance = delta.Length();
            var toward = distance <= 12000 ? at : (site.X + delta.X * (12000 / distance), site.Z + delta.Y * (12000 / distance));
            return Pursuit.Make(world, pop, data, raid.Squad, Tag(raid, record), at, toward);
        }
        return null;
    }

    /// <summary>The news towns held by a faction, with a position: (place in campNews.towns, its site's position).</summary>
    static List<(int Town, Vector3 At)> TargetTowns(PopulationData d, string faction)
    {
        var list = new List<(int, Vector3)>();
        var names = OutlawCampsSystem.NewsTowns;
        for (int s = 0; s < d.Sites.Count && s < d.SiteFactions.Count; s++)
        {
            int f = d.SiteFactions[s];
            if (f < 0 || f >= d.Factions.Count || d.Factions[f].Name != faction) continue;
            int t = names.IndexOf(d.Sites[s].Town.Name);
            if (t >= 0 && !list.Any(x => x.Item1 == t)) list.Add((t, d.Sites[s].Position));
        }
        return list;
    }

    static string TownName(int t) => t >= 0 && t < OutlawCampsSystem.NewsTowns.Count ? OutlawCampsSystem.NewsTowns[t] : "?";

    /// <summary>The gossip's state code for raid <paramref name="raid"/>.</summary>
    public int StateOf(World world, int raid) => raid < last.Length ? RaidRules.State(last[raid], world.GameHours) : 0;

    /// <summary>1 + the place in campNews.towns of the raid's town while it is talked about, else 0.</summary>
    public int TownOf(World world, int raid) => StateOf(world, raid) > 0 && last[raid] is { } r ? r.Town + 1 : 0;

    public string Summary(World? world) => KuoWars ? "raids stand aside: Kenshi Ultimate Overhaul runs raids and ambushes" : string.Join("; ", raids.Select((r, i) => last[i] is not { } x ? $"{r.Name}: none yet"
        : x.Out ? $"{r.Name}: out near {TownName(x.Town)}" : $"{r.Name}: last {(x.Caught ? "caught" : "got away")} near {TownName(x.Town)}"));

    static readonly ConditionalWeakTable<World, RaidsSystem> byWorld = new();
    public static RaidsSystem? For(World world) => byWorld.TryGetValue(world, out var s) ? s : null;
    internal static RaidsSystem? Latest;

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(SaveFormat);
        w.Write(raids.Count);
        for (int i = 0; i < raids.Count; i++)
        {
            w.Write(raids[i].Name);
            w.Write(last[i] is not null);
            if (last[i] is { } r) { w.Write(r.Town); w.Write(r.SentHours); w.Write(r.X); w.Write(r.Z); w.Write(r.EndedHours); w.Write(r.Caught); w.Write(r.Helped); w.Write(r.Broke); w.Write(r.Tagged); }
        }
    }

    public void Read(Stream stream)
    {
        Array.Clear(last);
        using var rd = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int format = stream.Position >= stream.Length ? 0 : rd.ReadInt32();
        if (format is < 1 or > SaveFormat) return;
        for (int n = rd.ReadInt32(); n > 0; n--)
        {
            string name = rd.ReadString();
            RaidRecord? r = rd.ReadBoolean() ? new RaidRecord(rd.ReadInt32(), rd.ReadDouble(), rd.ReadSingle(), rd.ReadSingle(), rd.ReadDouble(), rd.ReadBoolean()) : null;
            if (r is not null && format >= 2) r = r with { Helped = rd.ReadBoolean(), Broke = rd.ReadBoolean() };
            if (r is not null && format >= 3) r = r with { Tagged = rd.ReadBoolean() };
            int i = raids.ToList().FindIndex(x => x.Name == name);
            if (i >= 0) last[i] = r;
        }
    }

    public void Hash(ref StateHasher h)
    {
        foreach (var r in last)
        {
            h.Add(r?.Town ?? -1);
            if (r is null) continue;
            h.Add(r.SentHours);
            h.Add(r.X);
            h.Add(r.Z);
            h.Add(r.EndedHours);
            h.Add(r.Caught ? 1 : 0);
            h.Add(r.Helped ? 1 : 0);
            h.Add(r.Broke ? 1 : 0);
            h.Add(r.Tagged ? 1 : 0);
        }
    }
}
