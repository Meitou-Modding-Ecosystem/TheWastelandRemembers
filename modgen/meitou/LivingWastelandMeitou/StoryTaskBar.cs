using System.Reflection;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

// The client exposes one transient mod bar per character. Give up if it is already occupied.
internal sealed class StoryTaskBar(CharacterCold actor, object value)
{
    static readonly PropertyInfo? slot = typeof(CharacterCold).GetProperty("ModTaskBar");
    static readonly ConstructorInfo? make = slot is null ? null :
        Nullable.GetUnderlyingType(slot.PropertyType)?.GetConstructor([typeof(string), typeof(float)]);
    static readonly PropertyInfo? picking = typeof(CharacterCold).GetProperty("PickingLock");
    static readonly PropertyInfo? forcing = typeof(CharacterCold).GetProperty("ForcingLock");
    static bool failed;

    internal static bool Available => make is not null && !failed;

    internal static StoryTaskBar? TryShow(CharacterCold actor, string text)
    {
        if (!Available || actor.InCombat || actor.IsImprisoned) return null;
        try
        {
            if (picking?.GetValue(actor) is not null || forcing?.GetValue(actor) is not null ||
                slot!.GetValue(actor) is not null) return null;
            var bar = make!.Invoke([text, .5f]);
            slot.SetValue(actor, bar);
            return new StoryTaskBar(actor, bar);
        }
        catch (Exception error) { Fail(error); return null; }
    }

    internal void Clear()
    {
        try
        {
            if (Equals(slot!.GetValue(actor), value)) slot.SetValue(actor, null);
        }
        catch (Exception error) { Fail(error); }
    }

    static void Fail(Exception error)
    {
        if (failed) return;
        failed = true;
        LwMod.Log($"task bars: {error.GetBaseException().Message}");
    }
}
