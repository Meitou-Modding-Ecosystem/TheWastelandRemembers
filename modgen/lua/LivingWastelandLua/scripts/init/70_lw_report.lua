-- One command for a test session: LW.report()
-- Runs the probes, lists which features are on, summarises which of our lines fired, and writes everything to lw_log.txt
-- (Kenshi\mods\LivingWastelandLua\lw_log.txt). Send that one file.
local LW = _G.LW

-- Console: LW.quests() lists the quests and contracts you have taken (with their next step), without turning the tracker on.
-- It only reads our quest variables; nothing is hooked.
function LW.quests()
    local ok, err = pcall(function() require("lw.tracker").report() end)
    if not ok then LW.log("quests: could not read the journal:", tostring(err)) end
end

function LW.report()
    LW.log("========== REPORT ==========")
    LW.try("version", function() LW.log("LW version", tostring(LW.version)) end)
    LW.try("probe", function() if LW.probe then LW.probe() end end)
    LW.try("probe_vars", function() if LW.probe_vars then LW.probe_vars() end end)
    LW.try("status", function() if LW.status then LW.status() end end)
    LW.try("quests", function() require("lw.tracker").report() end)
    LW.try("ideology", function() if LW.ideology and LW.ideology.list then LW.ideology.list() end end)
    LW.try("dialogue_seen", function() if LW.dialogue_seen then LW.dialogue_seen() end end)
    LW.try("crimes", function() LW.log("crimes seen this session:", tostring(LW.crimes and #LW.crimes or 0)) end)
    LW.try("log file", function()
        local core = require("lw.core")
        LW.log("log file:", tostring(core.logPath and core.logPath() or "unknown"))
    end)
    LW.try("session", function() LW.log("session: report written at", os.date("%Y-%m-%d %H:%M:%S"), "(the previous two sessions are kept as lw_log_prev.txt and lw_log_prev2.txt)") end)
    LW.log("========== END REPORT ==========")
end

LW.log("report ready: type LW.report() in the console for one combined report")
