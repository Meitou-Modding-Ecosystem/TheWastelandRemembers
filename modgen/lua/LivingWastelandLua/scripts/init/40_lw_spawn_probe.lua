-- Probe 4: can we spawn a squad? Console: LW.spawn_probe("Dust Bandits", "Dust Bandit squad")
-- Spawns the named squad template for the named faction a short distance from your first character.
-- Use a quiet spot and a weak squad for the first try. NOTHING runs automatically.
local LW = _G.LW

-- Second argument may be a template name or its record id ("<number>-<ModName>.mod"). Third argument "factory" uses the
-- createRandomSquad call a published mod (KFS) is known to use; the default uses createNewEmptyActivePlatoon.
function LW.spawn_probe(factionName, squadName, method)
    local w = getGameWorld()
    local ok, err = pcall(function()
        local faction = w.factionMgr:getFactionByName(factionName)
        assert(faction, "no such faction: " .. tostring(factionName))
        local who = w.player:getAnyPlayerCharacter()
        assert(who, "no player character")
        local pos = who:getPosition()
        local cat = itemType.SQUAD_TEMPLATE   -- 52 in EnumsReference
        assert(cat, "itemType.SQUAD_TEMPLATE missing")
        local tmpl = w.gamedata:getDataByName(squadName, cat)
        if not tmpl then tmpl = w.gamedata:getData(squadName) end      -- record id form
        assert(tmpl, "squad template not found: " .. tostring(squadName))
        LW.log("spawning", squadName, "for", factionName, "near", tostring(pos.x), tostring(pos.z), method or "platoon")
        if method == "factory" then
            local squad = getRootObjectFactory():createRandomSquad(faction, pos, nil, 1, nil, tmpl, nil, nil, nil, true, who:getHandle(), nil, 1, 2, false)
            if squad then squad:setPersistentSquad(true) end
            LW.log("createRandomSquad returned", tostring(squad))
        else
            faction:createNewEmptyActivePlatoon(tmpl, false, pos)
        end
    end)
    if not ok then LW.log("spawn_probe FAILED:", tostring(err)) end
end

LW.log("spawn probe ready: LW.spawn_probe(factionName, squadTemplateName)")
