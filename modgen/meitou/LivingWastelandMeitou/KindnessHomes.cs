using System.Numerics;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

// One look at the handful of remembered actors. They keep whatever work, movement and company the game gave them.
internal sealed class KindnessHomes
{
    const double SinceDeed = 24, SettlingHours = 6;
    const float SettlingDistance = 800, Radius = 3500;
    readonly record struct Candidate(string Place, Vector2 At, double Since);
    readonly SortedDictionary<ulong, Candidate> candidates = [];

    internal void Look(World world, LwDeeds deeds, List<(string Name, CharacterId Id, Vector3 At, bool Down)> squad)
    {
        var book = deeds.Kindness;
        bool tags = OwnedCharacterTags.Preview;
        for (int i = 0; i < book.People.Count; i++)
        {
            var memory = book.People[i];
            if (tags && LastingActors.Find(world, memory.Actor) is { IsNone: false } actor)
                OwnedCharacterTags.MarkRole(world, actor, OwnedCharacterTags.Kindness, "remembered");
            if (book.Home(memory.Actor) is { } home) MarkApproach(book, memory.Actor, home, squad);
            else Observe(world, deeds, memory);
        }

        KindnessReunions.Listen(world, deeds, squad);
        if (candidates.Count > KindnessRules.Remembered || candidates.Count > book.People.Count) Prune(book);
    }

    void Prune(KindnessBook book)
    {
        foreach (var actor in candidates.Keys.Where(id => !book.People.Any(deed => deed.Actor == id)).ToArray())
            candidates.Remove(actor);
    }

    void Observe(World world, LwDeeds deeds, KindnessDeed memory)
    {
        double now = world.GameHours;
        if (!(now >= memory.Hours + SinceDeed)) return;
        var id = LastingActors.Find(world, memory.Actor);
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || !table.Next[slot].Alive || table.Cold(slot) is not
            { IsPlayer: false, IsImprisoned: false, InCombat: false, CarriedBy.IsNone: true, Medical: { Dead: false, Incapacitated: false } } actor ||
            (deeds.Data is { } data && world.Player.Faction >= 0 && actor.Faction >= 0 && data.Relations.Get(world.Player.Faction, actor.Faction) <= -50))
        {
            candidates.Remove(memory.Actor);
            return;
        }

        var position = table.Next[slot].Position;
        string place = deeds.TownAt(position).Town ?? "the wilds";
        var at = new Vector2(position.X, position.Z);
        if (!candidates.TryGetValue(memory.Actor, out var candidate) || candidate.Place != place || now < candidate.Since ||
            Vector2.DistanceSquared(at, candidate.At) > SettlingDistance * SettlingDistance)
        {
            candidates[memory.Actor] = new(place, at, now);
            return;
        }
        if (now - candidate.Since < SettlingHours) return;

        var home = new KindnessHome(place, candidate.At.X, candidate.At.Y, Radius, now);
        deeds.Kindness.SetHome(memory.Actor, home);
        candidates.Remove(memory.Actor);
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "kindness", FormattableString.Invariant($"person:{memory.Actor}"), "settled", $"{memory.Name} at {home.Place}");
    }

    static void MarkApproach(KindnessBook book, ulong actor, KindnessHome home, List<(string Name, CharacterId Id, Vector3 At, bool Down)> squad)
    {
        if (squad.Count == 0 || home.Approached) return;
        bool near = false;
        foreach (var person in squad)
        {
            if (!home.Contains(person.At.X, person.At.Z)) continue;
            near = true;
            break;
        }

        if (near && home.WasAway) book.SetHome(actor, home with { Approached = true });
        else if (!near && !home.WasAway) book.SetHome(actor, home with { WasAway = true });
    }

    internal void Clear() => candidates.Clear();

    internal void Hash(ref StateHasher hash)
    {
        foreach (var (actor, candidate) in candidates)
        {
            hash.Add(actor); hash.Add(SquadRecord.StableHash(candidate.Place));
            hash.Add(candidate.At.X); hash.Add(candidate.At.Y); hash.Add(candidate.Since);
        }
    }
}
