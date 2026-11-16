SCRIPTS = SCRIPTS or "scripts"
package.path = SCRIPTS .. "/?.lua;" .. package.path
local log = {}
function print(...) local t={} for i=1,select('#',...) do t[#t+1]=tostring((select(i,...))) end log[#log+1]=table.concat(t," ") end

-- ---- mock engine ----
local handlers = {}
Events = { on=function(name, fn) handlers[#handlers+1]={name=name,fn=fn}; return #handlers end, off=function() end }
local function fire(name, ...) local out; for _,h in ipairs(handlers) do if h.name==name then local r=h.fn(...); if r~=nil then out=r end end end return out end

local hours = 100
local PF = {name="Player"}
local HN = {name="The Holy Nation", relations={ affectRelations=function(self,f,a,m) print("MOCK relation",a) end, setEnemy=function() print("MOCK setEnemy") end }, getName=function(s) return s.name end}
local function mkchar(name, fac)
  local c={fdata={}}
  c.getName=function() return name end
  c.getFaction=function() return fac end
  c.getAppearanceData=function() return {fdata=c.fdata} end
  c.getPosition=function() return {x=10,y=0,z=10} end
  c.isUnconcious=function() return c.ko end
  c.getHandle=function() return "hand" end
  return c
end
local hero = mkchar("Hero", PF)
local boss = mkchar("Holy Lord Phoenix", HN)
local esata = mkchar("Lady Esata", HN)
local rando = mkchar("Dust Boss Orsa", {})
local pi = { participant=PF, getAllPlayerCharacters=function() local l={hero}; return setmetatable({size=function() return 1 end},{__index=function(t,k) return l[k] end}) end }
function getPlayerInterface() return pi end
local gw = { factionMgr={ getFactionByName=function(s,n) if n=="The Holy Nation" then return HN end end },
  gamedata={ getDataByName=function(s,n,t) return {name=n} end },
  getTimeStamp_inGameHours=function() return {getTotalDays=function() return hours/24 end, getTotalHours=function() return hours end} end,
  showPlayerAMessage_withLog=function(s,m) print("MSG:"..m) end }
function getGameWorld() return gw end
local spawned = {}
function getRootObjectFactory() return { createRandomSquad=function(...) spawned[#spawned+1]=select(2,...) end } end
CrimeEnum = { CRIME_ASSAULT=5, CRIME_STEALING=3 }
itemType = { SQUAD_TEMPLATE=52 }
SquadType = { SQ_ROAMING=2 }

-- ---- enable everything and start ----
local cfg = require("lw.config")
for k in pairs(cfg.enabled) do cfg.enabled[k]=true end
cfg.hunterChancePerCheck = 1.0
dofile(SCRIPTS .. "/init/50_lw_start.lua")
local LW = _G.LW

local function tick(h) hours = hours + h; for i=1,40 do fire("GameWorld::charsUpdate") end end

-- notoriety: hit and kill Phoenix, then Esata
fire("Character::_NV_hitByMeleeAttack", boss, 0, {}, hero)  -- attacker hero (player)
fire("Character::declareDead", boss)
print("TITLE after Phoenix:", tostring(LW.title()))
fire("Character::_NV_hitByMeleeAttack", esata, 0, {}, hero)
fire("Character::declareDead", esata)
print("TITLE after Esata:", tostring(LW.title()))
-- a kill by someone else does not count
fire("Character::declareDead", mkchar("Tengu", HN))
print("saved score on hero:", tostring(hero.fdata["LW_NOTO_SCORE"]))

-- heat: assault witnessed by the Holy Nation, with the hero as criminal
tick(1)
for i=1,6 do fire("BountyManager::notifyCrimeWitnessed", {me=hero}, HN, {}, 100, 5) end   -- 6 x 4 = 24 -> tier 1
print("heat HN after 6 assaults:", LW.heat.get("The Holy Nation"))
fire("BountyManager::notifyCrimeWitnessed", {me=hero}, HN, {}, 100, 5)                    -- 28 -> tier 2
-- not the player: no heat
local before = LW.heat.get("The Holy Nation")
fire("BountyManager::notifyCrimeWitnessed", {me=rando}, HN, {}, 100, 5)
print("npc crime changed heat:", LW.heat.get("The Holy Nation") ~= before)
tick(7)   -- past hunterCheckHours
print("squads spawned:", #spawned)
-- nemesis: Orsa hits hero, hero goes KO
fire("Character::_NV_hitByMeleeAttack", hero, 0, {}, rando)
hero.ko = true
tick(0.1)
print("nemesis recorded:", tostring(LW.nemesis and LW.nemesis.list["Dust Boss Orsa"] and LW.nemesis.list["Dust Boss Orsa"].defeats))
local buffed = fire("CharStats::getStat", {me=rando}, 1, false, 50)
print("nemesis stat 50 ->", tostring(buffed))

for _,l in ipairs(log) do io.write(l, "\n") end
