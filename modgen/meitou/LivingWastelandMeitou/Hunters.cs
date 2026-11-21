// Bounty hunters (Meitou only, setting "Towns hire bounty hunters", on by default since build ai). Data: content/bounty_hunters.json (the "Bounty
// Crews" faction, hostile to the player only, and its "Bounty crew" squad).
//
// While a town has the squad marked as hunted (town heat 24+, Heat.cs), it hires one crew at most every three days. The crew is a
// persistent stand-in made 2.5 to 3.5 km from the nearest of the player's people, out of everyone's sight, with the engine's travel job
// (a short-range patrol job: see Pursuit.Steer) and the player's position as its point, so the population system walks it along the roads (StandIn). Every
// pass the point follows the player; once the crew is loaded its people are walked at the nearest player character (TaskStart.Walk, as
// Raid Commander does) and, being hostile, fight on sight. After three days, or when it is dead, the crew is let go (no longer persistent).
// Platoon ids change on load. New crews use a saved party tag where supported; old saves fall back to template and last position.
using System.Numerics;
using System.Reflection;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Ai;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public sealed class HuntersSystem(string crewSquad, PopulationSystem? population, PopulationData? data) : ITickSystem, IModSaveData, IStateHashed
{
    const int PassEvery = 300;
    const int Format = 1;

    readonly List<Crew> crews = [];
    public readonly Published Shown = new();                // the F10 row's line
    bool placeNow;                                          // a save just loaded (WorldReady): pass at the next tick the squad is seen

    /// <summary>The bounty crews on the hunt (WastelandApi).</summary>
    public IReadOnlyList<Crew> Crews => crews;
    // crews found (or placed again) since the game loaded, by town and send time: a crew missing before it was ever seen is not in the save
    // (placed again, once); one missing after it was seen has been beaten
    readonly Presence<(string, double)> presence = new();
    readonly SortedDictionary<string, double> lastSent = new(StringComparer.Ordinal);
    bool failed;

    public void SlowWorld(World world)
    {
        Latest = this;
        seenWorld = world;
        if (Published.Due(world.Tick, 11)) Shown.Set(Summary);
        if (crews.Count > 0 && WorldReady.TakeLoaded(world, "hunters")) placeNow = true;   // just loaded: place crews the save left out now
        if (failed || !LwMod.Settings.BountyHunters || (world.Tick % PassEvery != 200 && !placeNow) || population is null || data is null) return;
        try
        {
            Pass(world, population, data);
        }
        catch (Exception e)
        {
            failed = true;
            LwMod.Log($"bounty hunters: stopped after an error: {e.GetBaseException().Message}");
        }
    }

    void Pass(World world, PopulationSystem pop, PopulationData d)
    {
        var players = Pursuit.Players(world);
        if (players.Count == 0) return;                     // (the first ticks after a load: kept for the next tick)
        placeNow = false;
        double now = world.GameHours;

        // crews other mods asked for (WastelandApi.SendHunters), first; a request waits its turn under Rivalry Core's cap, a day at most
        for (int i = 0; i < requests.Count; i++)
            if (requests[i].Hours < 0) requests[i] = requests[i] with { Hours = now };      // stamped at the first pass after the ask
        foreach (var ask in requests.ToList())
        {
            if (crews.Any(c => c.Town == ask.Town)) { requests.Remove(ask); LwMod.Log($"bounty hunters: {ask.Town} already has a crew out ({ask.Reason})"); continue; }
            if (now - ask.Hours > HunterRules.RequestWaits || now < ask.Hours) { requests.Remove(ask); LwMod.Log($"bounty hunters: dropped the request for a crew from {ask.Town} ({ask.Reason}): no turn within a day"); continue; }
            if (Send(world, pop, d, players, ask.Town, now, ask.Reason)) requests.Remove(ask);
        }

        // New crews from hunted towns are a reaction to heat. Explicit requests above have their own cause.
        if (LwMod.Settings.Heat && LwDeeds.For(world) is { } deeds)
        {
            foreach (var (town, _, crime, blood) in deeds.Heat.All())
            {
                double heat = deeds.Heat.Here(town);
                if (crews.Any(c => c.Town == town) || !HunterRules.Sends(heat, now, lastSent.TryGetValue(town, out var t) ? t : null)) continue;
                Send(world, pop, d, players, town, now, $"heat {heat:0}");
            }
        }

        // crews on the hunt
        var list = crews.ToList();
        var matched = MatchCrews(world, list);
        for (int ci = 0; ci < list.Count; ci++)
        {
            var crew = list[ci];
            var p = matched[ci];
            bool tagged = crew.Tagged || p is not null && Pursuit.ReadTag(p) == Tag(crew);
            // a crew never loaded near the squad has no characters yet, and Meitou's save keeps no such squad (SaveCapture writes stored or
            // campaign platoons only): after a load it is missing, not beaten. Place it again where it was, once per session.
            var key = (crew.Town, crew.SentHours);
            bool late = now - crew.SentHours > HunterRules.GiveUpAfter || now < crew.SentHours;
            var seen = presence.Check(world, key, p is not null);
            // killed just before a save: Meitou keeps a killed squad in the save as a dead platoon (v0.20.2-nightly.20261117, #860); it was
            // beaten, not missing, so it is not placed again
            if (seen == Presence<(string, double)>.Verdict.PlaceAgain && (!tagged || !Pursuit.HasTags) &&
                Pursuit.DeadNear(world, crewSquad, crew.X, crew.Z, eligible: Pursuit.IsUntagged))
                LwMod.Log($"bounty hunters: the crew from {crew.Town} is in the save as dead");
            else if (seen == Presence<(string, double)>.Verdict.PlaceAgain && !late)
            {
                p = Pursuit.Make(world, pop, d, crewSquad, $"lw:crew:{crew.Town}:{crew.SentHours:0.00}", (crew.X, crew.Z), players[0]);
                if (p is not null)
                {
                    tagged = Mark(p, crew);
                    LwMod.Log($"bounty hunters: the crew from {crew.Town} was not in the save; placed again at {crew.X:0} {crew.Z:0}");
                }
                else { presence.Forget(key); continue; }          // try again next pass
            }
            bool lost = p is not null && !p.Dead && Pursuit.Beaten(world, p);
            if (p is null || p.Dead || late || lost)
            {
                // seen this session and now gone, dead, or nobody left standing: beaten. Out of time: let go (no Recap moment for a crew that
                // just went home)
                bool beaten = !late && (p is null || p.Dead || lost);
                if (p is not null && !p.Dead) Pursuit.Release(p);
                bool warrant = beaten && lost && Trophies.DropWarrant(world, p!.SquadId, crew.Town);
                if (beaten) world.Post($"The bounty hunters {crew.Town} hired are finished.{(warrant ? " One of them carried a warrant with your squad's name on it." : "")}");
                if (beaten) RecapLink.Moment($"The bounty hunters {crew.Town} hired were {(lost ? "beaten" : "wiped out")}.", crew.Town, 70);
                crews.Remove(crew);
                LwMod.Log($"bounty hunters: the crew from {crew.Town} is {(beaten ? "gone (beaten)" : "giving up")}");
                continue;
            }
            Pursuit.Steer(world, p, players, BarkLines.Crew(crew.Town, (int)crew.SentHours));
            int i = crews.IndexOf(crew);
            crews[i] = crew with { X = p.Position.X, Z = p.Position.Y, Tagged = tagged };
        }
        RivalryLink.Keep(world, "crew:", crews.Select(c => PartyClaims.Crew(c.Town, c.SentHours)).ToList());
        if (crews.Count > 0 && (int)now != loggedHour)            // once a game hour: where the crews are, for play logs
        {
            loggedHour = (int)now;
            LwMod.Log($"bounty hunters: {Summary()}");
        }
    }

    int loggedHour = -1;

    /// <summary>Makes a crew from the town and sends it after the squad: false when no start point was found, the hostile slot is taken
    /// (Rivalry Core) or the crew could not be made.</summary>
    bool Send(World world, PopulationSystem pop, PopulationData d, List<(float X, float Z)> players, string town, double now, string why)
    {
        var near = players[0];
        var walk = world.Walkability;
        var start = HunterRules.Start(world.Seed, town, now, near.X, near.Z,
            (x, z) => walk.IsWalkable(x, z) && walk.GroundHeight(x, z) - walk.TerrainHeight(x, z) < 5f && CampRules.MayMove((x, z), (x, z), players));
        if (start is not { } at || !RivalryLink.MaySend(world, PartyClaims.Crew(town, now))
            || Pursuit.Make(world, pop, d, crewSquad, $"lw:crew:{world.Tick}", at, near) is not { } platoon) return false;
        var crew = new Crew(town, now, at.X, at.Z);
        crews.Add(crew with { Tagged = Mark(platoon, crew) });
        lastSent[town] = now;
        world.Post($"Word on the road: {town} has hired blades to bring you in.");
        RecapLink.Moment($"{town} hired bounty hunters to bring your squad in.", town, 65);
        LwMod.Log($"bounty hunters: {town} ({why}) sent a crew from {at.X:0} {at.Z:0}, day {now / 24:0.0}");
        return true;
    }

    static string Tag(Crew crew) => FormattableString.Invariant($"lw:bounty:{crew.Town}:{crew.SentHours:R}");

    static bool Mark(Platoon platoon, Crew crew) => Pursuit.Mark(platoon, Tag(crew));

    Platoon?[] MatchCrews(World world, IReadOnlyList<Crew> active)
    {
        if (active.Count == 0) return Array.Empty<Platoon?>();

        // An exact tag also matches a dead platoon. An older save only gets the proximity fallback on untagged parties.
        var tagged = world.Platoons.All.Where(p => p is not null && p.TemplateId == crewSquad && !Pursuit.IsUntagged(p))
            .ToLookup(p => Pursuit.ReadTag(p!), StringComparer.Ordinal);
        var resolved = new Platoon?[active.Count];
        var legacy = new List<int>();
        for (int i = 0; i < active.Count; i++)
        {
            var crew = active[i];
            var exact = tagged[Tag(crew)].Take(2).ToArray();
            if (exact.Length == 1) resolved[i] = exact[0];
            else if (!Pursuit.HasTags || !crew.Tagged && exact.Length == 0) legacy.Add(i);
        }
        if (legacy.Count == 0) return resolved;

        var lastKnown = new (float X, float Z)[legacy.Count];
        for (int i = 0; i < legacy.Count; i++)
        {
            var crew = active[legacy[i]];
            lastKnown[i] = (crew.X, crew.Z);
        }
        var near = Pursuit.Match(world, crewSquad, lastKnown, Pursuit.IsUntagged);
        for (int i = 0; i < legacy.Count; i++) resolved[legacy[i]] = near[i];
        return resolved;
    }

    readonly List<CrewRequest> requests = [];

    /// <summary>Queues a crew from a town for the next pass (WastelandApi.SendHunters); false when one is already out or asked for.</summary>
    public bool Request(string town, string reason)
    {
        if (crews.Any(c => c.Town == town) || requests.Any(r => r.Town == town)) return false;
        requests.Add(new CrewRequest(town, reason, -1));
        LwMod.Log($"bounty hunters: asked for a crew from {town} ({reason})");
        return true;
    }

    /// <summary>The F10 row's line.</summary>
    public string Summary()
    {
        if (crews.Count == 0) return "no crews hunting you";
        var w = seenWorld;
        var players = w is null ? [] : Pursuit.Players(w);
        var matched = w is null ? null : MatchCrews(w, crews);
        return string.Join("; ", crews.Select((c, i) =>
        {
            string far = "";
            var found = matched?[i];
            // loaded, its people are where it is (the platoon's own point can stay put: seen in play, 375 m for 13 game hours)
            var at = found is { State: PlatoonState.Loaded } && w!.Squads.Find(found.SquadId) is { } sq && Pursuit.MembersAt(w, sq) is { } m
                ? m : new Vector2(c.X, c.Z);
            if (players.Count > 0)
            {
                var n = Pursuit.Nearest(players, at.X, at.Y);
                far = $", about {MathF.Sqrt((n.X - at.X) * (n.X - at.X) + (n.Z - at.Y) * (n.Z - at.Y)) / 10:0} m from you";
            }
            string ago = w is null ? "" : $", sent {Math.Max(0, w.GameHours - c.SentHours):0.0} game hours ago";
            string state = w is not null && found is { Dead: false } p
                ? p.State == PlatoonState.Loaded ? (Pursuit.Stage(w, p.SquadId) is { } st ? $", in the world ({st})" : ", in the world (no order yet)")
                    : p.State == PlatoonState.Unloaded ? ", on the road (off screen)" : ", arriving"
                : ", not found";
            return $"a crew from {c.Town}{far}{ago}{state}";
        }));
    }

    World? seenWorld;

    internal static HuntersSystem? Latest;

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Format);
        w.Write(crews.Count);
        foreach (var c in crews) { w.Write(c.Town); w.Write(c.SentHours); w.Write(c.X); w.Write(c.Z); }
        w.Write(lastSent.Count);
        foreach (var (t, h) in lastSent) { w.Write(t); w.Write(h); }
        // after the rest, so builds before 20261116ap still read this save
        w.Write(RequestsTag);
        w.Write(requests.Count);
        foreach (var q in requests) { w.Write(q.Town); w.Write(q.Reason); w.Write(q.Hours); }
        if (crews.Any(c => c.Tagged))
        {
            w.Write(CrewTags);
            foreach (var c in crews) w.Write(c.Tagged);
        }
    }

    const int CrewTags = 0x4C435447;
    const int RequestsTag = 0x4C525153;   // "LRQS"

    public void Read(Stream stream)
    {
        crews.Clear();
        lastSent.Clear();
        requests.Clear();
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Position >= stream.Length || r.ReadInt32() != Format) return;
        for (int n = r.ReadInt32(); n > 0; n--) crews.Add(new Crew(r.ReadString(), r.ReadDouble(), r.ReadSingle(), r.ReadSingle()));
        for (int n = r.ReadInt32(); n > 0; n--) lastSent[r.ReadString()] = r.ReadDouble();
        if (stream.Length - stream.Position >= 8 && r.ReadInt32() == RequestsTag)
        {
            for (int n = r.ReadInt32(); n > 0; n--) requests.Add(new CrewRequest(r.ReadString(), r.ReadString(), r.ReadDouble()));
            if (stream.Length - stream.Position >= sizeof(int) + crews.Count && r.ReadInt32() == CrewTags)
                for (int i = 0; i < crews.Count; i++) crews[i] = crews[i] with { Tagged = r.ReadBoolean() };
        }
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(crews.Count);
        foreach (var c in crews) { h.Add(SquadRecord.StableHash(c.Town)); h.Add(c.SentHours); if (c.Tagged) h.Add(CrewTags); }
        h.Add(lastSent.Count);
        h.Add(requests.Count);
    }
}

/// <summary>
/// A squad sent after the player (bounty crews, outlaw reprisals): made as a persistent stand-in with the travel job, steered at the nearest
/// player character every pass (the stand-in's point while unloaded, a walk order for each member once loaded), found again after a load
/// by template and last position.
/// </summary>
public static class Pursuit
{
    static readonly PropertyInfo? modTag = typeof(Platoon).GetProperty("ModTag") is
        { CanRead: true, CanWrite: true, PropertyType: var type } property && type == typeof(string) ? property : null;

    internal static bool HasTags => modTag is not null;

    internal static string? ReadTag(Platoon platoon) => modTag?.GetValue(platoon) as string;

    internal static bool IsUntagged(Platoon platoon) => string.IsNullOrEmpty(ReadTag(platoon));

    internal static bool Mark(Platoon platoon, string tag)
    {
        if (modTag is null) return false;
        modTag.SetValue(platoon, tag);
        return true;
    }

    /// <summary>
    /// The town a placed squad counts as coming from. Meitou makes a fresh roaming squad (no saved or stored characters) at its origin town's
    /// site (PopulationSystem.FinishRoamer reads Sites[Origin], 0.17 to 0.19.1), so Origin -1 would throw when it loads. The nearest town of
    /// another faction: never one that would draft it into its own campaigns. -1 only when the world has no town.
    /// </summary>
    public static int OriginFor(PopulationData d, float x, float z, int faction)
    {
        int best = -1, any = -1;
        float bestD = float.MaxValue, anyD = float.MaxValue;
        for (int i = 0; i < d.Sites.Count; i++)
        {
            var at = d.Sites[i].Position;
            float dd = (at.X - x) * (at.X - x) + (at.Z - z) * (at.Z - z);
            if (dd < anyD) { anyD = dd; any = i; }
            int owner = i < d.SiteFactions.Count ? d.SiteFactions[i] : -1;
            if (owner != faction && dd < bestD) { bestD = dd; best = i; }
        }
        return best >= 0 ? best : any;
    }

    public static Platoon? Make(World world, PopulationSystem pop, PopulationData d, string templateId, string keyText, (float X, float Z) at, (float X, float Z) toward)
    {
        if (d.Db.Find(templateId) is not { } record) { LwMod.Log($"pursuit: squad {templateId} is not in the game data"); return null; }
        var template = SquadTemplate.From(record);
        ulong key = Rng.StableHash(keyText);
        var plan = SquadFactory.Plan(d.Db, template, pop.Settings.SquadSizeMultiplier, world.Seed, key, pop.States);
        var (package, _) = AiCompat.JobsFor(d.Ai, template);
        var platoon = new Platoon
        {
            Id = world.Platoons.NextId(),
            Origin = OriginFor(d, at.X, at.Z, template.Faction is { } f0 ? d.FactionIndex(f0) : -1),
            TemplateId = template.Id,
            Faction = template.Faction is { } f ? d.FactionIndex(f) : -1,
            Key = key,
            Size = Math.Max(2, plan.TotalMembers),
            Position = new Vector2(at.X, at.Z),
            Package = package?.Id,
            Job = PlatoonJob.PatrolShortRange,         // see Steer: the one job that walks a persistent stand-in to the point
            Point = new Vector2(toward.X, toward.Z),
            Persistent = true,
        };
        world.Platoons.Add(platoon);
        return platoon;
    }

    /// <summary>Platoons of a template for several last known points at once, closest pairs first, each platoon given once; null where none is left.</summary>
    public static Platoon?[] Match(World world, string templateId, IReadOnlyList<(float X, float Z)> points, Func<Platoon, bool>? eligible = null)
    {
        var result = new Platoon?[points.Count];
        var live = world.Platoons.All.Where(p => p is not null && !p.Dead && p.TemplateId == templateId && (eligible is null || eligible(p))).Select(p => p!).ToList();
        var pairs = new List<(float D, int Point, Platoon P)>();
        for (int i = 0; i < points.Count; i++)
            foreach (var p in live) pairs.Add((Vector2.Distance(p.Position, new Vector2(points[i].X, points[i].Z)), i, p));
        var used = new HashSet<int>();
        foreach (var (_, i, p) in pairs.OrderBy(x => x.D))
        {
            if (result[i] is not null || used.Contains(p.Id)) continue;
            result[i] = p;
            used.Add(p.Id);
        }
        return result;
    }

    public static Platoon? Find(World world, string templateId, float x, float z, HashSet<int> claimed) =>
        Find(world, templateId, x, z, claimed, eligible: null);

    internal static Platoon? Find(World world, string templateId, float x, float z, HashSet<int> claimed, Func<Platoon, bool>? eligible)
    {
        Platoon? best = null;
        float bestD = float.MaxValue;
        foreach (var p in world.Platoons.All)
        {
            if (p is null || p.Dead || p.TemplateId != templateId || claimed.Contains(p.Id) || (eligible is not null && !eligible(p))) continue;
            float dd = Vector2.Distance(p.Position, new Vector2(x, z));
            if (dd < bestD) { best = p; bestD = dd; }
        }
        return best;
    }

    /// <summary>Whether a dead platoon of the template lies within <paramref name="within"/> of a last known position: a squad killed before
    /// a save, kept in it as dead (Meitou #860).</summary>
    public static bool DeadNear(World world, string templateId, float x, float z, float within = 3000, Func<Platoon, bool>? eligible = null)
    {
        foreach (var p in world.Platoons.All)
            if (p is { Dead: true } && p.TemplateId == templateId && (eligible is null || eligible(p)) && Vector2.Distance(p.Position, new Vector2(x, z)) <= within) return true;
        return false;
    }

    /// <summary>Points the stand-in (or, loaded, each able member) at the nearest player character.</summary>
    sealed class RoadProgress { public Vector2? Last; }
    sealed class SquadProgress
    {
        public string? Package;
        public float Best = float.MaxValue;
        public int Still;
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Platoon, RoadProgress> roadProgress = new();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Squad, SquadProgress> squadProgress = new();
    public const string AssaultPackage = "2666-gamedata.base";   // Assault target squad (agg): attack enemies, follow leader, assault the target
    public const string FollowPackage = "5090-gamedata.base";    // Bodyguard: follow mission target (closes the distance), attack enemies

    /// <summary>Every standing member not already fighting is ordered to attack the target (the order vanilla dialogue's Assault gives).</summary>
    static void Assault(World world, Squad squad, CharacterId target)
    {
        if (!AiCompat.CanAssault || world.Systems.OfType<Meitou.Simulation.Ai.AiSystem>().FirstOrDefault() is not { } ai) return;
        var table = world.Characters;
        bool any = false;
        foreach (var m in squad.Members)
        {
            if (!table.TryResolveNext(m, out int s) || table.Cold(s) is not { } c || c.InCombat || c.Medical is { Incapacitated: true }) continue;
            any |= AiCompat.OrderAssault(ai, world, m, target);
        }
        if (any) squadProgress.GetOrCreateValue(squad).Package = AssaultPackage;
    }

    /// <summary>What a loaded pursuer was last told (the F10 rows): following, attacking, or null.</summary>
    public static string? Stage(World world, int squadId) =>
        world.Squads.Find(squadId) is { } squad && squadProgress.TryGetValue(squad, out var progress) && progress.Package is { } package
            ? package == AssaultPackage ? "attacking" : "following you"
            : null;

    static System.Numerics.Vector3? table0(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int s) ? world.Characters.Next[s].Position : null;

    static CharacterId? NearestPlayer(World world, Vector2 at)
    {
        var table = world.Characters;
        CharacterId? best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < table.HighWater; i++)
        {
            if (!table.Next[i].Alive || table.Cold(i) is not { IsPlayer: true, Animal: null } c || c.Medical is { Incapacitated: true }) continue;
            var pos = table.Next[i].Position;
            float d = (pos.X - at.X) * (pos.X - at.X) + (pos.Z - at.Y) * (pos.Z - at.Y);
            if (d < bestD) { bestD = d; best = table.IdOf(i); }
        }
        return best;
    }   // platoon -> where it stood at the last pass (not state: rebuilt after a load)

    /// <summary>Squads that have spoken their line on closing in, per loaded game (once each).</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, HashSet<int>> barked = new();

    /// <summary>Steers a pursuer to the nearest player character. <paramref name="bark"/>: the line one of them says over their head
    /// (World.Bark) the first time they close in to attack; <paramref name="speakerName"/>: who says it (a rival), else whoever leads.</summary>
    public static void Steer(World world, Platoon p, List<(float X, float Z)> players, string? bark = null, string? speakerName = null)
    {
        var target = Nearest(players, p.Position.X, p.Position.Y);
        if (p.State == PlatoonState.Unloaded)
        {
            // the job matters (PopulationSystem.StandIn / MissionGoal, read in 0.19.1): a travel job walks the stand-in to its origin town
            // and clears the point; no job on a persistent platoon never moves at all; a short-range patrol walks to the point (Goal),
            // which this pass sets again every time
            p.Point = new Vector2(target.X, target.Z);
            p.Job = PlatoonJob.PatrolShortRange;
            p.WaitLeft = 0;
            // the fallback: a stand-in the engine moves too little (a save's wild squad; or one inside the loaded area round the squad, where
            // stand-ins mostly wait to be made: seen in play crawling 70 m an hour) is walked on by hand to walking pace, 2.5 m/s (Meitou's
            // own travel pace) over the pass's ten game seconds, never closer than 30 m (from there it is loaded and walks itself)
            // Judged by how much nearer it got, not how far it went: seen in play (build au), crews drifted AWAY from a squad that waited
            // two game hours (the engine moving the stand-in elsewhere, likely home), and a stand-in that moved at all was never walked on.
            // One that went the wrong way is walked on from where it was.
            var progress = roadProgress.GetOrCreateValue(p);
            if (progress.Last is { } was)
            {
                var aim = new Vector2(target.X, target.Z);
                float closed = Vector2.Distance(was, aim) - Vector2.Distance(p.Position, aim);
                var from = closed < 0 ? was : p.Position;
                var to = aim - from;
                float len = to.Length();
                if (len > 300f && closed < 200f) p.Position = from + to / len * MathF.Min(closed < 0 ? 250f : 250f - closed, len - 300f);
            }
            progress.Last = p.Position;
        }
        else if (p.State == PlatoonState.Loaded && world.Squads.Find(p.SquadId) is { } squad)
        {
            // Loaded near the squad, its own AI decides what it does and walk orders alone lose to it. Far off (over 60 m), it gets the
            // vanilla Bodyguard contract aimed at the nearest player character (Meitou 0.19+ StartContract), whose "follow mission target"
            // goal walks it over (Meitou 0.19.1+ made that goal follow properly). Given once: a running contract cannot be re-aimed or
            // replaced. Close (60 m), each member gets the Assault order vanilla dialogue gives (AiSystem.OrderAssault), again each pass
            // while it is not fighting.
            // The distance is from its nearest standing member, not the platoon's point: seen in play (build aw), a loaded crew's point stayed
            // 375 m off for 13 game hours, so it never got the Assault order. One that has not got nearer for StallPasses passes is ordered
            // to attack from wherever it is (the order walks it over).
            if (NearestPlayer(world, MembersAt(world, squad) ?? p.Position) is { } prey && table0(world, prey) is { } preyAt)
            {
                var from = MembersAt(world, squad, new Vector2(preyAt.X, preyAt.Z)) ?? p.Position;
                float d = Vector2.Distance(from, new Vector2(preyAt.X, preyAt.Z));
                var progress = squadProgress.GetOrCreateValue(squad);
                if (progress.Package is null && SquadRoles.Contract(squad.Id, FollowPackage, 24f, prey)) progress.Package = FollowPackage;
                int still = progress.Still = d < progress.Best - 100f ? 0 : progress.Still + 1;
                progress.Best = Math.Min(progress.Best, d);
                bool stalled = still >= StallPasses;
                if (stalled && still == StallPasses)
                {
                    string who = Speaker(world, squad, speakerName) is { } sp ? world.Characters.Cold(sp.Slot)?.Name ?? "a pursuer" : "a pursuer";
                    LwMod.Log($"pursuit: {who}'s party got no nearer in {StallPasses} passes ({d / 10:0} m off): ordered to attack");
                }
                if (d < 600f || stalled)
                {
                    Assault(world, squad, prey);
                    if (bark is not null && barked.GetOrCreateValue(world).Add(squad.Id) && Speaker(world, squad, speakerName) is { } who)
                    {
                        world.Bark(who, bark);
                        LwMod.Log($"barks: \"{bark}\"");
                    }
                }
            }
            var table = world.Characters;
            foreach (var m in squad.Members)
            {
                if (!table.TryResolveNext(m, out int s) || table.Cold(s) is not { } c || c.InCombat || c.Medical is { Incapacitated: true }) continue;
                var at = table.Next[s].Position;
                var goal = Nearest(players, at.X, at.Z);
                TaskStart.Walk(world, s, new Vector2(goal.X, goal.Z), 0, TaskType.TravelToTargetPackage, TaskSource.Goal);
            }
        }
    }

    const int StallPasses = 12;                                     // two game minutes of passes without getting 10 m nearer

    /// <summary>Where a loaded squad is: its standing member nearest <paramref name="to"/> (any standing member without one); null when
    /// none stands.</summary>
    public static Vector2? MembersAt(World world, Squad squad, Vector2? to = null)
    {
        var table = world.Characters;
        Vector2? best = null;
        foreach (var m in squad.Members)
        {
            if (!table.TryResolveNext(m, out int s) || !table.Next[s].Alive || table.Cold(s) is not { } c || c.Medical is { Incapacitated: true }) continue;
            var at = new Vector2(table.Next[s].Position.X, table.Next[s].Position.Z);
            if (to is null) return at;
            if (best is null || Vector2.Distance(at, to.Value) < Vector2.Distance(best.Value, to.Value)) best = at;
        }
        return best;
    }

    /// <summary>A loaded pursuer with nobody left standing (all dead, down, or taken) has lost: the squad won the fight. Seen in play: a crew
    /// beaten to the last man down still read "attacking" while one lay alive. Unloaded or still standing: not beaten.</summary>
    public static bool Beaten(World world, Platoon p)
    {
        if (p.State != PlatoonState.Loaded || world.Squads.Find(p.SquadId) is not { } squad) return false;
        var table = world.Characters;
        int seen = 0;
        foreach (var m in squad.Members)
        {
            if (!table.TryResolveNext(m, out int s) || table.Cold(s) is not { } c) continue;
            seen++;
            if (table.Next[s].Alive && c.Medical is { Dead: false, Incapacitated: false } && !c.IsImprisoned && c.CarriedBy.IsNone) return false;
        }
        return seen > 0;
    }

    /// <summary>Who of a squad speaks: the member named <paramref name="name"/>, else the leader, else anyone standing.</summary>
    static CharacterId? Speaker(World world, Squad squad, string? name)
    {
        var table = world.Characters;
        CharacterId? first = null;
        foreach (var m in squad.Members)
        {
            if (!table.TryResolveNext(m, out int s) || !table.Next[s].Alive || table.Cold(s) is not { } c || c.Medical is { Incapacitated: true }) continue;
            if (name is not null && c.Name == name) return m;
            if (m == squad.Leader) first = m;
            first ??= m;
        }
        return name is null || first is null ? first : null;
    }

    /// <summary>Lets a pursuer go: no longer kept by the engine or steered.</summary>
    public static void Release(Platoon p) { p.Persistent = false; p.Point = null; }

    public static (float X, float Z) Nearest(List<(float X, float Z)> players, float x, float z)
    {
        ArgumentNullException.ThrowIfNull(players, "source");
        if (players.Count == 0) throw new InvalidOperationException("Sequence contains no elements");

        var nearest = players[0];
        float best = SquaredDistance(nearest, x, z);
        for (int i = 1; i < players.Count; i++)
        {
            var candidate = players[i];
            float distance = SquaredDistance(candidate, x, z);
            if (distance.CompareTo(best) >= 0) continue;
            nearest = candidate;
            best = distance;
        }
        return nearest;

        static float SquaredDistance((float X, float Z) at, float x, float z)
        {
            float dx = at.X - x, dz = at.Z - z;
            return dx * dx + dz * dz;
        }
    }

    public static List<(float X, float Z)> Players(World world)
    {
        var list = new List<(float X, float Z)>();
        var table = world.Characters;
        var hot = table.Previous;
        for (int s = 0; s < hot.Length; s++)
        {
            if (!hot[s].Alive || table.Cold(s) is not { IsPlayer: true, Animal: null } c || c.Medical is { Dead: true }) continue;
            list.Add((hot[s].Position.X, hot[s].Position.Z));
        }
        return list;
    }

    /// <summary>A dry, unseen start point 2.5 to 3.5 km from (x, z).</summary>
    public static (float X, float Z)? StartNear(World world, string seedText, (float X, float Z) near, List<(float X, float Z)> players)
    {
        var walk = world.Walkability;
        return HunterRules.Start(world.Seed, seedText, world.GameHours, near.X, near.Z,
            (x, z) => walk.IsWalkable(x, z) && walk.GroundHeight(x, z) - walk.TerrainHeight(x, z) < 5f && CampRules.MayMove((x, z), (x, z), players));
    }
}
