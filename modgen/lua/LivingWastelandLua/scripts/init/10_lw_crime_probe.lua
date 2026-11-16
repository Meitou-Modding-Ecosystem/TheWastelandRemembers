-- Probe 1: does the crime hook fire, and what do its arguments look like?
-- Commit a crime (assault, theft) in view of a faction, then check KenshiLua.log (Shift+Ctrl+L, Log Viewer, or the file).
local LW = _G.LW
LW.crimes = LW.crimes or {}

LW.try("hook BountyManager::notifyCrimeWitnessed", function()
    Events.on("BountyManager::notifyCrimeWitnessed", function(this, faction, againstWho, expiryTime, crimeType)
        local fname, vname = "?", "?"
        pcall(function() fname = faction:getName() end)
        pcall(function() vname = againstWho:getName() end)
        if #LW.crimes < 200 then LW.crimes[#LW.crimes + 1] = { faction = fname, victim = vname, crime = tostring(crimeType), expires = tostring(expiryTime) } end
        -- a fight can witness the same crime hundreds of times: log the first 25, then every 25th
        LW.crime_count = (LW.crime_count or 0) + 1
        if LW.crime_count <= 25 or LW.crime_count % 25 == 0 then
            LW.log("CRIME witnessed by", fname, "against", vname, "type", tostring(crimeType), "expires", tostring(expiryTime), "(#" .. LW.crime_count .. ")")
        end
    end)
end)

-- The relations hook fires hundreds of times a minute, so it is only registered when you ask for it (LW.relations_log(true)).
local relationsHooked = false
local function hookRelations()
    if relationsHooked then return end
    relationsHooked = true
    LW.try("hook FactionRelations::affectRelations", function()
        Events.on("FactionRelations::affectRelations", function(this, otherFaction, eventType, multiplier)
            if not LW.relations_logging then return end
            local oname = "?"
            pcall(function() oname = otherFaction:getName() end)
            LW.log("RELATIONS change with", oname, "event", tostring(eventType), "x", tostring(multiplier))
        end)
    end)
end

-- Console: LW.relations_log(true) before committing a crime to see every relation change; LW.relations_log(false) to stop.
function LW.relations_log(on)
    if on then hookRelations() end
    LW.relations_logging = on and true or false
    LW.log("relations logging", LW.relations_logging and "ON" or "off")
end
