using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class WallConstructionWatch
{
    static readonly Type? observer = typeof(World).Assembly.GetType("Meitou.Simulation.Items.IItemUseObserver");
    static readonly Type? payload = typeof(World).Assembly.GetType("Meitou.Simulation.Items.ItemUsed");
    static readonly System.Reflection.PropertyInfo? kind = payload?.GetProperty("Kind"), site = payload?.GetProperty("Site");
    static readonly object? buildingMaterial = kind?.PropertyType is { IsEnum: true } type &&
        Enum.TryParse(type, "BuildingMaterial", out var value) ? value : null;

    internal static Type? Build() => observer is not null && buildingMaterial is not null && site?.PropertyType == typeof(string)
        ? ObserverEmit.Build(observer, "ItemUsedUp", typeof(WallConstructionWatch).GetMethod(nameof(Used))!, "WallConstructionSystem")
        : null;

    internal static void Register(ModContext context)
    {
        if (Build() is { } type)
            context.AddSystem("lw-wall-construction", _ => (ITickSystem)Activator.CreateInstance(type)!, SystemAnchor.End);
    }

    public static void Used(World world, object use)
    {
        if (!LwMod.Settings.RememberOutposts || buildingMaterial?.Equals(kind!.GetValue(use)) != true ||
            site?.GetValue(use) is not string id) return;
        (OutpostMemories.For(world) ?? world.Systems.OfType<OutpostMemories>().FirstOrDefault())?.WallMaterialDelivered(world, id);
    }
}
