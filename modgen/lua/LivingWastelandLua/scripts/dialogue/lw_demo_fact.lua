-- Demo (not used by shipped content): the speaker states how much money the listener carries.
-- Attached through a LUA_SCRIPT record and the line field "lua": ["LW demo: say a fact"].
if not currentDialogue then return end
local speaker, target = currentDialogue.speaker, currentDialogue.target
if speaker and target then
    local ok, money = pcall(function() return target:getMoney() end)
    if ok then speaker:say("You carry " .. tostring(money) .. " cats. I counted.") end
end
