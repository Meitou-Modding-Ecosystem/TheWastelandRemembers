-- Give the quest items we assumed would drop. A kill the player's side landed a hit on, then: the item goes to the player's leader.
local core = require("lw.core")
local credit = require("lw.credit")
local cfg = core.cfg
local D = {}

function D.give(itemName, who)
    local gw = getGameWorld()
    local gd = gw.gamedata:getDataByName(itemName, itemType.ITEM)
    if not gd then core.log("drops: no item named", itemName); return false end
    local item = getRootObjectFactory():createItem(gd, nil, nil, nil, -1, nil)
    if not item then core.log("drops: could not create", itemName); return false end
    who = who or core.playerCharacters()[1]
    if not who then return false end
    local ok = who:giveItem(item, false, true)
    if not ok then          -- inventory full or the call refused: drop a new item at their feet (the create call a published mod uses)
        ok = pcall(function() getRootObjectFactory():create(gd, who:getPosition(), true, nil, who:getOrientation()) end)
    end
    if ok then core.notify("You take " .. itemName .. ".") end
    return ok
end

function D.onDeath(victim)
    local name = core.nameOf(victim)
    if not credit.recentlyHit(name) then return end
    local fname
    pcall(function() fname = core.factionOf(victim):getName() end)
    for _, d in ipairs(cfg.drops) do
        local hit = (d.match and name:lower():find(d.match, 1, true)) or (d.faction and fname == d.faction)
        if hit and math.random() < (d.chance or 1) then
            pcall(D.give, d.item)
        end
    end
end

function D.register(eventIds)
    credit.register(eventIds)
    eventIds[#eventIds + 1] = Events.on("Character::declareDead", function(self) pcall(D.onDeath, self) end)
end

return D
