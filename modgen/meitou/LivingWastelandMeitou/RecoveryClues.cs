using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Locks;

namespace LivingWasteland.MeitouMod;

/// <summary>Only a squadmate standing near fresh, verified physical evidence can comment on it.</summary>
internal sealed class RecoveryClues : ITickSystem, IModSaveData, IStateHashed
{
    internal enum Kind : byte { OpenLock, BrokenGate, EmptyCage }
    sealed record Clue(int Episode, string Fixture, string Building, Kind Sign, double Until, bool Heard = false);
    const int Capacity = 48;
    const double Lifetime = 24;
    readonly List<Clue> clues = [];

    internal static void Notice(World world, int episode, Fixture fixture, Kind sign)
    {
        if (world.Systems.OfType<RecoveryClues>().FirstOrDefault() is not { } system ||
            system.clues.Any(c => c.Episode == episode && c.Fixture == fixture.Id && c.Sign == sign)) return;
        if (system.clues.Count >= Capacity) system.clues.RemoveAt(0);
        system.clues.Add(new(episode, fixture.Id, fixture.Building, sign, world.GameHours + Lifetime));
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % 150 != 133 || !LwMod.Settings.PrisonerRecovery) return;
        clues.RemoveAll(c => c.Until <= world.GameHours);
        for (int i = 0; i < clues.Count; i++)
        {
            var c = clues[i];
            if (c.Heard || world.Fixtures.Find(c.Fixture) is not { } fixture || fixture.Building != c.Building ||
                fixture.Owner != world.Player.Faction || world.Buildings.Find(c.Building) is not { Complete: true, Destroyed: false } site ||
                site.Owner != world.Player.Faction || !StillThere(fixture, c.Sign) ||
                FindSpeaker(world, fixture.Position) is not { } speaker) continue;
            world.Bark(speaker, c.Sign switch
            {
                Kind.OpenLock => "Someone's been at this lock.",
                Kind.BrokenGate => "Gate's smashed. Check the yard.",
                _ => "Cage is empty. Someone look outside.",
            });
            clues[i] = c with { Heard = true };
            return;
        }
    }

    static bool StillThere(Fixture fixture, Kind sign) => sign switch
    {
        Kind.OpenLock => fixture.Kind == FixtureKind.Door && !fixture.DoorBroken && fixture.Lock is { EffectivelyLocked: false },
        Kind.BrokenGate => fixture is { Kind: FixtureKind.Door, IsGate: true, DoorBroken: true },
        Kind.EmptyCage => fixture is { Kind: FixtureKind.Cage, Occupant.IsNone: true },
        _ => false,
    };

    static CharacterId? FindSpeaker(World world, Vector3 at)
    {
        var table = world.Characters;
        for (int i = 0; i < table.HighWater; i++)
        {
            var member = table.Next[i];
            if (member is not { Alive: true, Dormant: false } || table.Cold(i) is not
                { IsPlayer: true, Animal: null, InCombat: false, IsImprisoned: false, CarriedBy.IsNone: true,
                    Medical: { Dead: false, Incapacitated: false } } ||
                MathF.Abs(member.Position.Y - at.Y) > 20 || Vector3.DistanceSquared(member.Position, at) > 80 * 80 ||
                !PrisonerHandover.ClearSight(world, member.Position, at)) continue;
            return table.IdOf(i);
        }
        return null;
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(1);
        w.Write(clues.Count);
        foreach (var clue in clues)
        {
            w.Write(clue.Episode); w.Write(clue.Fixture); w.Write(clue.Building); w.Write((byte)clue.Sign);
            w.Write(clue.Until); w.Write(clue.Heard);
        }
    }

    public void Read(Stream stream)
    {
        clues.Clear();
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Position == stream.Length || r.ReadInt32() != 1) return;
        for (int count = r.ReadInt32(); count > 0 && clues.Count < Capacity; count--)
            clues.Add(new(r.ReadInt32(), r.ReadString(), r.ReadString(), (Kind)r.ReadByte(), r.ReadDouble(), r.ReadBoolean()));
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(clues.Count);
        foreach (var clue in clues)
        {
            h.Add(clue.Episode); h.Add(SquadRecord.StableHash(clue.Fixture)); h.Add(SquadRecord.StableHash(clue.Building));
            h.Add((int)clue.Sign); h.Add(clue.Until); h.Add(clue.Heard ? 1 : 0);
        }
    }
}
