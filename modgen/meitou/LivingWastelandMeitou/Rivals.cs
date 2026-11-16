// Rivals (Living Wasteland only): someone the squad beat and left alive can come back for it.
//
// Every fight, the enemies the squad hurt are noted (damage, a limb cut off, knocked down, a squad leader). When a won fight ends, the most
// notable one still alive and free may become a rival: the actual character, renamed for what happened ("Hollin One-Arm"), with a grudge and
// a squad that the world keeps (its platoon is made persistent). The first won fight with such a survivor always makes one, so the feature
// shows itself early; later ones come now and then, at most three at a time.
// A rival recovers for two to four days, then hunts the squad (its squad walks after the nearest player character, as the bounty crews do),
// a little stronger each time. If it beats the squad it boasts and goes away to recover; if it loses and lives it gets away again; if it dies
// or is jailed, it is over. Townsfolk near a rival talk about it (word swaps LWRIVAL_k carry the live name), and the Atlas lists rivals.
// The character is resolved by its handle, and after a load by name and faction (names are saved with the character).
using System.Numerics;
using System.Reflection;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Population;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Factions;

namespace LivingWasteland.MeitouMod;

public sealed record RivalTable(List<int> Codes, List<string> Tokens);

public sealed class RivalsSystem(PopulationData? data) : ITickSystem, IModSaveData, IStateHashed
{
    public const int Slots = 3;
    const int PassEvery = 300;
    const int Format = 3;
    const float TalkKm = 7;

    public enum Phase { Recovering = 0, Hunting = 1, Dead = 2, Jailed = 3, Lost = 4 }

    public sealed class Rival
    {
        public int Slot;
        public string Name = "", OldName = "", Faction = "", Grudge = "", By = "";
        public double Made, Next;
        public double LastCounted = double.MinValue;   // game hour of the last fight end that counted (not saved: a reload starts a new fight)
        public int Returns, Wins, Boosted, PlatoonId = -1;
        public bool Nameless;          // the old name was only a type ("Dust Bandit"): "Once a Dust Bandit."
        public Phase Phase;
        public float X, Z;
        public CharacterId Id = CharacterId.None;
        public bool Told = true;       // announced; until then the squad has not walked away yet (a rival made quietly at the fight's end)
        public bool Active => Told && Phase is Phase.Recovering or Phase.Hunting;
        public bool Holds => Phase is Phase.Recovering or Phase.Hunting;   // takes a slot (told or not)
    }

    readonly List<Rival> rivals = [];

    /// <summary>The rivals the squad has been told about (WastelandApi).</summary>
    internal IEnumerable<(string Name, string Faction, string Phase, float X, float Z, int Returns, int Wins)> Told() =>
        rivals.Where(r => r.Told).Select(r => (r.Name, r.Faction, r.Phase.ToString().ToLowerInvariant(), r.X, r.Z, r.Returns, r.Wins));
    int made;                       // rivals ever made in this game (the first is guaranteed)
    double lastMade = -1e9;
    bool failed;

    // this fight: enemies the squad hurt, and rivals that took part
    sealed class Hurt { public double Damage; public bool Limb, Arm, Leg, Downed, Leader; public string By = ""; }
    readonly Dictionary<CharacterId, Hurt> hurt = [];
    readonly HashSet<int> inFight = [];

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, RivalsSystem> byWorld = new();
    public static RivalsSystem? For(World world) => byWorld.TryGetValue(world, out var s) ? s : null;
    internal static RivalsSystem? Latest;
    World? seen;

    // ---- seeing fights (called by Notoriety's hit observer and fight end) ----

    /// <summary>A blow in a fight: an enemy the squad struck (who struck, how hard, a limb, a fall), or a rival taking part either way.</summary>
    public void Hit(World world, in CombatHit hit, CharacterCold victim, CharacterCold? attacker, string? striker, bool leader)
    {
        if (!LwMod.Settings.Rivals) return;
        // an unloaded rival has no handle (None), and some blows have no attacker (None): never let None match None
        // the handle alone is not enough: an unloaded rival's slot is reused by whoever loads next (seen in play: a bounty crew killed over and
        // over counted as the rival "getting away again"), so the character on that handle must also carry the rival's name
        foreach (var r in rivals)
            if (r.Active && !r.Id.IsNone && (r.Id == hit.Victim || (!hit.Attacker.IsNone && r.Id == hit.Attacker)) && IsRival(world, r, r.Id))
                inFight.Add(r.Slot);
        if (striker is null || victim.IsPlayer || victim.Animal is not null || victim.Medical is not { } med) return;
        if (!hurt.TryGetValue(hit.Victim, out var h)) hurt[hit.Victim] = h = new Hurt();
        h.Damage += (hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce) * hit.Damage.Scale;
        h.By = striker;
        h.Leader |= leader;
        if (med.Incapacitated) h.Downed = true;
        if (hit.Part >= 0 && hit.Part < med.Parts.Count && med.Parts[hit.Part].Limb == LimbState.Stump)
        {
            h.Limb = true;
            var type = med.Parts[hit.Part].Template.Type;
            h.Arm |= type == BodyPartType.Arm;
            h.Leg |= type == BodyPartType.Leg;
        }
    }

    /// <summary>A fight the squad was in has ended (won: someone of the squad still standing).</summary>
    public void FightEnded(World world, bool won)
    {
        if (!LwMod.Settings.Rivals) { hurt.Clear(); inFight.Clear(); return; }
        try
        {
            var table = world.Characters;
            // rivals who were in it: beat the squad, got away again, or are done
            foreach (var r in rivals.Where(r => inFight.Contains(r.Slot) && r.Active))
            {
                Resolve(world, r);
                if (!IsRival(world, r, r.Id)) continue;          // not loaded or not them: they were not in this fight
                if (!Alive(world, r, out _)) continue;
                var outcome = RivalRules.OnFightEnd(r.Phase == Phase.Hunting, won, world.GameHours, r.LastCounted);
                if (outcome == RivalRules.Outcome.None) { LwMod.Log($"rivals: {r.Name} was in a fight ({(won ? "squad won" : "squad lost")}, {r.Phase.ToString().ToLowerInvariant()}): not counted"); continue; }
                r.LastCounted = world.GameHours;
                if (outcome == RivalRules.Outcome.BeatSquad)
                {
                    r.Wins++;
                    if (r.Phase == Phase.Hunting) { r.Phase = Phase.Recovering; r.Next = world.GameHours + 72; }
                    Tell(world, $"{r.Name} beat your squad and walked away laughing.", $"{r.Name} beat the squad and left them in the dirt.", 70, new Vector3(r.X, 0, r.Z));
                }
                else
                {
                    r.Returns++;
                    r.Phase = Phase.Recovering;
                    r.Next = world.GameHours + 48 + Spread(r.Name, r.Returns, 48);
                    Tell(world, $"{r.Name} got away again.", $"{r.Name} lost again and got away again.", 55, new Vector3(r.X, 0, r.Z));
                }
            }
            if (won) MaybeMake(world);
        }
        catch (Exception e) { LwMod.Log($"rivals: skipped a fight's end after an error: {e.GetBaseException().Message}"); }
        hurt.Clear();
        inFight.Clear();
    }

    void MaybeMake(World world)
    {
        int active = rivals.Count(r => r.Holds);
        if (active >= Slots || rivals.Any(r => !r.Told)) { if (hurt.Count > 0) LwMod.Log($"rivals: no new rival ({(active >= Slots ? "three already" : "one is still being made")})"); return; }
        bool first = !rivals.Any(r => r.Holds);    // with no rival about (none yet, or the last one is dead or jailed), the next one is sure
        if (!first && (world.GameHours - lastMade < 48 || Spread("make", (long)world.GameHours, 100) >= 30))   // later: 30%, two days apart
        {
            if (hurt.Count > 0) LwMod.Log($"rivals: no new rival this fight ({(world.GameHours - lastMade < 48 ? "the last was made under two days ago" : "the 30% chance said no")})");
            return;
        }
        var table = world.Characters;
        (CharacterId Id, int Slot, int Score, Hurt H)? best = null;
        var others = new List<(CharacterId Id, int Score, Hurt H)>();
        foreach (var (id, h) in hurt)
        {
            if (!table.TryResolveNext(id, out int s) || !table.Next[s].Alive || table.Cold(s) is not { } c || c.IsPlayer || c.Animal is not null
                || c.IsImprisoned || c.Medical is { Dead: true } || world.Uniques.RecordOf(id) is not null || rivals.Any(r => r.Id == id || r.Name == c.Name)) continue;
            int score = (h.Limb ? 3 : 0) + (h.Leader ? 2 : 0) + (h.Downed ? 1 : 0) + (h.Damage >= 60 ? 1 : 0);
            others.Add((id, score, h));
            if (score < (first ? 1 : 2)) continue;
            if (best is null || score > best.Value.Score) best = (id, s, score, h);
        }
        if (best is not { } b || table.Cold(b.Slot) is not { } cold)
        {
            if (hurt.Count > 0) LwMod.Log($"rivals: no new rival: of {hurt.Count} enemies the squad hurt, {others.Count} lived, none notable enough (a lost limb, a leader, knocked down or hurt badly)");
            return;
        }
        // the rest of the fight's survivors, best first: seen in play (build ae), the one picked was often killed a moment later (the fight
        // "ends" while the beaten lie down; they get up and fight on), so another survivor of the same fight takes the grudge (Announce)
        backups = others.Where(o => o.Id != b.Id).OrderByDescending(o => o.Score).Select(o => (o.Id, o.H)).ToList();
        // made now, while the survivor is loaded (seen in play, build ae: four survivors in a row were dropped as "gone": the squad ran far
        // enough that they unloaded, and an unloaded character cannot be renamed). Announced once the squad has walked away (Announce).
        Make(world, b.Id, b.Slot, cold, b.H);
    }

    List<(CharacterId Id, Hurt H)> backups = [];
    readonly HashSet<string> killedBySquad = new(StringComparer.Ordinal);

    /// <summary>The squad killed someone (Notoriety's kill record): a rival of that name died at the squad's hands.</summary>
    public void SquadKilled(string name) { if (rivals.Any(r => r.Name == name)) killedBySquad.Add(name); }

    /// <summary>The rival-to-be died before they were told: the next survivor of the same fight still alive takes the grudge.</summary>
    bool Handover(World world, Rival r, CharacterCold dead)
    {
        var table = world.Characters;
        while (backups.Count > 0)
        {
            var (id, h) = backups[0];
            backups.RemoveAt(0);
            if (!table.TryResolveNext(id, out int s) || !table.Next[s].Alive || table.Cold(s) is not { } c || c.IsPlayer || c.IsImprisoned
                || c.Medical is not { Dead: false } || !c.CarriedBy.IsNone || rivals.Any(x => x.Id == id || x.Name == c.Name)) continue;
            dead.Name = r.OldName;                       // the body keeps its own name
            rivals.Remove(r);
            made--;
            LwMod.Log($"rivals: {r.Name} died after the fight; another survivor takes the grudge");
            Make(world, id, s, c, h);
            return true;
        }
        return false;
    }

    /// <summary>
    /// A rival made at a fight's end is announced ("crawled away from the fight") once two game hours have passed with the squad 250 m away
    /// (eight hours at most; an unloaded one counts as away). Until then they are kept from bleeding out (they are the one who lives), and
    /// one who is killed, jailed or carried off by the squad first is let go quietly.
    /// </summary>
    void Announce(World world, List<(float X, float Z)> players)
    {
        foreach (var r in rivals.Where(r => !r.Told).ToList())
        {
            var table = world.Characters;
            Resolve(world, r);
            double since = world.GameHours - r.Made;
            bool loaded = table.TryResolveNext(r.Id, out int s) && table.Cold(s) is not null;
            if (loaded)
            {
                var cold = table.Cold(s)!;
                bool taken = !cold.CarriedBy.IsNone && table.TryResolveNext(cold.CarriedBy, out int cs) && table.Cold(cs) is { IsPlayer: true };
                if (!table.Next[s].Alive || cold.Medical is not { } med || med.Dead || cold.IsImprisoned || taken)
                {
                    if (!taken && !cold.IsImprisoned && since < 4 && Handover(world, r, cold)) continue;
                    LwMod.Log($"rivals: {r.Name} did not make it ({(cold.IsImprisoned || taken ? "taken" : "dead")}); no rival this time");
                    if (cold.Medical is { Dead: false }) cold.Name = r.OldName;
                    rivals.Remove(r);
                    made--;
                    continue;
                }
                if (med.Blood < 40) med.Blood = 40;
                var at = table.Next[s].Position;
                (r.X, r.Z) = (at.X, at.Z);
            }
            var near = Pursuit.Nearest(players, r.X, r.Z);
            bool away = !loaded || players.Count == 0 || (near.X - r.X) * (near.X - r.X) + (near.Z - r.Z) * (near.Z - r.Z) > 2500f * 2500f;
            if (!(away && since >= 2) && since < 8) continue;
            r.Told = true;
            r.Next = Math.Max(r.Next, world.GameHours + 12);
            Tell(world, $"{r.Name} crawled away from the fight. They won't forget your squad.",
                $"{r.Name} ({r.Faction}) {r.Grudge}{(r.By.Length > 0 ? $" ({r.By})" : "")} and swore to come back for it.", 72, new Vector3(r.X, 0, r.Z));
            LwMod.Log($"rivals: {r.Name} is told (after {since:0.0} h)");
            Words(world);
        }
    }

    void Make(World world, CharacterId id, int s, CharacterCold cold, Hurt h)
    {
        var table = world.Characters;
        var b = (Id: id, Slot: s, H: h);
        int slot = Enumerable.Range(0, Slots).First(k => rivals.All(r => !r.Holds || r.Slot != k));
        rivals.RemoveAll(r => r.Slot == slot);
        string first2 = FirstName(world, cold);
        string epithet = b.H.Arm ? "One-Arm" : b.H.Leg ? "Crutch" : b.H.Leader ? "the Unbowed" : b.H.Downed ? "Twice-Born" : b.H.Damage >= 60 ? "the Scarred" : "the Grudge-Keeper";
        string grudge = b.H.Arm ? "lost an arm to your squad" : b.H.Leg ? "lost a leg to your squad" : b.H.Leader ? "watched your squad break their crew"
            : b.H.Downed ? "was left for dead by your squad" : "barely survived your squad";
        var by = LwDeeds.For(world) is { } deeds ? deeds.Record.Display(b.H.By) : b.H.By;
        var r = new Rival
        {
            Slot = slot, OldName = cold.Name, Name = $"{first2} {epithet}", Faction = data is not null && cold.Faction >= 0 && cold.Faction < data.Factions.Count
                ? data.Factions[cold.Faction].Name : "no faction", Grudge = grudge, By = by, Made = world.GameHours,
            Next = world.GameHours + 24 + Spread(cold.Name, 0, 24), Phase = Phase.Recovering, Id = b.Id, Nameless = !lastNamed,
        };
        var at = table.Next[b.Slot].Position;
        (r.X, r.Z) = (at.X, at.Z);
        cold.Name = r.Name;
        if (Platoon(world, cold) is { } pl) { pl.Persistent = true; r.PlatoonId = pl.Id; }
        r.Told = false;
        rivals.Add(r);
        made++;
        lastMade = world.GameHours;
        LwMod.Log($"rivals: {r.OldName} is now {r.Name} ({r.Faction}): {grudge}; slot {slot} (told once the squad has walked away)");
    }

    // ---- the slow tick: recovering, hunting, done ----

    public void SlowWorld(World world)
    {
        Latest = this;
        if (!ReferenceEquals(seen, world)) { seen = world; byWorld.AddOrUpdate(world, this); Words(world); }
        if (failed || !LwMod.Settings.Rivals || world.Tick % PassEvery != 120) return;
        try { Pass(world); }
        catch (Exception e)
        {
            failed = true;
            LwMod.Log($"rivals: stopped after an error: {e.GetBaseException().Message}");
        }
    }

    void Pass(World world)
    {
        var players = Pursuit.Players(world);
        Announce(world, players);
        bool changed = false;
        foreach (var r in rivals.Where(r => r.Active))
        {
            Resolve(world, r);
            if (!Alive(world, r, out var cold))
            {
                if (cold is { IsImprisoned: true }) { r.Phase = Phase.Jailed; Tell(world, $"{r.Name} is in chains now.", $"{r.Name} ended up in chains.", 60); }
                else if (cold is null && r.Id.IsNone) { r.Phase = Phase.Lost; }
                else if (killedBySquad.Contains(r.Name))
                {
                    r.Phase = Phase.Dead;
                    Tell(world, $"{r.Name} is dead. The grudge died with them.", $"{r.Name} came back once too often. Your squad settled the grudge.", 75, new Vector3(r.X, 0, r.Z));
                }
                else
                {
                    // died out in the world, not at the squad's hands: a quiet line, no Recap moment (seen in play: a rival dead hours after
                    // being made, by someone else, read as the squad's own deed)
                    r.Phase = Phase.Dead;
                    Tell(world, $"Word on the road: {r.Name} is dead, and not by your hand.", $"{r.Name} died out in the wastes before they could come back for the squad.", 0);
                }
                changed = true;
                continue;
            }
            var table = world.Characters;
            if (table.TryResolveNext(r.Id, out int s)) { var at = table.Next[s].Position; (r.X, r.Z) = (at.X, at.Z); }
            if (cold is not null)
            {
                if (Platoon(world, cold) is { } own) { own.Persistent = true; r.PlatoonId = own.Id; }
                // stronger for each return, applied once the character is in the world
                for (; r.Boosted < r.Returns + (r.Phase == Phase.Hunting ? 1 : 0); r.Boosted++) Stronger(cold, r.Boosted);
                // licking their wounds: one left lying does not bleed out (seen in play: a new rival dead within hours, not at the squad's hands)
                if (r.Phase == Phase.Recovering && cold.Medical is { Dead: false } med && med.Blood < 40) med.Blood = 40;   // awake or not (seen in play: a one-armed rival bled out on their feet)
            }
            if (r.Phase == Phase.Recovering && world.GameHours >= r.Next && RivalryLink.MaySend(world, PartyClaims.Rival(r.Name)))
            {
                r.Phase = Phase.Hunting;
                r.Next = world.GameHours + 96;                  // gives up the trail after four days
                // one who lost a leg comes back on a cheap prosthetic, never crawling after the squad for days
                bool legged = cold is not null && Mend(cold);
                Tell(world, $"Word on the road: {r.Name} is hunting your squad{(legged ? ", on a new iron leg" : "")}.", null, 0);
                LwMod.Log($"rivals: {r.Name} is hunting the squad (return {r.Returns + 1})");
            }
            else if (r.Phase == Phase.Hunting)
            {
                if (world.GameHours >= r.Next)
                {
                    r.Phase = Phase.Recovering;
                    r.Next = world.GameHours + 48;
                    LwMod.Log($"rivals: {r.Name} lost the squad's trail");
                    continue;
                }
                var p = cold is not null ? Platoon(world, cold) : null;
                p ??= r.PlatoonId >= 0 ? world.Platoons.Find(r.PlatoonId) : null;
                if (players.Count > 0 && p is { Dead: false }) Pursuit.Steer(world, p, players, BarkLines.Rival(r.Grudge, r.Returns, r.Wins, r.Slot + r.Returns), r.Name);
            }
        }
        if (changed) Words(world);
        RivalryLink.Keep(world, "rival:", rivals.Where(r => r.Active && r.Phase == Phase.Hunting).Select(r => PartyClaims.Rival(r.Name)).ToList());
    }

    // ---- helpers ----

    static readonly string[] Firsts = ["Hollin", "Varro", "Tesk", "Mara", "Joss", "Brenn", "Kesh", "Orla", "Dace", "Rook", "Sabine", "Tamsin", "Corrick",
        "Ivo", "Wren", "Garra", "Nell", "Fitch", "Holt", "Senna", "Arlo", "Bex", "Callum", "Dunn", "Esk", "Faro", "Grist", "Hask", "Jory", "Kell",
        "Lorne", "Mabb", "Nyla", "Oswin", "Pell", "Quill", "Rusk", "Sorrel", "Tobin", "Ulla", "Vance", "Wick", "Yara", "Zeb", "Brack", "Cass",
        "Doyle", "Edda", "Flint", "Gage", "Hettie", "Iver", "Jessop", "Kirra", "Lusk", "Merrow", "Nash", "Odo", "Pike", "Rhea"];

    bool lastNamed;

    string FirstName(World world, CharacterCold c)
    {
        // a character with a name of its own keeps its first name; a "Dust Bandit" gets one: by the world's seed (different every game),
        // never one an active rival already has
        bool named = data?.Db.Find(c.RecordId) is { } rec && rec.GetBool("named");
        lastNamed = named;
        if (named && c.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts) return parts[0];
        int start = (int)((ulong)SquadRecord.StableHash($"{world.Seed}:{c.Name}:{c.RecordId}:{made}") % (ulong)Firsts.Length);
        for (int i = 0; i < Firsts.Length; i++)
        {
            string f = Firsts[(start + i) % Firsts.Length];
            if (rivals.All(r => !r.Name.StartsWith(f + " ", StringComparison.Ordinal))) return f;
        }
        return Firsts[start];
    }

    // the cheap hiver-made prosthetic legs every shop sells (Newwworld.mod), by limb slot: 2 left leg, 3 right leg
    static readonly Dictionary<int, string> EconomyLegs = new() { [2] = "96136-Newwworld.mod", [3] = "96135-Newwworld.mod" };

    /// <summary>
    /// A rival who lost a leg comes back on an Economy Leg (fitted as Meitou fits the prosthetics an NPC spawns with: the item in the limb's
    /// section and on the part). An arm lost stays lost. True when a leg was fitted.
    /// </summary>
    bool Mend(CharacterCold c)
    {
        if (data is null || c.Medical is not { } med || c.Inventory is not { } inv) return false;
        bool fitted = false;
        try
        {
            foreach (var part in med.Parts.ToList())
            {
                if (part.Limb != LimbState.Stump || part.Template.Type != BodyPartType.Leg || part.Replacement is not null) continue;
                int slot = part.Template.LimbSlot;
                if (!EconomyLegs.TryGetValue(slot, out var id) || data.Items.Limb(id) is not { } limb || data.Items.Create(id) is not { } item) continue;
                item.Level = 20;
                if (!med.FitReplacement(limb, item)) continue;
                (item.Section, item.X, item.Y) = (Meitou.Data.Gameplay.Bodies.LimbReplacement.SlotOf(slot), 0, 0);
                inv.Items.Add(item);
                fitted = true;
            }
        }
        catch (Exception e) { LwMod.Log($"rivals: could not fit a leg ({e.GetBaseException().Message})"); }
        return fitted;
    }

    static int Spread(string key, long n, int range) => (int)((ulong)SquadRecord.StableHash(key + "#" + n) % (ulong)Math.Max(1, range));

    /// <summary>A little stronger each time they come back: the fighting stats, up to 95.</summary>
    static void Stronger(CharacterCold c, int returns)
    {
        if (c.Stats is not { } st) return;
        float add = 5 + 3 * returns;
        foreach (var k in new[] { StatsEnumerated.Strength, StatsEnumerated.Toughness, StatsEnumerated.MeleeAttack, StatsEnumerated.MeleeDefence, StatsEnumerated.Dodge })
            st[k] = Math.Min(95, st[k] + add);
    }

    static Platoon? Platoon(World world, CharacterCold c)
    {
        if (c.SquadId < 0) return null;
        foreach (var p in world.Platoons.All)
            if (p is not null && !p.Dead && p.SquadId == c.SquadId) return p;
        return null;
    }

    /// <summary>Finds the rival's character again after a load (handles change): by its name, nearest its last place.</summary>
    static void Resolve(World world, Rival r)
    {
        var table = world.Characters;
        if (table.TryResolveNext(r.Id, out int s) && table.Cold(s)?.Name == r.Name) return;
        CharacterId best = CharacterId.None;
        float bestD = float.MaxValue;
        for (int i = 0; i < table.HighWater; i++)
        {
            if (table.Cold(i) is not { } c || c.Name != r.Name) continue;
            var at = table.Next[i].Position;
            float d = (at.X - r.X) * (at.X - r.X) + (at.Z - r.Z) * (at.Z - r.Z);
            if (d < bestD) { bestD = d; best = table.IdOf(i); }
        }
        r.Id = best;
    }

    static bool IsRival(World world, Rival r, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int s) && world.Characters.Cold(s)?.Name == r.Name;

    static bool Alive(World world, Rival r, out CharacterCold? cold)
    {
        cold = null;
        var table = world.Characters;
        if (r.Id.IsNone || !table.TryResolveNext(r.Id, out int s)) return r.Id.IsNone;   // not loaded: assume alive (off screen)
        cold = table.Cold(s);
        return cold is { Medical.Dead: false, IsImprisoned: false } && table.Next[s].Alive;
    }

    static void Tell(World world, string post, string? history, int recap, Vector3? at = null)
    {
        world.Post(post);
        LwMod.Log("rivals: " + post);
        if (history is not null && LwDeeds.For(world) is { } deeds) deeds.Record.Note(history);
        if (recap > 0) RecapLink.Moment(history ?? post, Place(world, at), recap, at);   // with a place, Recap can take a picture of it
    }

    static string Place(World world, Vector3? at) =>
        at is { } p && LwDeeds.For(world)?.TownAt(p) is ({ } town, _) ? town : "the wastes";

    // ---- talk and the Atlas ----

    /// <summary>What a town speaker's rival condition sees: 0 no rival in that slot (or too far), 1 a rival about, 2 one that has beaten the squad.</summary>
    public int SlotValue(World world, int slot, CharacterId speaker)
    {
        var r = rivals.FirstOrDefault(x => x.Slot == slot && x.Active);
        if (r is null || !world.Characters.TryResolveNext(speaker, out int s)) return 0;
        var at = world.Characters.Next[s].Position;
        float dx = at.X - r.X, dz = at.Z - r.Z;
        if (!CampRules.InReach(MathF.Sqrt(dx * dx + dz * dz), (int)TalkKm)) return 0;
        return r.Wins > 0 ? 2 : 1;
    }

    public static List<string> Tokens = [];

    /// <summary>Puts each slot's rival name into its word swap (LWRIVAL_k), so the town lines name them.</summary>
    void Words(World world)
    {
        if (data is null) return;
        for (int k = 0; k < Tokens.Count; k++)
        {
            var r = rivals.FirstOrDefault(x => x.Slot == k && x.Active);
            WordSwap.Set(data, Tokens[k], r?.Name ?? "someone");
        }
    }

    /// <summary>The Atlas's rivals: (name, the list line, the page).</summary>
    public IEnumerable<(string Name, string Row, List<string> Page)> AtlasRows()
    {
        foreach (var r in rivals.Where(x => x.Told).OrderBy(x => x.Made))
        {
            string state = r.Phase switch
            {
                Phase.Hunting => "out looking for your squad", Phase.Recovering => "licking their wounds", Phase.Dead => "dead",
                Phase.Jailed => "in chains", _ => "not seen in a long while",
            };
            var page = new List<string>
            {
                $"-- {r.Name}, a rival --",
                $"{Once(r)} {char.ToUpperInvariant(r.Grudge[0])}{r.Grudge[1..]}{(r.By.Length > 0 ? $" ({r.By})" : "")}, day {Math.Floor(r.Made / 24):0}.",
                $"{char.ToUpperInvariant(state[0])}{state[1..]}.",
            };
            if (r.Returns > 0) page.Add(r.Returns == 1 ? "Has come back for your squad once and got away." : $"Has come back for your squad {r.Returns} times and got away each time.");
            if (r.Wins > 0) page.Add(r.Wins == 1 ? "Has beaten your squad once, and tells everyone." : $"Has beaten your squad {r.Wins} times, and tells everyone.");
            if (r.Active) page.Add("Stronger each time they come back.");
            yield return (r.Name, $"{r.Name} ({state})", page);
        }
    }

    /// <summary>"Once a Dust Bandit." for a nameless type, "Once Mora Vell, of the Dust Bandits." for someone with a name.</summary>
    static string Once(Rival r)
    {
        string a = "aeiouAEIOU".Contains(r.OldName[0]) ? "an" : "a";
        return r.Nameless ? $"Once {a} {r.OldName}." : $"Once {r.OldName}, of the {r.Faction}.";
    }

    public string Summary() => rivals.Count == 0 ? (made == 0 ? "no rival yet: the first won fight that leaves someone notable alive makes one" : "no rivals")
        : string.Join("; ", rivals.Select(r => !r.Told ? $"{r.Name}: not told yet (the squad must walk away)" : $"{r.Name}: {r.Phase.ToString().ToLowerInvariant()}{(r.Phase == Phase.Recovering ? $", back in {Math.Max(0, r.Next - (seen?.GameHours ?? 0)) / 24:0.0} days" : "")}"));

    // ---- save ----

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Format);
        w.Write(made);
        w.Write(lastMade);
        w.Write(rivals.Count);
        foreach (var r in rivals)
        {
            w.Write(r.Slot); w.Write(r.Name); w.Write(r.OldName); w.Write(r.Faction); w.Write(r.Grudge); w.Write(r.By);
            w.Write(r.Made); w.Write(r.Next); w.Write(r.Returns); w.Write(r.Wins); w.Write((int)r.Phase); w.Write(r.X); w.Write(r.Z);
            w.Write(r.Boosted); w.Write(r.PlatoonId); w.Write(r.Nameless); w.Write(r.Told);
        }
    }

    public void Read(Stream stream)
    {
        rivals.Clear();
        made = 0;
        lastMade = -1e9;
        using var rd = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int format = stream.Position >= stream.Length ? 0 : rd.ReadInt32();
        if (format is not (1 or 2 or 3)) return;
        made = rd.ReadInt32();
        lastMade = rd.ReadDouble();
        for (int n = rd.ReadInt32(); n > 0; n--)
        {
            rivals.Add(new Rival
            {
                Slot = rd.ReadInt32(), Name = rd.ReadString(), OldName = rd.ReadString(), Faction = rd.ReadString(), Grudge = rd.ReadString(), By = rd.ReadString(),
                Made = rd.ReadDouble(), Next = rd.ReadDouble(), Returns = rd.ReadInt32(), Wins = rd.ReadInt32(), Phase = (Phase)rd.ReadInt32(),
                X = rd.ReadSingle(), Z = rd.ReadSingle(), Boosted = rd.ReadInt32(), PlatoonId = rd.ReadInt32(),
            });
            rivals[^1].Nameless = format >= 2 ? rd.ReadBoolean() : true;
            rivals[^1].Told = format < 3 || rd.ReadBoolean();
        }
        seen = null;
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(made);
        h.Add(rivals.Count);
        foreach (var r in rivals) { h.Add(SquadRecord.StableHash(r.Name)); h.Add((int)r.Phase); h.Add(r.Returns); h.Add(r.Wins); }
    }
}

/// <summary>Sets a word swap's text at runtime (the line the game substitutes for /TOKEN/), as FallenName does for the fallen's name.</summary>
public static class WordSwap
{
    static readonly HashSet<string> broken = new(StringComparer.Ordinal);

    public static void Set(PopulationData data, string token, string text)
    {
        if (broken.Contains(token)) return;
        try
        {
            if (data.Dialogue.WordSwap(token) is not { } swap || swap.Children.Count == 0) throw new InvalidOperationException($"no {token} word swap");
            var line = swap.Children[0];
            var texts = line.GetType().GetProperty("Texts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException("DialogueNode", "Texts");
            texts.SetValue(line, (IReadOnlyList<string>)[text]);
        }
        catch (Exception e)
        {
            broken.Add(token);
            LwMod.Log($"word swap {token}: could not set it ({e.GetBaseException().Message})");
        }
    }
}
