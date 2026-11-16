SCRIPTS = SCRIPTS or "scripts"
package.path = SCRIPTS .. "/?.lua;" .. package.path
function print() end
local handlers = {}
Events = { on=function(n,f) handlers[#handlers+1]={n=n,f=f}; return #handlers end, off=function() end }
local function send(who,what) local out; for _,h in ipairs(handlers) do if h.n=="Dialogue::sendEvent" then local r=h.f({},who,what,true); if r~=nil then out=r end end end return out end
local PF={}
local function mk(x) local c={}; c.getPosition=function() return {x=x,y=0,z=0} end; c.getFaction=function() return PF end; return c end
local a,b,far=mk(0),mk(50),mk(5000)
local list={a,b,far}
local hours=10
getPlayerInterface=function() return {participant=PF, getAllPlayerCharacters=function() return setmetatable({size=function() return #list end},{__index=function(t,k) return list[k] end}) end} end
getGameWorld=function() return {getTimeStamp_inGameHours=function() return {getTotalHours=function() return hours end,getTotalDays=function() return hours/24 end} end} end
EventTriggerEnum={EV_ENTER_BIOME=1,EV_LAUNCH_ATTACK=2,EV_I_SEE_NEUTRAL_SQUAD=3}
local cfg=require("lw.config"); cfg.chatterPerHour=2
for k in pairs(cfg.enabled) do cfg.enabled[k]=false end
local K=require("lw.chatter"); K.register({})
print=function(...) io.write(table.concat({...}," "),"\n") end
print("together, arrival:", tostring(send(a,1)))
print("far one, arrival (alone):", tostring(send(far,1)))
print("together, unwatched event:", tostring(send(a,2)))
print("budget 2/hour: 2nd", tostring(send(b,1)), "3rd", tostring(send(a,1)))
hours=11.5
print("an hour later:", tostring(send(a,1)))

-- ambient burst limit
cfg.ambientBurst = 2; cfg.ambientWindowSeconds = 60
local npc = { getPosition=function() return {x=0,y=0,z=0} end, getFaction=function() return {} end }
local r1, r2, r3 = send(npc,3), send(npc,3), send(npc,3)
assert(r1==nil and r2==nil and r3==false, "ambient limit")
print("ambient ok")
