-- Heat: crimes witnessed by a faction raise that faction's heat against you. Heat decays. Tiers: cold, hostile, hunted.
local core = require("lw.core")
local cfg = core.cfg
local H = { heat = {}, tier = {}, lastDay = nil }

local function key(name) return "HEAT_" .. (tostring(name):gsub("[^%w]", "")) end

local function crimeName(value)
    for name, v in pairs(CrimeEnum) do
        if v == value and type(name) == "string" and name:find("^CRIME_") then return name end
    end
    return "CRIME_" .. tostring(value)
end

function H.get(factionName) return H.heat[factionName] or 0 end

local function factionByName(name)
    return getGameWorld().factionMgr:getFactionByName(name)
end

local function tierOf(h)
    if h >= cfg.heatTiers.hunted then return 3 end
    if h >= cfg.heatTiers.hostile then return 2 end
    if h >= cfg.heatTiers.cold then return 1 end
    return 0
end

function H.add(factionName, amount, why)
    local before = tierOf(H.get(factionName))
    H.heat[factionName] = H.get(factionName) + amount
    core.saveNumber(key(factionName), H.heat[factionName])
    local after = tierOf(H.heat[factionName])
    core.debug("heat", factionName, "+" .. amount, why or "", "=", H.heat[factionName], "tier", after)
    if after > before then H.onTierUp(factionName, after) end
end

function H.onTierUp(factionName, tier)
    local ok, err = pcall(function()
        local f = factionByName(factionName)
        local pf = core.playerFaction()
        if not f or not pf then return end
        if tier == 1 then
            f.relations:affectRelations(pf, -cfg.coldRelationPenalty, 1)
            core.notify(factionName .. " are growing cold towards you.")
        elseif tier == 2 then
            core.notify(factionName .. " have put out word to find you.")
        elseif tier == 3 then
            f.relations:setEnemy(pf)
            core.notify(factionName .. " have declared you an enemy.")
        end
    end)
    if not ok then core.log("tier effect failed:", tostring(err)) end
end

function H.clear(factionName)
    H.heat[factionName] = 0
    core.saveNumber(key(factionName), 0)
    core.log("heat cleared for", factionName)
end

-- A crime was witnessed. Only the player's crimes count.
local function onCrime(self, faction, againstWho, expiryTime, crimeType)
    local criminal = self and self.me
    if not core.isPlayer(criminal) then return end
    local fname = faction and faction:getName()
    if not fname then return end
    local w = cfg.crimeHeat[crimeName(crimeType)] or 2
    H.add(fname, w, crimeName(crimeType))
end

local function decay()
    local today = core.day()
    if H.lastDay and today > H.lastDay then
        local days = math.min(today - H.lastDay, 3)
        for name, h in pairs(H.heat) do
            if h > 0 then
                H.heat[name] = math.max(0, h - cfg.heatDecayPerDay * days)
                core.saveNumber(key(name), H.heat[name])
            end
        end
    end
    H.lastDay = today
end

local function load()
    for fname in pairs(cfg.hunterSquads) do
        local v = core.loadNumber(key(fname))
        if v and v > (H.heat[fname] or 0) then H.heat[fname] = v end
    end
end

function H.register(eventIds)
    eventIds[#eventIds + 1] = Events.on("BountyManager::notifyCrimeWitnessed", function(self, faction, againstWho, expiryTime, crimeType)
        pcall(onCrime, self, faction, againstWho, expiryTime, crimeType)
    end)
    core.addTask("heat_decay", 6, decay)
    core.addTask("heat_load", 1, load)
end

function H.report()
    for name, h in pairs(H.heat) do core.log("heat", name, string.format("%.1f", h), "tier", tierOf(h)) end
end

H.tierOf = tierOf
return H
