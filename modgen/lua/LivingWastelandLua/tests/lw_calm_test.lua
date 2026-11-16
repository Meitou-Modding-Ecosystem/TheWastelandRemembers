SCRIPTS = SCRIPTS or "scripts"
package.path = SCRIPTS .. "/?.lua;" .. package.path
print = function(...) end
local handlers = {}
Events = { on=function(n,f) handlers[#handlers+1]={n=n,f=f}; return #handlers end, off=function() end }
local PF = {}
local function mk(fac) local c={}; function c:getFaction() return fac end; return c end
local me, npc = mk(PF), mk({})
local hours = 10
getPlayerInterface=function() return {participant=PF, getAllPlayerCharacters=function() return setmetatable({size=function() return 1 end},{__index=function(t,k) return me end}) end} end
getGameWorld=function() return {getTimeStamp_inGameHours=function() return {getTotalHours=function() return hours end,getTotalDays=function() return hours/24 end} end} end
EventTriggerEnum={EV_I_SEE_NEUTRAL_SQUAD=3,EV_I_SEE_ALLY_PLAYER=4,EV_BAR_TALK=9}
local clock = 100
os.clock = function() return clock end
local cfg=require("lw.config"); cfg.calmGraceSeconds=25; cfg.calmBurst=2; cfg.calmWindowSeconds=4
local C=require("lw.calm"); C.register({})
local core = require("lw.core"); core.tasks["calm_install"].fn()   -- the scheduler would run this once the world is up
local function send(who,what) local r; for _,h in ipairs(handlers) do if h.n=="Dialogue::sendEvent" then local x=h.f({},who,what,true); if x~=nil then r=x end end end return r end
local function fire(n) for _,h in ipairs(handlers) do if h.n==n then h.f() end end end
-- new world: grace
C.graceStart = os.clock(); C.stamps = {}
clock = 110
assert(send(npc,3)==false, "grace should veto")
clock = 130
assert(send(npc,3)==nil and send(npc,3)==nil, "first two after grace pass")
assert(send(npc,3)==false, "third in the window is vetoed")
clock = 140
assert(send(npc,3)==nil, "window passed")
-- player and other events untouched
assert(send(me,3)==nil and send(npc,9)==nil, "untouched")
-- time going backwards counts as a new game
hours = 1; C.sample(); clock = 141
assert(send(npc,3)==false, "grace after time went backwards")
io.write("ok\n")
