// What the Squad record window needs from the game's input that the mod UI API does not give (shared by the Living Wasteland helper and
// Squad Record; installed with the hotkey, RecordHotkey.cs):
// * The mouse, for the Settings tab's hover descriptions: a Harmony prefix on the UI painter's Draw (Meitou.Game...UiPainter.Draw(controls,
//   width, height, mouse, ...)), the call that already decides which button is under the mouse, keeps the last real position (the loading
//   screen passes -1, -1). Read only; the argument is found by its type, so a reordered signature still works.
// * Typing, for the Settings tab's search box: while the box is active, a Harmony prefix on PlayerInterface.UiKey takes letters, digits,
//   space and backspace for the query before anything else sees them (so "i" does not open the inventory); Enter or Esc ends typing, Esc
//   going no further. Camera keys the game reads as held (WASD, Q, E) may still nudge the camera while typing.
// Anything not found switches that part off with one log line: no hover boxes (clicking a setting's name still shows its description), or
// no typing (the search box then says so).
using System.Numerics;
using System.Reflection;
using HarmonyLib;

namespace LivingWasteland.MeitouMod;

public static class UiInput
{
    static float mx = -1, my = -1;
    public static bool HasMouse { get; private set; }
    public static Vector2 Mouse => new(mx, my);

    public static volatile bool Typing;
    public static bool CanType { get; private set; }
    static readonly object gate = new();
    static string query = "";
    public static string Query { get { lock (gate) return query; } set { lock (gate) query = value ?? ""; } }

    /// <summary>Patches the painter (mouse) and the key handler (typing); each part that fails is off with a log line.</summary>
    public static void Install(Harmony harmony, Action<string> log)
    {
        try
        {
            var draw = Painter()?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "Draw" && m.GetParameters().Any(p => p.ParameterType == typeof(Vector2)));
            if (draw is null) log("settings hover: the UI painter was not found; hover descriptions are off (click a setting's name instead)");
            else harmony.Patch(draw, prefix: new HarmonyMethod(typeof(UiInput).GetMethod(nameof(DrawPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
        }
        catch (Exception e) { log($"settings hover: patching failed ({e.GetBaseException().Message}); hover descriptions are off"); }

        try
        {
            if (RecordHotkey.UiKeyMethod() is { } ui)
            {
                harmony.Patch(ui, prefix: new HarmonyMethod(typeof(UiInput).GetMethod(nameof(KeyPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                CanType = true;
            }
            else log("settings search: the key handler was not found; typing in the search box is off");
        }
        catch (Exception e) { log($"settings search: patching failed ({e.GetBaseException().Message}); typing in the search box is off"); }
    }

    /// <summary>The UI painter's type: by its name in the current source, else any type of that name (a listing that cannot load every type
    /// still gives the ones it could).</summary>
    static Type? Painter()
    {
        var asms = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var a in asms)
            if (a.GetType("Meitou.Game.Rendering.Ui.UiPainter", false) is { } t) return t;
        foreach (var a in asms)
        {
            if (a.GetName().Name is not { } n || !n.StartsWith("Meitou", StringComparison.Ordinal)) continue;
            Type?[] types;
            try { types = a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            catch { continue; }
            if (types.FirstOrDefault(t => t?.Name == "UiPainter") is { } found) return found;
        }
        return null;
    }

    static void DrawPrefix(object[] __args)
    {
        foreach (var a in __args)
            if (a is Vector2 v)
            {
                if (v.X >= 0 && v.Y >= 0) { mx = v.X; my = v.Y; HasMouse = true; }
                return;
            }
    }

    /// <summary>False (the key is ours, the game skips it) while the search box takes typing.</summary>
    static bool KeyPrefix(object key, bool ctrl, ref bool __result)
    {
        if (!Typing || ctrl || !StatsUi.open) return true;
        string k = key?.ToString() ?? "";
        lock (gate)
        {
            if (k.Length == 1 && k[0] is >= 'A' and <= 'Z') query = (query + char.ToLowerInvariant(k[0]))[..Math.Min(query.Length + 1, 40)];
            else if (k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1])) query = (query + k[1])[..Math.Min(query.Length + 1, 40)];
            else if (k == "Space") { if (query.Length > 0 && query[^1] != ' ' && query.Length < 40) query += " "; }
            else if (k == "Backspace") { if (query.Length > 0) query = query[..^1]; }
            else if (k is "Enter" or "Escape") Typing = false;
            else return true;                                               // anything else (F keys, arrows...) is the game's
        }
        __result = true;
        return false;
    }
}
