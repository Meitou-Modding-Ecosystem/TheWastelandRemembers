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

    /// <summary>A report from another mod (WastelandApi.Report). False, with the reason logged, when it is not taken.</summary>
    public bool Report(string kind, string place, string faction, string text, int weight, string source)
    {
        if (!LwMod.Settings.OtherModsNews || seen is not { } world || data is null) return false;
        var at = PlaceAt(place);
        if (at is null)
        {
            LwMod.Log($"news: not taken from {source}: no town called \"{place}\"");
            return false;
        }
        var item = board.Accept(kind, place, faction, text, source, weight, world.GameHours, at.Value.X, at.Value.Z, out var why);
        if (item is null)
        {
            LwMod.Log($"news: not taken from {source} ({kind}, {place}): {why}");
            return false;
        }
        LwMod.Log($"news: {source} reports {item.Kind} at {item.Place}: \"{item.Text}\" (weight {item.Weight})");
        Refresh(world);
        return true;
    }

    Vector3? PlaceAt(string place)
    {
        if (data is null || string.IsNullOrWhiteSpace(place)) return null;
        for (int i = 0; i < data.Sites.Count; i++)
            if (string.Equals(data.Sites[i].Town.Name, place.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(data.Sites[i].Record.Name, place.Trim(), StringComparison.OrdinalIgnoreCase))
                return data.Sites[i].Position;
        return null;
    }

    public void SlowWorld(World world)
    {
        Latest = this;
        if (!ReferenceEquals(seen, world)) { seen = world; byWorld.AddOrUpdate(world, this); Refresh(world); }
        if (!LwMod.Settings.OtherModsNews || world.Tick % PassEvery != 40) return;
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
        talked = board.Talked(world.GameHours);
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
        foreach (var n in board.Items.Where(n => !n.Heard && world.GameHours - n.Hours <= NewsBoard.LiveDays * 24))
        {
            if (!players.Any(p => CampRules.InReach(Vector2.Distance(new Vector2(p.X, p.Z), new Vector2(n.X, n.Z)), km))) continue;
            n.Heard = true;
            n.HeardHours = world.GameHours;
            LwDeeds.For(world)?.Record.Note(n.Text);
            RecapLink.Moment(n.Text, n.Place, Math.Clamp(n.Weight, 30, 80), new Vector3(n.X, 0, n.Z));
            if (n.Weight >= PostAtWeight) world.Post("Word on the road: " + n.Text);
            LwMod.Log($"news: the squad heard \"{n.Text}\"");
            LogTally.Add("news heard");
        }
    }

    /// <summary>What a speaker's news condition sees for slot k: 1 when that news is live and the speaker is within reach of its place.</summary>
    public int SlotValue(World world, int slot, CharacterId speaker)
    {
        if (!LwMod.Settings.OtherModsNews || slot >= talked.Count || !world.Characters.TryResolveNext(speaker, out int s)) return 0;
        var n = talked[slot];
        var at = world.Characters.Next[s].Position;
        return CampRules.InReach(Vector2.Distance(new Vector2(at.X, at.Z), new Vector2(n.X, n.Z)), Math.Max(1, LwMod.Settings.NewsReach)) ? 1 : 0;
    }

    /// <summary>The Atlas's News section: what the squad has heard, newest first (id, row, page).</summary>
    public IEnumerable<(string Id, string Row, List<string> Page)> AtlasRows() =>
        board.Items.Where(n => n.Heard).OrderByDescending(n => n.HeardHours).Take(15).Select(n => (n.Id.ToString(), n.Text, new List<string>
        {
            n.Text,
            "",
            $"Heard on day {(int)(n.HeardHours / 24) + 1}, near {n.Place}.",
        }));

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
        Array.Clear(shown);
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(board.Items.Count);
        foreach (var n in board.Items) { h.Add(n.Id); h.Add(n.Heard ? 1 : 0); }
    }
}
