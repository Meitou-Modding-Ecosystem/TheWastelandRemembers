// Living Wasteland, Meitou side: the quest helper. Built against the Meitou 0.17.2 release files (see modgen/MEITOU_PORT.md).
//
// What it does: keeps the quest and contract state that the Kenshi build keeps in BFrizz variables. The data mods (built with
// `build_mod.py --target meitou`) ask for it through dialogue condition and effect CODES; lw_codes.json (next to this DLL, written by the
// build) says which code is which variable or item:
//   condition CONDITION_BASE+n  compare variable n with the value           (check.Compare: compare by 1 '<', 2 '>', else '==')
//   condition capCondition      the open-job counter is below the cap set on the settings page
//   effect    SET_BASE+n        set variable n to the value
//   effect    ADD_BASE+n        add the value (may be negative) to variable n
//   effect    TAKE_BASE+m       take `value` units of item m from the conversation's target (the player character)
// State is per world, saved in the mod's sidecar save data, and part of the state hash. Every change is written to log.txt, the
// job-cap messages are posted in game, and F10 > "Wasteland Remembers: quests" lists what you hold. It also fires bar talk (BarTalk.cs).
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

/// <summary>A play-test build (compiled with PLAYTEST) switches the test features the session should cover on by default; the release keeps them off.</summary>
public static class PlayTest
{
#if PLAYTEST
    public const bool On = true;
#else
    public const bool On = false;
#endif
}

/// <summary>the Squad record window's Settings tab.</summary>
public sealed class LwSettings
{
    [Setting("Added talk amount (%)", Hint = "How much of our added talk you hear: bar talk, gossip, reactions, banter, victory and mourning lines. 100 is as written, 0 is none (vanilla talk is untouched). Conversations you start are not affected.",
        Min = 0, Max = 100, Step = 10)]
    public int Talk { get; set; } = 40;

    [Setting("Rumour and gossip amount (%)", Hint = "How often people pass on rumours, outlaw news, war talk and talk about titles. 100 is as written, 0 is none.",
        Min = 0, Max = 200, Step = 10)]
    public int Gossip { get; set; } = 40;

    [Setting("Bar patrons chat", Hint = "Bar patrons talk among themselves when you are in the bar (Meitou does not do this by itself yet).")]
    public bool BarTalk { get; set; } = true;

    [Setting("Bar chat pause (seconds)", Hint = "How long a bar stays quiet after someone says something.", Min = 5, Max = 120)]
    public int BarTalkPause { get; set; } = 25;

    [Setting("Squad speaks after a won fight", Hint = "After your squad wins a fight, one of those still standing may say something about it (Meitou does not do this by itself yet).")]
    public bool VictoryLines { get; set; } = true;

    [Setting("Enemies gloat when you lose", Hint = "When your whole squad goes down, the winners may gloat. This also wakes Kenshi's own reactions to beating a squad (guards arresting it, and the like), which Meitou does not do yet.")]
    public bool EnemyVictoryLines { get; set; } = true;

    [Setting("Lookouts call out hostiles", Hint = "When hostile people come within about 150 m, the nearest of your squad calls them out by name (\"Dust Bandits on the ridge!\") and a squadmate may answer. Once per enemy squad every few hours, never mid-fight.")]
    public bool LookoutCalls { get; set; } = true;

    [Setting("Town criers", Hint = "Now and then, in a town that is not hostile to you, a local calls out fresh news from the roads that their faction would care about (wars and marches, outlaws raiding, raids beaten off, big deaths), or now and then their faction's proclamation. About every six minutes per town; each piece of news once a day.")]
    public bool TownCriers { get; set; } = true;

    [Setting("Squad mourns its fallen", Hint = "Your squad mourns its fallen by name, remembers where they fell, mutters at the faction that killed them, and remembers its scars.")]
    public bool Memorials { get; set; } = true;

    [Setting("Factions react to your titles", Hint = "Factions admire, fear or despise your squad's titles: lines, a one-time change in relations, refused jobs, a barman's gift.")]
    public bool TitleReactions { get; set; } = true;

    [Setting("Quests and outlaw contracts", Hint = "Townsfolk offer our quests and contracts on outlaws. Off: no new offers (jobs already taken can still be handed in).")]
    public bool Jobs { get; set; } = true;

    [Setting("Open job limit", Hint = "How many unpaid quests and contracts you can carry before nobody offers you more.", Min = 1, Max = 20)]
    public int JobCap { get; set; } = 5;

    [Setting("Job limit messages", Hint = "Say so in game when you reach the limit and when a slot frees up.")]
    public bool CapMessages { get; set; } = true;

    [Setting("Contract reach (km)", Hint = "How far from an outlaw's ground bounty givers offer contracts on them. 0 is anywhere.", Min = 0, Max = 30)]
    public int ContractReach { get; set; } = 12;

    [Setting("Outlaw news range (km)", Hint = "How far talk about a named outlaw travels from their ground (the whole map is about 30 km across). Townsfolk further away don't know them.", Min = 1, Max = 30)]
    public int NewsReach { get; set; } = 5;

    [Setting("Extra pay for distant outlaws", Hint = "Contracts on outlaws far from the giver pay more: nothing extra within 5 km, up to double at 25 km.")]
    public bool DistancePay { get; set; } = true;

    [Setting("Bounties grow while outlaws raid", Hint = "An outlaw who keeps raiding gets a bigger price on their head: a tenth more for each spell of raiding, up to double. Contracts pay the growth too.")]
    public bool GrowingBounties { get; set; } = true;

    [Setting("Contracts improve faction relations", Hint = "A faction you finish outlaw contracts for thinks a little better of you each time (+4 for a reported kill, +5 for a body handed in), up to friendly but never allied.")]
    public bool ContractFavours { get; set; } = true;

    [Setting("Alms and offerings", Hint = "Some townsfolk ask for alms (Empire towns), an offering to Okran (Holy Nation towns) or a donation to the Anti-Slavers' cause. Their faction thinks a little better of you, up to friendly, and what you give counts toward squad titles.")]
    public bool Alms { get; set; } = true;

    [Setting("Moving outlaw camps (test)", Hint = "Named outlaws keep a camp in their regions that moves with the outlaw news: near towns when raiding, far away when lying low.")]
    public bool OutlawCamps { get; set; } = false;

    [Setting("Outlaws send men after you", Hint = "An outlaw whose people your squad bloodied, and who lived, sends men after you two to four days later.")]
    public bool OutlawReprisals { get; set; } = true;

    [Setting("Town heat (towns remember crimes)", Hint = "Towns remember your crimes: guards warn, gates react, heat fades with time. Off: nobody reacts to heat.")]
    public bool Heat { get; set; } = true;

    [Setting("News from other mods", Hint = "Other mods can report what happens in the world (a town falls, a siege begins, a caravan is lost). Townsfolk near the place talk about it, and once your squad has heard it, it goes into History, the Atlas and Recap.")]
    public bool OtherModsNews { get; set; } = true;
    [Setting("Rivals", Hint = "Someone your squad beats and leaves alive can come back for it: renamed for what happened, stronger each time, talked about in town and listed in the Atlas.")]
    public bool Rivals { get; set; } = true;
    [Setting("Outlaw successors", Hint = "A few days after a named outlaw dies or is jailed, one of their gang takes over the same ground under a new name, with a smaller bounty and a new contract.")]
    public bool OutlawSuccessors { get; set; } = true;
    [Setting("Towns hire bounty hunters", Hint = "A town that has you marked as hunted hires a crew of blades to track your squad down.")]
    public bool BountyHunters { get; set; } = true;

    [Setting("Small factions raid big ones (test)", Hint = "Small factions now and then send a raiding party to lurk near a big enemy's town for two days: Flotsam against the Holy Nation, Anti-Slavers against slavers, rebel farmers against the Cities, Cannibal Hunters against cannibals. Talk follows what happened.")]
    public bool HarassmentRaids { get; set; } = false;

    [Setting("Trophies and keepsakes", Hint = "Named outlaws carry a token you can take, a fallen squadmate leaves a keepsake to the one who mourns them, and admiring barmen add their faction's charm to a gift. On Meitou 0.18 and later they are worn in a neck slot.")]
    public bool Trophies { get; set; } = true;

    [Setting("Neck slot position: left (restart)", Hint = "Where the neck slot sits in the character window: pixels from its left edge. Takes effect after restarting the game.", Min = 0, Max = 800, Step = 2)]
    public int NeckSlotX { get; set; } = 420;

    [Setting("Neck slot position: top (restart)", Hint = "Where the neck slot sits in the character window: pixels from its top edge. Takes effect after restarting the game.", Min = 0, Max = 800, Step = 2)]
    public int NeckSlotY { get; set; } = 255;

    [Setting("Atlas tab", Hint = "The Atlas tab: places you know and the people talked about, filled by the gossip your squad overhears.")]
    public bool Atlas { get; set; } = true;

    [Setting("Squad record button position", Hint = "Where the Squad record button sits, if it covers another button.",
        Options = ["Top, beside Feedback", "Top right, lower", "Top right", "Top left, lower", "Top left"])]
    public string RecordButton { get => StatsUi.Position; set => StatsUi.Position = value; }

    [Setting("Squad record hotkey", Hint = "The key that opens and closes the Squad record window. None of these is bound by Kenshi; Off for no key.",
        Options = ["U", "N", "O", "Off"])]
    public string RecordHotkey { get => LivingWasteland.MeitouMod.RecordHotkey.Key; set => LivingWasteland.MeitouMod.RecordHotkey.Key = value; }

    [Setting("Notices", Hint = "Small messages that float up for a few seconds: a squad member gaining a skill level, and a faction's standing with you going up (green) or down (red).",
        Options = ["Skills and standing", "Skills only", "Standing only", "Off"])]
    public string Notices { get => LivingWasteland.MeitouMod.Notices.Mode; set => LivingWasteland.MeitouMod.Notices.Mode = value; }

    [Setting("Notices position", Hint = "Where the floating notices appear on screen.", Options = ["Top centre", "Left side", "Right side"])]
    public string NoticesPlace { get => LivingWasteland.MeitouMod.Notices.Place; set => LivingWasteland.MeitouMod.Notices.Place = value; }

    [Setting("Send moments to the Recap mod", Hint = "If the Recap mod is installed, titles, last stands, towns marking you as wanted or hunted, bounty hunters, outlaw reprisals and grown bounties appear in its recap.")]
    public bool RecapMoments { get; set; } = true;

    [Setting("Log quest changes (for testing)", Hint = "Write every quest step change to log.txt (useful for play tests).")]
    public bool LogChanges { get; set; } = true;
}

public sealed class LwMod : IMod
{
    internal static LwSettings Settings = new();
    internal static Action<string> Log = _ => { };
    internal static string JobVariable = "LW open jobs";
    /// <summary>The build this helper was made for (modgen/BUILD_ID); the data mods carry theirs in lw_codes.json.</summary>
    public static string DataBuild = "unknown";
    public const string HelperBuild = "20261116ap";
    internal static List<RumourCode> Rumours = [];
    internal static List<(int Index, string Name)> Outlaws = [];

    public void Initialize(ModContext c)
    {
        string path = Path.Combine(c.Folder, "lw_codes.json");
        if (!File.Exists(path))
        {
            c.LogError($"{path} is missing: build the data mods with --target meitou and copy lw_codes.json next to the DLL");
            return;
        }
        var table = JsonSerializer.Deserialize<CodeTable>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("lw_codes.json is empty");
        LwState.Initial = table.Variables.ToDictionary(v => v.Name, v => v.Initial);
        Log = line => { LogTally.Count(line); c.Log(line); };
        RecordHost.Log = Log;
        SquadRoles.Context = c;
        RecordHost.LogChanges = () => Settings.LogChanges;
        RecapLink.Enabled = () => Settings.RecapMoments;
        RecapLink.Source = "LivingWasteland";
        Settings.JobCap = table.Jobs?.Cap ?? 5;
        SettingsFile.Attach(c.AddSettings(Settings), "LivingWastelandMeitou");
        if (table.Jobs is { } jobs) JobVariable = jobs.Variable;

        RegisterVariables(c, table.Variables, table.VariableMoments);
        RegisterQuestRoutes(c, table.QuestRoutes);
        RegisterTakes(c, table.TakeItems);
        Rumours = table.Rumours ?? [];
        foreach (var r in Rumours)
        {
            string state = r.State;
            c.AddDialogueCondition(r.Code, check => check.Compare(LwState.For(check.World).Core.DaysSince(state, check.World.GameHours)));
        }
        if (table.Jobs is { CapCondition: > 0 } j)
            c.AddDialogueCondition(j.CapCondition, check => check.Matches(LwState.For(check.World).Get(JobVariable) < Settings.JobCap));

        RegisterNotoriety(c, table.Notoriety);
        RegisterNews(c, table.Outlaws ?? [], table.Wars ?? []);
        RegisterWarTowns(c, table.WarTowns);
        RegisterNamedKills(c, table.NamedKills ?? []);
        RegisterCamps(c, table.Camps ?? [], table.CampNews);
        RegisterTemperaments(c, table.Temperaments);
        RegisterMemorials(c, table.Memorials);
        foreach (var np in table.NearPlaces ?? [])
        {
            string town = np.Town;
            c.AddDialogueCondition(np.Code, check => check.Matches(NearTown(check.World, check.Speaker, town)));
        }
        if (table.Favours is { } favours)
        {
            c.AddDialogueEffect(favours.Code, act => { if (Settings.ContractFavours) Favour(act.World, act.Speaker, act.Effect.Value, favours.Cap); });
            if (favours.Alms > 0)
                c.AddDialogueEffect(favours.Alms, act =>
                {
                    // alms and offerings: the giver's faction thinks better of the squad (+1 per 100 cats, at most +4, same cap as contract
                    // favours), and the cats count toward the squad's alms titles
                    int cats = act.Effect.Value;
                    if (!Settings.Alms || cats <= 0) return;
                    Favour(act.World, act.Speaker, Math.Clamp(cats / 100, 1, 4), favours.Cap);
                    if (LwDeeds.For(act.World) is { } d)
                        foreach (var said in d.Record.SetCrewStat("alms", d.Record.CrewStat("alms") + cats)) act.World.Post(said);
                });
        }
        if (table.Marches is { } marches)
            c.AddDialogueCondition(marches.Marching, check => check.Matches(Marches.IsMarching(check.World, LwState.For(check.World).Population, check.Subject)));
        if (table.Raids is { Count: > 0 } raids)
        {
            c.AddSystem("lw-raids", ctx => new RaidsSystem(raids, ctx.Find<PopulationSystem>(), ctx.Data), SystemAnchor.End);
            for (int i = 0; i < raids.Count; i++)
            {
                int k = i;
                c.AddDialogueCondition(raids[i].State, check => check.Compare(RaidsSystem.For(check.World)?.StateOf(check.World, k) ?? 0));
                c.AddDialogueCondition(raids[i].Town, check => check.Compare(RaidsSystem.For(check.World)?.TownOf(check.World, k) ?? 0));
            }
            c.AddDebugCommand("Wasteland Remembers: raids", _ => !Settings.HarassmentRaids ? "raids are off (the Squad record window's Settings tab)"
                : RaidsSystem.Latest?.Summary(null) ?? "no world yet", hasField: false);
        }
        if (table.Atlas is { } atlas)
            c.AddSystem("lw-atlas", ctx => new AtlasSystem(atlas, ctx.Find<Meitou.Simulation.Dialogue.DialogueSystem>(), ctx.Data), SystemAnchor.End);
        if (table.Trophies is { Keepsake: { Length: > 0 } keepsake })
        {
            LivingWasteland.MeitouMod.Trophies.Keepsake = keepsake;
            LivingWasteland.MeitouMod.Trophies.AddSlot(c, Settings.NeckSlotX, Settings.NeckSlotY);
        }
        LivingWasteland.MeitouMod.Trophies.Warrant = table.Trophies?.Warrant;
        if (table.Trophies?.Keepsakes is { Count: > 0 } keepsakes)
        {
            KeepsakeBook.Slots = keepsakes;
            c.AddSystem("lw-keepsakes", ctx => new KeepsakeBook(ctx.Data), SystemAnchor.End);
        }
        if (table.Trophies is { } trophies)
            ItemUses.Register(c, [.. trophies.Keepsakes ?? [], trophies.Warrant ?? ""]);
        if (table.Hunters is { Squad: { Length: > 0 } crew })
        {
            c.AddSystem("lw-hunters", ctx => new HuntersSystem(crew, ctx.Find<PopulationSystem>(), ctx.Data), SystemAnchor.End);
            c.AddDebugCommand("Wasteland Remembers: bounty hunters", _ => !Settings.BountyHunters ? "bounty hunters are off (the Squad record window's Settings tab)"
                : HuntersSystem.Latest?.Summary() ?? "no world yet", hasField: false);
        }
        // rivals (Rivals.cs): survivors the squad left alive who come back for it; their names in the town talk through word swaps
        c.AddSystem("lw-rivals", ctx =>
        {
            Pursuit.Ai = ctx.Find<Meitou.Simulation.Ai.AiSystem>();       // for pursuers' assault orders (Pursuit.Steer)
            return new RivalsSystem(ctx.Data);
        }, SystemAnchor.End);
        c.AddDebugCommand("Wasteland Remembers: rivals", _ => !Settings.Rivals ? "rivals are off (the Squad record window's Settings tab)"
            : RivalsSystem.Latest?.Summary() ?? "no world yet", hasField: false);
        // news from other mods (News.cs, WastelandApi.Report): towns near the place talk about it through word swaps
        c.AddSystem("lw-news", ctx => new NewsSystem(ctx.Data), SystemAnchor.End);
        c.AddDebugCommand("Wasteland Remembers: news from other mods", _ => NewsSystem.Latest?.Summary() ?? "no world yet", hasField: false);
        if (PlayTest.On)
            c.AddDebugCommand("Wasteland Remembers: test news (type a town)", d =>
            {
                if (string.IsNullOrWhiteSpace(d.Field)) return "type a town's name, e.g. Stack; it is reported as besieged (as another mod would)";
                NewsSystem.TestReports.Enqueue(("siege-began", d.Field.Trim()));
                return $"reporting a siege at {d.Field.Trim()} (see the log and the news row)";
            }, hasField: true);
        if (table.News is { } nw)
        {
            NewsSystem.Tokens = nw.Tokens;
            for (int k = 0; k < nw.Codes.Count; k++)
            {
                int slot = k;
                c.AddDialogueCondition(nw.Codes[k], check => check.Compare(NewsSystem.For(check.World)?.SlotValue(check.World, slot, check.Speaker) ?? 0));
            }
        }
        if (table.Rivals is { } rv)
        {
            RivalsSystem.Tokens = rv.Tokens;
            for (int k = 0; k < rv.Codes.Count; k++)
            {
                int slot = k;
                c.AddDialogueCondition(rv.Codes[k], check => check.Compare(RivalsSystem.For(check.World)?.SlotValue(check.World, slot, check.Speaker) ?? 0));
            }
        }
        if (table.Gossip is { Code: > 0 } g) c.AddDialogueCondition(g.Code, check => check.Matches(GossipRoll(check) < Settings.Gossip / 200.0));
        if (table.Talk is { Code: > 0 } t)
        {
            c.AddDialogueCondition(t.Code, check => check.Matches(TalkPacer.Allow(check.World, check.Speaker)
                && (Settings.Talk >= 100 || GossipRoll(check, 0x5A17) < Settings.Talk / 100.0)));
            foreach (var (feature, code) in t.Features ?? [])
            {
                Func<bool> on = feature switch { "jobs" => () => Settings.Jobs, "titles" => () => Settings.TitleReactions, "memorials" => () => Settings.Memorials, "alms" => () => Settings.Alms, _ => () => true };
                c.AddDialogueCondition(code, check => check.Matches(on()));
            }
        }
        c.AddSystem("lw-state", ctx => new LwSystem(ctx.Find<PopulationSystem>(), ctx.Find<DialogueSystem>(), ctx.Data), SystemAnchor.End);
        c.AddDebugCommand("Wasteland Remembers: quests", _ => LwState.Latest?.Summary() ?? "no world yet", hasField: false);
        c.AddDebugCommand("Wasteland Remembers: give up job (number or name)", d =>
        {
            if (string.IsNullOrWhiteSpace(d.Field)) return "type the job's number from the quests row, or part of its name";
            d.Enqueue(new GiveUpJob(d.Field) { Tick = d.Tick });
            return $"giving up '{d.Field.Trim()}'";
        }, hasField: true);
        string data = table.Build is { Length: > 0 } b ? b : "unknown";
        DataBuild = data;
        c.Log(data == HelperBuild ? $"build {HelperBuild} (data and helper match)"
            : $"WARNING: data build {data} but helper build {HelperBuild}: run install_meitou.bat from the newest build folder, then restart Meitou");
        c.Log($"{table.Variables.Count} quest variables, {table.TakeItems.Count} take-item codes, {Rumours.Count} rumour subjects, job limit {Settings.JobCap}, bar talk {(Settings.BarTalk ? "on" : "off")}");
    }

    /// <summary>Outlaw news (what each living named outlaw is up to) and war news (which factions are on campaign): WorldNews.cs.</summary>
    static void RegisterNews(ModContext c, List<OutlawCode> outlaws, List<WarCode> wars)
    {
        Outlaws = outlaws.Select(o => (o.Index, o.Name)).ToList();
        foreach (var o in outlaws)
        {
            int index = o.Index;
            c.AddDialogueCondition(o.Code, check => check.Compare(OutlawCampsSystem.DoingIn(check.World, index)));
        }
        foreach (var w in wars)
        {
            var (faction, player) = (w.Faction, w.Player);
            c.AddDialogueCondition(w.Code, check =>
            {
                var state = LwState.For(check.World);
                return check.Matches(WarNews.AtWar(state.Population, state.Data, faction, player));
            });
        }
        c.Log($"news: {outlaws.Count} outlaws, {wars.Count} war conditions");
    }

    /// <summary>
    /// A number in [0, 1) for one gossip check: from the tick and the speaker, never from the world's random source (a condition must only read,
    /// and the same tick and speaker always give the same answer).
    /// </summary>
    static double GossipRoll(DialogueCheck check, ulong salt = 0)
    {
        ulong h = (ulong)check.World.Tick * 0x9E3779B97F4A7C15UL ^ (ulong)SquadRecord.StableHash(check.Speaker.ToString()) ^ salt * 0xC2B2AE3D27D4EB4FUL;
        h = (h ^ (h >> 31)) * 0xBF58476D1CE4E5B9UL;
        h ^= h >> 29;
        return (h >> 11) / (double)(1UL << 53);
    }

    /// <summary>
    /// War town names for the war talk's word swaps: one condition per town; the line's value is 1 + the attacking faction's place in the
    /// table's faction list, and the condition holds while that faction marches on that town.
    /// </summary>
    static void RegisterWarTowns(ModContext c, WarTownTable? t)
    {
        if (t is null) return;
        var factions = t.Factions;
        foreach (var town in t.Towns)
        {
            string name = town.Town;
            c.AddDialogueCondition(town.Code, check =>
            {
                int f = check.Condition.Value - 1;
                var state = LwState.For(check.World);
                bool on = f >= 0 && f < factions.Count && WarNews.AttackedBy(state.Population, state.Data, factions[f], name);
                return check.Matches(on);
            });
        }
        c.Log($"war towns: {t.Towns.Count} towns, {factions.Count} factions");
    }

    /// <summary>
    /// Faction temperaments (TITLES_BY_FACTION.md): which titles a faction admires or despises changes how its people treat the holder.
    /// "Once" memory per faction and title (relations bump, gift) lives in the saved quest variables as "@temper faction|key".
    /// </summary>
    static void RegisterTemperaments(ModContext c, TemperamentTable? t)
    {
        if (t is null) return;
        TitleViews = t.Views ?? [];
        var despises = t.Despises ?? [];
        c.AddDialogueCondition(t.Once, check =>
        {
            string? f = FactionOf(check.World, check.Speaker);
            return check.Matches(f is not null && LwState.For(check.World).Get(TemperKey(f, check.Condition.Value)) == 0);
        });
        c.AddDialogueEffect(t.Mark, act =>
        {
            if (FactionOf(act.World, act.Speaker) is { } f) LwState.For(act.World).Core.Set(TemperKey(f, act.Effect.Value), 1);
        });
        c.AddDialogueCondition(t.Despised, check =>
        {
            string? f = FactionOf(check.World, check.Speaker);
            bool hated = f is not null && despises.TryGetValue(f, out var ids) && LwDeeds.For(check.World) is { } d
                && ids.Any(id => d.Holds(check.World, check.Subject, id));
            return check.Matches(Settings.TitleReactions && hated);
        });
        foreach (var g in t.Gifts ?? [])
        {
            string item = g.Item;
            c.AddDialogueEffect(g.Code, act => Give(act, item, Math.Max(1, act.Effect.Value)));
        }
        c.Log($"temperaments: {despises.Count} factions refuse work to titles they despise, {t.Gifts?.Count ?? 0} gift items");
    }

    /// <summary>Memorials (Deeds.cs, Notoriety.cs): the target is a dead squadmate; days since the last loss; the town of the last loss;
    /// the target's faction killed one of the squad within the last 60 game days.</summary>
    static void RegisterMemorials(ModContext c, MemorialTable? t)
    {
        if (t is null) return;
        c.AddDialogueCondition(t.Mourn, check =>
        {
            var table = check.World.Characters;
            bool fallen = table.TryResolveNext(check.Subject, out int s) && table.Cold(s) is { IsPlayer: true, Medical.Dead: true };
            return check.Matches(fallen);
        });
        if (t.SquadNear > 0)
            c.AddDialogueCondition(t.SquadNear, check =>
            {
                // other squad members on their feet within 50 m of the speaker: victory lines to the squad need someone to hear them
                var table = check.World.Characters;
                if (!table.TryResolveNext(check.Speaker, out int me)) return check.Compare(0);
                var at = table.Next[me].Position;
                int n = 0;
                for (int i = 0; i < table.HighWater; i++)
                    if (i != me && table.Next[i].Alive && table.Cold(i) is { IsPlayer: true, Animal: null, Medical: { Incapacitated: false, Dead: false } }
                        && System.Numerics.Vector3.DistanceSquared(table.Next[i].Position, at) < 500f * 500f) n++;
                return check.Compare(n);
            });
        c.AddDialogueCondition(t.LossDays, check => check.Compare(LwDeeds.For(check.World)?.Record.DaysSinceLoss(check.World.GameHours) ?? 1e6));
        c.AddDialogueCondition(t.LossTown, check =>
        {
            string? place = LwDeeds.For(check.World)?.Record.LastLossPlace;
            int i = place is null ? -1 : OutlawCampsSystem.NewsTowns.FindIndex(n => string.Equals(n, TownNames.Clean(place), StringComparison.OrdinalIgnoreCase));
            return check.Compare(i + 1);
        });
        c.AddDialogueCondition(t.Feud, check =>
        {
            string? f = FactionOf(check.World, check.Subject);
            bool feud = f is not null && LwDeeds.For(check.World)?.Record.Feuds.TryGetValue(f, out double h) == true && check.World.GameHours - h < 60 * 24;
            return check.Matches(feud);
        });
        if (t.Scar > 0)
            c.AddDialogueCondition(t.Scar, check =>
            {
                var table = check.World.Characters;
                if (LwDeeds.For(check.World) is not { } d || !table.TryResolveNext(check.Subject, out int s) || table.Cold(s) is not { IsPlayer: true } cold)
                    return check.Compare(0);
                string key = d.Record.PeekKey(LwDeeds.Identity(cold), cold.Name);
                return check.Compare(d.Record.ScarHere(key, d.TownAt(table.Next[s].Position).Town));
            });
        c.Log("memorials: mourning, remembrance, feud and scar codes registered");
    }

    static string TemperKey(string faction, int key) => $"@temper {faction}|{key}";

    /// <summary>Contract favours: the speaker's faction (the job's giver) thinks better of the player by <paramref name="amount"/>, but a favour
    /// never lifts the relation past <paramref name="cap"/> (friendly; allies, who fight for you, start at 50) and never touches a relation already
    /// there or a hostile faction's (a job for an enemy does not make it a friend).</summary>
    static void Favour(World world, CharacterId speaker, int amount, int cap)
    {
        var data = LwState.For(world).Data;
        if (data is null || world.Player.Faction < 0 || amount <= 0 || FactionOf(world, speaker) is not { } name) return;
        int f = data.FactionIndex(name);
        if (f < 0 || f == world.Player.Faction) return;
        float now = data.Relations.Get(world.Player.Faction, f);
        if (now >= cap || now <= Meitou.Data.Gameplay.FactionRelations.HostileThreshold) return;
        var (before, after) = data.Relations.Add(world.Player.Faction, f, Math.Min(amount, cap - now));
        string shown = name.StartsWith("The ", StringComparison.Ordinal) ? "the " + name[4..] : name;
        world.Post(after >= cap ? $"{char.ToUpperInvariant(shown[0])}{shown[1..]} count you as friends now. Not the kind who'd bleed for you, but friends."
            : $"{char.ToUpperInvariant(shown[0])}{shown[1..]} think a little better of you.");
        if (after >= cap && LwDeeds.For(world) is { } deeds) deeds.Record.Note($"Enough jobs done for {shown}: they count the squad as friends.");
        if (Settings.LogChanges) Log($"favour: {name} {before:0} -> {after:0}");
    }

    static string? FactionOf(World world, CharacterId who)
    {
        var data = LwState.For(world).Data;
        var table = world.Characters;
        if (data is null || !table.TryResolveNext(who, out int s) || table.Cold(s) is not { } cold) return null;
        return cold.Faction >= 0 && cold.Faction < data.Factions.Count ? data.Factions[cold.Faction].Name : null;
    }

    /// <summary>Gives `count` of an item to the effect's target (a barman's "on the house"); logged, nothing happens if there is no room.</summary>
    static void Give(DialogueAct act, string itemRecord, int count)
    {
        var data = LwState.For(act.World).Data;
        var table = act.World.Characters;
        if (data is null || !table.TryResolveNext(act.Target, out int slot) || table.Cold(slot)?.Inventory is not { } inventory) return;
        int given = 0;
        for (; given < count; given++)
        {
            if (data.Items.Create(itemRecord) is not { } item || !inventory.Add(item)) break;
        }
        if (Settings.LogChanges) Log($"gift: {given} of {count} x {data.Db.Find(itemRecord)?.Name ?? itemRecord}");
    }

    /// <summary>How far a character stands from an outlaw's ground (float.MaxValue when unknown: then reach does not limit and no bonus is paid).</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Numerics.Vector3[]> townAt = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether a speaker is near a town (talk about a boss stays near the boss's home): within 1.6 times the "Outlaw news range" (8 km by
    /// default; the map is about 30 km across) of any town of that name (several cannibal villages share one). A town the world does not have
    /// (renamed or removed by another mod) does not hold talk back.
    /// </summary>
    static bool NearTown(World world, CharacterId who, string town)
    {
        var data = LwState.For(world).Data;
        if (data is null || !world.Characters.TryResolveNext(who, out int s)) return true;
        var at = townAt.GetOrAdd(town, t =>
        {
            var list = new List<System.Numerics.Vector3>();
            for (int i = 0; i < data.Sites.Count; i++)
                if (data.Sites[i].Town.Name == t || data.Sites[i].Record.Name == t) list.Add(data.Sites[i].Position);
            return [.. list];
        });
        if (at.Length == 0) return true;
        var me = world.Characters.Next[s].Position;
        int km = (int)MathF.Ceiling(Math.Max(1, Settings.NewsReach) * 1.6f);
        foreach (var p in at)
        {
            float dx = me.X - p.X, dz = me.Z - p.Z;
            if (CampRules.InReach(MathF.Sqrt(dx * dx + dz * dz), km)) return true;
        }
        return false;
    }

    static float GroundDistance(World world, CharacterId who, int outlaw)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(who, out int s) || OutlawCampsSystem.For(world) is not { } camps) return float.MaxValue;
        var p = table.Next[s].Position;
        return camps.DistanceToGround(outlaw, p.X, p.Z);
    }

    /// <summary>Moving outlaw camps (OutlawCamps.cs), behind the "Outlaw camps" setting.</summary>
    static void RegisterCamps(ModContext c, List<CampCode> camps, CampNewsTable? news)
    {
        if (camps.Count == 0) return;
        if (news is not null)
        {
            // gossip that points (where a camp is) and outlaws who react (days since the squad struck them); the outlaw is found by name
            OutlawCampsSystem.NewsTowns = news.Towns;
            foreach (var n in news.Outlaws)
            {
                // a successor's own codes (their contract's reach and pay) go by the outlaw whose ground they took over
                bool isSuccessor = camps.All(x => x.Name != n.Name) && camps.Any(x => x.Successor?.Name == n.Name);
                if ((camps.FirstOrDefault(x => x.Name == n.Name) ?? camps.FirstOrDefault(x => x.Successor?.Name == n.Name)) is not { } o)
                {
                    // a contract target with no roaming camp (the cannibal leaders in their village): every giver may offer it, no road bonus
                    if (n.Reach > 0) c.AddDialogueCondition(n.Reach, check => check.Matches(true));
                    if (n.NewsReach > 0) c.AddDialogueCondition(n.NewsReach, check => check.Matches(true));   // they stay home: everyone knows
                    if (n.Pay > 0) c.AddDialogueEffect(n.Pay, _ => { });
                    continue;
                }
                int index = o.Index;
                string who = isSuccessor ? o.Successor!.Character : o.Character;
                if (n.Successor > 0 && !isSuccessor)
                    c.AddDialogueCondition(n.Successor, check => check.Matches(OutlawCampsSystem.For(check.World)?.SuccessorActive(check.World, index) == true));
                c.AddDialogueCondition(n.Town, check => check.Compare(OutlawCampsSystem.For(check.World)?.CampTown(index) ?? 0));
                c.AddDialogueCondition(n.Dir, check => check.Compare(OutlawCampsSystem.For(check.World)?.CampDir(index) ?? 0));
                if (n.Reach > 0)
                    c.AddDialogueCondition(n.Reach, check =>
                    {
                        float d = GroundDistance(check.World, check.Speaker, index);
                        return check.Matches(d == float.MaxValue || CampRules.InReach(d, Settings.ContractReach));
                    });
                if (n.NewsReach > 0)
                    c.AddDialogueCondition(n.NewsReach, check =>
                    {
                        // gossip travels a region or two, not across the map; unknown distance: nothing said (never everywhere)
                        float d = GroundDistance(check.World, check.Speaker, index);
                        return check.Matches(d != float.MaxValue && CampRules.InReach(d, Math.Max(1, Settings.NewsReach)));
                    });
                if (n.Pay > 0)
                    c.AddDialogueEffect(n.Pay, act =>
                    {
                        float d = GroundDistance(act.World, act.Speaker, index);
                        int road = Settings.DistancePay && d < float.MaxValue ? CampRules.DistanceBonus(act.Effect.Value, d) : 0;
                        int grown = Settings.GrowingBounties && OutlawCampsSystem.For(act.World) is { } camps
                            ? (int)Math.Round(act.Effect.Value * (isSuccessor ? camps.SuccessorGrowth(act.World, index) : camps.GrowthOf(act.World, index))) : 0;
                        if (road + grown <= 0) return;
                        act.World.Player.Money += road + grown;
                        string why = road > 0 && grown > 0 ? "for the long road and the price on their head" : road > 0 ? "for the long road" : "for the price on their head";
                        act.World.Post($"{road + grown} cats more {why}.");
                        if (Settings.LogChanges) Log($"contract extra: +{road} road ({d / CampRules.Km:0.0} km) +{grown} grown price, on {act.Effect.Value} for {o.Name}");
                    });
                if (n.Reprisal > 0)
                    c.AddDialogueCondition(n.Reprisal, check => check.Matches(OutlawCampsSystem.For(check.World)?.InReprisalOf(check.World, check.Speaker, index) == true));
                if (n.Tier > 0)
                    c.AddDialogueCondition(n.Tier, check => check.Compare(check.World.Uniques.StateOf(who) != Meitou.Simulation.Factions.UniqueState.Alive
                        ? 0 : OutlawCampsSystem.For(check.World)?.TierOf(check.World, index) ?? 0));
                c.AddDialogueCondition(n.Struck, check => check.Compare(check.World.Uniques.StateOf(who) != Meitou.Simulation.Factions.UniqueState.Alive
                    ? 1e6 : OutlawCampsSystem.For(check.World)?.DaysSinceStruck(check.World, index) ?? 1e6));
            }
            c.Log($"camp news: {news.Outlaws.Count} outlaws, {news.Towns.Count} towns");
        }
        c.AddSystem("lw-camps", ctx => new OutlawCampsSystem(camps, ctx.Find<PopulationSystem>(), ctx.Data), SystemAnchor.End);
        c.AddDebugCommand("Wasteland Remembers: outlaw camps", _ => OutlawCampsSystem.Latest?.Summary(null) ?? "no world yet", hasField: false);
        c.AddDebugCommand("Wasteland Remembers: outlaw successors", _ => !Settings.OutlawSuccessors ? "successors are off (the Squad record window's Settings tab)" : OutlawCampsSystem.Latest?.SuccessorSummary() ?? "no world yet", hasField: false);
        c.Log($"outlaw camps: {camps.Count} outlaws, {(Settings.OutlawCamps ? "on" : "off")}");
    }

    /// <summary>Rumours about named characters the squad killed (bleed-outs after its blow included, which Meitou's own state misses).</summary>
    static void RegisterNamedKills(ModContext c, List<NamedKillCode> named)
    {
        foreach (var n in named)
        {
            string name = n.Name;
            c.AddDialogueCondition(n.Killed, check => check.Matches(LwDeeds.For(check.World)?.Record.KilledNamed(name) == true));
            c.AddDialogueCondition(n.Days, check =>
            {
                double days = LwDeeds.For(check.World)?.Record.DaysSinceNamed(name, check.World.GameHours) ?? -1;
                return days >= 0 && check.Compare(days);
            });
        }
    }

    static void RegisterNotoriety(ModContext c, NotorietyTable? n)
    {
        if (n is null) return;
        var rules = n.Titles.Select(t => new TitleRule(t.Id, t.Name, t.Scope, t.Stat, t.Faction, t.Targets, t.At, t.Legendary)).ToList();
        c.AddSystem("lw-deeds", ctx => new LwDeedsSystem(rules, ctx.Data) { Talk = ctx.Find<DialogueSystem>() }, SystemAnchor.End);
        foreach (var t in n.Titles.Where(t => t.Tale > 0))
        {
            string id = t.Id;
            c.AddDialogueCondition(t.Tale, check => check.Matches(LwDeeds.For(check.World)?.Record.AnyHoldsTop(id) == true));
        }
        foreach (var t in n.Titles)
        {
            string id = t.Id;
            c.AddDialogueCondition(t.Code, check => check.Matches(LwDeeds.For(check.World)?.Holds(check.World, check.Subject, id) == true));
        }
        c.AddDialogueCondition(n.HeatCondition, check => check.Compare(Settings.Heat ? LwDeeds.For(check.World)?.HeatFor(check.World, check.Speaker) ?? 0 : 0));
        c.AddDialogueEffect(n.HeatEffect, act => { if (Settings.Heat) LwDeeds.For(act.World)?.HeatEffect(act.World, act.Speaker, act.Effect.Value); });
        c.AddUi(new StatsUi());
        RecordHotkey.Install(new HarmonyLib.Harmony("livingwasteland.recordhotkey"), Log);
        c.AddDebugCommand("Wasteland Remembers: squad record", _ =>
        {
            StatsUi.open = !StatsUi.open;
            c.Log("squad record:\n" + RecordHost.Panel + "\n\n" + RecordHost.CatalogText + "\n\nhistory:\n" + RecordHost.HistoryText);
            string build = DataBuild == HelperBuild ? $"build {HelperBuild}" : $"build {HelperBuild}, but the data is {DataBuild}: reinstall the folder";
            return (StatsUi.open ? "record window open (also written to log.txt)" : "record window closed") + "; " + build;
        }, hasField: false);
        c.Log($"notoriety: {rules.Count} titles, heat codes {n.HeatCondition}/{n.HeatEffect}");
    }

    static void RegisterVariables(ModContext c, List<VariableCode> variables, Dictionary<string, MomentCode>? variableMoments)
    {
        var moments = variableMoments ?? [];
        foreach (var v in variables)
        {
            string name = v.Name;
            c.AddDialogueCondition(v.Condition, check => check.Compare(LwState.For(check.World).Get(name)));
            moments.TryGetValue(name, out var moment);
            c.AddDialogueEffect(v.Set, act =>
            {
                var state = LwState.For(act.World);
                bool first = state.Get(name) == 0 && act.Effect.Value != 0;
                state.Set(act.World, name, act.Effect.Value);
                if (first && moment is not null) RecapLink.Moment(moment.Caption, moment.Place, moment.Weight);
            });
            c.AddDialogueEffect(v.Add, act =>
            {
                var state = LwState.For(act.World);
                bool first = state.Get(name) == 0 && act.Effect.Value != 0;
                state.Add(act.World, name, act.Effect.Value);
                if (first && moment is not null) RecapLink.Moment(moment.Caption, moment.Place, moment.Weight);
            });
        }
    }

    /// <summary>
    /// Quests paid by the length of their route (the "Distance pay" setting): where the job was taken, where each step was done and where it
    /// is handed in are noted (quest variables "@route quest i x|z", in metres, saved with the rest), and the hand-in pays the same bonus
    /// as a contract on the route's length: nothing under 5 km, up to double at 25 km.
    /// </summary>
    static void RegisterQuestRoutes(ModContext c, List<QuestRouteCode>? routes)
    {
        const int MaxPoints = 12;
        foreach (var q in routes ?? [])
        {
            string key = "@route " + q.Quest;
            c.AddDialogueEffect(q.Leg, act =>
            {
                var state = LwState.For(act.World);
                if (Where(act.World, act.Speaker) is not { } at) return;
                int n = act.Effect.Value == 1 ? 0 : state.Get(key);
                if (n >= MaxPoints) return;
                state.Set(act.World, $"{key} {n} x", (int)MathF.Round(at.X / 10));
                state.Set(act.World, $"{key} {n} z", (int)MathF.Round(at.Z / 10));
                state.Set(act.World, key, n + 1);
            });
            c.AddDialogueEffect(q.Pay, act =>
            {
                var state = LwState.For(act.World);
                int n = state.Get(key);
                if (n == 0) return;
                state.Set(act.World, key, 0);
                if (!Settings.DistancePay || Where(act.World, act.Speaker) is not { } end) return;
                var points = new List<(float X, float Z)>();
                for (int i = 0; i < n; i++) points.Add((state.Get($"{key} {i} x") * 10f, state.Get($"{key} {i} z") * 10f));
                points.Add((end.X, end.Z));
                float length = CampRules.RouteLength(points);
                int bonus = CampRules.DistanceBonus(act.Effect.Value, length);
                if (Settings.LogChanges) Log($"quest route: {q.Quest} {length / CampRules.Km:0.0} km, +{bonus} on {act.Effect.Value}");
                if (bonus <= 0) return;
                act.World.Player.Money += bonus;
                act.World.Post($"{bonus} cats more for the long road.");
            });
        }
    }

    static System.Numerics.Vector3? Where(World world, CharacterId who) =>
        world.Characters.TryResolveNext(who, out int s) ? world.Characters.Next[s].Position : null;

    static void RegisterTakes(ModContext c, List<TakeCode> takes)
    {
        foreach (var t in takes)
        {
            string item = t.Item;
            c.AddDialogueEffect(t.Code, act => TakeItems(act, item, act.Effect.Value));
        }
    }

    // Takes up to `count` units of an item record from the target's inventory (backpack included). Nothing happens if the target or
    // the inventory cannot be resolved.
    static void TakeItems(DialogueAct act, string itemRecord, int count)
    {
        var table = act.World.Characters;
        if (!table.TryResolveNext(act.Target, out int slot) || table.Cold(slot)?.Inventory is not { } inventory) return;
        int taken = 0;
        for (; taken < count; taken++)
        {
            var found = inventory.All().FirstOrDefault(it => it.Record == itemRecord);
            if (found is null || !inventory.RemoveOne(found)) break;
        }
        if (Settings.LogChanges) Log($"took {taken} of {count} x {itemRecord}");
    }

    sealed record CodeTable(List<VariableCode> Variables, List<TakeCode> TakeItems, JobTable? Jobs, List<RumourCode>? Rumours, NotorietyTable? Notoriety,
        List<OutlawCode>? Outlaws, List<WarCode>? Wars, GossipTable? Gossip, WarTownTable? WarTowns, List<NamedKillCode>? NamedKills, List<CampCode>? Camps, CampNewsTable? CampNews,
        TemperamentTable? Temperaments, HuntersTable? Hunters, MemorialTable? Memorials, TrophyTable? Trophies = null, Dictionary<string, MomentCode>? VariableMoments = null, AtlasTable? Atlas = null, List<RaidCode>? Raids = null, List<QuestRouteCode>? QuestRoutes = null, TalkTable? Talk = null, MarchTable? Marches = null, FavourTable? Favours = null, List<NearPlaceCode>? NearPlaces = null, string? Build = null, RivalTable? Rivals = null, NewsTable? News = null);
    sealed record NearPlaceCode(string Town, int Code);
    sealed record MarchTable(int Marching);
    sealed record FavourTable(int Code, int Cap, int Alms = 0);
    sealed record TalkTable(int Code, Dictionary<string, int>? Features);
    sealed record QuestRouteCode(string Quest, int Leg, int Pay);
    sealed record MomentCode(string Caption, string Place, int Weight);
    sealed record TrophyTable(string? Keepsake, string? Warrant = null, List<string>? Keepsakes = null);
    sealed record MemorialTable(int Mourn, int LossDays, int LossTown, int Feud, int Scar = 0, int SquadNear = 0);
    sealed record HuntersTable(string Squad);
    sealed record TemperamentTable(int Once, int Mark, int Despised, List<string>? Titles, Dictionary<string, List<string>>? Despises, List<GiftCode>? Gifts,
        Dictionary<string, Dictionary<string, List<string>>>? Views = null);

    /// <summary>Faction -> "admires" / "despises" / "fears" -> title names (the Atlas's faction pages).</summary>
    internal static Dictionary<string, Dictionary<string, List<string>>> TitleViews = [];
    sealed record GiftCode(int Code, string Item);
    sealed record WarTownTable(List<string> Factions, List<WarTownCode> Towns);
    sealed record WarTownCode(int Code, string Town);
    sealed record NamedKillCode(string Name, int Killed, int Days);
    sealed record GossipTable(int Code, int Dialogues);
    sealed record OutlawCode(int Code, int Index, string Name);
    sealed record WarCode(int Code, string Faction, bool Player);
    sealed record NotorietyTable(int HeatCondition, int HeatEffect, List<TitleCode> Titles);
    sealed record TitleCode(int Code, string Id, string Name, string Scope, string Stat, string? Faction, List<string>? Targets, int At, bool Legendary, int Tale = 0);
    sealed record VariableCode(string Name, int Initial, int Condition, int Set, int Add);
    sealed record TakeCode(int Code, string Item);
    sealed record JobTable(string Variable, int Cap, int CapCondition);
}

/// <summary>One world state the rumour lines talk about: its condition code, the state's record id and its name (for the log).</summary>
public sealed record RumourCode(int Code, string State, string Name);

/// <summary>The quest variables of one world: an <see cref="LwCore"/> plus the engine parts (messages, the world registry, the state hash).</summary>
public sealed class LwState
{
    public static Dictionary<string, int> Initial = [];
    static readonly ConditionalWeakTable<World, LwState> byWorld = new();
    readonly LwCore core = new(Initial);

    /// <summary>The state of the world that ticked last; the F10 rows read it (they run outside the tick).</summary>
    public static LwState? Latest { get; private set; }

    /// <summary>The state of a world: the one its system registered on the first tick, else a fresh one (a preview before the first tick).</summary>
    public static LwState For(World world) => byWorld.TryGetValue(world, out var s) ? s : new LwState();

    internal static void Register(World world, LwState state)
    {
        byWorld.AddOrUpdate(world, state);
        Latest = state;
    }

    public LwCore Core => core;

    /// <summary>The world's population system and data (set by LwSystem; the war conditions read them).</summary>
    public PopulationSystem? Population { get; internal set; }

    public PopulationData? Data { get; internal set; }

    public int Get(string name) => core.Get(name);

    public void Set(World world, string name, int value) => Report(world, name, core.Set(name, value));

    public void Add(World world, string name, int delta) => Report(world, name, core.Add(name, delta));

    void Report(World world, string name, (int Before, int After) change)
    {
        if (LwMod.Settings.LogChanges) LwMod.Log($"{name}: {change.Before} -> {change.After}");
        if (name == LwMod.JobVariable && LwMod.Settings.CapMessages && LwCore.CapMessage(change.Before, change.After, LwMod.Settings.JobCap) is { } m)
            world.Post(m);
    }

    /// <summary>One line for the F10 row; the full list goes to log.txt.</summary>
    public string Summary()
    {
        LwMod.Log($"open jobs: {core.FullList()}");
        return core.Summary(LwMod.Settings.JobCap);
    }

    public void Write(BinaryWriter w) => core.Write(w);

    public void Read(BinaryReader r) => core.Read(r);

    internal void Hash(ref StateHasher h)
    {
        var all = core.Ordered();
        h.Add(all.Count);
        foreach (var (k, v) in all) { h.Add(Rng.StableHash(k)); h.Add(v); }
    }
}

/// <summary>"Give up a job", queued from the F10 row and applied in the tick (the row itself must not touch the world).</summary>
public sealed record GiveUpJob(string Which) : SimCommand;

/// <summary>Squad roles: squad <paramref name="SquadId"/> escorts squad <paramref name="Target"/> (-1: escorts nobody). Saved in LwCore.</summary>
public sealed record SetEscort(int SquadId, int Target) : SimCommand;

/// <summary>The mod's system: owns the world's state, registers it with the handlers on the first tick, saves and hashes it.</summary>
public sealed class LwSystem(PopulationSystem? population, DialogueSystem? talk, PopulationData? data) : ITickSystem, IModSaveData, IStateHashed
{
    readonly LwState state = new();
    bool registered;

    /// <summary>Every 150 ticks (5 game seconds): note the first time each rumour's world state is true, so the lines can tell fresh news from old.</summary>
    readonly Dictionary<int, int> outlawDoing = [];
    static readonly string[] DoingNames = ["", "raiding", "lying low", "gathering men", "licking wounds"];

    /// <summary>Logs each outlaw's new activity when it changes (a play-test aid; the rumour lines read it directly).</summary>
    void OutlawLog(World world)
    {
        foreach (var (index, name) in LwMod.Outlaws)
        {
            int now = OutlawCampsSystem.DoingIn(world, index);
            if (outlawDoing.TryGetValue(index, out int before) && before == now) continue;
            outlawDoing[index] = now;
            if (LwMod.Settings.LogChanges) LwMod.Log($"outlaw news: {name} is {DoingNames[now]} (day {world.GameHours / 24:0.0})");
        }
    }

    bool buildWarned;

    public void SlowWorld(World world)
    {
        if (world.Tick % 900 == 0) OutlawLog(world);
        if (world.Tick % 60 == 10 && LogTally.Day(world.GameHours) is { } summary) LwMod.Log(summary);
        if (!buildWarned && world.Tick > 300)
        {
            buildWarned = true;
            // seen in play: a stale folder in Kenshi's mods made a whole session test the old build; the log said so, but nobody reads it mid-game
            if (LwMod.DataBuild != LwMod.HelperBuild)
                world.Post($"The Wasteland Remembers: the data (build {LwMod.DataBuild}) and code (build {LwMod.HelperBuild}) do not match. Copy the newest LivingWasteland folder into Kenshi's mods folder again.");
        }
        if (world.Tick % 60 == 30) Marches.Watch(world, population, data);
        if (world.Tick % 30 == 25) SquadRoles.Refresh(world, state.Core);
        if (world.Tick % 150 == 60 && LwMod.Settings.TownCriers) Criers.Look(world, talk, data);
        if (talk is not null && LwMod.Settings.BarTalk && world.Tick % BarPass == 0) BarTalk(world);
        if (population is null || LwMod.Rumours.Count == 0 || world.Tick % 150 != 0) return;
        // the first poll of a world: whatever is already true happened before the mod was watching, so it is old news
        bool first = state.Core.Get(LwCore.RumoursStarted) == 0;
        int old = 0;
        foreach (var r in LwMod.Rumours)
        {
            if (!population.States.IsTrue(r.State)) continue;
            if (first) { state.Core.MarkOld(r.State); old++; }
            else if (state.Core.RecordSeen(r.State, world.GameHours) && LwMod.Settings.LogChanges)
                LwMod.Log($"rumour: '{r.Name}' is true from day {world.GameHours / 24:0.0}");
        }
        if (first)
        {
            state.Core.Set(LwCore.RumoursStarted, 1);
            LwMod.Log($"rumours: first look at this world, {old} subjects already true (old news)");
        }
    }

    // ---- bar talk (BarTalk.cs) ----
    const int BarPass = 90;                // ticks between passes (3 game seconds)
    const int BarLinesPerPass = 2;         // at most this many bars start a line in one pass
    const float BarRetrySeconds = 10;      // a bar where nobody had anything to say is tried again after this
    const int BarTalkEvent = 12;           // EV_BAR_TALK
    readonly BarPacer<CharacterId> bars = new();
    int barLines;

    void BarTalk(World world)
    {
        var table = world.Characters;
        var hot = table.Previous;
        var players = new Dictionary<string, CharacterId>();
        var patrons = new Dictionary<string, List<CharacterId>>();
        for (int s = 0; s < hot.Length; s++)
        {
            if (!hot[s].Alive || hot[s].Dormant || table.Cold(s) is not { } cold) continue;
            if (world.Sight.BuildingOf(s) is not { } building) continue;
            if (cold.IsPlayer) players.TryAdd(building, table.IdOf(s));
            else if (patrons.TryGetValue(building, out var list)) list.Add(table.IdOf(s));
            else patrons[building] = [table.IdOf(s)];
        }

        float perSecond = 1f / world.TickSeconds;
        long buildingPause = (long)(LwMod.Settings.BarTalkPause * perSecond), npcPause = buildingPause * 4, retry = (long)(BarRetrySeconds * perSecond);
        int said = 0;
        foreach (var (building, player) in players)
        {
            if (said >= BarLinesPerPass) break;
            if (!patrons.TryGetValue(building, out var here) || !bars.BuildingReady(building, world.Tick)) continue;
            bool spoke = false;
            foreach (var npc in BarPacer<CharacterId>.Order(here, world.Tick, building.Length))
            {
                if (!bars.NpcReady(npc, world.Tick) || Sleep.Is(world, npc) || !TalkPacer.Exempt(() => talk!.FireEvent(world, npc, player, BarTalkEvent))) continue;
                bars.Said(building, npc, world.Tick, buildingPause, npcPause);
                spoke = true;
                said++;
                LogTally.Add("bar lines");
                if (++barLines == 1 || barLines % 25 == 0) LwMod.Log($"bar talk: {barLines} lines so far (latest in {building})");
                break;
            }
            if (!spoke) bars.Nothing(building, world.Tick, retry);
        }
        if (world.Tick % (BarPass * 100) == 0) bars.Prune(world.Tick);
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        if (!registered)
        {
            LwState.Register(world, state);
            state.Population = population;
            state.Data = data;
            registered = true;
        }
        foreach (var c in commands)
        {
            if (c is SetEscort e) { state.Core.Set(SquadRoles.EscortKey(e.SquadId), e.Target + 1); continue; }
            if (c is not GiveUpJob g) continue;
            string? gone = state.Core.GiveUp(g.Which, LwMod.JobVariable);
            LwMod.Log(gone is null ? $"give up: nothing matched '{g.Which}'" : $"gave up: {gone}");
            world.Post(gone is null ? "No open job matches that." : $"You give up the job: {gone}. Someone else can take it now.");
        }
    }

    public void Write(Stream stream) { using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true); state.Write(w); }
    public void Read(Stream stream) { using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true); state.Read(r); }
    public void Hash(ref StateHasher hasher) => state.Hash(ref hasher);
}
