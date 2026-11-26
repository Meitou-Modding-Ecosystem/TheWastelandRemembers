// The engine side of Deeds.cs (shared by the Living Wasteland helper and the stand-alone Squad Record mod, which builds it with SQUAD_RECORD_ONLY):
// Meitou's combat-hit observer (every landed blow, after the engine has applied it) feeds the squad record; a once-a-second look at the player
// characters adds time per activity, distance walked, times downed, limbs lost, food eaten and items finished at the benches they worked last.
// Living Wasteland only: town heat (fades every game minute) and the dialogue codes (lw_codes.json "notoriety"): a title condition per title,
// the heat condition and the heat effect (meitou_target.py rewrites the Heat pack's reputation references into these).
using System.Runtime.CompilerServices;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

/// <summary>The world's record and heat, with the engine lookups the handlers need.</summary>
public sealed class LwDeeds(IReadOnlyList<TitleRule> rules, PopulationData? data)
{
    static readonly ConditionalWeakTable<World, LwDeeds> byWorld = new();

    public SquadRecord Record { get; } = new(rules);

#if !SQUAD_RECORD_ONLY
    public HeatMap Heat { get; } = new();
    internal KindnessBook Kindness { get; } = new();
    internal KindnessHomes KindnessWhere { get; } = new();
    internal LwDeedsSystem? Tracker { get; set; }
#endif

    public PopulationData? Data => data;

    /// <summary>Messages to post at the next slow step (the hit observer must not change the world).</summary>
    public List<string> Pending { get; } = [];

    public static LwDeeds? Latest { get; private set; }

    public static LwDeeds? For(World world) => byWorld.TryGetValue(world, out var d) ? d : null;

    internal static void Register(World world, LwDeeds deeds)
    {
        byWorld.AddOrUpdate(world, deeds);
        Latest = deeds;
    }

    /// <summary>The town (by name) a position is in, and the faction (by name) that owns it; nulls outside every town.</summary>
    public (string? Town, string? Owner) TownAt(System.Numerics.Vector3 at) => TownAtSite(data?.TownAt(at) ?? -1);

    internal (string? Town, string? Owner) TownAtSite(int site)
    {
        if (data is null || site < 0 || site >= data.Sites.Count) return (null, null);
        int f = site < data.SiteFactions.Count ? data.SiteFactions[site] : -1;
        return (data.Sites[site].Town.Name, f >= 0 && f < data.Factions.Count ? data.Factions[f].Name : null);
    }

    public string FactionName(int f) => data is not null && f >= 0 && f < data.Factions.Count ? data.Factions[f].Name : "";

    /// <summary>A placed town's name by its site index (null when there is none).</summary>
    public string? SiteName(int site) => data is not null && site >= 0 && site < data.Sites.Count ? data.Sites[site].Town.Name : null;

#if !SQUAD_RECORD_ONLY
    internal string PriceNode(int site) => data is not null && site >= 0 && site < data.Sites.Count ? NewsSystem.NodeOf(data.Sites[site]) : "";

    /// <summary>The heat a speaker feels: its town's, or the echo of its faction's hottest town.</summary>
    public double HeatFor(World world, CharacterId speaker)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(speaker, out int s) || table.Cold(s) is not { } cold) return 0;
        return Heat.For(TownAt(table.Next[s].Position).Town, FactionName(cold.Faction));
    }

    /// <summary>A heat effect said by <paramref name="speaker"/>: positive adds crime heat where it stands (its faction's), negative forgives.</summary>
    public void HeatEffect(World world, CharacterId speaker, int value)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(speaker, out int s) || table.Cold(s) is not { } cold) return;
        string faction = FactionName(cold.Faction);
        if (value < 0)
        {
            Heat.Forgive(faction, -value);
            if (RecordHost.LogChanges()) RecordHost.Log($"heat: {faction} forgives {-value}");
            return;
        }
        int site = data?.TownAt(table.Next[s].Position) ?? -1;
        string town = SiteName(site) ?? $"{faction} lands";
        Heat.AddCrime(town, faction, value, PriceNode(site));
        if (RecordHost.LogChanges()) RecordHost.Log($"heat: +{value} in {town} ({Heat.Here(town):0.0})");
    }
#endif

    /// <summary>Whether a player character holds a title and no higher rung of it (what the world reacts to).</summary>
    public bool Holds(World world, CharacterId who, string title)
    {
        var table = world.Characters;
        return table.TryResolveNext(who, out int s) && table.Cold(s) is { IsPlayer: true } cold && Record.HoldsTop(Record.PeekKey(Identity(cold), cold.Name), title);
    }

    /// <summary>What identifies a character across renames, squad moves and loads: Meitou's portrait serial (saved with the character) and
    /// its record id; the name when it has no serial.</summary>
    public static string Identity(CharacterCold c) => c.PortraitSerial is { } serial ? $"{serial}:{c.RecordId}" : $"name:{c.Name}";

    /// <summary>The squad record key of a player character.</summary>
    public string Who(CharacterCold c) => Record.KeyFor(Identity(c), c.Name);
}

/// <summary>The record system: observes hits, samples the squad and the benches, fades heat (Living Wasteland), saves and hashes it all.</summary>
public sealed partial class LwDeedsSystem(IReadOnlyList<TitleRule> rules, PopulationData? data) : ICombatHitObserver, IModSaveData, IStateHashed
{
    const int LookEvery = 30;                       // ticks between looks at the squad (1 game second)
    const int PanelEvery = 90;
    const double KillBlood = 6, KnockoutCrime = 2;  // heat per townsperson killed / knocked down

    readonly LwDeeds deeds = Linked(new LwDeeds(rules, data));
    internal LwDeeds Deeds => deeds;

#if !SQUAD_RECORD_ONLY
    /// <summary>The dialogue system for fallback event 15 and memorial lines. Set by the helper.</summary>
    public Meitou.Simulation.Dialogue.DialogueSystem? Talk { get; init; }
#endif

    /// <summary>The last enemy a player character struck (the target of the squad's victory line).</summary>
    CharacterId lastEnemy = CharacterId.None;
#if !SQUAD_RECORD_ONLY
    bool wipeTold;
#endif

    /// <summary>Titles and last stands go to Recap too, when it is installed (RecapLink.cs).</summary>
    static LwDeeds Linked(LwDeeds d)
    {
        d.Record.OnMoment = (caption, weight) => RecapLink.Moment(caption, d.Record.Place, weight);
        return d;
    }
    readonly HashSet<(CharacterId, int)> cutParts = [];
#if !SQUAD_RECORD_ONLY
    readonly Lookout lookout = new();
#endif

    /// <summary>Each struck character's stumps as of the last blow anyone landed (bit per part), so a limb severed beside the one struck
    /// (Brutality's paired arm or leg cuts) is credited too, and one lost before is not.</summary>
    readonly Dictionary<CharacterId, ulong> stumpsBefore = [];

    static ulong Stumps(MedicalState med)
    {
        ulong mask = 0;
        for (int p = 0; p < med.Parts.Count && p < 64; p++) if (med.Parts[p].Limb == LimbState.Stump) mask |= 1UL << p;
        return mask;
    }
    readonly Dictionary<CharacterId, (System.Numerics.Vector3 At, bool Down, int Arms, int Legs, float Fed)> last = [];
    readonly Dictionary<string, Dictionary<string, int>> benchOutput = new(StringComparer.Ordinal);
    long benchVersion = -1;
    bool registered;
    readonly List<int> panelSlots = [];
    readonly HashSet<string> presentInRecord = new(StringComparer.Ordinal);
#if !SQUAD_RECORD_ONLY
    readonly List<(CharacterId Id, string Key)> nativePanelMembers = [];
    bool observeLimbs;
#endif
#if !SQUAD_RECORD_ONLY
    double lastFadeHours = double.NaN;
    readonly HashSet<string> occupiedPriceNodes = new(StringComparer.Ordinal);
#endif

    #region hits

    /// <summary>What the helper knows about a victim a player character struck, kept until it dies, recovers or a game day passes.</summary>
    sealed record Struck(string Striker, string Name, string Faction, bool Leader, string? Named, string? Town, string? Owner, bool Local, double Hours,
        string? UniqueRecord = null, System.Numerics.Vector3? At = null)
    {
        /// <summary>Struck in an arena bout (saved with named victims: one can bleed out after a reload).</summary>
        public bool Sport { get; init; }
#if !SQUAD_RECORD_ONLY
        public string PriceNode { get; init; } = "";
#endif
    }

    readonly Dictionary<CharacterId, Struck> struck = [];

    /// <summary>Squadmates who just died, waiting for a standing squadmate to mourn them by name once the fight is over.</summary>
    readonly List<(CharacterId Body, string Name, double Hours)> toMourn = [];

    /// <summary>Named victims struck before a save, by unique record id: character ids do not survive a load, so after one they are found
    /// again through the unique registry (moved back into <see cref="struck"/> once the character is in the world again).</summary>
    readonly SortedDictionary<string, Struck> pendingNamed = new(StringComparer.Ordinal);

    /// <summary>Set after the first error: the record stops for this world rather than risk the game (the error is logged once).</summary>
    bool failed;

    public void HitResolved(World world, in CombatHit hit)
    {
        if (failed || StandDown()) return;
        try
        {
            OnHit(world, hit);
        }
        catch (Exception e)
        {
            Fail("a hit", e);
        }
    }

    /// <summary>
    /// The stand-alone Squad Record steps aside when the Wasteland Remembers helper is loaded. Its Initialize checks too, but the helper's
    /// assembly may load after it; by the first tick every mod is loaded. Always false in the helper itself.
    /// </summary>
    static bool StandDown()
    {
#if SQUAD_RECORD_ONLY
        if (standDown is null)
        {
            standDown = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "LivingWastelandMeitou");
            if (standDown == true)
            {
                RecordHost.Off = true;
                RecordHost.Log("the Wasteland Remembers helper is loaded and has the squad record built in: Squad Record stays off");
            }
        }
        return standDown == true;
#else
        return false;
#endif
    }

#if SQUAD_RECORD_ONLY
    static bool? standDown;
#endif

    void Fail(string where, Exception e)
    {
        failed = true;
        RecordHost.Log($"the squad record stopped after an error in {where} (please send log.txt): {e}");
    }

    readonly Dictionary<CharacterId, FightFoe> foes = [];
#if !SQUAD_RECORD_ONLY
    readonly HashSet<CharacterId> wakingFoes = [];

    internal void FoeKnockout(CharacterId id, bool temporary)
    {
        if (temporary && (foes.ContainsKey(id) || restoredFoes.ContainsKey(id))) wakingFoes.Add(id);
        else wakingFoes.Remove(id);
    }
#endif

    /// <summary>How many of the squad's foes in this fight are on their feet within 50 m of a standing squad member (the fight is not over
    /// while any are: Deeds.FightLook). The dead, the gone and the taken drop out.</summary>
    int FoesUp(World world, List<(string Name, CharacterId Id, System.Numerics.Vector3 At, bool Down)> squad,
        Dictionary<CharacterId, FightFoe> opponents, bool restored = false)
    {
        if (opponents.Count == 0) return 0;
        var table = world.Characters;
        int up = 0;
        foreach (var id in opponents.Keys.ToList())
        {
            if (!table.TryResolveNext(id, out int s) || !table.Next[s].Alive || table.Cold(s) is not { } c || c.Medical is not { Dead: false } med)
            {
                opponents.Remove(id);
#if !SQUAD_RECORD_ONLY
                wakingFoes.Remove(id);
#endif
                continue;
            }
            if (restored && foes.ContainsKey(id)) continue;
            if (c.IsImprisoned || !c.CarriedBy.IsNone) continue;
#if !SQUAD_RECORD_ONLY
            bool temporarilyDown = wakingFoes.Contains(id) && med.Unconscious && !med.Coma && med.KoTimer > 0f;
            if (med.Incapacitated && !temporarilyDown) continue;
#else
            if (med.Incapacitated) continue;
#endif
            var at = table.Next[s].Position;
            if (squad.Any(m => !m.Down && System.Numerics.Vector3.DistanceSquared(m.At, at) < 500f * 500f)) up++;
        }
        return up;
    }

    void OnHit(World world, in CombatHit hit)
    {
        Register(world);
        var table = world.Characters;
        if (!table.TryResolveNext(hit.Victim, out int v) || table.Cold(v) is not { } victim) return;
        if (victim.IsPlayer && OtherModsPeople.Runs(world, victim)) return;   // a blow on another mod's people is not one on the squad
        // A returning named actor may be hit before the squad look rebinds the saved claim. This hit replaces it.
        if (pendingNamed.Count > 0 && world.Uniques.RecordOf(hit.Victim) is { } record)
            pendingNamed.Remove(record);
        bool knewStumps = stumpsBefore.TryGetValue(hit.Victim, out ulong before);
        if (victim.Medical is { } vm) stumpsBefore[hit.Victim] = Stumps(vm);
        var attacker = table.TryResolveNext(hit.Attacker, out int a) ? table.Cold(a) : null;
        if (attacker is { IsPlayer: true } && OtherModsPeople.Runs(world, attacker)) attacker = null;    // another mod's people are not the squad
        string? striker = attacker is { IsPlayer: true } ? deeds.Who(attacker) : null;
        // a blow in one of Proving Grounds' arena bouts (striker and victim both in it) is sport: the record keeps it, but it is no fight the
        // squad is in, heats no town and makes no rival (ArenaLink.cs)
        bool sport = ArenaLink.Sport(hit.Attacker, hit.Victim);
#if !SQUAD_RECORD_ONLY
        if (sport) LogTally.Add("arena blows");
        else if (striker is not null && !victim.IsPlayer && victim.Animal is null && !hit.VictimWasIncapacitated &&
                 victim.Medical is { Incapacitated: true, Dead: false } && KindnessWatch.Enabled)
            deeds.Kindness.NoteSquadDown(LastingActors.Grant(world, hit.Victim), world.GameHours);
#endif
        // the squad's foes in this fight (FoesUp): whoever it struck, and whoever struck it
        if (sport) { }
        else if (attacker is { IsPlayer: true } && !victim.IsPlayer) RememberFoe(world, hit.Victim, victim);
        else if (victim.IsPlayer && attacker is { IsPlayer: false }) RememberFoe(world, hit.Attacker, attacker);
#if !SQUAD_RECORD_ONLY
        // A later knockdown of the same actor is a fresh defeat. Any new native event follows in Inputs.
        if (nativeDefeats is not null && !hit.VictimWasIncapacitated && victim.Medical is { Incapacitated: true })
            nativeDefeats.Remove(hit.Victim);
#endif
        double amount = (hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce) * hit.Damage.Scale;

        deeds.Record.Damage(striker, victim.IsPlayer ? deeds.Who(victim) : null, amount);
        // The hit observer can name a killer only when this blow took a living squadmate to dead.
#if !SQUAD_RECORD_ONLY
        if (victim.IsPlayer && attacker is { IsPlayer: false } && hit.VictimWasAlive && victim.Medical is { Dead: true })
            RivalsSystem.For(world)?.ConfirmedSquadmateKill(hit.Attacker, attacker);
#endif
        if (!sport && victim.IsPlayer && attacker is { IsPlayer: false } && hit.VictimWasAlive && victim.Medical is { Dead: true })
            RecordFallen(world, hit.Victim, victim, deeds.TownAt(table.Next[v].Position).Town,
                attacker.Name, deeds.FactionName(attacker.Faction), world.Uniques.RecordOf(hit.Attacker));
        // Mercenaries the player hired are on the squad's side: their blows never take the squad's claim away, and a kill they make belongs
        // to the squad as a whole (no member credit).
        if (striker is null && attacker is not null && IsHired(world, attacker))
        {
            if (victim.IsPlayer || victim.Medical is not { } m) return;
            var by = Snapshot(world, hit.Victim, v, victim, HiredBlades);
            if (hit.VictimWasAlive && m.Dead)
            {
                struck.Remove(hit.Victim);
                Killed(world, by);
            }
            else struck[hit.Victim] = by;       // a later bleed-out after their blow is the squad's too
            return;
        }
        // The last blow decides the credit (Recap's rule): anyone else hitting the victim after the squad takes the squad's claim away.
        if (striker is null && struck.Remove(hit.Victim) && RecordHost.LogChanges())
            RecordHost.Log($"kill credit: someone else struck {victim.Name} after your squad; no later kill credit");
#if !SQUAD_RECORD_ONLY
        bool leads = victim.SquadId >= 0 && world.Squads.Find(victim.SquadId) is { } vsq && vsq.Leader == hit.Victim;
        if (!sport) RivalsSystem.For(world)?.Hit(world, hit, victim, attacker, striker, leads);
#endif
        if (striker is null || victim.IsPlayer || victim.Medical is not { } med) return;

        lastEnemy = hit.Victim;
        var info = Snapshot(world, hit.Victim, v, victim, striker) with { Sport = sport };
        bool killed = hit.VictimWasAlive && med.Dead;
        bool downed = !killed && !hit.VictimWasIncapacitated && med.Incapacitated;
        if (killed)
        {
            struck.Remove(hit.Victim);
            Killed(world, info);
#if !SQUAD_RECORD_ONLY
            if (!sport) LeaderStories.Confirm(world, deeds, info.UniqueRecord, captured: false, info.At);
#endif
            // a blow that kills someone already down: the Execution mod's blows, or finishing a downed enemy in a fight
            if (hit.VictimWasIncapacitated || hit.Source == CombatHitSource.Execution) Say(deeds.Record.Executed(striker));
        }
        else
        {
            struck[hit.Victim] = info;
            if (downed) Downed(info);
        }
        if (hit.Part >= 0 && hit.Part < med.Parts.Count && med.Parts[hit.Part].Limb == LimbState.Stump
            && !(knewStumps && hit.Part < 64 && (before >> hit.Part & 1) != 0) && cutParts.Add((hit.Victim, hit.Part)))
        {
            Say(deeds.Record.LimbCut(striker));
            // other limbs that became stumps with this blow (Brutality's paired cuts): only when the stumps before it are known
            ulong extra = knewStumps ? Stumps(med) & ~before & ~(1UL << hit.Part) : 0;
            for (int p = 0; p < med.Parts.Count && p < 64; p++)
                if ((extra >> p & 1) != 0 && cutParts.Add((hit.Victim, p))) Say(deeds.Record.LimbCut(striker));
        }
    }

    static readonly System.Reflection.PropertyInfo? missionEmployer = typeof(Blackboard).GetProperty("MissionEmployer");
    static readonly System.Reflection.PropertyInfo? contractTown = typeof(Blackboard).GetProperty("ContractTown");

    /// <summary>Checks who hired a bodyguard or owns the town they defend, as Meitou's aggro does.</summary>
    internal static bool IsHired(World world, CharacterCold c)
    {
        if (world.Player.Faction < 0 || c.SquadId < 0 || world.Squads.Find(c.SquadId) is not { Ai: { Contract: { } job } board } squad)
            return false;

        if (job.Signal == 12)
        {
            // Older clients use the mission target for the employer. Newer clients can retain a separate employer.
            var employer = missionEmployer?.GetValue(board) is CharacterId hirer ? hirer : board.MissionTarget;
            return !employer.IsNone && world.Characters.TryResolveNext(employer, out int slot) &&
                world.Characters.Cold(slot)?.Faction == world.Player.Faction;
        }

        if (job.Signal != 15) return false;
        if (contractTown is not null)
            return contractTown.GetValue(board) is CampaignTown town && world.Buildings.FindTown(town.PlayerTown)?.Owner == world.Player.Faction;

        // The old engine infers the mission town from the squad's position.
        float nearest = float.PositiveInfinity;
        int owner = -1;
        foreach (var town in world.Buildings.Towns)
        {
            float distance = System.Numerics.Vector3.DistanceSquared(town.Position, squad.Position);
            if (distance >= nearest) continue;
            nearest = distance;
            owner = town.Owner;
        }
        return owner == world.Player.Faction;
    }

    Struck Snapshot(World world, CharacterId id, int slot, CharacterCold victim, string striker)
    {
        bool leader = victim.SquadId >= 0 && world.Squads.Find(victim.SquadId) is { } squad && squad.Leader == id;
        string? unique = world.Uniques.RecordOf(id);
        string? named = unique is { } rec ? data?.Db.Find(rec)?.Name ?? victim.Name : TitleTarget(victim);
        string faction = deeds.FactionName(victim.Faction);
        var at = world.Characters.Next[slot].Position;
        int site = data?.TownAt(at) ?? -1;
        var (town, owner) = deeds.TownAtSite(site);
        bool lives = victim.SquadId >= 0 && world.Squads.Find(victim.SquadId) is { Town: >= 0 } s && data is not null
            && s.Town < data.Sites.Count && data.Sites[s.Town].Town.Name == town;
        bool local = town is not null && owner is not null && (faction == owner || lives);
        var result = new Struck(striker, victim.Name, faction, leader, named, town, owner, local, world.GameHours, unique, at);
#if !SQUAD_RECORD_ONLY
        result = result with { PriceNode = deeds.PriceNode(site) };
#endif
        return result;
    }

    /// <summary>A victim the engine does not register as unique still counts as named when its record is a title's target (the bosses).</summary>
    string? TitleTarget(CharacterCold victim)
    {
        string? name = data?.Db.Find(victim.RecordId)?.Name;
        if (name is null) return null;
        bool target = rules.Any(r => r.Targets is { } t && t.Any(x => TitleRule.Matches(x, name)));
        return target ? name : null;
    }

    /// <summary>The striker name used for blows by mercenaries the player hired (never a character's name: it starts with a control character).</summary>
    const string HiredBlades = "\u0001hired";

    void Killed(World world, Struck k)
    {
        deeds.Record.Hours = k.Hours;
        deeds.Record.Place = k.Town ?? "the wilds";
        if (k.Striker == HiredBlades)
        {
            Say(deeds.Record.CrewKill(k.Faction, k.Leader, k.Named));
            if (RecordHost.LogChanges()) RecordHost.Log($"kill: hired blades killed {k.Name} ({k.Faction}{(k.Named is null ? "" : ", named")}); credited to the squad");
            TownHeat(k, KillBlood, blood: true);
            return;
        }
        Say(deeds.Record.Kill(k.Striker, k.Faction, k.Leader, k.Named));
        if (deeds.Record.Avenge(k.Striker, k.Name, k.Faction, k.UniqueRecord).Count > 0) Say(deeds.Record.Award(k.Striker));
#if !SQUAD_RECORD_ONLY
        RivalsSystem.Latest?.SquadKilled(k.Name);
        if (k.UniqueRecord is { } killedUnique) OutlawCampsSystem.SquadKilled.Add(killedUnique);
#endif
        if (RecordHost.LogChanges())
            RecordHost.Log($"kill: {k.Striker} killed {k.Name} ({k.Faction}{(k.Leader ? ", leader" : "")}{(k.Named is null ? "" : ", named")})");
        TownHeat(k, KillBlood, blood: true);
    }

    void Downed(Struck k)
    {
        deeds.Record.Hours = k.Hours;
        deeds.Record.Place = k.Town ?? "the wilds";
        Say(deeds.Record.Knockout(k.Striker));
        TownHeat(k, KnockoutCrime, blood: false);
    }

    (string Town, string Tier, System.Numerics.Vector3? At, double Hours)? heatMoment;

    void SendHeatMoment()
    {
        if (heatMoment is not { } h) return;
        heatMoment = null;
        RecapLink.Moment($"{h.Town} now has you marked as {h.Tier}.", h.Town, h.Tier == "hunted" ? 75 : 65, h.At);
    }

    /// <summary>Violence against a town's own people (its owner's faction, or a squad that lives there) heats that town.</summary>
    void TownHeat(Struck k, double amount, bool blood)
    {
#if !SQUAD_RECORD_ONLY
        if (!k.Local || k.Sport) return;
        string tierBefore = HeatMap.Tier(deeds.Heat.Here(k.Town!));
        if (blood) deeds.Heat.AddBlood(k.Town!, k.Owner!, amount, k.PriceNode);
        else deeds.Heat.AddCrime(k.Town!, k.Owner!, amount, k.PriceNode);
        string tierNow = HeatMap.Tier(deeds.Heat.Here(k.Town!));
        if (tierNow != tierBefore)
        {
            deeds.Record.Note($"{k.Town} now has you marked as {tierNow}.");
            if (tierNow is "wanted" or "hunted")
            {
                // held a game hour: "wanted" then "hunted" in the same fight is one Recap page (the higher one), not two
                if (heatMoment is { } h && h.Town != k.Town) SendHeatMoment();
                heatMoment = (k.Town!, tierNow, k.At, k.Hours);
            }
        }
        if (tierNow != tierBefore && RecordHost.LogChanges()) RecordHost.Log($"heat: {k.Town} now {deeds.Heat.Here(k.Town!):0.0} ({tierNow})");   // only when the level changes
#endif
    }

#if !SQUAD_RECORD_ONLY
    internal void LimbLost(World world, CharacterId id, CharacterCold cold, string kind)
    {
        if (!observeLimbs) return;
        string name = deeds.Who(cold);
        string? town = deeds.TownAt(world.Characters.Next[id.Slot].Position).Town;
        deeds.Record.LimbLost(name, kind, town, world.GameHours, town ?? "the wilds");
        Say(deeds.Record.Award(name));
    }

    internal void ForgetCharacter(CharacterId id)
    {
        if (lastEnemy == id) lastEnemy = CharacterId.None;
        last.Remove(id);
        stumpsBefore.Remove(id);
        foes.Remove(id);
        restoredFoes.Remove(id);
        wakingFoes.Remove(id);
        if (struck.Remove(id, out var hit) && hit.UniqueRecord is { } record)
            pendingNamed[record] = hit;

        for (int i = 0; i < toMourn.Count; i++)
        {
            var (body, name, hours) = toMourn[i];
            if (body == id) toMourn[i] = (CharacterId.None, name, hours);
        }
    }

    // A death with a confirmed outside killer supersedes older hits, including claims carried through a save.
    internal void OutsideDeath(CharacterId victim, string? unique)
    {
        if (!victim.IsNone) struck.Remove(victim);
        if (unique is null) return;
        pendingNamed.Remove(unique);
        foreach (var (id, claim) in struck)
            if (claim.UniqueRecord == unique) struck.Remove(id);
    }
#endif

    /// <summary>Victims the squad struck last that have died since (bled out, off screen too for named ones): the last player striker gets the
    /// kill. A later blow by anyone else has already removed the victim from <see cref="struck"/>.</summary>
    void LaterDeaths(World world)
    {
        var table = world.Characters;
        foreach (var (rec, k) in pendingNamed.ToList())
        {
            double since = world.GameHours - k.Hours;
            var entry = world.Uniques.Find(rec);
            var fate = world.Uniques.StateOf(rec);
            if (fate == Meitou.Simulation.Factions.UniqueState.Dead) { pendingNamed.Remove(rec); Killed(world, k); }
            else if (fate != Meitou.Simulation.Factions.UniqueState.Alive || since > 24 || since < 0) pendingNamed.Remove(rec);
            else if (entry is not null && !entry.Handle.IsNone && table.TryResolveNext(entry.Handle, out _))
            {
                pendingNamed.Remove(rec);
                struck.TryAdd(entry.Handle, k);          // back in the world: the usual rules apply again (a later blow by others cancels it)
            }
        }
        foreach (var (id, k) in struck.ToList())
        {
            double since = world.GameHours - k.Hours;
            if (!table.TryResolveNext(id, out int s) || table.Cold(s)?.Medical is not { } med)
            {
                // Gone from the table: its area unloaded (or it was removed). That alone is not a death. A unique (named) character's
                // fate is still kept by the engine's unique registry, so it can still be credited if it bleeds out off screen within the
                // day; anyone else is dropped (an unloaded nobody's death cannot be seen).
                var fate = k.UniqueRecord is { } rec ? world.Uniques.StateOf(rec) : (Meitou.Simulation.Factions.UniqueState?)null;
                if (fate == Meitou.Simulation.Factions.UniqueState.Dead)
                {
                    struck.Remove(id);
                    Killed(world, k);
                }
                else if (fate != Meitou.Simulation.Factions.UniqueState.Alive || since > 24 || since < 0)
                {
                    struck.Remove(id);
                }
                continue;
            }
            if (med.Dead)
            {
                struck.Remove(id);
                Killed(world, k);
            }
            else if (!med.Incapacitated && since > 2 || since > 24 || since < 0)
            {
                struck.Remove(id);
            }
        }
    }

    void Say(List<string> titles)
    {
        foreach (var t in titles)
        {
            deeds.Pending.Add(t);
            RecordHost.Log($"title: {t}");
        }
    }

    #endregion

    #region squad look, heat fade, panel

    public void SlowWorld(World world)
    {
        if (failed || StandDown()) return;
        try
        {
            Slow(world);
        }
        catch (Exception e)
        {
            Fail("the squad look", e);
        }
    }

    void Slow(World world)
    {
        Register(world);
        if (heatMoment is { } hm && (world.GameHours - hm.Hours > 1 || world.GameHours < hm.Hours)) SendHeatMoment();
        if (RecordHost.Announce())
        {
            foreach (var m in deeds.Pending) world.Post(m);
        }
        deeds.Pending.Clear();
        if (world.Tick % 150 == 75) Say(CompanionLink.Poll(world, deeds));      // Guild & Quests and Raid Commander, when installed
        if (world.Tick % 30 == 20) Notices.Watch(world, data);                 // floating notices: skill levels, faction standing
#if !SQUAD_RECORD_ONLY
        if (world.Tick % 90 == 45 && LwMod.Settings.LookoutCalls) lookout.Look(world, Talk, data);
        if (world.Tick % 90 == 15 && data is not null) FallenName.Set(data, deeds.Record.FallenMembers.Count > 0 ? deeds.Record.FallenMembers[^1].Name : null);
#endif
        if (world.Tick % LookEvery != 0) return;

        int panelEvery = PanelEvery;
#if !SQUAD_RECORD_ONLY
        if (LwMod.Settings.LighterChecks) panelEvery *= 2;
#endif
        List<int>? panelPlayers = world.Tick % panelEvery == 0 ? panelSlots : null;
        panelPlayers?.Clear();

        LaterDeaths(world);
        var occupied = Look(world, panelPlayers);
        Benches(world);
#if !SQUAD_RECORD_ONLY
        Fade(world, occupied);
#endif
        if (panelPlayers is not null)
        {
            PublishPanelPlayers(world, panelPlayers);
            RecordHost.Panel = StatsPanel.Text(deeds, occupied, presentInRecord);
            var catalog = deeds.Record.Catalog();
            var fallen = deeds.Record.FallenLines();
#if !SQUAD_RECORD_ONLY
            if (LwMod.Settings.AnimalCompanions)
                catalog.Add($"-- Animal companions --\n{SquadRecord.CompanionTitleName}: survive {CompanionMemorials.Battles} fights with the squad");
            LwState.RegisteredIn(world)?.Companions?.AppendFallen(fallen, deeds.Record.FallenMembers.Count > 0);
#endif
            RecordHost.CatalogText = string.Join("\n", catalog);
            RecordHost.HistoryText = string.Join("\n", deeds.Record.HistoryLines());
            RecordHost.FallenText = string.Join("\n", fallen);
        }
        if (world.Tick % (LookEvery * 600) == 0) Prune(world);
    }

    void PublishPanelPlayers(World world, List<int> slots)
    {
        presentInRecord.Clear();
#if !SQUAD_RECORD_ONLY
        nativePanelMembers.Clear();
        bool nativePanels = LwMod.Settings.RecordInPanels || CharacterTitleDisplay.Enabled;
#endif
        var table = world.Characters;
        var hot = table.Previous;
        foreach (int slot in slots)
        {
            if (table.Cold(slot) is not { IsPlayer: true } cold || cold.Medical is { Dead: true }
                || OtherModsPeople.Runs(world, cold)) continue;
            bool inRecord = hot[slot].Alive && cold.Medical is not null;
#if !SQUAD_RECORD_ONLY
            bool inNativePanel = nativePanels && cold.Animal is null;
            if (!inRecord && !inNativePanel) continue;
            string key = deeds.Who(cold);
            if (inNativePanel) nativePanelMembers.Add((table.IdOf(slot), key));
            if (inRecord) presentInRecord.Add(key);
#else
            if (inRecord) presentInRecord.Add(deeds.Who(cold));
#endif
        }
#if !SQUAD_RECORD_ONLY
        PanelLines.Publish(deeds.Record, nativePanelMembers, world);
        CharacterTitleDisplay.Publish(deeds.Record, nativePanelMembers, world);
#endif
    }

    void RecordFallen(World world, CharacterId id, CharacterCold member, string? town,
        string? killer = null, string? faction = null, string? killerRecord = null)
    {
        if (member.Animal is not null)
        {
#if !SQUAD_RECORD_ONLY
            CompanionMemorials.Died(world, deeds.Record, member, town);
#endif
            return;
        }
        string name = deeds.Who(member);
        if (deeds.Record.FallenMembers.Any(f => f.Name == name)) return;
        string place = town ?? "the wilds";
        deeds.Record.Hours = world.GameHours;
        deeds.Record.Place = place;
        deeds.Record.Died(name, world.GameHours, place, killer, faction, killerRecord);
        RecordHost.Log($"memorial: {member.Name} fell at {place}{(killer is null ? "" : $", killed by {killer} ({faction})")}");
        toMourn.Add((id, name, world.GameHours));
    }

    /// <summary>Every player character: time on its task, distance walked, falling down, limbs lost. Returns the towns they are in.</summary>
    HashSet<string> Look(World world, List<int>? panelPlayers)
    {
        var table = world.Characters;
        var hot = table.Previous;
        var occupied = new HashSet<string>(StringComparer.Ordinal);
#if !SQUAD_RECORD_ONLY
        occupiedPriceNodes.Clear();
        bool trackNodes = deeds.Heat.HasOwnedHeat;
        List<string>? survivingCompanions = null;
        bool companions = LwMod.Settings.AnimalCompanions && deeds.Record.InFight;
#endif
        var fighting = new List<string>();
        var downNow = new List<string>();
        var squad = new List<(string Name, CharacterId Id, System.Numerics.Vector3 At, bool Down)>();
        string? fightPlace = null;
        float seconds = LookEvery * world.TickSeconds;
        for (int s = 0; s < hot.Length; s++)
        {
            if (!hot[s].Alive && panelPlayers is null) continue;
            if (table.Cold(s) is not { IsPlayer: true } cold) continue;
            panelPlayers?.Add(s);
            if (!hot[s].Alive || OtherModsPeople.Runs(world, cold)) continue;
            var id = table.IdOf(s);
            var at = hot[s].Position;
            int site = data?.TownAt(at) ?? -1;
            string? townHere = deeds.SiteName(site);
            string me = deeds.Who(cold);
            if (cold.Medical is { Dead: true })
            {
                RecordFallen(world, id, cold, townHere);
                continue;
            }
            if (cold.Animal is null) deeds.Record.Seen(me, world.GameHours);
#if !SQUAD_RECORD_ONLY
            else if (companions && cold.PortraitSerial is not null && cold.Medical is { Dead: false } && !hot[s].Dormant)
                (survivingCompanions ??= []).Add(me);
#endif
            if (townHere is not null)
            {
                occupied.Add(townHere);
#if !SQUAD_RECORD_ONLY
                if (trackNodes) occupiedPriceNodes.Add(deeds.PriceNode(site));
#endif
            }
            deeds.Record.Hours = world.GameHours;
            deeds.Record.Place = townHere ?? "the wilds";

            bool down = cold.Medical?.Incapacitated == true;
#if !SQUAD_RECORD_ONLY
            bool sampleLimbs = !observeLimbs;
#else
            const bool sampleLimbs = true;
#endif
            int arms = sampleLimbs ? cold.Medical?.Parts.Count(p => p.Limb == LimbState.Stump && p.Template.Type == Meitou.Data.Gameplay.Bodies.BodyPartType.Arm) ?? 0 : 0;
            int legs = sampleLimbs ? cold.Medical?.Parts.Count(p => p.Limb == LimbState.Stump && p.Template.Type == Meitou.Data.Gameplay.Bodies.BodyPartType.Leg) ?? 0 : 0;
            float fed = cold.Medical?.Fed ?? 0;
            double moved = 0;
            if (last.TryGetValue(id, out var before))
            {
                moved = System.Numerics.Vector3.Distance(before.At, at);
                if (moved > 3000) moved = 0;                                     // a teleport or a load, not a walk
                if (down && !before.Down)
                {
                    deeds.Record.Downed(me);
                    if (townHere is not null) deeds.Record.DownedIn(me, townHere);
                }
                for (int n = before.Arms; n < arms; n++) deeds.Record.LimbLost(me, "arm", townHere);
                for (int n = before.Legs; n < legs; n++) deeds.Record.LimbLost(me, "leg", townHere);

                // A bite sets the stomach to a hundredth of the hunger points it gives (FeedSystem.Eat); it only drains between bites.
                if (fed > before.Fed + 0.001f) Say(deeds.Record.Ate(me, fed * 100));
            }
            last[id] = (at, down, arms, legs, fed);
            if (cold.Animal is null) squad.Add((me, id, at, down));
            if (cold.Animal is null && !down) RecapLink.SquadIsAt(at);      // a place for Recap moments that name none
            if (down) downNow.Add(me);
            else if (cold.InCombat && !hot[s].Dormant)        // a Proving Grounds bout counts too: a win there gets its victory line
            {
                fighting.Add(me);
                fightPlace ??= townHere ?? "the wilds";
            }
            if (down) continue;
            // a ride or a voyage (Meitou Transport) moves the seated character too: counted on its own, not as walking
            if (moved > 1 && TransportLink.Mode(world, id) is var mode and not TransportLink.None)
            {
                Say(deeds.Record.Travelled(me, mode, moved));
                moved = 0;
            }
            if (Activities.OfName(((Meitou.Simulation.Movement.CharacterTask)hot[s].Task).ToString(), moved > 1) is { } activity) Say(deeds.Record.Spend(me, activity, seconds, moved));
        }
        bool resumedFight = deeds.Record.HasResumedFight;
        if (fighting.Count > 0 || deeds.Record.InFight)
        {
            deeds.Record.Hours = world.GameHours;
            if (fightPlace is not null) lastFightPlace = fightPlace;
            deeds.Record.Place = lastFightPlace;
#if !SQUAD_RECORD_ONLY
            DefeatLines(world, squad);
#endif
            int freshFoesUp = FoesUp(world, squad, foes);
            if (resumedFight) BindSavedFoes(world);
            int savedFoesUp = resumedFight ? FoesUp(world, squad, restoredFoes, restored: true) : 0;
            Say(deeds.Record.FightLook(fighting, downNow, freshFoesUp + savedFoesUp));
#if !SQUAD_RECORD_ONLY
            if (resumedFight && deeds.Record.FightLookSinceLoad(fighting, downNow, freshFoesUp) is { } wonAfterLoad)
            {
                RivalsSystem.For(world)?.FightEnded(world, wonAfterLoad);
                // These opponents remain part of the saved record's fight if it is still open.
                foreach (var (id, foe) in foes) restoredFoes[id] = foe;
                foes.Clear();
            }
#endif
            if (!deeds.Record.InFight)
            {
#if !SQUAD_RECORD_ONLY
                if (survivingCompanions is not null)
                    foreach (string key in survivingCompanions)
                        if (deeds.Record.Fighters.Contains(key)) CompanionMemorials.Survived(world, deeds.Record, key);
#endif
                foes.Clear();
                restoredFoes.Clear();
                savedFoes = null;
                deeds.Record.FinishResumedFight();
#if !SQUAD_RECORD_ONLY
                wakingFoes.Clear();
#endif
            }
        }
#if !SQUAD_RECORD_ONLY
        VictoryLine(world, squad, resumedFight);
        Mourn(world, squad);
        if (world.Tick % 150 == 0 && deeds.Kindness.People.Count > 0 && KindnessWatch.Enabled)
            deeds.KindnessWhere.Look(world, deeds, squad);
#endif
        return occupied;
    }

#if !SQUAD_RECORD_ONLY
    StoryTaskBar? mourningBar;
    (CharacterId Body, string Name, double Hours)? shownMourning;

    void ClearMourningBar()
    {
        mourningBar?.Clear();
        mourningBar = null;
        shownMourning = null;
    }

    const int SeeRagdollEvent = 4;          // EV_I_SEE_RAGDOLL: Meitou does not fire it; vanilla gives player characters no lines on it

    /// <summary>Once the fight is over, the nearest standing squadmate (within 250 m) says something over each new body, by name
    /// (/YOURNAME/ is the fallen: the target). A body no longer in the world, or nobody near it within a game hour, is let go.</summary>
    void Mourn(World world, List<(string Name, CharacterId Id, System.Numerics.Vector3 At, bool Down)> squad)
    {
        var finishedBar = shownMourning;
        ClearMourningBar();
        if (!LwMod.Settings.Memorials) { toMourn.Clear(); return; }
        if (toMourn.Count == 0 || deeds.Record.InFight) return;
        var table = world.Characters;
        foreach (var (body, name, hours) in toMourn.ToList())
        {
            int b = -1;
            bool there = !body.IsNone && table.TryResolveNext(body, out b) && table.Cold(b) is { } bc && deeds.Who(bc) == name;   // a reused handle is someone else
            if (!there || world.GameHours - hours > 1)
            {
                // nobody came to the body within the hour (seen in play: no keepsake at all), or the body is gone (or a save was loaded): the
                // nearest standing squadmate still gets it, without the words over the body. Nobody standing yet: it waits (two days at most).
                var bodyAt = there ? table.Next[b].Position : (System.Numerics.Vector3?)null;
                var heir = squad.Where(m => !m.Down).OrderBy(m => bodyAt is { } p ? System.Numerics.Vector3.Distance(m.At, p) : 0).FirstOrDefault();
                if (heir.Name is null)
                {
                    if (world.GameHours - hours > 48 || world.GameHours < hours - 1) { toMourn.Remove((body, name, hours)); RecordHost.Log($"memorial: nobody stood to keep something of {name}'s"); }
                    continue;
                }
                toMourn.Remove((body, name, hours));
#if !SQUAD_RECORD_ONLY
                Trophies.GiveKeepsake(world, heir.Id, deeds.Record.Display(heir.Name), deeds.Record.Display(name), name);
                RecordHost.Log($"memorial: nobody came to {name}'s body; {heir.Name} has the keepsake");
#endif
                continue;
            }
            var at = table.Next[b].Position;
            var near = squad.Where(m => !m.Down).OrderBy(m => System.Numerics.Vector3.Distance(m.At, at)).FirstOrDefault();
            if (near.Name is null || System.Numerics.Vector3.Distance(near.At, at) > 2500) continue;
            var memorial = (body, name, hours);
            if (mourningBar is null && finishedBar != memorial && StoryTaskBar.Available &&
                Trophies.Keepsake is not null && LwMod.Settings.Trophies &&
                table.TryResolveNext(near.Id, out int mourner) && table.Cold(mourner) is { Inventory: not null } actor &&
                StoryTaskBar.TryShow(actor, $"Remembering {deeds.Record.Display(name)}...") is { } bar)
            {
                mourningBar = bar;
                shownMourning = memorial;
                continue;
            }
            toMourn.Remove(memorial);
#if !SQUAD_RECORD_ONLY
            Trophies.GiveKeepsake(world, near.Id, deeds.Record.Display(near.Name), deeds.Record.Display(name), name);
#endif
            if (Talk is null || !LwMod.Settings.VictoryLines) continue;
            bool said = TalkPacer.Exempt(() => Talk.FireEvent(world, near.Id, body, SeeRagdollEvent));
            if (RecordHost.LogChanges()) RecordHost.Log($"memorial: {near.Name} {(said ? "spoke over" : "stood by")} {name}");
        }
    }

    const int DefeatedSquadEvent = 15;     // EV_I_DEFEATED_SQUAD: "I just won a battle, whole enemy squad is down. Target is the person I defeated."

    /// <summary>
    /// The squad's victory line: when a fight the squad won ends, one of those still standing (a different one each time) gets event 15 with
    /// the last enemy struck as the target; only lines written for the player's characters can answer (squad banter, recruits, grudges).
    /// </summary>
    void VictoryLine(World world, List<(string Name, CharacterId Id, System.Numerics.Vector3 At, bool Down)> squad, bool resumedFight)
    {
        if (deeds.Record.TakeFightEnd() is not { } end) return;
        bool covered = WasNativeDefeat(world, lastEnemy);
        ResetDefeatDialogue();
        if (!resumedFight) RivalsSystem.For(world)?.FightEnded(world, end.Won);
        if (Talk is null || !LwMod.Settings.VictoryLines || !end.Won || end.Standing.Count == 0 || covered) return;
        var table = world.Characters;
        if (!table.TryResolveNext(lastEnemy, out _)) return;
        var standing = squad.Where(m => !m.Down && end.Standing.Contains(m.Name)).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
        if (standing.Count == 0) return;
        var speaker = standing[(int)(world.Tick / 30 % standing.Count)];
        // most victory lines talk to the rest of the squad ("take their boots"); those carry a squad-near condition, and a few lines for one
        // fighting alone carry the opposite (squad_banter.json), so a lone fighter is asked too (seen in play: a solo squad never spoke)
        bool said = TalkPacer.Exempt(() => Talk.FireEvent(world, speaker.Id, lastEnemy, DefeatedSquadEvent));
        if (RecordHost.LogChanges()) RecordHost.Log($"victory line: {speaker.Name} {(said ? "spoke" : "had nothing to say")}");
    }

    /// <summary>
    /// The enemy's victory line: the moment everyone of the squad who fought is down, the nearest standing fighter of up to two enemy squads
    /// gets event 15 with the nearest downed squad member as the target. This also wakes vanilla's own event-15 lines (guards arresting a
    /// beaten squad, and the like), which is why it has its own setting, off by default.
    /// </summary>
    void DefeatLines(World world, List<(string Name, CharacterId Id, System.Numerics.Vector3 At, bool Down)> squad)
    {
        if (Talk is null || !LwMod.Settings.EnemyVictoryLines)
        {
            wipeTold = wipePending = false;
            return;
        }
        var fought = deeds.Record.Fighters;
        var down = squad.Where(m => m.Down && fought.Contains(m.Name)).ToList();
        bool wiped = fought.Count > 0 && fought.All(n => squad.Any(m => m.Name == n && m.Down) || squad.All(m => m.Name != n));
        if (!wiped || down.Count == 0) { if (!wiped) wipeTold = wipePending = false; return; }
        if (wipeTold) return;
        if (nativeDefeat && !wipePending)
        {
            wipePending = true;
            return;
        }
        wipeTold = true;
        if (nativeDefeat && down.Any(member => WasNativeDefeat(world, member.Id))) return;

        var table = world.Characters;
        var hot = table.Previous;
        var best = new Dictionary<int, (float Dist, CharacterId Id, CharacterId Target)>();
        for (int s = 0; s < hot.Length; s++)
        {
            if (!hot[s].Alive || hot[s].Dormant || table.Cold(s) is not { IsPlayer: false, InCombat: true } c || c.Animal is not null
                || c.Medical is { Incapacitated: true } || IsHired(world, c)) continue;
            var here = hot[s].Position;
            float dist = float.MaxValue;
            var target = CharacterId.None;
            foreach (var m in down)
            {
                float d = System.Numerics.Vector3.Distance(m.At, here);
                if (d < dist) { dist = d; target = m.Id; }
            }
            if (dist > 2500) continue;
            int key = c.SquadId;
            if (!best.TryGetValue(key, out var b) || dist < b.Dist) best[key] = (dist, table.IdOf(s), target);
        }
        foreach (var (_, (_, id, target)) in best.OrderBy(p => p.Value.Dist).Take(2))
        {
            bool said = TalkPacer.Exempt(() => Talk.FireEvent(world, id, target, DefeatedSquadEvent));
            if (RecordHost.LogChanges()) RecordHost.Log($"defeat line: an enemy {(said ? "spoke" : "had nothing to say")} over your downed squad");
        }
    }
#endif

    string lastFightPlace = "the wilds";

    /// <summary>
    /// Items finished at a crafting bench go to its output (CraftState.Done), not to the crafter; the bench remembers its last operator
    /// (Machine.Smith). A rise in a bench's output since the last look is credited to that operator when it is a player character. Output already
    /// there the first time a bench is seen (a loaded save) is not counted.
    /// </summary>
    void Benches(World world)
    {
        var machines = world.Machines;
        if (machines.Version == benchVersion) return;
        benchVersion = machines.Version;
        var table = world.Characters;
        foreach (var m in machines.All)
        {
            if (m.Craft is not { } craft) continue;
            var now = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in craft.Done) now[item.Record] = now.GetValueOrDefault(item.Record) + item.Quantity;
            bool known = benchOutput.TryGetValue(m.Site, out var before);
            benchOutput[m.Site] = now;
            if (!known || !table.TryResolveNext(m.Smith, out int s) || table.Cold(s) is not { IsPlayer: true } smith) continue;
            foreach (var (record, count) in now)
            {
                int made = count - before!.GetValueOrDefault(record);
                if (made <= 0) continue;
                Say(deeds.Record.Made(deeds.Who(smith), CraftKindOf(m, record), made));
                if (RecordHost.LogChanges()) RecordHost.Log($"crafted: {smith.Name} made {made} x {data?.Db.Find(record)?.Name ?? record}");
            }
        }
    }

    static string CraftKindOf(Meitou.Simulation.Production.Machine m, string record) =>
        m.Data.Craft?.Options.FirstOrDefault(o => o.Item == record)?.Kind switch
        {
            Meitou.Data.Gameplay.Construction.CraftKind.Weapon => "weapons",
            Meitou.Data.Gameplay.Construction.CraftKind.Armour => "armour",
            Meitou.Data.Gameplay.Construction.CraftKind.Crossbow => "crossbows",
            Meitou.Data.Gameplay.Construction.CraftKind.Robotics => "robotics",
            _ => "items",
        };

#if !SQUAD_RECORD_ONLY
    void Fade(World world, HashSet<string> occupied)
    {
        if (double.IsNaN(lastFadeHours) || world.GameHours < lastFadeHours) lastFadeHours = world.GameHours;
        double hours = world.GameHours - lastFadeHours;
        if (hours < 1.0 / 60) return;
        deeds.Heat.Fade(hours, occupied, occupiedPriceNodes);
        lastFadeHours = world.GameHours;
    }
#endif

    void Prune(World world)
    {
        var table = world.Characters;
        cutParts.RemoveWhere(p => !table.IsAlive(p.Item1));
        foreach (var k in stumpsBefore.Keys.Where(k => !table.IsAlive(k)).ToList()) stumpsBefore.Remove(k);
        if (struck.Count > 4096) struck.Clear();
        foreach (var k in last.Keys.Where(k => !table.IsAlive(k)).ToList()) last.Remove(k);
    }

    void Register(World world)
    {
        if (registered) return;
        RecapLink.ForgetSquadPlace();
#if !SQUAD_RECORD_ONLY
        deeds.Tracker = this;
        observeLimbs = LifecycleWatch.TracksLimbs(world);
#endif
        LwDeeds.Register(world, deeds);
        registered = true;
    }

    #endregion

    #region save and hash

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        deeds.Record.Write(w);
#if !SQUAD_RECORD_ONLY
        deeds.Heat.Write(w);
#endif
        WriteStruck(w);
        // keepsakes still to be given (after the struck list, so older builds stop reading before it): the body's handle doesn't survive a
        // load, the fallen's name does (from a review of the code: a save before the fight ended lost the keepsake)
        w.Write(MournTag);
        w.Write(toMourn.Count);
        foreach (var (_, name, hours) in toMourn) { w.Write(name); w.Write(hours); }
#if !SQUAD_RECORD_ONLY
        deeds.Heat.WriteOwners(w);
        deeds.Kindness.Write(w);
#endif
        WriteFightTrailer(w);
    }

    const int MournTag = 0x4C4D524E;   // "LMRN"

    /// <summary>The named victims the squad struck last and that may still bleed out. Others cannot be found after a load.</summary>
    void WriteStruck(BinaryWriter w)
    {
        var named = struck.Values.Where(k => k.UniqueRecord is not null).GroupBy(k => k.UniqueRecord!).Select(g => g.Last())
            .Concat(pendingNamed.Values).GroupBy(k => k.UniqueRecord!).Select(g => g.First()).OrderBy(k => k.UniqueRecord, StringComparer.Ordinal).ToList();
#if SQUAD_RECORD_ONLY
        w.Write(2);
#else
        w.Write(3);                                     // 3: placed town identity at the time of the hit
#endif
        w.Write(named.Count);
        foreach (var k in named)
        {
            w.Write(k.UniqueRecord!); w.Write(k.Striker); w.Write(k.Name); w.Write(k.Faction); w.Write(k.Leader); w.Write(k.Named ?? "");
            w.Write(k.Town ?? ""); w.Write(k.Owner ?? ""); w.Write(k.Local); w.Write(k.Hours); w.Write(k.Sport);
#if !SQUAD_RECORD_ONLY
            w.Write(k.PriceNode);
#endif
        }
    }

    void ReadStruck(BinaryReader r)
    {
        pendingNamed.Clear();
        if (r.BaseStream.Position >= r.BaseStream.Length) return;    // a save from before this was kept
        int format = r.ReadInt32();
        if (format is < 1 or > 3) return;
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            string rec = r.ReadString();
            var k = new Struck(r.ReadString(), r.ReadString(), r.ReadString(), r.ReadBoolean(), Blank(r.ReadString()), Blank(r.ReadString()),
                Blank(r.ReadString()), r.ReadBoolean(), r.ReadDouble(), rec);
            // a named fighter struck in a bout can bleed out after a reload: still sport, no heat (from a review of the code)
            if (format >= 2) k = k with { Sport = r.ReadBoolean() };
            if (format >= 3)
            {
                string node = r.ReadString();
#if !SQUAD_RECORD_ONLY
                k = k with { PriceNode = node };
#endif
            }
            pendingNamed[rec] = k;
        }

        static string? Blank(string v) => v.Length == 0 ? null : v;
    }

    public void Read(Stream stream)
    {
        ResetFightTrailer();
#if !SQUAD_RECORD_ONLY
        ClearMourningBar();
        deeds.Kindness.Clear();
        deeds.KindnessWhere.Clear();
#endif
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        try
        {
            if (!deeds.Record.Read(r))
            {
                RecordHost.Log("the saved squad record is from a newer version of the mod; this one starts a fresh record");
                return;
            }
#if !SQUAD_RECORD_ONLY
            deeds.Heat.Read(r);
#endif
            ReadStruck(r);
            toMourn.Clear();
            if (r.BaseStream.Length - r.BaseStream.Position >= 8 && r.ReadInt32() == MournTag)
            {
                for (int n = r.ReadInt32(); n > 0; n--) toMourn.Add((CharacterId.None, r.ReadString(), r.ReadDouble()));
#if !SQUAD_RECORD_ONLY
                deeds.Heat.ReadOwners(r);
                deeds.Kindness.Read(r);
#endif
            }
            ReadFightTrailer(r);
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or InvalidDataException or FormatException)
        {
            RecordHost.Log($"the saved squad record could not be read ({e.Message}); starting a fresh one");
        }
    }

    public void Hash(ref StateHasher hasher)
    {
        foreach (long v in deeds.Record.HashParts()) hasher.Add(v);
#if !SQUAD_RECORD_ONLY
        foreach (long v in deeds.Heat.HashParts()) hasher.Add(v);
        foreach (long v in deeds.Heat.OwnerHashParts()) hasher.Add(v);
        foreach (long v in deeds.Kindness.HashParts()) hasher.Add(v);
        deeds.KindnessWhere.Hash(ref hasher);
#endif
    }

    #endregion
}

/// <summary>
/// Player-faction characters another mod runs on its own (Fiefdom's townspeople and guards: squads of the player's faction that run
/// themselves, Squad.Independent, and carry that mod's Squad.ModTags key). They are not the squad: no record, no keepsake, no rival, no
/// fight. Both properties are Meitou 0.21+ (read by reflection: built against 0.17.3); without them nobody is excluded.
/// </summary>
internal static class OtherModsPeople
{
    const string OurTag = "LivingWastelandMeitou";
    static readonly System.Reflection.PropertyInfo? independent = typeof(Meitou.Simulation.Factions.Squad).GetProperty("Independent");
    static readonly System.Reflection.PropertyInfo? tags = typeof(Meitou.Simulation.Factions.Squad).GetProperty("ModTags");

    internal static bool Runs(World world, CharacterCold c)
    {
        if (!c.IsPlayer || world.Squads.Find(c.SquadId) is not { } squad) return false;
        if (independent?.GetValue(squad) is true) return true;
        return tags?.GetValue(squad) is IDictionary<string, string> t && t.Keys.Any(k => k != OurTag);
    }
}
