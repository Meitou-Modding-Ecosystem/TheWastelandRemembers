# The Wasteland Remembers: changelog

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
