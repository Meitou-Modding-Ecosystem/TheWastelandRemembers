-- Bodies and voices: a squad member speaks about what has happened to their body, in a voice that fits them.
--   * a limb lost (hook on MedicalSystem::amputate)
--   * badly hurt: blood well below maximum, or crippled (checked on a timer, never while knocked out)
-- Reads MedicalSystem fields (blood, getMaxBlood, crippled, unconcious, dead, me). Each member has a fixed temperament picked from
-- their name (stoic, dry, anxious, blunt) so the same person always sounds the same. Cooldowns per member and a cap per hour keep it rare.
-- All guarded with pcall: if a field is not what the notes say, the module logs it once and stays quiet.
local core = require("lw.core")
local cfg = core.cfg
local B = { last = {}, stamps = {}, warned = {} }

-- state -> temperament -> lines. Short, dry, vanilla-leaning.
B.lines = {
    limb = {
        stoic   = { "Gone. Well. I have another.", "It was only an arm.", "Keep moving. I will manage.", "Do not look at it. I have not." },
        dry     = { "That was attached a moment ago.", "Funny. It did not hurt until I saw it.", "I liked that one. It was the good one.", "Careless of me." },
        anxious = { "My arm. Where is my arm?", "No. No, no. Is it still there?", "Somebody tell me that is not mine.", "I cannot feel it. Why can I not feel it?" },
        blunt   = { "Lost a limb. Fine. Bandage me.", "Take it out of their hide.", "Patch me up. I am not done.", "That one cost me." },
    },
    hurt = {
        stoic   = { "I am hurt. I can stand.", "Bleeding. Not dying. Yet.", "It will keep." },
        dry     = { "I feel a draught where there should be blood.", "I have been better. Once or twice.", "Is it normal to see the floor this close?" },
        anxious = { "I am bleeding. Why am I bleeding so much?", "Please. Somebody. Bandages.", "I do not want to die here." },
        blunt   = { "Need a medic. Now.", "Bleeding out. Do something.", "I am no use like this. Patch me." },
    },
    crippled = {
        stoic   = { "I can crawl. That will do.", "Leave me a wall to lean on.", "The legs are done. The rest is not." },
        dry     = { "I would walk, but they have other plans.", "My legs have left the company.", "Carry me or leave me. I am not particular." },
        anxious = { "I cannot stand. I cannot stand!", "Do not leave me here.", "My legs. Why will they not move?" },
        blunt   = { "Legs are gone. Carry me.", "Cannot walk. Fix it.", "Do not leave me. Fix me." },
    },
}
B.order = { "stoic", "dry", "anxious", "blunt" }

function B.temperament(c)
    local name = core.nameOf(c)
    local h = 0
    for i = 1, #name do h = (h * 31 + name:byte(i)) % 100003 end
    return B.order[h % #B.order + 1]
end

local function overBudget(now)
    local fresh = {}
    for _, t in ipairs(B.stamps) do if now - t < 1 then fresh[#fresh + 1] = t end end
    B.stamps = fresh
    if #fresh >= (cfg.bodyPerHour or 3) then return true end
    return false
end

local function canSpeak(c)
    local ok, res = pcall(function()
        if c.canSpeakNormally and not c:canSpeakNormally() then return false end
        local d = c.dialogue
        if d and ((tonumber(d.speechTextTimer) or 0) > 0 or (d.sayMsg and d.sayMsg ~= "")) then return false end   -- already talking
        return true
    end)
    return (not ok) or res          -- if the fields are missing, do not block
end

function B.speak(c, state)
    if not canSpeak(c) then return false end
    local now = core.hours()
    local key = core.nameOf(c) .. state
    local cd = (state == "limb") and 0.2 or (cfg.bodyCooldownHours or 2)
    if B.last[key] and now - B.last[key] < cd then return false end
    if overBudget(now) then return false end
    local pool = B.lines[state][B.temperament(c)]
    local text = pool[math.random(#pool)]
    B.last[key] = now
    B.stamps[#B.stamps + 1] = now
    local ok, err = pcall(function() c:say(text) end)
    if not ok then
        if not B.warned.say then B.warned.say = true; core.log("body: c:say failed:", tostring(err)) end
        return false
    end
    core.debug("body", state, core.nameOf(c), "->", text)
    return true
end

local function ownerOf(medical)
    local ok, c = pcall(function() return medical.me end)
    if ok and c and core.isPlayer(c) then return c end
end

local function check(c, med)
    local ok, maxBlood = pcall(function() return med:getMaxBlood() end)
    local blood = med.blood
    if med.dead or med.unconcious then return end
    if med.crippled then B.speak(c, "crippled"); return end
    if ok and type(maxBlood) == "number" and maxBlood > 0 and type(blood) == "number"
       and blood / maxBlood < (cfg.bodyBloodFraction or 0.45) then
        B.speak(c, "hurt")
    end
end

function B.poll()
    for _, c in ipairs(core.playerCharacters()) do
        local ok, err = pcall(function()
            local med = c:getMedical()
            if med then check(c, med) end
        end)
        if not ok and not B.warned.poll then B.warned.poll = true; core.log("body: poll failed:", tostring(err)) end
    end
end

function B.register(eventIds)
    eventIds[#eventIds + 1] = Events.on("MedicalSystem::amputate", function(self)
        local c = ownerOf(self)
        if c then pcall(B.speak, c, "limb") end
    end)
    core.addTask("lw_body", cfg.bodyPollHours or 0.1, B.poll)
end

return B
