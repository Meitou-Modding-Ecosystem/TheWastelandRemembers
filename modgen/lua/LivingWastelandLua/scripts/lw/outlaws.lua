-- Named outlaws: keep one out in the open now and then (the data mod only gives a rare biome spawn), and remember kills.
local core = require("lw.core")
local credit = require("lw.credit")
local data = require("lw.data")
local cfg = core.cfg
local O = { killed = {}, lastSpawn = nil }

local function key(name) return "OUTLAW_" .. (tostring(name):gsub("[^%w]", "")) end

local function load()
    for _, o in ipairs(data.outlaws) do
        if core.loadNumber(key(o.name)) == 1 then O.killed[o.name] = true end
    end
    local d = core.loadNumber("OUTLAW_LASTSPAWN")
    if d and (not O.lastSpawn or d > O.lastSpawn) then O.lastSpawn = d end
end

function O.onDeath(victim)
    local name = core.nameOf(victim)
    for _, o in ipairs(data.outlaws) do
        if o.name == name then
            if credit.recentlyHit(name) then
                O.killed[name] = true
                core.saveNumber(key(name), 1)
                core.log("outlaw killed:", name)
            end
            return
        end
    end
end

local function candidates()
    local out = {}
    for _, o in ipairs(data.outlaws) do
        if not O.killed[o.name] then out[#out + 1] = o end
    end
    return out
end

function O.spawn(o, leader)
    local gw = getGameWorld()
    local faction = gw.factionMgr:getFactionByName(o.faction)
    local tmpl = gw.gamedata:getDataByName(o.squad, itemType.SQUAD_TEMPLATE)
    if not faction or not tmpl then core.log("outlaws: missing faction or squad for", o.name); return false end
    local p = leader:getPosition()
    local d = cfg.outlawDistance[1] + math.random() * (cfg.outlawDistance[2] - cfg.outlawDistance[1])
    local a = math.random() * 2 * math.pi
    local pos = { x = p.x + math.cos(a) * d, y = p.y, z = p.z + math.sin(a) * d }
    local ok, err = pcall(function()
        getRootObjectFactory():createRandomSquad(faction, pos, nil, 8, nil, tmpl, nil, nil, nil, false, nil, nil, 1.0, SquadType.SQ_ROAMING, false)
    end)
    if not ok then core.log("outlaws: spawn failed for", o.name, tostring(err)); return false end
    core.log("outlaws: sent", o.name, "out near", core.nameOf(leader))
    return true
end

local function check()
    local leader = core.playerCharacters()[1]
    if not leader then return end
    local today = core.day()
    if O.lastSpawn and today - O.lastSpawn < cfg.outlawCooldownDays and today >= O.lastSpawn then return end
    if math.random() > cfg.outlawChance then return end
    local list = candidates()
    if #list == 0 then return end
    local o = list[math.random(#list)]
    if O.spawn(o, leader) then
        O.lastSpawn = today
        core.saveNumber("OUTLAW_LASTSPAWN", today)
    end
end

function O.register(eventIds)
    require("lw.credit").register(eventIds)
    eventIds[#eventIds + 1] = Events.on("Character::declareDead", function(self) pcall(O.onDeath, self) end)
    core.addTask("outlaws_load", 1, load)
    core.addTask("outlaws_check", cfg.outlawCheckHours, check)
end

return O
