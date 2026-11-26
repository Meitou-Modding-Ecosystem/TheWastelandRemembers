// Bathhouse marks visiting parties in the shared tag before it holds or sends them. TWR leaves their orders and tags alone.
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace LivingWasteland.MeitouMod;

sealed class BathhouseGuests
{
    const string TagPrefix = "bathhouse.customers.";
    Dictionary<int, Platoon>? visitors;
    List<int>? expired;
    HashSet<CharacterId>? members;

    static bool Marked(Platoon party) => Pursuit.SharedTag(party)?.StartsWith(TagPrefix, StringComparison.Ordinal) == true;

    internal static bool Contains(World world, int squadId) => squadId >= 0 &&
        world.Squads.Find(squadId) is { PlatoonId: >= 0 } squad &&
        world.Platoons.Find(squad.PlatoonId) is { State: PlatoonState.Loaded, Dead: false } party &&
        party.SquadId == squadId && Marked(party);

    internal void Reset(World world, NightlyHooks.AskSession session)
    {
        visitors?.Clear();
        if (session.Towns.Length == 0 || !PlatoonFates.Available) return;
        // Saves supply starting platoons without an Added notice. Later arrivals are tracked by notice.
        foreach (var party in world.Platoons.All)
            if (party is { State: PlatoonState.Loaded } && Marked(party))
                (visitors ??= [])[party.Id] = party;
        Publish(world, session);
    }

    internal void Observe(Platoon party, string change)
    {
        if (change is "Removed" or "Unloaded") visitors?.Remove(party.Id);
        else if (change is "Added" or "Loaded" && party.State == PlatoonState.Loaded && Marked(party))
            (visitors ??= [])[party.Id] = party;
    }

    internal void Publish(World world, NightlyHooks.AskSession session)
    {
        if (visitors is not { Count: > 0 })
        {
            session.Guests = null;
            return;
        }
        var ids = members ??= [];
        var remove = expired ??= [];
        ids.Clear();
        remove.Clear();
        foreach (var (id, party) in visitors)
        {
            if (party.Dead || !Marked(party) || !ReferenceEquals(party, world.Platoons.Find(id)))
            {
                remove.Add(id);
                continue;
            }
            if (party.State != PlatoonState.Loaded || world.Squads.Find(party.SquadId) is not { } squad || squad.PlatoonId != id) continue;
            foreach (var character in squad.Members) ids.Add(character);
        }
        foreach (int id in remove) visitors.Remove(id);
        if (ids.Count == 0) session.Guests = null;
        else if (session.Guests is not HashSet<CharacterId> old || !old.SetEquals(ids))
            session.Guests = new HashSet<CharacterId>(ids);
    }
}
