using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

/// <summary>A named outlaw's verified second can send one party to the actual cage.</summary>
internal sealed class PrisonerRecovery(PopulationData data, PopulationSystem population, IReadOnlyList<CampCode> outlaws) : ITickSystem, IModSaveData, IStateHashed
{
    const string Prefix = "custody-recovery:";
    const double HoldHours = 24, JourneyHours = 72;
    internal enum Approach : byte { OpenCell, Siege, Covert }

    sealed record Trip(int Episode, string Captive, string Second, string Template, string Outpost, double Until,
        Approach Method = Approach.OpenCell)
    {
        internal RecoveryGate.Crossing[] Entry { get; init; } = [];
        internal bool Withdrawing { get; init; }
        internal int Muster { get; init; }
        internal Vector3? Rally { get; init; }
        internal string Cage { get; init; } = "";
        internal Vector3 CageAt { get; init; }
        internal bool Searching { get; init; }
        internal bool FollowUp { get; init; }
        internal string Claim => FormattableString.Invariant($"{Prefix}{Episode}") + (FollowUp ? ":lead" : "");
        internal string Tag => "lw:" + Claim;
    }

    readonly SortedDictionary<string, int> attempted = new(StringComparer.Ordinal);
    readonly PrisonerSiege siege = new();
    readonly RecoveryLeads leads = new();
    Trip? trip;
    sealed record Inspection(string Stage, Approach Method, string Captive, string Outpost, string Fixture, int RouteLength)
    {
        public override string ToString() => Captive.Length == 0 ? Stage :
            $"{Stage}; {Method}, {Captive}; outpost {Outpost}, {RouteLength} recorded passages" +   // one line: F10 shows a line break as ?
            (Fixture.Length == 0 ? "" : $"; fixture {Fixture}");
    }

    static Inspection inspection = new("no world yet", default, "", "", "", 0);
    World? inspectedWorld;

    internal static string Inspect() => LwMod.Settings.PrisonerRecovery ? Volatile.Read(ref inspection).ToString() : "off";

    static void Report(World world, string stage, Trip? active = null, string fixture = "")
    {
        var current = Volatile.Read(ref inspection);
        var method = active?.Method ?? default;
        string captive = active?.Captive ?? "";
        string outpost = active?.Outpost ?? "";
        int route = active?.Entry.Length ?? 0;
        if (PlaytestLog.Enabled)
            PlaytestLog.Change(world, "prisoner recovery", active?.Tag ?? "", stage,
                $"captive={captive} method={method} outpost={outpost} fixture={fixture} passages={route}");
        if (current.Stage == stage && current.Method == method && current.Captive == captive && current.Outpost == outpost &&
            current.Fixture == fixture && current.RouteLength == route) return;
        Volatile.Write(ref inspection, new(stage, method, captive, outpost, fixture, route));
    }

    static string LockStage(CovertRecovery.LockAttempt result) => result switch
    {
        CovertRecovery.LockAttempt.Picked => "lock opened",
        CovertRecovery.LockAttempt.Approaching => "approaching the lock",
        CovertRecovery.LockAttempt.BlockedApproach => "approach to lock refused",
        CovertRecovery.LockAttempt.NoWorkSpot => "no reachable work position for lock",
        CovertRecovery.LockAttempt.Unable => "scout reached lock but cannot pick it",
        _ => "working the lock",
    };

    internal static void Register(ModContext context, IReadOnlyList<CampCode> outlaws)
    {
        if (!Couriers.Available || !PrisonerHandover.Available) return;
        context.AddSystem("lw-recovery-clues", _ => new RecoveryClues(), SystemAnchor.End);
        context.AddSystem("lw-prisoner-recovery", ctx => ctx.Find<PopulationSystem>() is { } pop
            ? new PrisonerRecovery(ctx.Data, pop, outlaws) : null!, SystemAnchor.End);
        context.AddDebugCommand(UiLanguage.Text("Wasteland Remembers: prisoner recovery"), _ => Inspect(), hasField: false);
    }

    static bool Enabled => LwMod.Settings.PrisonerCases && LwMod.Settings.RememberOutposts && LwMod.Settings.PrisonerRecovery;

    public void SlowWorld(World world)
    {
        if (!Couriers.Available || !PrisonerHandover.Available) return;
        if (!ReferenceEquals(inspectedWorld, world))
        {
            inspectedWorld = world;
            Report(world, trip is null ? "no recovery underway" : "restoring the recovery party", trip);
        }
        if (trip is { Method: Approach.Covert } quiet)
        {
            if (world.Tick % CovertRecovery.Cadence == 7) Continue(world, quiet);
            return;
        }
        if (world.Tick % 150 != 127) return;
        if (trip is { } active)
        {
            Continue(world, active);
            return;
        }
        if (!Enabled || PrisonerCases.For(world) is not { } cases ||
            OutpostMemories.For(world) is not { } posts || world.Player.Faction < 0) return;

        foreach (var camp in outlaws)
        {
            if (PrisonerCases.IsLeader(camp.Character) || camp is not { Successor: { } second } ||
                world.Uniques.StateOf(camp.Character) != UniqueState.Imprisoned || !cases.TryHeld(world, camp.Character, out var captive) ||
                cases.RouteOf(camp.Character) != PrisonerCases.Route.Open || captive.Captive.Bounties?.Entries.Any(b => b.Claimed) == true ||
                world.GameHours - captive.Since < HoldHours) continue;

            int faction = captive.Captive.Faction;
            if (MethodFor(FactionName(faction), camp.Name) is not { } method || !Allowed(method) ||
                (method == Approach.Covert ? camp.Infiltrators : camp.Reprisal) is not { } template ||
                !Hostile(world, faction, method) ||
                !TryFactionTemplate(template, faction, out _) ||
                OutlawCampsSystem.For(world)?.ReprisalParties().Any(r => r.Outlaw == camp.Name) == true ||
                !Source(world, second, faction, out Vector2 source) ||
                posts.FoundedTowns(world).FirstOrDefault(p => p.Town.Number == captive.Site.Town) is not { Town: { } town } destination)
                continue;

            bool followUp = attempted.GetValueOrDefault(camp.Character) == captive.Episode;
            if (followUp && !leads.CanFollow(world, captive, destination.Key)) continue;

            // Use the second's position, well outside loaded player sight. The engine tests the spawn ring's ground and route.
            if (Pursuit.Players(world).Any(p => Vector2.DistanceSquared(new(p.X, p.Z), source) < 4500 * 4500)) continue;
            int origin = Pursuit.OriginFor(data, source.X, source.Y, faction);
            if (origin < 0) continue;
            var planned = new Trip(captive.Episode, camp.Character, second.Character, template, destination.Key, world.GameHours + JourneyHours, method)
            { Cage = captive.Cage.Id, CageAt = captive.Cage.Position, FollowUp = followUp };
            if (!RivalryLink.MaySend(world, planned.Claim)) continue;
            if (Couriers.Dispatch(world, population, faction, template, origin, source) is not { } party)
            {
                RivalryLink.Keep(world, Prefix, []);
                continue;
            }
            attempted[captive.Record] = captive.Episode;
            leads.Dispatched(captive.Record, captive.Episode, destination.Key, followUp);
            if (method == Approach.Covert && party.Size is not (1 or 2))
            {
                // The native spawn has only created its off-screen stand-in, so it is still safe to reject a changed roster.
                world.Platoons.Remove(party.Id);
                RivalryLink.Keep(world, Prefix, []);
                continue;
            }
            trip = planned with
            {
                Muster = party.Size,
                Rally = new Vector3(source.X, world.Walkability.GroundHeight(source.X, source.Y), source.Y),
            };
            Pursuit.Mark(party, planned.Tag);
            if (!Couriers.SendHeld(world, party, Couriers.Flat(captive.Cage.Position)))
            {
                End(world, party, "the road to the outpost was refused");
                return;
            }
            if (method != Approach.Covert) world.Post(method == Approach.Siege
                ? $"Word on the road: {second.Name} is marching on {town.Name} to take {captive.Name} back."
                : $"Word on the road: {second.Name} has sent people for {captive.Name}.");
            Report(world, "on the road", planned, captive.Cage.Id);
            LwMod.Log($"prisoner recovery: {second.Name} sent a party for {captive.Name} at {town.Name}");
            return;
        }
    }

    internal static Approach? MethodFor(string? faction, string outlaw) => (faction, outlaw) switch
    {
        ("Dust Bandits", _) => Approach.OpenCell,
        ("Kral's Chosen", "Maruka Tal-Khesh") => Approach.Siege,
        ("Sand ninjas", "Sayo of the Grey Ribbon") => Approach.Covert,
        _ => null,
    };

    string? FactionName(int faction) => faction >= 0 && faction < data.Factions.Count ? data.Factions[faction].Name : null;

    static bool Allowed(Approach method) => method switch
    {
        Approach.OpenCell => true,
        Approach.Siege => LwMod.Settings.ShekGateRecovery,
        Approach.Covert => LwMod.Settings.CovertRecovery && CovertRecovery.Available,
        _ => false,
    };

    bool Hostile(World world, int faction, Approach method)
    {
        var owner = method switch
        {
            Approach.OpenCell => "Dust Bandits",
            Approach.Siege => "Kral's Chosen",
            Approach.Covert => "Sand ninjas",
            _ => null,
        };
        return owner is not null && world.Player.Faction >= 0 && FactionName(faction) == owner &&
            data.Relations.Get(faction, world.Player.Faction) <= -30;
    }

    bool TryFactionTemplate(string record, int faction, out SquadTemplate? template)
    {
        template = data.Db.Find(record) is { } rec ? SquadTemplate.From(rec) : null;
        return template?.Faction is { } owner && data.FactionIndex(owner) == faction;
    }

    bool Source(World world, SuccessorCode second, int faction, out Vector2 source)
    {
        source = default;
        if (world.Uniques.Find(second.Character) is not { Claimed: true, State: UniqueState.Alive } issuer ||
            !TryFactionTemplate(second.Squad, faction, out var template) || template!.Leader?.Id != second.Character) return false;
        var table = world.Characters;
        if (table.TryResolveNext(issuer.Handle, out int slot))
        {
            if (table.Cold(slot) is not { IsPlayer: false, IsImprisoned: false, Medical: { Dead: false, Incapacitated: false } } captain ||
                captain.Faction != faction || !captain.CarriedBy.IsNone) return false;
            source = Couriers.Flat(table.Next[slot].Position);
            return true;
        }

        var camps = world.Platoons.All.Where(p => p is { Dead: false } && p.TemplateId == second.Squad && p.Faction == faction).Take(2).ToArray();
        if (camps.Length != 1) return false;
        source = camps[0].Position;
        return true;
    }

    void Continue(World world, Trip active)
    {
        var parties = world.Platoons.All.Where(p => p is not null && Pursuit.ReadTag(p) == active.Tag).Take(2).ToArray();
        if (parties.Length > 1)
        {
            End(world, null, "the party could not be identified");
            return;
        }
        var party = parties.SingleOrDefault();
        if (party is not null && party.TemplateId != active.Template) party = null;
        if (party is { State: PlatoonState.Loaded } && world.Squads.Find(party.SquadId) is { } loaded &&
            !OwnsSquad(party, loaded))
        {
            End(world, party, "the recovery squad could not be identified");
            return;
        }
        if (active.Withdrawing)
        {
            if (Enabled && Allowed(active.Method) && party is { Dead: false } && world.GameHours < active.Until &&
                ContinueRetreat(world, party, active))
            {
                if (!Volatile.Read(ref inspection).Stage.StartsWith("withdrawing", StringComparison.Ordinal))
                    Report(world, "withdrawing", active);
                return;
            }
            End(world, party, "the party withdrew");
            return;
        }
        if (!Enabled || party is null || party.Dead || world.GameHours >= active.Until ||
            !Allowed(active.Method) || !Hostile(world, party.Faction, active.Method) || !IssuerSurvives(world, active.Second, party.Faction) ||
            Pursuit.Beaten(world, party) || !RivalryLink.MaySend(world, active.Claim))
        {
            End(world, party, "the party withdrew or was beaten");
            return;
        }

        if (PrisonerCases.For(world) is not { } cases || OutpostMemories.For(world) is not { } posts) return;
        if (!cases.IsOpenEpisode(active.Captive, active.Episode) || cases.RouteOf(active.Captive) != PrisonerCases.Route.Open ||
            posts.FoundedTowns(world).FirstOrDefault(p => p.Key == active.Outpost).Town is not { } town)
        {
            LoseObjective(world, party, active, "the captive or outpost was lost");
            return;
        }
        bool hasHeld = cases.TryHeld(world, active.Captive, out var held);
        if (hasHeld && held.Captive.Bounties?.Entries.Any(b => b.Claimed) == true)
        {
            LoseObjective(world, party, active, "the captive was claimed");
            return;
        }

        if (hasHeld && active.Cage.Length == 0 && held.Site.Town == town.Number)
            trip = active = active with { Cage = held.Cage.Id, CageAt = held.Cage.Position };

        bool moved = hasHeld && (held.Site.Town != town.Number || held.Cage.Id != active.Cage);
        if (moved || active.Searching)
        {
            if (hasHeld && posts.FoundedTowns(world).FirstOrDefault(p => p.Town.Number == held.Site.Town) is { Town: not null } found)
                leads.CanFollow(world, held, found.Key);
            if (active.Cage.Length == 0)
            {
                LoseObjective(world, party, active, "the captive moved before the old cage could be identified");
                return;
            }
            if (!active.Searching)
            {
                trip = active = active with { Searching = true, Until = Math.Min(active.Until, world.GameHours + 24) };
                if (world.Squads.Find(party.SquadId) is { } searching && OwnsSquad(party, searching))
                    siege.Clear(world, searching);
            }
            SearchOldCell(world, active, party, town.Number);
            return;
        }
        if (!hasHeld)
        {
            if (active.Cage.Length > 0 && party.State != PlatoonState.Loaded &&
                !Couriers.SendHeld(world, party, Couriers.Flat(active.CageAt))) End(world, party, "the route was refused");
            else Report(world, "waiting for the exact captive and cage to load", active, active.Cage);
            return;
        }
        if (party.State != PlatoonState.Loaded || world.Squads.Find(party.SquadId) is not { } squad)
        {
            Report(world, "on the road", active, held.Cage.Id);
            if (!Couriers.SendHeld(world, party, Couriers.Flat(held.Cage.Position))) End(world, party, "the route was refused");
            return;
        }

        bool quiet = active.Method == Approach.Covert;
        bool assault = active.Method == Approach.Siege;
        if (assault && PrisonerSiege.HeavyLosses(world, squad, active.Muster > 0 ? active.Muster : party.Size))
        {
            LoseObjective(world, party, active, "the Shek withdrew after heavy losses");
            return;
        }
        LockSystem? locks = null;
        if (quiet)
        {
            locks = world.Systems.OfType<LockSystem>().FirstOrDefault();
            if (locks is null || squad.Members.Count is not (1 or 2) || CovertRecovery.Interrupted(world, squad))
            {
                if ((active.Entry.Length != 0 || active.Rally is not null) && locks is not null)
                {
                    trip = active with { Withdrawing = true, Until = world.GameHours + 3 };
                    Report(world, "withdrawing after a scout was detected or entered combat", active);
                    RivalryLink.Keep(world, Prefix, []);
                }
                else End(world, party, "the covert attempt was interrupted");
                return;
            }
            CovertRecovery.SetSneak(world, squad, true);
        }

        var table = world.Characters;
        bool defended = !quiet && Defended(world, held.Cage.Position);
        int picker = quiet ? CovertRecovery.Picker(world, squad) : -1;
        var bashers = assault ? PrisonerSiege.Bashers(world, squad) : null;
        foreach (var member in squad.Members)
        {
            if (!table.TryResolveNext(member, out int slot) || !table.Next[slot].Alive ||
                table.Cold(slot) is not { IsPlayer: false, InCombat: false, IsImprisoned: false, Animal: null,
                    Medical: { Dead: false, Incapacitated: false } } actor || actor.Faction != party.Faction ||
                actor.SquadId != party.SquadId || !actor.CarriedBy.IsNone || !actor.Carrying.IsNone) continue;

            var at = table.Next[slot].Position;
            if (quiet && slot != picker)
            {
                CovertRecovery.KeepWatch(world, slot, active.Entry);
                continue;
            }
            var entrance = active.Method == Approach.OpenCell ? null :
                RecoveryGate.Entrance(world, locks ?? world.Systems.OfType<LockSystem>().FirstOrDefault(),
                    held.Site.Town, at, held.Cage.Position);
            if (entrance is not null)
            {
                if (assault && RecoveryGate.Shut(entrance))
                {
                    if (bashers!.Contains(slot))
                    {
                        bool bashing = RecoveryGate.Bash(world, slot, entrance);
                        Report(world, bashing ? "bashing the gate" : "native bash order refused", active, entrance.Id);
                        if (bashing) RecoveryClues.Notice(world, active.Episode, entrance, RecoveryClues.Kind.BrokenGate);
                    }
                    else siege.Cover(world, slot);
                }
                else if (quiet && entrance.Lock is { EffectivelyLocked: true } && RecoveryGate.Shut(entrance))
                {
                    var result = CovertRecovery.PickDoor(world, locks!, slot, entrance);
                    if (result == CovertRecovery.LockAttempt.Picked)
                    {
                        RecoveryClues.Notice(world, active.Episode, entrance, RecoveryClues.Kind.OpenLock);
                        active = RememberEntrance(world, active, entrance, at);
                    }
                    Report(world, LockStage(result), active, entrance.Id);
                }
                else if (RecoveryGate.Passage(world, entrance, at) is { } across)
                {
                    if (quiet) active = RememberEntrance(world, active, entrance, at);
                    PrisonerHandover.WalkTo(world, slot, across.Far);
                    Report(world, "crossing the entrance", active, entrance.Id);
                }
                else Report(world, "no walkable crossing at this entrance", active, entrance.Id);
                continue;
            }
            if (quiet && RecoveryGate.Blocking(world, held.Site.Town, at,
                    held.Cage.Position, lockedOnly: true, gatesOnly: false, interiorOnly: true) is { } barred)
            {
                var result = CovertRecovery.PickDoor(world, locks!, slot, barred);
                if (result == CovertRecovery.LockAttempt.Picked)
                {
                    RecoveryClues.Notice(world, active.Episode, barred, RecoveryClues.Kind.OpenLock);
                    active = RememberEntrance(world, active, barred, at);
                }
                Report(world, LockStage(result), active, barred.Id);
                continue;
            }
            if (assault && defended && !bashers!.Contains(slot) && siege.Cover(world, slot)) continue;
            if (!PrisonerHandover.AtExit(world, slot, held.Cage))
            {
                var result = CovertRecovery.Approach(world, slot, PrisonerHandover.Exit(held.Cage));
                Report(world, result == CovertRecovery.LockAttempt.BlockedApproach ? "approach to cage refused" : "approaching cage", active, held.Cage.Id);
                continue;
            }
            if (held.Cage.Lock is { EffectivelyLocked: true })
            {
                if (!quiet)
                {
                    Report(world, "cage is locked", active, held.Cage.Id);
                    continue;
                }
                var result = CovertRecovery.PickCage(world, locks!, slot, held.Cage);
                Report(world, LockStage(result), active, held.Cage.Id);
                if (result != CovertRecovery.LockAttempt.Picked) continue;
            }
            if (defended || !cases.Recover(world, held, quiet))
            {
                Report(world, defended ? "standing defenders near the cage" : "native custody release refused", active, held.Cage.Id);
                continue;
            }
            RecoveryClues.Notice(world, active.Episode, held.Cage, RecoveryClues.Kind.EmptyCage);
            if (!quiet) world.Post($"{held.Name}'s gang got them out of the cage.");
            if (!quiet || active.Entry.Length == 0) ClearObjectives(world, active, squad);
            var second = outlaws.FirstOrDefault(c => c.Character == active.Captive)?.Successor;
            bool escorted = second is not null && Source(world, second, party.Faction, out var home) &&
                PrisonerEscorts.Begin(world, party, held, data.Factions[party.Faction].Id,
                    new Vector3(home.X, world.Walkability.GroundHeight(home.X, home.Y), home.Y), issuer: active.Second, entry: active.Entry, covert: quiet);
            if (quiet && !escorted) ClearObjectives(world, active, squad);
            End(world, escorted ? null : party, "the gang freed the captive");
            return;
        }
    }

    void SearchOldCell(World world, Trip active, Platoon party, int town)
    {
        Report(world, "searching the original cage", active, active.Cage);
        if (party.State != PlatoonState.Loaded)
        {
            if (!Couriers.SendHeld(world, party, Couriers.Flat(active.CageAt))) End(world, party, "the route to the original cage was refused");
            return;
        }
        if (world.Squads.Find(party.SquadId) is not { } squad || !OwnsSquad(party, squad)) return;

        if (active.Method == Approach.Covert && CovertRecovery.Interrupted(world, squad))
        {
            BeginRetreat(world, active, "withdrawing after discovery at the old outpost");
            return;
        }
        if (active.Method == Approach.Covert) CovertRecovery.SetSneak(world, squad, true);
        var table = world.Characters;
        foreach (var member in squad.Members)
        {
            if (!PrisonerSiege.Fit(world, squad, member, out int slot) ||
                table.Cold(slot) is not { InCombat: false, Animal: null }) continue;
            var at = table.Next[slot].Position;
            var walk = CovertRecovery.Approach(world, slot, active.CageAt);
            bool reached = Vector3.DistanceSquared(at, active.CageAt) <= 130 * 130 &&
                PrisonerHandover.ClearSight(world, at, active.CageAt);
            bool blocked = walk == CovertRecovery.LockAttempt.BlockedApproach &&
                Vector3.DistanceSquared(at, active.CageAt) <= 500 * 500 &&
                world.Buildings.Find(world.Fixtures.Find(active.Cage)?.Building ?? "")?.Town == town;
            if (!reached && !blocked) continue;
            BeginRetreat(world, active, reached ? "withdrawing after searching the old cage" : "withdrawing at the blocked old approach");
            return;
        }
    }

    void BeginRetreat(World world, Trip active, string reason)
    {
        trip = active with { Withdrawing = true, Until = world.GameHours + 12 };
        RivalryLink.Keep(world, Prefix, []);
        Report(world, reason, active, active.Cage);
    }

    Trip RememberEntrance(World world, Trip active, Fixture door, Vector3 from)
    {
        if (active.Entry.Length >= 32 || active.Entry.Any(c => c.Door == door.Id) ||
            RecoveryGate.Passage(world, door, from) is not { } crossing) return active;
        trip = active = active with { Entry = [.. active.Entry, crossing] };
        return active;
    }

    static bool IssuerSurvives(World world, string record, int faction)
    {
        if (world.Uniques.Find(record) is not { Claimed: true, State: UniqueState.Alive } entry) return false;
        return !world.Characters.TryResolveNext(entry.Handle, out int slot) || world.Characters.Cold(slot) is
            { IsPlayer: false, IsImprisoned: false, Medical.Dead: false, Faction: var owner } && owner == faction;
    }

    static bool Defended(World world, Vector3 at)
    {
        var table = world.Characters;
        for (int i = 0; i < table.HighWater; i++)
            if (table.Next[i].Alive && !table.Next[i].Dormant &&
                table.Cold(i) is { IsPlayer: true, IsImprisoned: false, Medical: { Dead: false, Incapacitated: false } } actor &&
                actor.CarriedBy.IsNone && Vector3.DistanceSquared(table.Next[i].Position, at) <= 250 * 250)
                return true;
        return false;
    }

    bool ContinueRetreat(World world, Platoon party, Trip active)
    {
        if (party.State != PlatoonState.Loaded)
            return active.Rally is { } home &&
                Vector2.DistanceSquared(party.Position, Couriers.Flat(home)) > 300 * 300 &&
                Couriers.SendHeld(world, party, Couriers.Flat(home));
        if (world.Squads.Find(party.SquadId) is not { } squad || !OwnsSquad(party, squad)) return false;
        if (active.Method == Approach.Covert && CovertRecovery.Withdraw(world, squad, active.Entry)) return true;
        return active.Rally is { } rally && PrisonerSiege.FallBack(world, squad, rally);
    }

    void LoseObjective(World world, Platoon party, Trip active, string why)
    {
        if (active.Method != Approach.Siege || active.Rally is null)
        {
            End(world, party, why);
            return;
        }
        if (world.Squads.Find(party.SquadId) is { } squad && OwnsSquad(party, squad))
            siege.Clear(world, squad);
        trip = active with { Withdrawing = true, Until = world.GameHours + 12 };
        RivalryLink.Keep(world, Prefix, []);
        LwMod.Log("prisoner recovery: " + why);
    }

    void ClearObjectives(World world, Trip active, Squad squad)
    {
        if (active.Method == Approach.Siege) siege.Clear(world, squad);
        if (active.Method == Approach.Covert) CovertRecovery.SetSneak(world, squad, false);
    }

    static bool OwnsSquad(Platoon party, Squad squad) =>
        squad.PlatoonId == party.Id && squad.TemplateId == party.TemplateId && squad.Faction == party.Faction;

    void End(World world, Platoon? party, string why)
    {
        if (party is not null)
        {
            var squad = world.Squads.Find(party.SquadId);
            bool ownsSquad = squad is not null && OwnsSquad(party, squad);
            if (trip is { } active && ownsSquad)
                ClearObjectives(world, active, squad!);
            Couriers.Release(world, party, clearTraderVisit: ownsSquad);
        }
        siege.Forget();
        Report(world, why, trip);
        trip = null;
        RivalryLink.Keep(world, Prefix, []);
        LwMod.Log("prisoner recovery: " + why);
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(5);
        w.Write(attempted.Count);
        foreach (var (key, attempt) in attempted) { w.Write(key); w.Write(attempt); }
        leads.Write(w);
        w.Write(trip is not null);
        if (trip is not { } active) return;
        w.Write(active.Episode);
        w.Write(active.Captive);
        w.Write(active.Second);
        w.Write(active.Template);
        w.Write(active.Outpost);
        w.Write(active.Until);
        w.Write((byte)active.Method);
        w.Write(active.Withdrawing);
        RecoveryGate.Crossing.Write(w, active.Entry);
        w.Write(active.Muster);
        w.Write(active.Rally is not null);
        if (active.Rally is { } rally) { w.Write(rally.X); w.Write(rally.Y); w.Write(rally.Z); }
        w.Write(active.Cage);
        w.Write(active.CageAt.X); w.Write(active.CageAt.Y); w.Write(active.CageAt.Z);
        w.Write(active.Searching); w.Write(active.FollowUp);
    }

    public void Read(Stream stream)
    {
        attempted.Clear();
        trip = null;
        leads.Read(null);
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Position == stream.Length) return;
        int version = r.ReadInt32();
        if (version is < 1 or > 5) return;
        for (int n = r.ReadInt32(); n > 0; n--) attempted[r.ReadString()] = r.ReadInt32();
        if (version >= 5) leads.Read(r);
        if (r.ReadBoolean()) trip = new(r.ReadInt32(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadDouble(),
            version >= 2 ? (Approach)r.ReadByte() : Approach.OpenCell);
        if (trip is { } active && version >= 3)
            trip = active with { Withdrawing = r.ReadBoolean(), Entry = RecoveryGate.Crossing.Read(r) };
        if (trip is { } ready && version >= 4)
            trip = ready with { Muster = r.ReadInt32(), Rally = r.ReadBoolean() ? new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) : null };
        if (trip is { } dispatched && version >= 5)
            trip = dispatched with { Cage = r.ReadString(), CageAt = new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Searching = r.ReadBoolean(), FollowUp = r.ReadBoolean() };
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(attempted.Count);
        foreach (var (key, attempt) in attempted) { h.Add(SquadRecord.StableHash(key)); h.Add(attempt); }
        leads.Hash(ref h);
        h.Add(trip is null ? 0 : 1);
        if (trip is not { } active) return;
        h.Add(active.Episode);
        h.Add(SquadRecord.StableHash(active.Captive));
        h.Add(SquadRecord.StableHash(active.Second));
        h.Add(SquadRecord.StableHash(active.Template));
        h.Add(SquadRecord.StableHash(active.Outpost));
        h.Add(active.Until);
        h.Add((int)active.Method);
        h.Add(active.Withdrawing ? 1 : 0);
        RecoveryGate.Crossing.Hash(ref h, active.Entry);
        h.Add(active.Muster);
        h.Add(active.Rally is null ? 0 : 1);
        if (active.Rally is { } rally) { h.Add(rally.X); h.Add(rally.Y); h.Add(rally.Z); }
        h.Add(SquadRecord.StableHash(active.Cage));
        h.Add(active.CageAt.X); h.Add(active.CageAt.Y); h.Add(active.CageAt.Z);
        h.Add(active.Searching ? 1 : 0); h.Add(active.FollowUp ? 1 : 0);
    }
}
