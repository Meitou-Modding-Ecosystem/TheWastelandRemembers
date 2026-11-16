-- Mock test for outlaws, drops, captives, base, recruits, domepapers. Run: lua with SCRIPTS set to the scripts folder.
SCRIPTS = SCRIPTS or "scripts"
package.path = SCRIPTS .. "/?.lua;" .. package.path
local log = {}
function print(...) local t={} for i=1,select('#',...) do t[#t+1]=tostring((select(i,...))) end log[#log+1]=table.concat(t," ") end
local handlers = {}
Events = { on=function(name, fn) handlers[#handlers+1]={name=name,fn=fn}; return #handlers end, off=function() end }
local function fire(name, ...) local out; for _,h in ipairs(handlers) do if h.name==name then local r=h.fn(...); if r~=nil then out=r end end end return out end
local hours = 100
local PF = {name="Player", getName=function() return "Player" end}
local function fac(n) return {getName=function() return n end} end
local says = {}
local function mkchar(name, f)
  local c={fdata={}, items={}}
  c.getName=function() return name end
  c.getFaction=function() return f end
  c.getAppearanceData=function() return {fdata=c.fdata} end
  c.getPosition=function() return {x=10,y=0,z=10} end
  c.isUnconcious=function() return c.ko end
  c.getHandle=function() return "hand" end
  c.say=function(s,t) says[#says+1]=name..": "..t end
  c.giveItem=function(s,item) c.items[#c.items+1]=item.name; return true end
  c.getInventory=function() return {addItem=function(s,item) c.items[#c.items+1]=item.name; return true end} end
  c.setFaction=function(s,f2) c.fac2=f2 end
  return c
end
local hero = mkchar("Hero", PF)
local victimCarried = mkchar("Prisoner", fac("Shek Kingdom"))
hero.getCarryingObject=function() return {getCharacter=function() return victimCarried end} end
local pi = { participant=PF, getAllPlayerCharacters=function() local l={hero}; return setmetatable({size=function() return 1 end},{__index=function(t,k) return l[k] end}) end }
function getPlayerInterface() return pi end
local gw = { factionMgr={ getFactionByName=function(s,n) return fac(n) end },
  gamedata={ getDataByName=function(s,n,t) return {name=n} end },
  getTimeStamp_inGameHours=function() return {getTotalDays=function() return hours/24 end, getTotalHours=function() return hours end} end,
  showPlayerAMessage_withLog=function(s,m) print("MSG:"..m) end }
function getGameWorld() return gw end
local spawned = {}
function getRootObjectFactory() return { createRandomSquad=function(s,f,pos,ht,mx,home,tmpl) spawned[#spawned+1]=tmpl.name end,
  createItem=function(s,gd) return {name=gd.name} end } end
CrimeEnum = {}; itemType = { SQUAD_TEMPLATE=52, ITEM=4 }; SquadType = { SQ_ROAMING=2 }
BuildingFunction = { BF_SHOP=9, BF_TURRET=12, BF_WALL=20, BF_BED=6, BF_CAGE=8 }
EventTriggerEnum = { EV_I_SEE_NEUTRAL_SQUAD=40 }

local cfg = require("lw.config")
for k in pairs(cfg.enabled) do cfg.enabled[k]=true end
cfg.enabled.hunters=false
cfg.outlawChance = 1.0; cfg.baseChance = 1.0
dofile(SCRIPTS .. "/init/50_lw_start.lua")
local LW = _G.LW
local function tick(h) hours = hours + h; for i=1,40 do fire("GameWorld::charsUpdate") end end
tick(1); tick(13)
print("outlaw squads spawned:", #spawned, spawned[1])
-- kill an outlaw after hitting it
local orsa = mkchar("Orsa Quillon", fac("Dust Bandits"))
fire("Character::_NV_hitByMeleeAttack", orsa, 0, {}, hero); fire("Character::declareDead", orsa)
print("Orsa killed saved:", tostring(hero.fdata["LW_OUTLAW_OrsaQuillon"]))
-- drops
local rhino = mkchar("Rhinobot", fac("Second Empire"))
fire("Character::_NV_hitByMeleeAttack", rhino, 0, {}, hero); fire("Character::declareDead", rhino)
local cann = mkchar("Cannibal", fac("Cannibals"))
cfg.drops[2].chance = 1.0
fire("Character::_NV_hitByMeleeAttack", cann, 0, {}, hero); fire("Character::declareDead", cann)
print("leader items:", table.concat(hero.items, ", "))
-- dome papers
local hoa = mkchar("Head of Agriculture", fac("Ancient"))
require('lw.domepapers').chance = 1.0; fire("Character::_NV_init", hoa)
print("Head of Agriculture carries:", table.concat(hoa.items, ", "))
-- recruits
local venna = mkchar("Venna Marsh", fac("Anti-Slavers"))
fire("PlayerInterface::recruit", pi, venna)
print("venna loyalty:", LW.recruits.loyalty["Venna Marsh"])
-- base reactions
local town = { getFaction=function() return PF end, getBuildingsWithFunction=function(s,bf) if bf==9 then return {1,2} end return {} end }
local visitor = mkchar("Trader", fac("Traders Guild")); visitor.getTown=function() return town end
fire("Dialogue::sendEvent", {}, visitor, 40, true)
-- captives
LW.captive.comment(visitor, hero)
for _,l in ipairs(says) do print("SAY", l) end
for _,l in ipairs(log) do io.write(l, "\n") end
