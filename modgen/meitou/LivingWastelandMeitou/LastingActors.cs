using System.Reflection;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

/// <summary>Optional Meitou lasting character IDs, bound without a reference above the 0.17.3 floor.</summary>
internal static class LastingActors
{
    static readonly PropertyInfo? registry = typeof(World).GetProperty("LastingIds");
    static readonly MethodInfo? grant = registry?.PropertyType.GetMethod("GetOrGive", [typeof(CharacterId)]);
    static readonly Type? id = grant?.ReturnType;
    static readonly PropertyInfo? value = id?.GetProperty("Value");
    static readonly ConstructorInfo? wrap = id?.GetConstructor([typeof(ulong)]);
    static readonly MethodInfo? find = id is null ? null : registry?.PropertyType.GetMethod("Find", [id]);
    static readonly PropertyInfo? actorId = typeof(CharacterCold).GetProperty("LastingId");

    internal static bool Available { get; } = actorId is not null && actorId.PropertyType == id &&
        wrap is not null && value?.PropertyType == typeof(ulong) && find?.ReturnType == typeof(CharacterId);

    internal static ulong Grant(World world, CharacterId actor) => Available &&
        grant!.Invoke(registry!.GetValue(world), [actor]) is { } token && value!.GetValue(token) is ulong n ? n : 0UL;

    internal static ulong Of(CharacterCold actor) => Available && actorId!.GetValue(actor) is { } token &&
        value!.GetValue(token) is ulong n ? n : 0UL;

    internal static CharacterId Find(World world, ulong assigned) => Available && assigned != 0 &&
        find!.Invoke(registry!.GetValue(world), [wrap!.Invoke([assigned])]) is CharacterId actor ? actor : CharacterId.None;
}
