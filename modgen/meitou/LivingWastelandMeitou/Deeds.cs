// The squad's record (what each member has done) and the titles earned from it. Pure logic, no engine types: RecordSystem (Notoriety.cs)
// feeds it from the combat-hit observer and a once-a-second look at the player characters, and the unit tests drive it directly. Shared by the
// Living Wasteland helper and the stand-alone Squad Record mod.
namespace LivingWasteland.MeitouMod;

/// <summary>One player character's record. Keyed by the character's name (a renamed character starts a new record).</summary>
public sealed class MemberRecord
{
    public int Kills, Knockouts, LeadersKilled, NamedKills, LimbsCut, TimesDowned, LimbsLost, Bites, Crafted, Fights, FightsWon, LastStands;

    /// <summary>Kills of someone already down (an execution, or finishing a downed enemy).</summary>
    public int Executions;

    /// <summary>Killers of fallen squadmates this member has killed.</summary>
    public int Avenged;

    /// <summary>Wounds remembered: where this member lost a limb or was knocked down, in towns only (newest last, at most 12).</summary>
    public List<Scar> Scars { get; } = [];
    public double DamageDealt, DamageTaken, Distance, Food;
    /// <summary>World units ridden on an animal and sailed (Meitou Transport, TransportLink.cs); not in Distance.</summary>
    public double Ridden, Sailed;

    /// <summary>Items made at benches, by kind (weapons, armour, crossbows, robotics, items).</summary>
    public SortedDictionary<string, int> CraftedBy { get; } = new(StringComparer.Ordinal);

    /// <summary>Kills by the victim's faction name.</summary>
    public SortedDictionary<string, int> KillsBy { get; } = new(StringComparer.Ordinal);

    /// <summary>Seconds spent per activity (fighting, first aid, working, building, hauling, looting, sleeping, travelling, sneaking).</summary>
    public SortedDictionary<string, double> Seconds { get; } = new(StringComparer.Ordinal);

    /// <summary>Titles this member holds, in the order earned.</summary>
    public List<string> Titles { get; } = [];

    /// <summary>Named characters this member killed (their record names).</summary>
    public List<string> Named { get; } = [];

    public int KillsOf(string faction) => KillsBy.GetValueOrDefault(faction);

    public double SecondsOf(string activity) => Seconds.GetValueOrDefault(activity);
}

/// <summary>
/// A title rule from lw_codes.json: who holds it and what earns it. Stats: kills (of <see cref="Faction"/> when set), knockouts, leaders, named,
/// limbs, downed, limbs_lost, damage, distance (world units, 10 a metre: Meitou docs "world units (0.1 m)"), food (hunger points), crafted (of the kind in <see cref="Faction"/> when set:
/// weapons, armour, crossbows, robotics, items), hours:&lt;activity&gt;; "named" with <see cref="Targets"/> needs ALL of
/// them dead (one target: that one). A target may list alternatives separated by '|'.
/// </summary>
public sealed record TitleRule(string Id, string Name, string Scope, string Stat, string? Faction, IReadOnlyList<string>? Targets, int At,
    bool Legendary = false)
{
    public static bool Matches(string target, string name) =>
        target.Split('|').Any(t => string.Equals(t.Trim(), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>How the title is earned, for the catalog.</summary>
    public string Requirement() => Stat switch
    {
        "named" when Targets is { Count: 1 } t => $"kill {Pretty(t[0])}",
        "named" when Targets is { Count: > 1 } t => $"kill all of: {string.Join(", ", t.Select(Pretty))}",
        "named" => $"{At} named characters killed",
        "kills" when Faction is not null => $"{At} {Faction} killed",
        "kills" => $"{At} kills",
        "knockouts" => $"{At} knockouts",
        "executions" => $"{At} of the fallen finished off",
        "leaders" => $"{At} squad leaders killed",
        "limbs" => $"{At} limbs cut off",
        "downed" => $"downed {At} times and got up",
        "limbs_lost" => $"{At} limbs lost",
        "damage" => $"{At} damage dealt",
        "distance" => $"{At / 10000} km walked",
        "ridden" => $"{At / 10000} km ridden",
        "sailed" => $"{At / 10000} km sailed",
        "food" => $"{At} food eaten",
        "fights" => $"{At} fights",
        "fights_won" => $"{At} fights won",
        "avenged" => At == 1 ? "kill the one who killed a squadmate" : $"avenge {At} fallen squadmates",
        "last_stands" => At == 1 ? "the last one standing in a won fight" : $"the last one standing in {At} won fights",
        "crafted" => Faction is null ? $"{At} items crafted" : $"{At} {Faction} crafted",
        "guild_rank" => $"reach guild rank {"FEDCBAS"[Math.Clamp(At, 0, 6)]} (with Guild & Quests)",
        "guild_jobs" => $"{At} guild jobs done (with Guild & Quests)",
        "guild_seals" => $"{At} guild seals on the charter (with Guild & Quests)",
        "raids_repelled" => $"{At} raids on your base beaten off (with Raid Commander)",
        "alms" => $"{At} cats given as alms or offerings",
        _ when Stat.StartsWith("hours:", StringComparison.Ordinal) => $"{At} hours {Stat[6..]}",
        _ => Stat,
    } + (Scope == "squad" ? " (whole squad)" : "");

    static string Pretty(string target) => target.Split('|')[0].Trim();
}

/// <summary>
/// The squad's record: a <see cref="MemberRecord"/> per player character, the crew's named kills, and the titles. Squad titles (scope "squad")
/// belong to the whole crew, so a recruit who joins later carries them too; member titles (scope "member") belong to the one who earned them.
/// </summary>
public sealed class SquadRecord(IReadOnlyList<TitleRule> rules)
{
    /// <summary>History entries kept (oldest dropped first).</summary>
    public const int MaxHistory = 300;

    readonly SortedDictionary<string, MemberRecord> members = new(StringComparer.Ordinal);
    readonly List<(double Hours, string Place, string Text)> history = [];
    readonly FightState fight = new();

    /// <summary>Called with a caption and a weight (Recap's 0-100 scale) for moments worth a recap: titles and last stands.</summary>
    public Action<string, int>? OnMoment;

    /// <summary>The game hour and place the engine side sets before it reports a deed, for the history entries.</summary>
    public double Hours;
    public string Place = "the wilds";

    /// <summary>The squad's story, oldest first: titles, named kills, first kills, lost limbs, big fights (and, from the helper, heat).</summary>
    public IReadOnlyList<(double Hours, string Place, string Text)> History => history;

    public void Note(string text)
    {
        history.Add((Hours, Place, text));
        if (history.Count > MaxHistory) history.RemoveAt(0);
    }
    readonly List<string> crewTitles = [];

    /// <summary>Numbers that belong to the squad as a whole, fed by other mods (CompanionLink.cs: Guild &amp; Quests' rank, jobs and seals, raids
    /// Raid Commander saw beaten off), and the keys of events already noted, so a reload never notes one twice. Saved (record format 12).</summary>
    readonly SortedDictionary<string, double> crewStats = new(StringComparer.Ordinal);
    readonly SortedSet<string> events = new(StringComparer.Ordinal);

    public double CrewStat(string key) => crewStats.GetValueOrDefault(key);

    /// <summary>Whether anyone of the squad, or the squad itself, holds a title (tavern tales).</summary>
    public bool AnyHolds(string title) => crewTitles.Contains(title) || members.Values.Any(m => m.Titles.Contains(title));
    public bool HasCrewStat(string key) => crewStats.ContainsKey(key);

    /// <summary>Sets a squad number; returns the squad titles it newly earns (said once).</summary>
    public List<string> SetCrewStat(string key, double value)
    {
        if (crewStats.TryGetValue(key, out double was) && was == value) return [];
        crewStats[key] = value;
        return AwardCrew();
    }

    /// <summary>True the first time an event key is seen (then remembered).</summary>
    public bool NewEvent(string key) => events.Add(key);
    readonly List<string> namedDead = [];
    readonly Dictionary<string, double> namedHours = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, MemberRecord> Members => members;

    public IReadOnlyList<string> CrewTitles => crewTitles;

    public IReadOnlyList<string> NamedDead => namedDead;

    // ---- who is who: members are keyed by the character's identity, not its name ----

    readonly Dictionary<string, string> keyOf = new(StringComparer.Ordinal);       // identity -> member key
    readonly Dictionary<string, string> shown = new(StringComparer.Ordinal);       // member key -> current name

    /// <summary>
    /// The member key for a character: its identity (Meitou: portrait serial plus record id, saved with the character) maps to the key it
    /// got when first seen, so a renamed character keeps its record and two characters with one name get two ("Beep (2)"). A record from
    /// before identities (keyed by name) is adopted by the first character of that name.
    /// </summary>
    public string KeyFor(string identity, string name)
    {
        if (keyOf.TryGetValue(identity, out var key))
        {
            if (shown.GetValueOrDefault(key) != name)
            {
                if (shown.ContainsKey(key)) Note($"{shown[key]} goes by {name} now.");
                shown[key] = name;
            }
            return key;
        }
        key = name;
        var taken = new HashSet<string>(keyOf.Values, StringComparer.Ordinal);
        for (int n = 2; taken.Contains(key); n++) key = $"{name} ({n})";
        keyOf[identity] = key;
        shown[key] = name;
        return key;
    }

    /// <summary>The key for an identity without changing anything (for dialogue conditions, which must only read): its key when known,
    /// else the name, as <see cref="KeyFor"/> would first give it.</summary>
    public string PeekKey(string identity, string name) => keyOf.TryGetValue(identity, out var key) ? key : name;

    /// <summary>A member's current name (its key when it has no identity yet).</summary>
    public string Display(string key) => shown.TryGetValue(key, out var n) ? n : key;

    string D(string key) => Display(key);

    // ---- memorials: the squad's fallen, and the feuds they leave ----

    readonly List<Fallen> fallen = [];
    readonly SortedDictionary<string, double> feuds = new(StringComparer.Ordinal);      // faction -> game hour of the last loss to it
    readonly Dictionary<string, double> firstSeen = new(StringComparer.Ordinal);       // member -> game hour first seen in the squad

    /// <summary>The squad's dead, oldest first.</summary>
    public IReadOnlyList<Fallen> FallenMembers => fallen;

    /// <summary>Factions that killed one of the squad (with when, for "lately").</summary>
    public IReadOnlyDictionary<string, double> Feuds => feuds;

    /// <summary>Notes when a member was first seen (for "served N days"); the engine side calls it on every look.</summary>
    public void Seen(string name, double hours) => firstSeen.TryAdd(name, hours);

    /// <summary>A member died: kept with their deeds; the faction of whoever killed them becomes a feud. Once per name.</summary>
    public void Died(string name, double hours, string place, string? killer, string? killerFaction, string? killerRecord)
    {
        if (fallen.Any(f => f.Name == name)) return;
        if (fight.Active) fight.Down.Add(name);              // the dead are not "still standing" when the fight is judged
        var m = Member(name);
        double served = firstSeen.TryGetValue(name, out var since) ? Math.Max(0, (hours - since) / 24) : 0;
        var titles = m.Titles.Select(TitleName).Concat(crewTitles.Select(TitleName)).Distinct().ToList();
        fallen.Add(new Fallen(name, hours, place, killer, killerFaction, killerRecord, m.Kills, served, titles, null));
        string by = killer is null ? "" : killerFaction is null or "" ? $", killed by {killer}" : $", killed by {killer} of {killerFaction}";
        Note($"{D(name)} fell at {place}{by}.");
        // no Recap moment: Recap shows a squadmate's death itself, with a picture (seen in play: both "Knife died in the wild" and ours)
        if (!string.IsNullOrEmpty(killerFaction)) feuds[killerFaction] = hours;
    }

    /// <summary>Whether a dead enemy was the one who killed a member (same record for a unique, else same name and faction); marks the
    /// fallen avenged and returns their names.</summary>
    public List<string> Avenge(string avenger, string victimName, string? victimFaction, string? victimRecord)
    {
        var done = new List<string>();
        for (int i = 0; i < fallen.Count; i++)
        {
            var f = fallen[i];
            if (f.AvengedBy is not null || f.Killer is null) continue;
            bool same = f.KillerRecord is not null && victimRecord is not null ? f.KillerRecord == victimRecord
                : f.Killer == victimName && (f.KillerFaction ?? "") == (victimFaction ?? "");
            if (!same) continue;
            fallen[i] = f with { AvengedBy = avenger };
            Member(avenger).Avenged++;
            Note($"{D(avenger)} avenged {D(f.Name)}.");
            OnMoment?.Invoke($"{D(avenger)} avenged {D(f.Name)}.", 85);
            done.Add(f.Name);
        }
        return done;
    }

    /// <summary>Game days since the squad last lost someone (a very large number when it never has).</summary>
    public double DaysSinceLoss(double hoursNow) => fallen.Count == 0 ? 1e6 : Math.Max(0, (hoursNow - fallen[^1].Hours) / 24);

    /// <summary>Where the most recent loss happened ("the wilds" outside towns); null when nobody has fallen.</summary>
    public string? LastLossPlace => fallen.Count == 0 ? null : fallen[^1].Place;

    /// <summary>The Fallen page: one paragraph per member, newest first.</summary>
    public List<string> FallenLines()
    {
        if (fallen.Count == 0) return ["No one has died."];
        var lines = new List<string>();
        foreach (var f in fallen.AsEnumerable().Reverse())
        {
            lines.Add($"{D(f.Name)}: fell on day {(int)(f.Hours / 24)} at {f.Place}{(f.Killer is null ? "" : $", killed by {f.Killer}{(string.IsNullOrEmpty(f.KillerFaction) ? "" : " of " + f.KillerFaction)}")}.");
            lines.Add($"   served {f.DaysServed:0} days, {f.Kills} kills{(f.Titles.Count > 0 ? ", known as " + string.Join(", ", f.Titles) : "")}{(f.AvengedBy is null ? "" : $". Avenged by {D(f.AvengedBy)}")}.");
            lines.Add($"   \"{Epitaph(f)}\"");
        }
        return lines;
    }

    /// <summary>A line for a fallen squadmate's grave, from how they lived and died (the same every time for the same record).</summary>
    public static string Epitaph(Fallen f)
    {
        if (f.AvengedBy is not null) return $"Avenged by {f.AvengedBy}.";
        if (f.Titles.Count > 0) return $"Known as {f.Titles[^1]}.";
        if (f.Kills >= 10) return $"{f.Kills} kills in {Math.Max(1, (int)f.DaysServed)} days with the squad.";
        if (f.DaysServed < 3) return "Joined the squad only days before.";
        if (f.Killer is null) return "Cause of death unknown.";
        return $"With the squad for {Math.Max(1, (int)f.DaysServed)} days.";
    }

    /// <summary>Whether the squad killed a named character (by record name; '|' alternatives as in titles), bleed-outs after its blow included.</summary>
    public bool KilledNamed(string name) => namedDead.Any(d => TitleRule.Matches(name, d));

    /// <summary>Game days since the squad killed that named character; -1 when it has not.</summary>
    public double DaysSinceNamed(string name, double hoursNow)
    {
        foreach (var d in namedDead)
            if (TitleRule.Matches(name, d) && namedHours.TryGetValue(d, out double h)) return Math.Max(0, (hoursNow - h) / 24);
        return -1;
    }

    public IReadOnlyList<TitleRule> Rules => rules;

    public MemberRecord Member(string name)
    {
        if (!members.TryGetValue(name, out var m)) members[name] = m = new MemberRecord();
        return m;
    }

    /// <summary>A kill by <paramref name="killer"/>: the victim's faction, whether it led its squad, and its name if it was a named character.</summary>
    public List<string> Kill(string killer, string faction, bool leader, string? named)
    {
        var m = Member(killer);
        m.Kills++;
        if (m.Kills == 1) Note($"{D(killer)}'s first kill.");
        if (named is not null) Note($"{D(killer)} killed {named}.");
        fight.EnemiesDown++;
        m.KillsBy[faction] = m.KillsOf(faction) + 1;
        if (leader) m.LeadersKilled++;
        if (named is not null)
        {
            m.NamedKills++;
            if (!m.Named.Contains(named)) m.Named.Add(named);
            if (!namedDead.Contains(named)) namedDead.Add(named);
            namedHours.TryAdd(named, Hours);
        }
        return Award(killer);
    }

    /// <summary>
    /// A kill by mercenaries the player hired: it belongs to the squad as a whole (crew named kills, squad titles, rumours, history), never to
    /// one member, so no member title (such as a named-kill title) is earned from a hired blade's blow.
    /// </summary>
    public List<string> CrewKill(string faction, bool leader, string? named, string by = "Your hired blades")
    {
        fight.EnemiesDown++;
        if (named is not null)
        {
            Note($"{by} killed {named}.");
            if (!namedDead.Contains(named)) namedDead.Add(named);
            namedHours.TryAdd(named, Hours);
        }
        else if (leader) Note($"{by} killed a {(faction.Length > 0 ? faction + " " : "")}squad leader.");
        return AwardCrew();
    }

    /// <summary>Checks the squad-scope rules only (nothing a single member earns).</summary>
    public List<string> AwardCrew()
    {
        var said = new List<string>();
        foreach (var r in rules)
        {
            if (r.Scope != "squad" || crewTitles.Contains(r.Id) || CrewValue(r) < r.At) continue;
            crewTitles.Add(r.Id);
            said.Add($"Your squad is now known as {r.Name}.");
            Note($"The squad became known as {r.Name}.");
            OnMoment?.Invoke($"Your squad is now known as {r.Name}.", r.Legendary ? 95 : 85);
        }
        return said;
    }

    /// <summary>The kill just recorded was of someone already down (an execution, or finishing a downed enemy).</summary>
    public List<string> Executed(string killer)
    {
        var m = Member(killer);
        m.Executions++;
        if (m.Executions == 1) Note($"{D(killer)} finished off one of the fallen for the first time.");
        return Award(killer);
    }

    public List<string> Knockout(string striker)
    {
        Member(striker).Knockouts++;
        fight.EnemiesDown++;
        return Award(striker);
    }

    public List<string> LimbCut(string striker)
    {
        Member(striker).LimbsCut++;
        return Award(striker);
    }

    public void Damage(string? striker, string? victim, double amount)
    {
        if (striker is not null) Member(striker).DamageDealt += amount;
        if (victim is not null) Member(victim).DamageTaken += amount;
    }

    public void Downed(string name)
    {
        Member(name).TimesDowned++;
        if (fight.Active) fight.Down.Add(name);
    }

    /// <summary>A bite of food worth <paramref name="amount"/> hunger points.</summary>
    public List<string> Ate(string name, double amount)
    {
        var m = Member(name);
        m.Bites++;
        m.Food += amount;
        return Award(name);
    }

    /// <summary><paramref name="count"/> items of <paramref name="kind"/> finished at a bench this member worked last.</summary>
    public List<string> Made(string name, string kind, int count)
    {
        var m = Member(name);
        m.Crafted += count;
        m.CraftedBy[kind] = m.CraftedBy.GetValueOrDefault(kind) + count;
        return Award(name);
    }

    public void LimbLost(string name) => LimbLost(name, "limb", null);

    /// <summary>A member lost an arm or a leg (<paramref name="kind"/> "arm", "leg" or "limb"); in a town it is a scar they remember there.</summary>
    public void LimbLost(string name, string kind, string? town)
    {
        var m = Member(name);
        m.LimbsLost++;
        Note($"{D(name)} lost a{(kind == "arm" ? "n" : "")} {kind}{(town is null ? "" : $" at {town}")}.");
        if (town is not null && kind is "arm" or "leg") AddScar(m, new Scar(kind, town, Hours));
    }

    /// <summary>A member was knocked down in a town: remembered there (once per town).</summary>
    public void DownedIn(string name, string town)
    {
        var m = Member(name);
        if (!m.Scars.Any(x => x.Kind == "down" && x.Town == town)) AddScar(m, new Scar("down", town, Hours));
    }

    static void AddScar(MemberRecord m, Scar scar)
    {
        m.Scars.Add(scar);
        if (m.Scars.Count > 12) m.Scars.RemoveAt(m.Scars.FindIndex(x => x.Kind == "down") is >= 0 and var i ? i : 0);
    }

    /// <summary>What this town did to this member: 1 took an arm, 2 took a leg, 3 knocked them down, 0 nothing (the worst one counts).</summary>
    public int ScarHere(string name, string? town)
    {
        if (town is null || !members.TryGetValue(name, out var m)) return 0;
        var here = m.Scars.Where(x => string.Equals(x.Town, town, StringComparison.OrdinalIgnoreCase)).ToList();
        return here.Any(x => x.Kind == "arm") ? 1 : here.Any(x => x.Kind == "leg") ? 2 : here.Count > 0 ? 3 : 0;
    }

    /// <summary>
    /// The once-a-second fight look: <paramref name="fighting"/> are the members in combat now. A fight runs while anyone is in combat and ends
    /// after <see cref="FightState.QuietLooks"/> quiet looks. At its end every member who fought counts a fight; it is won when the squad brought
    /// at least one enemy down and not everyone who fought is down; a won fight of three or more where only one fighter stayed up is that
    /// member's last stand. Big fights (four or more down) and lost ones go into the history.
    /// <paramref name="foesUp"/>: of those the squad fought in this fight (struck or was struck by), how many stand within 50 m. The fight is not
    /// over while any do (learnt in play: the fight "ended" while the beaten lay down; they got up and fought on, and each end counted
    /// again). Only the squad's own foes count, so other hostiles about (a busy road, a high population) never hold a fight open; a stand-off
    /// with foes up but nobody fighting ends after <see cref="FightState.StandOffLooks"/> looks.
    /// </summary>
    public List<string> FightLook(IReadOnlyCollection<string> fighting, IReadOnlyCollection<string> downNow, int foesUp = 0)
    {
        var said = new List<string>();
        if (fighting.Count > 0)
        {
            if (!fight.Active) fight.Start();
            fight.Quiet = 0;
            foreach (var n in fighting) fight.Fought.Add(n);
            return said;
        }
        if (!fight.Active) return said;
        ++fight.Quiet;
        if (fight.Quiet < FightState.QuietLooks || foesUp > 0 && fight.Quiet < FightState.StandOffLooks) return said;

        foreach (var n in downNow) if (fight.Fought.Contains(n)) fight.Down.Add(n);
        // judged by who of those who fought still stands: Down also holds squadmates who died elsewhere during the fight, and comparing the
        // counts turned a win into a loss when one did (from a review of the code)
        var standing = fight.Fought.Where(n => !fight.Down.Contains(n)).ToList();
        bool won = fight.EnemiesDown > 0 && standing.Count > 0;
        foreach (var n in fight.Fought)
        {
            var m = Member(n);
            m.Fights++;
            if (won) m.FightsWon++;
        }
        if (won && fight.Fought.Count >= 3 && standing.Count == 1)
        {
            Member(standing[0]).LastStands++;
            Note($"{D(standing[0])} was the last one standing.");
            OnMoment?.Invoke($"{D(standing[0])} was the last one standing.", 80);
        }
        if (!won) Note($"A fight went badly: {string.Join(", ", fight.Fought.OrderBy(x => x).Select(D))} went down.");
        else if (fight.EnemiesDown >= 4) Note($"A fight with {fight.EnemiesDown} down. {Names(standing.Select(D).ToList())} came through it.");
        foreach (var n in fight.Fought) said.AddRange(Award(n));
        LastFightEnd = (won, standing);
        fight.Active = false;
        return said;
    }

    static string Names(List<string> names) => names.Count switch
    {
        0 => "Nobody",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    public bool InFight => fight.Active;

    /// <summary>Who has fought in the current fight (empty between fights).</summary>
    public IReadOnlyCollection<string> Fighters => fight.Fought;

    /// <summary>Set when a fight ends: whether it was won and who was still standing (for the squad's victory lines).</summary>
    public (bool Won, List<string> Standing)? LastFightEnd { get; private set; }

    /// <summary>The engine side takes the last fight's end once (null afterwards).</summary>
    public (bool Won, List<string> Standing)? TakeFightEnd()
    {
        var end = LastFightEnd;
        LastFightEnd = null;
        return end;
    }

    sealed class FightState
    {
        public const int QuietLooks = 5;
        public const int StandOffLooks = 60;
        public bool Active;
        public int Quiet, EnemiesDown;
        public readonly HashSet<string> Fought = new(StringComparer.Ordinal), Down = new(StringComparer.Ordinal);

        public void Start()
        {
            Active = true;
            Quiet = 0;
            EnemiesDown = 0;
            Fought.Clear();
            Down.Clear();
        }
    }

    /// <summary>Distance carried by an animal (mode 1) or a boat (mode 2), from Meitou Transport.</summary>
    public List<string> Travelled(string name, int mode, double distance)
    {
        var m = Member(name);
        if (mode == 1) m.Ridden += distance; else m.Sailed += distance;
        return Award(name);
    }

    public List<string> Spend(string name, string activity, double seconds, double distance)
    {
        var m = Member(name);
        m.Seconds[activity] = m.SecondsOf(activity) + seconds;
        m.Distance += distance;
        return Award(name);
    }

    /// <summary>Checks every rule against the member and the crew; returns announcements for titles newly earned.</summary>
    public List<string> Award(string name)
    {
        var said = new List<string>();
        var m = Member(name);
        foreach (var r in rules)
        {
            if (r.Scope == "squad")
            {
                if (crewTitles.Contains(r.Id) || !Earned(r, m)) continue;
                crewTitles.Add(r.Id);
                said.Add($"Your squad is now known as {r.Name}.");
                Note($"The squad became known as {r.Name}.");
                OnMoment?.Invoke($"Your squad is now known as {r.Name}.", r.Legendary ? 95 : 85);
            }
            else
            {
                if (m.Titles.Contains(r.Id) || !Earned(r, m)) continue;
                m.Titles.Add(r.Id);
                said.Add($"{D(name)} is now known as {r.Name}.");
                Note($"{D(name)} became known as {r.Name}.");
                OnMoment?.Invoke($"{D(name)} is now known as {r.Name}.", r.Legendary ? 95 : 70);
            }
        }
        return said;
    }

    bool Earned(TitleRule r, MemberRecord m) => r.Scope == "squad" ? CrewValue(r) >= r.At : Value(r, m) >= r.At;

    /// <summary>A squad rule counts the whole crew: a named target among the crew's dead, or the stat summed over every member.</summary>
    double CrewValue(TitleRule r) => r.Stat == "named" && r.Targets is { Count: > 0 } t
        ? (AllDead(t, namedDead) ? r.At : 0)
        : crewStats.TryGetValue(r.Stat, out double crew) ? crew
        : members.Values.Sum(m => Value(r, m));

    static bool AllDead(IReadOnlyList<string> targets, List<string> dead) => targets.All(t => dead.Any(d => TitleRule.Matches(t, d)));

    public static double Value(TitleRule r, MemberRecord m) => r.Stat switch
    {
        "kills" => r.Faction is null ? m.Kills : m.KillsOf(r.Faction),
        "knockouts" => m.Knockouts,
        "executions" => m.Executions,
        "leaders" => m.LeadersKilled,
        "named" when r.Targets is { Count: > 0 } t => AllDead(t, m.Named) ? r.At : 0,
        "named" => m.NamedKills,
        "limbs" => m.LimbsCut,
        "downed" => m.TimesDowned,
        "limbs_lost" => m.LimbsLost,
        "damage" => m.DamageDealt,
        "distance" => m.Distance,
        "ridden" => m.Ridden,
        "sailed" => m.Sailed,
        "food" => m.Food,
        "fights" => m.Fights,
        "fights_won" => m.FightsWon,
        "last_stands" => m.LastStands,
        "avenged" => m.Avenged,
        "crafted" => r.Faction is null ? m.Crafted : m.CraftedBy.GetValueOrDefault(r.Faction),
        _ when r.Stat.StartsWith("hours:", StringComparison.Ordinal) => m.SecondsOf(r.Stat[6..]) / 3600,
        _ => 0,
    };

    /// <summary>Whether a character (by name) holds a title, its own or the crew's.</summary>
    public bool Holds(string name, string titleId) =>
        crewTitles.Contains(titleId) || members.TryGetValue(name, out var m) && m.Titles.Contains(titleId);

    /// <summary>Title id -> the higher rungs of its ladder (the titles of the same scope, stat, faction and targets earned at more). A rung
    /// is "outgrown" by its holder once they hold a higher one: the world then reacts to the highest only (TopHolds, AnyHoldsTop).</summary>
    Dictionary<string, string[]>? higher;
    string[] Higher(string id)
    {
        higher ??= rules.ToDictionary(r => r.Id, r => rules
            .Where(o => o.At > r.At && o.Scope == r.Scope && o.Stat == r.Stat && o.Faction == r.Faction
                && string.Join("|", o.Targets ?? []) == string.Join("|", r.Targets ?? []))
            .Select(o => o.Id).ToArray());
        return higher.TryGetValue(id, out var h) ? h : [];
    }

    bool CrewTop(string id) => crewTitles.Contains(id) && !Higher(id).Any(crewTitles.Contains);
    static bool MemberTop(MemberRecord m, string id, string[] up) => m.Titles.Contains(id) && !up.Any(m.Titles.Contains);

    /// <summary>Holds a title and no higher rung of it: what factions, gossip and tales react to.</summary>
    public bool HoldsTop(string name, string titleId) =>
        CrewTop(titleId) || members.TryGetValue(name, out var m) && MemberTop(m, titleId, Higher(titleId));

    /// <summary>Anyone of the squad, or the squad itself, holds a title and no higher rung of it (tavern tales).</summary>
    public bool AnyHoldsTop(string title)
    {
        var up = Higher(title);
        return CrewTop(title) || members.Values.Any(m => MemberTop(m, title, up));
    }

    /// <summary>Whether that holder has outgrown the title (holds a higher rung of it).</summary>
    public bool Outgrown(string name, string titleId) => Higher(titleId) is var up && (name == "the squad"
        ? up.Any(crewTitles.Contains) : members.TryGetValue(name, out var m) && up.Any(m.Titles.Contains));

    public string TitleName(string id) => rules.FirstOrDefault(r => r.Id == id)?.Name ?? id;

    /// <summary>Who holds a title: "the squad", member names, or nobody.</summary>
    public List<string> Holders(TitleRule r) => r.Scope == "squad"
        ? (crewTitles.Contains(r.Id) ? ["the squad"] : [])
        : members.Where(p => p.Value.Titles.Contains(r.Id)).Select(p => D(p.Key)).ToList();

    /// <summary>The catalog: every title, how it is earned, and who holds it; legendary ones last.</summary>
    public List<string> Catalog()
    {
        var lines = new List<string>();
        int held = rules.Count(r => Holders(r).Count > 0);
        lines.Add($"Titles: {held} of {rules.Count} earned");
        foreach (var group in rules.GroupBy(r => r.Legendary ? "Legendary" : r.Scope == "squad" ? "Squad" : "Members").OrderBy(g => Order(g.Key)))
        {
            lines.Add("");
            lines.Add($"-- {group.Key} --");
            foreach (var r in group)
            {
                var who = r.Scope == "squad"
                    ? (crewTitles.Contains(r.Id) ? [Outgrown("the squad", r.Id) ? "the squad (outgrown)" : "the squad"] : new List<string>())
                    : members.Where(p => p.Value.Titles.Contains(r.Id)).Select(p => D(p.Key) + (Outgrown(p.Key, r.Id) ? " (outgrown)" : "")).ToList();
                lines.Add($"{(who.Count > 0 ? "*" : " ")} {r.Name}: {r.Requirement()}{(who.Count > 0 ? "  [" + string.Join(", ", who) + "]" : "")}");
            }
        }
        return lines;
    }

    static int Order(string g) => g switch { "Members" => 0, "Squad" => 1, _ => 2 };

    /// <summary>The History page: newest first, "Day 4, Squin: ...".</summary>
    public List<string> HistoryLines()
    {
        if (history.Count == 0) return ["Nothing yet."];
        return history.AsEnumerable().Reverse().Select(h => $"Day {(int)(h.Hours / 24)}, {h.Place}: {h.Text}").ToList();
    }

    // ---- save ----

    public void Write(BinaryWriter w)
    {
        w.Write(12);
        WriteList(w, crewTitles);
        WriteList(w, namedDead);
        w.Write(members.Count);
        foreach (var (name, m) in members)
        {
            w.Write(name);
            foreach (int v in new[] { m.Kills, m.Knockouts, m.LeadersKilled, m.NamedKills, m.LimbsCut, m.TimesDowned, m.LimbsLost, m.Bites, m.Crafted })
                w.Write(v);
            w.Write(m.Fights);
            w.Write(m.FightsWon);
            w.Write(m.LastStands);
            w.Write(m.DamageDealt);
            w.Write(m.DamageTaken);
            w.Write(m.Distance);
            w.Write(m.Food);
            w.Write(m.CraftedBy.Count);
            foreach (var (k, v) in m.CraftedBy) { w.Write(k); w.Write(v); }
            w.Write(m.KillsBy.Count);
            foreach (var (k, v) in m.KillsBy) { w.Write(k); w.Write(v); }
            w.Write(m.Seconds.Count);
            foreach (var (k, v) in m.Seconds) { w.Write(k); w.Write(v); }
            WriteList(w, m.Titles);
            WriteList(w, m.Named);
            w.Write(m.Executions);
            w.Write(m.Avenged);
            w.Write(m.Scars.Count);
            foreach (var x in m.Scars) { w.Write(x.Kind); w.Write(x.Town); w.Write(x.Hours); }
            w.Write(m.Ridden);
            w.Write(m.Sailed);
        }
        w.Write(history.Count);
        foreach (var (hours, place, text) in history) { w.Write(hours); w.Write(place); w.Write(text); }
        w.Write(fallen.Count);
        foreach (var f in fallen)
        {
            w.Write(f.Name); w.Write(f.Hours); w.Write(f.Place); w.Write(f.Killer ?? ""); w.Write(f.KillerFaction ?? ""); w.Write(f.KillerRecord ?? "");
            w.Write(f.Kills); w.Write(f.DaysServed); WriteList(w, f.Titles.ToList()); w.Write(f.AvengedBy ?? "");
        }
        w.Write(feuds.Count);
        foreach (var (k, v) in feuds) { w.Write(k); w.Write(v); }
        w.Write(firstSeen.Count);
        foreach (var (k, v) in firstSeen) { w.Write(k); w.Write(v); }
        w.Write(keyOf.Count);
        foreach (var (k, v) in keyOf.OrderBy(p => p.Key, StringComparer.Ordinal)) { w.Write(k); w.Write(v); w.Write(shown.GetValueOrDefault(v, v)); }
        w.Write(namedHours.Count);
        foreach (var (name, hours) in namedHours) { w.Write(name); w.Write(hours); }
        w.Write(crewStats.Count);
        foreach (var (k, v) in crewStats) { w.Write(k); w.Write(v); }
        w.Write(events.Count);
        foreach (var e in events) w.Write(e);
    }

    /// <summary>Reads a saved record; false (nothing read past the version) for a format this build does not know.</summary>
    public bool Read(BinaryReader r)
    {
        int version = r.ReadInt32();
        if (version is not (4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12)) return false;
        members.Clear();
        ReadList(r, crewTitles);
        ReadList(r, namedDead);
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var m = Member(r.ReadString());
            m.Kills = r.ReadInt32(); m.Knockouts = r.ReadInt32(); m.LeadersKilled = r.ReadInt32(); m.NamedKills = r.ReadInt32();
            m.LimbsCut = r.ReadInt32(); m.TimesDowned = r.ReadInt32(); m.LimbsLost = r.ReadInt32(); m.Bites = r.ReadInt32(); m.Crafted = r.ReadInt32();
            if (version >= 5) { m.Fights = r.ReadInt32(); m.FightsWon = r.ReadInt32(); m.LastStands = r.ReadInt32(); }
            m.DamageDealt = r.ReadDouble();
            m.DamageTaken = r.ReadDouble();
            m.Distance = r.ReadDouble();
            m.Food = r.ReadDouble();
            for (int k = r.ReadInt32(); k > 0; k--) m.CraftedBy[r.ReadString()] = r.ReadInt32();
            for (int k = r.ReadInt32(); k > 0; k--) m.KillsBy[r.ReadString()] = r.ReadInt32();
            for (int k = r.ReadInt32(); k > 0; k--) m.Seconds[r.ReadString()] = r.ReadDouble();
            ReadList(r, m.Titles);
            ReadList(r, m.Named);
            if (version >= 7) m.Executions = r.ReadInt32();
            if (version >= 8) m.Avenged = r.ReadInt32();
            if (version >= 10) for (int k = r.ReadInt32(); k > 0; k--) m.Scars.Add(new Scar(r.ReadString(), r.ReadString(), r.ReadDouble()));
            if (version >= 11) { m.Ridden = r.ReadDouble(); m.Sailed = r.ReadDouble(); }
        }
        history.Clear();
        if (version >= 5)
            for (int n = r.ReadInt32(); n > 0; n--) history.Add((r.ReadDouble(), r.ReadString(), r.ReadString()));
        fallen.Clear(); feuds.Clear(); firstSeen.Clear();
        if (version >= 8)
        {
            static string? N(string v) => v.Length == 0 ? null : v;
            for (int n = r.ReadInt32(); n > 0; n--)
            {
                string name = r.ReadString(); double hours = r.ReadDouble(); string place = r.ReadString();
                string? killer = N(r.ReadString()), kf = N(r.ReadString()), kr = N(r.ReadString());
                int kills = r.ReadInt32(); double served = r.ReadDouble(); var titles = new List<string>(); ReadList(r, titles);
                fallen.Add(new Fallen(name, hours, place, killer, kf, kr, kills, served, titles, N(r.ReadString())));
            }
            for (int n = r.ReadInt32(); n > 0; n--) feuds[r.ReadString()] = r.ReadDouble();
            for (int n = r.ReadInt32(); n > 0; n--) firstSeen[r.ReadString()] = r.ReadDouble();
        }
        keyOf.Clear(); shown.Clear();
        if (version >= 9)
            for (int n = r.ReadInt32(); n > 0; n--) { string id = r.ReadString(), key = r.ReadString(); keyOf[id] = key; shown[key] = r.ReadString(); }
        namedHours.Clear();
        if (version >= 6)
            for (int n = r.ReadInt32(); n > 0; n--) namedHours[r.ReadString()] = r.ReadDouble();
        crewStats.Clear(); events.Clear();
        if (version >= 12)
        {
            for (int n = r.ReadInt32(); n > 0; n--) crewStats[r.ReadString()] = r.ReadDouble();
            for (int n = r.ReadInt32(); n > 0; n--) events.Add(r.ReadString());
        }
        return true;
    }

    /// <summary>Numbers for the state hash (whole numbers only, so rounding never splits two runs).</summary>
    public IEnumerable<long> HashParts()
    {
        yield return crewTitles.Count;
        yield return namedDead.Count;
        yield return history.Count;
        foreach (var (name, m) in members)
        {
            yield return StableHash(name);
            yield return m.Kills;
            yield return m.Knockouts;
            yield return m.LimbsCut;
            yield return m.Titles.Count;
            yield return m.Bites;
            yield return m.Crafted;
            yield return (long)m.DamageDealt;
        }
    }

    static void WriteList(BinaryWriter w, List<string> list)
    {
        w.Write(list.Count);
        foreach (var s in list) w.Write(s);
    }

    static void ReadList(BinaryReader r, List<string> list)
    {
        list.Clear();
        for (int n = r.ReadInt32(); n > 0; n--) list.Add(r.ReadString());
    }

    internal static long StableHash(string s)
    {
        unchecked
        {
            long h = 1469598103934665603;
            foreach (char c in s) h = (h ^ c) * 1099511628211;
            return h;
        }
    }
}

/// <summary>A wound a member remembers: "arm", "leg" or "down", the town, the game hour.</summary>
public sealed record Scar(string Kind, string Town, double Hours);

/// <summary>A squadmate who died: when, where, who killed them, what they had done, and who avenged them.</summary>
public sealed record Fallen(string Name, double Hours, string Place, string? Killer, string? KillerFaction, string? KillerRecord, int Kills,
    double DaysServed, IReadOnlyList<string> Titles, string? AvengedBy);

/// <summary>What the engine side reports through: the log and whether to log every change (each mod sets these in Initialize).</summary>
public static class RecordHost
{
    public static Action<string> Log = _ => { };
    public static Func<bool> LogChanges = () => true;

    /// <summary>Whether a newly earned title is posted in game (it is always logged).</summary>
    public static Func<bool> Announce = () => true;

    /// <summary>The window's text, rebuilt in the tick every few seconds and read by the interface thread.</summary>
    public static volatile string Panel = "No world yet.";

    /// <summary>The title catalog's text, rebuilt with <see cref="Panel"/>.</summary>
    public static volatile string CatalogText = "No world yet.";

    /// <summary>The History page's text (newest first), rebuilt with <see cref="Panel"/>.</summary>
    public static volatile string HistoryText = "No world yet.";
    /// <summary>The Atlas tab (Living Wasteland only, Atlas.cs): the list ("@@key|label" lines are buttons) and each entry's page by key.</summary>
    public static volatile string AtlasList = "Nothing here yet.";
    public static volatile IReadOnlyDictionary<string, string> AtlasPages = new Dictionary<string, string>();
    /// <summary>Atlas page key -> the CHARACTER record whose portrait the page shows (people the squad knows of, not a blur).</summary>
    public static volatile IReadOnlyDictionary<string, string> AtlasPortraits = new Dictionary<string, string>();

    /// <summary>The Fallen page's text (newest first), rebuilt with <see cref="Panel"/>.</summary>
    public static volatile string FallenText = "No world yet.";

    /// <summary>Set by the stand-alone Squad Record when it finds the Living Wasteland helper loaded after all: it records nothing and its
    /// button is hidden (the helper has the record built in).</summary>
    public static volatile bool Off;
}

/// <summary>
/// Which activity a character's current task counts as. Matched by Meitou's CharacterTask NAME (the engine side passes the enum's name,
/// so a renumbered enum cannot shift the activities); <see cref="Of"/> takes the 0.17 numbering for the unit tests.
/// </summary>
public static class Activities
{
    static readonly string[] Names017 = ["Idle", "Wander", "GoTo", "Follow", "Attack", "Treat", "Rest", "PickUp", "PutDown", "Loot", "StealthKo",
        "Build", "Dismantle", "DeliverMaterials", "Operate", "Haul", "Prospect", "FetchMaterials", "Sleep"];

    public static string? Of(byte task, bool moving) => task < Names017.Length ? OfName(Names017[task], moving) : null;

    public static string? OfName(string task, bool moving) => task switch
    {
        "Attack" => "fighting",
        "Treat" => "first aid",
        "Rest" or "Sleep" => "resting",
        "PickUp" or "PutDown" or "Haul" or "DeliverMaterials" or "FetchMaterials" => "hauling",
        "Loot" => "looting",
        "StealthKo" => "sneak attacks",
        "Build" or "Dismantle" => "building",
        "Operate" or "Prospect" => "working",
        "Wander" or "GoTo" or "Follow" => moving ? "travelling" : null,
        _ => null,
    };
}
