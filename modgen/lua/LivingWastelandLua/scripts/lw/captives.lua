-- Captive helpers: who is the player carrying, and from which faction? Dialogue scripts use LW.captive() for faction-aware lines.
local core = require("lw.core")
local C = {}

-- the captive carried by this character, and their faction name (either may be nil)
function C.carried(c)
    local ok, h = pcall(function() return c:getCarryingObject() end)
    if not ok or not h then return nil end
    local ok2, who = pcall(function() return h:getCharacter() end)
    if not ok2 or not who then return nil end
    local fname
    pcall(function() fname = core.factionOf(who):getName() end)
    return who, fname
end

-- first player character who is carrying someone
function C.playerCarrying()
    for _, c in ipairs(core.playerCharacters()) do
        local who, fname = C.carried(c)
        if who then return c, who, fname end
    end
end

C.lines = {
    ["The Holy Nation"]  = { "A heretic, or a friend of heretics. The Flame will sort it out.", "Whose sin is that on your shoulder?" },
    ["Shek Kingdom"]     = { "You carry a Shek. Put him down, or answer to the Kingdom.", "A warrior, carried like a sack. That is an insult." },
    ["United Cities"]    = { "A citizen in a sack. The Cities will want a word.", "Is that man wanted, or only unlucky?" },
    ["Slave Traders"]    = { "Prime stock. Name a price and do not tell me where you found him.", "Carry him this way. We pay on the nail." },
    ["Anti-Slavers"]     = { "Whoever that is, you will not sell him in my sight.", "Put him down gently. We will see to him." },
}
C.default = { "Another body on someone's back. Not the first I have seen today.", "You carry him as if he owes you." }

-- the speaker (a dialogue script's speaker) comments on the captive the target is carrying
function C.comment(speaker, target)
    local who, fname = C.carried(target)
    if not who then return false end
    local set = C.lines[fname or ""] or C.default
    speaker:say(set[math.random(#set)])
    return true
end

function C.register(eventIds)
    _G.LW = _G.LW or {}
    _G.LW.captive = C
end

return C
