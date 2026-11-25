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

## Kindness remembered (suggested by Lodred on the Workshop, 2026-11-25)
**When any of this ships: reply to Lodred in the Workshop comments ("@Lodred") and credit the idea in the change note.**
The other half of rivals: rivals come back to settle a score, the people the squad saved come back to settle a debt. Same machinery
(a few remembered people at a time, a name and a story, town talk, the Atlas, History and Recap), the other way round.

**The deed (what gets remembered).** Two kinds, both moments Kenshi already has:
- **Freed:** shackles or a cage someone else put them in, opened by the squad (Meitou 0.23 `IHeldStateObserver`: held to free, a squad
  member at the lock or the cage). Where (Rebirth, a slaver camp, a police cell, a bandit hideout) and who held them.
- **Saved:** patched up while dying (unconscious and bleeding out, or blood near the end) by a squad member's first aid (`AidUseWatch`),
  or carried to a bed. Not someone the squad itself knocked down: mending your own damage earns nothing.
What's kept per person, at the moment of the deed: their name and record, faction, `LastingId` (0.23: the same person across saves and
unloads), the place, the day, which squad member did it, and a snapshot of their skills (to measure growth later).

**Not every one becomes a story (Rebirth starts free dozens).** Like rivals, only a few are remembered at once (say 6), and the most
telling ones win the slot: someone dying when found, someone held a long time, a named character, someone freed alone rather than in a
crowd. The rest still count: a mass escape is one deed the world talks about ("someone broke a dozen slaves out of Rebirth"), and the
squad's tally of freed and saved feeds titles (the Unchainers, the Field Surgeons).

**The reunion (the heart of it).** When a remembered person is near the squad, alive, not hostile and not fighting, they walk up and say
exactly what happened, so the player doesn't have to remember:
- "Leaf? You broke my shackles at Rebirth, 35 days ago. I couldn't lift a pick then. Look at me now."
- "You're the one who stitched me up outside Squin. I'd bled half out on the sand."
- **Never the game's day number** ("on day 12", "day 4": the player's own calendar). How long ago is fine, from how long it really was,
  as a person would put it: "35 days ago", "a few days back", "a month or so".
- Growth from the snapshot, in plain words by how much: "twice as strong", "learned to hold a blade", "made sergeant". What became of
  them: joined the Anti-Slavers, opened a stall in the Hub, went back to the Holy Nation, still drifting.
- Escaped together (a Rebirth start, the squad were slaves too): "We ran from Rebirth together, you and I."
- In their faction's voice; silent factions stay silent (a freed animal or a skeleton just follows a while).

**What they give back** (one, rarely, sized by what they can afford, like the rivals' prosthetics):
- **Word:** a tip that matters to TWR's other systems: an outlaw camp's place, bounty hunters on the squad's trail, a rival recovering
  nearby, a town that's turned on you.
- **A gift:** food, cats, a keepsake-style token ("a carved cat from Rebirth's quarry"), from the poor; a good item from the wealthy.
- **A hand:** if a fight breaks out near them, they join on the squad's side for that fight.
- **A place:** rarely, someone saved twice or owed a great deal asks to join the squad (as a rival beaten three times does).

**The world remembers it too.**
- Town talk where they live ("A woman at Spring says your lot pulled her out of Rebirth").
- The Atlas lists "People who owe you", next to the rivals.
- History and Recap note the deed and the reunion.
- Factions: a slave freed who reaches Anti-Slaver ground warms Tinfist's people; the Holy Nation thinks a freed mine slave stolen
  property; slavers already treat it as a crime.
- Healing a Holy Nation paladin is Okran's mercy to them; healing a Shek is a debt of honour.

**Endings, good and bad:** a debtor who dies is noted ("Ruka, whom you freed at Rebirth, was killed by Dust Bandits near Squin"), and a
keepsake-style item may reach the squad. One caught and enslaved again can be freed again, and the second time binds them for good.

**They don't hunt the squad down.** Unlike a rival, someone who owes you stays where life took them. Each has a home ground (the town
or region they settled in, from where they are on their own after the deed) and a radius; only when the squad comes inside it do they
notice and walk over. Meeting one is the luck of the road, not a pursuit.

**Agreed (2026-11-25):** six remembered at once; at most one reunion every few days; pay-offs start with word and gifts (a hand in a
fight and joining the squad after a play test); the squad's own escape from a Rebirth start counts ("we ran together").

**Settings:** "Kindness remembered" (on), and how often reunions happen. Meitou 0.23+ (the cage notices and lasting ids).

## PZA's bar games (MeitouMinigames, released; ideas to raise with PZA)
No conflicts (no blows, no crimes, its own save data). To make them fit:
- **Their side:** call `WastelandApi.Note` / `Report` by reflection for big moments: a big pot, a long hot chain, a busted Looter's Rights
  raid, beating Squin's arm-wrestling champion.
- **A small read API:** ledger totals (hands, staked, paid, biggest pot, best chain, raids) and each character's arm-wrestling record.
  TWR could then give titles ("Card Sharp", "Cleaned Out", "Squin's Grip") and bar gossip about the squad's luck.
- **Ours:** leave a dealer or challenger out of bar talk and criers while a game runs (they'd tag them).

## PZA's Moonscript (Lua packs, released)
Unlocks reach, not new engine features (its HUD, camera, scan and item calls sit on Meitou hooks TWR already uses from C#).
- **A read-only "twr" capability** (`IPackExtensionHost.RegisterPackCapability`, found by reflection, no dependency): heat, rivals,
  hunts, titles, recent news. Players and modders could write a heat meter, rival compass, hunt strip or news ticker in Lua.
- **"/twr" terminal commands** for play tests (heat, rivals, crews, outposts) instead of F10 rows.

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

## Meitou 0.21 and 0.22 (ideas, not scheduled: usage is low, 2026-11-18)
- **Recruit a beaten rival** (0.21 `PopulationSystem.JoinPlayerSquad`): a rival beaten often enough (say three times, or spared when down)
  offers to join instead of coming back again. Their grudge becomes their backstory, their record starts with what the squad did to them.
  Coordinate with Mazal (Rivalry Core), who wanted to shape this.
- **Prices by reputation** (0.21 `ITradePriceFilter`): a town that has the squad marked as wanted or hunted charges more; one the squad
  has helped (contracts, alms) gives a little off; a feared or despised title (Narko for the Holy Nation) makes its traders gouge or refuse.
  Built on the heat and title reactions we already have.
- **Your outpost remembered** (0.21 `PlayerTown.ModTags`, `SetSquadDormant`): the first concrete step of "Your base in the world" above:
  towns talk about the squad's base, raiders and rivals know where it is and can come to the door instead of only finding you on the road.
- **Squad roles, fully on their own** (0.21 `SetSquadAutonomy`, `OpenLootOrder` from a menu row): a squad given a role (guard, patrol,
  wander) runs as an independent squad, and can still be outfitted from a right-click row. Polish for the Squad roles feature.
- **Task bars** (0.22 `CharacterCold.ModTaskBar`, the bar the game shows while lockpicking):
  - **Writing a keepsake:** "Remembering Hollin..." over the mourner before the keepsake appears. Only **after the fight they died in is
    over** (the keepsake already waits for that): a bar in the middle of the same fight would read strangely.
  - **Ask for news:** "Listening..." for a moment before the answer.
  - Not wanted: a bar for bounty crews approaching or for a theft about to be missed (both give too much away).
- **Endorsed by the launcher** (0.22.1 lists the Workshop ports Meitou endorses): ask to be listed once TWR has some ratings and no open
  crash reports.
- Done from this list: the world-ready signal (0.21 `IWorldReady`, build az): squads a save left out are placed on the first tick.

## Meitou 0.23 / 0.23.1 follow-ups (2026-11-25, from the 0.23.0 release notes and the 0.23 modding docs)
0.23 is the first release with the unified AI chooser and real order queues, feature parity with the base game, Linux and macOS builds,
and the full set of hooks TWR has been preparing for. Small and soon first (suggested to start with: the Linux/macOS log script and "Ask for news" in the conversation window):
- **Bug reports on Linux and macOS.** "Save logs for a bug report.bat" is Windows-only; ship a `save-logs.sh` beside it that collects the
  same files (Meitou's log, TWR's settings, the play-test logs) from the Linux and macOS paths.
- **"Ask for news" as a reply in the conversation window** (`AddDialogueReplies`, 0.23): "Heard any news?" offered when talking to a
  townsperson, beside the right-click row. Simpler than the `IDialogueChoiceFilter` route noted below.
- **Sounds** (`world.PostSound`): a short call when a lookout spots hostiles, a bell for a town crier, a sting when a rival is sighted.
- **The game's own fixes toggles:** 0.23 adds optional fixes (raid progress, guard body passing, caged idles...). Re-test the play-test
  oddities against them (bodies not cleared by Remove Intruder jobs; the Hive Attack counted as an outpost's defence) and note which
  toggle covers what.
- **Rescued allies:** NPCs now carry downed allies to beds. A beaten foe carried off by their mates is a natural rival birth ("dragged
  off by their own"), and the squad's knockout credit must not count a carried foe as dying.
- **Measure TWR's cost** with Meitou's built-in benchmarks (the launcher has them now) on 0.23's faster frame: the look every second,
  the news, crier and lookout passes, and the panel publishing. Stagger anything that shows up.
- **Squad roles vs 0.23's own autonomy:** 0.23 adds per-squad AI options, autonomous outpost guards and "player squads on AI packages".
  Check where TWR's Squad roles (built on 0.19 hooks) now duplicate the game's, and step aside where the game does it better.
- **Settings:** Meitou's Settings window now has mod search and filters. TWR's own Settings tab (U) could become a shortcut to it.
Bigger, worth it:
- **Outposts that feel lived in** (test features): 0.23 has wandering traders visiting outposts, shoppers at counters, guards on duty
  and raids that ransack houses. Outpost couriers can ride on the game's own traders instead of spawning ours; outpost history can record
  a ransacked house and a guard who held the gate.
- **World states** (`AddWorldState`): TWR could claim a few states the vanilla dialogue already checks ("player killed X") so the
  game's own lines react to what TWR tracks, or new ones other mods (Dustlife, Realpolitik-style overhauls) can read.
- **The data phase** (`IModDataPhase`, 0.23): add records at load time from the player's actual load order, like RadiantQuests does.
  Town criers and gossip for modded factions and towns; titles for modded bosses; keepsakes for modded races. Today TWR's data is
  compiled offline from the base game only.
- **Translations:** Meitou 0.23 runs in all nine of Kenshi's languages. Translate TWR's interface (the U window, settings, notices and
  F10 rows) first; the dialogue is far larger and comes later, if at all.
- **The launcher's replacement list:** the launcher now suggests Meitou replacements and compatibility patches for RE_Kenshi mods and can
  subscribe to them. Ask to have TWR listed where it fits (and as endorsed, see "Endorsed by the launcher" above).
- **Shown-text, effects and glow hooks** (0.23): small touches, e.g. a faint glow on a keepsake, a named outlaw's name tinted on screen.

## New org mods (2026-11-25: Kenshi Races, Bathhouse, Way of the Fist, Husbandry, Wildlife)
None clash with TWR. Worth doing, or borrowing the idea of (never the code):
- Per-mod platoon tags: Kenshi Races writes `Platoon.ModTags[modId]`; TWR's rivals and crews use the single shared `Platoon.ModTag`
  (Bathhouse does too). Move to `ModTags` by reflection where the client has it, keeping `ModTag` as the fallback, so no other mod can
  overwrite our tag and lose a rival after a load. Needs a world test (a rival found again after a load by its ModTags key).
- Race recruits (Kenshi Races, Beep Tofuson) join an ordinary player squad, so the squad record, keepsakes and titles already cover them.
  Later, if the races catch on (BeatTheCount plans to use them): a few lines that notice one in the squad, only where lore fits.
- Bathhouse customers (Drifters, traders, bounty hunters, Shek duelists) are held by that mod: check in a play test that lookouts and
  "Ask for news" leave them alone, and that a bounty-hunter customer isn't taken for one of our crews.
- Kenshi Races' "introducer at the bar, for a fee" has the same shape as Kindness remembered (a person with a home ground you go to);
  its docs/depending-on-kenshi-races.md, with a skill for dependent mods, is a good pattern for our MODDER_API.md.
- Way of the Fist: gauntlets fight as Martial Arts. If titles ever count unarmed kills, gauntlet kills should count too.
- Wildlife's birds are client-side only (not world characters): nothing for lookouts or rivals to react to.

## Promised to other modders (built, API version 6: ships in the next build)
- **`LeaveAlone` by lasting id** (RivalryCore#25): another mod's named leader stays off the rival list even when renamed. API version 6.
- **`StrikableAfter(outlaw)` and a "not now" answer** (RivalryCore#59 decision 17, #126): when an outlaw may be struck (known to the
  squad, and 7 game days since they rose or a successor took over), at most once per 30 game days, and "not now" while wounded, dead,
  jailed, the player's, or in a story under way. Dustlife's bridge asks TWR instead of guessing.

## Meitou 0.22.2 hooks for the fundamentals (2026-11-24; in the 0.22.2 nightlies, not in 0.22.1)
Each behind a check of the client, so 0.22.1 and older play as before. Most useful first:
1. **Despawn veto** (`IRemovalVeto.Keep`: `PlatoonDespawned`, `PlatoonOrphaned`): keep our crews', reprisal parties' and hunting rivals'
   squads from being taken out off screen, instead of noticing and placing them again (Presence, `Pursuit.Make`). That re-placing has
   caused more bugs than anything else (crews mixed up after a load, killed crews coming back, rivals sitting still). Do it once a
   release has it and a play test can watch a crew across a long walk and a load.
2. **Squad notices** (`IPlatoonObserver`: Removed with `Despawned`, `LostFight` and the winner, `NoMembers`; Loaded, Unloaded).
   **Done for bounty crews in build bk** (`PlatoonFates.cs`): a crew the engine took out is placed again, one beaten far off by someone
   else is not the squad's win. Still to do: outlaw reprisal parties (`OutlawCamps`; they carry no tag of ours yet, found by position: tag them first), raid parties (`Raids`), hunting rivals.
3. **Death notices** (`ICharacterDeathObserver`: cause, site, killer). **Done in build bk** (`DeathNotices.cs`): the Fallen, their grave
   and keepsake say how they died (bled out, shot dead, executed, starved), and a killer the hit feed missed is named. Still to do: rivals
   and named outlaws who die off screen (credit only when the squad's blow came first).
4. **Done in bm** (`PanelLines.cs`). **Character panel lines** (`AddCharacterPanelLines`): each character's titles, kills and fights won in the game's own character panel,
   without opening U.
5. **Done in bm** (`PanelLines.cs`). **Item tooltip lines** (`AddItemTooltipLines`, up to 8 one-line rows): on a keepsake, "Fell on day 41 at Squin. Avenged by Leaf.";
   on a warrant, which town signed it. Adds to the full story in the description, doesn't replace it.
6. **Character lifecycle observers** (merged 2026-11-24, VlcekM/MeitouClient#1370 by PZA; in the next nightly). **Started** (`LifecycleWatch.cs`,
   after bk): knockouts, limbs, cages, joins and named removals in the play-test log; a joiner is first seen on joining. The rules below are next:
   - `ILimbObserver` (severed, crushed, prosthetic fitted): the exact moment a limb is lost, for "lost a limb and kept going" (now read from
     History lines) and a rival's "you took my arm".
   - `IKnockoutObserver` (out, waking, coma): when a downed foe gets back up, for judging a fight over (CLAUDE.md: downed foes get up).
   - `IHeldStateObserver` (cage, bed, restraint in and out, with the fixture): prisoner cases from the engine's own word, not a cage scan;
     may also show what Meitou #1271 (captives vanishing on load) does to them.
   - `ICharacterRemovedObserver` (a character leaving the table, still readable): forget a tracked handle at once, before it is reused.
   - `IRecruitObserver`: when someone joins or leaves the squad, for "served N days" and the record's first sighting.
   - `IDialogueChoiceFilter`: "Ask for news" as a reply in the game's own dialogue window, not only the right-click row.
Also noted, not planned: item names (#1332, not in the docs yet), shown text (#1354), placing characters and real bounties (#1283),
off-screen fight rules. Done earlier: lasting ids (rivals, `LastingActors`), stalled crews attack through the order queue (bj).

## Hooks coming after Meitou 0.22.1 (the MeitouClient wiki's "Code mod hooks", 2026-11-19; not in 0.22.1, so nightlies or the next release)
Not scheduled (usage is low). Best first:
1. **Lasting ids** (`world.LastingIds.GetOrGive(character)`, `Find(id)`): a 64-bit id per character for life, across saves. Replaces the
   name checks and the reused-handle workarounds (rivals, keepsake owners, crews' members); recruiting a rival needs it. Do this first:
   it removes a class of bugs rather than adding a feature.
2. **Deaths** (`ICharacterDeathObserver.Died`, loaded or off screen, with cause, site, killer and faction): "killed by the squad" vs
   "died" for rivals, outlaws and the Fallen without guessing from the last blow seen; off-screen deaths of rivals and outlaws.
3. **Crimes** (`ICrimeObserver.CrimeNoted`: pending, witnessed, booked, lapsed, expired, cleared): heat from the game's own crimes and
   bounties; a paid-off bounty cools the town.
4. **Key actions** (`c.AddKeyAction`): the Squad record key without the Harmony patch (`RecordHotkey.cs`), rebindable.
5. **Sounds** (`world.PostSound`): a lookout's call, a crew arriving.
6. **Status effects** (`c.AddStatusEffect`, saved, shown on the State line): "Grieving" after a death, "Old wound" on a returning rival.
Not for TWR now: hit and experience filters, skills, weather, world states (Dustlife's), building actions, NPC jobs and goals.

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
