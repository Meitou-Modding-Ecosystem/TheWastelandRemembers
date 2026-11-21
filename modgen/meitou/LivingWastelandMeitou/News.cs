// News from other mods (Meitou Worldbuilding issue #23; WastelandApi.Report). Another mod reports what happened (a town fell, a siege began,
// a caravan was lost); the world then talks about it the way it talks about the squad's own deeds:
// * townsfolk and bar patrons near the place say it (three news slots: word swaps /LWNEWS_k/ carry the text, a condition per slot lets
//   only speakers within the "Outlaw news range" of the place say it; content/news.json, gossip slider and town criers included);
// * when the squad first comes within that range, it has heard: a line in History, a Recap moment, the Atlas's News section, and for
//   weighty news a message;
// * the same news reported twice (by two mods, or one mod twice) is one piece of news (NewsBoard).
using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public sealed record NewsTable(List<int> Codes, List<string> Tokens);

public sealed class NewsSystem(PopulationData? data) : ITickSystem, IModSaveData, IStateHashed
{
    public const int PostAtWeight = 60;          // news this weighty is also posted as a message when the squad hears it
    const int PassEvery = 60;
    public static List<string> Tokens = [];

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, NewsSystem> byWorld = new();
    public static NewsSystem? For(World world) => byWorld.TryGetValue(world, out var s) ? s : null;
    internal static NewsSystem? Latest;

    readonly NewsBoard board = new();

    /// <summary>Test reports from the F10 menu (play-test builds): taken on the next pass, inside the tick.</summary>
    public static readonly System.Collections.Concurrent.ConcurrentQueue<(string Kind, string Place)> TestReports = new();
    List<NewsItem> talked = [];
    readonly string?[] shown = new string?[NewsBoard.Slots];
    World? seen;
    NightlyHooks.AskSession? asks;

    /// <summary>A report from another mod (WastelandApi.Report). False, with the reason logged, when it is not taken.</summary>
    public bool Report(string kind, string place, string faction, string text, int weight, string source)
    {
        if (!LwMod.Settings.OtherModsNews || seen is null || data is null) return false;
        if (SiteNamed(place) is not { } site)
        {
            LwMod.Log($"news: not taken from {source}: no town called \"{place}\"");
            return false;
        }
        return AcceptReport(site, place, kind, faction, text, weight, source);
    }

    public bool ReportAtNode(string node, string kind, string faction, string text, int weight, string source) =>
        SiteOf(node) is { } site && AcceptReport(site, site.Town.Name, kind, faction, text, weight, source);

    bool AcceptReport(TownSite site, string place, string kind, string faction, string text, int weight, string source)
    {
        if (!LwMod.Settings.OtherModsNews || seen is not { } world || source.Trim() is NewsBoard.TownSource or PrisonerSource) return false;
        var item = board.Accept(kind, place, faction, text, source, weight, world.GameHours, site.Position.X, site.Position.Z, out var why, NodeOf(site));
        if (item is null)
        {
            LwMod.Log($"news: not taken from {source} ({kind}, {place}): {why}");
            return false;
        }
        LwMod.Log($"news: {source} reports {item.Kind} at {item.Place}: \"{item.Text}\" (weight {item.Weight})");
        Refresh(world);
        return true;
    }

    const string OutpostSource = "TWR outpost history";
    const string PrisonerSource = "TWR prisoner cases";

    static bool Enabled(NewsItem item) => item.Source switch
    {
        NewsBoard.TownSource => LwMod.Settings.TownHandovers,
        OutpostSource => LwMod.Settings.RememberOutposts,
        PrisonerSource => LwMod.Settings.PrisonerCases,
        _ => LwMod.Settings.OtherModsNews,
    };

    internal void TownChanged(World world, int index, string? from)
    {
        if (!LwMod.Settings.TownHandovers || data is null || index < 0 || index >= data.Sites.Count || index >= data.SiteFactions.Count)
            return;
        var site = data.Sites[index];
        var oldOwner = from is null ? site.Record.Faction : data.Db.Find(from)?.GetReferences("faction").FirstOrDefault().TargetStringId;
        int current = data.SiteFactions[index];
        if (string.IsNullOrEmpty(oldOwner) || current < 0 || current >= data.Factions.Count || data.FactionIndex(oldOwner) == current)
            return;
        string node = NodeOf(site);
        if (node.Length == 0) return;
        string place = site.Town.Name, faction = data.Factions[current].Name;
        string line = $"{place} is held by {KeepsakeText.Faction(faction)} now.";
        if (board.Accept("town-fell", place, faction, line, NewsBoard.TownSource, 65, world.GameHours,
            site.Position.X, site.Position.Z, out _, node) is not null)
        {
            if (PlaytestLog.Enabled) PlaytestLog.Event(world, "town handover", node, "observed", $"{oldOwner} -> {data.Factions[current].Id}");
            LwMod.Log($"news: recorded a town handover at {node}");
        }
    }

    internal bool ReportOutpost(string key, string place, Vector3 at, string text)
    {
        if (!LwMod.Settings.RememberOutposts || seen is not { } world) return false;
        var report = board.Accept("first-defence:" + key, place, "", text, OutpostSource, 50,
            world.GameHours, at.X, at.Z, out _);
        if (report is null) return false;
        Refresh(world);
        return true;
    }

    internal void ReportPrisoner(string node, int episode, string outcome, string text, int weight)
    {
        if (!LwMod.Settings.PrisonerCases || seen is not { } world || SiteOf(node) is not { } site) return;
        var report = board.Accept($"custody:{episode}:{outcome}", site.Town.Name, "", text, PrisonerSource, weight,
            world.GameHours, site.Position.X, site.Position.Z, out _, node);
        if (report is not null) Refresh(world);
    }

    internal NewsItem? CourierReport(double now, int? id = null) =>
        board.Items.Where(n => Enabled(n) && !n.Heard && n.Source.Length > 0 && n.Source != "F10 test" &&
            n.Kind is ("town-fell" or "siege-broken" or "caravan-lost") &&
            n.Hours <= now && now - n.Hours < NewsBoard.LiveDays * 24 &&
            (id is null || n.Id == id)).OrderByDescending(n => n.Weight).ThenByDescending(n => n.Hours).ThenBy(n => n.Id).FirstOrDefault();

    internal bool DeliverCourierReport(World world, NewsItem report)
    {
        if (CourierReport(world.GameHours, report.Id) != report) return false;
        HearOne(world, report);
        return true;
    }

    internal static string NodeOf(TownSite site) => string.IsNullOrEmpty(site.InstanceId) ? "" : "town:" + site.InstanceId;

    TownSite? SiteOf(string node) => node.StartsWith("town:", StringComparison.Ordinal) && node.Length > 5
        ? data?.Sites.FirstOrDefault(site => NodeOf(site) == node) : null;

    internal int CourierOrigin(NewsItem report)
    {
        if (data is null) return -1;
        for (int i = 0; i < data.Sites.Count; i++)
        {
            var site = data.Sites[i];
            if (report.Node.Length > 0 ? NodeOf(site) == report.Node : site.Position.X == report.X && site.Position.Z == report.Z)
                return i;
        }
        return -1;
    }

    TownSite? SiteNamed(string place)
    {
        if (data is null || string.IsNullOrWhiteSpace(place)) return null;
        for (int i = 0; i < data.Sites.Count; i++)
            if (string.Equals(data.Sites[i].Town.Name, place.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(data.Sites[i].Record.Name, place.Trim(), StringComparison.OrdinalIgnoreCase))
                return data.Sites[i];
        return null;
    }

    public void SlowWorld(World world)
    {
        Latest = this;
        if (!ReferenceEquals(seen, world))
        {
            seen = world;
            byWorld.AddOrUpdate(world, this);
            NightlyHooks.Marks = [];
            Refresh(world);
            asks = new NightlyHooks.AskSession(data?.Sites.Select(s => (s.Position.X, s.Position.Z)).ToArray() ?? []);
            NightlyHooks.CurrentAsks = asks;
        }
        if (Published.Due(world.Tick, 13)) Shown.Set(Summary);
        if (world.Tick % PassEvery != 40) return;
        try
        {
            while (asks is { } session && session.Asks.TryDequeue(out var who)) if (LwMod.Settings.AskForNews) AnswerAsk(world, who);   // not if switched off since the click
            NightlyHooks.Marks = LwMod.Settings.MapMarks ? Marks(world) : [];
        }
        catch (Exception e) { LwMod.Log($"news: skipped asks and map marks after an error: {e.GetBaseException().Message}"); }
        if (!LwMod.Settings.OtherModsNews && !LwMod.Settings.RememberOutposts && !LwMod.Settings.TownHandovers) return;
        try
        {
            while (TestReports.TryDequeue(out var t)) Report(t.Kind, t.Place, "", "", 70, "F10 test");
            board.Prune(world.GameHours);
            Refresh(world);
            Hear(world);
        }
        catch (Exception e) { LwMod.Log($"news: skipped a pass after an error: {e.GetBaseException().Message}"); }
    }

    /// <summary>The slots' texts: what the towns talk about now.</summary>
    void Refresh(World world)
    {
        talked = board.Talked(world.GameHours, Enabled);
        if (data is null) return;
        for (int k = 0; k < Tokens.Count && k < NewsBoard.Slots; k++)
        {
            string text = k < talked.Count ? talked[k].Text : "Nothing much, these days.";
            if (shown[k] == text) continue;
            shown[k] = text;
            WordSwap.Set(data, Tokens[k], text);
        }
    }

    /// <summary>News the squad has come within reach of is heard: History, Recap, the Atlas, a message for weighty news.</summary>
    void Hear(World world)
    {
        var players = Pursuit.Players(world);
        if (players.Count == 0) return;
        int km = Math.Max(1, LwMod.Settings.NewsReach);
        foreach (var n in board.Items.Where(n => Enabled(n) && !n.Heard && world.GameHours - n.Hours <= NewsBoard.LiveDays * 24))
        {
            bool heard = n.Source == NewsBoard.TownSource
                ? players.Any(p => WitnessesTown(p.X, p.Z, n))
                : players.Any(p => CampRules.InReach(Vector2.Distance(new Vector2(p.X, p.Z), new Vector2(n.X, n.Z)), km));
            if (!heard) continue;
            HearOne(world, n);
        }
    }

    bool WitnessesTown(float x, float z, NewsItem report)
    {
        if (data is null || data.TownAt(new Vector3(x, 0, z)) is not (>= 0 and var index)
            || index >= data.Sites.Count || index >= data.SiteFactions.Count || NodeOf(data.Sites[index]) != report.Node)
            return false;
        int faction = data.SiteFactions[index];
        return faction >= 0 && faction < data.Factions.Count && data.Factions[faction].Name == report.Faction
            && !board.Items.Any(n => n.Source == NewsBoard.TownSource && n.Node == report.Node && n.Id > report.Id);
    }

    void HearOne(World world, NewsItem n)
    {
        n.Heard = true;
        n.HeardHours = world.GameHours;
        board.RememberHandover(n);
        // another mod may have noted the same words already (Proving Grounds notes the squad's own arena wins and reports them as news too)
        if (LwDeeds.For(world)?.Record is { } record &&
            (n.Source == NewsBoard.TownSource || !record.History.Any(h => h.Text == n.Text && world.GameHours - h.Hours < 24)))
        {
            record.Note(n.Text, n.HeardHours, n.Place);
            RecapLink.Moment(n.Text, n.Place, Math.Clamp(n.Weight, 30, 80), new Vector3(n.X, 0, n.Z));
        }
        if (n.Weight >= PostAtWeight) world.Post("Word on the road: " + n.Text);
        if (PlaytestLog.Enabled) PlaytestLog.Event(world, "news", $"report:{n.Id}", "heard", $"{n.Source} at {n.Place}: {n.Text}");
        LwMod.Log($"news: the squad heard \"{n.Text}\"");
        LogTally.Add("news heard");
    }

    /// <summary>"Ask for news" chosen on someone (NightlyHooks): they say the weightiest live news within reach of them, and the squad has
    /// heard it.</summary>
    public void AnswerAsk(World world, CharacterId who)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(who, out int s) || !table.Next[s].Alive || table.Cold(s) is not { IsPlayer: false, Animal: null } cold
            || cold.Medical is { Dead: true } or { Incapacitated: true }) return;
        var at = table.Next[s].Position;
        var live = board.Items.Where(n => Enabled(n) && world.GameHours - n.Hours <= NewsBoard.LiveDays * 24 && n.Hours <= world.GameHours + 1).ToList();
        var squad = new List<string>();
        var hot = table.Next;
        for (int k = 0; k < hot.Length; k++)
            if (hot[k].Alive && table.Cold(k) is { IsPlayer: true, Animal: null } pc) squad.Add(pc.Name);
        var (line, i) = NewsAskRules.Answer(live.Select(n => (n.Text, n.X, n.Z, n.Weight, n.Hours)).ToList(), at.X, at.Z,
            Math.Max(1, LwMod.Settings.NewsReach) * 10000f, (ulong)world.Tick ^ (ulong)s, squad,
            live.Select(n => toldAt.TryGetValue(n, out double h) ? h : double.MinValue).ToList(), world.GameHours);
        if (i >= 0) toldAt[live[i]] = world.GameHours;
        foreach (var gone in toldAt.Keys.Where(k => !board.Items.Contains(k)).ToList()) toldAt.Remove(gone);
        world.Bark(who, line);
        if (i >= 0 && !live[i].Heard) HearOne(world, live[i]);
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "news", i >= 0 ? $"report:{live[i].Id}" : "", "asked", $"{cold.Name}: {line}");
        LwMod.Log($"ask for news: {cold.Name} said \"{line}\"");
    }

    /// <summary>When each piece of news was last told to the squad by someone asked (not saved: a load starts afresh).</summary>
    readonly Dictionary<NewsItem, double> toldAt = [];

    /// <summary>The map marks (NightlyHooks): rivals about, known outlaw camps, news heard this week.</summary>
    List<MapMark> Marks(World world) => MapMarkRules.Build(
        RivalsSystem.For(world)?.Told().Select(r => (r.Name, r.Phase, r.X, r.Z, r.Grudge)) ?? [],
        OutlawCampsSystem.For(world)?.KnownCamps(world) ?? [],
        board.Items.Where(n => n.Heard && (n.Source is not (OutpostSource or NewsBoard.TownSource or PrisonerSource) || Enabled(n))).Select(n => (n.Text, n.Place, n.X, n.Z, n.HeardHours)), world.GameHours);

    /// <summary>What a speaker's news condition sees for slot k: 1 when that news is live and the speaker is within reach of its place.</summary>
    public int SlotValue(World world, int slot, CharacterId speaker)
    {
        if (slot >= talked.Count || !Enabled(talked[slot]) || !world.Characters.TryResolveNext(speaker, out int s)) return 0;
        var n = talked[slot];
        var at = world.Characters.Next[s].Position;
        return CampRules.InReach(Vector2.Distance(new Vector2(at.X, at.Z), new Vector2(n.X, n.Z)), Math.Max(1, LwMod.Settings.NewsReach)) ? 1 : 0;
    }

    /// <summary>The Atlas's News section: what the squad has heard, newest first (id, row, page).</summary>
    public IEnumerable<(string Id, string Row, List<string> Page)> AtlasRows() =>
        board.Items.Where(n => n.Heard && (n.Source is not (OutpostSource or NewsBoard.TownSource or PrisonerSource) || Enabled(n))).OrderByDescending(n => n.HeardHours).Take(15).Select(n => (n.Id.ToString(), n.Text, new List<string>
        {
            n.Text,
            "",
            $"Heard on day {(int)(n.HeardHours / 24) + 1}, near {n.Place}.",
        }));

    internal IEnumerable<(string Node, string Name, IEnumerable<string> History)> TownHistory() =>
        LwMod.Settings.TownHandovers
            ? board.Handovers.GroupBy(n => n.Node, StringComparer.Ordinal).Select(group =>
            {
                var entries = group.OrderByDescending(n => n.Hours).ThenByDescending(n => n.Id).ToList();
                return (group.Key, SiteOf(group.Key)?.Town.Name ?? entries[0].Place,
                    entries.Select(n => $"Day {(int)(n.Hours / 24) + 1}: {n.Text}"));
            })
            : [];

    public readonly Published Shown = new();                // the F10 row's line

    public string Summary() =>
        !LwMod.Settings.OtherModsNews ? "news from other mods is off (the Squad record window's Settings tab)"
        : board.Items.Count == 0 ? "no news from other mods yet"
        : string.Join("; ", board.Items.OrderByDescending(n => n.Hours).Take(6)
            .Select(n => $"{n.Text} ({n.Source}, day {(int)(n.Hours / 24) + 1}{(n.Heard ? ", heard" : "")}{(talked.Contains(n) ? ", talked about" : "")})"));

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        board.Write(w);
    }

    public void Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        board.Read(r);
        seen = null;
        toldAt.Clear();
        Array.Clear(shown);
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(board.NextId);
        h.Add(board.Items.Count);
        foreach (var item in board.Items)
        {
            HashItem(ref h, item);
            h.Add(toldAt.TryGetValue(item, out double hours) ? hours : double.MinValue);
        }
        h.Add(board.Handovers.Count);
        foreach (var item in board.Handovers) HashItem(ref h, item);
    }

    static void HashItem(ref StateHasher h, NewsItem item)
    {
        h.Add(item.Id);
        h.Add(SquadRecord.StableHash(item.Kind));
        h.Add(SquadRecord.StableHash(item.Node));
        h.Add(SquadRecord.StableHash(item.Place));
        h.Add(SquadRecord.StableHash(item.Faction));
        h.Add(SquadRecord.StableHash(item.Text));
        h.Add(SquadRecord.StableHash(item.Source));
        h.Add(item.Hours);
        h.Add(item.X);
        h.Add(item.Z);
        h.Add(item.Weight);
        h.Add(item.Heard);
        h.Add(item.HeardHours);
    }
}
