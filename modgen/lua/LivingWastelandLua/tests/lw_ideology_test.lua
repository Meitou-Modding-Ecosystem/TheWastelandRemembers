SCRIPTS = SCRIPTS or "scripts"
package.path = SCRIPTS .. "/?.lua;" .. package.path
print = function(...) end
local handlers = {}
Events = { on=function(n,f) handlers[#handlers+1]={n=n,f=f}; return #handlers end, off=function() end }
local PF = {name="Player"}
local HF, SF = {name="The Holy Nation"}, {name="Skeletons"}
local out = {}
local function mk(name, fac)
  local c = {}
  function c:getName() return name end
  function c:getFaction() return fac end
  function c:say(t) out[#out+1] = name..": "..t end
  return c
end
for _, f in ipairs({PF, HF, SF}) do f.getName = function(self) return self.name end end
local me, guard, skel = mk("Hero", PF), mk("Guard", HF), mk("Bones", SF)
local ad = {fdata = {}}
function me:getAppearanceData() return ad end
local list = {me}
getPlayerInterface=function() return {participant=PF, getAllPlayerCharacters=function() return setmetatable({size=function() return #list end},{__index=function(t,k) return list[k] end}) end} end
local hours = 10
getGameWorld=function() return {getTimeStamp_inGameHours=function() return {getTotalHours=function() return hours end,getTotalDays=function() return hours/24 end} end} end
EventTriggerEnum={EV_I_SEE_NEUTRAL_SQUAD=3,EV_BAR_TALK=9,EV_I_SEE_ALLY_PLAYER=4}
local cfg=require("lw.config"); cfg.ideologyChance=100; cfg.ideologyPerHour=5
local I=require("lw.ideology"); I.register({})
local function send(who,what) local r; for _,h in ipairs(handlers) do if h.n=="Dialogue::sendEvent" then local x=h.f({},who,what,true); if x~=nil then r=x end end end return r end
assert(send(guard,3)==nil and #out==0, "spoke without ideology")
I.set("anti_okran", {"slavery"}, "The Gate")
assert(send(guard,3)==false and #out==1, "holy guard should react to anti_okran")
assert(out[1]:find("Guard"), out[1])
assert(send(guard,3)==nil and #out==1, "cooldown")
hours = 30
assert(send(skel,3)==nil, "skeleton had no line")
I.set(nil, {"pro_skeleton"})
assert(send(skel,3)==false, "skeleton should approve pro_skeleton")
assert(send(skel,99)==nil and send(me,3)==nil)
I.set("okran", {"male_supremacy","free_trade"})
I.major=nil; I.minors={}; I.load()
assert(I.major=="okran" and I.minors.male_supremacy and I.minors.free_trade, "load failed")
assert(I.set("nonsense")==false)
io.write("ok\n")
