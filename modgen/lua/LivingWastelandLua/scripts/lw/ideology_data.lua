-- Ideology reactions: what each faction says when it meets someone who has declared a faction ideology.
-- Policy ids follow the Kenshi Faction System's lists so the same ids can be used with either. Each entry is
-- { policy = id, faction = key, tone = "approve" | "hostile", lines = { ... } }. Short, dry vanilla-tone lines.
local D = {}

D.majors = { "merit", "aristocracy", "freefolk", "okran", "liberation", "technology", "anti_okran",
             "human_supremacy", "anti_human", "imperial", "cannibalism", "anti_cannibal" }
D.minors = { "equality", "male_supremacy", "female_supremacy", "machine", "anti_slavery", "slavery", "multiracial",
             "free_trade", "standing_army", "martial_honor", "free_thought", "traditional", "pro_skeleton", "anti_skeleton",
             "pro_outlaw", "anti_outlaw" }

-- faction key -> lowercase substrings of the faction's display name (checked in order; first match wins)
D.factions = {
    { key = "cannibal_hunters", match = { "cannibal hunters" } },
    { key = "cannibals",        match = { "cannibals" } },
    { key = "holy",             match = { "holy nation" } },
    { key = "uc",               match = { "united cities" } },
    { key = "shek",             match = { "shek kingdom", "shek" } },
    { key = "anti",             match = { "anti-slavers", "anti slavers" } },
    { key = "slave_traders",    match = { "slave traders" } },
    { key = "hunters",          match = { "tech hunters" } },
    { key = "flotsam",          match = { "flotsam" } },
    { key = "hive",             match = { "western hive", "southern hive" } },
    { key = "skeletons",        match = { "skeletons", "skeleton" } },
    { key = "dust",             match = { "dust bandits" } },
}

D.lines = {
    -- Holy Nation
    { policy = "okran", faction = "holy", tone = "approve", lines = {
        "I hear your people walk in Okran's light. Good. Keep walking.",
        "Faithful, and not even ours. Remarkable.",
        "Okran sees who kneels. Remember that." } },
    { policy = "anti_okran", faction = "holy", tone = "hostile", lines = {
        "Heretic. The word is out about your people. Keep your distance.",
        "You deny Okran openly. A brave sin, and a short one.",
        "The Inquisition keeps a list. You are on it." } },
    { policy = "technology", faction = "holy", tone = "hostile", lines = {
        "Old machines worshipped as miracles. That is not faith, that is salvage.",
        "Revive the old tech if you like. Not in Nation lands.",
        "Okran did not ask for your gears." } },
    { policy = "imperial", faction = "holy", tone = "hostile", lines = {
        "Emperor talk. The Nation answers to Okran, not to crowns.",
        "Imperial dreams. Bring them to our border and see.",
        "Another would-be emperor. We have met the type." } },
    { policy = "anti_human", faction = "holy", tone = "hostile", lines = {
        "A creed against mankind. The Nation will not hear it.",
        "You hate your own kind? Then we have nothing to say.",
        "Keep that preaching out of Nation towns." } },
    { policy = "male_supremacy", faction = "holy", tone = "approve", lines = {
        "A house with a firm head. The Nation approves.",
        "Order between the sexes. You are not a savage, then.",
        "Women kept in their place, I am told. Good." } },
    { policy = "equality", faction = "holy", tone = "hostile", lines = {
        "Women equal to men? That is Narko's talk.",
        "Equal, you say. The faith says otherwise.",
        "A house with no head. Narko would be pleased." } },
    { policy = "female_supremacy", faction = "holy", tone = "hostile", lines = {
        "Women over men. That is Narko's work.",
        "A house ruled by women. Okran forgive us for hearing it.",
        "Say that in the Nation and see how long it lasts." } },

    -- United Cities
    { policy = "aristocracy", faction = "uc", tone = "approve", lines = {
        "Lords and ladies in their seats. Order from the top. I understand it.",
        "A noble house, then. The Cities remember when that was normal.",
        "Rule by the high born. Not our way now, but I will not argue." } },
    { policy = "liberation", faction = "uc", tone = "hostile", lines = {
        "Free the slaves, you say. Who then pays for the Cities?",
        "Slave liberators. The Guild has a list for those.",
        "That sort of talk costs a man his licence." } },
    { policy = "slavery", faction = "uc", tone = "approve", lines = {
        "Labour has a price. A sensible creed.",
        "Slaves are lawful here, as you know. Welcome.",
        "Sound business, that." } },
    { policy = "anti_slavery", faction = "uc", tone = "hostile", lines = {
        "Against the trade? Bad for your prices.",
        "The Cities are built on that trade. Mind your tongue.",
        "Abolition. Hm. Not in my shop." } },
    { policy = "anti_human", faction = "uc", tone = "hostile", lines = {
        "Hating humans in a human city. Bold.",
        "Careful what you say about people in the Cities.",
        "A creed against mankind. You will not sell it here." } },
    { policy = "free_trade", faction = "uc", tone = "approve", lines = {
        "Open roads, open markets. That I can drink to.",
        "Free trade. Now there is a creed worth a handshake.",
        "No tariffs? You are my kind of faction." } },

    -- Shek
    { policy = "merit", faction = "shek", tone = "approve", lines = {
        "Strength decides, you say. The Stone Golem would nod.",
        "A warrior's rule. You have the look.",
        "The strong lead. Good. That is how it is done." } },
    { policy = "human_supremacy", faction = "shek", tone = "hostile", lines = {
        "Flatskins above all. We have a horn for that kind of talk.",
        "You hold us below your kind. We heard.",
        "A faction that sets us lower. Remember where you say that." } },
    { policy = "anti_human", faction = "shek", tone = "approve", lines = {
        "You scorn the flatskins? You speak like family.",
        "No love for the flatskins. We understand each other.",
        "A creed against them. Stranger, sit." } },
    { policy = "multiracial", faction = "shek", tone = "approve", lines = {
        "A band of every kind. We do not mind horns at your table.",
        "All kinds, one banner. That is worth a nod.",
        "You count us in. We noticed." } },
    { policy = "standing_army", faction = "shek", tone = "approve", lines = {
        "A standing army. Those who can fight should be ready.",
        "Soldiers always ready. That is respect.",
        "A faction with a sword at the door. Good." } },
    { policy = "martial_honor", faction = "shek", tone = "approve", lines = {
        "Honour in the fight. Your people speak our language.",
        "A creed of the blade. The Stone Golem approves.",
        "Fight cleanly and keep your word. That is a creed." } },

    -- Anti-Slavers
    { policy = "liberation", faction = "anti", tone = "approve", lines = {
        "You free slaves. Then we are not strangers.",
        "Chains cut. That is the only good news this year.",
        "A faction that frees. Say it again so I know I heard." } },
    { policy = "anti_slavery", faction = "anti", tone = "approve", lines = {
        "Against the trade. You will find friends here.",
        "No chains in your house. That is rare.",
        "One more voice against it. Welcome." } },
    { policy = "slavery", faction = "anti", tone = "hostile", lines = {
        "You keep slaves. Then you are our work.",
        "A slaver's creed. We do not talk to those.",
        "We have cut chains from kinder hands than yours." } },

    -- Slave Traders
    { policy = "liberation", faction = "slave_traders", tone = "hostile", lines = {
        "Liberators. Bad for stock.",
        "You free what we paid for. We remember that.",
        "Your kind costs us money. Walk on." } },
    { policy = "anti_slavery", faction = "slave_traders", tone = "hostile", lines = {
        "Against the trade? We heard. We are not amused.",
        "Abolition talk. Not near my wagons.",
        "You would ruin an honest business." } },
    { policy = "slavery", faction = "slave_traders", tone = "approve", lines = {
        "A house that understands the trade. Come buy.",
        "Fair market. You are welcome.",
        "Good. Somebody sensible." } },

    -- Tech Hunters
    { policy = "technology", faction = "hunters", tone = "approve", lines = {
        "Revive the old tech? Finally someone sensible.",
        "Bring the labs back to life. I will drink to that.",
        "A faction that wants the old science. Rare." } },
    { policy = "machine", faction = "hunters", tone = "approve", lines = {
        "Machines treated with respect. Odd, in this world.",
        "Machine worship. Eccentric, but I like it.",
        "Someone who appreciates the old gears." } },
    { policy = "free_trade", faction = "hunters", tone = "approve", lines = {
        "Free trade. More buyers for what we dig up.",
        "No tariffs on ruins? Good.",
        "That is a creed a hunter can live with." } },
    { policy = "free_thought", faction = "hunters", tone = "approve", lines = {
        "Think what you like. That is how we found anything.",
        "Free thought, and free salvage. Good.",
        "Nobody tells you what is forbidden. Sensible." } },
    { policy = "imperial", faction = "hunters", tone = "approve", lines = {
        "Imperial revival. They say the old empires had good machines.",
        "Bring back the old order? If it brings the old tech, fine.",
        "Imperial banners, old labs. I can work with that." } },
    { policy = "okran", faction = "hunters", tone = "hostile", lines = {
        "Okranites and old machines mix badly. Choose carefully.",
        "Okran folk burn what we dig up. Mind your friends.",
        "Faith first, science never. We are not friends." } },

    -- Flotsam
    { policy = "anti_okran", faction = "flotsam", tone = "approve", lines = {
        "Down with Okran. Say it again so I know I heard.",
        "Finally. Somebody says it loud.",
        "Okranite scum fear folk who say that." } },
    { policy = "okran", faction = "flotsam", tone = "hostile", lines = {
        "Okran folk. Our people fled that faith. Stay away from the village.",
        "You follow the Nation's god. We remember what that cost us.",
        "Okranite. Keep your hand off your blade." } },

    -- Hives
    { policy = "multiracial", faction = "hive", tone = "approve", lines = {
        "All kinds of hands in one band. The hive notes it.",
        "You do not shut out the hive. That is rare.",
        "A faction with room for us. Noted." } },
    { policy = "human_supremacy", faction = "hive", tone = "hostile", lines = {
        "Humans above the hive. The hive does not agree.",
        "Your kind rules others. We will not kneel.",
        "A creed that sets us low. The hive remembers." } },
    { policy = "anti_human", faction = "hive", tone = "approve", lines = {
        "You have no love for humans. Nor does the hive.",
        "Against humans. The hive understands.",
        "A faction that does not trust them. Good." } },

    -- Skeletons
    { policy = "pro_skeleton", faction = "skeletons", tone = "approve", lines = {
        "A faction that speaks well of us. We note it.",
        "Friend of the bones. Rare.",
        "We remember kindness. Slowly." } },
    { policy = "anti_skeleton", faction = "skeletons", tone = "hostile", lines = {
        "You dislike our kind. We have heard.",
        "A creed against machines of our sort. We will remember.",
        "We do not forget a slight. Not even when we forget everything else." } },
    { policy = "multiracial", faction = "skeletons", tone = "approve", lines = {
        "All kinds under one banner. That is how it should be.",
        "You count us in. That counts.",
        "A faction without a wall for us. Good." } },
    { policy = "human_supremacy", faction = "skeletons", tone = "hostile", lines = {
        "Humans first. We were here before them. Or I think we were.",
        "A creed that ranks us below. We do not agree.",
        "Your kind above ours. That is a poor idea." } },
    { policy = "anti_human", faction = "skeletons", tone = "approve", lines = {
        "You do not love the humans. We understand.",
        "A creed against them. We will listen.",
        "No humans on top. I like it." } },

    -- Dust Bandits
    { policy = "pro_outlaw", faction = "dust", tone = "approve", lines = {
        "Outlaw friendly, eh? Boss might shake your hand.",
        "Heard you leave the free folk alone. Good manners.",
        "A faction that does not hate us. Welcome, I suppose." } },
    { policy = "anti_outlaw", faction = "dust", tone = "hostile", lines = {
        "You hate outlaws. We heard. We hate you right back.",
        "Hunt us and we hunt you. Simple.",
        "A creed against our sort? You first." } },

    -- Cannibals (their dialect: the th sound is dh)
    { policy = "cannibalism", faction = "cannibals", tone = "approve", lines = {
        "Meat folk. You eat man too? Dhen we sit together.",
        "Dhe way of dhe meat. You know it. /CANNIBALHA/",
        "Good. You hungry like us." } },
    { policy = "anti_cannibal", faction = "cannibals", tone = "hostile", lines = {
        "You kill dhe meat people. We know. Dhe boys know.",
        "You hate us. Dhen we eat you. Simple.",
        "No sit with you. You kill us." } },

    -- Cannibal Hunters
    { policy = "anti_cannibal", faction = "cannibal_hunters", tone = "approve", lines = {
        "Cannibal killers? Then we share work.",
        "Against the painted ones. Finally, someone sane.",
        "A faction that hunts them. Welcome." } },
    { policy = "cannibalism", faction = "cannibal_hunters", tone = "hostile", lines = {
        "You eat people and call it a creed. We hunt people like that.",
        "Cannibals in a suit. We will see you at the crossbow.",
        "A faction of man-eaters? Not on this road." } },
}

return D
