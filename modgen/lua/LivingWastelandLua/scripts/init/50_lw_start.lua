-- Entry point for the Living Wasteland Lua features. Everything is OFF until switched on in lw/config.lua.
local previous = _G.LW and _G.LW._shutdown
if previous then pcall(previous) end

local core = require("lw.core")
local cfg = core.cfg
local eventIds = {}
local LW = _G.LW or {}
_G.LW = LW

local function shutdown()
    for _, id in ipairs(eventIds) do pcall(Events.off, id) end
    eventIds = {}
end
LW._shutdown = shutdown

math.randomseed(os.time())
eventIds[#eventIds + 1] = Events.on("GameWorld::charsUpdate", function() core.onCharsUpdate() end)

local ok, err = pcall(function()
    if cfg.enabled.notoriety then
        local N = require("lw.notoriety"); N.register(eventIds)
        LW.title = N.title
    end
    if cfg.enabled.heat then
        local H = require("lw.heat"); H.register(eventIds)
        LW.heat = H
    end
    if cfg.enabled.hunters and cfg.enabled.heat then
        require("lw.hunters").register()
    end
    if cfg.enabled.nemesis then
        local M = require("lw.nemesis"); M.register(eventIds)
        LW.nemesis = M
    end
    if cfg.enabled.outlaws then LW.outlaws = require("lw.outlaws"); LW.outlaws.register(eventIds) end
    if cfg.enabled.drops then LW.drops = require("lw.drops"); LW.drops.register(eventIds) end
    if cfg.enabled.captives then LW.captive = require("lw.captives"); LW.captive.register(eventIds) end
    if cfg.enabled.base then require("lw.base").register(eventIds) end
    if cfg.enabled.recruits then LW.recruits = require("lw.recruits"); LW.recruits.register(eventIds) end
    if cfg.enabled.domepapers then require("lw.domepapers").register(eventIds) end
    if cfg.enabled.tracker then LW.tracker = require("lw.tracker"); LW.tracker.register(eventIds) end
    if cfg.enabled.calm then LW.calm = require("lw.calm"); LW.calm.register(eventIds) end
    if cfg.enabled.ideology then LW.ideology = require("lw.ideology"); LW.ideology.register(eventIds) end
    if cfg.enabled.body then LW.body = require("lw.body"); LW.body.register(eventIds) end
    if cfg.enabled.chatter then require("lw.chatter").register(eventIds) end
end)
if not ok then core.log("startup failed:", tostring(err)); shutdown() end

-- console helpers that work even with everything off
LW.config = cfg
function LW.status()
    for k, v in pairs(cfg.enabled) do core.log("feature", k, v and "ON" or "off") end
end
core.log("Lua features loaded (all off unless enabled in lw/config.lua). LW.status() lists them.")
