-- Quest journal: finds which of our quests and contracts you have taken (their progress is kept in BFrizz variables), tells you when one
-- changes, and shows them in a window (F7). Entries come from lw/quests_data.lua, generated from the content packs.
-- Reading a variable from Lua is UNVERIFIED: run LW.tracker.debug() first. If it prints 'unreadable', the journal falls back to
-- remembering what it sees you accept (it cannot, so it stays empty): tell me and I will switch to another method.
local core = require("lw.core")
local quests = require("lw.quests_data")
local T = { last = {}, window = nil, list = nil, text = nil, rows = {} }

-- F7 is Kenshi's own "hide the UI" key and F6 is Cheat Menu, so the journal uses Y (key code 21). Change journalKeyCode in config.lua to rebind; LW.journal() also opens it.
local KC_JOURNAL = core.cfg.journalKeyCode or 21
local JOB_CAP = 5                       -- keep in step with JOB_CAP in modgen/content.py

local function readVar(name)
    local ok, v = pcall(function()
        local gd = getGameWorld().gamedata:getDataByName(name, 1000)
        if not gd then return nil end
        return gd.idata["value"]
    end)
    if ok and type(v) == "number" then return v end
    return nil
end

function T.status(q, v)
    local done = (q.var:find("^Contract") and 2) or 99
    if v == nil then return "unknown" end
    if v == 0 then return "not taken" end
    if v >= done then return "done" end
    return "active"
end

local function objective(q, v)
    local i = math.max(1, math.min(v, #q.objectives))
    return q.objectives[i] or ""
end

function T.snapshot()
    local out = {}
    for _, q in ipairs(quests) do
        local v = readVar(q.var)
        local st = T.status(q, v)
        out[#out + 1] = { q = q, v = v, status = st }
    end
    return out
end

function T.check()
    for _, e in ipairs(T.snapshot()) do
        local prev = T.last[e.q.var]
        if prev ~= nil and e.v ~= nil and e.v ~= prev and e.status ~= "not taken" then
            if e.status == "done" then core.notify("Journal: " .. e.q.title .. " is complete.")
            else core.notify("Journal updated: " .. e.q.title .. ".") end
        end
        if e.v ~= nil then T.last[e.q.var] = e.v end
    end
    -- open jobs: tell the player when the cap is reached (no one offers more work until one is handed in) and when it frees up
    local jobs = readVar("LW open jobs")
    if jobs ~= nil then
        if T.lastJobs and jobs >= JOB_CAP and T.lastJobs < JOB_CAP then
            core.notify("You carry " .. JOB_CAP .. " unfinished jobs. Nobody will offer you more until you hand one in.")
        elseif T.lastJobs and jobs < JOB_CAP and T.lastJobs >= JOB_CAP then
            core.notify("You have room for another job.")
        end
        T.lastJobs = jobs
    end
    if T.window and T.window:getVisible() then T.refresh() end
end

function T.debug()
    local readable, total = 0, 0
    for _, q in ipairs(quests) do
        total = total + 1
        local v = readVar(q.var)
        if v ~= nil then readable = readable + 1 end
        if total <= 3 then core.log("debug read", q.var, "=", tostring(v)) end
    end
    core.log("tracker: readable", readable, "of", total, readable == 0 and "(unreadable: variables cannot be read this way)" or "")
end

-- text fallback that needs no window: LW.tracker.report()
function T.report()
    local n = 0
    for _, e in ipairs(T.snapshot()) do
        if e.status == "active" then
            n = n + 1
            core.log("ACTIVE:", e.q.title, "| now:", objective(e.q, e.v), "| reward", e.q.reward)
        end
    end
    if n == 0 then core.log("no active quests found (or variables unreadable: run LW.tracker.debug())") end
end

-- ===== window (MyGUI; calls are guarded: a missing function just leaves the window plain) =====
local function ensureWindow()
    if T.window then return T.window end
    local ok = pcall(function()
        -- createWindowReal / ...BoxReal take fractions of the screen / parent; this is the call pattern a published mod (KFS) uses.
        local w = MyGUI.createWindowReal("Kenshi_WindowCX", 0.25, 0.15, 0.50, 0.65, "LW_QuestJournal", "Window")
        w:setCaption("Quest Journal")
        w:setMovable(true)
        w:on("eventWindowButtonPressed", function(win, name) if name == "close" then win:setVisible(false) end end)
        T.window = w
        T.list = MyGUI.createListBoxReal("Kenshi_ListBox", 0.03, 0.04, 0.94, 0.42, w)
        T.text = MyGUI.createEditBoxReal("Kenshi_EditBox", 0.03, 0.50, 0.94, 0.46, w)
        pcall(function() T.text:setEditMultiLine(true) end)
        pcall(function() T.text:setEditReadOnly(true) end)
        pcall(function() T.text:setEditWordWrap(true) end)
        T.list:on("eventListChangePosition", function(widget, index)
            local row = T.rows[(index or -1) + 1]
            if row then T.text:setCaption(row.detail) end
        end)
    end)
    if not ok then core.log("journal window could not be created; use LW.tracker.report() instead") return nil end
    return T.window
end

local function wrap(text, width)
    local out, line = {}, ""
    for word in tostring(text):gmatch("%S+") do
        if #line + #word + 1 > width and #line > 0 then out[#out + 1] = line; line = word
        else line = (#line > 0 and (line .. " ") or "") .. word end
    end
    if #line > 0 then out[#out + 1] = line end
    return out
end

function T.refresh()
    if not T.list then return end
    T.rows = {}
    pcall(function() T.list:removeAllItems() end)
    for _, e in ipairs(T.snapshot()) do
        if e.status == "active" or e.status == "done" then
            local mark = e.status == "done" and "[done] " or "[active] "
            local detail = e.q.title .. "\n\n" .. (e.status == "done" and "Complete." or objective(e.q, e.v)) .. "\n\nReward: " .. e.q.reward .. " cats"
            -- the list shows everything itself: some builds of the edit box below show only the first line, so the quest's next step is
            -- also written into the list as wrapped, indented lines under its title
            T.rows[#T.rows + 1] = { detail = detail }
            pcall(function() T.list:addItem(mark .. e.q.title) end)
            local lines = wrap((e.status == "done" and "Complete." or objective(e.q, e.v)) .. "  Reward: " .. e.q.reward .. " cats.", 78)
            for _, ln in ipairs(lines) do
                T.rows[#T.rows + 1] = { detail = detail }          -- selecting a detail line shows the same text below
                pcall(function() T.list:addItem("    " .. ln) end)
            end
        end
    end
    if #T.rows == 0 then pcall(function() T.list:addItem("(no quests taken yet)") end) end
    if T.lastJobs then pcall(function() T.list:addItem("Unfinished jobs: " .. T.lastJobs .. " of " .. JOB_CAP) end) end
end

function T.toggle()
    local w = ensureWindow()
    if not w then return end
    if w:getVisible() then w:setVisible(false) else T.refresh(); w:setVisible(true); pcall(function() w:upLayerItem() end) end
end

function T.register(eventIds)
    _G.LW = _G.LW or {}
    _G.LW.journal = T.toggle
    eventIds[#eventIds + 1] = Events.on("InputHandler::keyDownEvent", function(self, keyCode)
        if keyCode == KC_JOURNAL then pcall(T.toggle) end
    end)
    core.addTask("tracker_check", 0.25, T.check)
end

return T
