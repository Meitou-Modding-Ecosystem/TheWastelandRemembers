using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

internal sealed class OutpostMemories(PopulationData? data, PopulationSystem? population, string modId) :
    ICombatHitObserver, IModSaveData, IStateHashed
{
    internal enum PanelStatus { None, Recorded, Defended }

    const string Prefix = "outpost:";
    const int Format = 3;
    const int DefenceTag = 0x31415244; // DRA1
    const int TrackedRaids = 4, RecentRaids = 4;
    static readonly PropertyInfo? townTags = typeof(PlayerTown).GetProperty("ModTags");
    static readonly ConditionalWeakTable<World, OutpostMemories> worlds = new();
    static readonly HashSet<string> WitnessFactions = new(StringComparer.Ordinal)
        { "Nomads", "Drifters", "Free Traders", "Traders Guild", "Trade Ninjas", "Tech Hunters", "Western Hive" };

    sealed class Outpost(string name, double founded, bool existing)
    {
        public string Name = name;
        public readonly double Founded = founded;
        public readonly bool Existing = existing;
        public bool HasShelter;
        public double? FirstWall;
        public TradeVisit? FirstVisit;
        public Defence? FirstDefence;
        public List<Defence>? FurtherDefences;
        public int FurtherRaidsFought;
        public int RaidsFought => FurtherRaidsFought + (FirstDefence?.Ended is null ? 0 : 1);
    }

    sealed record TradeVisit(string Faction, double Hours);

    sealed class Defence(string factionId, string faction, string campaign, int instance)
    {
        public readonly string FactionId = factionId, Faction = faction, Campaign = campaign;
        public readonly int Instance = instance;
        public double? Ended;
        public readonly SortedDictionary<string, string> Fallen = new(StringComparer.Ordinal);
        public bool RumourReported;
    }

    readonly SortedDictionary<string, Outpost> outposts = new(StringComparer.Ordinal);
    readonly SortedSet<string> wallsWithMaterials = new(StringComparer.Ordinal);
    readonly Published shown = new();
    static OutpostMemories? latest;
    volatile string chronicle = "", defenceSummary = "";
    volatile IReadOnlyDictionary<int, (int Owner, string Name, PanelStatus Status)> panel =
        new Dictionary<int, (int, string, PanelStatus)>();
    bool surveyed;

    internal static bool Available => townTags is not null && typeof(IDictionary<string, string>).IsAssignableFrom(townTags.PropertyType);
    internal static OutpostMemories? For(World world) => worlds.TryGetValue(world, out var state) ? state : null;
    internal static string Chronicle => LwMod.Settings.RememberOutposts ? Volatile.Read(ref latest)?.chronicle ?? "" : "";
    internal static string DefenceSummary => RecordDefences ? Volatile.Read(ref latest)?.defenceSummary ?? "" : "";
    static bool RecordDefences => LwMod.Settings.RememberOutposts && LwMod.Settings.OutpostDefenceRecord;
    internal static void ResetPresentation() => Volatile.Write(ref latest, null);

    internal static PanelStatus StatusInPanel(bool owned, int town, string name, int owner)
    {
        if (!owned || !LwMod.Settings.RememberOutposts || Volatile.Read(ref latest) is not { } memories ||
            !memories.panel.TryGetValue(town, out var status) || status.Owner != owner || status.Name != name) return PanelStatus.None;
        return status.Status;
    }

    internal static void Register(ModContext context)
    {
        if (!Available)
        {
            context.Log("outpost memory: this Meitou has no saved player-town tags");
            return;
        }
        context.AddSystem("lw-outpost-memories", c => new OutpostMemories(c.Data, c.Find<PopulationSystem>(), context.Id), SystemAnchor.End);
        WallConstructionWatch.Register(context);
        context.AddDebugCommand(UiLanguage.Text("Wasteland Remembers: outposts"), _ => !LwMod.Settings.RememberOutposts ? "off" :
            Volatile.Read(ref latest)?.shown.Text ?? "no world yet", hasField: false);
    }

    IDictionary<string, string>? Tags(PlayerTown town) => townTags?.GetValue(town) as IDictionary<string, string>;

    public void SlowWorld(World world)
    {
        if (For(world) is null) worlds.Add(world, this);
        Volatile.Write(ref latest, this);
        if (!Available || !LwMod.Settings.RememberOutposts || world.Player.Faction < 0 ||
            world.Tick % 150 != 105 || LwDeeds.For(world) is not { } deeds || data is null) return;
        try { Survey(world, deeds); }
        catch (Exception e) { LwMod.Log($"outpost memory: skipped a survey ({e.GetBaseException().Message})"); }
    }

    bool IsShelter(BuildSite site) => site.Parent is null && data?.Db.Find(site.Record) is { } record &&
        record.GetReferences("interior").Count > 0 && !record.GetBool("links together") && !record.GetBool("is wall gate");

    bool IsIndoorItem(BuildSite site) => site.Parent is not null && data?.Db.Find(site.Record) is { } record &&
        record.GetBool("is interior furniture") && !record.GetBool("is exterior furniture") && !record.GetBool("is node");

    bool IsPlayerWall(World world, BuildSite site) =>
        site.Owner == world.Player.Faction && site.Town > 0 && site.LevelPlacement is null && site.Parent is null &&
        !site.Destroyed && data?.Db.Find(site.Record) is { } record && record.GetBool("links together") && !record.GetBool("is wall gate");

    internal void WallMaterialDelivered(World world, string id)
    {
        if (!LwMod.Settings.RememberOutposts || world.Player.Faction < 0 || world.Buildings.Find(id) is not { } wall ||
            !IsPlayerWall(world, wall) || world.Buildings.FindTown(wall.Town) is not { } town || town.Owner != world.Player.Faction) return;
        if (Tags(town)?.TryGetValue(modId, out string? key) == true &&
            outposts.TryGetValue(key, out var post) && post.FirstWall is not null) return;
        wallsWithMaterials.Add(id);
    }

    void Survey(World world, LwDeeds deeds)
    {
        int player = world.Player.Faction;
        var completed = world.Buildings.All.Where(s => s.Owner == player && s.LevelPlacement is null &&
            s.Complete && !s.Destroyed && s.Town > 0).ToLookup(s => s.Town);
        wallsWithMaterials.RemoveWhere(id => world.Buildings.Find(id) is not { } wall || !IsPlayerWall(world, wall));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        VisitorRoster? visitors = null;
        var panelRows = OutpostTownPanel.Available ? new Dictionary<int, (int, string, PanelStatus)>() : null;
        foreach (var town in world.Buildings.Towns)
        {
            if (town.Owner != player || Tags(town) is not { } tags) continue;
            var sites = completed[town.Number];
            var shelters = sites.Where(IsShelter).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            if (!tags.TryGetValue(modId, out var key))
            {
                // a finished shelter is enough; one with furniture inside is preferred (play test of bf: an empty storm house and a cage
                // never counted until a bed went in)
                string? anchor = sites.Where(s => IsIndoorItem(s) && shelters.Contains(s.Parent!))
                    .Select(s => s.Parent).Order(StringComparer.Ordinal).FirstOrDefault() ?? shelters.Order(StringComparer.Ordinal).FirstOrDefault();
                if (anchor is null) continue;
                key = Prefix + anchor;
                tags.Add(modId, key);
            }
            if (!key.StartsWith(Prefix, StringComparison.Ordinal) || !seen.Add(key)) continue;
            if (!outposts.TryGetValue(key, out var post))
            {
                if (shelters.Count == 0) continue;
                post = new Outpost(town.Name, world.GameHours, !surveyed);
                outposts.Add(key, post);
                string line = post.Existing ? $"The squad first recorded {post.Name}." : $"The squad founded {post.Name}.";
                Record(world, deeds, town.Position, post.Name, line, 65);
            }
            post.Name = town.Name;
            post.HasShelter = shelters.Count > 0;
            if (wallsWithMaterials.Count > 0 && post.HasShelter)
            {
                bool finishedWall = false;
                foreach (var site in sites)
                    finishedWall |= wallsWithMaterials.Remove(site.Id);
                if (finishedWall && post.FirstWall is null)
                {
                    post.FirstWall = world.GameHours;
                    Record(world, deeds, town.Position, post.Name, $"The squad finished a defensive wall at {post.Name}.", 45);
                }
            }
            if (post.FirstDefence is { Ended: null } defence && FinishDefence(world, town, defence))
                Record(world, deeds, town.Position, post.Name, DefenceLine(post, defence, first: true), 75);
            if (RecordDefences && post.FurtherDefences is { } others)
                FinishFurtherDefences(world, deeds, town, post, others);
            ObserveVisitors(world, deeds, town, key, post, ref visitors);
            if (post.HasShelter && panelRows is not null)
                panelRows.Add(town.Number, (town.Owner, post.Name, post.FirstDefence?.Ended is null ? PanelStatus.Recorded : PanelStatus.Defended));
        }
        foreach (var (key, post) in outposts)
            if (!seen.Contains(key)) post.HasShelter = false;
        surveyed = true;
        if (panelRows is not null) panel = panelRows;
        if (RecordDefences)
            defenceSummary = string.Join(Environment.NewLine, outposts.Values.Where(post => post.RaidsFought > 0)
                .Select(post => $"{post.Name}: {RaidsLine(post)}"));
        string separator = Environment.NewLine;
        chronicle = string.Join(separator + separator, AtlasRows().Select(row => string.Join(separator, row.Page)));
        shown.Set(() => $"{outposts.Count} recorded, {outposts.Values.Count(p => p.HasShelter)} with shelter, " +
            $"{outposts.Values.Count(p => p.FirstDefence?.Ended is not null)} with a first defence");
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        if (!LwMod.Settings.RememberOutposts || !Available || population is null || data is null || hit.Attacker.IsNone) return;
        var table = world.Characters;
        if (!table.TryResolveNext(hit.Attacker, out int a) || table.Cold(a) is not { } attacker ||
            !table.TryResolveNext(hit.Victim, out int v) || table.Cold(v) is not { } victim ||
            attacker.IsPlayer == victim.IsPlayer || attacker.IsImprisoned || victim.IsImprisoned ||
            ArenaLink.Sport(hit.Attacker, hit.Victim)) return;
        var raider = attacker.IsPlayer ? victim : attacker;
        if (world.Squads.Find(raider.SquadId) is not { PlatoonId: >= 0 } squad ||
            world.Platoons.Find(squad.PlatoonId)?.Campaign is not { Phase: 1 } campaign ||
            campaign.Faction < 0 || campaign.Faction >= data.Factions.Count || campaign.Enemy != world.Player.Faction ||
            population.CampaignOf(campaign.CampaignId) is not { IsHostile: true } ||
            world.Buildings.FindTown(campaign.Target.PlayerTown) is not { } town || town.Owner != world.Player.Faction ||
            Tags(town) is not { } tags || !tags.TryGetValue(modId, out var key) || !outposts.TryGetValue(key, out var post) ||
            !post.HasShelter || OtherModsPeople.Runs(world, attacker.IsPlayer ? attacker : victim) ||
            !Within(table.Next[a].Position, town) || !Within(table.Next[v].Position, town)) return;
        var faction = data.Factions[campaign.Faction];
        var first = post.FirstDefence ??= new Defence(faction.Id, faction.Name, campaign.CampaignId, campaign.Id);
        var defence = SameRaid(first, faction.Id, campaign) ? first : RecordDefences ? FurtherDefence(post, faction.Id, faction.Name, campaign) : null;
        if (defence is null || defence.Ended is not null) return;
        if (victim is { IsPlayer: true, Animal: null, Medical.Dead: true } && hit.VictimWasAlive)
            defence.Fallen.TryAdd(LwDeeds.Identity(victim), victim.Name);
    }

    static bool SameRaid(Defence defence, string factionId, CampaignInstance campaign) =>
        defence.FactionId == factionId && defence.Instance == campaign.Id && defence.Campaign == campaign.CampaignId;

    static bool Within(Vector3 at, PlayerTown town)
    {
        float x = at.X - town.Position.X, z = at.Z - town.Position.Z;
        return x * x + z * z <= town.Radius * town.Radius;
    }

    static Defence? FurtherDefence(Outpost post, string factionId, string faction, CampaignInstance campaign)
    {
        var others = post.FurtherDefences ??= [];
        int active = 0;
        foreach (var defence in others)
        {
            if (SameRaid(defence, factionId, campaign)) return defence;
            if (defence.Ended is null) active++;
        }
        if (active >= TrackedRaids) return null;
        var next = new Defence(factionId, faction, campaign.CampaignId, campaign.Id);
        others.Add(next);
        return next;
    }

    bool FinishDefence(World world, PlayerTown town, Defence defence)
    {
        if (data is null || population is null) return false;
        int faction = data.FactionIndex(defence.FactionId);
        if (population.Wars.Any(w => w.Faction == faction && w.Actives.Any(c => c.Phase < 2 &&
            c.Target.PlayerTown == town.Number && SameRaid(defence, defence.FactionId, c)))) return false;

        defence.Ended = world.GameHours;
        return true;
    }

    void FinishFurtherDefences(World world, LwDeeds deeds, PlayerTown town, Outpost post, List<Defence> others)
    {
        int completed = 0;
        foreach (var defence in others)
        {
            if (defence.Ended is null && FinishDefence(world, town, defence))
            {
                post.FurtherRaidsFought++;
                Record(world, deeds, town.Position, post.Name, DefenceLine(post, defence, first: false), 65);
            }
            if (defence.Ended is not null) completed++;
        }

        while (completed > RecentRaids)
        {
            int oldest = -1;
            for (int i = 0; i < others.Count; i++)
                if (others[i].Ended is double ended && (oldest < 0 || ended < others[oldest].Ended)) oldest = i;
            others.RemoveAt(oldest);
            completed--;
        }
    }

    void ObserveVisitors(World world, LwDeeds deeds, PlayerTown town, string key, Outpost post, ref VisitorRoster? visitors)
    {
        bool firstVisit = town.Public && post.HasShelter && post.FirstVisit is null;
        var defence = post.FirstDefence;
        var news = defence is { Ended: double ended, RumourReported: false } && world.GameHours - ended <= 24 * 7
            ? NewsSystem.For(world) : null;
        if (data is null || !firstVisit && news is null) return;

        visitors ??= FindVisitors(world, data);
        var people = visitors.Players.Where(at => Within(at, town)).ToList();
        if (people.Count == 0) return;
        foreach (var (witness, visitor) in visitors.Locals)
        {
            if (!Within(visitor, town) || !people.Any(at => Vector3.DistanceSquared(at, visitor) <= 500 * 500)) continue;
            if (firstVisit && TraderIsVisiting(world, witness, town))
            {
                string faction = data.Factions[witness.Faction].Name;
                post.FirstVisit = new TradeVisit(faction, world.GameHours);
                Record(world, deeds, town.Position, post.Name, $"A trader from {KeepsakeText.Faction(faction)} visited {post.Name}.", 45);
                firstVisit = false;
            }
            if (news is not null && defence is not null)
            {
                string line = $"There's been fighting at {post.Name}. {defence.Faction} sent raiders there.";
                defence.RumourReported = news.ReportOutpost(key, post.Name, town.Position, line);
                if (defence.RumourReported) LwMod.Log($"outpost memory: word of {post.Name}'s first defence reached the roads");
                news = null;
            }
            if (!firstVisit && news is null) return;
        }
    }

    sealed record VisitorRoster(List<Vector3> Players, List<(CharacterCold Actor, Vector3 At)> Locals);

    static VisitorRoster FindVisitors(World world, PopulationData data)
    {
        var players = new List<Vector3>();
        var locals = new List<(CharacterCold, Vector3)>();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            ref readonly var hot = ref table.Next[slot];
            if (!hot.Alive || hot.Dormant || table.Cold(slot) is not
                { Animal: null, InCombat: false, IsImprisoned: false, Medical: { Dead: false, Incapacitated: false } } actor ||
                !actor.CarriedBy.IsNone) continue;
            if (actor.IsPlayer)
            {
                if (!OtherModsPeople.Runs(world, actor) && !Sleep.Is(world, table.IdOf(slot))) players.Add(hot.Position);
                continue;
            }
            if (actor.Faction >= 0 && actor.Faction < data.Factions.Count &&
                data.Relations.Get(actor.Faction, world.Player.Faction) > -30 &&
                WitnessFactions.Contains(data.Factions[actor.Faction].Name) && !Sleep.Is(world, table.IdOf(slot)))
                locals.Add((actor, hot.Position));
        }
        return new(players, locals);
    }

    static bool TraderIsVisiting(World world, CharacterCold witness, PlayerTown town) =>
        witness.SquadId >= 0 && world.Squads.Find(witness.SquadId)?.Ai is
            { TraderState: 1 or 2, CurrentClass: PackageClass.WanderingTrader or PackageClass.Bar or PackageClass.BarOutdoors } board &&
        board.InTown.PlayerTown == town.Number && board.Destination == board.InTown;

    static string RaidsLine(Outpost post) => post.RaidsFought == 1 ? "1 raid fought." : $"{post.RaidsFought} raids fought.";

    static string DefenceLine(Outpost post, Defence defence, bool first) =>
        (first ? $"Your squad defended {post.Name} against raiders from {KeepsakeText.Faction(defence.Faction)}."
               : $"The squad fought raiders from {KeepsakeText.Faction(defence.Faction)} at {post.Name}.") +
        (defence.Fallen.Count > 0 ? $" Fallen in the fighting: {string.Join(", ", defence.Fallen.Values)}." : "");

    static void Record(World world, LwDeeds deeds, Vector3 at, string place, string line, int weight)
    {
        deeds.Record.Note(line, world.GameHours, place);
        PlaytestLog.Event(world, "outpost", place, "recorded", line);
        RecapLink.Moment(line, place, weight, at);
        LwMod.Log($"outpost memory: {line}");
    }

    internal (string Name, double Hours)? FirstFoundation() =>
        outposts.Values.Where(post => !post.Existing).MinBy(post => post.Founded) is { } first ? (first.Name, first.Founded) : null;

    internal IEnumerable<(string Key, string Name, List<string> Page)> AtlasRows()
    {
        if (!LwMod.Settings.RememberOutposts) yield break;
        foreach (var (key, post) in outposts)
        {
            var page = new List<string> { post.Name, "", $"{(post.Existing ? "First recorded" : "Founded")} on day {(int)(post.Founded / 24) + 1}.",
                post.HasShelter ? "A finished shelter stands here." : "There is no finished shelter here now." };
            if (post.FirstWall is { } wall)
                page.Add($"First defensive wall recorded on day {(int)(wall / 24) + 1}.");
            if (post.FirstVisit is { } visit)
                page.Add($"First trader from {KeepsakeText.Faction(visit.Faction)}, day {(int)(visit.Hours / 24) + 1}.");
            if (post.FirstDefence is { Ended: double ended } defence)
            {
                page.Add($"First defence, day {(int)(ended / 24) + 1}.");
                page.Add(DefenceLine(post, defence, first: true));
            }
            if (RecordDefences && post.RaidsFought > 0)
            {
                page.Add($"Raids fought: {post.RaidsFought}.");
                if (post.FurtherDefences is { } others)
                    foreach (var recent in others.Where(raid => raid.Ended is not null).OrderByDescending(raid => raid.Ended))
                        page.Add(DefenceLine(post, recent, first: false));
            }
            yield return (key, post.Name, page);
        }
    }

    internal IEnumerable<(string Key, PlayerTown Town)> CourierTowns(World world) =>
        FoundedTowns(world).Where(p => p.Town.Public);

    internal IEnumerable<(string Key, PlayerTown Town)> FoundedTowns(World world)
    {
        if (!LwMod.Settings.RememberOutposts) yield break;
        foreach (var town in world.Buildings.Towns)
            if (town.Owner == world.Player.Faction && Tags(town)?.TryGetValue(modId, out var key) == true &&
                outposts.TryGetValue(key, out var post) && post.HasShelter)
                yield return (key, town);
    }

    internal static bool CanHearCourier(World world, PlayerTown town, CharacterId trader)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(trader, out int slot) || !Standing(slot, trader, player: false)) return false;
        for (int i = 0; i < table.HighWater; i++)
            if (Standing(i, table.IdOf(i), player: true) &&
                Vector3.DistanceSquared(table.Next[i].Position, table.Next[slot].Position) <= 500 * 500) return true;
        return false;

        bool Standing(int s, CharacterId id, bool player) => table.Next[s].Alive && !table.Next[s].Dormant &&
            table.Cold(s) is { Animal: null, InCombat: false, IsImprisoned: false, Medical: { Dead: false, Incapacitated: false } } cold &&
            cold.IsPlayer == player && cold.CarriedBy.IsNone && Within(table.Next[s].Position, town) &&
        (!player || !OtherModsPeople.Runs(world, cold)) && !Sleep.Is(world, id);
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Format); w.Write(surveyed); w.Write(outposts.Count);
        foreach (var (key, post) in outposts)
        {
            w.Write(key); w.Write(post.Name); w.Write(post.Founded); w.Write(post.Existing); w.Write(post.HasShelter);
            w.Write(post.FirstWall ?? -1);
            w.Write(post.FirstVisit is not null);
            if (post.FirstVisit is { } visit) { w.Write(visit.Faction); w.Write(visit.Hours); }
            w.Write(post.FirstDefence is not null);
            if (post.FirstDefence is { } defence) WriteDefence(w, defence);
        }
        w.Write(wallsWithMaterials.Count);
        foreach (string id in wallsWithMaterials) w.Write(id);
        int extra = outposts.Values.Count(post => post.FurtherDefences is { Count: > 0 });
        if (extra == 0) return;
        w.Write(DefenceTag); w.Write(extra);
        foreach (var (key, post) in outposts)
        {
            if (post.FurtherDefences is not { Count: > 0 } others) continue;
            w.Write(key); w.Write(post.FurtherRaidsFought); w.Write(others.Count);
            foreach (var defence in others) WriteDefence(w, defence);
        }
    }

    static void WriteDefence(BinaryWriter writer, Defence defence)
    {
        writer.Write(defence.FactionId); writer.Write(defence.Faction); writer.Write(defence.Campaign); writer.Write(defence.Instance);
        writer.Write(defence.Ended ?? -1); writer.Write(defence.RumourReported); writer.Write(defence.Fallen.Count);
        foreach (var (id, name) in defence.Fallen) { writer.Write(id); writer.Write(name); }
    }

    static Defence ReadDefence(BinaryReader reader)
    {
        var defence = new Defence(reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadInt32());
        double ended = reader.ReadDouble();
        defence.Ended = ended >= 0 ? ended : null;
        defence.RumourReported = reader.ReadBoolean();
        for (int count = reader.ReadInt32(); count > 0; count--) defence.Fallen.Add(reader.ReadString(), reader.ReadString());
        return defence;
    }

    public void Read(Stream stream)
    {
        outposts.Clear();
        wallsWithMaterials.Clear();
        surveyed = false;
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int version = stream.Position == stream.Length ? 0 : r.ReadInt32();
        if (version is not (1 or 2 or Format)) return;
        surveyed = r.ReadBoolean();
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            string key = r.ReadString();
            var post = new Outpost(r.ReadString(), r.ReadDouble(), r.ReadBoolean()) { HasShelter = r.ReadBoolean() };
            outposts.Add(key, post);
            if (version >= 3 && r.ReadDouble() is >= 0 and var wall) post.FirstWall = wall;
            if (version >= 2 && r.ReadBoolean()) post.FirstVisit = new TradeVisit(r.ReadString(), r.ReadDouble());
            if (r.ReadBoolean()) post.FirstDefence = ReadDefence(r);
        }
        if (version < 3) return;
        for (int count = r.ReadInt32(); count > 0; count--) wallsWithMaterials.Add(r.ReadString());

        if (stream.Length - stream.Position < 8 || r.ReadInt32() != DefenceTag) return;
        int extra = r.ReadInt32();
        if (extra < 0 || extra > outposts.Count) return;
        for (int i = 0; i < extra; i++)
        {
            string key = r.ReadString();
            int total = r.ReadInt32(), count = r.ReadInt32();
            if (total < 0 || count is < 1 or > TrackedRaids + RecentRaids ||
                !outposts.TryGetValue(key, out var post) || post.FurtherDefences is not null) return;
            var others = new List<Defence>(count);
            for (int j = 0; j < count; j++) others.Add(ReadDefence(r));
            post.FurtherRaidsFought = total;
            post.FurtherDefences = others;
        }
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(surveyed ? 1 : 0);
        h.Add(outposts.Count);
        h.Add(wallsWithMaterials.Count);
        foreach (string id in wallsWithMaterials) h.Add(SquadRecord.StableHash(id));
        foreach (var (key, post) in outposts)
        {
            h.Add(SquadRecord.StableHash(key)); h.Add(SquadRecord.StableHash(post.Name)); h.Add(post.Founded);
            h.Add(post.Existing ? 1 : 0); h.Add(post.HasShelter ? 1 : 0);
            h.Add(post.FirstWall ?? -1);
            h.Add(post.FirstVisit is null ? 0 : 1);
            if (post.FirstVisit is { } visit) { h.Add(SquadRecord.StableHash(visit.Faction)); h.Add(visit.Hours); }
            h.Add(post.FirstDefence is null ? 0 : 1);
            if (post.FirstDefence is { } defence) HashDefence(ref h, defence);
            if (post.FurtherDefences is { Count: > 0 } others)
            {
                h.Add(DefenceTag); h.Add(post.FurtherRaidsFought); h.Add(others.Count);
                foreach (var other in others) HashDefence(ref h, other);
            }
        }
    }

    static void HashDefence(ref StateHasher h, Defence defence)
    {
        h.Add(SquadRecord.StableHash(defence.FactionId)); h.Add(SquadRecord.StableHash(defence.Faction));
        h.Add(SquadRecord.StableHash(defence.Campaign)); h.Add(defence.Instance); h.Add(defence.Ended ?? -1);
        h.Add(defence.RumourReported ? 1 : 0); h.Add(defence.Fallen.Count);
        foreach (var (id, name) in defence.Fallen) { h.Add(SquadRecord.StableHash(id)); h.Add(SquadRecord.StableHash(name)); }
    }
}
