using System.Reflection;
using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

// These have names and durations only. Meitou keeps and expires them with the character's stats.
internal static class StoryStatuses
{
    sealed record Binding(object Effect, MethodInfo Apply);
    static Binding? grieving, oldWound;

    internal static void Register(ModContext context)
    {
        var add = typeof(ModContext).GetMethod("AddStatusEffect");
        if (add?.GetParameters() is not [{ ParameterType: var type }]) return;
        try
        {
            Configure(type, effect => add.Invoke(context, [effect]));
            context.Log("story statuses: Grieving and Old wound");
        }
        catch (Exception error)
        {
            context.Log($"story statuses: {error.GetBaseException().Message}");
        }
    }

    internal static void Configure(Type type, Action<object> register)
    {
        Reset();
        var constructor = type.GetConstructor([typeof(string), typeof(string)])
            ?? throw new MissingMethodException(type.FullName, ".ctor(string, string)");
        var apply = type.GetMethod("Apply", [typeof(World), typeof(CharacterId), typeof(double), typeof(int)])
            ?? throw new MissingMethodException(type.FullName, "Apply");
        var detail = type.GetProperty("Description");

        Binding Add(string id, string name, string description)
        {
            var effect = constructor.Invoke([id, name]);
            detail?.SetValue(effect, description);
            register(effect);
            return new Binding(effect, apply);
        }

        grieving = Add("twr.grieving", "Grieving", "They kept a keepsake of a fallen squadmate.");
        oldWound = Add("twr.old-wound", "Old wound", "An earlier fight stayed with them.");
    }

    internal static void Reset() => (grieving, oldWound) = (null, null);

    internal static void Grieve(World world, CharacterId actor) => Set(world, actor, grieving, 72, "Grieving");

    internal static void Returned(World world, CharacterId actor) => Set(world, actor, oldWound, 48, "Old wound");

    static void Set(World world, CharacterId actor, Binding? binding, double hours, string name)
    {
        if (binding is null || actor.IsNone) return;
        try
        {
            if (binding.Apply.Invoke(binding.Effect, [world, actor, hours, 1]) is true && PlaytestLog.Enabled)
                PlaytestLog.Event(world, "story status", actor.ToString(), name);
        }
        catch (Exception error) { LwMod.Log($"story status: {name}: {error.GetBaseException().Message}"); }
    }
}
