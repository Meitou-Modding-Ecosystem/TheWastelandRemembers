SCRIPTS = SCRIPTS or "scripts"
package.path = SCRIPTS .. "/?.lua;" .. package.path
local out = {}
function print(...) local t={} for i=1,select('#',...) do t[#t+1]=tostring((select(i,...))) end out[#out+1]=table.concat(t," ") end
local handlers = {}
Events = { on=function(n,f) handlers[#handlers+1]={n=n,f=f}; return #handlers end, off=function() end }
local vars = {}
local hours = 10
getGameWorld = function() return {
  gamedata={ getDataByName=function(s,name,t) if vars[name]~=nil and t==1000 then return {idata={value=vars[name]}} end end },
  showPlayerAMessage_withLog=function(s,m) print("MSG:"..m) end,
  getTimeStamp_inGameHours=function() return {getTotalHours=function() return hours end, getTotalDays=function() return hours/24 end} end } end
local core = require("lw.core")
local T = require("lw.tracker")
T.register({})
local function tick(h) hours = hours + h; for i=1,40 do for _,x in ipairs(handlers) do if x.n=="GameWorld::charsUpdate" then x.f() end end end end
-- core registers its own charsUpdate in start script; do it here
Events.on("GameWorld::charsUpdate", function() core.onCharsUpdate() end)
tick(1)
vars["Quest qc_hunt"] = 1; tick(1)
vars["Quest qc_hunt"] = 2; tick(1)
vars["Quest qc_hunt"] = 99; tick(1)
vars["Contract ol_orsa_quillon_contract"] = 1; tick(1)
T.debug(); T.report()
for _,l in ipairs(out) do io.write(l,"\n") end
