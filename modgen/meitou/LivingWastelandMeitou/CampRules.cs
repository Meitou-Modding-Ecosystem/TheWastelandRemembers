// Moving outlaw camps, the pure part (no engine types; unit-tested): where a camp goes, how big it is, when it may move, and the book
// of camps the helper keeps per world. The engine part is OutlawCamps.cs; the design is modgen/OUTLAWS_LIVE_DESIGN.md.
namespace LivingWasteland.MeitouMod;

public static class CampRules
{
    // ---- when another mod may strike an outlaw (WastelandApi.StrikableAfter / StrikeAnswer, RivalryCore#59 decision 17, #126) ----

    /// <summary>Game hours an outlaw is left alone after rising (or a successor taking over): 7 game days.</summary>
    public const double StrikeWaitHours = 7 * 24;

    /// <summary>Game hours between two strikes by other mods on the same outlaw: 30 game days.</summary>
    public const double StrikeCooldownHours = 30 * 24;

    /// <summary>
    /// From when another mod may strike an outlaw, in game hours: <see cref="StrikeWaitHours"/> after they rose and
    /// <see cref="StrikeCooldownHours"/> after the last such strike, whichever is later. -1 when never (dead, jailed, the player's).
    /// </summary>
    public static double StrikableAfter(string status, double roseHours, double? lastOtherStrike) =>
        status is "alive" or "succeeded"
            ? Math.Max(roseHours + StrikeWaitHours, lastOtherStrike is { } last ? last + StrikeCooldownHours : double.MinValue)
            : -1;

    /// <summary>
    /// Whether another mod may strike an outlaw now: "yes", or why not: "dead", "jailed", "players" (never); "not-known" (the squad hasn't
    /// heard of them), "too-soon" (they rose under 7 days ago), "cooldown" (struck by another mod under 30 days ago), "wounded" (already
    /// licking their wounds), "story" (their men are out after the squad). Checked in that order.
    /// </summary>
    public static string StrikeAnswer(string status, bool known, string mood, bool storyUnderway, double hoursNow, double roseHours, double? lastOtherStrike)
    {
        switch (status)
        {
            case "dead": return "dead";
            case "jailed": return "jailed";
            case "joined": return "players";
            case not ("alive" or "succeeded"): return "dead";
        }
        if (!known) return "not-known";
        if (hoursNow < roseHours + StrikeWaitHours) return "too-soon";
        if (lastOtherStrike is { } last && hoursNow < last + StrikeCooldownHours) return "cooldown";
        if (mood == "wounded") return "wounded";
        if (storyUnderway) return "story";
        return "yes";
    }

    /// <summary>Meitou's region map: 64 x 64 cells of 4608 units, cell 32 starting at 0 (WeatherAreas.CellRect).</summary>
    public const float CellSize = 4608f;
    public const int Cells = 64, Origin = 32;

    /// <summary>How far from the nearest town each activity's camp may be (min, max). Raiders sit near the towns and roads they rob,
    /// those lying low far away, the rest in between.</summary>
    public static (float Min, float Max) TownDistance(int doing) => doing switch
    {
        OutlawNews.Raiding => (3500f, 10000f),            // 350 m to 1 km (world units are 0.1 m)
        OutlawNews.LyingLow or OutlawNews.Wounded => (25000f, float.MaxValue),  // 2.5 km or more (licking wounds: as far off)
        _ => (10000f, float.MaxValue),                    // 1 km or more
    };

    /// <summary>Tries before a spot is given up for this pass (each costs one walkability look).</summary>
    public const int Tries = 48;

    /// <summary>No player character may be this close to a camp's old or new spot when it moves (three region cells, about 1.4 km: the
    /// zones around the player are loaded, and a camp must never appear or vanish in sight). World units are 0.1 m.</summary>
    public const float MoveClearance = 3 * CellSize;

    /// <summary>The camp's size for an activity: gathering men adds two, licking wounds takes one, never below two (leader and one).</summary>
    public static int Size(int planned, int doing) => doing switch
    {
        OutlawNews.Gathering => planned + 2,
        OutlawNews.Wounded => Math.Max(2, planned - 1),
        _ => Math.Max(planned, 1),
    };

    public static (float MinX, float MinZ) CellCorner(int cx, int cz) => ((cx - Origin) * CellSize, (cz - Origin) * CellSize);

    public static (int X, int Z) CellOf(float x, float z) => ((int)MathF.Floor(x / CellSize) + Origin, (int)MathF.Floor(z / CellSize) + Origin);

    /// <summary>
    /// A spot for an outlaw's camp in one stretch: a point in one of the outlaw's region cells, on dry ground, at the activity's distance
    /// from the nearest town. The same seed, outlaw, stretch and activity always give the same spot (or none), so nothing but the last spot
    /// needs saving. If no try fits the distance rule, the first dry spot anywhere in the regions is used; null if there is none.
    /// </summary>
    public static (float X, float Z)? Spot(ulong seed, int outlaw, long period, int doing, IReadOnlyList<(int X, int Z)> cells,
        IReadOnlyList<(float X, float Z)> towns, Func<float, float, bool> dry)
    {
        if (cells.Count == 0) return null;
        var (min, max) = TownDistance(doing);
        (float X, float Z)? fallback = null;
        for (int i = 0; i < Tries; i++)
        {
            ulong h = Hash(seed, (ulong)outlaw, (ulong)period, (ulong)i);
            var (cx, cz) = cells[(int)(h % (ulong)cells.Count)];
            var (x0, z0) = CellCorner(cx, cz);
            // keep a margin from the cell's edge so the spot is inside the region, not on its border
            float x = x0 + CellSize * (0.1f + 0.8f * Unit(h >> 8));
            float z = z0 + CellSize * (0.1f + 0.8f * Unit(h >> 32));
            if (!dry(x, z)) continue;
            float d = NearestTown(x, z, towns);
            if (d >= min && d <= max) return (x, z);
            if (d >= 3000f) fallback ??= (x, z);    // never inside a town, even as a fallback
        }
        return fallback;
    }

    public static float NearestTown(float x, float z, IReadOnlyList<(float X, float Z)> towns)
    {
        float best = float.MaxValue;
        foreach (var (tx, tz) in towns) best = Math.Min(best, MathF.Sqrt((tx - x) * (tx - x) + (tz - z) * (tz - z)));
        return best;
    }

    /// <summary>Whether a camp may move from one spot to another: no player character near either.</summary>
    public static bool MayMove((float X, float Z) from, (float X, float Z) to, IReadOnlyList<(float X, float Z)> players)
    {
        foreach (var (px, pz) in players)
        {
            if (Near(px, pz, from) || Near(px, pz, to)) return false;
        }
        return true;

        static bool Near(float px, float pz, (float X, float Z) p) => (px - p.X) * (px - p.X) + (pz - p.Z) * (pz - p.Z) < MoveClearance * MoveClearance;
    }

    // ---- outlaws who react: struck by the squad and still alive ----

    /// <summary>How long an outlaw stays shaken after the squad struck its people (two activity stretches, about five game days).</summary>
    public const double ReactFor = 2 * OutlawNews.Stretch;

    public static bool Shaken(StruckMark? s, double hours) => s is not null && hours >= s.Hours && hours - s.Hours < ReactFor;

    /// <summary>What the outlaw is doing: licking wounds after a bloody beating, lying low after a scare, else the news roll.</summary>
    public static int Doing(ulong seed, int outlaw, double hours, StruckMark? s) =>
        Shaken(s, hours) ? (s!.Bloodied ? OutlawNews.Wounded : OutlawNews.LyingLow) : OutlawNews.Doing(seed, outlaw, hours);

    /// <summary>The key of the camp's current spot: the news stretch, or (while shaken) the time of the blow, so a beating moves the camp at once.</summary>
    public static long Key(int outlaw, double hours, StruckMark? s) =>
        Shaken(s, hours) ? -1 - (long)Math.Floor(s!.Hours) : OutlawNews.Period(outlaw, hours);

    /// <summary>Game days since the squad struck the outlaw's people (a very large number when it never has).</summary>
    public static double DaysSince(StruckMark? s, double hours) => s is null ? 1e6 : Math.Max(0, (hours - s.Hours) / 24);

    // ---- gossip that points: which town a camp is nearest, and which way ----

    /// <summary>Closer than this (150 m) a camp is "right by" its town.</summary>
    public const float RightBy = 1500f;

    /// <summary>1 north, 2 north-east, 3 east, 4 south-east, 5 south, 6 south-west, 7 west, 8 north-west (from the town to the camp; +x is east,
    /// +z south, as in Guild & Quests' direction words); 9 right by the town.</summary>
    public static int Direction(float townX, float townZ, float x, float z)
    {
        float dx = x - townX, dz = z - townZ;
        if (dx * dx + dz * dz < RightBy * RightBy) return 9;
        double compass = Math.Atan2(dx, -dz) * 180 / Math.PI;          // 0 north, 90 east
        return (int)Math.Round(((compass % 360) + 360) % 360 / 45) % 8 + 1;
    }

    /// <summary>The place in <paramref name="towns"/> nearest a point (-1 when the list is empty).</summary>
    public static int NearestIndex(float x, float z, IReadOnlyList<(float X, float Z)> towns)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < towns.Count; i++)
        {
            float d = (towns[i].X - x) * (towns[i].X - x) + (towns[i].Z - z) * (towns[i].Z - z);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    // ---- growing bounties ----

    /// <summary>How many whole stretches the outlaw has spent raiding since the world began (the news roll; a shaken spell is not counted
    /// separately, it is short and rare).</summary>
    public static int RaidingStretches(ulong seed, int outlaw, double hours)
    {
        long now = OutlawNews.Period(outlaw, hours);
        int n = 0;
        // from the game's start (the stretch running at hour 0 is the first that counts): a new world starts every price at its base
        for (long p = Math.Max(OutlawNews.Period(outlaw, 0) + 1, now - 400); p < now; p++)   // 400 stretches = 1,000 game days, plenty
            if (OutlawNews.DoingIn(seed, outlaw, p) == OutlawNews.Raiding) n++;
        return n;
    }

    /// <summary>How much an outlaw's price has grown: a tenth per raiding stretch, at most the whole price again (double).</summary>
    public static double Growth(int raidingStretches) => Math.Min(1.0, 0.1 * raidingStretches);

    /// <summary>0 no talk yet, 1 the price is climbing (+20%), 2 a big price (+50%), 3 one of the biggest (+90%).</summary>
    public static int Tier(double growth) => growth >= 0.9 ? 3 : growth >= 0.5 ? 2 : growth >= 0.2 ? 1 : 0;

    // ---- contracts that pay by distance ----

    /// <summary>World units in a kilometre (world units are 0.1 m).</summary>
    public const float Km = 10000f;

    /// <summary>The extra pay for a contract on an outlaw <paramref name="distance"/> world units from the giver: nothing within 5 km,
    /// rising evenly to the whole reward again (double pay) at 25 km and beyond.</summary>
    public static int DistanceBonus(int reward, float distance) =>
        (int)Math.Round(reward * Math.Clamp((distance - 5 * Km) / (20 * Km), 0, 1));

    /// <summary>The length of a route through these points, in world units (quests paid by distance: start, each step, the hand-in).</summary>
    public static float RouteLength(IReadOnlyList<(float X, float Z)> points)
    {
        float sum = 0;
        for (int i = 1; i < points.Count; i++)
            sum += MathF.Sqrt((points[i].X - points[i - 1].X) * (points[i].X - points[i - 1].X) + (points[i].Z - points[i - 1].Z) * (points[i].Z - points[i - 1].Z));
        return sum;
    }

    /// <summary>Whether an outlaw <paramref name="distance"/> away is within a giver's reach (0 km means anywhere).</summary>
    public static bool InReach(float distance, int reachKm) => reachKm <= 0 || distance <= reachKm * Km;

    /// <summary>The centre of a region-map cell.</summary>
    public static (float X, float Z) CellCentre(int cx, int cz) => ((cx - Origin + 0.5f) * CellSize, (cz - Origin + 0.5f) * CellSize);

    static float Unit(ulong h) => (h & 0xFFFFFF) / (float)0x1000000;

    static ulong Hash(ulong a, ulong b, ulong c, ulong d)
    {
        ulong h = Mix(a ^ 0x4C57_43414D50UL);                 // "LWCAMP"
        h = Mix(h ^ (b + 1) * 0x9E3779B97F4A7C15UL);
        h = Mix(h ^ (c + 1) * 0xC2B2AE3D27D4EB4FUL);
        return Mix(h ^ (d + 1) * 0x165667B19E3779F9UL);
    }

    static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

/// <summary>When the squad last struck an outlaw's people (the outlaw lived) and whether it drew blood (someone downed or killed).</summary>
public sealed record StruckMark(double Hours, bool Bloodied);

/// <summary>One managed camp: where it was last put, for which stretch and activity.</summary>
public sealed record CampEntry(float X, float Z, long Period, int Doing);

/// <summary>The camps of one world, by outlaw index. Saved and hashed; platoon ids are not kept (they change on every load).</summary>
public sealed class CampBook
{
    const int Format = 3;
    readonly SortedDictionary<int, CampEntry> camps = [];
    readonly SortedDictionary<int, StruckMark> struck = [];

    public IReadOnlyDictionary<int, StruckMark> Struck => struck;

    readonly List<Reprisal> reprisals = [];
    readonly SortedDictionary<int, double> answered = [];       // outlaw -> the beating (its hour) already answered with a party

    public IReadOnlyList<Reprisal> Reprisals => reprisals;

    public double? AnsweredOf(int outlaw) => answered.TryGetValue(outlaw, out var h) ? h : null;

    public void Sent(Reprisal r, double beating) { reprisals.Add(r); answered[r.Outlaw] = beating; }

    /// <summary>Marks a beating as answered without a reprisal (another mod's war band struck the camp: not the squad's doing).</summary>
    public void Answer(int outlaw, double beating) => answered[outlaw] = beating;

    public void Update(Reprisal old, Reprisal now) { int i = reprisals.IndexOf(old); if (i >= 0) reprisals[i] = now; }

    public void Done(Reprisal r) => reprisals.Remove(r);

    public StruckMark? StruckOf(int outlaw) => struck.TryGetValue(outlaw, out var s) ? s : null;

    /// <summary>The squad struck the outlaw's people. While still shaken a new blow only adds blood (the first blow's time stands, so the camp
    /// does not move again and again during one fight); after that it starts a new reaction. True when this began a new one.</summary>
    public bool Strike(int outlaw, double hours, bool bloodied)
    {
        var old = StruckOf(outlaw);
        if (CampRules.Shaken(old, hours))
        {
            if (bloodied && !old!.Bloodied) struck[outlaw] = old with { Bloodied = true };
            return false;
        }
        struck[outlaw] = new StruckMark(hours, bloodied);
        return true;
    }

    public IReadOnlyDictionary<int, CampEntry> All => camps;

    public CampEntry? Get(int outlaw) => camps.TryGetValue(outlaw, out var e) ? e : null;

    public void Set(int outlaw, CampEntry entry) => camps[outlaw] = entry;

    public bool Remove(int outlaw) => camps.Remove(outlaw);

    public void Write(BinaryWriter w)
    {
        w.Write(Format);
        w.Write(camps.Count);
        foreach (var (k, e) in camps)
        {
            w.Write(k); w.Write(e.X); w.Write(e.Z); w.Write(e.Period); w.Write(e.Doing);
        }
        w.Write(struck.Count);
        foreach (var (k, s) in struck) { w.Write(k); w.Write(s.Hours); w.Write(s.Bloodied); }
        w.Write(reprisals.Count);
        foreach (var r in reprisals) { w.Write(r.Outlaw); w.Write(r.SentHours); w.Write(r.X); w.Write(r.Z); }
        w.Write(answered.Count);
        foreach (var (k, h) in answered) { w.Write(k); w.Write(h); }
    }

    public void Read(BinaryReader r)
    {
        camps.Clear();
        struck.Clear();
        reprisals.Clear();
        answered.Clear();
        if (r.BaseStream.Position >= r.BaseStream.Length) return;    // a save from before camps existed
        int format = r.ReadInt32();
        if (format is not (1 or 2 or Format)) return;
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            int k = r.ReadInt32();
            camps[k] = new CampEntry(r.ReadSingle(), r.ReadSingle(), r.ReadInt64(), r.ReadInt32());
        }
        if (format < 2) return;
        for (int count = r.ReadInt32(), i = 0; i < count; i++) { int k = r.ReadInt32(); struck[k] = new StruckMark(r.ReadDouble(), r.ReadBoolean()); }
        if (format < 3) return;
        for (int count = r.ReadInt32(), i = 0; i < count; i++) reprisals.Add(new Reprisal(r.ReadInt32(), r.ReadDouble(), r.ReadSingle(), r.ReadSingle()));
        for (int count = r.ReadInt32(), i = 0; i < count; i++) { int k = r.ReadInt32(); answered[k] = r.ReadDouble(); }
    }
}
