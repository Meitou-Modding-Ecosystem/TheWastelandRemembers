using System.Collections;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Content;
using Meitou.Data;
using Meitou.Modding;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class RecordHotkeyWorldTests
{
    const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    static ModContext Context()
    {
        var ctor = typeof(ModContext).GetConstructors(Instance).Single();
        var args = ctor.GetParameters();
        var info = new ModInfo("Living Wasteland", "LivingWasteland.mod", default);
        var manifest = new CodeModManifest("livingwasteland", 2, ["LivingWastelandMeitou.dll"], "LivingWasteland.MeitouMod.LwMod");
        var mod = args[0].ParameterType.GetConstructors(Instance).Single().Invoke([info, manifest]);
        var registry = Activator.CreateInstance(args[1].ParameterType, nonPublic: true);
        return (ModContext)ctor.Invoke([mod, registry, new GameDatabase(), W.Data([]), (Func<World?>)(() => null), null]);
    }

    [Fact]
    public void New_Wasteland_settings_prefer_the_native_record_key()
    {
        Assert.True(new LwSettings().NativeRecordHotkey);
    }

    [Theory]
    [InlineData("N")]
    [InlineData("Off")]
    public void Native_record_action_uses_the_initial_key_and_respects_search(string key)
    {
        var logs = new List<string>();
        if (typeof(ModContext).GetMethod("AddKeyAction") is null)
        {
            Assert.False(RecordHotkey.TryRegisterNative(null!, logs.Add));
            Assert.Contains("using the legacy hotkey", Assert.Single(logs));
            return;
        }

        var ctx = Context();
        var prior = (RecordHotkey.Key, StatsUi.open, UiInput.Typing, RecordHost.Off);
        try
        {
            RecordHotkey.Key = key;
            StatsUi.open = UiInput.Typing = RecordHost.Off = false;
            Assert.True(RecordHotkey.TryRegisterNative(ctx, logs.Add), string.Join(", ", logs));
            var pending = typeof(ModContext).GetProperty("Pending", Instance)!.GetValue(ctx)!;
            var actions = (IEnumerable)pending.GetType().GetProperty("KeyActions", Instance)!.GetValue(pending)!;
            var action = Assert.Single(actions.Cast<object>());
            Assert.Equal("squad-record", action.GetType().GetProperty("Name")!.GetValue(action));
            var defaults = ((IEnumerable)action.GetType().GetProperty("Defaults")!.GetValue(action)!).Cast<object>();
            if (key == "Off") Assert.Empty(defaults);
            else
            {
                var binding = Assert.Single(defaults);
                Assert.Equal(key, binding.GetType().GetProperty("Key")!.GetValue(binding)!.ToString());
            }

            var press = action.GetType().GetMethod("Press", Instance)!;
            var input = press.GetParameters()[0].ParameterType.GetConstructors().Single().Invoke(
                [null, Array.Empty<CharacterId>(), 0L, (Action<SimCommand>)(_ => throw new InvalidOperationException())]);
            press.Invoke(action, [input]);
            Assert.True(StatsUi.open);
            UiInput.Typing = true;
            press.Invoke(action, [input]);
            Assert.True(StatsUi.open);
            UiInput.Typing = false;
            press.Invoke(action, [input]);
            Assert.False(StatsUi.open);
            RecordHost.Off = true;
            press.Invoke(action, [input]);
            Assert.False(StatsUi.open);
            Assert.False((bool)action.GetType().GetProperty("Faulted", Instance)!.GetValue(action)!);
        }
        finally { (RecordHotkey.Key, StatsUi.open, UiInput.Typing, RecordHost.Off) = prior; }
    }
}
