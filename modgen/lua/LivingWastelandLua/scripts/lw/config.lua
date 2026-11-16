-- Living Wasteland (Lua): settings. EVERYTHING that changes the game is OFF by default (draft).
-- Turn a feature on only after the probe results (see PLAYTEST_GUIDE.md) show the API behaves as expected.
return {
    debug = true,
    journalKeyCode = 21,   -- quest journal key: Y (OIS code 21). F6 is Cheat Menu, F7 hides the UI, F8 and F10 are KFS; U N X T G are True Adventurers.

    enabled = {
        notoriety = false,   -- earned titles for the most significant bosses (messages and dialogue scripts)
        heat      = false,   -- track crimes witnessed by factions as heat
        hunters   = false,   -- spawn hunter squads for factions whose heat is high (needs heat = true)
        nemesis   = false,   -- remember who beat you; they come back stronger, near where you last met
        outlaws   = false,   -- make sure a named outlaw is out there; remember which you have killed
        drops     = false,   -- replaced by data (Cannibal Chief and Grand Wizard drop an arm; Rhinobot already drops his CPU); leave off (Rhinobot's CPU, a cannibal's arm)
        captives  = false,   -- helpers for faction-aware captive lines (dialogue scripts call LW.captive)
        base      = false,   -- replaced by data (x_native: base reactions by town level and tech level); leave off
        recruits  = true,    -- the 12 recruits: a joining message, loyalty, and leaving if mistreated
        domepapers = false,  -- put the Ashlands papers on the dome heads
        tracker   = true,    -- quest journal: notices when your quests change; Y opens the journal window (LW.journal())
        calm      = true,       -- (try after the game has loaded) the whole town does not bark at you the moment your squad appears
        ideology  = false,   -- NPCs of the right factions remark on the ideology you declare (LW.ideology.set)
        body      = false,   -- replaced by data (content/body_voices.json); leave off
        chatter   = true,    -- squad chatter rules: no arrival/status barks from a squad member who is alone, and a cap per game hour
    },

    -- ===== notoriety =====
    -- Weight 3 = a boss the whole wasteland talks about; weight 1 = notable. A match is a case-insensitive substring of the
    -- character's name. epithet = the name you earn for that kill.
    bosses = {
        { match = "phoenix",   weight = 3, epithet = "Phoenix-Slayer" },
        { match = "esata",     weight = 3, epithet = "Esata's Bane" },
        { match = "tengu",     weight = 3, epithet = "the Emperor's Reckoning" },
        { match = "cat-lon",   weight = 3, epithet = "Cat-Lon's Undoing" },
        { match = "eyegore",   weight = 3, epithet = "Eyegore's Reaper" },
        { match = "longen",    weight = 2, epithet = "the Slave-Master's Ruin" },
        { match = "savant",    weight = 2, epithet = "the Tailor-Killer" },
        { match = "hat-12",    weight = 2, epithet = "the Ash-Dome Breaker" },
        { match = "jang",      weight = 2, epithet = "the General's End" },
        { match = "rhinobot",  weight = 2, epithet = "the Machine's End" },
        { match = "head of agriculture", weight = 2, epithet = "the Harvest Breaker" },
        { match = "valtena",   weight = 2, epithet = "Valtena's Fall" },
        { match = "seta",      weight = 2, epithet = "the Inquisitor's End" },
        { match = "tinfist",   weight = 2, epithet = "Tinfist's End" },
        { match = "flying bull", weight = 2, epithet = "the Bull-Breaker" },
        { match = "moll",      weight = 1, epithet = "Moll's End" },
        { match = "grande",    weight = 1, epithet = "Grande's End" },
        { match = "ponk",      weight = 1, epithet = "Ponk's End" },
        { match = "screamer",  weight = 1, epithet = "the Screamer's End" },
    },
    -- the title grows with the total score; the highest tier reached wins. {minScore, text}. "%s" = the latest epithet.
    titleTiers = {
        { 3,  "%s" },
        { 7,  "the Kingbreaker" },
        { 12, "Wasteland's Bane" },
        { 20, "the Nameless Death" },
    },
    killCreditDays = 0.5,      -- a boss counts as your kill if you (or your squad) hit it within this many days of its death

    -- ===== heat =====
    crimeHeat = {              -- per witnessed crime, CrimeEnum name -> heat added for the witnessing faction
        CRIME_ASSAULT = 4, CRIME_ASSAULT_VIP = 8, CRIME_MURDER = 10, CRIME_STEALING = 5, CRIME_LOOTING = 4,
        CRIME_ENSLAVING = 7, CRIME_SLAVE_FREEING = 6, CRIME_TERRORISM = 12, CRIME_SMUGGLING = 3,
        CRIME_LOCKPICKING = 2, CRIME_TRESSPASSING = 1, CRIME_ESCAPE_PRISON = 3,
    },
    heatDecayPerDay = 1.0,
    heatTiers = { cold = 10, hostile = 25, hunted = 45 },   -- cold: relation penalty; hostile: hunters; hunted: declare enemy
    coldRelationPenalty = 10,

    -- ===== hunters =====
    -- faction name (as the game knows it) -> list of squad template names to try, in order. Names are checked at spawn.
    hunterSquads = {
        ["The Holy Nation"]    = { "Inquisitor roam (phoenix alive)", "Temple Inquisitor" },
        ["Shek Kingdom"]       = { "Shek Patrol", "Bounty Hunter Solo Shek" },
        ["United Cities"]      = { "Mercenary Police", "police patrol unit" },
        ["Slave Traders"]      = { "slave hunters (N)", "slave hunters (S)" },
        ["Anti-Slavers"]       = { "Anti slaver all strong small" },
        ["Cannibal Hunters"]   = { "cannibal hunter patrol" },
        ["Tech Hunters"]       = { "Tech Hunter Guards small" },
        ["Mercenary Guild"]    = { "Mercenary patrol" },
        ["Bounty Hunters"]     = { "Bounty hunters low", "Bounty hunters wandering between towns" },
        ["Manhunters"]         = { "manhunter patrol (N)", "manhunter patrol (S)" },
    },
    hunterCheckHours = 6,      -- how often to consider a spawn
    hunterCooldownDays = 2.5,  -- per faction, at the 'hostile' tier (halved at 'hunted')
    hunterSpawnDistance = { 160, 260 },   -- units from your leader, out of sight
    hunterSquadMax = 6,
    hunterChancePerCheck = 0.5, -- between rare and common

    -- ===== nemesis =====
    nemesisMaxRemembered = 6,
    nemesisBuffPerDefeat = 0.06,   -- +6% to their stats per time they beat you (cap below)
    nemesisBuffCap = 0.40,
    nemesisAmbushMinDays = 2,
    nemesisAmbushRadius = 700,     -- you must be within this many units of where you last met
    nemesisAmbushChance = 0.5,
    ambushSquads = {               -- faction -> squad template (their own friends turn up with them)
        ["Dust Bandits"] = { "Dust Bandit squad" },
    },

    -- ===== outlaws =====
    outlawCheckHours = 12,
    outlawCooldownDays = 3,          -- at most one named outlaw sent out per this many days
    outlawChance = 0.35,
    outlawDistance = { 450, 800 },   -- units from your leader: out in the open, away from towns
    outlawRespawn = false,           -- once killed they stay dead (the game's own states agree)

    -- ===== drops =====
    drops = {
        { match = "rhinobot", item = "CPU of Rhinobot", chance = 1.0 },
        { faction = "Cannibals", item = "Severed Cannibal Right Arm", chance = 0.25 },
    },

    -- ===== recruits =====
    recruitLoyaltyStart = 60,
    recruitLoyaltyKo = -8,           -- lose this when knocked out far from your leader
    recruitLoyaltyWin = 3,
    recruitLeaveBelow = 10,

    -- ===== base reactions =====
    baseCooldownDays = 1.5,
    baseChance = 0.5,

    -- ===== calm =====
    calmGraceSeconds = 25,                -- after a game starts or loads, no NPC sighting barks for this many real seconds
    calmBurst = 2,                        -- afterwards at most this many NPCs bark within the window (0 = no limit)
    calmWindowSeconds = 4,
    calmEvents = { "EV_I_SEE_NEUTRAL_SQUAD", "EV_I_SEE_ALLY_PLAYER" },

    -- ===== ideology =====
    ideologyChance = 35,                 -- percent chance an eligible NPC speaks when the game sends a watched event
    ideologyPerHour = 2,                 -- at most this many ideology remarks per in-game hour
    ideologyCooldownHours = 8,           -- the same NPC does not remark again for this long
    ideologyEvents = { "EV_I_SEE_NEUTRAL_SQUAD", "EV_I_SEE_ALLY_PLAYER", "EV_BAR_TALK" },

    -- ===== body =====
    bodyPollHours = 0.1,          -- how often to look at the squad's health (in-game hours)
    bodyBloodFraction = 0.45,     -- below this share of maximum blood a member says they are hurt
    bodyCooldownHours = 2,        -- the same member repeats a hurt/crippled line no sooner than this
    bodyPerHour = 3,              -- at most this many body barks per in-game hour for the whole squad

    -- ===== chatter =====
    chatterAloneRadius = 350,        -- a squad member with no squadmate within this many units stays quiet (arrival/status barks only)
    ambientBurst = 0,                -- 0 = off (the calm feature now does this by default). N = when a squad of NPCs sees you, at most N of them bark within the window (needs chatter on)
    ambientWindowSeconds = 4,
    chatterPerHour = 5,              -- at most this many such barks from your squad per in-game hour
    chatterEvents = { "EV_ENTER_BIOME", "EV_I_SEE_NEUTRAL_SQUAD", "EV_I_DEFEATED_SQUAD", "EV_GET_UP_PEACE", "EV_GET_UP_FIGHT",
                      "EV_HEALING_OTHER_START", "EV_HEALING_OTHER_FINISHED", "EV_BEING_HEALED_START", "EV_BEING_HEALED_FINISHED",
                      "EV_LOOTING_EVERYTHING", "EV_WINDY", "EV_I_SEE_ANIMAL_SQUAD" },
}
