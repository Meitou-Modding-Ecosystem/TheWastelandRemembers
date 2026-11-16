-- Dialogue script: the speaker comments on the captive the player is carrying, in the style of the captive's faction.
-- Needs lw.captives switched on in lw/config.lua.
local LW = _G.LW
if not currentDialogue or not LW or not LW.captive then return end
local speaker = currentDialogue:getCharacter()
local hand = currentDialogue:getConversationTarget()
local target = hand and hand:getCharacter()
if speaker and target then pcall(LW.captive.comment, speaker, target) end
