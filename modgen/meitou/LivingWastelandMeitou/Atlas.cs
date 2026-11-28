// The Atlas (Meitou only; data: content/x_atlas.json from gen_atlas.py): places the squad knows of and the people talked about.
//
// Places come from the game's own knowledge (world.Player.Towns over PopulationData.Sites: discovered = on the map, explored = visited).
// What the squad has heard comes from listening: once a second the speech bubbles on show (DialogueSystem.PublishSpeech) said by someone
// within earshot of a squad member are matched against our gossip lines; a match is noted under the person it is about and under every
// known place it names. People the squad has stood near are "met". Hidden people (the Bugmaster and the like) show as a blur after one
// rumour, with their place after two, and in full once met. The pages are built here in the tick; the window only shows them.
using System.Globalization;
using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public sealed record AtlasTable(Dictionary<string, AtlasPlace>? Places = null, Dictionary<string, AtlasPerson>? People = null, List<List<string>>? Lines = null);

public sealed class AtlasSystem(AtlasTable table, Meitou.Simulation.Dialogue.DialogueSystem? talk, PopulationData? data) : ITickSystem, IModSaveData, IStateHashed
{
    const float Earshot = 500;     // world units (50 m): a squad member this close hears what is said
    const float Sight = 900;       // 90 m: close enough to say the squad has seen someone

    readonly Dictionary<string, AtlasPlace> places = table.Places ?? [];
    readonly Dictionary<string, AtlasPerson> people = table.People ?? [];
    readonly ILookup<string, KeyValuePair<string, AtlasPerson>> peopleByPlace =
        (table.People ?? []).Where(person => person.Value.Place is not null)
            .ToLookup(person => person.Value.Place!, StringComparer.Ordinal);
    readonly AtlasBook book = new();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, AtlasSystem> byWorld = new();
    internal static AtlasSystem? For(World world) => byWorld.TryGetValue(world, out var atlas) ? atlas : null;
    bool registered;
    internal AtlasBook Locations => book;
    internal static bool TrackPlaces => LwMod.Settings.Atlas && LwMod.Settings.LastKnownPlaces;

    internal void Locate(World world, string key, string identity, float x, float z, string? reportedTown = null)
    {
        var at = new Vector3(x, 0, z);
        string town = reportedTown ?? TownAt(at) ?? "";
        if (town.Length == 0 && data is not null)
        {
            float nearest = 20_000 * 20_000; // Beyond two kilometres, call it the wilds.
            foreach (var site in data.Sites)
            {
                float dx = site.Position.X - x, dz = site.Position.Z - z;
                float distance = dx * dx + dz * dz;
                if (distance >= nearest) continue;
                nearest = distance;
                town = site.Town.Name;
            }
        }
        book.Locate(key, new LastKnownPlace(identity, x, z, town, world.GameHours, reportedTown is not null));
    }
    readonly Dictionary<string, (PersonPageInputs Inputs, string Text)> personPages = new(StringComparer.Ordinal);
    KindnessBook? kindnessBook;
    int kindnessRevision;
    readonly List<(string Key, string Row, string Page)> kindnessRows = [];
    CultureInfo? pageCulture;
    readonly Dictionary<(CharacterId, string), double> lastSeen = [];
    static readonly IReadOnlyDictionary<string, string> EmptyPages = new Dictionary<string, string>();
    IReadOnlyDictionary<string, string> lastPages = EmptyPages;
    AtlasMatcher? matcher;
    bool failed;

    public void SlowWorld(World world)
    {
        if (!registered) { byWorld.AddOrUpdate(world, this); registered = true; }
        if (failed || world.Tick % 30 != 7) return;
        if (!LwMod.Settings.Atlas)
        {
            RecordHost.AtlasList = "The Atlas is off (this window's Settings tab).";
            lastPages = RecordHost.AtlasPages = EmptyPages;
            RecordHost.AtlasPortraits = EmptyPages;
            AtlasLiveFaces.Publish(null);
            return;
        }
        try
        {
            matcher ??= new AtlasMatcher((table.Lines ?? []).Where(l => l.Count == 2).Select(l => (l[0], l[1])));
            var players = Pursuit.Players(world);
            if (players.Count > 0)
            {
                Listen(world, players);
                if (world.Tick % 150 == 7) Look(world, players);
            }
            // Outlaw logic reads the index. Its timing stays fixed when detail pages refresh less often.
            if (world.Tick % 60 == 7)
                Publish(world, !LwMod.Settings.LighterChecks || world.Tick % 120 == 7);
        }
        catch (Exception e)
        {
            failed = true;
            LwMod.Log($"atlas: stopped after an error: {e.GetBaseException().Message}");
        }
    }

    // ---- listening ----

    void Listen(World world, List<(float X, float Z)> players)
    {
        if (talk is null) return;
        var chars = world.Characters;
        foreach (var b in talk.PublishSpeech())
        {
            if (!chars.TryResolveNext(b.Speaker, out int s) || chars.Cold(s) is not { IsPlayer: false } cold) continue;
            var at = chars.Next[s].Position;
            var near = Pursuit.Nearest(players, at.X, at.Z);
            if ((near.X - at.X) * (near.X - at.X) + (near.Z - at.Z) * (near.Z - at.Z) > Earshot * Earshot) continue;
            if (lastSeen.TryGetValue((b.Speaker, b.Text), out double when) && world.GameHours - when < 1) continue;
            lastSeen[(b.Speaker, b.Text)] = world.GameHours;
            if (matcher!.Match(b.Text) is not { } subject) continue;

            string? town = TownAt(at);
            string where = town ?? "on the road";
            var heard = new Heard(b.Text.Trim(), where, world.GameHours);
            if (!GossipLink.Preview) GossipLink.Remember(world, b.Speaker, cold, town, heard.Text);
            bool any = false;
            if (subject.Length > 0) any |= book.Note(subject, heard);
            else foreach (var name in people.Keys.Where(n => b.Text.Contains(n, StringComparison.Ordinal))) any |= book.Note(name, heard);
            foreach (var place in places.Keys.Where(p => b.Text.Contains(p, StringComparison.Ordinal))) any |= book.Note("@" + place, heard);
            if (data is not null)
                foreach (var fname in data.Factions.Select(x => x.Name).Where(n => n.Length >= 5 && b.Text.Contains(n, StringComparison.Ordinal)).Distinct())
                    any |= book.Note("#" + fname, heard);
            if (any && LwMod.Settings.LogChanges) LwMod.Log($"atlas: heard at {where}: {heard.Text}");
        }
        if (lastSeen.Count > 400)
            foreach (var k in lastSeen.Where(p => world.GameHours - p.Value > 1).Select(p => p.Key).ToList()) lastSeen.Remove(k);
    }

    void Look(World world, List<(float X, float Z)> players)
    {
        // a town a squad member stands in counts as visited (Meitou's own "explored" flag is not set just by walking in), kept in LwCore
        var core = LwState.For(world).Core;
        foreach (var (px, pz) in players)
            if (TownAt(new Vector3(px, 0, pz)) is { } here && core.Get("@visited " + here) == 0) core.Set("@visited " + here, 1);
        var chars = world.Characters;
        foreach (var (name, p) in people)
        {
            if (p.Id is null || world.Uniques.Find(p.Id) is not { } u || u.Handle.IsNone || !chars.TryResolveNext(u.Handle, out int s)) continue;
            var at = chars.Next[s].Position;
            var near = Pursuit.Nearest(players, at.X, at.Z);
            if ((near.X - at.X) * (near.X - at.X) + (near.Z - at.Z) * (near.Z - at.Z) > Sight * Sight) continue;
            book.Saw(name, TownAt(at) ?? (p.Region is { Length: > 0 } r ? $"out in {r}" : "out in the wilds"), world.GameHours);
            if (book.Meet(name)) LwMod.Log($"atlas: the squad has seen {name}");
        }
    }

    string? TownAt(Vector3 at) => data is not null && data.TownAt(at) is >= 0 and var t && t < data.Sites.Count ? data.Sites[t].Town.Name : null;

    // ---- what the squad knows of places ----

    /// <summary>Town name -> 1 on the map, 2 visited (the best of its sites).</summary>
    Dictionary<string, int> Known(World world)
    {
        var known = new Dictionary<string, int>(StringComparer.Ordinal);
        if (data is null) return known;
        var towns = world.Player.Towns;
        for (int i = 0; i < towns.Length && i < data.Sites.Count; i++)
        {
            int k = (towns[i] & TownKnowledge.Explored) != 0 ? 2 : (towns[i] & TownKnowledge.Discovered) != 0 ? 1 : 0;
            if (k == 0) continue;
            string n = data.Sites[i].Town.Name;
            known[n] = Math.Max(known.GetValueOrDefault(n), k);
        }
        var core = LwState.For(world).Core;
        foreach (var n in known.Keys.ToList())
            if (core.Get("@visited " + n) == 1) known[n] = 2;
        return known;
    }

    // ---- the pages ----

    void Publish(World world, bool refreshDetails)
    {
        if (!Equals(pageCulture, CultureInfo.CurrentCulture))
        {
            personPages.Clear();
            pageCulture = CultureInfo.CurrentCulture;
        }

        // Ownership can change at any time. Share the lookup within this publication.
        var owners = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? PublishedOwner(string town)
        {
            if (!owners.TryGetValue(town, out var owner)) owners[town] = owner = Owner(town);
            return owner;
        }

        List<(string Name, string Owner)>? leaders = null;
        List<(string Name, string Owner)> Leaders()
        {
            if (leaders is not null) return leaders;
            leaders = [];
            foreach (var (name, person) in people)
                if (person.Place is { } place && places.ContainsKey(place) && PublishedOwner(place) is { } owner)
                    leaders.Add((name, owner));
            return leaders;
        }

        var known = Known(world);
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        var portraits = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, CharacterId>? liveFaces = AtlasLiveFaces.Enabled ? new(StringComparer.Ordinal) : null;
        var list = new List<string>();

        bool ReusePage(string key)
        {
            if (refreshDetails || !lastPages.TryGetValue(key, out var text)) return false;
            pages[key] = text;
            return true;
        }

        var placeRows = places.Where(p => known.ContainsKey(p.Key) || book.CountOf("@" + p.Key) > 0)
            .OrderBy(p => p.Value.Region, StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
        list.Add("-- Places --");
        if (placeRows.Count == 0) list.Add("No places yet.");
        foreach (var (name, p) in placeRows)
        {
            int k = known.GetValueOrDefault(name);
            list.Add($"@@p:{name}|{name}  ({(k == 2 ? "visited" : k == 1 ? "on your map" : "only heard of")}, {p.Region})");
            string pageKey = "p:" + name;
            if (!ReusePage(pageKey)) pages[pageKey] = string.Join("\n", PlacePage(world, name, p, k, (!LwMod.Settings.TownHandovers || !TownSwitchWatch.Available) ? PublishedOwner(name) : null));
        }

        if (OutpostMemories.For(world)?.AtlasRows().ToList() is { Count: > 0 } outposts)
        {
            list.Add("");
            list.Add("-- Outposts --");
            foreach (var (key, name, page) in outposts)
            {
                list.Add($"@@o:{key}|{name}");
                string pageKey = "o:" + key;
                if (!ReusePage(pageKey)) pages[pageKey] = string.Join("\n", page);
            }
        }

        if (PrisonerCases.For(world)?.AtlasRows().ToList() is { Count: > 0 } custody)
        {
            list.Add("");
            list.Add("-- Prisoners --");
            foreach (var (key, name, page) in custody)
            {
                list.Add($"@@c:{key}|{name}");
                string pageKey = "c:" + key;
                if (!ReusePage(pageKey)) pages[pageKey] = page;
            }
        }

        if (NewsSystem.For(world)?.TownHistory().OrderBy(t => t.Name, StringComparer.Ordinal).ToList() is { Count: > 0 } handovers)
        {
            list.Add("");
            list.Add("-- Town handovers --");
            foreach (var (node, name, history) in handovers)
            {
                list.Add($"@@h:{node}|{name}");
                string pageKey = "h:" + node;
                if (!ReusePage(pageKey)) pages[pageKey] = string.Join("\n", history.Prepend(name));
            }
        }

        bool trackPlaces = TrackPlaces;
        var personRows = people.Select(p => (p.Key, p.Value, Level: book.Level(p.Key, p.Value,
                p.Value.Place is { } pl && known.ContainsKey(pl) ||
                trackPlaces && p.Value.Outlaw && book.Position("camp:" + p.Key, p.Key) is not null)))
            .Where(r => r.Level > 0).OrderBy(r => r.Value.Outlaw).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();
        // rivals (Rivals.cs): the people who have a grudge against the squad, first
        if (RivalsSystem.For(world) is { } rivals && rivals.AtlasRows(trackPlaces ? book : null, world.GameHours).ToList() is { Count: > 0 } rivalRows)
        {
            list.Add("");
            list.Add("-- Rivals --");
            foreach (var (rn, row, page) in rivalRows)
            {
                list.Add($"@@r:{rn}|{row}");
                string pageKey = "r:" + rn;
                if (trackPlaces || !ReusePage(pageKey)) pages[pageKey] = string.Join("\n", page);
                if (liveFaces is not null && rivals.AtlasPortrait(world, rn) is { } rivalActor)
                    liveFaces[pageKey] = rivalActor;
            }
        }
        if (KindnessWatch.Enabled && LwDeeds.For(world)?.Kindness is { People.Count: > 0 } kindness)
        {
            list.Add("");
            list.Add("-- People who owe you --");
            foreach (var (key, row, page) in KindnessPages(kindness))
            {
                list.Add(row);
                pages[key] = page;
            }
        }
        // news from other mods (News.cs): what the squad has heard happen in the world
        if (NewsSystem.For(world)?.AtlasRows().ToList() is { Count: > 0 } newsRows)
        {
            list.Add("");
            list.Add("-- News --");
            foreach (var (id, row, page) in newsRows)
            {
                list.Add($"@@n:{id}|{row}");
                string pageKey = "n:" + id;
                if (!ReusePage(pageKey)) pages[pageKey] = string.Join("\n", page);
            }
        }
        list.Add("");
        list.Add("-- People --");
        if (personRows.Count == 0) list.Add("Nobody of note yet.");
        foreach (var (name, p, level) in personRows)
        {
            string label = level == 1 ? "someone talked about" : p.Epithet;
            string fate = Fate(world, name, p, level);
            list.Add($"@@w:{name}|{(level == 1 ? "? " : "")}{name}, {label}{fate}");
            string pageKey = "w:" + name;
            var camp = trackPlaces && p.Outlaw ? book.Position("camp:" + name, name) : null;
            if (camp is not null || !ReusePage(pageKey)) pages[pageKey] = PersonText(world, name, p, level, known, fate, camp?.Line(world.GameHours));
            if (level >= 2 && p.Id is { Length: > 0 } rid)
            {
                portraits[pageKey] = rid;
                if (liveFaces is not null && world.Uniques.Find(rid) is { Handle: var actor } &&
                    AtlasLiveFaces.Living(world, actor, rid) is { } living)
                    liveFaces[pageKey] = living;
            }
        }
        // factions the player has met: how they stand with the squad, their wars, what they think of its titles, the towns that remember it
        if (data is not null && world.Player.Faction >= 0)
        {
            var met = world.Player.KnownFactions.Where(f => f >= 0 && f < data.Factions.Count && f != world.Player.Faction)
                .Select(f => (Index: f, Name: data.Factions[f].Name)).Where(f => f.Name.Length > 0).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
            if (met.Count > 0)
            {
                list.Add("");
                list.Add("-- Factions --");
                foreach (var (f, fname) in met)
                {
                    float rel = data.Relations.Get(world.Player.Faction, f);
                    list.Add($"@@f:{fname}|{fname}  ({Standing(rel)})");
                    string pageKey = "f:" + fname;
                    if (!ReusePage(pageKey)) pages[pageKey] = string.Join("\n", FactionPage(world, f, fname, rel, met, Leaders()));
                }
            }
        }
        RecordHost.AtlasList = string.Join("\n", list);
        lastPages = RecordHost.AtlasPages = pages;
        RecordHost.AtlasPortraits = portraits;
        AtlasLiveFaces.Publish(liveFaces);
    }

    List<(string Key, string Row, string Page)> KindnessPages(KindnessBook book)
    {
        if (ReferenceEquals(kindnessBook, book) && kindnessRevision == book.PresentationRevision) return kindnessRows;
        kindnessBook = book;
        kindnessRevision = book.PresentationRevision;
        kindnessRows.Clear();
        for (int i = 0; i < book.People.Count; i++)
        {
            var memory = book.People[i];
            var home = book.Home(memory.Actor);
            string key = FormattableString.Invariant($"kindness:{memory.Actor}");
            string row = $"@@{key}|{memory.Name}{(home is null ? "" : $" ({home.Place})")}";
            string status = home is null ? "You don't know where they settled."
                : book.HasReunited(memory.Actor) ? $"You met again in {home.Place}."
                : $"Last known around {home.Place}.";
            kindnessRows.Add((key, row, $"-- {memory.Name} --\n{memory.Name}, {KindnessWatch.What(memory)}.\n{status}"));
        }
        return kindnessRows;
    }

    static string Cap(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    IEnumerable<string> PlacePage(World world, string name, AtlasPlace p, int k, string? owner)
    {
        yield return $"-- {name} --";
        yield return $"{Cap(p.Region)}. {(k == 2 ? "You have been there." : k == 1 ? "On your map; you have not been there yet." : "You have only heard of it.")}";
        yield return p.Known;
        if (owner is not null) yield return owner;
        if (k == 2)
            foreach (var line in p.Explored ?? []) yield return line;
        if (k == 2 && LandmarkLink.Line(data, name) is { } places) yield return places;   // with Landmark Banners installed
        var here = peopleByPlace[name];
        if (k == 2 && here.Any())
        {
            yield return "";
            yield return "-- Who is there --";
            foreach (var (pn, pp) in here)
                if (!pp.Hidden || book.Met(pn) || book.CountOf(pn) >= 2) yield return $"{pn}, {pp.Epithet}{Fate(world, pn, pp, 2)}";
        }
        if (LwDeeds.For(world) is { } deeds)
        {
            var mine = new List<string>();
            double heat = deeds.Heat.Here(name);
            if (heat >= 1) mine.Add(heat >= 24 ? "They want your squad dead here." : heat >= 12 ? "Your squad is wanted here." : "They remember your squad's trouble here.");
            foreach (var f in deeds.Record.FallenMembers.Where(f => f.Place == name))
                mine.Add($"{deeds.Record.Display(f.Name)} fell here, day {Math.Floor(f.Hours / 24):0}.");
            foreach (var (m, rec) in deeds.Record.Members)
                foreach (var sc in rec.Scars.Where(x => x.Town == name))
                    mine.Add($"{deeds.Record.Display(m)} {(sc.Kind == "arm" ? "lost an arm" : sc.Kind == "leg" ? "lost a leg" : "was knocked down")} here.");
            if (mine.Count > 0)
            {
                yield return "";
                yield return "-- Your squad here --";
                foreach (var l in mine.Distinct()) yield return l;
            }
        }
        foreach (var l in HeardLines("@" + name)) yield return l;
    }

    /// <summary>
    /// Who holds the town now (live: a takeover, a ruin or a revival swaps the town's record, KUO's or the game's own), what it was before,
    /// and its state when the swap says (an override's editor label: destroyed, half destroyed, malnourished, prosperous, takeover).
    /// </summary>
    string? Owner(string town)
    {
        if (data is null) return null;
        int site = -1;
        for (int s = 0; s < data.Sites.Count; s++) if (data.Sites[s].Town.Name == town || data.Sites[s].Record.Name == town) { site = s; break; }
        if (site < 0) return null;
        string Name(int f) => f >= 0 && f < data.Factions.Count ? data.Factions[f].Name : "";
        string now = site < data.SiteFactions.Count ? Name(data.SiteFactions[site]) : "";
        string was = data.Sites[site].Record.Faction is { } fid ? Name(data.FactionIndex(fid)) : "";
        string label = (data.Sites[site].Replacement?.Name ?? "").ToLowerInvariant();
        string state = label.Contains("half destroyed") ? " It is half in ruins."
            : label.Contains("destroyed") ? " It lies in ruins."
            : label.Contains("malnourished") ? " Its people are going hungry."
            : label.Contains("prosperous") ? " It is doing well."
            : "";
        if (now.Length == 0) return state.Length > 0 ? state.Trim() : null;
        return (was.Length > 0 && was != now ? $"Held by {now} now; it was {was}'s." : $"Held by {now}.") + state;
    }

    static string Standing(float rel) => rel >= 50 ? "allies" : rel >= 20 ? "friendly" : rel > -20 ? "neutral" : rel > -50 ? "unfriendly" : "at war with you";

    /// <summary>
    /// The faction's own title for the squad, from its record's rank strings (the game's display data, which nothing in the game reads): while
    /// hostile (-30 or worse), its negative ranks by how hostile ("Heathen" ... "The Devil" for the Holy Nation); otherwise the rank the player
    /// has earned there (contracts raise it), rank 1 and up ("Approved Trader" ... "Hero of Light"). Null when the faction has no titles or
    /// the player has no rank there yet.
    /// </summary>
    string? RankTitle(int f, float rel)
    {
        if (data is null || f < 0 || f >= data.Factions.Count || data.Db.Find(data.Factions[f].Id) is not { } rec) return null;
        string Count(string prefix, out int n)
        {
            n = 0;
            while (n < 20 && rec.GetString($"{prefix}{n}").Length > 0) n++;
            return prefix;
        }
        if (rel <= -30)
        {
            string pre = Count("negative rank", out int neg);
            if (neg == 0) return null;
            int tier = Math.Clamp((int)((-rel - 30) / 70f * neg), 0, neg - 1);
            return rec.GetString($"{pre}{tier}");
        }
        int rank = data.Relations.Rank(f);
        string p = Count("rank", out int pos);
        return rank <= 0 || pos == 0 ? null : rec.GetString($"{p}{Math.Min(rank, pos - 1)}");
    }

    IEnumerable<string> FactionPage(World world, int f, string name, float rel, List<(int Index, string Name)> met,
        List<(string Name, string Owner)> peopleWithOwner)
    {
        yield return $"-- {name} --";
        yield return $"With your squad: {Standing(rel)} ({rel:0}).";
        if (RankTitle(f, rel) is { } title) yield return rel <= -30 ? $"To them you are: {title}." : $"Your rank with them: {title}.";
        var wars = met.Where(o => o.Index != f && data!.Relations.Get(f, o.Index) <= -50).Select(o => o.Name).ToList();
        if (wars.Count > 0) yield return "At war with: " + string.Join(", ", wars) + ".";
        if (LwMod.TitleViews.TryGetValue(name, out var views))
        {
            if (views.GetValueOrDefault("admires") is { Count: > 0 } a) yield return "They admire: " + string.Join(", ", a) + ".";
            if (views.GetValueOrDefault("fears") is { Count: > 0 } fe) yield return "They fear: " + string.Join(", ", fe) + ".";
            if (views.GetValueOrDefault("despises") is { Count: > 0 } d) yield return "They despise: " + string.Join(", ", d) + ".";
        }
        if (LwDeeds.For(world) is { } deeds)
        {
            var hot = deeds.Heat.All().Where(h => h.Faction == name && deeds.Heat.Here(h.Town) >= 1).Select(h => h.Town).Distinct().ToList();
            if (hot.Count > 0) yield return "Towns of theirs that remember your squad's crimes: " + string.Join(", ", hot) + ".";
            if (LwMod.Settings.FallenLeaderStories)
                foreach (var line in LeaderStories.FactionLines(data, deeds.Record, name)) yield return line;
        }
        var leaders = peopleWithOwner.Where(person => person.Owner.Contains(name, StringComparison.Ordinal))
            .Select(person => person.Name).Take(6).ToList();
        if (leaders.Count > 0) yield return "Their people of note you may know of: " + string.Join(", ", leaders) + ".";
        foreach (var l in HeardLines("#" + name)) yield return l;
    }

    readonly record struct PersonPageInputs(int Level, bool KnowsPlace, string Fate, string? Bounty,
        string? SeenPlace, double SeenDay, int HeardCount, string? Camp);

    string PersonText(World world, string name, AtlasPerson person, int level, Dictionary<string, int> known, string fate, string? camp)
    {
        var sighting = level == 3 ? book.LastSeen(name) : null;
        var inputs = new PersonPageInputs(level, person.Place is { } place && known.ContainsKey(place), fate,
            level > 1 ? Bounty(world, person) : null, sighting?.Place, Math.Floor((sighting?.Hours ?? 0) / 24), book.CountOf(name), camp);
        if (personPages.TryGetValue(name, out var cached) && cached.Inputs == inputs) return cached.Text;
        string text = string.Join("\n", PersonPage(name, person, inputs, sighting));
        personPages[name] = (inputs, text);
        return text;
    }

    IEnumerable<string> PersonPage(string name, AtlasPerson p, PersonPageInputs inputs, Heard? sighting)
    {
        int level = inputs.Level;
        if (level == 1)
        {
            yield return "-- Someone talked about --";
            yield return $"{name}.";
            yield return p.Vague ?? "Nobody says where.";
            foreach (var l in HeardLines(name)) yield return l;
            yield break;
        }
        yield return $"-- {name}, {p.Epithet} --";
        yield return p.About;
        string where = p.Place is { } pl ? (inputs.KnowsPlace ? $"Found at {pl}" + (p.Region is { Length: > 0 } r ? $", {r}." : ".") : $"Somewhere in {places.GetValueOrDefault(pl)?.Region ?? "the wastes"}.")
            : $"Seen in {p.Region ?? "the wastes"}.";
        if (p.Outlaw) where = $"Roams {p.Region}.";
        yield return where;
        if (inputs.Camp is not null) yield return inputs.Camp;
        if (inputs.Fate.Length > 0) yield return "Now:" + inputs.Fate.TrimStart(',') + ".";
        if (inputs.Bounty is { } b) yield return b;
        if (p.WantedFor is { Length: > 0 } w) yield return w;
        if (level == 3)
            yield return sighting is { } ls ? $"Last seen by your squad: {AtlasBook.When(ls)}." : "Your squad has met them.";
        foreach (var l in HeardLines(name)) yield return l;
    }

    IEnumerable<string> HeardLines(string key)
    {
        var heard = book.Of(key);
        if (heard.Count == 0) yield break;
        yield return "";
        yield return "-- What you have heard --";
        for (int i = heard.Count - 1; i >= 0; i--) yield return $"\"{heard[i].Text}\" ({AtlasBook.When(heard[i])})";
    }

    /// <summary>", dead" / ", killed by your squad" / ", in chains" when the game says so (and the squad would know).</summary>
    string Fate(World world, string name, AtlasPerson p, int level)
    {
        if (p.Id is null || level < 2) return "";
        var state = world.Uniques.StateOf(p.Id);
        if (state == UniqueState.Dead)
            return LwDeeds.For(world)?.Record.NamedDead.Contains(name) == true ? ", killed by your squad" : ", dead";
        return state == UniqueState.Imprisoned ? ", in chains" : "";
    }

    /// <summary>The price on their head: the live bounty when they are near, else the posters' amount.</summary>
    string? Bounty(World world, AtlasPerson p)
    {
        if (p.Id is null || world.Uniques.StateOf(p.Id) != UniqueState.Alive) return null;
        var chars = world.Characters;
        if (world.Uniques.Find(p.Id) is { } u && !u.Handle.IsNone && chars.TryResolveNext(u.Handle, out int s) && chars.Cold(s) is { Bounties: { } ledger })
        {
            var open = ledger.Entries.Where(e => !e.Claimed && e.Amount > 0).ToList();
            if (open.Count > 0) return $"Bounty: {open.Max(e => e.Amount)} cats ({string.Join(", ", open.Select(e => e.FactionName).Distinct())}).";
        }
        int amount = data?.Db.Find(p.Id)?.GetInt("bounty amount") ?? 0;
        return amount > 0 ? $"Bounty on the posters: {amount} cats." : null;
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        book.Write(w);
    }

    public void Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        book.Read(r);
        personPages.Clear();
        lastPages = EmptyPages;
    }

    public void Hash(ref StateHasher h) => h.Add(book.HashCount);
}

/// <summary>The loaded game's gossip memory, keyed by lasting identity when available.</summary>
public static class GossipLink
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Meitou.Simulation.World, GossipMemory> memories = new();

    internal static bool DeliveryAvailable { get; set; }
    internal static bool Preview => DeliveryAvailable && LwMod.Settings.RumoursFitSpeaker;

    internal static void Queue(DialogueAct act)
    {
        if (!Preview || act.Effect.Value is not ((int)RumourTopic.Road or (int)RumourTopic.Civic)) return;
        act.World.Defer(new HearRumour(act.Speaker, (RumourTopic)act.Effect.Value) { Tick = act.World.Tick + 1 });
    }

    public static GossipMemory For(Meitou.Simulation.World world) => memories.GetOrCreateValue(world);

    public static string Key(World world, CharacterId who) =>
        world.Characters.TryResolveNext(who, out int s) && world.Characters.Cold(s) is { } cold
            ? Key(who, cold.Name, LastingActors.Of(cold)) : who.ToString();

    internal static void Remember(World world, CharacterId who, CharacterCold cold, string? town, string text, RumourTopic topic = RumourTopic.Any)
    {
        ulong lasting = LastingActors.Of(cold);
        // Give a lasting ID only to a speaker the squad actually heard. Dialogue checks only read it.
        if (lasting == 0) lasting = LastingActors.Grant(world, who);
        For(world).Told(Key(who, cold.Name, lasting), town, text, world.GameHours, topic);
    }

    internal static void Delivered(World world, Meitou.Simulation.Dialogue.DialogueSystem? dialogue, PopulationData? data,
        CharacterId speaker, RumourTopic topic)
    {
        if (dialogue is null) return;
        var characters = world.Characters;
        if (!characters.TryResolveNext(speaker, out int slot) || characters.Cold(slot) is not { IsPlayer: false, Animal: null } cold)
            return;
        var at = characters.Next[slot].Position;
        if (!InEarshot(world, at)) return;
        // 0.17 applies the effect before showing the bubble. Read it on the next tick.
        foreach (var bubble in dialogue.PublishSpeech())
        {
            if (bubble.Speaker != speaker || string.IsNullOrWhiteSpace(bubble.Text)) continue;
            string? town = data is not null && data.TownAt(at) is >= 0 and var site && site < data.Sites.Count
                ? data.Sites[site].Town.Name : null;
            Remember(world, speaker, cold, town, bubble.Text.Trim(), topic);
            return;
        }
    }

    internal static bool InEarshot(World world, Vector3 at)
    {
        var characters = world.Characters;
        var hot = characters.Next;
        for (int i = 0; i < hot.Length; i++)
        {
            if (!hot[i].Alive || characters.Cold(i) is not { IsPlayer: true, Animal: null, Medical: not { Dead: true } }) continue;
            const float range = 500;  // the same 50 m as the Atlas's earshot
            float dx = hot[i].Position.X - at.X, dz = hot[i].Position.Z - at.Z;
            if (dx * dx + dz * dz <= range * range) return true;
        }
        return false;
    }

    static string Key(CharacterId who, string name, ulong lasting) => lasting == 0
        ? $"{who}|{name}"
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"actor:{lasting}");
}
