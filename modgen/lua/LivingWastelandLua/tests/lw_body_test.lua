SCRIPTS = SCRIPTS or "scripts"
package.path = SCRIPTS .. "/?.lua;" .. package.path
local out = {}
print = function(...) end
local handlers = {}
Events = { on=function(n,f) handlers[#handlers+1]={n=n,f=f}; return #handlers end, off=function() end }
local PF = {}
local hours = 10
local function mk(name, blood, crippled)
  local med = {blood=blood, crippled=crippled or false, unconcious=false, dead=false}
  function med:getMaxBlood() return 100 end
  local c = {}
  med.me = c
  function c:getName() return name end
  function c:getFaction() return PF end
  function c:getMedical() return med end
  function c:say(t) out[#out+1] = name..": "..t end
  c.med = med
  return c
end
local a, b, d = mk("Hamut", 100), mk("Miu", 30), mk("Jaglonger", 80, true)
local list = {a, b, d}
getPlayerInterface=function() return {participant=PF, getAllPlayerCharacters=function() return setmetatable({size=function() return #list end},{__index=function(t,k) return list[k] end}) end} end
getGameWorld=function() return {getTimeStamp_inGameHours=function() return {getTotalHours=function() return hours end,getTotalDays=function() return hours/24 end} end} end
local core = require("lw.core")
local cfg = require("lw.config"); cfg.bodyPerHour = 5
local B = require("lw.body"); B.register({})
-- fire the amputate hook
for _,h in ipairs(handlers) do if h.n=="MedicalSystem::amputate" then h.f(a.med, 1, true, {}) end end
B.poll()
assert(#out == 3, "expected 3 barks, got "..#out)
for _,l in ipairs(out) do io.write(l, "\n") end
-- cooldown: polling again right away says nothing new for hurt/crippled
local n = #out
B.poll(); assert(#out == n, "cooldown failed")
hours = 13; B.poll(); assert(#out > n, "should speak again after cooldown")
-- unconscious never speaks
b.med.unconcious = true; local m = #out; hours = 20; B.poll(); local spoke_b = false
for i = m+1, #out do if out[i]:find("Miu") then spoke_b = true end end
assert(not spoke_b, "KO'd member spoke")
-- temperament stable
assert(B.temperament(a) == B.temperament(a))
io.write("ok\n")
