-- Probe 3: can we keep our own data across save and load?
-- Two approaches are tested: (a) a sidecar file next to the game, (b) save/load hooks firing.
-- Console: LW.persist_set(7)   then save, quit, load, then: LW.persist_get()
local LW = _G.LW
local FILE = "lw_persist_probe.txt"

function LW.persist_set(n)
    LW.state = LW.state or {}
    LW.state.test = n
    local ok, err = pcall(function()
        local f = assert(io.open(FILE, "w"))
        f:write(tostring(n))
        f:close()
    end)
    LW.log("persist_set", n, ok and "file written" or ("file FAILED: " .. tostring(err)))
    -- the route our features actually use: a number in each player character's saved data
    local okc, errc = pcall(function() require("lw.core").saveNumber("PROBE", n) end)
    LW.log("persist_set character data:", okc and "written" or ("FAILED: " .. tostring(errc)))
end

function LW.persist_get()
    local mem = LW.state and LW.state.test
    local fileval
    pcall(function()
        local f = io.open(FILE, "r")
        if f then fileval = f:read("*a"); f:close() end
    end)
    local charval
    pcall(function() charval = require("lw.core").loadNumber("PROBE") end)
    LW.log("persist_get: in memory =", tostring(mem), "| in file =", tostring(fileval), "| in character data =", tostring(charval))
end

-- The save and load hooks that used to be here wrote a test value into the game's own save record on every save. The probe
-- proved saving works (through the character data used above), and the hook was the main suspect in a crash that happened in the
-- same second as a save, so it was removed. Nothing here touches the save record any more.
LW.log("persistence probe ready: LW.persist_set(7) / LW.persist_get()")
