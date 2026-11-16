-- The 12 recruits: a message when they join, a loyalty number, and they leave if you let them down.
local core = require("lw.core")
local data = require("lw.data")
local cfg = core.cfg
local R = { loyalty = {}, origin = {} }

local set = {}
for _, n in ipairs(data.recruits) do set[n] = true end

R.lines = {
    join  = "I will walk with you. Do not make me regret it.",
    leave = "I am done. Do not follow.",
}

local function key(name) return "RECRUIT_" .. (tostring(name):gsub("[^%w]", "")) end

function R.onRecruit(c)
    local name = core.nameOf(c)
    if not set[name] then return end
    R.loyalty[name] = core.loadNumber(key(name)) or cfg.recruitLoyaltyStart
    pcall(function() R.origin[name] = core.factionOf(c) end)
    core.saveNumber(key(name), R.loyalty[name])
    pcall(function() c:say(R.lines.join) end)
    core.notify(name .. " has joined you.")
end

local function change(name, delta)
    if not R.loyalty[name] then return end
    R.loyalty[name] = math.max(0, math.min(100, R.loyalty[name] + delta))
    core.saveNumber(key(name), R.loyalty[name])
end

-- a squad member who is a recruit gets knocked out: they lose a little faith when the leader was nowhere near
local function check()
    local chars = core.playerCharacters()
    local leader = chars[1]
    for _, c in ipairs(chars) do
        local name = core.nameOf(c)
        if R.loyalty[name] then
            local ok, ko = pcall(function() return c:isUnconcious() end)
            if ok and ko and not R["ko_" .. name] then
                R["ko_" .. name] = true
                change(name, cfg.recruitLoyaltyKo)
            elseif ok and not ko then
                R["ko_" .. name] = nil
            end
            if R.loyalty[name] < cfg.recruitLeaveBelow then R.leave(c) end
        end
    end
end

function R.leave(c)
    local name = core.nameOf(c)
    pcall(function() c:say(R.lines.leave) end)
    core.notify(name .. " has left you.")
    local back = R.origin[name]
    pcall(function()
        if back then c:setFaction(back, nil) end
    end)
    R.loyalty[name] = nil
    core.saveNumber(key(name), 0)
end

function R.win(victim, attacker)
    if core.isPlayer(attacker) then
        local name = core.nameOf(attacker)
        if R.loyalty[name] then change(name, cfg.recruitLoyaltyWin) end
    end
end

function R.register(eventIds)
    eventIds[#eventIds + 1] = Events.on("PlayerInterface::recruit", function(self, character) pcall(R.onRecruit, character) end)
    eventIds[#eventIds + 1] = Events.on("Character::declareDead", function(self) end)
    core.addTask("recruits_check", 0.5, check)
end

return R
