// Whether a character is asleep (Meitou's CarrySystem.Asleep: in a bed, or resting with the sleep behaviour). Meitou 0.19.1 stops a sleeping
// character from firing dialogue events, so our criers and lookouts pick someone awake instead of losing the line. Found by name: on a Meitou
// without it, nobody counts as asleep (as before).
using System.Reflection;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class Sleep
{
    static readonly MethodInfo? asleep = typeof(World).Assembly.GetType("Meitou.Simulation.Carrying.CarrySystem", false)
        ?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .FirstOrDefault(m => m.Name == "Asleep" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(int)
                             && m.ReturnType == typeof(bool));

    public static bool Is(World world, CharacterId who)
    {
        if (asleep is null || !world.Characters.TryResolveNext(who, out int s)) return false;
        try { return asleep.Invoke(null, [world.Characters, s]) is true; }
        catch { return false; }
    }
}
