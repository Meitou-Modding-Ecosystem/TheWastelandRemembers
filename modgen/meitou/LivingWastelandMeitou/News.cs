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
    StoryTaskBar? listeningBar;
    CharacterId? stagedAsk;
    readonly BathhouseGuests bathhouse = new();

    internal void BathhouseParty(World world, Meitou.Simulation.Factions.Platoon party, string change)
    {
        if (ReferenceEquals(seen, world) && asks is { Towns.Length: > 0 })
            bathhouse.Observe(party, change);
    }

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
        if (!LwMod.Settings.OtherModsNews || seen is not { } world || source.Trim() is NewsBoard.TownSource or PrisonerSource or NewsBoard.LeaderSource) return false;
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
        NewsBoard.LeaderSource => LwMod.Settings.FallenLeaderStories,
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

    internal void ReportPrisoner(string node, string place, int episode, string outcome, string text, int weight)
    {
        if (!LwMod.Settings.PrisonerCases || seen is not { } world || SiteOf(node) is not { } site) return;
        // from the outpost holding the captive when it is the squad's own (its name and spot), carried along the roads from the node
        var own = world.Buildings.Towns.FirstOrDefault(t => t.Owner == world.Player.Faction && t.Name == place);
        var at = own?.Position ?? site.Position;
        var report = board.Accept($"custody:{episode}:{outcome}", own?.Name ?? site.Town.Name, "", text, PrisonerSource, weight,
            world.GameHours, at.X, at.Z, out _, node);
        if (report is not null) Refresh(world);
    }

    internal void ReportLeader(World world, TownSite site, string record, bool captured, string faction, string text)
    {
        if (!LwMod.Settings.FallenLeaderStories) return;
        var item = board.Accept(LeaderStories.Kind(record, captured), site.Town.Name, faction, text, NewsBoard.LeaderSource, 85,
            world.GameHours, site.Position.X, site.Position.Z, out _, NodeOf(site));
        if (item is not null) Refresh(world);
    }

    static bool ConfirmedReport(NewsItem news, double now) =>
        Enabled(news) && !news.Heard && news.Source.Length > 0 && news.Source != "F10 test" &&
        news.Kind is ("town-fell" or "siege-broken" or "caravan-lost") &&
        news.Hours <= now && now - news.Hours < NewsBoard.LiveDays * 24;

    internal NewsItem? CourierReport(double now, int? id = null) =>
        board.Items.Where(news => ConfirmedReport(news, now) && (id is null || news.Id == id))
            .OrderByDescending(n => n.Weight).ThenByDescending(n => n.Hours).ThenBy(n => n.Id).FirstOrDefault();

    internal bool DeliverCourierReport(World world, NewsItem report)
    {
        if (CourierReport(world.GameHours, report.Id) != report) return false;
        HearOne(world, report);
        return true;
    }

    internal NewsItem? ShareNearbyReport(World world, Vector3 at)
    {
        const float radius = 20_000;
        var report = board.Items.Where(item =>
        {
            float dx = item.X - at.X, dz = item.Z - at.Z;
            return ConfirmedReport(item, world.GameHours) && dx * dx + dz * dz <= radius * radius &&
                   (item.Source != NewsBoard.TownSource || WitnessesTown(item.X, item.Z, item));
        }).OrderByDescending(n => n.Weight).ThenByDescending(n => n.Hours).ThenBy(n => n.Id).FirstOrDefault();
        if (report is not null) HearOne(world, report);
        return report;
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
            ClearListeningBar();
            stagedAsk = null;
            kindnessTold.Clear();
            seen = world;
            byWorld.AddOrUpdate(world, this);
            NightlyHooks.Marks = [];
            Refresh(world);
            var silent = data?.Factions.Select((faction, id) => (faction.Name, id))
                .Where(item => NewsAskRules.NoConversationNews(item.Name)).Select(item => item.id).ToHashSet();
            asks = new NightlyHooks.AskSession(data?.Sites.Select(s => (s.Position.X, s.Position.Z)).ToArray() ?? [], silent);
            bathhouse.Reset(world, asks);
            NightlyHooks.CurrentAsks = asks;
        }
        if (Published.Due(world.Tick, 13)) Shown.Set(Summary);
        if (world.Tick % PassEvery != 40)
        {
            if (!LwMod.Settings.AskForNews && listeningBar is not null) ClearListeningBar();
            else if (stagedAsk is null && StoryTaskBar.Available && LwMod.Settings.AskForNews &&
                     asks is { } pending && !pending.Asks.IsEmpty && pending.Asks.TryDequeue(out var question))
            {
                stagedAsk = question;
                if (AskSpeaker(world, question) is { Cold: { IsImprisoned: false } speaker })
                    listeningBar = StoryTaskBar.TryShow(speaker, "Listening...");
            }
            return;
        }
        ClearListeningBar();
        var queued = stagedAsk;
        stagedAsk = null;
        try
        {
            if (asks is { } guestSession) bathhouse.Publish(world, guestSession);
            if (queued is { } first && LwMod.Settings.AskForNews)
                AnswerAsk(world, first);
            while (asks is { } session && session.Asks.TryDequeue(out var request))
                if (LwMod.Settings.AskForNews) AnswerAsk(world, request);   // not if switched off since the click
            NightlyHooks.Marks = LwMod.Settings.MapMarks ? Marks(world) : [];
        }
        catch (Exception e) { LwMod.Log($"news: skipped asks and map marks after an error: {e.GetBaseException().Message}"); }
        if (!LwMod.Settings.OtherModsNews && !LwMod.Settings.RememberOutposts &&
            !LwMod.Settings.TownHandovers && !LwMod.Settings.PrisonerCases && !LwMod.Settings.FallenLeaderStories) return;
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
        foreach (var n in board.Items.Where(n => Enabled(n) && !n.Heard && NewsBoard.IsLive(n, world.GameHours)))
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
        if (n.Source != NewsBoard.LeaderSource && LwDeeds.For(world)?.Record is { } record &&
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

    void ClearListeningBar()
    {
        listeningBar?.Clear();
        listeningBar = null;
    }

    (int Slot, CharacterCold Cold)? AskSpeaker(World world, CharacterId who)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(who, out int slot) || !table.Next[slot].Alive ||
            table.Cold(slot) is not { IsPlayer: false, Animal: null } cold ||
            cold.Medical is { Dead: true } or { Incapacitated: true } || BathhouseGuests.Contains(world, cold.SquadId) ||
            asks?.SilentFactions?.Contains(cold.Faction) == true)
            return null;
        return (slot, cold);
    }

    /// <summary>Answers a news question with the most important local news the speaker can share.</summary>
    public void AnswerAsk(World world, CharacterId who)
    {
        if (AskSpeaker(world, who) is not { } speaker) return;
        var (s, cold) = speaker;
        var table = world.Characters;
        // a captive is in no mood to pass on news to the one holding them (play test of bf: the caged Queen told the squad she'd been taken)
        if (cold.IsImprisoned)
        {
            // in their own people's voice (CaptiveTalk); a Hive Queen screams
            world.Bark(who, CaptiveTalk.Line(world.Uniques.RecordOf(who), LwDeeds.For(world)?.FactionName(cold.Faction), (ulong)world.Tick));
            LwMod.Log($"ask for news: {cold.Name} is a captive and said nothing");
            return;
        }
        var at = table.Next[s].Position;
        // and nobody passes on news about themselves
        var live = board.Items.Where(n => Enabled(n) && NewsBoard.IsLive(n, world.GameHours)
            && !(cold.Name.Length >= 4 && n.Text.Contains(cold.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        var squad = new List<string>();
        var hot = table.Next;
        for (int k = 0; k < hot.Length; k++)
            if (hot[k].Alive && table.Cold(k) is { IsPlayer: true, Animal: null } pc) squad.Add(pc.Name);
        var (line, i) = NewsAskRules.Answer(live.Select(n => (n.Text, n.X, n.Z, n.Weight, n.Hours)).ToList(), at.X, at.Z,
            Math.Max(1, LwMod.Settings.NewsReach) * 10000f, (ulong)world.Tick ^ (ulong)s, squad,
            live.Select(n => toldAt.TryGetValue(n, out double h) ? h : double.MinValue).ToList(), world.GameHours);
        if (i >= 0)
        {
            toldAt[live[i]] = world.GameHours;
            line = LeaderStories.Reply(data, cold, at, live[i], line);
        }
        else if (LocalKindness(world, cold, at) is { } local) line = local;
        // the board has nothing: what they told the squad themselves lately, or what the town has been saying, never "Haven't heard a
        // thing" from someone who just told a rumour (seen in play, bv)
        else if (NewsAskRules.Fallback(GossipLink.For(world).LastBy(GossipLink.Key(world, who), world.GameHours),
                     GossipLink.For(world).FromTown(data?.TownAt(at) is >= 0 and var site && site < data.Sites.Count ? data.Sites[site].Town.Name : null,
                         world.GameHours, (ulong)world.Tick), (ulong)world.Tick ^ (ulong)s) is { } heard) line = heard;
        foreach (var gone in toldAt.Keys.Where(k => !board.Items.Contains(k)).ToList()) toldAt.Remove(gone);
        world.Bark(who, line);
        if (i >= 0 && !live[i].Heard) HearOne(world, live[i]);
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "news", i >= 0 ? $"report:{live[i].Id}" : "", "asked", $"{cold.Name}: {line}");
        LwMod.Log($"ask for news: {cold.Name} said \"{line}\"");
    }

    // The squad already knows the deed. Keep this as a local answer, outside the report board and its History.
    readonly Dictionary<int, double> kindnessTold = [];

    string? LocalKindness(World world, CharacterCold speaker, Vector3 at)
    {
        if (!KindnessWatch.Enabled || KindnessVoices.Current is not { } voices || data is null ||
            LwDeeds.For(world) is not { } deeds || world.Player.Faction < 0 || speaker.Faction < 0 ||
            data.TownAt(at) is not (>= 0 and var site) || site >= data.SiteFactions.Count ||
            data.SiteFactions[site] != speaker.Faction || data.Relations.Get(world.Player.Faction, speaker.Faction) <= -30 ||
            kindnessTold.TryGetValue(site, out double last) && world.GameHours >= last && world.GameHours - last < 24)
            return null;

        string faction = deeds.FactionName(speaker.Faction);
        if (NewsAskRules.NoConversationNews(faction)) return null;
        foreach (var memory in deeds.Kindness.People)
        {
            if (memory.Faction != faction || memory.Name == speaker.Name ||
                !deeds.Kindness.HasReunited(memory.Actor) ||
                deeds.Kindness.Home(memory.Actor) is not { } home ||
                data.TownAt(new Vector3(home.X, at.Y, home.Z)) != site ||
                voices.Gossip(memory) is not { Length: > 0 } line) continue;
            kindnessTold[site] = world.GameHours;
            return line;
        }
        return null;
    }

    /// <summary>When each piece of news was last told to the squad by someone asked (not saved: a load starts afresh).</summary>
    readonly Dictionary<NewsItem, double> toldAt = [];

    /// <summary>The map marks (NightlyHooks): rivals about, known outlaw camps, news heard this week.</summary>
    List<MapMark> Marks(World world) => MapMarkRules.Build(
        RivalsSystem.For(world)?.Told().Select(r => (r.Name, r.Phase, r.X, r.Z, r.Grudge)) ?? [],
        OutlawCampsSystem.For(world)?.KnownCamps(world) ?? [],
        board.Items.Where(n => n.Heard && (n.Source is not (OutpostSource or NewsBoard.TownSource or PrisonerSource or NewsBoard.LeaderSource) || Enabled(n))).Select(n => (n.Text, n.Place, n.X, n.Z, n.HeardHours)), world.GameHours);

    /// <summary>What a speaker's news condition sees for slot k: 1 when that news is live and the speaker is within reach of its place.</summary>
    public int SlotValue(World world, int slot, CharacterId speaker)
    {
        if (slot >= talked.Count || !Enabled(talked[slot]) || !world.Characters.TryResolveNext(speaker, out int s)) return 0;
        var n = talked[slot];
        if (n.Source == NewsBoard.LeaderSource && SpeakerFaction() is { Length: > 0 } speakerFaction &&
            NewsAskRules.NoConversationNews(speakerFaction)) return 0;
        var at = world.Characters.Next[s].Position;
        return CampRules.InReach(Vector2.Distance(new Vector2(at.X, at.Z), new Vector2(n.X, n.Z)), Math.Max(1, LwMod.Settings.NewsReach)) ? 1 : 0;

        string? SpeakerFaction() => data is not null && world.Characters.Cold(s) is { Faction: >= 0 } cold &&
            cold.Faction < data.Factions.Count ? data.Factions[cold.Faction].Name : null;
    }

    /// <summary>The Atlas's News section: what the squad has heard, newest first (id, row, page).</summary>
    public IEnumerable<(string Id, string Row, List<string> Page)> AtlasRows() =>
        board.Items.Where(n => n.Heard && (n.Source is not (OutpostSource or NewsBoard.TownSource or PrisonerSource or NewsBoard.LeaderSource) || Enabled(n))).OrderByDescending(n => n.HeardHours).Take(15).Select(n => (n.Id.ToString(), n.Text, new List<string>
        {
            n.Text,
            "",
            $"Heard on day {(int)(n.HeardHours / 24)}, near {n.Place}.",
        }));

    internal IEnumerable<(string Node, string Name, IEnumerable<string> History)> TownHistory() =>
        LwMod.Settings.TownHandovers
            ? board.Handovers.GroupBy(n => n.Node, StringComparer.Ordinal).Select(group =>
            {
                var entries = group.OrderByDescending(n => n.Hours).ThenByDescending(n => n.Id).ToList();
                return (group.Key, SiteOf(group.Key)?.Town.Name ?? entries[0].Place,
                    entries.Select(n => $"Day {(int)(n.Hours / 24)}: {n.Text}"));
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
        ClearListeningBar();
        stagedAsk = null;
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
