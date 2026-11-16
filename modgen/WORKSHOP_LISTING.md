Draft Steam Workshop description for The Wasteland Remembers (formerly Living Wasteland; Meitou build only). Steam markup; paste as is. Revisit after the first play
test (the "early test" line, numbers, the button's place). Kept here so it is not lost; not published.
The title is free to change on the Workshop (only the title: the mod folders and ids stay LivingWasteland*, which keeps settings and saves).
A short Discord teaser is at the bottom of this file.
Build the release with `python make_release.py <Kenshi>` (data built with --release, so the Play Test Squad starts are left out; DLL without sources; README and the bug-report log saver).

```
[h1]The Wasteland Remembers[/h1]
[i]Towns talk, outlaws react, your squad keeps its own story.[/i]

The Wasteland Remembers is meant as [b]vanilla-plus[/b]: the Kenshi you know, with a world that notices you. No new super weapons, no new races, no rewritten factions. Just more life, more memory and more consequences, written to fit the lore.

[b]Nearly every part of this mod can be switched off.[/b] Open the [b]Squad record[/b] window (press [b]U[/b], or the button near the top right) and go to its [b]Settings[/b] tab: features have On/Off buttons (search them by typing, hover over one to see what it does), and a single slider sets how much of the added talk you hear, from all of it down to none. One click on [b]Record only[/b] turns everything off except your squad's record. Changes apply at once and are kept between sessions. (The same settings are also under Esc > Settings > Mods.)

[b]Early version.[/b] It has been play tested, but by few people so far: expect rough edges. [b]Feedback and bug reports are very welcome[/b]: tell me what you liked, what felt off and what broke (see [b]Reporting a bug[/b] below). Every report helps.

[h2]Your squad's story[/h2]
[list]
[*][b]Squad record window[/b]: each character's kills (by faction), knockouts, limbs taken and lost, damage, fights won and last stands, distance walked, ridden and sailed, food, crafting, and time spent on every kind of work.
[*][b]Titles[/b] earned from real deeds: the Hard Hand, Bonepicker, the Headsman, Field Medic, the Walker... and named kills (kill the Holy Lord Phoenix and you become [b]Narko[/b]).
[*][b]Factions react to your titles[/b] by their own nature: some admire, some fear, some despise and will not hire you. An admiring barman may now and then give you something on the house.
[*][b]History[/b] of your squad, and [b]the Fallen[/b]: who died, where, who killed them, whether they were avenged, and an epitaph for each. [b]Save as text[/b] writes your squad's whole story to a file to keep or share.
[*][b]Notices[/b]: a small message floats up when one of your people gains a skill level, and when a faction's standing with you rises (green) or falls (red).
[*][b]Memorials[/b]: your people mourn the fallen by name, remember where they fell, and remember the town that took their arm.
[/list]

[h2]A world that talks[/h2]
[list]
[*]Thousands of new lines for townsfolk, bosses and factions vanilla barely voices, checked against the lore. Nomads with their own voice; cannibals who speak the way the game's own cannibals do.
[*][b]Bar talk[/b]: patrons talk among themselves, and once your squad is famous, they tell tales about it.
[*][b]Town criers[/b]: in towns, a local now and then calls out the latest news from the roads, the real news of your world (wars, marches, outlaws, bounties, your squad's deeds), or their faction's proclamations when there is none.
[*][b]Lookouts[/b]: when hostiles come near, your people call them out by name ("Dust Bandits on the ridge!"), and a squadmate answers. An enemy army on the march gets a warning of its own.
[*][b]Broken marches[/b]: beat back a nation's army on its way to take a town, and your squad's History remembers it.
[*][b]A squad that grieves[/b]: for a few days after a death, the banter turns quiet, and the victories turn angry.
[*][b]Rumours about what actually happened[/b]: who you killed, which outlaw took a beating, which town is at war, where an outlaw's camp is.
[*][b]The Atlas[/b]: a tab listing the places you know and the people talked about, filled by the gossip your squad really overhears. Hidden bosses stay a blur until the rumours add up. It shows who holds each town now, where you last saw someone, and for every faction you have met: how they stand with you (with the Empire's and the Holy Nation's own titles for you, from Obedient to General or from Heathen to The Devil), who they are at war with, and which of your titles they admire, fear or despise.
[/list]

[h2]New with Meitou 0.19[/h2]
[list]
[*][b]Faces in the Atlas[/b]: every person's page shows their portrait, drawn by the game itself. Hidden bosses keep their faces hidden until you know who they are.
[*][b]Squad roles[/b]: give any of your squads a role from the game's own NPC routines (guard your base, guard it day and night, patrol nearby, rest at home, wander from town to town) and a home building where they sleep, or have one squad [b]escort[/b] another, following it and fighting for it like hired bodyguards. Your orders always come first; the squad picks its role up again afterwards.
[/list]

[h2]Rivals[/h2]
Win a fight and leave someone alive, and they may come back for you: renamed for what your squad did to them ([i]Hollin One-Arm[/i]), stronger each time, talked about by name in town, and listed in the Atlas. Lose to one and they'll boast about it in every bar. Your first won fight can make your first rival.

[h2]Outlaws and work[/h2]
[list]
[*][b]36 named outlaws[/b] (when one falls, someone from their gang takes over a few days later under a new name, with a backstory and a bounty of their own) with contracts, bounties that grow while they raid, and pay that rises with the distance you travel. Every contract you finish makes the faction that posted it think a little better of you, up to friendly (never allied: they won't die for you). Outlaws who react: hurt their people and they lie low, lick their wounds, or send men after you.
[*][b]Quests and errands[/b] from townsfolk that pay for the long road, with a sensible limit on open jobs.
[*][b]Alms and offerings[/b]: some townsfolk ask for a few cats: alms for the Empire's poor, an offering for Okran's temple, a donation to the Anti-Slavers' cause. Giving warms their faction toward you (up to friendly), and a generous squad earns a name for it.
[*][b]Town heat[/b]: towns remember your crimes, guards warn you, and it fades with time. Push it too far and a town may hire bounty hunters.
[*]Optional, off by default: [b]moving outlaw camps[/b], [b]bounty hunters[/b], [b]outlaw reprisals[/b], and [b]harassment raids[/b] (small factions harass big ones; nothing changes hands).
[*][b]Trophies and keepsakes[/b] worn in a new neck slot: an outlaw's token, a fallen friend's keepsake, a faction's charm.
[/list]

[h2]Requirements and install[/h2]
[list]
[*][b]MeitouClient 0.17.3 or later (0.19 recommended: faces and squad roles need it).[/b] One mod: subscribe, then tick [b]LivingWasteland[/b] (the mod's working name) in the Meitou launcher, below your other mods.
[*]Nearly every feature has its own switch in the Settings tab, town heat and outlaw contracts included (the named outlaws in the world and the cannibals' speech are always on).
[*]New game recommended. Your record starts from zero in an existing save and is kept in your saves from then on.
[/list]

[h2]Reporting a bug[/h2]
[list]
[*]Quit Meitou [b]before starting it again[/b] (the log is only kept until the next start).
[*]Run [b]Save logs for a bug report.bat[/b] (in the download's folder, or grab [b]log.txt[/b] from %LOCALAPPDATA%\Meitou\versions\<your version>). It puts one zip on your Desktop.
[*]Post it in the comments or on Discord with a few words on what happened and a screenshot if you have one. The log's first lines say which build you have.
[/list]

[h2]Known in this version[/h2]
[list]
[*]The trophy neck slot has no label yet (Meitou's slot API has no label field).
[*]Moving outlaw camps, bounty hunters, outlaw reprisals and harassment raids are still being tested and are [b]off by default[/b] (Settings tab).
[*]The squad record starts from zero in an existing save.
[/list]

[h2]Plays well with[/h2]
[list]
[*][b]Recap[/b] ("Previously on your adventure"): adds what only this mod knows to its story, from titles earned and squadmates avenged to bounty hunters, reprisals and raids; without Recap it all stays in the History tab.
[*][b]Quest Log & Town Map[/b] (a quest journal and town plans): it finds our 36 named outlaws and their bounties on its own and lists them with the rest, with where they roam and where to turn them in.
[*][b]Guild & Quests[/b] (adventurers' guilds, a quest board, ranks F to S): your guild rank-ups, the seals on your charter and every ten guild jobs go into your squad's History and Recap, and earn squad titles: the Guild Hands, the Seal-Bearers, the Guild's Own.
[*][b]Raid Commander[/b] (raids on your base that wait, gather, send a herald and lay siege): a raid you beat off, or one that overruns you, goes into History and Recap; beat off three and your squad becomes the Gatekeepers.
[*][b]Execution[/b] (finishing off the downed): every finishing blow counts toward your squad member's record and the Headsman title.
[*][b]Brutality[/b] (directional cuts, severed limbs, cleaves): every limb it takes counts for the one who cut it, two at once included, toward Limb-Taker and the Carver; your squad remembers the town where it lost one.
[*][b]Meitou Transport[/b] (ride your animals, sail a skiff): rides and voyages are counted on their own instead of as walking, with titles for them (the Rider, the Boatman); the Nomads respect a Rider.
[*][b]Kenshi Ultimate Overhaul[/b] (world politics and wars): it runs raids and ambushes of its own, so ours step aside when it is installed; nothing doubles up. Its armies march for real: your lookouts know an army from a patrol, and a march your squad breaks goes into History and Recap.
[*][b]Landmark Banners[/b] (banners by bars, shops, doctors and police stations): the Atlas page of each town you've been to lists what it has, from the banners' own reading of the town.
[*][b]TimeLight Map[/b] (a live map in a corner): both sit top right by default. Put the map in another corner (console: topcam corner bottom-right) or move the Squad record button (Settings > Squad record button position).
[/list]

[h2]Made with generative AI[/h2]
Made largely with Claude Code (generative AI).

[h2]Credits and thanks[/h2]
[list]
[*][b]VlcekM and the MeitouClient contributors[/b], for the engine, its documentation and its mod support, and the Meitou Discord for answers and bug reports.
[*][b]BeatTheCount[/b], for Recap (and for adding its moments API when asked), Quest Log & Town Map, Raid Commander, Guild & Quests and Kenshi Ultimate Overhaul: reading how they work taught a lot about modding Meitou.
[*][b]Jame[/b] (Execution), [b]mattresspad[/b] (Meitou Transport) and [b]dxseba[/b] (Brutality), whose mods ours was made to fit.
[*]Ideas, not code: [b]Walnuts[/b]' Level Up Notifications inspired the notices, [b]Azamat97El[/b]'s Social lives and Live Streets the town criers and alms, and the RE_Kenshi [b]Squad Autonomy[/b] plugin the squad roles.
[*]The [b]Kenshi Wiki[/b] editors and [b]kenshimap.com[/b], for the lore and the places.
[*][b]Lo-Fi Games[/b], for Kenshi.
[/list]
No code from other mods is included; mods named here were read to learn from and to make ours fit alongside them.
```

Discord teaser (paste as is; Discord markdown; under Discord's 2,000-character limit):

```
**The Wasteland Remembers** (Meitou mod, coming soon to the Workshop)
Vanilla-plus: no new races or super weapons, just a world that notices what you do.

- **Your squad's story:** press U for a record of every character (kills, limbs, fights, distance, work), titles earned from real deeds, a History, and the Fallen with an epitaph each
- **Towns that talk:** bar talk, rumours of what *actually* happened, town criers calling out the real news of your world
- **36 named outlaws:** contracts, bounties that grow while they raid, outlaws who react to you
- **A squad that feels it:** lookouts call out enemies, grief after a death (by name), anger after the win
- **The Atlas:** places and people you have heard about, filled by gossip you overhear, with faces on 0.19
- **Squad roles (0.19):** guard, patrol, wander or escort your other squad, on the new autonomy feature
- Every feature can be switched off in game

**Made to fit other Meitou mods:**
- **Recap:** titles, last stands, avenged squadmates, broken marches and more appear in its "Previously on"
- **KUO:** its armies march for real; your lookouts know an army from a patrol, and a march you break goes into your History. Our small raids step aside so nothing doubles up
- **Guild & Quests:** guild ranks, seals and jobs go into your History and earn squad titles
- **Raid Commander:** raids on your base you beat off are remembered; three and you are the Gatekeepers
- **Execution / Brutality:** every finishing blow and severed limb counts for whoever did it
- **Meitou Transport:** rides and voyages count apart from walking, with their own titles
- **Quest Log:** finds our outlaws and bounties by itself

Feedback very welcome once it is up!

Huge shoutout to VlcekM, sashkaa and everyone building Meitou, to BeatTheCount, whose mods taught me how this all fits together, and to Jame, mattresspad and dxseba for the mods this one was made to play nice with.
```
