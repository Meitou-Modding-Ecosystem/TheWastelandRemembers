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
- **Hostile rival bounty hunters:** crews that go after the same bounties as the squad, and sometimes after the squad.

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
- Neck slot label: needs a label field on Meitou's `ModSlot` (ask the Meitou devs).
- Squad role "Patrol the town": needs a town home, which Meitou's Home command can't give yet.
- Recap moments for the newer systems.
