# The Wasteland Remembers: future plans

## Rivals - BUILT in 20261115t, released in 20261115ai
Next for rivals: a rival who beats the squad takes something (a trophy from the fallen), rivals recruit (bigger squad each return), and a
squad title for settling three grudges.

From the Meitou Discord after build ai:
- **Rivals recruit:** each return that they survive brings a bigger squad, and later returns can bring a hired Bounty Crews squad that rides
  with them (reuses the hunters' crew squad and Pursuit's follow and attack).
- **Rivals cheat death** (a setting: Never / Sometimes / Always): a rival who came hunting and goes down may slip away before the killing
  blow and swear vengeance, so the system gets its chance to shine. Only once they've been told (a fresh rival can still be killed). How to
  move a loaded, downed character off safely needs testing first; no smoke effect is available, so the escape is told in a line.
- **Hunt pacing (with the Meitou Worldbuilding group, rule "who owns a character or a hunt"):** bounty crews set out from the town that
  hired them and travel (distance is the player's warning time, with word when they set out) instead of appearing 2.5 to 3.5 km away; a
  few days' grace at the start of a game for crews and reprisals; ordinary crews give up beyond a set distance, while big grudges (a rival
  who has beaten the squad, a huge bounty) cross the whole map; all under one hunt at a time across mods. Keep: rivals as they are, so an
  early, weak rival (a hungry bandit) can hunt a new squad and it stays manageable.
- **Hostile rival bounty hunters:** crews that go after the same bounties as the squad, and sometimes after the squad.

## Your base in the world (the next big theme)
Long saves spend mid and late game at the base, where today the mod has little to say. Same idea as rivals and heat: the world notices your
base, and your base remembers. Others in the Meitou Worldbuilding group make things happen to bases (Rivalry Core's raids, burglars (Rivalry Core), Dustlife's economy); TWR is how the world and your own people talk about it and remember it.

**Near term (mostly reuses what exists):**
- **A reputation:** once an outpost grows, nearby towns gossip about it (size, walls, guards, wealth, from Meitou's player-town reports); it
  gets an Atlas page like a real town, and its founding goes into History and Recap.
- **A memory:** a base section in the squad record: raids withstood and who attacked, who fell defending it (memorials), titles for the
  place and its defenders ("the Unbreached", "Wall-Keeper"), Recap moments for sieges.
- **Your own people talk:** settlers on base life (no beds, hunger, a good harvest, the new wall, a fallen friend's empty bunk); your guards
  call out who is coming by faction and name, as the town lookouts do.

**Bigger (the "next level" ideas; all four agreed, 2026-11-16; start with reputation and memory above, which they build on):**
- **Fame draws challengers (late-game threats that come from your story, not a difficulty slider):** once the squad is strong and known,
  duellists come for title holders, ambitious outlaws want your name, a beaten rival's kin or second takes up the grudge (successors for
  rivals). Scaled by notoriety, so a quiet base stays quiet.
- **Prisoners and justice:** someone in your cages is news. Their faction sends a negotiator or a ransom offer, their gang plans a rescue,
  the town with their bounty wants them delivered; executing them (with the Execution mod) travels as gossip and heat. A captured outlaw
  in your cell turns their reprisal into a jailbreak.
- **Your town has a character:** what you do there (slaves, executions, prisoners, drugs grown, who you shelter: hivers, skeletons, shek)
  gives the place a reputation, like titles for the squad. Factions react: a Holy Nation inquisitor calls, the Anti-Slavers come to free
  your slaves, the UC's tax man remembers whether you paid last time.
- **Flags (with the PZA LEAF framework):** the flags you fly over your base (faction emblems on poles or building nodes) count toward
  its character: fly the Holy Nation's and the Holy Nation warms to you while the Shek and the Anti-Slavers cool; a blank or a player
  banner stays neutral. Visitors and town talk notice it ("they fly the Phoenix's colours out there now").
- **The world comes to you:** petitioners and messengers at your gate bring the outside in: refugees from a war (the Bast Front), survivors
  of a fallen town asking shelter, someone wanting an outlaw near you dealt with, a bounty contract delivered by courier. Mid-game content
  that doesn't need you to leave, and a reason to go out when you want to.
- **A chronicle with a base chapter:** founding, first wall, first raid, first death, anniversaries ("a year since Rivergate was
  founded"), feeding the shareable chronicle idea below.

**Via the shared work:** grudges come home (an outlaw or rival raids your outpost; heat cools your neighbours) through Rivalry Core's raid
behaviour, not our own; burglars (Rivalry Core) get spotted, gossiped about and added to heat and history; visitors who remember you pair with
the bed-rent hook and Dustlife's economy.

## Bast Front support (with the Meitou Worldbuilding group; the Dustlife brief "The Bast Front")
A living UC vs Holy Nation war in Bast (and the Shek vs Holy Nation front at Stack), simulated by Dustlife, with town states set through an
engine hook (RivalryCore issue #28). Our part:
- **War news in:** Dustlife's events ("Okran's Shield fell to the UC", a siege began) become criers, bar talk, rumours near the front,
  Atlas entries and Recap moments (issue #23). Step aside for Dustlife's war the way we do for KUO's.
- **Lines that assume who holds a town:** about 129 mentions of Okran's Shield in 8 content files, some assuming the Holy Nation holds it
  ("Valtena keeps Okran's Shield like a fist round a coin"), plus its Atlas entry. Gate them on the new world states once #28 exists (plain
  game world states, so dialogue conditions can read them with no code).
- **Outlaws at the front:** Hask Morrow, Ilsa Hearthmourn, Hodd Crake and Aldric Roane live in Bast: news of them raiding army supply, or
  caught between armies.
- **War starts:** the Bast Front game starts opt into early danger, so our early-game grace (crews, reprisals, outlaw grace) is off for them.

## Outlaws early in a game (from play test, build t)
- DONE in 20261115ae: the Border Zone holds three named outlaws (was ten); a downed named outlaw doesn't bleed out in the first three days.
- Still to do: named outlaws should not stand in a town on day 1 (Isolde Marr patrolling the Hub). Instead: now and then, and never in the
  first couple of days, one turns up drinking in a bar in their region, a chance meeting.

## Meitou 0.20 (to explore)
- Stat modifiers: a returning rival's "stronger" as a modifier that can be taken off, instead of a permanent stat change.
- AddMedicalMeter: an old wound a rival carries from the fight with the squad.
- Bone anchors and meshes: tokens shown on the body (see below).
- AddSkill: a squad skill earned from the record.

## Tokens you can see (idea, from play test)
Trophies and tokens sit in the mod's neck slot, which Meitou doesn't draw (and Kenshi has no necklace models). To show them on the body
they would have to be worn in a slot the game draws (a headband or armband).

## A shareable chronicle (idea)
"Save as text" as a laid-out page with titles, the fallen, rivals beaten and pictures from key moments.

Ideas agreed on for later releases, newest first. Each says what the player would notice, then how.

## Outlaw successors (suggested on the Meitou Discord after the early release) - BUILT in 20261114p, not yet play tested
A named outlaw's death doesn't empty the roads. Some days after a named outlaw dies or is jailed, one of their gang takes over under a new name, on the same ground, with a fresh (smaller) bounty, and a new contract appears.
The town talk marks the change ("Peel's dead, and his second has taken the coat"), and the Atlas lists the successor beside the one before.
How: a pool of successor names and epithets per outlaw. The helper notices the unique's death or capture, waits a few game days, and spawns the successor's squad in the outlaw's zones (Meitou spawn API).
The successor's state (alive, bounty, name) is saved in LwCore. Contracts and gossip are keyed to "the current holder" of each outlaw slot. Each slot can turn over a limited number of times, so the roads don't fill up.

Variants (also from the Discord): instead of the gang's second, a **rival outlaw** may move in on the empty ground; and the world may
send **bounty hunters after a living outlaw** whose price has grown big (the hunters' squad walks to the outlaw's ground and fights it; the
squad may hear "someone collected on Quillon"). Both reuse pieces we have: the successor spawn, and the bounty hunters system (today
aimed only at the player).

## Outlaw activities that change behaviour
Today the activity (raiding, lying low, gathering men, licking wounds) only changes the talk and the bounty. Next, it changes what the outlaw's squad does:
- **raiding:** roams wider and goes for caravans;
- **lying low:** keeps out of sight, away from roads and towns;
- **gathering men:** extra henchmen join;
- **licking wounds:** fewer men, and they stay put.

How: per-activity AI packages and squad size, switched by the helper when the stretch changes. It ties into the moving camps (now a test setting), which would then be on by default.

## Smaller
- **Tokens worth keeping:** today the 36 outlaw tokens are all "<Name>'s Token" with one shared description and one base item. Give each
  outlaw a token of its own (name, look and a description that tells something of them: Peel's skinning hook, Quillon's knotted rope),
  and the successors theirs. Make the faction charms (Shek Battle Knot, Broken Shackle Charm, Okran's Token...) as distinct.
  **Keepsake of the Fallen:** written for the one it belonged to: their name, a short synopsis of their life with the squad (where they
  joined, titles, best deeds, how and where they fell, who avenged them) and their record (kills, fights, days with the squad), from the
  squad record at the moment of death. Needs a per-item description (a new item per fallen, or a mod tag the description is read from).
  With the necklace models from the group (bone meshes), tokens could also show on the body.
- **Prefix internal names** (suggested in the Meitou Worldbuilding group): dialogue ids, world states, AI packages and squad templates get an
  `LW_` prefix, so mods and tools that look records up by name can't mix ours with theirs. Player-visible names (items, factions) stay as
  they are. Low priority: Kenshi tells records apart by file id, so nothing clashes today. Check saves still load after a rename.
- Neck slot label: needs a label field on Meitou's `ModSlot` (ask the Meitou devs).
- Squad role "Patrol the town": needs a town home, which Meitou's Home command can't give yet.
- Recap moments for the newer systems.
