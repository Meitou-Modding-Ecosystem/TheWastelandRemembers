-- Shared helpers: logging, time, player characters, saved numbers, a task scheduler.
local cfg = require("lw.config")
local core = { cfg = cfg }

local PREFIX = "[LivingWasteland] "
local SAVE = "LW_"

-- Everything we log is also appended to lw_log.txt in this mod's folder (the Log Viewer can overflow; this file does not).
local logPath
do
    local src = debug.getinfo(1, "S").source or ""
    if src:sub(1, 1) == "@" then src = src:sub(2) end
    local root = src:gsub("\\", "/"):match("^(.*)/scripts/lw/core%.lua$")
    if root and root ~= "" then logPath = root .. "/lw_log.txt" end
end
local logStarted = _G.LW_LOG_STARTED == true   -- survives a script reload, so a reload does not rotate the log mid-session
-- A relaunch used to overwrite the last session's log. Now the previous two sessions are kept (lw_log_prev.txt, lw_log_prev2.txt).
local function copyFile(from, to)
    local src = io.open(from, "rb")
    if not src then return false end
    local data = src:read("*a"); src:close()
    local dst = io.open(to, "wb")
    if not dst then return false end
    dst:write(data); dst:close()
    return true
end
local function rotateLogs()
    if not logPath then return end
    local base = logPath:gsub("lw_log%.txt$", "")
    pcall(function()
        copyFile(base .. "lw_log_prev.txt", base .. "lw_log_prev2.txt")
        copyFile(logPath, base .. "lw_log_prev.txt")
    end)
end
function core.filelog(msg)
    if not logPath then return end
    if not logStarted then rotateLogs() end
    pcall(function()
        local f = io.open(logPath, logStarted and "a" or "w")
        if f then
            f:write(os.date("%H:%M:%S "), msg, "\n")
            f:close()
            logStarted = true
            _G.LW_LOG_STARTED = true
        end
    end)
end
core.logPath = function() return logPath end

function core.log(...)
    local t = {}
    for i = 1, select("#", ...) do t[#t + 1] = tostring((select(i, ...))) end
    local msg = PREFIX .. table.concat(t, " ")
    print(msg)
    core.filelog(msg)
end

function core.debug(...)
    if cfg.debug then core.log("[debug]", ...) end
end

function core.notify(message)
    core.log(message)
    pcall(function() getGameWorld():showPlayerAMessage_withLog(message, true) end)
end

function core.day()
    return getGameWorld():getTimeStamp_inGameHours():getTotalDays()
end

function core.hours()
    return getGameWorld():getTimeStamp_inGameHours():getTotalHours()
end

function core.playerInterface()
    return getPlayerInterface()
end

function core.playerFaction()
    local pi = getPlayerInterface()
    return pi and pi.participant
end

-- list of the player's characters as a plain Lua array (the game's list is 1-based)
function core.playerCharacters()
    local out = {}
    local list = getPlayerInterface():getAllPlayerCharacters()
    if not list then return out end
    for i = 1, list:size() do
        local c = list[i]
        if c then out[#out + 1] = c end
    end
    return out
end

local function factionOf(c)
    local ok, f = pcall(function() return c:getFaction() end)
    if ok and f then return f end
    ok, f = pcall(function() return c:getBody():getFaction() end)
    return ok and f or nil
end
core.factionOf = factionOf

function core.isPlayer(c)
    if not c then return false end
    local pf = core.playerFaction()
    return pf ~= nil and factionOf(c) == pf
end

-- The character that owns the dialogue sending an event (the one who would bark). Dialogue::sendEvent hands us `self` (a Dialogue) and
-- `who`; which of them is the NPC was never verified, so prefer the dialogue's own character and fall back to `who`.
function core.speakerOf(dialogue, who)
    local ok, c = pcall(function() return dialogue:getCharacter() end)
    if ok and c then return c end
    return who
end

function core.nameOf(c)
    local ok, n = pcall(function() return c:getName() end)
    return ok and tostring(n) or "?"
end

-- ===== saved numbers =====
-- Stored in each player character's appearance data (saved by the engine with the character, as Animal_Husbandry-Lua does).
-- Written to every player character so one dying or leaving loses nothing; read as the largest value found.
function core.saveNumber(key, value)
    for _, c in ipairs(core.playerCharacters()) do
        pcall(function()
            local ad = c:getAppearanceData()
            if ad then ad.fdata[SAVE .. key] = value end
        end)
    end
end

function core.loadNumber(key)
    local best
    for _, c in ipairs(core.playerCharacters()) do
        pcall(function()
            local ad = c:getAppearanceData()
            local v = ad and ad.fdata[SAVE .. key]
            if type(v) == "number" and (best == nil or v > best) then best = v end
        end)
    end
    return best
end

-- ===== tasks: run something every N in-game hours from the world update =====
core.tasks = {}
function core.addTask(name, intervalHours, fn)
    core.tasks[name] = { hours = intervalHours, fn = fn, last = nil }
end

-- Heartbeat: one line every few real minutes, so a log from a crashed session still shows how long the game ran and when it stopped.
local startedAt = os.time()
local lastBeat = os.time()
local BEAT_SECONDS = 120
local function heartbeat()
    local okH, hours = pcall(core.hours)
    local on = {}
    for k, v in pairs(cfg.enabled) do if v then on[#on + 1] = k end end
    table.sort(on)
    core.filelog(string.format("[heartbeat] %d min played, game hour %s, features on: %s",
        math.floor((os.time() - startedAt) / 60), okH and string.format("%.1f", hours) or "?", #on > 0 and table.concat(on, ",") or "none"))
    local calm = package.loaded["lw.calm"]
    if calm and calm.installed then core.filelog(string.format("[heartbeat] calm: sightings %d, vetoed %d, let through %d", calm.seen or 0, calm.vetoed or 0, calm.passed or 0)) end
end

local frames = 0
function core.onCharsUpdate()
    frames = frames + 1
    if frames < 30 then return end
    frames = 0
    if os.time() - lastBeat >= BEAT_SECONDS then
        lastBeat = os.time()
        pcall(heartbeat)
    end
    local ok, now = pcall(core.hours)
    if not ok or not now then return end
    for name, t in pairs(core.tasks) do
        if t.last == nil or now < t.last then
            t.last = now                       -- first run, or an earlier save was loaded
        elseif now - t.last >= t.hours then
            t.last = now
            local okRun, err = pcall(t.fn, now)
            if not okRun then core.log("task " .. name .. " failed: " .. tostring(err)) end
        end
    end
end

return core
