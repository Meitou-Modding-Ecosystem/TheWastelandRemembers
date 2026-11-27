using System.Linq.Expressions;
using System.Reflection;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

/// <summary>Publishes earned titles for Meitou's actor-specific name display on character panels.</summary>
internal static class CharacterTitleDisplay
{
    static readonly MethodInfo? add = typeof(UiRegistration).GetMethod("AddText");
    static readonly Type? request = add?.GetParameters().SingleOrDefault()?.ParameterType;
    static readonly Type? context = request?.GenericTypeArguments.FirstOrDefault();
    static readonly PropertyInfo? actor = context?.GetProperty("Character"), place = context?.GetProperty("Place"), text = context?.GetProperty("Text");
    static readonly Action? refresh = context?.Assembly.GetType("Meitou.Localisation.ShownText")?
        .GetMethod("Refresh", Type.EmptyTypes)?.CreateDelegate<Action>();
    static readonly Delegate? source = BuildSource();
    static readonly IReadOnlyDictionary<ulong, string> empty = new Dictionary<ulong, string>();
    static volatile IReadOnlyDictionary<ulong, string> shown = empty;
    internal static bool Registered { get; private set; }

    internal static bool Available => source is not null && refresh is not null && LastingActors.Available;
    internal static bool Enabled => Registered && LwMod.Settings.TitlesInCharacterNames;

    internal static void WatchSettings(ModSettingsGroup group) => group.Changed += setting =>
    {
        if (setting.Key == nameof(LwSettings.TitlesInCharacterNames) && !LwMod.Settings.TitlesInCharacterNames) Clear();
    };

    static Delegate? BuildSource()
    {
        try
        {
            if (context is null || request is null || actor?.PropertyType != typeof(ulong) ||
                text?.PropertyType != typeof(string) || place?.PropertyType is not { IsEnum: true } surface ||
                !Enum.TryParse(surface, "Panel", out var panel)) return null;
            var input = Expression.Parameter(context, "context");
            var render = typeof(CharacterTitleDisplay).GetMethod(nameof(NameWithTitle), BindingFlags.Static | BindingFlags.NonPublic)!;
            return Expression.Lambda(request,
                Expression.Condition(Expression.Equal(Expression.Property(input, place), Expression.Constant(panel, surface)),
                    Expression.Call(render, Expression.Property(input, actor), Expression.Property(input, text)),
                    Expression.Constant(null, typeof(string))), input).Compile();
        }
        catch (Exception) { return null; }
    }

    internal static void Register(UiRegistration registration)
    {
        Registered = false;
        if (!Available || !LwMod.Settings.TitlesInCharacterNames) { Clear(); return; }
        try
        {
            add!.Invoke(registration, [source]);
            Registered = true;
        }
        catch (Exception e) { LwMod.Log($"character titles: unavailable ({e.GetBaseException().Message})"); }
    }

    static string? NameWithTitle(ulong id, string name) =>
        LwMod.Settings.TitlesInCharacterNames && shown.TryGetValue(id, out var title) && name.Length + 3 + title.Length <= 80
            ? $"{name} · {title}" : null;

    internal static void Publish(SquadRecord record, List<(CharacterId Id, string Key)> members, World world)
    {
        if (!Enabled)
        {
            Clear();
            return;
        }
        Dictionary<ulong, string>? next = null;
        foreach (var (actor, key) in members)
        {
            if (!record.Members.TryGetValue(key, out var member) || member.Titles.Count == 0) continue;
            ulong id = LastingActors.Grant(world, actor);
            if (id != 0) (next ??= [])[id] = record.TitleName(member.Titles[^1]);
        }

        if (next is null) { Clear(); return; }
        var old = shown;
        if (SameTitles(old, next)) return;
        shown = next;
        refresh!();
    }

    static bool SameTitles(IReadOnlyDictionary<ulong, string> old, Dictionary<ulong, string> next)
    {
        if (old.Count != next.Count) return false;
        foreach (var (id, title) in next)
            if (!old.TryGetValue(id, out var previous) || previous != title) return false;
        return true;
    }

    internal static void Clear()
    {
        if (shown.Count == 0) return;
        shown = empty;
        refresh!();
    }
}
