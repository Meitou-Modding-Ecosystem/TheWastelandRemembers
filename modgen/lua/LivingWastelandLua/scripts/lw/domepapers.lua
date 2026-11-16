-- Put the Ashlands papers on the dome heads (lootable when they fall or are knocked out), once per character.
local core = require("lw.core")
local data = require("lw.data")
local P = {}

-- list name -> the head whose dome it belongs to (a substring of the character's name)
P.heads = { ["Ash Dome I Papers"] = "head of agriculture", ["Ash Dome II Papers"] = "rhinobot",
            ["Ash Dome III Papers"] = "jang", ["Ash Dome IV Papers"] = "hat-12" }
P.chance = 0.8

local function given(c)
    local ok, v = pcall(function() local ad = c:getAppearanceData(); return ad and ad.fdata["LW_PAPER"] end)
    return ok and v == 1
end

local function mark(c)
    pcall(function() local ad = c:getAppearanceData(); if ad then ad.fdata["LW_PAPER"] = 1 end end)
end

local function addPaper(c, itemName)
    local gd = getGameWorld().gamedata:getDataByName(itemName, itemType.ITEM)
    if not gd then core.log("domepapers: no item", itemName); return false end
    local item = getRootObjectFactory():createItem(gd, nil, nil, nil, -1, nil)
    return item and c:getInventory():addItem(item, 1, false, true)
end

function P.onInit(c)
    local name = core.nameOf(c):lower()
    if name == "?" or given(c) then return end
    for _, lst in ipairs(data.domePapers) do
        local head = P.heads[lst.list]
        if head and name:find(head, 1, true) then
            mark(c)
            if math.random() < P.chance then
                local pick = lst.items[math.random(#lst.items)]
                if addPaper(c, pick) then core.debug("domepapers:", pick, "->", core.nameOf(c)) end
            end
            return
        end
    end
end

function P.register(eventIds)
    eventIds[#eventIds + 1] = Events.on("Character::_NV_init", function(self) pcall(P.onInit, self) end)
end

return P
