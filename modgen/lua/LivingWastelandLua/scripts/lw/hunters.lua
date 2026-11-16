-- Hunters: when a faction's heat is high, send a squad of theirs to find you (out of sight, rare to common).
local core = require("lw.core")
local heat = require("lw.heat")
local cfg = core.cfg
local U = { lastSpawn = {} }

local function position(c)
    local p = c:getPosition()
    return { x = p.x, y = p.y, z = p.z }
end

local function squadTemplate(factionName)
    local names = cfg.hunterSquads[factionName]
    if not names then return nil end
    local gd = getGameWorld().gamedata
    for _, n in ipairs(names) do
        local d = gd:getDataByName(n, itemType.SQUAD_TEMPLATE)
        if d then return d, n end
    end
end

-- Spawn one squad for the faction near the leader, aimed at the leader. Returns true if the engine accepted the call.
function U.spawn(factionName, leader)
    local faction = getGameWorld().factionMgr:getFactionByName(factionName)
    if not faction then core.log("hunters: no faction", factionName); return false end
    local tmpl, tname = squadTemplate(factionName)
    if not tmpl then core.log("hunters: no squad template for", factionName); return false end
    local p = position(leader)
    local d = cfg.hunterSpawnDistance[1] + math.random() * (cfg.hunterSpawnDistance[2] - cfg.hunterSpawnDistance[1])
    local a = math.random() * 2 * math.pi
    local pos = { x = p.x + math.cos(a) * d, y = p.y, z = p.z + math.sin(a) * d }
    local ok, err = pcall(function()
        getRootObjectFactory():createRandomSquad(faction, pos, nil, cfg.hunterSquadMax, nil, tmpl, nil, nil, nil,
            false, leader:getHandle(), nil, 1.0, SquadType.SQ_ROAMING, false)
    end)
    if not ok then core.log("hunters: spawn failed for", factionName, "-", tostring(err)); return false end
    core.log("hunters: sent", tname, "of", factionName, "after", core.nameOf(leader))
    return true
end

local function check()
    local chars = core.playerCharacters()
    local leader = chars[1]
    if not leader then return end
    local today = core.day()
    for factionName in pairs(cfg.hunterSquads) do
        local tier = heat.tierOf(heat.get(factionName))
        if tier >= 2 then
            local cooldown = cfg.hunterCooldownDays * (tier >= 3 and 0.5 or 1)
            local last = U.lastSpawn[factionName]
            if (not last or today - last >= cooldown or today < last) and math.random() < cfg.hunterChancePerCheck then
                if U.spawn(factionName, leader) then U.lastSpawn[factionName] = today end
            end
        end
    end
end

function U.register()
    core.addTask("hunters_check", cfg.hunterCheckHours, check)
end

return U
