-- Shared: remember what the player's side hit recently, so deaths can be credited to the player.
local core = require("lw.core")
local C = { hits = {} }

-- Last hitter wins (as a published kill counter does): every hit on a non-player updates the entry, so if someone else lands the
-- last blow before the victim dies, the player is not credited.
function C.onHit(victim, attacker)
    if not core.isPlayer(victim) then
        C.hits[core.nameOf(victim)] = { day = core.day(), player = core.isPlayer(attacker) }
    end
end

-- true if the player's side landed the last hit on this character within `days`
function C.recentlyHit(name, days)
    local h = C.hits[name]
    return h ~= nil and h.player and core.day() - h.day <= (days or core.cfg.killCreditDays)
end

function C.forget(name) C.hits[name] = nil end

function C.register(eventIds)
    if C.registered then return end
    C.registered = true
    eventIds[#eventIds + 1] = Events.on("Character::_NV_hitByMeleeAttack", function(self, cutDir, damage, attacker)
        pcall(C.onHit, self, attacker)
    end)
end

return C
