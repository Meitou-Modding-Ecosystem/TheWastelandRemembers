// A key that opens and closes the Squad record window (the "Record hotkey" setting; shared by the Living Wasteland helper and Squad Record).
// Meitou has no hotkey API, so this is a Harmony postfix (Lib.Harmony, which the game ships for mods) on PlayerInterface.UiKey(key, ctrl,
// shift), the way docs/modding-practice.md describes: it acts only when the interface did not take the key (__result false: edit boxes,
// Esc and the game's windows come first) and only on the bare key and while the HUD shows, so it runs before the game's key bindings without stealing a used one.
// The default keys (U, N, O) are bound by neither Kenshi's default controls nor Meitou. The method is looked up by name in one place; if it
// is missing or the patch fails, the hotkey is off with one log line and the button still works. Only the window's open flag is touched.
using System.Reflection;
using HarmonyLib;

namespace LivingWasteland.MeitouMod;

public static class RecordHotkey
{
    public static readonly string[] Keys = ["U", "N", "O", "Off"];
    public static volatile string Key = Keys[0];

    /// <summary>PlayerInterface.UiKey(key, ctrl, shift), looked up by name in one place (the hotkey and the search box's typing use it); null
    /// when it is not there in that form.</summary>
    public static MethodInfo? UiKeyMethod()
    {
        var target = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Meitou.Game.PlayerInterface", false))
            .FirstOrDefault(t => t is not null)
            ?.GetMethod("UiKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return target is not null && target.ReturnType == typeof(bool) && target.GetParameters() is [{ Name: "key" }, { Name: "ctrl" }, { Name: "shift" }]
            ? target : null;
    }

    /// <summary>Patches PlayerInterface.UiKey; false (and a log line) when it cannot. Also installs the Settings tab's mouse and typing
    /// (UiInput.cs).</summary>
    public static bool Install(Harmony harmony, Action<string> log)
    {
        UiInput.Install(harmony, log);
        try
        {
            if (UiKeyMethod() is not { } target)
            {
                log("record hotkey: PlayerInterface.UiKey(key, ctrl, shift) not found; the hotkey is off (the button still works)");
                return false;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(RecordHotkey).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
            return true;
        }
        catch (Exception e)
        {
            log($"record hotkey: patching failed ({e.GetBaseException().Message}); the hotkey is off (the button still works)");
            return false;
        }
    }

    static PropertyInfo? visible;

    static void Postfix(object __instance, object key, bool ctrl, bool shift, ref bool __result)
    {
        if (__result || ctrl || shift) return;
        visible ??= __instance.GetType().GetProperty("UiVisible");
        if (visible?.GetValue(__instance) is false) return;          // the HUD is hidden (photo mode, a menu): leave the window be
        string want = Key;
        if (want == "Off" || key?.ToString() != want) return;
        StatsUi.open = !StatsUi.open;
        __result = true;
    }
}
