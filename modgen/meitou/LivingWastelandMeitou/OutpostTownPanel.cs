using System.Linq.Expressions;
using System.Reflection;
using Meitou.Game.Ui;

namespace LivingWasteland.MeitouMod;

/// <summary>One optional, read-only history line on Meitou's outpost HUD.</summary>
internal static class OutpostTownPanel
{
    static readonly MethodInfo? add = typeof(UiRegistration).GetMethod("AddTownPanelLines");
    internal static bool Available => add is not null;

    internal static bool Register(UiRegistration registration, Action<string> log)
    {
        try
        {
            if (add is null) return false;
            var callback = add.GetParameters().Single().ParameterType;
            var types = callback.GetGenericArguments();
            var line = types[1].GetGenericArguments().Single();
            var ctor = line.GetConstructor([typeof(string), typeof(string), typeof(string), typeof(Action)])
                ?? throw new MissingMethodException(line.FullName, ".ctor");
            var context = Expression.Parameter(types[0], "context");
            var town = Expression.Property(context, "Town");
            var status = Expression.Call(typeof(OutpostMemories).GetMethod(nameof(OutpostMemories.StatusInPanel),
                    BindingFlags.Static | BindingFlags.NonPublic)!,
                Expression.Property(context, "Owned"), Expression.Property(town, "Number"),
                Expression.Property(town, "Name"), Expression.Property(town, "Owner"));
            var rows = Expression.Switch(status, MakeRows(null),
                Expression.SwitchCase(MakeRows("Recorded"), Expression.Constant(OutpostMemories.PanelStatus.Recorded)),
                Expression.SwitchCase(MakeRows("First defence recorded"), Expression.Constant(OutpostMemories.PanelStatus.Defended)));
            add.Invoke(registration, [Expression.Lambda(callback, rows, context).Compile()]);
            return true;

            Expression MakeRows(string? value)
            {
                var entries = Array.CreateInstance(line, value is null ? 0 : 1);
                if (value is not null)
                    entries.SetValue(ctor.Invoke(["Outpost history", value, "See the Atlas for this outpost's story.", null]), 0);
                return Expression.Constant(entries, types[1]);
            }
        }
        catch (Exception e)
        {
            log($"outpost history: town panel not added ({e.GetBaseException().Message})");
            return false;
        }
    }
}
