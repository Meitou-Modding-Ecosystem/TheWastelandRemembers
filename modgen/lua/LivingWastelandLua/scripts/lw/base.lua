-- Base reactions from what the base actually contains. A visitor standing in a player town who sees your squad may comment on it.
-- Uses the Dialogue::sendEvent override only to LOOK (it returns nothing, so the game's own answer stands).
local core = require("lw.core")
local cfg = core.cfg
local B = { last = {} }

-- how many buildings of a function does the town have? Best effort: the call returns void in the bindings, so try the result variants.
local function count(town, bf)
    local ok, res = pcall(function() return town:getBuildingsWithFunction(bf) end)
    if ok and type(res) == "table" then return #res end
    if ok and res and res.size then return res:size() end
    return nil
end

B.lines = {
    shop    = { "You sell things here? Good. I am looking.", "A counter and a price list. That is how a place becomes real." },
    turret  = { "Turrets. Somebody here expects visitors of the wrong sort.", "I count the guns before I count the beds." },
    wall    = { "Walls, and a gate. You mean to stay.", "A wall tells me what a place is afraid of." },
    bed     = { "Beds. Someone sleeps indoors here. That is rare.", "A roof and a bed. The wasteland is generous to you." },
    cage    = { "Cages. I will not ask who they are for.", "Cages at your gate. A hard way to be neighbours." },
    generic = { "A base. Not the worst I have seen.", "This place has the look of staying power." },
}

local function pick(set) return set[math.random(#set)] end

local function visitorSees(who)
    local ok, town = pcall(function() return who:getTown() end)
    if not ok or not town then return end
    local ok2, tf = pcall(function() return town:getFaction() end)
    if not ok2 or tf ~= core.playerFaction() then return end        -- only inside a player town
    local name = core.nameOf(who)
    local today = core.day()
    if B.last[name] and today - B.last[name] < cfg.baseCooldownDays then return end
    if math.random() > cfg.baseChance then return end
    local kinds = {}
    for label, bf in pairs({ shop = BuildingFunction.BF_SHOP, turret = BuildingFunction.BF_TURRET, wall = BuildingFunction.BF_WALL,
                             bed = BuildingFunction.BF_BED, cage = BuildingFunction.BF_CAGE }) do
        local n = count(town, bf)
        if n and n > 0 then kinds[#kinds + 1] = label end
    end
    local label = #kinds > 0 and kinds[math.random(#kinds)] or "generic"
    B.last[name] = today
    who:say(pick(B.lines[label]))
end

function B.register(eventIds)
    eventIds[#eventIds + 1] = Events.on("Dialogue::sendEvent", function(self, who, what, defaultVal)
        if what == EventTriggerEnum.EV_I_SEE_NEUTRAL_SQUAD and who and not core.isPlayer(who) then
            pcall(visitorSees, who)
        end
        return nil          -- never change the game's own decision
    end)
end

return B
