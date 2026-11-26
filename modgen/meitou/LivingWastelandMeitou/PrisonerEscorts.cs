using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

/// <summary>A freed captive walks home with the recovering party while both are materialised.</summary>
internal sealed class PrisonerEscorts(PopulationData data) : ITickSystem, IModSaveData, IStateHashed
{
    const float HomeReach = 350, EscortReach = 900;
    const double ReturnHours = 72;

    sealed record Trip(string Captive, int Episode, string Faction, string Town, string Issuer, Vector3 Destination, double Until)
    {
        internal RecoveryGate.Crossing[] Entry { get; init; } = [];
        internal bool StandIns { get; init; }
        internal bool Covert { get; init; }
        internal string WardTag => Tag + ":ward";
        internal string Tag => FormattableString.Invariant($"lw:prisoner-escort:{Episode}:{Captive}");
    }

    readonly SortedDictionary<string, Trip> trips = new(StringComparer.Ordinal);
    static string summary = "no captive escorts";

    internal static void Register(ModContext context)
    {
        if (!Couriers.Available) return;
        context.AddSystem("lw-prisoner-escorts", ctx => new PrisonerEscorts(ctx.Data), SystemAnchor.End);
        context.AddDebugCommand(UiLanguage.Text("Wasteland Remembers: prisoner escorts"), _ => Volatile.Read(ref summary), hasField: false);
    }

    internal static bool Begin(World world, Platoon party, PrisonerCases.Held held, string faction, Vector3 destination,
        string town = "", string issuer = "", RecoveryGate.Crossing[]? entry = null, bool covert = false)
    {
        if ((!LwMod.Settings.PrisonerEscorts && entry is not { Length: > 0 }) || !Couriers.Available ||
            world.Systems.OfType<PrisonerEscorts>().FirstOrDefault() is not { } escorts ||
            escorts.trips.ContainsKey(held.Record) || party is not { Dead: false, State: PlatoonState.Loaded } ||
            !world.Characters.TryResolveNext(held.Actor, out int slot) ||
            world.Characters.Cold(slot) is not { IsPlayer: false, IsImprisoned: false, Medical.Dead: false } actor ||
            actor.Faction != party.Faction || faction.Length == 0 || !float.IsFinite(destination.X) || !float.IsFinite(destination.Z))
            return false;

        var trip = new Trip(held.Record, held.Episode, faction, town, issuer, destination, world.GameHours + ReturnHours) { Entry = entry ?? [], Covert = covert };
        Couriers.LeaveVisit(world, party);
        if (!Couriers.SendHeld(world, party, party.Position) || !Pursuit.Mark(party, trip.Tag)) return false;
        escorts.trips.Add(held.Record, trip);
        if (PlaytestLog.Enabled)
            PlaytestLog.Change(world, "prisoner escort", trip.Tag, "started",
                $"faction {faction}, destination {town}, issuer {issuer}, covert {covert}, passages {trip.Entry.Length}");
        LwMod.Log($"prisoner escort: {held.Name} is walking home with the party");
        return true;
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % CovertRecovery.Cadence != 7 || trips.Count == 0) return;
        foreach (var trip in trips.Values.ToArray())
            if (trip.Entry.Length > 0 || world.Tick % 90 == 67) Advance(world, trip);
        Volatile.Write(ref summary, $"{trips.Count} captive escorts on the road");
    }

    void Advance(World world, Trip trip)
    {
        var party = EscortStandIns.Tagged(world, trip.Tag);
        int faction = data.FactionIndex(trip.Faction);
        if (!LwMod.Settings.PrisonerEscorts && trip.Entry.Length == 0)
        {
            Finish(world, trip, party?.Faction == faction ? party : null, "escort setting is off");
            return;
        }
        if (faction < 0 || party is not { Dead: false } || party.Faction != faction)
        {
            Finish(world, trip, party?.Faction == faction ? party : null, "tagged guide party missing, dead, or faction mismatch");
            return;
        }
        if (world.GameHours >= trip.Until)
        {
            Finish(world, trip, party, "route expired");
            return;
        }
        if (!HasHome(world, trip, faction))
        {
            Finish(world, trip, party, "home is no longer valid for this faction");
            return;
        }
        if (world.Uniques.Find(trip.Captive) is not { State: UniqueState.Alive } unique)
        {
            Finish(world, trip, party, "unique ward missing or no longer alive");
            return;
        }
        if (Pursuit.Beaten(world, party))
        {
            Finish(world, trip, party, "guide party defeated");
            return;
        }

        var table = world.Characters;
        bool hasCaptive = table.TryResolveNext(unique.Handle, out int captive);
        var actor = hasCaptive ? table.Cold(captive) : null;
        if (hasCaptive && (actor is not { IsPlayer: false, IsImprisoned: false, Medical.Dead: false } || actor.Faction != faction))
        {
            Finish(world, trip, party, "ward dead, recaptured, recruited or in a different faction");
            return;
        }

        if (trip.StandIns)
        {
            var ward = EscortStandIns.Tagged(world, trip.WardTag);
            if (ward is null || !EscortStandIns.Eligible(ward, faction) ||
                (actor is not null && !EscortStandIns.SameLoadedWard(world, ward, unique.Handle, actor)))
            {
                Finish(world, trip, party, "tagged ward did not match the escort");
                return;
            }
            if (party.State != PlatoonState.Loaded || ward.State != PlatoonState.Loaded)
            {
                PlaytestLog.Change(world, "prisoner escort", trip.Tag, "travel via off-screen platoons");
                AdvanceStandIns(world, trip, party, ward, faction, actor);
                return;
            }
        }

        // A stand-in by itself cannot prove that the individual is still travelling with it.
        if (!Couriers.SendHeld(world, party, party.Position))
        {
            Finish(world, trip, party, "native hold refused");
            return;
        }
        if (party.State != PlatoonState.Loaded || world.Squads.Find(party.SquadId) is not { } squad ||
            actor is not { Medical.Incapacitated: false, InCombat: false } || !actor.CarriedBy.IsNone)
        {
            PlaytestLog.Change(world, "prisoner escort", trip.Tag, "paused",
                actor switch
                {
                    { InCombat: true } => "ward in combat",
                    { Medical.Incapacitated: true } => "ward incapacitated",
                    { CarriedBy.IsNone: false } => "ward is being carried",
                    _ => "waiting for loaded party, squad, or ward",
                });
            return;
        }

        int guide = -1;
        float closest = float.MaxValue;
        var at = table.Next[captive].Position;
        foreach (var person in squad.Members)
        {
            if (!table.TryResolveNext(person, out int slot) || !table.Next[slot].Alive ||
                table.Cold(slot) is not { IsPlayer: false, IsImprisoned: false, InCombat: false, Medical: { Dead: false, Incapacitated: false } } member ||
                member.Faction != faction || member.SquadId != squad.Id || !member.CarriedBy.IsNone) continue;
            float distance = Vector2.DistanceSquared(Couriers.Flat(at), Couriers.Flat(table.Next[slot].Position));
            if (distance < closest) (guide, closest) = (slot, distance);
        }
        if (guide < 0)
        {
            PlaytestLog.Change(world, "prisoner escort", trip.Tag, "paused", "no eligible guide, including guides in combat or down");
            return;
        }

        if (trip.Entry.Length > 0)
        {
            CovertRecovery.SetSneak(world, squad, true);
            var exit = trip.Entry[^1];
            PlaytestLog.Change(world, "prisoner escort", trip.Tag, "exiting", exit.Door);
            var guideAt = table.Next[guide].Position;
            bool prisonerOut = exit.OnNearSide(at), guideOut = exit.OnNearSide(guideAt);
            if (prisonerOut && guideOut)
            {
                Trip remaining = trip with { Entry = trip.Entry[..^1] };
                trips[trip.Captive] = remaining;
                if (remaining.Entry.Length == 0)
                {
                    CovertRecovery.SetSneak(world, squad, false);
                    if (!LwMod.Settings.PrisonerEscorts) Finish(world, remaining, party, "extraction completed without a homeward escort");
                }
                return;
            }

            if (world.Fixtures.Find(exit.Door) is { Lock.EffectivelyLocked: true } gate && RecoveryGate.Shut(gate))
            {
                // A relocked exit must be worked from the inside again. A new guard or a fight can interrupt that work.
                if (!guideOut && !CovertRecovery.Interrupted(world, squad) &&
                    world.Systems.OfType<LockSystem>().FirstOrDefault() is { } locks)
                    CovertRecovery.PickDoor(world, locks, guide, gate);
                return;
            }

            if (!prisonerOut) PrisonerHandover.WalkTo(world, captive, exit.ExitToward(world, at));
            if (!guideOut) PrisonerHandover.WalkTo(world, guide, exit.ExitToward(world, guideAt));
            return;
        }

        var home = Couriers.Flat(trip.Destination);
        if (Vector2.DistanceSquared(Couriers.Flat(at), home) <= HomeReach * HomeReach &&
            Vector2.DistanceSquared(Couriers.Flat(table.Next[guide].Position), home) <= EscortReach * EscortReach)
        {
            Arrived(world, trip, party, actor.Name);
            return;
        }

        bool regroup = closest > EscortReach * EscortReach;
        PlaytestLog.Change(world, "prisoner escort", trip.Tag, regroup ? "regrouping" : "walking home");
        if (regroup)
        {
            PrisonerHandover.WalkTo(world, guide, at);
            return;
        }

        if (!trip.StandIns && EscortStandIns.Link(world, party, actor, unique.Handle, trip.WardTag))
            trips[trip.Captive] = trip with { StandIns = true };
        PrisonerHandover.WalkTo(world, guide, trip.Destination);
        PrisonerHandover.WalkTo(world, captive, trip.Destination);
    }

    void AdvanceStandIns(World world, Trip trip, Platoon party, Platoon ward, int faction, CharacterCold? actor)
    {
        var home = Couriers.Flat(trip.Destination);
        string? stored = EscortStandIns.ExactStored(ward, trip.Captive, faction);
        if (stored is not null && EscortStandIns.HasGuide(party))
        {
            if (Vector2.DistanceSquared(ward.Position, home) <= HomeReach * HomeReach &&
                Vector2.DistanceSquared(party.Position, home) <= HomeReach * HomeReach)
            {
                Arrived(world, trip, party, stored);
                return;
            }
            if (!EscortStandIns.SendPair(world, party, ward, home, EscortReach))
                Finish(world, trip, party, "off-screen route refused");
            return;
        }

        // During activation keep the other side nearby. Proof is rechecked before either stand-in is sent home.
        if (stored is not null && party.State == PlatoonState.Loaded)
            Couriers.SendHeld(world, ward, party.Position);
        else if (actor is { Medical.Incapacitated: false, InCombat: false } && EscortStandIns.HasGuide(party))
            Couriers.SendHeld(world, party, ward.Position);
        else
        {
            Couriers.SendHeld(world, ward, ward.Position);
            Couriers.SendHeld(world, party, party.Position);
        }
    }

    void Arrived(World world, Trip trip, Platoon party, string name)
    {
        string place = trip.Town.Length > 0 ? data.Sites.First(s => NewsSystem.NodeOf(s) == trip.Town).Town.Name : "the gang's camp";
        if (!trip.Covert) LwDeeds.For(world)?.Record.Note($"The escort brought {name} home.", world.GameHours, place);
        LwMod.Log($"prisoner escort: {name} reached {place}");
        Finish(world, trip, party, $"reached {place}");
    }

    bool HasHome(World world, Trip trip, int faction)
    {
        if (trip.Issuer.Length > 0) return world.Uniques.Find(trip.Issuer) is { Claimed: true, State: UniqueState.Alive };
        for (int i = 0; i < data.Sites.Count && i < data.SiteFactions.Count; i++)
            if (data.SiteFactions[i] == faction && NewsSystem.NodeOf(data.Sites[i]) == trip.Town) return true;
        return false;
    }

    void Finish(World world, Trip trip, Platoon? party, string reason)
    {
        PlaytestLog.Event(world, "prisoner escort", trip.Tag, "ended", reason);
        if (party is not null)
        {
            if (trip.Entry.Length > 0 && world.Squads.Find(party.SquadId) is { } squad &&
                squad.TemplateId == party.TemplateId && squad.Faction == party.Faction)
                CovertRecovery.SetSneak(world, squad, false);
            Couriers.Release(world, party);
        }
        if (trip.StandIns && EscortStandIns.Tagged(world, trip.WardTag) is { } ward &&
            ward.Faction == data.FactionIndex(trip.Faction)) EscortStandIns.Release(world, ward);
        trips.Remove(trip.Captive);
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(4);
        w.Write(trips.Count);
        foreach (var trip in trips.Values)
        {
            w.Write(trip.Captive);
            w.Write(trip.Episode);
            w.Write(trip.Faction);
            w.Write(trip.Town);
            w.Write(trip.Issuer);
            w.Write(trip.Destination.X);
            w.Write(trip.Destination.Y);
            w.Write(trip.Destination.Z);
            w.Write(trip.Until);
            RecoveryGate.Crossing.Write(w, trip.Entry);
            w.Write(trip.StandIns);
            w.Write(trip.Covert);
        }
    }

    public void Read(Stream stream)
    {
        trips.Clear();
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Position == stream.Length) return;
        int version = r.ReadInt32();
        if (version is not (1 or 2 or 3 or 4)) return;
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var trip = new Trip(r.ReadString(), r.ReadInt32(), r.ReadString(), r.ReadString(), r.ReadString(),
                new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), r.ReadDouble());
            if (version >= 2) trip = trip with { Entry = RecoveryGate.Crossing.Read(r) };
            if (version >= 3) trip = trip with { StandIns = r.ReadBoolean() };
            trip = trip with { Covert = version >= 4 ? r.ReadBoolean() : trip.Entry.Length > 0 };
            trips[trip.Captive] = trip;
        }
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(trips.Count);
        foreach (var trip in trips.Values)
        {
            h.Add(SquadRecord.StableHash(trip.Captive));
            h.Add(trip.Episode);
            h.Add(SquadRecord.StableHash(trip.Faction));
            h.Add(SquadRecord.StableHash(trip.Town));
            h.Add(SquadRecord.StableHash(trip.Issuer));
            h.Add(trip.Destination.X);
            h.Add(trip.Destination.Y);
            h.Add(trip.Destination.Z);
            h.Add(trip.Until);
            RecoveryGate.Crossing.Hash(ref h, trip.Entry);
            h.Add(trip.StandIns ? 1 : 0);
            h.Add(trip.Covert ? 1 : 0);
        }
    }
}
