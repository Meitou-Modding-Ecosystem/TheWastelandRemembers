using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

internal sealed record RivalOfferChoice(object WorldKey, CharacterId Rival, string Identity, double OfferUntil, CharacterId[] Selected, bool Accept) : SimCommand;

internal static class RivalOffers
{
    internal sealed record Offer(object WorldKey, string Name, string Identity, double Until);
    static IReadOnlyDictionary<CharacterId, Offer> offers = new Dictionary<CharacterId, Offer>();
    static readonly MethodInfo? join = typeof(PopulationSystem).GetMethod("JoinPlayerSquad", [typeof(World), typeof(CharacterId), typeof(int)]);
    static ConstructorInfo? verb;
    static PropertyInfo? target, id, name, selected, tick;
    static MethodInfo? enqueue;
    static string summary = "no world yet";
    static int barredCount;

    internal static bool CanJoin => join?.ReturnType == typeof(bool);
    internal static bool Available => CanJoin && Volatile.Read(ref verb) is not null;

    internal static void Register(ModContext c)
    {
        c.AddDebugCommand(UiLanguage.Text("Wasteland Remembers: rival offers"), _ => Volatile.Read(ref summary), hasField: false);
        if (!CanJoin) c.Log("rival offers: this Meitou does not expose JoinPlayerSquad");
    }

    internal static void Publish(Dictionary<CharacterId, Offer> rows, int barred)
    {
        barredCount = barred;
        Volatile.Write(ref offers, rows);
        Volatile.Write(ref summary, !LwMod.Settings.RecruitRivals ? "off" :
            $"{rows.Count} offers within reach, {barred} rivals barred for a squadmate's death");
    }

    internal static void Forget(CharacterId id, object worldKey)
    {
        var current = Volatile.Read(ref offers);
        if (!current.TryGetValue(id, out var offer) || !ReferenceEquals(offer.WorldKey, worldKey)) return;
        var remaining = new Dictionary<CharacterId, Offer>(current);
        remaining.Remove(id);
        Publish(remaining, barredCount);
    }

    internal static void RegisterUi(object registration)
    {
        if (!CanJoin) return;
        try
        {
            var add = registration.GetType().GetMethod("AddCharacterMenuVerbs");
            if (add is null) return;
            var func = add.GetParameters()[0].ParameterType;
            var context = func.GetGenericArguments()[0];
            var row = func.GetGenericArguments()[1].GetGenericArguments()[0];
            target = context.GetProperty("Target");
            id = target?.PropertyType.GetProperty("Id");
            name = target?.PropertyType.GetProperty("Name");
            selected = context.GetProperty("Selected");
            tick = context.GetProperty("Tick");
            enqueue = context.GetMethod("Enqueue", [typeof(SimCommand)]);
            var ctor = row.GetConstructor([typeof(string), typeof(Action)]);
            if (id?.PropertyType != typeof(CharacterId) || name?.PropertyType != typeof(string) ||
                selected is null || tick?.PropertyType != typeof(long) || enqueue is null || ctor is null) return;
            var ctx = Expression.Parameter(context, "context");
            var rows = Expression.Call(typeof(RivalOffers).GetMethod(nameof(Menu), BindingFlags.Static | BindingFlags.NonPublic)!,
                Expression.Convert(ctx, typeof(object)));
            var cast = Expression.Call(typeof(Enumerable).GetMethod(nameof(Enumerable.Cast))!.MakeGenericMethod(row), rows);
            add.Invoke(registration, [Expression.Lambda(func, cast, ctx).Compile()]);
            Volatile.Write(ref verb, ctor);
        }
        catch (Exception e) { LwMod.Log($"rival offers: menu unavailable ({e.GetBaseException().Message})"); }
    }

    static IEnumerable Menu(object context)
    {
        if (!Available || !LwMod.Settings.Rivals || !LwMod.Settings.RecruitRivals || target!.GetValue(context) is not { } npc ||
            id!.GetValue(npc) is not CharacterId who || !Volatile.Read(ref offers).TryGetValue(who, out var offer) ||
            name!.GetValue(npc) as string != offer.Name || selected!.GetValue(context) is not IEnumerable<CharacterId> picked) return Array.Empty<object>();
        var actors = picked.ToArray();
        if (actors.Length == 0) return Array.Empty<object>();
        object Row(string caption, bool accept)
        {
            Action choose = () => enqueue!.Invoke(context,
                [new RivalOfferChoice(offer.WorldKey, who, offer.Identity, offer.Until, actors, accept) { Tick = (long)tick!.GetValue(context)! }]);
            return verb!.Invoke([caption, choose]);
        }
        return new[] { Row("Take them in", true), Row("Turn them away", false) };
    }

    internal static bool Join(PopulationSystem? population, World world, CharacterId who, int squad) =>
        population is not null && CanJoin && join!.Invoke(population, [world, who, squad]) is true;
}
