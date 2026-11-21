using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public static class TownSwitchWatch
{
    static readonly Type? observer = typeof(PopulationSystem).Assembly.GetType("Meitou.Simulation.Population.ITownSwitchObserver");
    static readonly Type? payload = typeof(PopulationSystem).Assembly.GetType("Meitou.Simulation.Population.TownSwitch");
    static readonly System.Reflection.PropertyInfo? site = payload?.GetProperty("Site");
    static readonly System.Reflection.PropertyInfo? from = payload?.GetProperty("From");

    internal static bool Available => observer is not null && site?.PropertyType == typeof(int) && from?.PropertyType == typeof(string);

    internal static void Register(ModContext context)
    {
        if (!Available || ObserverEmit.Build(observer!, "TownSwitched",
                typeof(TownSwitchWatch).GetMethod(nameof(Changed))!, "TownSwitchSystem") is not { } type) return;
        context.AddSystem("lw-town-switch", _ => (ITickSystem)Activator.CreateInstance(type)!, SystemAnchor.End);
    }

    public static void Changed(World world, object change)
    {
        if (site?.GetValue(change) is not int index) return;
        var news = NewsSystem.For(world) ?? world.Systems.OfType<NewsSystem>().FirstOrDefault();
        news?.TownChanged(world, index, from?.GetValue(change) as string);
    }
}
