// A key that opens and closes the Squad record window (the "Record hotkey" setting; shared by the Living Wasteland helper and Squad Record).
// The native Controls action is opt-in on builds that expose it. The fallback is a Harmony postfix on PlayerInterface.UiKey(key, ctrl,
// shift): it acts only when the interface did not take the key (__result false: edit boxes,
// Esc and the game's windows come first) and only on the bare key and while the HUD shows, so it runs before the game's key bindings without stealing a used one.
// The default keys (U, N, O) are bound by neither Kenshi's default controls nor Meitou. The method is looked up by name in one place; if it
// is missing or the patch fails, the hotkey is off with one log line and the button still works. Only the window's open flag is touched.
using System.Reflection;
using HarmonyLib;
using Meitou.Modding;

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

    /// <summary>Installs the Settings input and one hotkey path.</summary>
    public static bool Install(ModContext context, Harmony harmony, Action<string> log, bool useNative)
    {
        UiInput.Install(harmony, log);
        if (useNative && TryRegisterNative(context, log)) return true;
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

    internal static bool TryRegisterNative(ModContext context, Action<string> log)
    {
        try
        {
            var add = typeof(ModContext).GetMethod("AddKeyAction");
            if (add?.GetParameters() is not
                [{ ParameterType: var name }, { ParameterType: var label },
                 { ParameterType: var pressed }, { ParameterType: var bindings }] ||
                name != typeof(string) || label != typeof(string) || !pressed.IsGenericType ||
                pressed.GetGenericTypeDefinition() != typeof(Action<>) || bindings.GetElementType() is not { } binding ||
                !bindings.IsSZArray)
            {
                log("record hotkey: native Controls action unavailable; using the legacy hotkey");
                return false;
            }

            string preferred = Key;
            bool hasDefault = preferred != "Off" && Keys.Contains(preferred);
            var defaults = Array.CreateInstance(binding, hasDefault ? 1 : 0);
            if (hasDefault)
            {
                var keyType = binding.Assembly.GetType("Meitou.Game.Host.Input.Key", throwOnError: true)!;
                var of = binding.GetMethod("Of", BindingFlags.Public | BindingFlags.Static, [keyType])
                    ?? throw new MissingMethodException(binding.FullName, "Of(Key)");
                defaults.SetValue(of.Invoke(null, [Enum.Parse(keyType, preferred)]), 0);
            }

            var press = typeof(RecordHotkey).GetMethod(nameof(NativePress), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(pressed.GetGenericArguments()[0]).CreateDelegate(pressed);
            add.Invoke(context, ["squad-record", "Toggle Squad record", press, defaults]);
        }
        catch (Exception e)
        {
            log($"record hotkey: native registration failed ({e.GetBaseException().Message}); using the legacy hotkey");
            return false;
        }
        log("record hotkey: registered in Settings > Controls");
        return true;
    }

    static void NativePress<T>(T _)
    {
        if (RecordHost.Off || StatsUi.open && UiInput.Typing) return;
        StatsUi.open = !StatsUi.open;
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
