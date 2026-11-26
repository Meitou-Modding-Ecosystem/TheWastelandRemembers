using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Meitou.Simulation;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Locks;

namespace LivingWasteland.MeitouMod;

// Set Free reports its crime against the cage's owner just before the engine clears the hold. A later notice only
// proves this case when the same prisoner and cage left the same tick and the native release marked the prisoner freed.
internal static class KindnessReleases
{
    const float AtCage = 100;  // world units, including a short ramp up to the cage
    static readonly Assembly engine = typeof(World).Assembly;
    static readonly Type? payload = engine.GetType("Meitou.Simulation.Crime.CrimeEvent");
    static readonly PropertyInfo? kind = payload?.GetProperty("Kind"), code = payload?.GetProperty("Code"),
        culprit = payload?.GetProperty("Culprit"), victim = payload?.GetProperty("Victim"), owner = payload?.GetProperty("VictimFaction");
    static readonly PropertyInfo? freed = typeof(CharacterCold).GetProperty("Freed");
    static readonly object? pending = kind?.PropertyType is { IsEnum: true } type && Enum.TryParse(type, "Pending", out var parsed) ? parsed : null;
    internal static Type? Observer { get; } = culprit?.PropertyType == typeof(CharacterId) && victim?.PropertyType == typeof(CharacterId) &&
        code?.PropertyType == typeof(CrimeCode) && owner?.PropertyType == typeof(int) && pending is not null && freed?.PropertyType == typeof(bool)
        ? engine.GetType("Meitou.Simulation.Crime.ICrimeObserver") : null;
    internal static bool Enabled => Observer is not null && KindnessWatch.Enabled;

    readonly record struct Witness(string Fixture, string Helper, int Captor, double HeldHours);
    sealed class ThisTick
    {
        internal long Tick;
        internal readonly Dictionary<CharacterId, Witness> Releases = [];
    }
    static readonly ConditionalWeakTable<World, ThisTick> evidence = new();

    internal static void Crime(World world, object notice)
    {
        if (!Enabled || !pending!.Equals(kind!.GetValue(notice)) || code!.GetValue(notice) is not CrimeCode.Terrorism ||
            culprit!.GetValue(notice) is not CharacterId helper || victim!.GetValue(notice) is not CharacterId who ||
            owner!.GetValue(notice) is not int captor || captor < 0 || captor == world.Player.Faction) return;
        var table = world.Characters;
        if (!table.TryResolveNext(helper, out int worker) || !table.TryResolveNext(who, out int target) ||
            table.Cold(worker) is not { IsPlayer: true, Animal: null } squadmate || OtherModsPeople.Runs(world, squadmate) ||
            table.Cold(target) is not { IsPlayer: false, IsImprisoned: true, InWhat: { } fixture, Medical.Dead: false } prisoner ||
            freed!.GetValue(prisoner) is not false || world.Fixtures.Find(fixture) is not { Kind: FixtureKind.Cage } cage ||
            cage.Occupant != who || cage.Owner != captor ||
            Vector3.DistanceSquared(table.Next[worker].Position, PrisonerHandover.Exit(cage)) > AtCage * AtCage) return;

        var current = evidence.GetOrCreateValue(world);
        if (current.Tick != world.Tick)
        {
            current.Tick = world.Tick;
            current.Releases.Clear();
        }
        double hours = prisoner.SlaveState > 0 && prisoner.SlaveSince > 0 ? Math.Max(0, world.GameHours - prisoner.SlaveSince) : 0;
        current.Releases[who] = new(fixture, squadmate.Name, captor, hours);
    }

    internal static void Released(World world, CharacterId who, CharacterCold prisoner, string fixture)
    {
        if (!Enabled || !evidence.TryGetValue(world, out var current) || current.Tick != world.Tick ||
            !current.Releases.Remove(who, out var witness) || witness.Fixture != fixture ||
            prisoner.IsPlayer || !prisoner.CarriedBy.IsNone || prisoner.Medical is not { Dead: false } ||
            freed!.GetValue(prisoner) is not true || LwDeeds.For(world) is not { } deeds) return;
        KindnessWatch.Remember(world, deeds, who, prisoner, KindnessKind.Freed, witness.Helper,
            heldHours: witness.HeldHours, captor: deeds.FactionName(witness.Captor));
    }
}
