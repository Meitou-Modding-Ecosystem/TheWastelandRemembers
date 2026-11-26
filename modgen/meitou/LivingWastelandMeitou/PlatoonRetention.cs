using System.Reflection;
using System.Runtime.CompilerServices;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace LivingWasteland.MeitouMod;

/// <summary>Preserves the exact platoon matched by a live pursuit when the engine asks to remove it.</summary>
public static class PlatoonRetention
{
    internal enum Kind { Crew, Reprisal, Rival }

    // Mutated only in the serial slow-world phase. The veto reads this state at the engine's settled-tick boundary.
    sealed class Job(string tag)
    {
        internal string Tag { get; set; } = tag;
        internal Kind Kind;
        internal double From, Until;
        internal RivalsSystem.Rival? Rival;
    }

    static readonly ConditionalWeakTable<World, ConditionalWeakTable<Platoon, Job>> jobs = new();
    static readonly Type? kind = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.RemovalKind");
    static readonly int despawned = Value("PlatoonDespawned"), orphaned = Value("PlatoonOrphaned");
    static readonly Type? veto = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.IRemovalVeto");

    internal static (Type Hook, MethodInfo Target)? Hook =>
        despawned >= 0 && orphaned >= 0 && veto is not null ? (veto, typeof(PlatoonRetention).GetMethod(nameof(Keep))!) : null;

    static int Value(string name) => kind is { IsEnum: true } && Enum.TryParse(kind, name, out object? value) ? Convert.ToInt32(value) : -1;

    static readonly Lazy<Type?> adapter = new(() =>
        veto is not null && PlatoonFates.BuildWatch() is { } watch && veto.IsAssignableFrom(watch) ? watch : null);

    internal static Type? BuildWatch() => adapter.Value;

    internal static void Track(World world, Platoon party, string tag, Kind purpose, double from, double until, RivalsSystem.Rival? rival = null)
    {
        if (BuildWatch() is null || Pursuit.ReadTag(party) != tag) return;
        var parties = jobs.GetOrCreateValue(world);
        if (!parties.TryGetValue(party, out var job))
        {
            job = new Job(tag);
            parties.Add(party, job);
            PlaytestLog.Event(world, "platoon retention", tag, "despawn protection armed", purpose.ToString());
        }
        job.Tag = tag;
        job.Kind = purpose;
        job.From = from;
        job.Until = until;
        job.Rival = rival;
    }

    internal static void Release(World world, Platoon party, string tag)
    {
        if (jobs.TryGetValue(world, out var parties) && parties.TryGetValue(party, out var job) && job.Tag == tag)
            job.Until = double.NegativeInfinity;
    }

    /// <summary>Called on a simulation worker for the orphan check. Pure read and no allocations.</summary>
    public static bool Keep(World world, int reason, int platoon)
    {
        if (platoon < 0 || reason != despawned && reason != orphaned || !jobs.TryGetValue(world, out var parties) ||
            world.Platoons.Find(platoon) is not { Dead: false } party || !parties.TryGetValue(party, out var job) ||
            !(world.GameHours >= job.From && world.GameHours <= job.Until)) return false;

        bool active = job.Kind switch
        {
            Kind.Crew => LwMod.Settings.BountyHunters,
            Kind.Reprisal => LwMod.Settings.OutlawReprisals,
            Kind.Rival => LwMod.Settings.Rivals && job.Rival is { Told: true, Phase: RivalsSystem.Phase.Hunting } r &&
                r.PlatoonId == platoon && world.GameHours < r.Next,
            _ => false,
        };
        return active && Pursuit.ReadTag(party) == job.Tag;
    }
}
