-- Probe 2: what does the API give us? In the console (Shift+Ctrl+L, Console): LW.probe()
local LW = _G.LW

function LW.world()
    return getGameWorld()
end

function LW.probe()
    LW.log("=== probe start ===")
    local w
    LW.try("getGameWorld", function() w = getGameWorld() end)
    if not w then return end
    LW.try("game time (hours stamp)", function() LW.log("timestamp", w:getTimeStamp()) end)
    LW.try("game time (TimeOfDay)", function()
        local t = w:getTimeStamp_inGameHours()
        LW.log("total hours", t:getTotalHours(), "total days", t:getTotalDays())
    end)
    LW.try("paused", function() LW.log("paused", w:isPaused()) end)
    LW.try("player faction", function()
        local f = w.player:getFaction()
        LW.log("player faction:", f:getName())
    end)
    LW.try("player characters", function()
        local list = w.player:getAllPlayerCharacters()
        LW.log("player characters:", tostring(list:size()))
        for i = 1, list:size() do
            local c = list[i]
            LW.log("  ", c:getName(), "money", tostring(c:getMoney()))
        end
    end)
    LW.try("faction lookup", function()
        for _, name in ipairs({ "Holy Nation", "The Holy Nation", "Shek Kingdom", "United Cities", "Gate Folk" }) do
            local f = w.factionMgr:getFactionByName(name)
            LW.log("faction '" .. name .. "':", f and f:getName() or "nil")
        end
    end)
    LW.try("player interface accessor", function()
        local pi = getPlayerInterface and getPlayerInterface() or w.player
        LW.log("getPlayerInterface:", tostring(pi), "participant:", tostring(pi and pi.participant))
    end)
    LW.try("itemType enum names with SQUAD or LUA", function()
        for k, v in pairs(itemType) do
            local s = tostring(k)
            if s:find("SQUAD") or s:find("LUA") or s:find("WORD") then LW.log("itemType", s, v) end
        end
    end)
    LW.log("=== probe end ===")
end

LW.log("probe ready: run LW.probe() in the console")

-- Can Lua read a BFrizz variable (the quest and contract progress counters)? Console: LW.probe_vars()
function LW.probe_vars()
    for _, name in ipairs({ "Quest qc_hunt", "Quest qc_caravan", "Contract ol_elric_vosk_contract" }) do
        local ok, res = pcall(function()
            local gd = getGameWorld().gamedata:getDataByName(name, 1000)
            if not gd then return "record not found" end
            return "idata.value = " .. tostring(gd.idata["value"])
        end)
        LW.log("variable", name, "->", ok and tostring(res) or ("ERROR " .. tostring(res)))
    end
end
