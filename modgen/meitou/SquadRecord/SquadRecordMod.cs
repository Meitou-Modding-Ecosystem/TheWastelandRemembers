// Squad Record for Meitou: a per-character record of what your people have done (kills by faction, knockouts, limbs cut and lost, damage dealt
// and taken, times downed, distance walked, food eaten, items crafted, time spent fighting, healing, working, building, hauling, looting and
// sneaking) and the titles they earn from it, in a window opened from the HUD or F10. Titles come from titles.json next to the DLL.
// The Living Wasteland helper has all of this built in: with it enabled, this mod stays out of the way.
using System.Text.Json;
using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

/// <summary>Esc > Settings > Mods > Squad Record.</summary>
public sealed class SquadRecordSettings
{
    [Setting("Title messages", Hint = "Say so in game when someone earns a title.")]
    public bool TitleMessages { get; set; } = true;

    [Setting("Record button", Hint = "Where the Squad record button sits, if it covers another button.",
        Options = ["Top, beside Feedback", "Top right, lower", "Top right", "Top left, lower", "Top left"])]
    public string RecordButton { get => StatsUi.Position; set => StatsUi.Position = value; }

    [Setting("Record hotkey", Hint = "The key that opens and closes the Squad record window. None of these is bound by Kenshi; Off for no key.",
        Options = ["U", "N", "O", "Off"])]
    public string RecordHotkey { get => LivingWasteland.MeitouMod.RecordHotkey.Key; set => LivingWasteland.MeitouMod.RecordHotkey.Key = value; }

    [Setting("Native record hotkey (test, restart)", Hint = "Use Settings > Controls to bind the record on supported Meitou builds. The current record hotkey choice supplies the initial default.")]
    public bool NativeRecordHotkey { get; set; }

    [Setting("Notices", Hint = "Small messages that float up for a few seconds: a squad member gaining a skill level, and a faction's standing with you going up (green) or down (red).",
        Options = ["Skills and standing", "Skills only", "Standing only", "Off"])]
    public string Notices { get => LivingWasteland.MeitouMod.Notices.Mode; set => LivingWasteland.MeitouMod.Notices.Mode = value; }

    [Setting("Notices position", Hint = "Where the floating notices appear on screen.", Options = ["Top centre", "Left side", "Right side"])]
    public string NoticesPlace { get => LivingWasteland.MeitouMod.Notices.Place; set => LivingWasteland.MeitouMod.Notices.Place = value; }

    [Setting("Share moments with Recap", Hint = "If the Recap mod is installed, titles and last stands appear in its recap.")]
    public bool RecapMoments { get; set; } = true;

    [Setting("Log every kill and title", Hint = "Write each kill, craft and title to log.txt.")]
    public bool LogChanges { get; set; } = false;
}

public sealed class SquadRecordMod : IMod
{
    static readonly SquadRecordSettings Settings = new();

    public void Initialize(ModContext c)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "LivingWastelandMeitou"))
        {
            c.Log("the Living Wasteland helper is loaded and has the squad record built in: Squad Record stays off");
            return;
        }

        SettingsFile.Attach(c.AddSettings(Settings), "SquadRecord");
        RecordHost.Log = c.Log;
        RecordHost.LogChanges = () => Settings.LogChanges;
        RecordHost.Announce = () => Settings.TitleMessages;
        RecapLink.Enabled = () => Settings.RecapMoments;
        RecapLink.Source = "SquadRecord";
        var rules = ReadTitles(c);
        c.AddSystem("squad-record", ctx => new LwDeedsSystem(rules, ctx.Data), SystemAnchor.End);
        AidUseWatch.Register(c);
        c.AddUi(new StatsUi("squadrecord"));
        RecordHotkey.Install(c, new HarmonyLib.Harmony("squadrecord.recordhotkey"), c.Log, Settings.NativeRecordHotkey);
        c.AddDebugCommand("Squad record", _ =>
        {
            StatsUi.open = !StatsUi.open;
            c.Log("squad record:\n" + RecordHost.Panel + "\n\n" + RecordHost.CatalogText + "\n\nhistory:\n" + RecordHost.HistoryText);
            return StatsUi.open ? "record window open (also written to log.txt)" : "record window closed";
        }, hasField: false);
        c.Log($"{rules.Count} titles");
    }

    static List<TitleRule> ReadTitles(ModContext c)
    {
        string path = Path.Combine(c.Folder, "titles.json");
        if (!File.Exists(path))
        {
            c.Log("no titles.json: the record runs without titles");
            return [];
        }
        var titles = JsonSerializer.Deserialize<List<TitleJson>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return (titles ?? []).Select(t => new TitleRule(t.Id, t.Name, t.Scope, t.Stat, t.Faction, t.Targets, t.At, t.Legendary)).ToList();
    }

    sealed record TitleJson(string Id, string Name, string Scope, string Stat, string? Faction, List<string>? Targets, int At, bool Legendary);
}
