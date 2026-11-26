using System.Linq.Expressions;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

/// <summary>Marks established TWR actors using Meitou's optional saved character tags.</summary>
internal static class OwnedCharacterTags
{
    const string Owner = "LivingWastelandMeitou";
    static readonly Func<World, CharacterId, string?>? read = Bind<Func<World, CharacterId, string?>>("Get");
    static readonly Func<World, CharacterId, string, bool>? write = Bind<Func<World, CharacterId, string, bool>>("Set");

    internal static bool Available => read is not null && write is not null;

    internal static void Mark(World world, CharacterId actor, string prefix, string identity)
    {
        if (read is not null && write is not null && read(world, actor) is null)
            write(world, actor, prefix + identity);
    }

    static T? Bind<T>(string method) where T : Delegate
    {
        try
        {
            if (typeof(World).GetProperty("CharacterTags") is not { } registry) return null;
            bool setter = method == "Set";
            Type[] types = setter ? [typeof(CharacterId), typeof(string), typeof(string)] : [typeof(CharacterId), typeof(string)];
            if (registry.PropertyType.GetMethod(method, types) is not { } call ||
                call.ReturnType != (setter ? typeof(bool) : typeof(string))) return null;

            var world = Expression.Parameter(typeof(World), "world");
            var actor = Expression.Parameter(typeof(CharacterId), "actor");
            var owner = Expression.Constant(Owner);
            var service = Expression.Property(world, registry);
            if (!setter) return Expression.Lambda<T>(Expression.Call(service, call, actor, owner), world, actor).Compile();
            var tag = Expression.Parameter(typeof(string), "tag");
            return Expression.Lambda<T>(Expression.Call(service, call, actor, owner, tag), world, actor, tag).Compile();
        }
        catch (Exception) { return null; }
    }
}
