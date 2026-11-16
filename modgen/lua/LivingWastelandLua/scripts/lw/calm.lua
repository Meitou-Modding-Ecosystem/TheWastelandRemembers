-- Calm: stops the whole town barking at you the moment your squad appears.
--   * grace: for the first few real seconds after a game starts or loads, no NPC sighting barks (EV_I_SEE_NEUTRAL_SQUAD, EV_I_SEE_ALLY_PLAYER)
--   * burst: afterwards, when many NPCs see you at once, only a few of them bark within a short window
-- Uses the same veto the chatter module uses (Dialogue::sendEvent returning false); only non-player speakers and only the
-- listed events are touched. Settings in config.lua (calmGraceSeconds, calmBurst, calmWindowSeconds, calmEvents).
local core = require("lw.core")
local cfg = core.cfg
local C = { graceStart = nil, lastHours = nil, stamps = {}, watched = nil, seen = 0, vetoed = 0, passed = 0 }

local function watched()
    if C.watched then return C.watched end
    C.watched = {}
    for _, n in ipairs(cfg.calmEvents or {}) do
        local v = EventTriggerEnum and EventTriggerEnum[n]
        if v ~= nil then C.watched[v] = n end
    end
    return C.watched
end

-- called every so often from the world update: a time that goes backwards, or the first sample, means a new game or a load
function C.sample()
    local ok, h = pcall(core.hours)
    if not ok or not h then return end
    local jumped = false
    pcall(function()                                   -- a big jump of the first character's position = a load or a teleport
        local who = core.playerCharacters()[1]
        local p = who and who:getPosition()
        if p then
            if C.lastPos and ((p.x - C.lastPos.x) ^ 2 + (p.z - C.lastPos.z) ^ 2) > 3000 ^ 2 then jumped = true end
            C.lastPos = { x = p.x, z = p.z }
        end
    end)
    if C.lastHours == nil or h < C.lastHours - 0.001 or jumped then
        C.graceStart = os.clock()
        C.stamps = {}
        core.debug("calm: grace period started")
    end
    C.lastHours = h
end

function C.decide(who, what)
    if not what or not watched()[what] then return nil end
    C.seen = C.seen + 1
    if core.isPlayer(who) then C.passed = C.passed + 1; return nil end
    local now = os.clock()
    if C.graceStart and now - C.graceStart < (cfg.calmGraceSeconds or 25) then C.vetoed = C.vetoed + 1; return false end
    local burst = cfg.calmBurst or 0
    if burst > 0 then
        local window, fresh = cfg.calmWindowSeconds or 4, {}
        for _, t in ipairs(C.stamps) do if now - t < window then fresh[#fresh + 1] = t end end
        C.stamps = fresh
        if #fresh >= burst then C.vetoed = C.vetoed + 1; return false end
        fresh[#fresh + 1] = now
    end
    C.passed = C.passed + 1
    return nil
end

-- Console: LW.calm_stats() -- how many sighting barks were seen, vetoed and let through (also written to the log)
function C.stats()
    core.log("calm: installed", tostring(C.installed), "| sightings seen", C.seen, "| vetoed", C.vetoed, "| let through", C.passed)
end

-- The hook is installed only once the world is running, never while the game is still starting up (hooking the dialogue system at
-- start-up is the prime suspect in a crash on launch). No GameWorld/onGameLoaded hooks: a new game or a load is noticed by the time
-- going backwards or the squad's position jumping.
function C.install(eventIds)
    if C.installed then return end
    local ok = pcall(core.hours)
    if not ok then return end                            -- the world is not up yet; try again on the next pass
    C.installed = true
    C.sample()
    eventIds[#eventIds + 1] = Events.on("Dialogue::sendEvent", function(self, who, what, defaultVal)
        local ok2, res = pcall(C.decide, core.speakerOf(self, who), what)
        if ok2 then return res end
        return nil
    end)
    core.log("calm: installed")
end

function C.register(eventIds)
    _G.LW = _G.LW or {}
    _G.LW.calm_stats = C.stats
    core.addTask("calm_install", 0.0, function() C.install(eventIds) end)
    core.addTask("calm_sample", 0.0, function() if C.installed then C.sample() end end)
end

return C
