using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

internal enum PrisonerAction : byte { LawfulDelivery, AcceptRansom, CompleteRansom }

internal sealed record PrisonerChoice(object Session, int Episode, CharacterId Target, CharacterId[] Party, PrisonerAction Action, string Issuer) : SimCommand;

/// <summary>Menu rows are published from the simulation. A click only queues a choice for that session.</summary>
internal static class PrisonerMenu
{
    internal sealed record Row(object Session, int Episode, CharacterId Target, string Name, string Caption, PrisonerAction Action, string Issuer = "");
    static Row[] lawful = [], ransom = [];
    internal static bool Available { get; private set; }

    internal static void Lawful(Row[] rows) => Volatile.Write(ref lawful, rows);
    internal static void Ransom(Row[] rows) => Volatile.Write(ref ransom, rows);

    internal static void Register(object registration)
    {
        try
        {
            if (registration.GetType().GetMethod("AddCharacterMenuVerbs") is not { } add) return;
            var func = add.GetParameters()[0].ParameterType;
            var context = func.GetGenericArguments()[0];
            var verb = func.GetGenericArguments()[1].GetGenericArguments()[0];
            var target = context.GetProperty("Target");
            var id = target?.PropertyType.GetProperty("Id");
            var name = target?.PropertyType.GetProperty("Name");
            var selected = context.GetProperty("Selected");
            var tick = context.GetProperty("Tick");
            var enqueue = context.GetMethod("Enqueue", [typeof(SimCommand)]);
            var ctor = verb.GetConstructor([typeof(string), typeof(Action)]);
            if (target is null || ctor is null || id?.PropertyType != typeof(CharacterId) || name?.PropertyType != typeof(string) ||
                selected is null || tick?.PropertyType != typeof(long) || enqueue is null) return;

            IEnumerable Make(object menu)
            {
                if (!LwMod.Settings.PrisonerDeals || !LwMod.Settings.PrisonerCases || target.GetValue(menu) is not { } actor ||
                    id.GetValue(actor) is not CharacterId who || name.GetValue(actor) is not string shown ||
                    selected.GetValue(menu) is not IEnumerable<CharacterId> people) return Array.Empty<object>();
                var party = people.ToArray();
                if (party.Length == 0) return Array.Empty<object>();
                long at = (long)tick.GetValue(menu)!;
                return Volatile.Read(ref lawful).Concat(Volatile.Read(ref ransom))
                    .Where(r => r.Target == who && r.Name == shown)
                    .Select(r => ctor.Invoke([r.Caption, (Action)(() => enqueue.Invoke(menu,
                        [new PrisonerChoice(r.Session, r.Episode, who, party, r.Action, r.Issuer) { Tick = at }]))])).ToArray();
            }

            var arg = Expression.Parameter(context, "menu");
            var rows = Expression.Invoke(Expression.Constant((Func<object, IEnumerable>)Make), Expression.Convert(arg, typeof(object)));
            var cast = Expression.Call(typeof(Enumerable).GetMethod(nameof(Enumerable.Cast))!.MakeGenericMethod(verb), rows);
            add.Invoke(registration, [Expression.Lambda(func, cast, arg).Compile()]);
            Available = true;
        }
        catch (Exception e) { LwMod.Log($"prisoner deals: menu unavailable ({e.GetBaseException().Message})"); }
    }
}
