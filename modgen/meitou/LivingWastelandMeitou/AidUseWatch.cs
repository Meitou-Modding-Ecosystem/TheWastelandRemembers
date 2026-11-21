using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class AidUseWatch
{
    static readonly Type assemblyType = typeof(World);
    static readonly Type? observer = assemblyType.Assembly.GetType("Meitou.Simulation.Items.IItemUseObserver");
    static readonly Type? payload = assemblyType.Assembly.GetType("Meitou.Simulation.Items.ItemUsed");
    static readonly System.Reflection.PropertyInfo? kind = payload?.GetProperty("Kind");
    static readonly System.Reflection.PropertyInfo? user = payload?.GetProperty("User");
    static readonly System.Reflection.PropertyInfo? amount = payload?.GetProperty("Amount");
    static readonly System.Reflection.PropertyInfo? finished = payload?.GetProperty("Finished");
    static readonly object? firstAid = kind?.PropertyType is { IsEnum: true } type && Enum.TryParse(type, "FirstAid", out var value) ? value : null;

    internal static void Register(ModContext context)
    {
        if (observer is null || firstAid is null || user?.PropertyType != typeof(CharacterId) ||
            amount?.PropertyType != typeof(float) || finished?.PropertyType != typeof(bool)) return;
        if (ObserverEmit.Build(observer, "ItemUsedUp", typeof(AidUseWatch).GetMethod(nameof(Used))!, "AidUseSystem") is { } type)
            context.AddSystem("lw-aid-use", _ => (ITickSystem)Activator.CreateInstance(type)!, SystemAnchor.End);
    }

    public static void Used(World world, object use)
    {
        if (firstAid is null || !firstAid.Equals(kind!.GetValue(use)) || user!.GetValue(use) is not CharacterId id
            || !world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { IsPlayer: true } medic
            || amount!.GetValue(use) is not float charges || !float.IsFinite(charges) || charges <= 0) return;
        var deeds = LwDeeds.For(world) ?? world.Systems.OfType<LwDeedsSystem>().FirstOrDefault()?.Deeds;
        if (deeds is not null) deeds.Record.UsedAid(deeds.Who(medic), charges, finished!.GetValue(use) is true);
    }
}
