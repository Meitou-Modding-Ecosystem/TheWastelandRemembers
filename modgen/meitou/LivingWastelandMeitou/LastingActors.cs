using System.Reflection;
using System.Linq.Expressions;
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
    static readonly PropertyInfo? savedCold = typeof(Meitou.Simulation.Saving.SavedMember).GetProperty("Cold");
    static readonly PropertyInfo? actorId = typeof(CharacterCold).GetProperty("LastingId");

    internal static bool Available { get; } = actorId is not null && actorId.PropertyType == id &&
        wrap is not null && value?.PropertyType == typeof(ulong) && find?.ReturnType == typeof(CharacterId);

    static readonly Func<World, ulong, CharacterId>? resolve = Bind(static () =>
    {
        var world = Expression.Parameter(typeof(World));
        var assigned = Expression.Parameter(typeof(ulong));
        var call = Expression.Call(Expression.Property(world, registry!), find!, Expression.New(wrap!, assigned));
        return Expression.Lambda<Func<World, ulong, CharacterId>>(call, world, assigned).Compile();
    });

    static readonly Func<World, CharacterId, ulong>? assign = Bind(static () =>
    {
        var world = Expression.Parameter(typeof(World));
        var actor = Expression.Parameter(typeof(CharacterId));
        var lasting = Expression.Call(Expression.Property(world, registry!), grant!, actor);
        return Expression.Lambda<Func<World, CharacterId, ulong>>(Expression.Property(lasting, value!), world, actor).Compile();
    });

    static readonly Func<CharacterCold, ulong>? read = Bind(static () =>
    {
        var actor = Expression.Parameter(typeof(CharacterCold));
        return Expression.Lambda<Func<CharacterCold, ulong>>(Expression.Property(Expression.Property(actor, actorId!), value!), actor).Compile();
    });

    static readonly Func<Meitou.Simulation.Saving.SavedMember, CharacterCold?>? readSaved = Bind(static () =>
    {
        var member = Expression.Parameter(typeof(Meitou.Simulation.Saving.SavedMember));
        return Expression.Lambda<Func<Meitou.Simulation.Saving.SavedMember, CharacterCold?>>(Expression.Property(member, savedCold!), member).Compile();
    });

    static T? Bind<T>(Func<T> compile) where T : Delegate
    {
        if (!Available) return null;
        try { return compile(); }
        catch (Exception) { return null; }
    }

    internal static ulong Grant(World world, CharacterId actor)
    {
        if (!Available) return 0;
        if (assign is not null) return assign(world, actor);
        return grant!.Invoke(registry!.GetValue(world), [actor]) is { } token && value!.GetValue(token) is ulong n ? n : 0UL;
    }

    internal static ulong Of(CharacterCold actor)
    {
        if (!Available) return 0;
        if (read is not null) return read(actor);
        return actorId!.GetValue(actor) is { } token && value!.GetValue(token) is ulong n ? n : 0UL;
    }

    internal static ulong OfSaved(Meitou.Simulation.Saving.SavedMember member)
    {
        if (!Available) return 0;
        var actor = readSaved is not null ? readSaved(member) : savedCold?.GetValue(member) as CharacterCold;
        return actor is not null ? Of(actor) : 0;
    }

    internal static CharacterId Find(World world, ulong assigned)
    {
        if (!Available || assigned == 0) return CharacterId.None;
        if (resolve is not null) return resolve(world, assigned);
        return find!.Invoke(registry!.GetValue(world), [wrap!.Invoke([assigned])]) is CharacterId actor ? actor : CharacterId.None;
    }
}
