-- Dialogue script: the speaker greets the player with their earned title (needs lw.notoriety switched on in lw/config.lua).
-- Attach to a line with "lua": ["LW title greeting"] after declaring it in the pack's "lua_scripts".
local LW = _G.LW
if not currentDialogue or not LW or not LW.title then return end
local title = LW.title()
local speaker = currentDialogue.speaker
if title and speaker then
    local lines = {
        "So it is true. You are " .. title .. ".",
        "They call you " .. title .. ". I did not think I would meet you.",
        "I have heard the name: " .. title .. ". Keep your distance.",
    }
    speaker:say(lines[math.random(#lines)])
end
