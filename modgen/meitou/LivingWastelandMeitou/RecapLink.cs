// Moments for Recap ("Previously on your adventure", BeatTheCount's Meitou mod), when it is installed. Recap 1.2 added a public call for other mods,
// Recap.RecapApi.AddMoment(caption, place, position, weight, picture, kind), returning whether it kept the moment. Its author's notes: reflection
// works on every Meitou version, the call works from any thread and never throws, and Recap limits other mods' moments (24 a session, each once,
// at most half its pages). We use reflection, so this mod loads the same with Recap 1.1, 1.2 or none at all.
//
// The call's parameters are filled by name and type, so a small change in Recap's signature (a nullable position, an enum kind) still works;
// anything we cannot fill switches the link off with one log line. We send only what Recap cannot see: titles, last stands, a town marking
// the player as wanted or hunted, bounty hunters hired and wiped out, outlaw reprisals sent and cut down, an outlaw's price grown to a fortune.
// Named kills, lost limbs and fights Recap tells itself.
using System.Numerics;
using System.Reflection;

namespace LivingWasteland.MeitouMod;

public static class RecapLink
{
    /// <summary>Whether to send moments at all (a settings switch in each mod).</summary>
    public static Func<bool> Enabled = () => true;

    /// <summary>The value for a "kind" parameter that takes text: names who sent the moment.</summary>
    public static string Source = "LivingWasteland";

    static readonly object gate = new();
    static bool looked, off;
    static MethodInfo? add;
    static ParameterInfo[] parameters = [];
    static int sent;

    static object? squadAt;      // a boxed Vector3, written once a game second by the squad record's look (Notoriety.cs)

    /// <summary>Where the squad is now (a standing member): the place of a moment that names none.</summary>
    public static void SquadIsAt(Vector3 at) => Volatile.Write(ref squadAt, at);

    internal static void ForgetSquadPlace() => Volatile.Write(ref squadAt, null);

    /// <summary>Offers a moment to Recap; false when Recap is absent, off, refused it, or the link is off. With no position, where the squad is
    /// now: Recap takes a picture only of a moment with a place (seen in play: "The bounty hunters Squin hired were wiped out." had none).</summary>
    public static bool Moment(string caption, string place, int weight, Vector3? position = null)
    {
        position ??= Volatile.Read(ref squadAt) as Vector3?;
        try
        {
            if (!Enabled() || !Find()) return false;
            var args = parameters.Select(p => Arg(p, caption, place, weight, position)).ToArray();
            bool kept = add!.Invoke(null, args) is true;
            if (kept && Interlocked.Increment(ref sent) == 1) RecordHost.Log("Recap: our first moment is in this session's story");
            return kept;
        }
        catch (Exception e)
        {
            Off($"calling Recap failed ({e.GetBaseException().Message})");
            return false;
        }
    }

    static bool Find()
    {
        lock (gate)
        {
            if (looked) return !off && add is not null;
            looked = true;
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Recap.RecapApi", throwOnError: false))
                .FirstOrDefault(t => t is not null);
            if (type is null) return false;     // Recap not installed, or older than 1.2: nothing to say
            var selected = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "AddMoment" && m.ReturnType == typeof(bool) && !m.IsGenericMethod)
                .Select(m => (Method: m, Args: m.GetParameters()))
                .Where(x => x.Args.Any(p => FieldOf(p) == Field.Caption) && x.Args.All(p => FieldOf(p) != Field.Unknown || p.HasDefaultValue))
                .OrderByDescending(x => x.Args.Count(p => FieldOf(p) != Field.Unknown))
                .ThenBy(x => x.Args.Length)
                .FirstOrDefault();
            if (selected.Method is null)
            {
                Off("Recap has no compatible public AddMoment overload");
                return false;
            }
            add = selected.Method;
            parameters = selected.Args;
            RecordHost.Log($"Recap {type.Assembly.GetName().Version} found: titles and other moments will be offered to it");
            return true;
        }
    }

    enum Field { Unknown, Caption, Place, Position, Weight, Picture, Source }

    static Field FieldOf(ParameterInfo p)
    {
        var t = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
        var name = (p.Name ?? "").ToLowerInvariant();
        if (t == typeof(string) && (name.Contains("caption") || name.Contains("text"))) return Field.Caption;
        if (t == typeof(string) && name.Contains("place")) return Field.Place;
        if (t == typeof(Vector3) && (name.Contains("position") || name is "at" or "point")) return Field.Position;
        if ((t == typeof(int) || t == typeof(float) || t == typeof(double)) && name.Contains("weight")) return Field.Weight;
        if (t == typeof(bool) && name.Contains("picture")) return Field.Picture;
        if ((t.IsEnum || t == typeof(string)) && (name.Contains("kind") || name.Contains("source"))) return Field.Source;
        return Field.Unknown;
    }

    static object? Arg(ParameterInfo p, string caption, string place, int weight, Vector3? position)
    {
        var type = p.ParameterType;
        var underlying = Nullable.GetUnderlyingType(type);
        var t = underlying ?? type;
        return FieldOf(p) switch
        {
            Field.Caption => caption,
            Field.Place => place,
            Field.Position => position is { } at ? at : underlying is not null ? null :
                p.HasDefaultValue ? p.DefaultValue : Vector3.Zero,
            Field.Weight => Convert.ChangeType(weight, t),
            Field.Picture => position is not null,
            Field.Source => t.IsEnum ? EnumValue(t) : Source,
            _ => p.DefaultValue,
        };
    }

    static object EnumValue(Type t)
    {
        foreach (var want in new[] { "Custom", "Mod", "Other", "External" })
            if (Enum.GetNames(t).FirstOrDefault(n => n.Equals(want, StringComparison.OrdinalIgnoreCase)) is { } hit) return Enum.Parse(t, hit);
        return Activator.CreateInstance(t)!;
    }

    static void Off(string why)
    {
        off = true;
        RecordHost.Log($"Recap link off: {why}");
    }
}
