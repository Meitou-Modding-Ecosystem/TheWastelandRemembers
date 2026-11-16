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

    /// <summary>Offers a moment to Recap; false when Recap is absent, off, refused it, or the link is off.</summary>
    public static bool Moment(string caption, string place, int weight, Vector3? position = null)
    {
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
            add = type.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "AddMoment");
            if (add is null)
            {
                Off("Recap is installed but has no AddMoment call");
                return false;
            }
            parameters = add.GetParameters();
            var unknown = parameters.Where(p => !Fillable(p)).Select(p => $"{p.ParameterType.Name} {p.Name}").ToList();
            if (unknown.Count > 0)
            {
                Off($"Recap's AddMoment has parameters we do not know ({string.Join(", ", unknown)})");
                return false;
            }
            RecordHost.Log($"Recap {type.Assembly.GetName().Version} found: titles and other moments will be offered to it");
            return true;
        }
    }

    static bool Fillable(ParameterInfo p)
    {
        var t = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
        return t == typeof(string) || t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(bool) || t == typeof(Vector3)
            || t.IsEnum || p.HasDefaultValue;
    }

    static object? Arg(ParameterInfo p, string caption, string place, int weight, Vector3? position)
    {
        string name = (p.Name ?? "").ToLowerInvariant();
        var type = p.ParameterType;
        var t = Nullable.GetUnderlyingType(type) ?? type;
        bool nullable = !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

        if (t == typeof(Vector3)) return position is { } v ? v : nullable ? null : p.HasDefaultValue ? p.DefaultValue : Vector3.Zero;
        if (name.Contains("caption") || name.Contains("text")) return caption;
        if (name.Contains("place")) return place;
        if (name.Contains("weight")) return Convert.ChangeType(weight, t);
        if (name.Contains("picture") && t == typeof(bool)) return position is not null;   // a picture only where there is a place to aim at
        if (name.Contains("kind") || name.Contains("source"))
        {
            if (t == typeof(string)) return Source;
            if (t.IsEnum) return EnumValue(t);
        }
        return p.HasDefaultValue ? p.DefaultValue : t.IsValueType ? Activator.CreateInstance(t) : null;
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
