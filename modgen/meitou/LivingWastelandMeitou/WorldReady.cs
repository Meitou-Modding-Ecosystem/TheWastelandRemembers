// The engine's "world ready" signal (Meitou 0.21+, IWorldReady: once between ticks before the first, after a save has loaded or a new game
// has placed the player's characters). This mod builds against 0.17.3, so ObserverEmit makes the system at load. With it, the bounty crews
// and outlaw reprisal parties a save left out (Meitou saves a squad only once it has loaded) are placed again on the first tick, not at the
// systems' next pass; Presence (first sighting per loaded world) still decides what is missing, and is all there is on older Meitou.
using System.Runtime.CompilerServices;
using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class WorldReady
{
    sealed class Told { public bool FromSave; public readonly HashSet<string> Taken = []; }
    static readonly ConditionalWeakTable<World, Told> told = new();

    /// <summary>Whether this Meitou has the signal (registered at load).</summary>
    public static bool Available { get; private set; }

    /// <summary>Registers the system where the engine has the interface. Never throws; logs what happened.</summary>
    public static void Register(ModContext c)
    {
        try
        {
            var observer = Find();
            if (observer is null) { c.Log("world ready: this Meitou has no world-ready signal (0.21 and later do); squads left out of a save are placed at the next pass"); return; }
            if (ObserverEmit.Build(observer, "Ready", typeof(WorldReady).GetMethod(nameof(Ready))!, "WorldReadySystem") is not { } type)
            {
                c.Log("world ready: the engine's signal changed; not added");
                return;
            }
            c.AddSystem("lw-world-ready", _ => (ITickSystem)Activator.CreateInstance(type)!, SystemAnchor.End);
            Available = true;
            c.Log("world ready: squads a save left out are placed on the first tick");
        }
        catch (Exception e) { c.Log($"world ready: not added ({e.GetBaseException().Message})"); }
    }

    /// <summary>The engine's IWorldReady, by name in any namespace (types that fail to load, such as ones needing a native library, are skipped).</summary>
    public static Type? Find()
    {
        Type?[] types;
        try { types = typeof(World).Assembly.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
        return types.FirstOrDefault(t => t is { IsInterface: true, Name: "IWorldReady" });
    }

    /// <summary>The engine's call (boxed bool: from a save, or a new game).</summary>
    public static void Ready(World world, object fromSave)
    {
        bool save = fromSave is true;
        RecapLink.ForgetSquadPlace();
        OutpostMemories.ResetPresentation();
        told.AddOrUpdate(world, new Told { FromSave = save });
        PlaytestLog.Event(world, "world", "", save ? "save loaded" : "new game");
        LwMod.Log($"world ready: {(save ? "a save has loaded" : "a new game")}");
    }

    /// <summary>
    /// True once per <paramref name="who"/> for a world just loaded from a save: that system runs its placing pass now, not at its usual
    /// tick. False for a new game, a world already taken, or a Meitou without the signal.
    /// </summary>
    public static bool TakeLoaded(World world, string who) =>
        told.TryGetValue(world, out var t) && t.FromSave && t.Taken.Add(who);
}
