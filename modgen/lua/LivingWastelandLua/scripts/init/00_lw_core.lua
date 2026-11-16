-- Living Wasteland (Lua side). Shared helpers. Everything here is defensive: a failed probe must never break the game.
-- Expose a table to the console and to other scripts of ours.
_G.LW = _G.LW or {}
local LW = _G.LW
LW.version = "0.1-probe"

function LW.log(...)
    local parts = {}
    for i = 1, select("#", ...) do parts[#parts + 1] = tostring((select(i, ...))) end
    local msg = "[LivingWasteland] " .. table.concat(parts, " ")
    if KenshiLua and KenshiLua.log then KenshiLua.log(msg) else print(msg) end
    local ok, core = pcall(require, "lw.core")
    if ok and type(core) == "table" and core.filelog then core.filelog(msg) end
end

-- run f with pcall; log a failure instead of raising
function LW.try(label, f, ...)
    local ok, res = pcall(f, ...)
    if not ok then LW.log("FAILED", label, ":", tostring(res)) end
    return ok, res
end

LW.log("core loaded, version", LW.version)
