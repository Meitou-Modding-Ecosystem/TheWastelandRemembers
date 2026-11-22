// Moving outlaw camps (Meitou only, behind the "Outlaw camps" setting, off by default until play-tested). Design and the engine
// findings it rests on: modgen/OUTLAWS_LIVE_DESIGN.md. Pure rules: CampRules.cs.
//
// Each living outlaw with a camp entry in lw_codes.json gets one persistent squad in its regions. Where it sits follows the outlaw news
// (OutlawNews.Doing): raiding near towns, lying low far from them, the rest in between; it moves when a new stretch starts, only while
// it is not loaded and no player character is near the old or the new spot. Gathering men makes a fresh camp bigger, licking wounds
// smaller. Platoon ids change on every load and a camp nobody went near is not in the save, so every pass finds the camp by its squad
// template (adopting the engine's own spawn of it, if there is one) and makes a new one when there is none. Only the official API.
using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.World;
using Meitou.Modding;
using Meitou.Simulation;
using System.Runtime.CompilerServices;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

/// <summary>One outlaw's camp entry from lw_codes.json: its news index, name, character and squad template ids, region (biome group) ids.</summary>
public sealed record CampCode(int Index, string Name, string Character, string Squad, List<string> Biomes, string? Reprisal = null,
    SuccessorCode? Successor = null, string? Infiltrators = null);

/// <summary>One of the gang who takes over the outlaw's ground some days after the outlaw dies or is jailed: a unique character of its own and
/// its squad, placed only by the helper (never in a spawn list).</summary>
public sealed record SuccessorCode(string Name, string Character, string Squad, int Bounty = 0);

/// <summary>The outlaws the camp gossip names (lw_codes.json "campNews"): the towns its word swaps list, and each outlaw's three codes.</summary>
public sealed record CampNewsTable(List<string> Towns, List<CampNewsCode> Outlaws);
public sealed record CampNewsCode(string Name, int Town, int Dir, int Struck, int Pay = 0, int Reach = 0, int Tier = 0, int Reprisal = 0, int NewsReach = 0,
    int Successor = 0);

public sealed class OutlawCampsSystem(IReadOnlyList<CampCode> outlaws, PopulationSystem? population, PopulationData? data)
    : ICombatHitObserver, IModSaveData, IStateHashed
{
    static readonly ConditionalWeakTable<World, OutlawCampsSystem> byWorld = new();

    /// <summary>The system of a world (for the dialogue conditions); null before its first tick.</summary>
    public static OutlawCampsSystem? For(World world) => byWorld.TryGetValue(world, out var s) ? s : null;

    // ---- the public API (WastelandApi version 4) ----

    /// <summary>One row per named outlaw (WastelandApi.Outlaws): who, which faction, alive or not, the successor, the mood, the price, the
    /// home regions and the camp (when moving camps keep one), and whether the squad has heard of them.</summary>
    public List<IReadOnlyDictionary<string, object>> ApiRows(World world)
    {
        var rows = new List<IReadOnlyDictionary<string, object>>();
        string atlas = RecordHost.AtlasList;
        foreach (var o in outlaws)
        {
            var state = world.Uniques.StateOf(o.Character);
            bool succeeded = o.Successor is not null && SuccessorActive(world, o.Index);
            string status = JoinedPlayer(world, o) ? "joined" : state == UniqueState.Alive ? "alive" : succeeded ? "succeeded"
                : state == UniqueState.Imprisoned ? "jailed" : "dead";
            bool active = status is "alive" or "succeeded";
            int bounty = status == "alive" ? (int)Math.Round((data?.Db.Find(o.Character)?.GetInt("bounty amount") ?? 0) * (1 + GrowthOf(world, o.Index)))
                : succeeded ? (int)Math.Round((data?.Db.Find(o.Successor!.Character)?.GetInt("bounty amount") ?? o.Successor!.Bounty) * (1 + SuccessorGrowth(world, o.Index))) : 0;
            var camp = book.Get(o.Index);
            rows.Add(new Dictionary<string, object>
            {
                ["Name"] = o.Name, ["Faction"] = FactionOf(o), ["Status"] = status, ["Successor"] = succeeded ? o.Successor!.Name : "",
                ["Mood"] = active ? Mood(DoingOf(world, o.Index)) : "",
                ["Bounty"] = bounty, ["Regions"] = string.Join(",", o.Biomes), ["HasCamp"] = camp is not null,
                ["CampX"] = camp?.X ?? 0f, ["CampZ"] = camp?.Z ?? 0f,
                ["Known"] = atlas.Contains($"@@w:{o.Name}|", StringComparison.Ordinal),
            });
        }
        return rows;
    }

    static string Mood(int doing) => doing switch
    {
        OutlawNews.Raiding => "raiding", OutlawNews.LyingLow => "lying-low", OutlawNews.Gathering => "gathering", OutlawNews.Wounded => "wounded", _ => "",
    };

    string FactionOf(CampCode o) =>
        data is not null && data.Db.Find(o.Squad) is { } rec && SquadTemplate.From(rec).Faction is { } f && data.FactionIndex(f) is int i and >= 0
            && i < data.Factions.Count ? data.Factions[i].Name : "";

    /// <summary>The outlaw whose ground the point is in or nearest (camp, else the nearest cell of its regions), within
    /// <paramref name="within"/> world units; "" when none.</summary>
    public string OutlawAt(World world, float x, float z, float within)
    {
        string best = "";
        float bestD = within;
        foreach (var o in outlaws)
        {
            if (world.Uniques.StateOf(o.Character) != UniqueState.Alive && !(o.Successor is not null && SuccessorActive(world, o.Index))) continue;
            float d = DistanceToGround(o.Index, x, z);
            if (d <= bestD) { bestD = d; best = o.Name; }
        }
        return best;
    }

    /// <summary>Another mod's band struck an outlaw's camp (WastelandApi.HideoutStruck): the outlaw lies low (or licks wounds) as after the
    /// squad's blows, but no reprisal comes after the squad. False for an unknown or dead outlaw.</summary>
    public bool StruckByOthers(World world, string name, bool bloodied)
    {
        var o = outlaws.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (o is null || world.Uniques.StateOf(o.Character) != UniqueState.Alive && !(o.Successor is not null && SuccessorActive(world, o.Index))) return false;
        // a new mark is the band's doing: answered, so no reprisal; one still fresh from the squad's own blows keeps its reprisal
        if (book.Strike(o.Index, world.GameHours, bloodied)) book.Answer(o.Index, world.GameHours);
        return true;
    }

    /// <summary>Living outlaws with a camp whose page the Atlas shows (the squad has heard of them): name, mood, camp (map marks).</summary>
    public IEnumerable<(string Name, string Doing, float X, float Z)> KnownCamps(World world)
    {
        string atlas = RecordHost.AtlasList;
        foreach (var o in outlaws)
            if (book.Get(o.Index) is { } e && world.Uniques.StateOf(o.Character) == UniqueState.Alive && atlas.Contains($"@@w:{o.Name}|", StringComparison.Ordinal))
                yield return (o.Name, MapMarkRules.Doing(DoingOf(world, o.Index)), e.X, e.Z);
    }

    /// <summary>The town names the camp word swaps list, in code order (set from lw_codes.json).</summary>
    internal static List<string> NewsTowns = [];

    const int PassEvery = 300;                  // ticks between passes (10 game seconds)
    const float HoldStill = 1e9f;               // WaitLeft for a stand-in that must stay where it is
    const float HomeRadius = 60f;               // how far a loaded camp's people may stray from the spot

    static readonly string[] DoingNames = ["", "raiding", "lying low", "gathering men", "licking wounds"];

    /// <summary>The system of the world that ticked last; the F10 row reads it.</summary>
    internal static OutlawCampsSystem? Latest;
    /// <summary>The world this system last ticked in (the API's reads).</summary>
    internal World? LastWorld;

    readonly CampBook book = new();

    /// <summary>The reprisal parties out after the squad, by outlaw name (WastelandApi).</summary>
    internal IEnumerable<(string Outlaw, float X, float Z)> ReprisalParties() =>
        book.Reprisals.Select(r => (outlaws.FirstOrDefault(o => o.Index == r.Outlaw)?.Name ?? "?", r.X, r.Z));
    readonly Dictionary<int, List<(int X, int Z)>> cellsOf = [];
    readonly Dictionary<int, int> pinned = [];      // platoon id -> the squad id last pinned (a camp gets a new squad each time it loads)
    List<(float X, float Z)>? towns;
    string lastSummary = "camps are off (the Squad record window's Settings tab)";
    bool warned;

    // ---- outlaws who react ----

    /// <summary>Blows on an outlaw or its people by the squad (or its hired blades), queued by the hit observer and applied in the slow step.</summary>
    readonly List<(int Outlaw, bool Bloodied)> blows = [];
    Dictionary<string, CampCode>? bySquad, byCharacter;

    public void HitResolved(World world, in CombatHit hit)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(hit.Attacker, out int a) || table.Cold(a) is not { } attacker) return;
        if (!attacker.IsPlayer && !IsHiredBy(world, attacker)) return;
        if (ArenaLink.Sport(hit.Attacker, hit.Victim)) return;          // a bout in Proving Grounds' arena strikes no outlaw
        if (!table.TryResolveNext(hit.Victim, out int v) || table.Cold(v) is not { IsPlayer: false } victim) return;
        bySquad ??= outlaws.ToDictionary(o => o.Squad);
        byCharacter ??= outlaws.ToDictionary(o => o.Character);
        CampCode? o = world.Uniques.RecordOf(hit.Victim) is { } rec && byCharacter.TryGetValue(rec, out var named) ? named : null;
        if (o is null && victim.SquadId >= 0)
        {
            foreach (var p in world.Platoons.All)
                if (p is not null && p.SquadId == victim.SquadId) { bySquad.TryGetValue(p.TemplateId, out o); break; }
        }
        if (o is null) return;
        bool bloodied = victim.Medical is { } m && (m.Dead || m.Incapacitated);
        blows.Add((o.Index, bloodied));
    }

    static bool IsHiredBy(World world, CharacterCold c) => c.SquadId >= 0 && world.Squads.Find(c.SquadId)?.Ai?.Contract?.Signal is 12 or 15;

    /// <summary>What an outlaw is doing in this world (the news roll, unless the squad shook them lately).</summary>
    public int DoingOf(World world, int outlaw) => CampRules.Doing(world.Seed, outlaw, world.GameHours, book.StruckOf(outlaw));

    /// <summary>For the dialogue conditions when no system has ticked yet: the plain news roll.</summary>
    public static int DoingIn(World world, int outlaw) => For(world)?.DoingOf(world, outlaw) ?? OutlawNews.Doing(world.Seed, outlaw, world.GameHours);

    void ApplyBlows(World world)
    {
        foreach (var (index, bloodied) in blows)
        {
            if (!book.Strike(index, world.GameHours, bloodied)) continue;
            var o = outlaws.First(x => x.Index == index);
            if (world.Uniques.StateOf(o.Character) == UniqueState.Alive)
                LwMod.Log($"outlaws react: your squad struck {o.Name}'s people (day {world.GameHours / 24:0.0}); they {(bloodied ? "lick their wounds" : "lie low")} and move camp");
        }
        blows.Clear();
    }

    // ---- outlaw reprisals ----

    /// <summary>Squads (by squad id) that are an outlaw's reprisal party, for the demand line's condition (rebuilt every pass).</summary>
    volatile Dictionary<int, int> reprisalSquads = [];

    /// <summary>Whether a character is one of this outlaw's reprisal party.</summary>
    public bool InReprisalOf(World world, CharacterId who, int outlaw)
    {
        var table = world.Characters;
        return table.TryResolveNext(who, out int s) && table.Cold(s) is { } c && reprisalSquads.TryGetValue(c.SquadId, out int o) && o == outlaw;
    }

    readonly Presence<(int, double)> parties = new();

    bool reprisalsNow;                                      // a save just loaded (WorldReady): pass at the next tick the squad is seen

    void Reprisals(World world, PopulationSystem pop, PopulationData d)
    {
        var players = Pursuit.Players(world);
        if (players.Count == 0) return;                     // (the first ticks after a load: kept for the next tick)
        reprisalsNow = false;
        double now = world.GameHours;
        foreach (var o in outlaws)
        {
            var mark = book.StruckOf(o.Index);
            if (world.Uniques.StateOf(o.Character) != UniqueState.Alive || JoinedPlayer(world, o)
                || o.Reprisal is null || !ReprisalRules.Due(world.Seed, o.Index, mark, now, book.AnsweredOf(o.Index)) || book.Reprisals.Any(r => r.Outlaw == o.Index)) continue;
            var near = players[0];
            if (Pursuit.StartNear(world, $"reprisal:{o.Name}:{mark!.Hours}", near, players) is not { } at
                || !RivalryLink.MaySend(world, PartyClaims.Reprisal(o.Name, now))) continue;
            if (Pursuit.Make(world, pop, d, o.Reprisal, $"lw:reprisal:{o.Name}:{world.Tick}", at, near) is null) continue;
            book.Sent(new Reprisal(o.Index, now, at.X, at.Z), mark.Hours);
            world.Post($"Word on the road: {o.Name} has not forgotten you. Men are coming.");
            LwMod.Log($"outlaw reprisal: {o.Name} sent a party from {at.X:0} {at.Z:0}, day {now / 24:0.0}");
            RecapLink.Moment($"{o.Name} sent men after your squad.", "the road", 70);
        }

        var claimed = new HashSet<int>();
        var squads = new Dictionary<int, int>();
        foreach (var r in book.Reprisals.ToList())
        {
            var o = outlaws.First(x => x.Index == r.Outlaw);
            var p = o.Reprisal is null ? null : Pursuit.Find(world, o.Reprisal, r.X, r.Z, claimed);
            // as the bounty crews (Presence): a party never loaded is not in Meitou's save; after a load, place it again where it was (once)
            bool late = now - r.SentHours > ReprisalRules.GiveUpAfter || now < r.SentHours;
            var seen = parties.Check(world, (r.Outlaw, r.SentHours), p is not null);
            if (seen == Presence<(int, double)>.Verdict.PlaceAgain && o.Reprisal is not null && Pursuit.DeadNear(world, o.Reprisal, r.X, r.Z))
                LwMod.Log($"outlaw reprisal: {o.Name}'s party is in the save as dead");
            else if (seen == Presence<(int, double)>.Verdict.PlaceAgain && !late && o.Reprisal is not null)
            {
                p = Pursuit.Make(world, pop, d, o.Reprisal, $"lw:reprisal:{o.Name}:{r.SentHours:0.00}", (r.X, r.Z), players.Count > 0 ? players[0] : (r.X, r.Z));
                if (p is not null) LwMod.Log($"outlaw reprisal: {o.Name}'s party was not in the save; placed again at {r.X:0} {r.Z:0}");
                else { parties.Forget((r.Outlaw, r.SentHours)); continue; }
            }
            bool lost = p is not null && !p.Dead && Pursuit.Beaten(world, p);
            if (p is null || p.Dead || late || lost)
            {
                bool beaten = !late && (p is null || p.Dead || lost);
                if (p is not null && !p.Dead) Pursuit.Release(p);
                if (beaten) RecapLink.Moment($"The men {o.Name} sent after your squad were cut down.", "the road", 70);
                book.Done(r);
                LwMod.Log($"outlaw reprisal: {o.Name}'s party is {(beaten ? "gone (beaten)" : "giving up")}");
                continue;
            }
            claimed.Add(p.Id);
            Pursuit.Steer(world, p, players, BarkLines.Reprisal(o.Name, (int)r.SentHours));
            if (p.State == PlatoonState.Loaded && p.SquadId >= 0) squads[p.SquadId] = o.Index;
            book.Update(r, r with { X = p.Position.X, Z = p.Position.Y });
        }
        reprisalSquads = squads;
        RivalryLink.Keep(world, "reprisal:", book.Reprisals
            .Select(r => PartyClaims.Reprisal(outlaws.First(x => x.Index == r.Outlaw).Name, r.SentHours)).ToList());
    }

    // ---- gossip that points ----

    List<(float X, float Z)>? newsTownPos;

    /// <summary>1 + the place in <see cref="NewsTowns"/> of the town nearest the outlaw's camp; 0 when no camp is known.</summary>
    public int CampTown(int outlaw) => Place(outlaw).Town;

    /// <summary>Which way the camp lies from that town (1-8 from north clockwise, 9 right by it); 0 when no camp is known.</summary>
    public int CampDir(int outlaw) => Place(outlaw).Dir;

    (int Town, int Dir) Place(int outlaw)
    {
        if (book.Get(outlaw) is not { } e || data is null || NewsTowns.Count == 0) return (0, 0);
        newsTownPos ??= NewsTowns.Select(name => data.Sites.FirstOrDefault(site => string.Equals(TownNames.Clean(site.Town.Name), name, StringComparison.OrdinalIgnoreCase)))
            .Select(site => site is null ? (float.NaN, float.NaN) : (site.Position.X, site.Position.Z)).ToList();
        var known = newsTownPos.Select((p, i) => (p, i)).Where(t => !float.IsNaN(t.p.Item1)).ToList();
        int k = CampRules.NearestIndex(e.X, e.Z, known.Select(t => t.p).ToList());
        if (k < 0) return (0, 0);
        var (tx, tz) = known[k].p;
        return (known[k].i + 1, CampRules.Direction(tx, tz, e.X, e.Z));
    }

    /// <summary>How far a point is from the outlaw's ground: its camp when the helper knows it, else the nearest cell of its zones;
    /// float.MaxValue when neither is known (no region map).</summary>
    public float DistanceToGround(int outlaw, float x, float z)
    {
        float best = float.MaxValue;
        if (book.Get(outlaw) is { } e) best = MathF.Sqrt((e.X - x) * (e.X - x) + (e.Z - z) * (e.Z - z));
        if (data?.Biomes is not null && outlaws.FirstOrDefault(o => o.Index == outlaw) is { } code)
        {
            foreach (var (cx, cz) in CellsOf(data, code))
            {
                var (mx, mz) = CampRules.CellCentre(cx, cz);
                best = Math.Min(best, MathF.Sqrt((mx - x) * (mx - x) + (mz - z) * (mz - z)));
            }
        }
        return best;
    }

    // ---- successors ----

    World? seenWorld;                                  // for the F10 row (the debug context carries no world)
    readonly HashSet<int> remadeSuccessors = [];
    object? remadeIn;                                  // the world remadeSuccessors belongs to (a game loaded in the same run starts again)
    public const double SuccessorDelayHours = 72;     // three game days after the outlaw is first seen dead or jailed
    static string DownKey(int index) => $"@succdown {index}";
    static string UpKey(int index) => $"@succup {index}";
    static string AtKey(int index) => $"@succat {index}";

    /// <summary>
    /// How much a successor's price has grown since they took over (0 to 1: up to double): a tenth for each stretch the gang spent raiding
    /// since then, the same rule as the outlaw before them (the gang's stretches carry on from the outlaw's).
    /// </summary>
    public double SuccessorGrowth(World world, int index)
    {
        if (!SuccessorActive(world, index)) return 0;
        int at = LwState.For(world).Core.Get(AtKey(index)) - 1;
        if (at < 0) return 0;
        long from = OutlawNews.Period(index, at), now = OutlawNews.Period(index, world.GameHours);
        int n = 0;
        for (long p = from + 1; p <= now && p - from <= 400; p++)
            if (OutlawNews.DoingIn(world.Seed, index, p) == OutlawNews.Raiding) n++;
        return CampRules.Growth(n);
    }

    /// <summary>Whether outlaw <paramref name="index"/>'s successor has taken over and is still alive (their talk and contract).</summary>
    public bool SuccessorActive(World world, int index)
    {
        if (outlaws.FirstOrDefault(x => x.Index == index) is not { Successor: { } sc }) return false;
        return LwState.For(world).Core.Get(UpKey(index)) == 1 && world.Uniques.StateOf(sc.Character) == UniqueState.Alive;
    }

    /// <summary>
    /// Places each successor whose outlaw has been dead or jailed long enough: one of the gang, under a new name, on the same ground (a camp-like
    /// spot out of every player's sight), walking the ground from spot to spot afterwards. Each outlaw is succeeded once.
    /// </summary>
    void Successors(World world, PopulationSystem pop, PopulationData d)
    {
        var core = LwState.For(world).Core;
        var players = PlayerPositions(world);
        towns ??= d.Sites.Select(s => (s.Position.X, s.Position.Z)).ToList();
        foreach (var o in outlaws)
        {
            if (o.Successor is not { } sc || world.Uniques.StateOf(o.Character) == UniqueState.Alive || JoinedPlayer(world, o)) continue;
            int day = (int)(world.GameHours / 24);
            int down = core.Get(DownKey(o.Index));
            if (down == 0)
            {
                core.Set(DownKey(o.Index), day + 1);
                bool bySquad = SquadKilled.Contains(o.Character);
                LwMod.Log($"outlaw successors: {o.Name} is gone (day {day}{(bySquad ? ", killed by the squad" : "")}); {sc.Name} takes over in a few days");
                // killed by someone else: say so, once the squad has heard of them (the squad's own kills are in its record already)
                if (!bySquad && world.Uniques.StateOf(o.Character) == UniqueState.Dead && RecordHost.AtlasList.Contains($"@@w:{o.Name}|", StringComparison.Ordinal))
                    world.Post($"Word on the road: {o.Name} is dead, and not by your hand. Someone else got there first.");
                continue;
            }
            int up = core.Get(UpKey(o.Index));
            if (up == 1)
            {
                // a successor's squad never loaded near the squad is not in Meitou's save: after a load, place it again on the ground (once)
                if (!ReferenceEquals(remadeIn, world)) { remadeSuccessors.Clear(); remadeIn = world; }
                bool there = world.Platoons.All.Any(x => x is not null && !x.Dead && x.TemplateId == sc.Squad);
                // the session's one placing is spent only once the gang is found or placed: a spot the squad stood on used to spend it, and
                // the gang never came back (from a review of the code). Seen and then gone this session: beaten, not placed again.
                if (there) remadeSuccessors.Add(o.Index);
                else if (world.Uniques.StateOf(sc.Character) == UniqueState.Alive && !remadeSuccessors.Contains(o.Index))
                {
                    long per = CampRules.Key(o.Index, world.GameHours, null);
                    int doingNow = CampRules.Doing(world.Seed, o.Index, world.GameHours, null);
                    if (SpotFor(world, d, o, per, doingNow) is { } spot && CampRules.MayMove(spot, spot, players)
                        && Pursuit.Make(world, pop, d, sc.Squad, "lw:succ:" + sc.Name, spot, SpotFor(world, d, o, per + 1, doingNow) ?? spot) is not null)
                    {
                        remadeSuccessors.Add(o.Index);
                        LwMod.Log($"outlaw successors: {sc.Name}'s gang was not in the save; placed again on {o.Name}'s ground");
                    }
                }
                Roam(world, d, o, sc);
                continue;
            }
            if (up != 0 || world.GameHours - (down - 1) * 24.0 < SuccessorDelayHours) continue;
            long period = CampRules.Key(o.Index, world.GameHours, null);
            int doing = CampRules.Doing(world.Seed, o.Index, world.GameHours, null);
            if (SpotFor(world, d, o, period, doing) is not { } at || !CampRules.MayMove(at, at, players)) continue;
            var next = SpotFor(world, d, o, period + 1, doing) ?? at;
            if (Pursuit.Make(world, pop, d, sc.Squad, "lw:succ:" + sc.Name, at, next) is null) { core.Set(UpKey(o.Index), 2); continue; }
            core.Set(UpKey(o.Index), 1);
            core.Set(AtKey(o.Index), (int)world.GameHours + 1);
            world.Post($"Word on the road: {sc.Name} has taken over {o.Name}'s gang.");
            RecapLink.Moment($"{sc.Name} took over {o.Name}'s gang.", "the wastes", 55);
            LwMod.Log($"outlaw successors: {sc.Name} took over {o.Name}'s gang at {at.X:0} {at.Z:0}, day {day}");
        }
    }

    /// <summary>The F10 row: each outlaw that is gone, and where their successor stands.</summary>
    public string SuccessorSummary()
    {
        if (seenWorld is not { } world) return "no world yet";
        var core = LwState.For(world).Core;
        var rows = new List<string>();
        foreach (var o in outlaws)
        {
            if (o.Successor is not { } sc || world.Uniques.StateOf(o.Character) == UniqueState.Alive) continue;
            int down = core.Get(DownKey(o.Index)), up = core.Get(UpKey(o.Index));
            string st = up == 1 ? (world.Uniques.StateOf(sc.Character) == UniqueState.Alive ? "has taken over" : "is gone too")
                : up == 2 ? "could not be placed" : down == 0 ? "not noticed yet"
                : $"takes over in {Math.Max(0, SuccessorDelayHours - (world.GameHours - (down - 1) * 24.0)) / 24:0.0} days";
            rows.Add($"{o.Name} -> {sc.Name}: {st}");
        }
        return rows.Count == 0 ? "no outlaw is gone yet" : string.Join("; ", rows);
    }

    /// <summary>Keeps a placed successor's squad moving about its ground: a new spot each time the unloaded stand-in reaches the last one.</summary>
    void Roam(World world, PopulationData d, CampCode o, SuccessorCode sc)
    {
        if (world.Uniques.StateOf(sc.Character) != UniqueState.Alive) return;
        Platoon? p = null;
        foreach (var x in world.Platoons.All)
            if (x is not null && !x.Dead && x.TemplateId == sc.Squad) { p = x; break; }
        if (p is null || p.State != PlatoonState.Unloaded || (p.Point is { } aim && Vector2.Distance(p.Position, aim) > 500)) return;
        long period = CampRules.Key(o.Index, world.GameHours, null) + (long)(world.Tick / PassEvery % 7);
        if (SpotFor(world, d, o, period, CampRules.Doing(world.Seed, o.Index, world.GameHours, null)) is { } next)
        {
            p.Job = PlatoonJob.PatrolShortRange;       // as Pursuit.Steer: walks to the point (no job: never moves; travel: goes home)
            p.WaitLeft = 0;
            p.Point = new Vector2(next.X, next.Z);
        }
    }

    // ---- growing bounties ----

    readonly Dictionary<int, (long Period, int Count)> raids = [];

    /// <summary>How much the outlaw's price has grown (0 to 1: up to double), from the stretches it spent raiding.</summary>
    public double GrowthOf(World world, int outlaw)
    {
        long now = OutlawNews.Period(outlaw, world.GameHours);
        if (!raids.TryGetValue(outlaw, out var r) || r.Period != now)
            raids[outlaw] = r = (now, CampRules.RaidingStretches(world.Seed, outlaw, world.GameHours));
        return CampRules.Growth(r.Count);
    }

    public int TierOf(World world, int outlaw) => CampRules.Tier(GrowthOf(world, outlaw));

    readonly Dictionary<int, int> tiersSeen = [];

    /// <summary>Raises the live bounty on each loaded outlaw to its grown price (never lowers one; crimes may have raised it further).</summary>
    void RaiseBounties(World world)
    {
        var table = world.Characters;
        foreach (var o in outlaws)
        {
            // a price grown to a fortune is a moment for Recap (tiers 2 and 3; the first pass after a load only notes where each stands)
            int tier = world.Uniques.StateOf(o.Character) == UniqueState.Alive ? TierOf(world, o.Index) : 0;
            // only for an outlaw the squad has heard of (listed in the Atlas, not a blur): an unknown name means nothing in a recap
            bool known = RecordHost.AtlasList.Contains($"@@w:{o.Name}|", StringComparison.Ordinal);
            if (known && tiersSeen.TryGetValue(o.Index, out int was) && tier > was && tier >= 2)
                RecapLink.Moment(tier >= 3 ? $"The price on {o.Name}'s head is now the biggest on any road." : $"{o.Name}'s bounty has grown to a small fortune.",
                    CampTown(o.Index) is int t and > 0 && t <= NewsTowns.Count ? NewsTowns[t - 1] : "the wastes", tier >= 3 ? 60 : 50);
            tiersSeen[o.Index] = tier;

            if (world.Uniques.Find(o.Character) is not { } u || u.Handle.IsNone || !table.TryResolveNext(u.Handle, out int s)
                || table.Cold(s) is not { Bounties: { } ledger } || data?.Db.Find(o.Character) is not { } record) continue;
            int target = (int)Math.Round(record.GetInt("bounty amount") * (1 + GrowthOf(world, o.Index)));
            foreach (var e in ledger.Entries)
            {
                if (e.Claimed || e.Amount >= target) continue;
                if (LwMod.Settings.LogChanges) LwMod.Log($"growing bounty: {o.Name} {e.Amount} -> {target} ({e.FactionName})");
                e.Amount = target;
            }
        }
    }

    /// <summary>Raises the live bounty on each placed successor to its grown price (never lowers one).</summary>
    void RaiseSuccessorBounties(World world)
    {
        var table = world.Characters;
        foreach (var o in outlaws)
        {
            if (o.Successor is not { } sc || !SuccessorActive(world, o.Index)) continue;
            if (world.Uniques.Find(sc.Character) is not { } u || u.Handle.IsNone || !table.TryResolveNext(u.Handle, out int s)
                || table.Cold(s) is not { Bounties: { } ledger } || data?.Db.Find(sc.Character) is not { } record) continue;
            int target = (int)Math.Round(record.GetInt("bounty amount") * (1 + SuccessorGrowth(world, o.Index)));
            foreach (var e in ledger.Entries)
            {
                if (e.Claimed || e.Amount >= target) continue;
                if (LwMod.Settings.LogChanges) LwMod.Log($"growing bounty: {sc.Name} {e.Amount} -> {target} ({e.FactionName})");
                e.Amount = target;
            }
        }
    }

    // ---- a few days' grace at the start ----

    /// <summary>For the first three game days a downed named outlaw does not bleed out (seen in play: in two games out of two, named outlaws
    /// were dead by day 2, before the squad had heard of them). Their blood is kept at 40% of the most seen; a killing blow still kills.</summary>
    public const double GraceHours = 72;

    /// <summary>Unique characters the squad killed this session (Notoriety's kill record): an outlaw's death is the squad's deed or another's.</summary>
    public static readonly HashSet<string> SquadKilled = new(StringComparer.Ordinal);
    readonly Dictionary<string, float> fullBlood = [];
    readonly HashSet<string> graced = [];

    void Grace(World world)
    {
        var table = world.Characters;
        foreach (var o in outlaws)
        {
            if (world.Uniques.Find(o.Character) is not { } u || u.Handle.IsNone || !table.TryResolveNext(u.Handle, out int s)) continue;
            if (table.Cold(s) is not { IsPlayer: false, Medical: { Dead: false } med }) continue;
            float full = fullBlood[o.Character] = Math.Max(fullBlood.GetValueOrDefault(o.Character), med.Blood);
            if (med.Incapacitated && med.Blood < full * 0.4f)
            {
                if (graced.Add(o.Character) && LwMod.Settings.LogChanges) LwMod.Log($"outlaw grace: {o.Name} is down early in the game; kept from bleeding out");
                med.Blood = full * 0.4f;
            }
        }
    }

    public double DaysSinceStruck(World world, int outlaw) => CampRules.DaysSince(book.StruckOf(outlaw), world.GameHours);

    public void SlowWorld(World world)
    {
        Latest = this;
        LastWorld = world;
        seenWorld = world;
        if (Published.Due(world.Tick, 14)) { Shown.Set(() => Summary(null)); ShownSuccessors.Set(SuccessorSummary); }
        byWorld.AddOrUpdate(world, this);
        if (blows.Count > 0) ApplyBlows(world);
        if (world.Tick % 30 == 7 && world.GameHours < GraceHours) Grace(world);
        if (LwMod.Settings.GrowingBounties && world.Tick % PassEvery == 150) { RaiseBounties(world); RaiseSuccessorBounties(world); }
        if (WorldReady.TakeLoaded(world, "reprisals")) reprisalsNow = true;          // just loaded: place parties the save left out now
        if (LwMod.Settings.OutlawReprisals && (world.Tick % PassEvery == 250 || reprisalsNow) && population is not null && data is not null) Reprisals(world, population, data);
        if (LwMod.Settings.OutlawSuccessors && world.Tick % PassEvery == 290 && population is not null && data?.Biomes is not null)
        {
            try { Successors(world, population, data); }
            catch (Exception e) { LwMod.Log($"outlaw successors: skipped a pass after an error: {e.GetBaseException().Message}"); }
        }
        if (!LwMod.Settings.OutlawCamps)
        {
            lastSummary = "camps are off (the Squad record window's Settings tab)";
            return;
        }
        if (world.Tick % PassEvery != 0) return;
        if (population is null || data?.Biomes is null)
        {
            if (!warned) LwMod.Log("outlaw camps: no population system or region map in this world; camps stay off");
            warned = true;
            return;
        }
        try
        {
            Pass(world, population, data);
        }
        catch (Exception e)
        {
            // never risk the game for this feature: switch it off and say why
            LwMod.Settings.OutlawCamps = false;
            LwMod.Log($"outlaw camps: switched off after an error: {e.GetBaseException().Message}");
            world.Post("Wasteland Remembers: outlaw camps hit an error and switched off (details in log.txt).");
        }
    }

    void Pass(World world, PopulationSystem pop, PopulationData d)
    {
        towns ??= d.Sites.Select(s => (s.Position.X, s.Position.Z)).ToList();
        var players = PlayerPositions(world);
        int alive = 0, made = 0, moved = 0;
        foreach (var o in outlaws)
        {
            if (world.Uniques.StateOf(o.Character) != UniqueState.Alive)
            {
                if (book.Remove(o.Index)) LwMod.Log($"outlaw camps: {o.Name} is {world.Uniques.StateOf(o.Character).ToString().ToLowerInvariant()}; the camp is left to the world");
                continue;
            }
            if (JoinedPlayer(world, o))
            {
                if (book.Remove(o.Index)) LwMod.Log($"outlaw camps: {o.Name} rides with you now; no camp");
                continue;
            }
            alive++;
            var shaken = book.StruckOf(o.Index);
            long period = CampRules.Key(o.Index, world.GameHours, shaken);
            int doing = CampRules.Doing(world.Seed, o.Index, world.GameHours, shaken);
            var entry = book.Get(o.Index);
            var platoon = Find(world, o, entry);

            if (platoon is null)
            {
                // a new camp: where this stretch puts it, made only out of every player's sight
                var at = entry is { } e && e.Period == period ? (e.X, e.Z) : SpotFor(world, d, o, period, doing);
                if (at is not { } spot || !CampRules.MayMove(spot, spot, players)) continue;
                if (Make(world, pop, d, o, spot, doing) is null) continue;
                book.Set(o.Index, new CampEntry(spot.X, spot.Z, period, doing));
                made++;
                LwMod.Log($"outlaw camps: {o.Name} made camp at {spot.X:0} {spot.Z:0} ({DoingNames[doing]}, day {world.GameHours / 24:0.0})");
                continue;
            }

            platoon.Persistent = true;
            if (platoon.State == PlatoonState.Loaded)
            {
                Pin(world, platoon, entry);
                continue;
            }
            if (platoon.State != PlatoonState.Unloaded) continue;
            platoon.WaitLeft = HoldStill;
            if (entry is not null && entry.Period == period) continue;

            // a new stretch: move the stand-in if nobody can see either end
            var from = (platoon.Position.X, platoon.Position.Y);
            if (SpotFor(world, d, o, period, doing) is not { } to || !CampRules.MayMove(from, to, players)) continue;
            platoon.Position = new Vector2(to.X, to.Z);
            if (platoon.Stored is null) platoon.Size = CampRules.Size(Planned(world, pop, d, o), doing);
            book.Set(o.Index, new CampEntry(to.X, to.Z, period, doing));
            moved++;
            LwMod.Log($"outlaw camps: {o.Name} moved {Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(to.X, to.Z)):0} to {to.X:0} {to.Z:0} ({DoingNames[doing]}, size {platoon.Size}, day {world.GameHours / 24:0.0})");
        }
        lastSummary = $"{book.All.Count} camps for {alive} living outlaws" + (made + moved > 0 ? $"; this pass made {made}, moved {moved}" : "");
    }

    /// <summary>
    /// The outlaw's camp: a living platoon of its squad template (ids change on load). A persistent one (ours, or one saved as ours) beats
    /// one the engine spawned from the zone lists (which, while the outlaw is ours, holds only henchmen); then the one nearest the last spot.
    /// </summary>
    static Platoon? Find(World world, CampCode o, CampEntry? entry)
    {
        Platoon? best = null;
        (bool, float) bestRank = (false, float.MaxValue);
        foreach (var p in world.Platoons.All)
        {
            if (p is null || p.Dead || p.Imprisoned || p.TemplateId != o.Squad) continue;
            float dist = entry is null ? 0 : Vector2.Distance(p.Position, new Vector2(entry.X, entry.Z));
            var rank = (!IsPersistent(p), dist);
            if (best is null || rank.CompareTo(bestRank) < 0) { best = p; bestRank = rank; }
        }
        return best;
    }

    static bool IsPersistent(Platoon p) => p.Persistent || (p.Saved?.Source.Record.Bools.GetValueOrDefault("persistent") ?? false);

    /// <summary>Whether the outlaw (a recruitable one) is now one of the player's characters.</summary>
    static bool JoinedPlayer(World world, CampCode o)
    {
        if (world.Uniques.Find(o.Character) is not { } entry || entry.Handle.IsNone) return false;
        var table = world.Characters;
        return table.TryResolveNext(entry.Handle, out int slot) && table.Cold(slot) is { IsPlayer: true };
    }

    (float X, float Z)? SpotFor(World world, PopulationData d, CampCode o, long period, int doing)
    {
        var walk = world.Walkability;
        return CampRules.Spot(world.Seed, o.Index, period, doing, CellsOf(d, o), towns!,
            (x, z) => walk.IsWalkable(x, z) && walk.GroundHeight(x, z) - walk.TerrainHeight(x, z) < 5f);
    }

    /// <summary>The region-map cells of the outlaw's regions (worked out once).</summary>
    List<(int X, int Z)> CellsOf(PopulationData d, CampCode o)
    {
        if (cellsOf.TryGetValue(o.Index, out var cells)) return cells;
        cells = [];
        var biomes = d.Biomes!;
        var wanted = new HashSet<int>();
        for (int g = 0; g < biomes.Groups.Count; g++)
            if (o.Biomes.Contains(biomes.Groups[g].Id)) wanted.Add(g);
        for (int cz = 0; cz < CampRules.Cells; cz++)
            for (int cx = 0; cx < CampRules.Cells; cx++)
                if (wanted.Contains(biomes.GroupOf(cx, cz))) cells.Add((cx, cz));
        if (cells.Count == 0) LwMod.Log($"outlaw camps: no map cells for {o.Name}'s regions; no camp");
        cellsOf[o.Index] = cells;
        return cells;
    }

    static ulong KeyOf(CampCode o) => Rng.StableHash("lw:camp:" + o.Name);

    static int Planned(World world, PopulationSystem pop, PopulationData d, CampCode o)
    {
        var record = d.Db.Find(o.Squad);
        return record is null ? 0
            : SquadFactory.Plan(d.Db, SquadTemplate.From(record), pop.Settings.SquadSizeMultiplier, world.Seed, KeyOf(o), pop.States).TotalMembers;
    }

    /// <summary>A new persistent, held-still stand-in for the outlaw's squad (the same steps the engine and Guild & Quests use).</summary>
    static Platoon? Make(World world, PopulationSystem pop, PopulationData d, CampCode o, (float X, float Z) at, int doing)
    {
        var record = d.Db.Find(o.Squad);
        if (record is null)
        {
            LwMod.Log($"outlaw camps: squad record {o.Squad} for {o.Name} is not in the game data; no camp");
            return null;
        }
        var template = SquadTemplate.From(record);
        var plan = SquadFactory.Plan(d.Db, template, pop.Settings.SquadSizeMultiplier, world.Seed, KeyOf(o), pop.States);
        var (package, job) = AiCompat.JobsFor(d.Ai, template);
        var platoon = new Platoon
        {
            Id = world.Platoons.NextId(),
            Origin = Pursuit.OriginFor(d, at.X, at.Z, template.Faction is { } f0 ? d.FactionIndex(f0) : -1),
            TemplateId = template.Id,
            Faction = template.Faction is { } f ? d.FactionIndex(f) : -1,
            Key = KeyOf(o),
            Size = CampRules.Size(plan.TotalMembers, doing),
            Position = new Vector2(at.X, at.Z),
            Package = package?.Id,
            Job = job,
            Persistent = true,
            WaitLeft = HoldStill,
        };
        world.Platoons.Add(platoon);
        return platoon;
    }

    /// <summary>Once a camp is loaded, keep its people at the spot instead of letting them wander (as Guild & Quests does).</summary>
    void Pin(World world, Platoon platoon, CampEntry? entry)
    {
        if (entry is null || world.Squads.Find(platoon.SquadId) is not { } squad) return;
        if (pinned.TryGetValue(platoon.Id, out int done) && done == squad.Id) return;
        pinned[platoon.Id] = squad.Id;
        squad.HomeCentre = new Vector2(entry.X, entry.Z);
        squad.HomeRadius = HomeRadius;
    }

    static List<(float X, float Z)> PlayerPositions(World world)
    {
        var list = new List<(float X, float Z)>();
        var table = world.Characters;
        var hot = table.Previous;
        for (int s = 0; s < hot.Length; s++)
        {
            if (!hot[s].Alive || table.Cold(s) is not { IsPlayer: true, Animal: null } cold || cold.Medical is { Dead: true }) continue;
            list.Add((hot[s].Position.X, hot[s].Position.Z));
        }
        return list;
    }

    /// <summary>The F10 row's line; each camp's outlaw, activity and spot go to log.txt.</summary>
    public readonly Published Shown = new(), ShownSuccessors = new();   // the F10 rows' lines

    // A camp's line goes to log.txt when it changes, not every time the row is refreshed: logged each refresh, 36 camps filled a play
    // test's log.txt and the play-test timeline (4,095 of its 4,096 rows, 386,673 older events lost; build bf).
    readonly Dictionary<int, string> campLogged = [];

    public string Summary(World? world)
    {
        foreach (var o in outlaws)
        {
            if (book.Get(o.Index) is not { } e) continue;
            var (cx, cz) = CampRules.CellOf(e.X, e.Z);
            string line = $"outlaw camp: {o.Name}, {DoingNames[e.Doing]}, at {e.X:0} {e.Z:0} (map cell {cx},{cz}), stretch {e.Period}";
            if (campLogged.TryGetValue(o.Index, out var last) && last == line) continue;
            campLogged[o.Index] = line;
            LwMod.Log(line);
        }
        if (!LwMod.Settings.OutlawReprisals) return lastSummary;
        var senders = book.Reprisals.Select(r => outlaws.FirstOrDefault(o => o.Index == r.Outlaw)?.Name ?? "?").ToList();
        return lastSummary + (senders.Count == 0 ? "; no outlaw has sent men after you" : $"; men out after you from {string.Join(", ", senders)}");
    }

    public void Write(Stream stream) { using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true); book.Write(w); }

    public void Read(Stream stream) { using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true); book.Read(r); }

    public void Hash(ref StateHasher h)
    {
        h.Add(book.Struck.Count);
        foreach (var (k, st) in book.Struck) { h.Add(k); h.Add(st.Hours); h.Add(st.Bloodied ? 1 : 0); }
        h.Add(book.All.Count);
        foreach (var (k, e) in book.All) { h.Add(k); h.Add(e.X); h.Add(e.Z); h.Add(e.Period); h.Add(e.Doing); }
    }
}
