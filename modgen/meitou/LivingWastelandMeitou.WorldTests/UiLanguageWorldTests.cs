using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Content;
using Meitou.Simulation;
using System.Text.Json;
using Meitou.Modding;
using Xunit;

[Collection("settings")]
public sealed class UiLanguageWorldTests
{
    [Fact]
    public void Exact_locale_wins_and_missing_labels_fall_back_to_language_then_english()
    {
        var loader = typeof(LwMod).Assembly.GetType("LivingWasteland.MeitouMod.UiLanguage");
        Assert.NotNull(loader);
        var read = loader.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(read);
        const string text = """
            {
              "de": { "Squad record": "Truppakte", "Titles": "Titel", "Fallen": "Gefallene" },
              "de_DE": { "Squad record": "Truppchronik", "Titles": "" },
              "es": { "Fallen": "Caídos" }
            }
            """;
        var exact = Assert.IsType<Dictionary<string, string>>(read.Invoke(null, [text, "DE-de"]));
        Assert.Equal("Truppchronik", exact["Squad record"]);
        Assert.Equal("Titel", exact["Titles"]);
        Assert.Equal("Gefallene", exact["Fallen"]);
        Assert.False(exact.ContainsKey("History"));
        Assert.Empty(Assert.IsType<Dictionary<string, string>>(read.Invoke(null, [text, "en_GB"])));
    }

    [Fact]
    public void A_real_mod_context_registers_the_shared_catalogue_when_supported()
    {
        var translate = typeof(ModContext).GetMethod("Tr", [typeof(string)]);
        var locale = typeof(ModContext).GetProperty("Language");
        if (translate is null || locale is null) return;
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var create = Assert.Single(typeof(ModContext).GetConstructors(all));
        var parameters = create.GetParameters();
        var load = Assert.Single(parameters[0].ParameterType.GetConstructors(all));
        var data = W.Data([]);
        var folder = Directory.CreateTempSubdirectory("twr-locale-");
        var info = new ModInfo("twr-locale-test", Path.Combine(folder.FullName, "locale-test.mod"), default);
        var manifest = new CodeModManifest("twr-locale-test", 2, ["test.dll"], typeof(LwMod).FullName!);
        var loaded = load.Invoke([info, manifest]);
        var registry = Activator.CreateInstance(parameters[1].ParameterType, nonPublic: true);
        var context = (ModContext)create.Invoke([loaded, registry, data.Db, data, (Func<World?>)(() => null), null]);
        var catalogue = new Dictionary<string, Dictionary<string, string>>
        {
            [(string)locale.GetValue(context)!] = new()
            {
                ["Wasteland Remembers: squad record"] = "Fixture caption",
                ["Melee Defence"] = "Fixture skill",
                ["Lighter checks"] = "Fixture light",
                ["Off in Settings"] = "Fixture off",
                ["{0}   {1} {2}"] = "{2} in {1} for {0}",
                ["{0} earned a title"] = "{malformed}",
            }
        };
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, UiLanguage.FileName), JsonSerializer.Serialize(catalogue));
            UiLanguage.Register(context);
            Assert.Equal("Fixture caption", UiLanguage.Text("Wasteland Remembers: squad record"));
            Assert.Equal("Fixture caption", translate.Invoke(context, ["Wasteland Remembers: squad record"]));
            Assert.Equal("English fallback", UiLanguage.Text("English fallback"));
            bool lighter = LwMod.Settings.LighterChecks;
            try
            {
                LwMod.Settings.LighterChecks = false;
                using var world = W.World();
                ActiveFeatures.Publish(world);
                Assert.Contains("Fixture light · Fixture off", ActiveFeatures.Lines());
            }
            finally
            {
                ActiveFeatures.Clear();
                LwMod.Settings.LighterChecks = lighter;
            }
            var skill = typeof(Notices).GetMethod("SkillLine", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(skill);
            Assert.Equal("12 in Fixture skill for Beep", skill.Invoke(null, ["Beep", "Melee Defence", 12]));
            var format = typeof(UiLanguage).GetMethod("Format", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(format);
            Assert.Equal("Beep earned a title", format.Invoke(null, ["{0} earned a title", new object?[] { "Beep" }]));
        }
        finally
        {
            folder.Delete(recursive: true);
            UiLanguage.Register(context);
        }
    }
}
