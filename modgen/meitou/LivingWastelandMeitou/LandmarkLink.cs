// Landmark Banners (a Meitou code mod by another author: banners by the doors of bars, shops, doctors, police stations...), when it is
// installed: it works out what each town building is from who lives there. The Atlas page of a town the squad has been to lists those places
// ("In town: a bar, a doctor, a police station"). Read by reflection (its LandmarkBanners.Plan.Build, called once per world); nothing of its
// code is in ours, and if it is absent or changes shape the Atlas simply leaves the line out.
using System.Collections;
using System.Reflection;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public static class LandmarkLink
{
    static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["Bar"] = "a bar", ["Food"] = "a food shop", ["Trader"] = "a trader", ["Weapons"] = "a weapon smith", ["Crossbows"] = "a crossbow shop",
        ["Armour"] = "an armour smith", ["Clothing"] = "a clothing shop", ["Tools"] = "a tool shop", ["Robotics"] = "a robotics shop",
        ["Doctor"] = "a doctor", ["Books"] = "a book shop", ["Drugs"] = "a drug shop", ["Police"] = "a police station",
        ["Slaves"] = "a slave market", ["Animals"] = "an animal trader", ["Thieves"] = "a thieves' guild",
    };

    static PopulationData? builtFor;
    static Dictionary<string, List<string>> byTown = new(StringComparer.OrdinalIgnoreCase);
    static bool failed;

    /// <summary>"In town: a bar, a doctor and a police station." for a town, or null (mod absent, nothing known, or it failed once).</summary>
    public static string? Line(PopulationData? data, string town)
    {
        if (data is null || failed) return null;
        lock (Words)
        {
            if (!ReferenceEquals(builtFor, data)) { builtFor = data; byTown = Build(data); }
            if (!byTown.TryGetValue(town, out var kinds) || kinds.Count == 0) return null;
            var list = kinds.Distinct().OrderBy(k => Array.IndexOf(Order, k)).Select(k => Words[k]).ToList();
            return "In town: " + (list.Count == 1 ? list[0] : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1]) + ".";
        }
    }

    static readonly string[] Order = ["Bar", "Doctor", "Police", "Trader", "Food", "Weapons", "Crossbows", "Armour", "Clothing", "Tools", "Robotics", "Books",
        "Drugs", "Animals", "Slaves", "Thieves"];

    static Dictionary<string, List<string>> Build(PopulationData data)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("LandmarkBanners.Plan", false)).FirstOrDefault(t => t is not null);
            var build = plan?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Build" && m.GetParameters() is { Length: >= 1 } ps && ps[0].ParameterType == typeof(PopulationData));
            if (build is null) return map;                         // not installed (or a version without it): no line
            var args = build.GetParameters().Select((p, i) => i == 0 ? data : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            if (build.Invoke(null, args) is not IDictionary marks) return map;
            foreach (var mark in marks.Values)
            {
                var t = mark?.GetType();
                if (t?.GetProperty("Town")?.GetValue(mark) is not string town || t.GetProperty("Kind")?.GetValue(mark)?.ToString() is not { } kind
                    || !Words.ContainsKey(kind)) continue;
                town = TownNames.Clean(town);
                if (!map.TryGetValue(town, out var list)) map[town] = list = [];
                list.Add(kind);
            }
            LwMod.Log($"landmarks: Landmark Banners found, {map.Count} towns' places known to the Atlas");
        }
        catch (Exception e)
        {
            failed = true;
            LwMod.Log($"landmarks: reading Landmark Banners failed, the Atlas leaves its places out ({e.GetBaseException().Message})");
        }
        return map;
    }
}
