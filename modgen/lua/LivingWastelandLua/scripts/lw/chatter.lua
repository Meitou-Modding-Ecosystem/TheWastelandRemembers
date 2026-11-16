-- Squad chatter rules, applied to the engine's own event dispatch (Dialogue::sendEvent can be vetoed by returning false):
--   * a squad member with nobody of the squad nearby does not speak the arrival/status barks (so a teleported or split-off member stays quiet);
--   * at most N such barks per in-game hour for the whole squad.
-- Only the player's characters and only the events listed in config are touched; everything else passes through unchanged.
local core = require("lw.core")
local cfg = core.cfg
local K = { stamps = {} }

local watched
local function names()
    if watched then return watched end
    watched = {}
    local byValue = {}
    for name, v in pairs(EventTriggerEnum) do if type(name) == "string" then byValue[v] = name end end
    for _, n in ipairs(cfg.chatterEvents) do
        local v = EventTriggerEnum[n]
        if v ~= nil then watched[v] = n end
    end
    return watched
end

local function distance(a, b)
    local pa, pb = a:getPosition(), b:getPosition()
    return math.sqrt((pa.x - pb.x) ^ 2 + (pa.z - pb.z) ^ 2)
end

local function aloneIn(who)
    local others = 0
    for _, c in ipairs(core.playerCharacters()) do
        if c ~= who then
            local ok, d = pcall(distance, who, c)
            if ok and d <= cfg.chatterAloneRadius then others = others + 1; break end
        end
    end
    return others == 0 and #core.playerCharacters() > 1
end

local function overBudget()
    local now = core.hours()
    local fresh = {}
    for _, t in ipairs(K.stamps) do if now - t < 1 then fresh[#fresh + 1] = t end end
    K.stamps = fresh
    if #fresh >= cfg.chatterPerHour then return true end
    fresh[#fresh + 1] = now
    return false
end

-- Optional: when a whole squad of NPCs sees you at once, let only a few of them bark (the rest keep quiet). Off unless
-- cfg.ambientBurst > 0. Counts real seconds, so it works with any game speed.
K.ambient = {}
local function ambientLimit(what)
    local burst = cfg.ambientBurst or 0
    if burst <= 0 then return nil end
    local v = EventTriggerEnum and EventTriggerEnum.EV_I_SEE_NEUTRAL_SQUAD
    if v == nil or what ~= v then return nil end
    local now, window = os.clock(), cfg.ambientWindowSeconds or 4
    local fresh = {}
    for _, t in ipairs(K.ambient) do if now - t < window then fresh[#fresh + 1] = t end end
    K.ambient = fresh
    if #fresh >= burst then return false end
    fresh[#fresh + 1] = now
    return nil
end

function K.decide(who, what)
    if not what then return nil end
    if not core.isPlayer(who) then return ambientLimit(what) end
    if not names()[what] then return nil end
    if aloneIn(who) then return false end
    if overBudget() then return false end
    return nil
end

function K.register(eventIds)
    eventIds[#eventIds + 1] = Events.on("Dialogue::sendEvent", function(self, who, what, defaultVal)
        local ok, res = pcall(K.decide, core.speakerOf(self, who), what)
        if ok then return res end
        return nil
    end)
end

return K
