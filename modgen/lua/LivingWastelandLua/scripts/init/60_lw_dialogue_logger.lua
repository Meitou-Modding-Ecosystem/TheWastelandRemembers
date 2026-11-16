-- Playtest aid: log every line the game says (speaker, which dialogue line, the text) so a test needs no screenshots.
-- OFF by default. Console: LW.dialogue_log(true) / LW.dialogue_log(false). Output goes to KenshiLua.log (lines start with [LW-SAY]).
local LW = _G.LW
local on = false
local seen = {}

LW.try("hook Dialogue::say", function()
    Events.on("Dialogue::say", function(this, dialogLine)
        if not on then return end
        local speaker, lname, text, parent = "?", "?", "?", "?"
        pcall(function() speaker = this:getCharacter():getName() end)
        pcall(function() lname = dialogLine:getName() end)
        pcall(function()
            local gd = dialogLine.data
            text = gd and gd.sdata and tostring(gd.sdata["text0"]) or "?"
        end)
        pcall(function() parent = dialogLine.parent and dialogLine.parent:getName() or "-" end)
        LW.log("[LW-SAY]", speaker, "|", lname, "| parent:", parent, "|", text)
        seen[lname] = (seen[lname] or 0) + 1
    end)
end)

function LW.dialogue_log(flag)
    on = flag and true or false
    LW.log("dialogue logging", on and "ON" or "off")
end

function LW.dialogue_seen()
    local n = 0
    for k, v in pairs(seen) do n = n + 1; LW.log("seen", k, v) end
    LW.log("distinct lines seen this session:", n)
end

LW.log("dialogue logger ready: LW.dialogue_log(true)")
