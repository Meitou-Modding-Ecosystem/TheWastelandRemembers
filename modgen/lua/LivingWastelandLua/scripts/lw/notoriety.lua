-- Earned titles for the most significant bosses. Develops as you kill more of them. Dialogue scripts can call LW.title().
local core = require("lw.core")
local cfg = core.cfg
local N = { score = 0, mask = 0, last = nil, lastEpithet = nil, hits = {} }

local function lower(s) return (tostring(s or "")):lower() end

local function bossFor(name)
    local n = lower(name)
    for i, b in ipairs(cfg.bosses) do
        if n:find(b.match, 1, true) then return i, b end
    end
end

function N.title()
    local best
    for _, tier in ipairs(cfg.titleTiers) do
        if N.score >= tier[1] then best = tier[2] end
    end
    if not best then return nil end
    if best:find("%%s") then
        if not N.lastEpithet then return nil end
        return (best:format(N.lastEpithet))
    end
    return best
end

local function persist()
    core.saveNumber("NOTO_SCORE", N.score)
    core.saveNumber("NOTO_MASK", N.mask)
    core.saveNumber("NOTO_LAST", N.last or 0)
end

function N.load()
    local s = core.loadNumber("NOTO_SCORE")
    if s and s > N.score then
        N.score = s
        N.mask = core.loadNumber("NOTO_MASK") or N.mask
        N.last = core.loadNumber("NOTO_LAST")
        if N.last == 0 then N.last = nil end
        N.lastEpithet = N.last and cfg.bosses[N.last] and cfg.bosses[N.last].epithet or nil
        core.debug("notoriety loaded: score", N.score, "title", tostring(N.title()))
    end
end

local function credit(index, boss)
    local bit = 2 ^ (index - 1)                 -- one bit per boss (floats hold 24 bits exactly: keep the list <= 24)
    if math.floor(N.mask / bit) % 2 == 1 then return end   -- already counted
    local before = N.title()
    N.mask = N.mask + bit
    N.score = N.score + boss.weight
    N.last, N.lastEpithet = index, boss.epithet
    persist()
    local after = N.title()
    if after and after ~= before then
        core.notify("Word spreads across the wasteland. They are starting to call you " .. after .. ".")
    end
end

-- A boss is credited to you if you hit it recently and then it dies.
function N.onHit(victim, attacker)
    local name = core.nameOf(victim)
    if bossFor(name) then N.hits[name] = { day = core.day(), player = core.isPlayer(attacker) } end   -- last hitter wins
end

function N.onDeath(victim)
    local name = core.nameOf(victim)
    local index, boss = bossFor(name)
    if not index then return end
    local h = N.hits[name]
    if h and h.player and core.day() - h.day <= cfg.killCreditDays then
        core.log("notoriety: credited kill of", name)
        credit(index, boss)
    end
    N.hits[name] = nil
end

function N.register(eventIds)
    eventIds[#eventIds + 1] = Events.on("Character::_NV_hitByMeleeAttack", function(self, cutDir, damage, attacker)
        pcall(N.onHit, self, attacker)
    end)
    eventIds[#eventIds + 1] = Events.on("Character::declareDead", function(self)
        pcall(N.onDeath, self)
    end)
    core.addTask("noto_sync", 1, N.load)       -- pick up the saved value after a load, and keep new recruits in step
    core.addTask("noto_write", 6, function() if N.score > 0 then persist() end end)
end

return N
