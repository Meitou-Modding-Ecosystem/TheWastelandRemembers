using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;

namespace LivingWasteland.MeitouMod;

public sealed partial class PrisonerCases
{
    internal enum Route : byte { Open, Lawful, Ransom, LawfulPaid }
    internal readonly record struct Held(int Episode, string Record, string Name, string Place, double Since,
        CharacterId Actor, CharacterCold Captive, Fixture Cage, BuildSite Site);
    object dealSession = new();
    World? dealWorld;
    readonly SortedSet<string> settledPayments = new(StringComparer.Ordinal);

    internal bool TryHeld(World world, string record, out Held held)
    {
        held = default;
        if (cases.GetValueOrDefault(record) is not { Outcome: End.Open } c) return false;
        if (world.Uniques.Find(record) is { } unique && unique.State != Meitou.Simulation.Factions.UniqueState.Dead &&
            world.Characters.TryResolveNext(unique.Handle, out int slot) &&
            world.Characters.Cold(slot) is { Medical.Dead: false, IsPlayer: false, InWhat: { } id } captive &&
            world.Fixtures.Find(id) is { } cage && PlayerCage(world, unique.Handle, captive, cage) is { } site)
        {
            held = new Held(c.Episode, record, c.Name, c.Place, c.Since, unique.Handle, captive, cage, site);
            return true;
        }
        // whoever of this record is in one of the squad's cages, as the case found them (the registry's handle can be another of them:
        // play test of bf, a debug-spawned Queen in the cage while the registry pointed at the one on her throne)
        foreach (var other in world.Fixtures.All)
        {
            if (other.Kind != FixtureKind.Cage || other.Owner != world.Player.Faction || other.Occupant.IsNone ||
                world.Uniques.RecordOf(other.Occupant) != record || !world.Characters.TryResolveNext(other.Occupant, out int s) ||
                world.Characters.Cold(s) is not { Medical.Dead: false, IsPlayer: false } caged ||
                PlayerCage(world, other.Occupant, caged, other) is not { } at) continue;
            held = new Held(c.Episode, record, c.Name, c.Place, c.Since, other.Occupant, caged, other, at);
            return true;
        }
        return false;
    }

    internal static bool PlayerNear(World world, IEnumerable<CharacterId> people, Vector3 at, float range) =>
        people.Any(id => world.Characters.TryResolveNext(id, out int s) && world.Characters.Next[s].Alive &&
            world.Characters.Cold(s) is { IsPlayer: true, InCombat: false, Animal: null, IsImprisoned: false,
                Medical: { Dead: false, Incapacitated: false } } c && c.CarriedBy.IsNone &&
            Vector3.DistanceSquared(world.Characters.Next[s].Position, at) <= range * range);

    internal Route RouteOf(string record) => cases.GetValueOrDefault(record)?.Deal ?? Route.Open;

    internal bool CanRansom(string record, int episode) => record == WesternQueen && !settledPayments.Contains(record) &&
        cases.GetValueOrDefault(record) is { Outcome: End.Open, Deal: Route.Open or Route.Ransom } c && c.Episode == episode;

    internal bool ReserveRansom(Held held)
    {
        if (!CanRansom(held.Record, held.Episode) || cases[held.Record] is not { Deal: Route.Open } c ||
            held.Captive.Bounties?.Entries.Any(b => b.Claimed) == true) return false;
        cases[held.Record] = c with { Deal = Route.Ransom };
        return true;
    }

    internal void CancelRansom(int episode)
    {
        if (cases.GetValueOrDefault(WesternQueen) is { Deal: Route.Ransom, Outcome: End.Open } c && c.Episode == episode)
            cases[WesternQueen] = c with { Deal = Route.Open };
    }

    internal bool PayRansom(World world, Held expected, Squad purse, int amount)
    {
        if (!CanRansom(expected.Record, expected.Episode) || cases[expected.Record] is not { Deal: Route.Ransom } c ||
            !TryHeld(world, expected.Record, out var held) || held.Actor != expected.Actor || held.Cage != expected.Cage ||
            held.Captive.Medical is not { Dead: false, Incapacitated: false } || held.Captive.SlaveState != 0 ||
            held.Captive.Bounties?.Entries.Any(b => b.Claimed) == true ||
            amount <= 0 || purse.Money < amount || world.Player.Money > long.MaxValue - amount ||
            !PrisonerHandover.Free(world, held)) return false;

        // All validation and the native release happen before money moves. The serial input applies both purses and the receipt together.
        purse.Money -= amount;
        world.Player.Money += amount;
        settledPayments.Add(held.Record);
        cases[held.Record] = c with { Outcome = End.Ransomed };
        OfferCustodyMoment(world, held, End.Ransomed);
        return true;
    }

    internal bool IsOpenEpisode(string record, int episode) =>
        cases.GetValueOrDefault(record) is { Outcome: End.Open } c && c.Episode == episode;

    internal bool Recover(World world, Held held, bool covert = false)
    {
        if (IsLeader(held.Record) || !IsOpenEpisode(held.Record, held.Episode) || RouteOf(held.Record) != Route.Open ||
            held.Captive.SlaveState != 0 || held.Captive.Medical is not { Dead: false, Incapacitated: false } ||
            held.Cage.Lock is { EffectivelyLocked: true } || !PrisonerHandover.Free(world, held)) return false;
        cases[held.Record] = cases[held.Record] with { Outcome = covert ? End.CovertRecovered : End.Recovered };
        if (!covert) OfferCustodyMoment(world, held, End.Recovered);
        return true;
    }

    void OfferCustodyMoment(World world, Held held, End outcome)
    {
        var c = cases[held.Record];
        if (!c.Told) return;
        string place = world.Buildings.FindTown(held.Site.Town)?.Name ?? held.Place;
        string line = CustodyLine(c, outcome);
        bool accepted = RecapLink.Moment(line, place, 80, held.Cage.Position);
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "Recap", $"custody:{c.Episode}", accepted ? "accepted scene moment" : "scene moment not accepted",
                $"{line} Cage {held.Cage.Id}");
    }

    int LawfulIssuer(CharacterCold captive)
    {
        if (data is null || captive.Bounties is not { } warrants || warrants.Entries.Any(b => b.Claimed)) return -1;
        return warrants.Entries.Where(b => !b.Claimed && b.Amount > 0 && b.Faction >= 0 && b.Faction < data.Factions.Count &&
                lawful.Contains(data.Factions[b.Faction].Name) && data.Factions[b.Faction].BooksBounties && data.SiteFactions.Contains(b.Faction))
            .OrderByDescending(b => b.Amount).ThenBy(b => b.Faction).Select(b => b.Faction).DefaultIfEmpty(-1).First();
    }

    void StartDealWorld(World world)
    {
        if (ReferenceEquals(dealWorld, world)) return;
        dealWorld = world;
        dealSession = new object();
        PrisonerMenu.Lawful([]);
    }

    void PublishLawful(World world)
    {
        if (!LwMod.Settings.PrisonerDeals || !PrisonerMenu.Available)
        {
            PrisonerMenu.Lawful([]);
            return;
        }
        var rows = new List<PrisonerMenu.Row>();
        foreach (var c in foreground)
        {
            if (c.Deal != Route.Open || !TryHeld(world, c.Record, out var held) || LawfulIssuer(held.Captive) is not (>= 0 and var issuer)) continue;
            rows.Add(new PrisonerMenu.Row(dealSession, c.Episode, held.Actor, held.Captive.Name,
                $"Reserve delivery to {data!.Factions[issuer].Name}", PrisonerAction.LawfulDelivery, data.Factions[issuer].Id));
        }
        PrisonerMenu.Lawful(rows.ToArray());
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        if (!ReferenceEquals(dealWorld, world) || !LwMod.Settings.PrisonerDeals || !LwMod.Settings.PrisonerCases) return;
        foreach (var input in commands)
        {
            if (input is not PrisonerChoice { Action: PrisonerAction.LawfulDelivery } choice ||
                !ReferenceEquals(choice.Session, dealSession) || world.Uniques.RecordOf(choice.Target) is not { } record ||
                cases.GetValueOrDefault(record) is not { Deal: Route.Open } c ||
                !TryHeld(world, record, out var held) || held.Actor != choice.Target || held.Episode != choice.Episode ||
                !PlayerNear(world, choice.Party, held.Cage.Position, 250) ||
                LawfulIssuer(held.Captive) is not (>= 0 and var issuer) || choice.Issuer != data!.Factions[issuer].Id) continue;

            string faction = data!.Factions[issuer].Name;
            cases[record] = c with { Deal = Route.Lawful, Claimant = data.Factions[issuer].Id };
            if (PlaytestLog.Enabled)
                PlaytestLog.Event(world, "prisoner case", $"custody:{c.Episode}", "lawful delivery reserved", $"{record} to {faction}");
            world.Systems.OfType<HiveEnvoys>().FirstOrDefault()?.Recall(world, c.Episode);
            string line = $"The squad agreed to take {c.Name} to {KeepsakeText.Faction(faction)}.";
            LwDeeds.For(world)?.Record.Note(line, world.GameHours, c.Place);
            world.Post($"Take {c.Name} to the usual bounty desk of {KeepsakeText.Faction(faction)}.");
            LwMod.Log($"prisoner deals: lawful delivery reserved for {record}");
            PrisonerMenu.Lawful([]);
        }
    }

    void ConfirmNativeClaims(World world, LwDeeds deeds)
    {
        if (!LwMod.Settings.PrisonerDeals) return;
        foreach (var c in cases.Values.ToArray())
        {
            if (c.Deal == Route.LawfulPaid || c.Outcome != End.Open || world.Uniques.Find(c.Record) is not { } unique ||
                !world.Characters.TryResolveNext(unique.Handle, out int slot) ||
                world.Characters.Cold(slot)?.Bounties?.Entries.FirstOrDefault(b => b.Claimed && b.Amount > 0) is not { } claim) continue;
            string faction = deeds.FactionName(claim.Faction);
            // The native bounty ledger records what was collected. A desk can pay before taking custody.
            string issuer = data is not null && claim.Faction >= 0 && claim.Faction < data.Factions.Count ? data.Factions[claim.Faction].Id : "";
            settledPayments.Add(c.Record);
            cases[c.Record] = c with { Deal = Route.LawfulPaid, Claimant = issuer };
            if (PlaytestLog.Enabled)
                PlaytestLog.Event(world, "prisoner case", $"custody:{c.Episode}", "native bounty paid", $"{c.Record}, {claim.Amount} cats, {faction}");
            world.Systems.OfType<HiveEnvoys>().FirstOrDefault()?.Recall(world, c.Episode);
            LwMod.Log($"prisoner deals: native bounty ledger has a paid claim for {c.Record} ({faction})");
        }
    }

    string DealNote(Case c) => c.Deal switch
    {
        Route.Lawful => data?.Db.Find(c.Claimant) is { } faction
            ? $"\nLawful delivery reserved for {KeepsakeText.Faction(faction.Name)}." : "\nLawful delivery reserved.",
        Route.Ransom => "\nRansom terms accepted.",
        Route.LawfulPaid => "\nA bounty has already been claimed.",
        _ => "",
    };
}
