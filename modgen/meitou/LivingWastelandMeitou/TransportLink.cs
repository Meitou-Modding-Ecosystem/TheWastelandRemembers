// Meitou Transport (mattresspad's framework: a squad member rides an owned Garru or Pack Beast, or sails a one-seat skiff), when it is
// installed. A rider's or helmsman's position is moved with the animal or boat, so the squad record would count a ride as walking. We ask
// the framework's TransportSystem (one of world.Systems) whom it has seated: its private `riders` (animals) and `helms` (boats), read by
// reflection so this mod loads the same with Transport or without it; a missing field means "not seated" with one log line. Read only.
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class TransportLink
{
    public const int None = 0, Riding = 1, Sailing = 2;

    sealed class Found(object? system) { public readonly object? System = system; }
    static readonly ConditionalWeakTable<World, Found> systems = new();
    static FieldInfo? riders, helms;
    static bool looked, off;

    /// <summary>Whether the character is riding an animal, sailing, or neither (also when Transport is not installed).</summary>
    public static int Mode(World world, CharacterId who)
    {
        if (off) return None;
        try
        {
            var system = systems.GetValue(world, static w =>
                new Found(w.Systems.FirstOrDefault(s => s.GetType().FullName == "Meitou.Transport.TransportSystem"))).System;
            if (system is null) return None;
            if (!looked)
            {
                looked = true;
                var t = system.GetType();
                riders = t.GetField("riders", BindingFlags.NonPublic | BindingFlags.Instance);
                helms = t.GetField("helms", BindingFlags.NonPublic | BindingFlags.Instance);
                RecordHost.Log(riders is null && helms is null
                    ? "Meitou Transport: found, but its seat lists changed; rides count as walking"
                    : "Meitou Transport: found; rides and voyages are counted on their own");
            }
            if (riders?.GetValue(system) is IDictionary r && r.Contains(who)) return Riding;
            if (helms?.GetValue(system) is IDictionary h && h.Contains(who)) return Sailing;
            return None;
        }
        catch (Exception e)
        {
            off = true;
            RecordHost.Log($"Meitou Transport: reading its seats failed ({e.GetBaseException().Message}); rides count as walking");
            return None;
        }
    }
}
