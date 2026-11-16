-- Ideology reactions: you declare your faction's ideology (focus + policies) and NPCs of the right factions occasionally say
-- something about it when they see you (or in bar talk). Spoken from Lua with character:say, so nothing here needs the
-- Kenshi Faction System; the ids match its lists, so you can copy what you picked there:
--   LW.ideology.set("okran", {"slavery","free_trade"}, "The Gate Wardens")   LW.ideology.clear()   LW.ideology.list()
-- Saved in the player characters' saved numbers (a focus number and a bitmask of policies); the name is kept for this session.
local core = require("lw.core")
local data = require("lw.ideology_data")
local cfg = core.cfg
local I = { major = nil, minors = {}, name = nil, last = {}, stamps = {} }

local function indexOf(list, id) for i, v in ipairs(list) do if v == id then return i end end end

local function persist()
    core.saveNumber("IDEO_MAJOR", I.major and indexOf(data.majors, I.major) or 0)
    local mask = 0
    for i, id in ipairs(data.minors) do if I.minors[id] then mask = mask + 2 ^ (i - 1) end end
    core.saveNumber("IDEO_MINORS", mask)
end

function I.load()
    local m = core.loadNumber("IDEO_MAJOR")
    if m == nil then return end
    I.major = data.majors[m]
    local mask = core.loadNumber("IDEO_MINORS") or 0
    I.minors = {}
    for i, id in ipairs(data.minors) do
        if math.floor(mask / 2 ^ (i - 1)) % 2 == 1 then I.minors[id] = true end
    end
end

function I.set(major, minors, name)
    if major ~= nil and not indexOf(data.majors, major) then core.log("ideology: unknown focus", tostring(major)); return false end
    local chosen = {}
    for _, id in ipairs(minors or {}) do
        if indexOf(data.minors, id) then chosen[id] = true else core.log("ideology: unknown policy", tostring(id)) end
    end
    I.major, I.minors = major, chosen
    if name then I.name = name end
    persist()
    core.log("ideology set:", tostring(I.major), table.concat(I.listMinors(), ","))
    return true
end

function I.clear()
    I.major, I.minors, I.name = nil, {}, nil
    persist()
    core.log("ideology cleared")
end

function I.listMinors()
    local out = {}
    for _, id in ipairs(data.minors) do if I.minors[id] then out[#out + 1] = id end end
    return out
end

function I.list()
    core.log("focus ids:", table.concat(data.majors, ", "))
    core.log("policy ids:", table.concat(data.minors, ", "))
    core.log("current:", tostring(I.major), table.concat(I.listMinors(), ","))
end

local function factionKey(c)
    local f = core.factionOf(c)
    if not f then return nil end
    local ok, name = pcall(function() return f:getName() end)
    if not ok or type(name) ~= "string" then return nil end
    name = name:lower()
    for _, entry in ipairs(data.factions) do
        for _, m in ipairs(entry.match) do
            if name:find(m, 1, true) then return entry.key end
        end
    end
end
I.factionKey = factionKey

-- pick one line for a speaker of this faction from the declared policies (or nil)
function I.pick(key)
    local options = {}
    for _, e in ipairs(data.lines) do
        if e.faction == key and ((I.major and e.policy == I.major) or I.minors[e.policy]) then options[#options + 1] = e end
    end
    if #options == 0 then return nil end
    local e = options[math.random(#options)]
    return e.lines[math.random(#e.lines)], e
end

local function canSpeak(c)
    local ok, res = pcall(function()
        if c.canSpeakNormally and not c:canSpeakNormally() then return false end
        local d = c.dialogue
        if d and ((tonumber(d.speechTextTimer) or 0) > 0 or (d.sayMsg and d.sayMsg ~= "")) then return false end
        return true
    end)
    return (not ok) or res
end

local watched
local function watch()
    if watched then return watched end
    watched = {}
    for _, n in ipairs(cfg.ideologyEvents) do
        local v = EventTriggerEnum[n]
        if v ~= nil then watched[v] = n end
    end
    return watched
end

local function overBudget(now)
    local fresh = {}
    for _, t in ipairs(I.stamps) do if now - t < 1 then fresh[#fresh + 1] = t end end
    I.stamps = fresh
    return #fresh >= (cfg.ideologyPerHour or 2)
end

-- called for every event the game sends; returns false to replace the NPC's usual line with ours, else nil
function I.decide(who, what)
    if not (I.major or next(I.minors)) then return nil end
    if not watch()[what] then return nil end
    if core.isPlayer(who) then return nil end
    if math.random(100) > (cfg.ideologyChance or 35) then return nil end
    local key = factionKey(who)
    if not key then return nil end
    local now = core.hours()
    local id = core.nameOf(who)
    if I.last[id] and now - I.last[id] < (cfg.ideologyCooldownHours or 8) then return nil end
    if overBudget(now) or not canSpeak(who) then return nil end
    local text = I.pick(key)
    if not text then return nil end
    I.last[id] = now
    I.stamps[#I.stamps + 1] = now
    local ok = pcall(function() who:say(text) end)
    if not ok then return nil end
    core.debug("ideology:", key, id, "->", text)
    return false
end

function I.register(eventIds)
    I.load()
    eventIds[#eventIds + 1] = Events.on("Dialogue::sendEvent", function(self, who, what, defaultVal)
        local ok, res = pcall(I.decide, who, what)
        if ok then return res end
        return nil
    end)
    core.addTask("ideology_sync", 1, I.load)
end

return I
