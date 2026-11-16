// Our settings, changeable from our own window (the Settings tab) and kept between sessions.
//
// Meitou keeps a mod's setting values in meitou.user.json only when they are changed on its own Settings page. A change made from our window
// goes through the same ModSetting (so the page shows it at once) but Meitou would not keep it, so we keep every non-default value in a small
// file of our own (%LocalAppData%\Meitou\mods\<mod>.settings.txt, "key=value" lines) and put it back on load, after Meitou's. A change on
// Meitou's page is written there too (the group's Changed event), so the two never disagree for long.
using System.Globalization;
using Meitou.Modding;

namespace LivingWasteland.MeitouMod;

public static class SettingsFile
{
    public static ModSettingsGroup? Group { get; private set; }
    static string? path;
    static bool loading;

    public static void Attach(ModSettingsGroup group, string modId)
    {
        Group = group;
        try
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meitou", "mods", modId + ".settings.txt");
            Load();
        }
        catch (Exception e)
        {
            RecordHost.Log($"settings file: not read ({e.GetBaseException().Message})");
        }
        group.Changed += _ => Save();
    }

    static void Load()
    {
        if (path is null || !File.Exists(path) || Group is null) return;
        loading = true;
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || Group.Settings.FirstOrDefault(s => s.Key == line[..eq].Trim()) is not { } setting) continue;
                string text = line[(eq + 1)..].Trim();
                try
                {
                    object? value = setting.Kind switch
                    {
                        ModSettingKind.Switch => bool.TryParse(text, out bool b) ? b : null,
                        ModSettingKind.List => text,
                        _ => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null,
                    };
                    if (value is not null) setting.Set(value);
                }
                catch (Exception e)
                {
                    RecordHost.Log($"settings file: {setting.Key} not set ({e.GetBaseException().Message})");
                }
            }
        }
        finally
        {
            loading = false;
        }
    }

    static void Save()
    {
        if (loading || path is null || Group is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, Group.Settings.Where(s => !s.IsDefault).Select(s => $"{s.Key}={s.Text}"));
        }
        catch (Exception e)
        {
            RecordHost.Log($"settings file: not written ({e.GetBaseException().Message})");
        }
    }

    /// <summary>Every setting back to its default.</summary>
    public static void Defaults()
    {
        foreach (var s in Group?.Settings ?? []) s.Reset();
    }

    /// <summary>
    /// "Record only": every switch off and every amount at its lowest, except the ones that are the record itself (the window's button,
    /// logging, Recap moments): Living Wasteland as a plain squad record. Squad Record's own settings are all of that kind already.
    /// </summary>
    public static void RecordOnly()
    {
        string[] keep = ["RecordButton", "RecordHotkey", "NoticesPlace", "RecapMoments", "LogChanges", "TitleMessages", "JobCap", "BarTalkPause", "NeckSlotX", "NeckSlotY", "ContractReach", "NewsReach"];
        foreach (var s in Group?.Settings ?? [])
        {
            if (keep.Contains(s.Key)) continue;
            try
            {
                if (s.Kind == ModSettingKind.Switch) s.Set(false);
                else if (s.Kind == ModSettingKind.Slider) s.Set(s.Min);
            }
            catch (Exception e)
            {
                RecordHost.Log($"settings: {s.Key} not changed ({e.GetBaseException().Message})");
            }
        }
    }

    /// <summary>One click on a setting's control: a switch flips, a slider moves one step (dir -1 or +1), a list goes to its next choice.</summary>
    public static void Step(string key, int dir)
    {
        if (Group?.Settings.FirstOrDefault(s => s.Key == key) is not { } s) return;
        try
        {
            switch (s.Kind)
            {
                case ModSettingKind.Switch:
                    s.Set(!(bool)(s.Get() ?? false));
                    break;
                case ModSettingKind.List:
                    var options = s.Options;
                    if (options.Count > 0) s.Set(options[(Math.Max(0, s.Choice) + (dir < 0 ? options.Count - 1 : 1)) % options.Count]);
                    break;
                default:
                    double step = s.Step > 0 ? s.Step : s.Whole ? 1 : (s.Max - s.Min) / 100;
                    s.Set(System.Convert.ToDouble(s.Get(), CultureInfo.InvariantCulture) + dir * step);
                    break;
            }
        }
        catch (Exception e)
        {
            RecordHost.Log($"settings: {key} not changed ({e.GetBaseException().Message})");
        }
    }
}
