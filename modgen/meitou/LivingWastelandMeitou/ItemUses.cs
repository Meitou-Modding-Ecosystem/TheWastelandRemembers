// Right clicks on the mod's items (Meitou 0.20.2+: ModContext.AddItemUse; older Meitou has none and the items behave as before). A
// keepsake is turned over: the holder says a line for the fallen and its story is written again (who holds it now, avenged since?). A
// warrant is read: which town sent it (the item's mod tag) and how that town feels about the squad now. Registered by reflection so the
// mod still loads on Meitou 0.17.4 to 0.20.1.
using System.Linq.Expressions;
using System.Reflection;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace LivingWasteland.MeitouMod;

public static class ItemUses
{
    static readonly PropertyInfo? modTag = typeof(ItemInstance).GetProperty("ModTag");

    /// <summary>Registers a right click for each record where the engine can. Never throws; logs what happened.</summary>
    public static void Register(ModContext c, IEnumerable<string> records)
    {
        try
        {
            var method = c.GetType().GetMethod("AddItemUse", BindingFlags.Public | BindingFlags.Instance);
            if (method is null) { c.Log("item uses: this Meitou has no right-click item uses (0.20.2 and later do)"); return; }
            var handler = Handler(method.GetParameters()[1].ParameterType);
            if (handler is null) { c.Log("item uses: the engine's item use type changed; none added"); return; }
            int n = 0;
            foreach (var r in records.Where(r => !string.IsNullOrEmpty(r)).Distinct()) { method.Invoke(c, [r, handler]); n++; }
            c.Log($"item uses: right click added to {n} items");
        }
        catch (Exception e) { c.Log($"item uses: none added ({e.GetBaseException().Message})"); }
    }

    /// <summary>A delegate of the engine's handler type that calls <see cref="Use"/> with the use's world, user and item.</summary>
    public static Delegate? Handler(Type delegateType)
    {
        var invoke = delegateType.GetMethod("Invoke");
        if (invoke?.GetParameters() is not [{ ParameterType: var useType }] || invoke.ReturnType != typeof(bool)) return null;
        var use = Expression.Parameter(useType, "use");
        var world = useType.GetProperty("World"); var user = useType.GetProperty("User"); var item = useType.GetProperty("Item");
        if (world?.PropertyType != typeof(World) || user?.PropertyType != typeof(CharacterId) || item?.PropertyType != typeof(ItemInstance)) return null;
        var call = Expression.Call(typeof(ItemUses).GetMethod(nameof(Use))!,
            Expression.Property(use, world), Expression.Property(use, user), Expression.Property(use, item));
        return Expression.Lambda(delegateType, call, use).Compile();
    }

    /// <summary>True when the click was used (the item stays where it is); false lets the click equip or move it as usual.</summary>
    public static bool Use(World world, CharacterId user, ItemInstance item)
    {
        try
        {
            string holder = world.Characters.Cold(user.Slot)?.Name ?? "Someone";
            if (item.Record == Trophies.Warrant)
            {
                string? town = Tag(item);
                bool crewOut = town is not null && WastelandApi.Hunts().Any(h => (string)h["Kind"] == "bounty crew" && (string)h["Name"] == town);
                var wanted = WastelandApi.HeatTowns().Where(t => (double)t["Heat"] >= 14).Select(t => (string)t["Town"]).ToList();
                world.Post(WarrantText.Read(town, town is null ? "quiet" : WastelandApi.HeatTier(town), crewOut, wanted));
                LwMod.Log($"item uses: {holder} read a warrant from {town ?? "(no town)"}");
                return true;
            }
            if (KeepsakeBook.For(world)?.Remember(world, item, holder) is not { } said) return false;
            world.Bark(user, said.Bark);
            world.Post(said.Message);
            LwMod.Log($"item uses: {holder} turned over {item.Record}: \"{said.Bark}\"");
            return true;
        }
        catch (Exception e) { LwMod.Log($"item uses: {e.GetBaseException().Message}"); return false; }
    }

    /// <summary>Tags an item (Meitou 0.20.2+ keeps a mod's tag with the item and saves it); false where the engine has no tags.</summary>
    public static bool SetTag(ItemInstance item, string tag)
    {
        if (modTag?.GetSetMethod(true) is not { } set) return false;
        set.Invoke(item, [tag]);
        return true;
    }

    public static string? Tag(ItemInstance item) => modTag?.GetValue(item) as string is { Length: > 0 } t ? t : null;
}
