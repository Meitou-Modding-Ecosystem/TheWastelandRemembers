using System.Linq.Expressions;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

/// <summary>Marks established TWR actors using Meitou's optional saved character tags.</summary>
internal static class OwnedCharacterTags
{
    const string Owner = "LivingWastelandMeitou";
    internal const string Rival = Owner + ".rival";
    internal const string Outlaw = Owner + ".outlaw";
    internal const string Kindness = Owner + ".kindness";
    internal const string Keepsake = Owner + ".keepsake";
    static readonly System.Reflection.PropertyInfo? registry = typeof(World).GetProperty("CharacterTags");
    static readonly Func<World, CharacterId, string, string?>? read = Bind<Func<World, CharacterId, string, string?>>("Get");
    static readonly Func<World, CharacterId, string, string, bool>? write = Bind<Func<World, CharacterId, string, string, bool>>("Set");
    static readonly Func<World, string, string, ulong>? find = BindUnique();

    internal static bool Available => read is not null && write is not null;
    internal static bool PreviewAvailable => Available && find is not null && LastingActors.Available;
    internal static bool Preview => PreviewAvailable && LwMod.Settings.SavedCharacterTags;

    internal static void Mark(World world, CharacterId actor, string prefix, string identity)
    {
        if (read is not null && write is not null && read(world, actor, Owner) is null)
            write(world, actor, Owner, prefix + identity);
    }

    internal static bool MarkRole(World world, CharacterId actor, string role, string identity) =>
        Preview && (read!(world, actor, role) == identity || write!(world, actor, role, identity));

    // FindIds includes an off-screen actor. An ambiguous result is no proof of identity.
    internal static ulong FindRole(World world, string role, string identity) => Preview ? find!(world, role, identity) : 0;

    static T? Bind<T>(string method) where T : Delegate
    {
        try
        {
            if (registry is null) return null;
            bool setter = method == "Set";
            Type[] types = setter ? [typeof(CharacterId), typeof(string), typeof(string)] : [typeof(CharacterId), typeof(string)];
            if (registry.PropertyType.GetMethod(method, types) is not { } call ||
                call.ReturnType != (setter ? typeof(bool) : typeof(string))) return null;

            var world = Expression.Parameter(typeof(World), "world");
            var actor = Expression.Parameter(typeof(CharacterId), "actor");
            var owner = Expression.Parameter(typeof(string), "owner");
            var service = Expression.Property(world, registry);
            if (!setter) return Expression.Lambda<T>(Expression.Call(service, call, actor, owner), world, actor, owner).Compile();
            var tag = Expression.Parameter(typeof(string), "tag");
            return Expression.Lambda<T>(Expression.Call(service, call, actor, owner, tag), world, actor, owner, tag).Compile();
        }
        catch (Exception) { return null; }
    }

    static Func<World, string, string, ulong>? BindUnique()
    {
        try
        {
            var method = registry?.PropertyType.GetMethod("FindIds", [typeof(string), typeof(string)]);
            if (method?.ReturnType.GetProperty("Count") is not { PropertyType: var count } || count != typeof(int) ||
                method.ReturnType.GetProperty("Item") is not { } item ||
                item.PropertyType.GetProperty("Value") is not { PropertyType: var value } raw || value != typeof(ulong)) return null;
            var world = Expression.Parameter(typeof(World), "world");
            var owner = Expression.Parameter(typeof(string), "owner");
            var tag = Expression.Parameter(typeof(string), "tag");
            var ids = Expression.Variable(method.ReturnType, "ids");
            var get = Expression.Call(Expression.Property(world, registry!), method, owner, tag);
            return Expression.Lambda<Func<World, string, string, ulong>>(Expression.Block([ids],
                Expression.Assign(ids, get),
                Expression.Condition(Expression.Equal(Expression.Property(ids, "Count"), Expression.Constant(1)),
                    Expression.Property(Expression.MakeIndex(ids, item, [Expression.Constant(0)]), raw),
                    Expression.Constant(0UL))), world, owner, tag).Compile();
        }
        catch (Exception) { return null; }
    }
}
