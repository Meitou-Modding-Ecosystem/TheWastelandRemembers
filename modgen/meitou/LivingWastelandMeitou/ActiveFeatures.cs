using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

/// <summary>Registration and settings at a glance. Only the simulation reads the world's systems.</summary>
internal static class ActiveFeatures
{
    sealed record Feature(string Name, Func<LwSettings, bool> Enabled, string? System = null,
        string? Hook = null, Func<bool>? Bound = null, Func<LwSettings, bool>? Prerequisite = null, string? Needs = null,
        Func<string?>? Pending = null);

    static readonly Feature[] Features =
    [
        new("Squad Record, titles and History", _ => true, "lw-deeds"),
        new("Atlas", s => s.Atlas, "lw-atlas"),
        new("Live Atlas portraits", s => s.LiveAtlasPortraits, "lw-atlas", "live portraits",
            () => AtlasLiveFaces.Available, s => s.Atlas, "Atlas"),
        new("Added talk", s => s.Talk > 0, "lw-state"),
        new("Rumours and gossip", s => s.Gossip > 0, "lw-state", Prerequisite: s => s.Talk > 0, Needs: "Added talk"),
        new("Bar chat", s => s.BarTalk, "lw-state", Prerequisite: s => s.Talk > 0, Needs: "Added talk"),
        new("Victory lines", s => s.VictoryLines, "lw-deeds", Prerequisite: s => s.Talk > 0, Needs: "Added talk"),
        new("Enemy victory lines", s => s.EnemyVictoryLines, "lw-deeds"),
        new("Lookout calls", s => s.LookoutCalls, "lw-deeds", Prerequisite: s => s.Talk > 0, Needs: "Added talk"),
        new("Town criers", s => s.TownCriers, "lw-state", Prerequisite: s => s.Talk > 0, Needs: "Added talk"),
        new("Story alert sounds", s => s.StoryAlertSounds, Hook: "interface sounds", Bound: () => StorySounds.Available),
        new("Squad memorials", s => s.Memorials, "lw-deeds"),
        new("Factions react to titles", s => s.TitleReactions, "lw-deeds"),
        new("Quests and outlaw contracts", s => s.Jobs, "lw-state"),
        new("Contract favours", s => s.ContractFavours, "lw-state"),
        new("Alms and offerings", s => s.Alms, "lw-state"),
        new("Moving outlaw camps", s => s.OutlawCamps, "lw-camps"),
        new("Outlaw reprisals", s => s.OutlawReprisals, "lw-camps"),
        new("Outlaw successors", s => s.OutlawSuccessors, "lw-camps"),
        new("Town heat", s => s.Heat, "lw-deeds"),
        new("News from other mods", s => s.OtherModsNews, "lw-news"),
        new("Squadmates remember each other", s => s.SquadMemories, "lw-deeds"),
        new("Anniversaries", s => s.Anniversaries, "lw-deeds"),
        new("Fallen leaders remembered", s => s.FallenLeaderStories, "lw-news"),
        new("Outpost history", s => s.RememberOutposts, "lw-outpost-memories", "player town tags"),
        new("Outpost defence record", s => s.OutpostDefenceRecord, "lw-outpost-memories", "player town tags",
            Prerequisite: s => s.RememberOutposts, Needs: "Outpost history"),
        new("Outpost couriers", s => s.OutpostCouriers, "lw-couriers", "platoon dispatch",
            Prerequisite: s => s.RememberOutposts && s.OtherModsNews, Needs: "Outpost history and News from other mods"),
        new("Town handovers", s => s.TownHandovers, "lw-town-switch", "town switch notices"),
        new("World map marks", s => s.MapMarks, "lw-news", "map markers", () => NightlyHooks.MarksRegistered),
        new("Ask for news in the character menu", s => s.AskForNews, "lw-news", "character menu actions", () => NightlyHooks.AskRegistered),
        new("Ask for news in a conversation", s => s.AskForNews, "lw-news", "dialogue replies", () => NightlyHooks.ConversationAskRegistered),
        new("Faction relation news", s => s.RelationNews, "lw-relations", "relation change notices"),
        new("Animal companions remembered", s => s.AnimalCompanions, "lw-deeds"),
        new("Witnesses remember faces", s => s.WitnessesRememberFaces, "lw-lifecycle-held", "crime notices and lasting character IDs", () => WitnessMemories.Available,
            Prerequisite: s => s.Talk > 0, Needs: "Added talk"),
        new("Stolen goods", s => s.TheftHeat, "lw-theft-watch", "theft notices"),
        new("Prices by reputation", s => s.ReputationPrices, "lw-reputation-price", "trade price filtering"),
        new("Rivals", s => s.Rivals, "lw-rivals"),
        new("Saved character tags", s => s.SavedCharacterTags, Hook: "saved character tags", Bound: () => OwnedCharacterTags.PreviewAvailable),
        new("Rival recruitment", s => s.RecruitRivals, "lw-rivals", "recruitment and character menu actions", () => RivalOffers.Available,
            s => s.Rivals, "Rivals"),
        new("Bounty hunters", s => s.BountyHunters, "lw-hunters"),
        new("Faction raids", s => s.HarassmentRaids, "lw-raids"),
        new("Trophies and keepsakes", s => s.Trophies, "lw-deeds"),
        new("Keepsake stories", s => s.KeepsakeProvenance, "lw-keepsakes", Prerequisite: s => s.Trophies, Needs: "Trophies and keepsakes"),
        new("Visible neck charms", s => s.VisibleNeckCharms, "lw-neck-meshes", "bone meshes",
            Prerequisite: s => s.Trophies, Needs: "Trophies and keepsakes"),
        new("Prisoner cases", s => s.PrisonerCases, "lw-prisoner-cases"),
        new("Prisoner deals", s => s.PrisonerDeals, "lw-prisoner-cases", "prisoner menu actions", () => PrisonerMenu.Available,
            s => s.PrisonerCases, "Prisoner cases"),
        new("Western Hive envoys", s => s.HiveEnvoys, "lw-hive-envoys", "platoon dispatch",
            Prerequisite: s => s.PrisonerCases && s.RememberOutposts, Needs: "Prisoner cases and Outpost history"),
        new("Outlaw recovery parties", s => s.PrisonerRecovery, "lw-prisoner-recovery", "platoon dispatch and cage release",
            Prerequisite: s => s.PrisonerCases && s.RememberOutposts, Needs: "Prisoner cases and Outpost history"),
        new("Shek gate recovery", s => s.ShekGateRecovery, "lw-prisoner-recovery", "platoon dispatch and cage release",
            Prerequisite: s => s.PrisonerRecovery && s.PrisonerCases && s.RememberOutposts, Needs: "Outlaw recovery parties"),
        new("Covert prisoner recovery", s => s.CovertRecovery, "lw-prisoner-recovery", "platoon dispatch and lock work spots",
            () => CovertRecovery.Available, s => s.PrisonerRecovery && s.PrisonerCases && s.RememberOutposts, "Outlaw recovery parties"),
        new("Freed prisoner escorts", s => s.PrisonerEscorts, "lw-prisoner-escorts", "platoon dispatch"),
        new("Kindness remembered", s => s.KindnessRemembered, "lw-aid-use", "lasting character IDs and aid notices", () => KindnessWatch.Available),
        new("Squad Record in the game's panels", s => s.RecordInPanels, Hook: "panel lines", Bound: () => PanelLines.Available),
        new("Panel icons", s => s.PanelIcons, Hook: "line icons and art", Bound: () => PanelLines.IconsAvailable),
        new("Titles in character names", s => s.TitlesInCharacterNames, Hook: "character-specific display text", Bound: () => CharacterTitleDisplay.Available,
            Pending: () => CharacterTitleDisplay.Registered ? null : "Restart to apply this setting"),
        new("Story task bars", _ => true, Hook: "task bars", Bound: () => StoryTaskBar.Available),
        new("Squad roles", _ => true, "lw-state", "squad autonomy orders", () => SquadRoles.Available),
        new("Playtest diagnostics", s => s.PlaytestDiagnostics, "lw-state"),
        new("Lighter checks", s => s.LighterChecks, "lw-state"),
    ];

    static volatile IReadOnlySet<string>? systems;
    static readonly bool earlyClient = typeof(World).Assembly.GetName().Version is { Major: 0, Minor: <= 17 };

    internal static void Clear() => systems = null;

    internal static void Publish(World world)
    {
        var started = world.Systems.Select(SimulationExtensions.FactoryOf).OfType<SystemFactory>()
            .Where(f => f.Owner == "LivingWastelandMeitou").Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        systems = started;
        foreach (var line in Describe(started, LwMod.Settings, earlyClient))
            LwMod.Log("feature status: " + line);
    }

    internal static string[] Lines() => systems is { } started
        ? Describe(started, LwMod.Settings, earlyClient, localise: true)
        : [UiLanguage.Text("Feature status will appear when the world starts.")];

    internal static string[] Describe(IReadOnlySet<string> started, LwSettings settings, bool early, bool localise = false)
    {
        string Text(string key) => localise ? UiLanguage.Text(key) : key;
        string Reason(string key, string reason) => localise ? UiLanguage.Format(key, Text(reason)) : string.Format(key, reason);
        return Features.Select(feature =>
        {
            string state;
            if (!feature.Enabled(settings)) state = Text("Off in Settings");
            else if (feature.System is { } name && !started.Contains(name) || feature.Bound?.Invoke() == false)
                state = feature.Hook is { } hook
                    ? Reason(early ? "Warning: needs a newer Meitou for {0}" : "Warning: unavailable on this Meitou, missing {0}", hook)
                    : Text("Warning: unavailable, the system did not start");
            else if (feature.Prerequisite?.Invoke(settings) == false) state = Reason("Off, turn on {0}", feature.Needs ?? "");
            else if (feature.Pending?.Invoke() is { } pending) state = Text(pending);
            else state = Text("On");
            return $"{Text(feature.Name)} · {state}";
        }).ToArray();
    }
}
