-- Nemesis: remember a named enemy who knocks one of your people out; they come back tougher, near where you last met.
local core = require("lw.core")
local cfg = core.cfg
local M = { list = {}, lastHit = {} }    -- list[name] = {defeats, day, x, z, faction}

local function isNamedEnemy(c)
    if not c or core.isPlayer(c) then return false end
    local n = core.nameOf(c)
    if n == "?" or n == "" then return false end
    -- generic soldiers share names like "Mercenary": treat a unique-looking proper name as a nemesis candidate
    local generic = { mercenary = 1, bandit = 1, ["hungry bandit"] = 1, guard = 1, thug = 1, paladin = 1, sentinel = 1, drifter = 1 }
    return not generic[n:lower()]
end

function M.onHit(victim, attacker)
    if core.isPlayer(victim) and isNamedEnemy(attacker) then
        M.lastHit[core.nameOf(victim)] = { by = core.nameOf(attacker), who = attacker, t = core.hours() }
    end
end

local function koCheck()
    for _, c in ipairs(core.playerCharacters()) do
        local ok, ko = pcall(function() return c:isUnconcious() end)
        local rec = M.lastHit[core.nameOf(c)]
        if ok and ko and rec and core.hours() - rec.t < 0.5 then
            local name = rec.by
            local pos = c:getPosition()
            local n = M.list[name]
            if not n then
                n = { defeats = 0 }
                M.list[name] = n
            end
            n.defeats = n.defeats + 1
            n.day, n.x, n.z = core.day(), pos.x, pos.z
            pcall(function() n.faction = core.nameOf(core.factionOf(rec.who)) end)
            M.lastHit[core.nameOf(c)] = nil
            core.notify(name .. " has beaten " .. core.nameOf(c) .. ". They will remember this.")
            core.log("nemesis recorded:", name, "defeats", n.defeats)
        end
    end
end

-- tougher after every victory over you (cap in config)
local function buff(self, statType, unmodified, defaultVal)
    if next(M.list) == nil or type(defaultVal) ~= "number" then return nil end
    local me = self.me
    if not me then return nil end
    local n = M.list[core.nameOf(me)]
    if not n then return nil end
    return defaultVal * (1 + math.min(n.defeats * cfg.nemesisBuffPerDefeat, cfg.nemesisBuffCap))
end

local function ambush()
    local chars = core.playerCharacters()
    local leader = chars[1]
    if not leader then return end
    local p = leader:getPosition()
    for name, n in pairs(M.list) do
        local near = n.x and ((p.x - n.x) ^ 2 + (p.z - n.z) ^ 2) <= cfg.nemesisAmbushRadius ^ 2
        if near and core.day() - n.day >= cfg.nemesisAmbushMinDays and math.random() < cfg.nemesisAmbushChance then
            local names = cfg.ambushSquads[n.faction or ""]
            if names then
                local hunters = require("lw.hunters")
                core.notify("You can feel it: " .. name .. " is close.")
                -- the same squad spawn as the hunters (their faction's list)
                hunters.spawn(n.faction, leader)
                n.day = core.day()
            end
        end
    end
end

function M.register(eventIds)
    eventIds[#eventIds + 1] = Events.on("Character::_NV_hitByMeleeAttack", function(self, cutDir, damage, attacker)
        pcall(M.onHit, self, attacker)
    end)
    eventIds[#eventIds + 1] = Events.on("CharStats::getStat", function(self, statType, unmodified, defaultVal)
        local ok, v = pcall(buff, self, statType, unmodified, defaultVal)
        if ok then return v end
    end)
    core.addTask("nemesis_ko", 0.05, koCheck)
    core.addTask("nemesis_ambush", 6, ambush)
end

return M
