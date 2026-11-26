using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Content;
using Meitou.Modding;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class SettingsProfileWorldTests
{
    [Fact]
    public void Profiles_change_only_switches_and_the_player_can_still_edit_them()
    {
        const BindingFlags instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var data = W.Data([]);
        var ctor = Assert.Single(typeof(ModContext).GetConstructors(instance));
        var args = ctor.GetParameters();
        var manifest = new CodeModManifest("twr-profile-test", 2, ["LivingWastelandMeitou.dll"], typeof(LwMod).FullName!);
        var info = new ModInfo("twr-profile-test", "profile-fixture.mod", default);
        var mod = Assert.Single(args[0].ParameterType.GetConstructors(instance)).Invoke([info, manifest]);
        var registry = Activator.CreateInstance(args[1].ParameterType, nonPublic: true);
        var ctx = (ModContext)ctor.Invoke([mod, registry, data.Db, data, (Func<World?>)(() => null), null]);
        var settings = new LwSettings { JobCap = 11, NewsReach = 9, Talk = 20, PlaytestDiagnostics = true };
        var group = ctx.AddSettings(settings);
        var apply = typeof(SettingsFile).GetMethod("ApplyProfile", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(apply);

        apply.Invoke(null, [group, "full"]);
        Assert.True(settings.OutpostCouriers && settings.RememberOutposts && settings.PrisonerRecovery && settings.VisibleNeckCharms);
        Assert.True(settings.BarTalk);
        Assert.False(settings.LighterChecks);

        apply.Invoke(null, [group, "light"]);
        Assert.True(settings.LighterChecks);
        Assert.False(settings.OutpostCouriers || settings.PrisonerRecovery || settings.BarTalk || settings.OutlawCamps);
        Assert.True(settings.Jobs && settings.Atlas && settings.Trophies);
        var bar = Assert.Single(group.Settings, s => s.Key == nameof(LwSettings.BarTalk));
        bar.Set(true);
        Assert.True(settings.BarTalk);

        apply.Invoke(null, [group, "standard"]);
        Assert.True(settings.BarTalk && settings.TownCriers);
        Assert.False(settings.LighterChecks || settings.OutpostCouriers || settings.PrisonerRecovery);
        Assert.Equal((11, 9, 20, true), (settings.JobCap, settings.NewsReach, settings.Talk, settings.PlaytestDiagnostics));
    }
}
