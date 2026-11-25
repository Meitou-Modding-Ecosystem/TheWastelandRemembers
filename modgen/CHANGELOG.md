# The Wasteland Remembers: changelog

## Next build (not built yet)
- **Lighter on the game** (after a player found TWR laggy on a big mod list): town criers do far less work when nobody is close
  enough to hear them; the Atlas builds its pages about three times faster and reuses a person's page until something on it changes;
  the Squad record panels share one look at the squad instead of each scanning everyone. Nothing plays differently.
- **New setting "Lighter checks"** (off): existing Atlas pages and the Squad record panels refresh half as often, for big mod lists.
  What the squad sees, hears and does is still noticed at the usual rate.

## Build 20261125bo (Steam hotfix, built from bm's source, not from bn)
bm plus the four performance changes listed under "Next build" above ("Lighter on the game" and "Lighter checks"), nothing else.

## Build 20261125bn (beta, not on Steam)
- **Rivals come back with what their people can afford.** A rival who lost a leg used to return on a shoddy Economy Leg whoever they were,
  and a lost arm stayed lost. Now their faction decides: starving and hungry bandits, Dust Bandits and the like still limp back on a shoddy
  Economy Leg and stay one-armed; the nations, the hives, the ninjas and the bigger gangs come back on a standard Economy Leg and with a
  lost arm replaced too; Tech Hunters, traders, nobles, the guilds and slavers on a high-grade Skeleton leg and arm. Word on the road says
  which ("on a fine skeleton leg").
- **Wealthy rivals talk about their new limbs**, in their own people's voice, when they close in: a Tech Hunter's ancient work from World's
  End, the Traders Guild putting it on your account, a slaver who sold three of your sort to pay for it (each names the arm or leg). A rival whose arm was replaced no
  longer says "One arm's enough for you lot."
- **The neck slot works beside NUE.** NUE's inventory claims the game's neck slot for itself and hides it, which left TWR without one
  (trophies and keepsakes couldn't be worn). On Meitou 0.23 TWR now adds its neck as a second neck section in its usual place.
- **For other mods (API version 6):** `LeaveAloneId` leaves another mod's named character alone by Meitou 0.23's lasting id, so a rename
  doesn't undo it (asked for on RivalryCore#25); `StrikableAfter` and `StrikeAnswer` tell a war mod when it may strike a named outlaw's
  camp: 7 game days after they rose, once per 30 game days, never while wounded, unknown to the squad, or with their men out after it
  (RivalryCore#59).
- **For other mods (API version 5):** `Titles(character)` gives one squad member's titles with their names, so a mod like PZA's Service
  Record can show them beside its own numbers.

## Build 20261125bm (Steam release)
Build bl plus, on Meitou 0.23 (setting "Squad record in the game's panels", on):
- **Titles and record in the character panel.** Under a squad member's stats: the title they are known by (all of them on hover) and
  their kills and fights won.
- **Keepsakes and warrants on hover.** A keepsake's tooltip says how and where its owner fell and whether they were avenged; a bounty
  hunter's warrant, which town signed it.

## Build 20261125bl (Steam release candidate, not uploaded)
- **Meitou 0.23 support.** 0.23.0 refused the mod (its version range stopped below 0.23); the range now runs up to 0.24. Checked on 0.23.0:
  builds, loads with every setting, every engine call the DLL makes is there, and every test passes. Not play tested on 0.23 yet.
- **What bk did on the 0.22.2 nightlies now works on 0.23.0**, the first release with Meitou's AI rewrite: stalled bounty crews attack,
  the Fallen say how they died, and a crew the game removes keeps hunting.
- **Meitou 0.23's character lifecycle notices** (MeitouClient#1370): with Collect playtest diagnostics on, the log
  notes the squad's knockouts and limbs lost, named characters leaving the world, and (with Log quest changes) anyone put in or let out
  of a cage and anyone joining or leaving the squad. Someone who joins is counted as with the squad from that moment. Older Meitou: no
  change.

## Build 20261124bk (Steam release candidate, not uploaded)
Build bj plus, on Meitou's 0.22.2 nightlies (each needs a hook the nightlies added; 0.22.1 and older play exactly as bj):
- **The Fallen say how they died.** One who bled out after the fight, starved, or was shot, executed or eaten was "Cause of death
  unknown" on the Fallen page, their grave and their keepsake. The game's own death notice now says how ("Bled out on day 3 at Squin"),
  and names a killer the squad record didn't see strike (a crossbow bolt, an execution). Kept in the save (record format 14).
- **A bounty crew the game removes is no longer "finished".** Any crew that vanished counted as beaten by your squad. The game now says
  why a squad left the world: one it took out for its own reasons is placed again and keeps hunting; one beaten on the road by someone
  else is no win of yours ("The road finished them first").

## Build 20261123bj (Steam release candidate, not uploaded)
Build bi plus:
- **Ask townsfolk for news is on by default** and no longer a test feature (Meitou 0.20.2 and later: right-click someone in a town).
- **The Hub's crier speaks**, and so do the criers of the United Cities' prosperous towns (Heft, Heng, Bark and the rest in their good
  times). Those towns belong to factions with no news lines of their own, so their criers never had anything to say; they now pass on
  what their neighbours hear (the drifters' news in the Hub, the United Cities' in the peasants' towns).
- **On Meitou's AI rewrite (the 0.22.2 nightlies), a bounty crew that stops short of you attacks again.** The order the game used to
  give was removed in the rewrite; TWR now gives the rewrite's own Assault order through the order queue. Older Meitou is unchanged.

## Build 20261123bi (Steam release candidate, not uploaded)
Build bh's code, released to the Workshop (the first upload since 20261115ai). Test features stay off by default; Collect playtest
diagnostics is off. Checked on Meitou 0.17.3, 0.22.1 and v0.22.2-nightly.20261123 (build, settings, engine references, unit and world
tests); not play tested as a build of its own.

## Build 20261122bh
From a second play test (bg):
- **Keepsake and charm icons, really this time.** The items still told the game to draw their icon from the coin's 3D model, which shows
  as nothing; with their own icon set, they now use it.
- **Captives answer in their own voice.** Ask a caged Shek, a Holy Nation zealot, a bandit, a skeleton, a ninja, a hiver or a cannibal for
  news and each refuses in their own way; a Hive Queen screams.
- **Names keep their capital in news** ("Not long ago: The Queen was taken alive", not "the Queen").
- **"Lost a limb and kept going" only when they did.** A limb lost in the hour before death no longer counts.
- **Victory and mourning lines play whenever talk is on.** At the default talk amount most of them were silently dropped; the amount now
  thins only gossip, reactions and banter.
- **The Hive envoy's F10 row says what holds it back** (her cage, the outpost, the hive's mood), so a missing envoy can be traced.

## Build 20261122bg
From a play test of bf (a new save, every test feature on):
- **Keepsakes and charms show in the bag.** They were copied from a coin record with no icon, so they were invisible. Each now has an
  icon of its own.
- **A fallen squadmate leaves the Record page at once**, not when the body is cleared away.
- **An empty shelter is enough to found an outpost.** Before, it needed a piece of furniture inside.
- **News of a captive comes from your outpost**, not from the nearest town ("News from Rebel Base").
- **Captives can't be asked for news, and nobody tells you news about themselves.** The caged Queen had told the squad she'd been taken.
- **The Western Hive envoy and the ransom find the Queen in your cage** by who she is, as the prisoner case does.
- **Day numbers agree.** Keepsakes and heard news counted a day ahead of History and the game's own clock.
- **The neck slot sits clear of the inventory** (under the weight box), and an F10 row moves the neck charms live to find their fit.
- **Play-test logs keep the whole session.** The outlaw camps wrote a line per camp every refresh, which filled the log and pushed
  every other event out of the timeline; now a camp's line is written when it changes, and the general log can no longer push out a
  feature's own rows.
- **Fiefdom (and mods like it):** a town mod's residents and guards are left out of the squad record, its fights, keepsakes and rivals.
- F10 rows with line breaks showed "?"; Rivalry Core and Recap are logged with their own version numbers.

## Build 20261121bf
Play testing made simpler:
- **With Collect playtest diagnostics on, the play-test log saves itself** every minute, one file per launch
  (Documents > The Wasteland Remembers). No Export click needed; a crash or a second launch loses at most a minute.
- **The visible neck charms have their models** (setting "Visible neck charms", off by default): a Shek charm on Kral's Mark and Holy
  Nation beads on Okran's Token, 3D models by **Mazal Tofuson**. Their fit is a first guess until a play test.
- **Save logs for a bug report.bat also collects the play-test logs** (the last 14 days) and TWR's settings, so one double click still
  gathers everything.

## Build 20261121be
**Works on Meitou's AI rewrite** (code-mod API 3, in the 0.22.2 nightlies from 21 November). On those, bounty crews, couriers and outlaw
camps could not be made at all: the engine call that picks a squad's orders changed shape. TWR now uses whichever shape the game has, from
0.17.3 to the newest nightly. On the AI rewrite, a crew that stalls short of you is no longer ordered to charge (the order is gone); it
keeps following.

From an outside review's fourth round and a full sweep (each with a test that fails on bd):
- **A squadmate's memorial no longer names a killer who wasn't one.**
- **Handing in part of what a quest or toll asks for no longer counts as the whole.** The line that takes the items now checks you have
  them all.
- **News heard from someone keeps its own time and place**, not another event's.
- **With town heat off, no bounty hunters come for heat.**
- **A new game no longer shows the last game's map marks** (rivals included).
- **Other mods passing a broken number to TWR's heat calls are refused** instead of spoiling a town's heat.

**New, all off by default** (Squad record > Settings, each marked "test"): outpost history and couriers, town handover history, prices by
reputation, recruiting a rival beaten three times, keepsake stories, prisoner cases and deals, Western Hive envoys, outlaw, Shek and covert
recovery parties, escorts for freed prisoners, visible neck charms, a native record hotkey. **Collect playtest diagnostics** (also off)
keeps a timeline you can export from History (Mark test point, Export playtest log) for bug reports.

## Build 20261119bd
From an outside review's third round (each with a test that failed on bc):
- **Squad roles work on Meitou 0.22.** Choosing a role (Guard home and the others) failed: the engine's command gained new parameters.
- **Escorts follow the same squad after a load** (Meitou with saved squad tags); an old escort setting is cleared once and chosen again.
- **Stolen goods count for the town they were taken from**, even when the thief is standing outside it.
- **Bounty crews keep their own identities across saves** (Meitou 0.20.2+), and a raid party that hadn't loaded yet comes back after a
  load instead of counting as caught.
- **Another mod's tag on a rival's squad is left alone**, and Recap no longer gets a place from the previous game.

## Build 20261119bc
From a play test of bb (lookouts and alms confirmed working):
- **A hunting rival comes for you after a load.** Since ba, a rival out of sight after loading was never steered and sat where it was,
  "hunting", for days. TWR now tags the rival's squad, and Meitou 0.20.2 and later keep the tag in the save. On older Meitou the rival
  waits until you come near.
- **A rival who lost a leg gets the iron leg whenever they're in the world.** Before, only at the moment the hunt began, so one out of
  sight then crawled after you instead.
- **Recap pictures:** our moments without a place of their own use where your squad is, so Recap can take a picture. A squadmate's
  death is no longer sent twice (Recap shows it itself).
- **World map tooltips:** rows no longer run together ("Rivalhunting your squad"); the meaningless "Last heard of here" row is gone.

## Build 20261119bb
From an outside review's second round (each tested against the old code):
- **Lookouts call out hostiles again.** They never did on Meitou: a counter started at the lowest possible number and the first call was
  always "too soon". A sleeping squadmate no longer takes the call from one who is awake.
- **Contracts, alms and raid rewards warm the faction again.** The faction was looked up by its display name where the game wanted its
  record id, so no reward ever reached it.
- **After loading another save:** a march watched in the old game is no longer credited to the squad as broken; an "Ask for news" click
  from the old game's menu can't be answered by someone in the new one; pursuers no longer inherit the old game's progress and orders.
- F10's quest and raid rows read a line written in the tick, as the others already did.
- Tests that save and load the squad record for build ba's keepsake and arena fixes.

## Build 20261118ba
Fixes from an outside review of the code (each checked against the code first; the tests fail on the old code):
- **Rivals no longer stop for the session after a load.** A rival pass in the first ticks after loading, before the squad was in view,
  threw an error and switched rivals off until the next start.
- **Two bounty crews after a load each find their own squad.** A crew left out of the save could take another crew's, and that crew was
  then placed again beside its own.
- **A rival's saved squad number no longer steers someone else's squad** after a load (squad numbers are handed out again at every load).
- **A won fight stays won** when a squadmate dies elsewhere during it.
- **Keepsakes wait for someone to keep them:** if nobody is standing when the hour runs out, the keepsake waits (up to two days), and a
  keepsake still to be given is now kept in the save.
- **Arena sport survives a save:** a named fighter struck in a bout who bleeds out after a reload still counts as sport (no heat).
- **An outlaw's successor whose gang wasn't in the save** is placed again once the spot is clear, not only on the first try.
- **Stolen goods:** takes from one town and owner within an hour count as one theft; a later one waits on its own 12 hours.
- **Ask for news:** a question asked just before a load isn't answered in the loaded game, nor after the setting is switched off.
- **F10 rows** read a line each system writes in its own tick, instead of reading the game while it runs.

## Build 20261118az
- **After loading a save (Meitou 0.21+), bounty crews and outlaw reprisal parties the save left out come back at once**, on the first
  tick with your squad in view, instead of at the next pass a few seconds in: the engine now says when a save has loaded. Older Meitou
  works as before. Not play tested.

## Build 20261118ay
- **Meitou 0.22 support.** The version range now runs up to 0.23 (0.22.0 would have refused the mod). Checked on 0.22.0 and the
  0.21.1 nightly: builds, loads with all settings, every engine call the DLL makes is there, and every test passes. Not play tested on
  0.22 yet. Nothing else changed from ax.

## Build 20261118ax
- **Keepsakes on Meitou 0.21 (bug fix).** When a squadmate died on 0.21, no keepsake came and the squad record stopped for the rest of the
  session: 0.21 changed the game's "add to inventory" call. Fixed, and the same for barmen's gifts. If nobody comes to the body within a
  game hour, the nearest squadmate still gets the keepsake. Also in hotfix 20261118ai3 for the Steam version.
- **Bounty crews that stop short now attack.** A crew on screen that gets no nearer for a while is ordered to attack (seen in play: one
  waited 375 m away for half a day). Distances are measured from the crew's people.
- **Rivals in F10:** the rivals row says how far a rival is and whether they're on the road or in the world; the log notes it hourly.
- **Ask for news (test):** the same piece isn't told again for six game hours (a day for news about your squad); people say "Nothing
  new." and the like instead. More ways to say news is about you.

## Build 20261118aw
- **Bounty crews (and rivals and outlaw reprisals) keep closing in off screen.** In play they drifted away from a squad that waited for
  them; a hunter that goes the wrong way off screen is now walked on towards you at walking pace. The log notes where each crew is once a
  game hour.
- **Arena wins get their victory line again** (av had taken it away with the fight count): a Proving Grounds bout counts as a fight.

## Build 20261118av
- **News heard once:** news another mod has already written into History (Proving Grounds notes your arena wins and reports them too) no
  longer gets a second identical line.
- **Ask for news (test):** townsfolk tell you other news first. When the only news is something your squad did, they say so ("Everyone's
  on about your lot.") instead of telling you about yourselves as a stranger would.
- **Map marks (test):** news marks no longer repeat the town's name over its own label, and long texts in tooltips sit on a row of their
  own, where they used to run into the row's label.

## Build 20261117au
- **Proving Grounds (BeatTheCount's arena, 0.2.0 and later):** a blow traded in one of its bouts, in your own arena or Scratch's, is
  sport. Your record still keeps the knockouts, but no town's heat rises, nobody becomes a rival, no outlaw counts as struck and no raid
  party as broken. Scratch's arena fighters are never made rivals or renamed. Nothing changes without Proving Grounds.

## Build 20261117at
- **For other mods (API version 4):** `Outlaws()` lists TWR's named outlaws (status, successor, mood, bounty, home regions, camp),
  `OutlawAt(x, z)` says whose ground a point is on, `HideoutStruck(...)` lets another mod's war band strike an outlaw's camp (the outlaw lies
  low, and no reprisal comes after your squad), and `TownNode(town)` gives a town's stable id for shared frameworks. Nothing changes in play
  unless another mod uses them.

## Build 20261117as
- **Towns miss stolen goods** (Meitou 0.21 and later; setting "Towns miss stolen goods", on). Goods your people take from a town's shops,
  homes, storage and machines raise that town's heat, more for more valuable goods. Seen taking them: at once, on top of the crime
  itself. Unseen: the town misses them half a day later, and heat rises then; anything worth 2000 cats or more goes into History ("Squin is
  missing a katana and wants to know who took it."). Your own faction's goods, ownerless goods and anything under 100 cats don't count.

## Build 20261117ar
- **Meitou 0.21 support.** Meitou 0.21.0 refused the mod (its version range stopped below 0.21); the range now runs up to 0.22. Checked
  on 0.21.0: builds, loads with all settings, and every test passes. Not play tested on 0.21 yet.
- Behind the scenes: TWR's link to Rivalry Core is now tested against the real Rivalry Core (its published 0.6 beta), not only a stand-in.

## Build 20261117aq
- **A bounty crew or reprisal party killed just before a save stays dead.** Meitou's newest nightly keeps killed squads in the save; TWR
  now reads one as beaten instead of sending the same men after you again.
- **Three new test features for Meitou v0.20.2-nightly.20261117 and later, all off by default** (Esc > Settings > Mod settings):
  - *Marks on the world map:* rivals still about, outlaw camps the Atlas knows, places of news your squad heard this week.
  - *Ask townsfolk for news:* right-click someone in a town and ask; they tell you the biggest news that has reached them.
  - *Factions' change of heart in History:* when a faction turns against your squad or warms to it, History says why.
- Behind the scenes: tests now make real bounty crews with the game's own population system.

## Build 20261116ap
- **For other mods (API version 3):** `SendHunters(town, reason)` has a town send a bounty crew now, for another mod's reason (a bank's
  unpaid loan, a broken contract), and `LeaveAlone(name, faction, source)` marks a character another mod has named (a war leader) so
  it is never made one of your rivals. Nothing changes in play unless another mod uses them.

## Build 20261116ao
- **One hunting party at a time with Rivalry Core.** When Rivalry Core 0.6 or later is installed (The Ashen Hand and other mods build on
  it), our bounty crews, outlaw reprisal parties and returning rivals share its rule: only one hostile party is after you at once, whichever
  mod sends it. A crew or rival whose turn it isn't waits and sets out once the other party has been beaten or given up. Without Rivalry
  Core nothing changes.

## Build 20261116an
- **Right-click a keepsake** (Meitou 0.20.2 and later): whoever holds it turns it over and says a line for the fallen ("For Hollin.",
  "Bad Pete is still out there, Hollin. Not for long."), and you're told how they died and whether they've been avenged. The keepsake's
  story is written again as it stands now: who holds it, and who avenged them if someone has since.
- **Right-click a bounty hunter's warrant** (Meitou 0.20.2 and later): it says which town hired the crew, whether that town still wants
  you, and whether another of its crews is on the road. Warrants dropped before this build don't say who signed them; they list the
  towns that want you instead.
- On older Meitou both items behave as before.

## Build 20261116am
- **Pursuers speak.** A bounty crew, an outlaw's reprisal party or a rival come back says a line over their head the first time they close
  in to attack: "Squin sends its regards.", "Aldous Peel hasn't forgotten you.", and from a rival, words that fit what your squad did to
  them ("Remember me? You took my arm.", "Walked a long way on this iron leg to find you."), or a boast if they've beaten you before.
- News from other mods reads better: "Okran's Shield has fallen to the United Cities", "A caravan of the Traders Guild went missing".
- Behind the scenes: tests that run the mod's own systems in small synthetic game worlds against the real Meitou files (rivals getting
  away once and surviving a save, someone else on a rival's old handle, keepsake stories written into their records, news told once and
  heard near the place, a pursuer's line said once). They run on every Meitou release check.

## Build 20261116al
- **Keepsakes written for the fallen.** The keepsake a mourner takes from a fallen squadmate is now theirs by name ("Keepsake of
  Hollin") and tells their story: what it is, how long they were with the squad, their titles, named kills, who they mostly fought, last
  stands and lost limbs, how and where they fell and who avenged them, and their record (kills, fights won, knockouts, km walked).
  Written from the squad record at the moment of death, and kept with the save. A dozen keepsakes are written at once; after that the
  oldest story gives way to the newest.
- **Charms from the lore.** The Shek charm is now Kral's Mark (red cord round a sliver of horn shaped like Kral's), and every faction
  charm has a description of its own people: Tinfist's Anti-Slavers, Moll's Flotsam Ninjas, World's End's Tech Hunters, the Swampers'
  blood spider fang.

## Build 20261116ak
- **News from other mods** (Meitou Worldbuilding issue #23). Another mod can report what happens in the world: a town falls, a siege
  begins, a caravan is lost, a warlord rises (`WastelandApi.Report`, API version 2). Townsfolk, bar patrons and criers near the place talk
  about it, and when your squad comes within earshot it goes into History, the Atlas's new News section and Recap (weighty news is also
  posted as a message). The same news from two mods is told once. New setting "News from other mods" (on), and an F10 row.
- Play-test builds: F10 "test news" reports a siege at a town you type, to try it without another mod.

## Build 20261115aj
- For other mod makers: a small public API (`WastelandApi`, read by reflection, no hard dependency). Read and add town heat, see rivals
  and who is hunting the player, write to History, send Recap moments. Documented in "For modders - API.md" in the download.

## Build 20261115ai (release)
- Bounty hunters, outlaw reprisals and enemies gloating are on by default (play tested over several sessions).
- A recovering rival doesn't bleed out awake either (in play, a one-armed rival bled out on their feet).

## Build 20261115ah
- A fight is over only when everyone your squad fought is dead, down, or more than 50 m off. One who gets back up and comes at you again
  is the same fight. Other hostiles about (a busy road, a high population setting) don't count, and a stand-off ends after about a
  minute. Rivals, victory lines and the squad record's fight count all use this.
- Fighting alone? Your fighter now has a few victory lines of their own ("Just me, then, and it was enough."). The lines spoken to the
  rest of the squad ("Take their boots") wait for someone within 50 m to hear them.
- A beaten bounty hunters' crew drops a Bounty Hunter's Warrant with your squad's name on it, a trophy of the fight.
- A named outlaw killed by someone else, once you've heard of them: "Word on the road: Orsa Quillon is dead, and not by your hand."
- The log says why no rival was made after a won fight (no one notable left alive, the chance, or the last one too recent).

## Build 20261115ag
**From the build af play test** (a rival made, then dead within hours, not by the squad)
- A rival licking their wounds doesn't bleed out where they lie.
- A rival who dies at someone else's hands gets a quiet line ("is dead, and not by your hand") and no Recap moment. One your squad kills
  gets the Recap moment.
- Becoming a rival ("crawled away from the fight") is a stronger Recap moment.
- With no rival about (none yet, or the last one dead or jailed), the next won fight that leaves someone alive always makes one. Before,
  only your very first was sure; after that it was 30% and two days apart.

## Build 20261115af
**From the build ae play test** (no rival in a whole save; a beaten crew still "attacking")
- Rivals are made at once. Build ae waited until you'd walked away before making the rival, and in play every survivor was lost in the
  meantime: some unloaded once you were far off, and an unloaded character can't be renamed. The rest got back up, fought on and were
  killed. Now the survivor becomes the rival when the fight ends. You're told when you've been gone two hours and 250 m, as before. Until
  then:
  - they don't bleed out;
  - if they're killed soon after, the next survivor of the same fight takes the grudge;
  - if you carry them off or they're jailed, it's let go quietly.
- A crew or reprisal party with nobody left standing counts as beaten: "The bounty hunters Squin hired are finished." Before, one
  survivor lying in the dirt kept it "attacking".

## Build 20261115ae
- Rivals: the timer no longer resets mid-fight. A downed rival who got back up and was knocked down again counted as a new fight each
  time ("got away again", return pushed back, stronger again). Now only a rival who came hunting you can lose and get away, and one fight
  counts once. A rival's first return comes sooner (one to two days).
- A game's first three days: a downed named outlaw doesn't bleed out (in both play tests, named outlaws were dead by day 2). A killing
  blow still kills.
- Fewer named outlaws in the Border Zone (ten shared it; now three), so the Hub's first days aren't crowded with bosses.
- Town criers in the Hub: the Trade Ninjas have a crier of their own now, and every faction's proclamation comes up a little more often.
- Crews and reprisal parties: one shared check for "beaten or just not in the save", started again for each loaded game; a crew that
  runs out of time just goes home instead of being "wiped out".
- The log gets one summary line per game day (kills, Atlas, criers, victory lines, rivals, crews...), and a game shows a warning if the
  data and code builds don't match (a stale mod folder).

## Build 20261115ad
- Pursuers attack. A bounty crew, reprisal party or hunting rival loaded near you follows you (the bodyguards' follow goal, given once,
  since a running contract can't be changed). Within 60 m each of them gets the attack order vanilla dialogue uses.
- Rivals: no more "got away again" from fights they weren't in. The game reuses an unloaded rival's slot, so a respawning crew member
  counted as them; the rival's name is now checked too. Rival messages are written to the log.
- A beaten crew no longer comes back. Only a crew never seen since the game loaded is placed again (the save leaves those out).
- F10: the bounty hunters row says whether a crew is on the road, following you or attacking. The squad record row shows the build.

## Build 20261115z
- Crews, reprisal parties and hunting rivals keep walking pace all the way to you. Close to you, Meitou only nudged them along (about 70 m
  an hour in play).

## Build 20261115y
- Bounty crews, outlaw reprisal parties and successors' gangs survive a save and reload. Meitou's save leaves out a squad that has never
  been near you, so after a load it was missing (and counted as beaten). Now it is placed again where it was. "Wiped out" and "cut down"
  only when they really died.

## Build 20261115x
- Works with Meitou 0.20 (the mod's version range was "<0.20.0", so 0.20 refused it). Checked: builds and loads on 0.20.0, and the
  engine rules our moving squads depend on are unchanged.

## Build 20261115w
- Bounty crews, outlaw reprisals, hunting rivals and roaming successors move for real now. Build v's fix left them standing still: Meitou
  never moves a kept squad that has no job. They walk on a short-range patrol job aimed at you. If a squad still doesn't move, the mod
  walks it toward you at walking pace until it loads near you.

## Build 20261115v
**From the build u play test**
- Bounty hunters, outlaw reprisals, rivals hunting you and roaming successors now actually walk to their target. Since the fix in build r
  they were walking to their home town instead.
- Rivals: a rival far away could be counted in a fight it was nowhere near ("got away again" out of nowhere), which also pushed its return
  back. Fixed.
- Rivals: become one after 2 game hours with your squad at least 250 m away. One who lost a leg comes back on an Economy Leg (a real
  prosthetic), never crawling. Atlas wording: "Once a Rebel Fighter.", "Licking their wounds." Names vary per game and never repeat
  among your rivals (60 names).
- Atlas: a town you walk into counts as visited. The 12 hireable outlaws have proper descriptions. Isolde Marr is a woman in new games.
- The Squad record button sits beside Feedback at the top by default. The bounty hunters F10 row shows distance and time since sent.

## Build 20261115u
- Rivals: a survivor becomes a rival once your squad has walked away and they look like living (not bleeding out); one who dies first is
  let go. Their Recap moments come with the place, so Recap can take a picture. "Once a Dust Bandit." on their Atlas page.
- Outlaw successors now actually start (their check never ran in build t).
- Bounties start at their base price in a new game (they used to start grown); Recap only mentions an outlaw's price once they are in
  your Atlas.
- The welcome note is in gold.

## Build 20261115t
**New: rivals**
- Win a fight and leave someone alive, and they may come back for you. The survivor (the actual person your squad beat) is renamed for
  what happened: Hollin One-Arm, Tesk Crutch, Mara the Unbowed. They swear to come back.
- Your first won fight that leaves someone notable alive always makes one. After that it happens now and then, at most three at a time.
- Two to four days later they hunt your squad, a little stronger each time. Beat them and live, and they get away again. Lose, and they
  boast about it in every bar. Kill them or put them in chains to end it.
- Townsfolk near a rival talk about them by name. The Atlas has a Rivals section with each one's story. History and Recap note every
  turn. New setting **Rivals** (on) and an F10 row.

**New players**
- The Record tab opens with a short "what to expect" note in a new game, which goes away once your story gets going.

## Build 20261114s
**Plays well with other mods**
- **Landmark Banners:** with it installed, the Atlas page of each town you've been to lists what's there (a bar, a doctor, a police
  station, shops, a thieves' guild).
- **TimeLight Map:** works alongside. Both sit top right by default, so move the map (console: `topcam corner bottom-right`) or the
  Squad record button (Settings > Squad record button position).

## Build 20261114r
**New: outlaw successors** (an idea from the Meitou Discord)
- When a named outlaw dies or is jailed, someone from their gang takes over the same ground three game days later, under their own name.
  Examples: Peel's apprentice Merrin Stitch, Quillon's niece Benna, and the skeleton Tuppence built to carry his loot.
- Each of the 36 successors has a short backstory on their Atlas page.
- Their bounty starts at 60% of the outlaw's and grows while their gang raids, up to double.
- 24 new contracts, from the same givers as the outlaw's, offered once the successor has taken over.
- Townsfolk near the ground talk about the takeover, and it shows up in Recap.
- New setting **Outlaw successors** (on), and an F10 row that shows where each one stands.

**Fixes**
- Squads the mod places in the world (successors, and the test features: bounty hunters, reprisals, raids, moving camps) no longer
  throw an error when they load near you.
- Town criers, lookouts and bar patrons skip sleeping characters. From Meitou 0.19.1 sleepers can't talk, so lines were being lost.
- The Workshop title now says [Meitou].

## Build 20261114n (first release)
- Early release on the Steam Workshop, as one mod.
