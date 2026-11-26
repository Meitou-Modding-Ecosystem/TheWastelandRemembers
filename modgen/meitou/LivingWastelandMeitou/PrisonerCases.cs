using System.Numerics;
using System.Runtime.CompilerServices;
using System.Reflection;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

/// <summary>Notable captives verified in player-owned cages, with public news anchored at a nearby town.</summary>
public sealed partial class PrisonerCases(PopulationData? data, IReadOnlyList<CampCode> outlaws) : ICombatHitObserver, IModSaveData, IStateHashed
{
    const int Format = 3, Foreground = 3;
    const double OrdinaryWait = 6;

    // Vanilla NPC ids used by the Workshop Atlas and boss world states. Duplicate display names are not evidence.
    internal const string Esata = "43159-Dialogue.mod";
    internal const string WesternQueen = "96351-Dialogue.mod";
    static readonly HashSet<string> leaders = new(StringComparer.Ordinal)
    {
        Esata, "17225-Dialogue.mod", "51751-Dialogue.mod", WesternQueen, "57334-Newwworld.mod",
    };

    enum End : byte { Open, Left, Died, Executed, Ransomed, Recovered, CovertRecovered }

    sealed record Case(int Episode, string Record, string Name, string Faction, string Cage, string Node, string Place,
        double Since, string Warrant, bool Told = false, End Outcome = End.Open, Route Deal = Route.Open, string Claimant = "");

    static readonly ConditionalWeakTable<World, PrisonerCases> systems = new();
    internal static PrisonerCases? For(World world) => systems.TryGetValue(world, out var system) ? system : null;
    static string summary = "no world yet";
    static readonly HashSet<string> lawful = new(StringComparer.Ordinal)
        { "Shek Kingdom", "The Holy Nation", "United Cities", "Flotsam Ninjas" };

    static readonly Type? deathObserver = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.ICharacterDeathObserver");
    static readonly Type? deathPayload = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.CharacterDeath");
    static readonly PropertyInfo? deathSite = deathPayload?.GetProperty("Site");
    static readonly PropertyInfo? deathKiller = deathPayload?.GetProperty("Killer");
    static readonly PropertyInfo? deathVictim = deathPayload?.GetProperty("Victim");
    static readonly object? execution = deathSite?.PropertyType is { IsEnum: true } t &&
        Enum.TryParse(t, "Execution", out var value) ? value : null;

    readonly HashSet<string> named = outlaws.SelectMany(o => new[] { o.Character, o.Successor?.Character })
        .OfType<string>().ToHashSet(StringComparer.Ordinal);
    readonly SortedDictionary<string, Case> cases = new(StringComparer.Ordinal);
    List<Case> foreground = [];
    int nextEpisode = 1;

    internal static void Register(ModContext context, IReadOnlyList<CampCode> outlaws)
    {
        context.AddSystem("lw-prisoner-cases", ctx => new PrisonerCases(ctx.Data, outlaws), SystemAnchor.End);
        if (BuildDeathWatch() is { } watcher)
            context.AddSystem("lw-prisoner-deaths", _ => (ITickSystem)Activator.CreateInstance(watcher)!, SystemAnchor.End);
        context.AddDebugCommand(UiLanguage.Text("Wasteland Remembers: prisoner cases"),
            _ => LwMod.Settings.PrisonerCases ? Volatile.Read(ref summary) : "prisoner cases are off", hasField: false);
    }

    internal static Type? BuildDeathWatch() => deathObserver is not null && execution is not null &&
        deathKiller?.PropertyType == typeof(CharacterId) && deathVictim?.PropertyType == typeof(CharacterId)
        ? ObserverEmit.Build(deathObserver, "Died", typeof(PrisonerCases).GetMethod(nameof(DeathObserved))!, "PrisonerDeathWatch")
        : null;

    public static void DeathObserved(World world, object death)
    {
        if (!LwMod.Settings.PrisonerCases || execution?.Equals(deathSite!.GetValue(death)) != true ||
            deathKiller?.GetValue(death) is not CharacterId killer || deathVictim?.GetValue(death) is not CharacterId victim) return;
        (For(world) ?? world.Systems.OfType<PrisonerCases>().FirstOrDefault())?.Executed(world, killer, victim);
    }

    internal static bool IsLeader(string record) => leaders.Contains(record);

    static BuildSite? PlayerCage(World world, CharacterId id, CharacterCold captive, Fixture cage) =>
        world.Player.Faction >= 0 && captive.IsImprisoned && captive.InWhat == cage.Id && captive.CarriedBy.IsNone &&
        cage.Kind == FixtureKind.Cage && cage.Occupant == id && cage.Owner == world.Player.Faction &&
        world.Buildings.Find(cage.Building) is { Complete: true, Destroyed: false } site && site.Owner == world.Player.Faction
        ? site : null;

    string Warrant(CharacterCold captive, LwDeeds deeds) => captive.Bounties?.Entries
        .Where(b => !b.Claimed && b.Amount > 0)
        .OrderByDescending(b => b.Amount).ThenBy(b => b.Faction)
        .Select(b => deeds.FactionName(b.Faction)).FirstOrDefault(lawful.Contains) ?? "";

    Case? Observe(World world, LwDeeds deeds, CharacterId id, CharacterCold captive, Fixture cage)
    {
        if (captive.IsPlayer || captive.Animal is not null || world.Uniques.RecordOf(id) is not { } record ||
            PlayerCage(world, id, captive, cage) is not { } site) return null;

        string warrant = Warrant(captive, deeds);
        if (cases.TryGetValue(record, out var existing))
            return existing with { Cage = cage.Id, Warrant = warrant };
        if (!IsLeader(record) && !named.Contains(record) && warrant.Length == 0) return null;

        var anchor = data?.Sites.Where(town => NewsSystem.NodeOf(town).Length > 0)
            .MinBy(town => Vector2.DistanceSquared(new(site.Position.X, site.Position.Z), new(town.Position.X, town.Position.Z)));
        // the place is the player's own outpost where the cage stands; the node (the nearest town on the news roads) only carries the news
        // (play test of bf: "News from Rebel Base" for a Queen caged at the squad's outpost)
        string place = world.Buildings.FindTown(site.Town)?.Name is { Length: > 0 } own ? own : anchor?.Town.Name ?? "the wastes";
        return new Case(nextEpisode++, record, data?.Db.Find(record)?.Name ?? captive.Name, deeds.FactionName(captive.Faction),
            cage.Id, anchor is null ? "" : NewsSystem.NodeOf(anchor), place, world.GameHours, warrant);
    }

    World? registeredWorld;

    public void SlowWorld(World world)
    {
        StartDealWorld(world);
        if (world.Tick % 150 != 115) return;
        bool casesOn = LwMod.Settings.PrisonerCases, leadersOn = LwMod.Settings.FallenLeaderStories;
        if (!casesOn && !leadersOn || LwDeeds.For(world) is not { } deeds) return;
        if (casesOn && !ReferenceEquals(registeredWorld, world))
        {
            systems.AddOrUpdate(world, this);
            registeredWorld = world;
        }
        var held = new HashSet<string>(StringComparer.Ordinal);
        var table = world.Characters;
        foreach (var cage in world.Fixtures.All)
        {
            if (cage.Kind != FixtureKind.Cage || cage.Owner != world.Player.Faction || cage.Occupant.IsNone ||
                !table.TryResolveNext(cage.Occupant, out int slot) || table.Cold(slot) is not { Medical: { Dead: false } } captive) continue;
            if (leadersOn && !captive.IsPlayer && captive.Animal is null &&
                world.Uniques.RecordOf(cage.Occupant) is { } leader && LeaderStories.Tracks(leader) &&
                PlayerCage(world, cage.Occupant, captive, cage) is { } site)
                LeaderStories.Confirm(world, deeds, leader, captured: true, site.Position);
            if (!casesOn || Observe(world, deeds, cage.Occupant, captive, cage) is not { } found) continue;
            cases[found.Record] = found;
            held.Add(found.Record);
        }

        if (!casesOn) return;
        ConfirmNativeClaims(world, deeds);
        foreach (var entry in cases.Values.ToArray())
        {
            if (entry.Outcome == End.Open && held.Contains(entry.Record)) continue;
            var outcome = entry.Outcome == End.Open ? Recheck(world, entry) : entry.Outcome;
            if (outcome == End.Open) continue;
            Finish(world, deeds, entry, outcome);
            cases.Remove(entry.Record);
            held.Remove(entry.Record);
        }

        foreground = held.Select(record => cases[record])
            .Where(c => IsLeader(c.Record) || world.GameHours - c.Since >= OrdinaryWait)
            .OrderByDescending(c => IsLeader(c.Record)).ThenByDescending(c => c.Warrant.Length > 0)
            .ThenBy(c => c.Since).ThenBy(c => c.Record, StringComparer.Ordinal).Take(Foreground).ToList();
        for (int i = 0; i < foreground.Count; i++)
        {
            var c = foreground[i];
            if (c.Told) continue;
            Announce(world, deeds, c);
            foreground[i] = cases[c.Record] = c with { Told = true };
        }

        PublishLawful(world);
        Volatile.Write(ref summary, $"{foreground.Count} foreground, {cases.Count} recorded"
            + string.Concat(foreground.Select(c => $"; {c.Name}{(c.Warrant.Length > 0 ? $" (warrant from {c.Warrant})" : "")}")));
    }

    static End Recheck(World world, Case c)
    {
        if (world.Uniques.Find(c.Record) is not { } unique) return End.Open;
        if (unique.State == UniqueState.Dead) return End.Died;
        var table = world.Characters;
        if (!table.TryResolveNext(unique.Handle, out int slot) || table.Cold(slot) is not { } actor) return End.Open;
        if (actor.Medical is { Dead: true }) return End.Died;
        if (actor.IsImprisoned && (actor.InWhat is not { } cage || world.Fixtures.Find(cage) is null)) return End.Open;

        // Carrying between cells does not end the case. Unloaded actors are never announced as released.
        if (table.TryResolveNext(actor.CarriedBy, out int keeper) && table.Cold(keeper) is { IsPlayer: true }) return End.Open;
        return End.Left;
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        if (LwMod.Settings.PrisonerCases && hit.Source == CombatHitSource.Execution && hit.VictimWasAlive)
            Executed(world, hit.Attacker, hit.Victim);
    }

    void Executed(World world, CharacterId killer, CharacterId victim)
    {
        if (LwDeeds.For(world) is not { } deeds) return;
        var table = world.Characters;
        if (!table.TryResolveNext(killer, out int a) || table.Cold(a) is not { IsPlayer: true } ||
            !table.TryResolveNext(victim, out int v) || table.Cold(v) is not { Medical: { Dead: true }, InWhat: { } id } captive ||
            world.Fixtures.Find(id) is not { } cage || Observe(world, deeds, victim, captive, cage) is not { } witnessed) return;
        // The hit observer records evidence only. Announcements happen in the serial pass and survive a save in between.
        cases[witnessed.Record] = witnessed with { Outcome = End.Executed };
    }

    static void Announce(World world, LwDeeds deeds, Case c)
    {
        bool leader = IsLeader(c.Record);
        string line = $"The squad took {c.Name} alive.";
        string rumour = c.Record switch
        {
            Esata => "The Stone Golem's been taken alive. The Kingdom will feel that.",
            "17225-Dialogue.mod" => "The Phoenix was taken alive. What will the faithful do?",
            "51751-Dialogue.mod" => "Tengu's been taken alive. Word will reach the nobles.",
            WesternQueen or "57334-Newwworld.mod" => $"{c.Name} was taken alive. What will the hive do?",
            _ => $"{c.Name} has been taken alive.",
        };
        Record(world, deeds, c, line, leader ? 95 : 50);
        NewsSystem.For(world)?.ReportPrisoner(c.Node, c.Place, c.Episode, "held", rumour, leader ? 80 : 50);
        if (c.Warrant.Length > 0) world.Post($"There's still a warrant for {c.Name} from {KeepsakeText.Faction(c.Warrant)}.");
    }

    static void Finish(World world, LwDeeds deeds, Case c, End outcome)
    {
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "prisoner case", $"custody:{c.Episode}", outcome.ToString(),
                $"{c.Name} / {c.Record} at {c.Node}, cage {c.Cage}");
        if (outcome == End.CovertRecovered || (!c.Told && outcome != End.Executed)) return;
        string line = CustodyLine(c, outcome);
        Record(world, deeds, c, line, outcome is End.Executed or End.Ransomed or End.Recovered ? 80 : 45,
            offerRecap: outcome is not (End.Ransomed or End.Recovered));
        if (outcome == End.Executed)
            NewsSystem.For(world)?.ReportPrisoner(c.Node, c.Place, c.Episode, "executed", $"{c.Name} was executed after being taken alive.", 75);
    }

    static string CustodyLine(Case c, End outcome) => outcome switch
    {
        End.Executed => $"The squad executed {c.Name} in custody.",
        End.Died => $"The custody case closed when {c.Name} died.",
        End.Ransomed => $"The Western Hive paid a ransom and the squad handed over {c.Name}.",
        End.Recovered => $"{c.Name}'s gang freed them from the squad's cage.",
        _ => $"{c.Name} is no longer in the squad's custody.",
    };

    static void Record(World world, LwDeeds deeds, Case c, string line, int weight, bool offerRecap = true)
    {
        deeds.Record.Note(line, world.GameHours, c.Place);
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "prisoner case", $"custody:{c.Episode}", "recorded", line);
        if (weight >= 70)
        {
            world.Post(line);
            if (offerRecap)
            {
                bool accepted = RecapLink.Moment(line, c.Place, weight);
                if (PlaytestLog.Enabled)
                    PlaytestLog.Event(world, "Recap", $"custody:{c.Episode}", accepted ? "accepted moment" : "moment not accepted", line);
            }
        }
        LwMod.Log($"prisoner cases: {line}");
    }

    internal IEnumerable<(string Key, string Name, string Page)> AtlasRows() => LwMod.Settings.PrisonerCases
        ? foreground.Select(c => (c.Record, c.Name, $"{c.Name}\n{c.Faction}{(IsLeader(c.Record) ? " ruler" : "")}\nHeld since day {(int)(c.Since / 24) + 1}."
            + (c.Warrant.Length > 0 ? $"\nWarrant from {KeepsakeText.Faction(c.Warrant)}." : "") + DealNote(c)))
        : [];

    internal int QueenEpisode => cases.TryGetValue(WesternQueen, out var queen) && queen.Outcome == End.Open ? queen.Episode : 0;

    internal bool QueenAt(World world, out int episode, out int town)
    {
        episode = town = 0;
        if (!TryHeld(world, WesternQueen, out var held) || held.Site.Town <= 0) return false;
        episode = held.Episode;
        town = held.Site.Town;
        return true;
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        Write(w);
    }

    void Write(BinaryWriter w)
    {
        w.Write(Format);
        w.Write(nextEpisode);
        w.Write(cases.Count);
        foreach (var c in cases.Values)
        {
            w.Write(c.Episode);
            w.Write(c.Record);
            w.Write(c.Name);
            w.Write(c.Faction);
            w.Write(c.Cage);
            w.Write(c.Node);
            w.Write(c.Place);
            w.Write(c.Since);
            w.Write(c.Warrant);
            w.Write(c.Told);
            w.Write((byte)c.Outcome);
            w.Write((byte)c.Deal);
            w.Write(c.Claimant);
        }
        w.Write(settledPayments.Count);
        foreach (string record in settledPayments) w.Write(record);
    }

    public void Read(Stream stream)
    {
        cases.Clear();
        settledPayments.Clear();
        foreground.Clear();
        nextEpisode = 1;
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        dealWorld = null;
        if (stream.Position == stream.Length) return;
        int format = r.ReadInt32();
        if (format is < 1 or > Format) return;
        nextEpisode = r.ReadInt32();
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var c = new Case(r.ReadInt32(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString(),
                r.ReadDouble(), r.ReadString(), r.ReadBoolean(), (End)r.ReadByte());
            if (format >= 2) c = c with { Deal = (Route)r.ReadByte(), Claimant = r.ReadString() };
            cases[c.Record] = c;
        }
        if (format >= 3)
            for (int n = r.ReadInt32(); n > 0; n--) settledPayments.Add(r.ReadString());
    }

    public void Hash(ref StateHasher hash)
    {
        hash.Add(nextEpisode);
        hash.Add(settledPayments.Count);
        foreach (string record in settledPayments) hash.Add(SquadRecord.StableHash(record));
        hash.Add(cases.Count);
        foreach (var c in cases.Values)
        {
            hash.Add(c.Episode);
            hash.Add(SquadRecord.StableHash(c.Record));
            hash.Add(SquadRecord.StableHash(c.Name));
            hash.Add(SquadRecord.StableHash(c.Faction));
            hash.Add(SquadRecord.StableHash(c.Cage));
            hash.Add(SquadRecord.StableHash(c.Node));
            hash.Add(SquadRecord.StableHash(c.Place));
            hash.Add(c.Since);
            hash.Add(SquadRecord.StableHash(c.Warrant));
            hash.Add(c.Told ? 1 : 0);
            hash.Add((int)c.Outcome);
            hash.Add((int)c.Deal);
            hash.Add(SquadRecord.StableHash(c.Claimant));
        }
    }
}
