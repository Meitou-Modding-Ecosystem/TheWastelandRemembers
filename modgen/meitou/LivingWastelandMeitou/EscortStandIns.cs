using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace LivingWasteland.MeitouMod;

/// <summary>While unloaded, a return can continue on two native stand-ins whose own rosters identify both travellers.</summary>
internal static class EscortStandIns
{
    static readonly System.Reflection.PropertyInfo? held = typeof(Platoon).GetProperty("Held");

    internal static Platoon? Tagged(World world, string tag)
    {
        var parties = world.Platoons.All.Where(p => p is not null && Pursuit.ReadTag(p) == tag).Take(2).ToArray();
        return parties.Length == 1 ? parties[0] : null;
    }

    internal static bool Link(World world, Platoon escort, CharacterCold actor, CharacterId exact, string tag)
    {
        if (world.Squads.Find(actor.SquadId) is not { } squad || squad.Faction != actor.Faction ||
            squad.Members is not [var sole] || sole != exact ||
            world.Platoons.Find(squad.PlatoonId) is not { State: PlatoonState.Loaded } ward ||
            ward == escort || ward.SquadId != squad.Id || !Eligible(ward, actor.Faction) ||
            !Pursuit.IsUntagged(ward) || held?.GetValue(ward) is not false) return false;

        if (!Couriers.SendHeld(world, ward, ward.Position)) return false;
        if (Pursuit.Mark(ward, tag)) return true;
        Couriers.LeaveVisit(world, ward);
        return false;
    }

    internal static bool Eligible(Platoon ward, int faction) => ward.Faction == faction &&
        ward is { Dead: false, Pinned: false, Wild: false, Campaign: null } && ward.Origin >= 0 &&
        (ward.Persistent || ward.Saved is not null);

    internal static string? ExactStored(Platoon ward, string record, int faction)
    {
        if (ward.State != PlatoonState.Unloaded || ward.Imprisoned || !Eligible(ward, faction)) return null;
        if (ward.Stored is { } stored)
            return stored.Members is [{ Cold: { Medical: { Dead: false, Incapacitated: false }, IsPlayer: false,
                IsImprisoned: false, CarriedBy.IsNone: true } actor }] &&
                actor.RecordId == record && actor.Faction == faction ? actor.Name : null;

        // The saved platoon's actual member list is needed after a process restart, before it has a StoredSquad.
        return ward.Saved?.Members is [var member] && AliveInSave(member) && member.Source.RecordId == record
            ? member.Source.Name : null;
    }

    internal static bool HasGuide(Platoon escort) => escort.State == PlatoonState.Unloaded && !escort.Imprisoned &&
        (escort.Stored is { } stored
            ? stored.Members.Any(m => m.Cold is { IsPlayer: false, IsImprisoned: false, Medical: { Dead: false, Incapacitated: false } } guide &&
                guide.Faction == escort.Faction)
            : escort.Saved?.Members.Any(AliveInSave) == true);

    static bool AliveInSave(Meitou.Simulation.Saving.SavedMember member) =>
        !member.Gone && !member.Dead && member.Medical is not ({ Dead: true } or { Incapacitated: true }) &&
        member.Source.Medical?.Bools.GetValueOrDefault("dead") != true;

    internal static bool SameLoadedWard(World world, Platoon ward, CharacterId exact, CharacterCold actor) =>
        ward is { State: PlatoonState.Loaded } && world.Squads.Find(ward.SquadId) is { } squad &&
        actor.SquadId == ward.SquadId && squad.PlatoonId == ward.Id && squad.Faction == ward.Faction &&
        squad.Members is [var sole] && sole == exact;

    internal static bool SendPair(World world, Platoon escort, Platoon ward, Vector2 destination, float together)
    {
        var a = escort.Position;
        var b = ward.Position;
        var guideGoal = destination;
        var wardGoal = destination;
        if (Vector2.DistanceSquared(a, b) > together * together)
        {
            // Hold the one farther along. Let the trailing stand-in close the gap on the same road.
            if (Vector2.DistanceSquared(a, destination) < Vector2.DistanceSquared(b, destination))
                (guideGoal, wardGoal) = (a, a);
            else (guideGoal, wardGoal) = (b, b);
        }
        return Couriers.SendHeld(world, escort, guideGoal) && Couriers.SendHeld(world, ward, wardGoal);
    }

    internal static void Release(World world, Platoon ward)
    {
        Couriers.LeaveVisit(world, ward);
        Pursuit.Mark(ward, "");
    }
}
