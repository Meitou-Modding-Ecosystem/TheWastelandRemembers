# Living Wasteland on Meitou: port status

Meitou (https://github.com/VlcekM/MeitouClient) is an open-source reimplementation of the Kenshi engine. Its owner decided it does NOT load RE_Kenshi plugins, so KenshiLua, and BFrizz's Extra Extensions are all ignored there. Reading notes: `plugin/MEITOU_REFERENCE_NOTES.md`.

**Nothing in this port has been run in Meitou yet.** The data side builds and checks clean here. The C# helper **compiles** (2026-11-10) against the Meitou 0.17.2 win-x64 release files, with no errors or warnings, and the build copies `LivingWastelandMeitou.dll` into the mod folder.

## What is done

| Piece | State |
|---|---|
| `build_mod.py --target meitou` | Works. Same content packs, second output. The default target (Kenshi) is unchanged. |
| `meitou_target.py` | Rewrites every BFrizz reference into Meitou dialogue condition and effect codes (100 variables, 504 conditions, 321 sets, 146 adds, 18 take-item effects) and drops the Kenshi-only records (variables, LUA_SCRIPT). Checked: the offer for the Teshin Ro contract now carries a condition code 12043 (equals 0) and the job cap code 12000 (less than 5); a report line carries set 14025 = 99 (paid) and add 16000 = -1 (one job less). |
| `validate.py` | Accepts mod codes from 1000 up only when building for Meitou. |
| `meitou/LivingWastelandMeitou/` | C# code mod: reads `lw_codes.json`, registers the codes, keeps the variables per world, saves them in the mod's save data, hashes them. **Compiled** against Meitou 0.17.2 (16 KB DLL, committed beside the source so the installer ships it). Never run in a game. |
| `install_meitou.bat` | Builds and installs the Meitou target. Installs the same folders as `install_all.bat`: install ONE target at a time. |

## Codes

| Kind | Code | Meaning |
|---|---|---|
| condition | 12000 + n | variable n compared with the value (compare by 1 `<`, 2 `>`, else `==`) |
| effect | 14000 + n | set variable n to the value |
| effect | 16000 + n | add the value (may be negative) to variable n |
| effect | 18000 + m | take `value` units of item m from the player character |

`lw_codes.json` (written next to the C# project by the build) maps n and m to variable names and item record ids.

## Not done yet

* **Compiling the C# mod.** Needs the .NET 10 SDK and the game's `Meitou.Modding.dll`, `Meitou.Simulation.dll`, `Meitou.Core.dll`, `Meitou.Data.dll` (from a Meitou release folder): `dotnet build -c Release -p:MeitouDir="<that folder>"`, then copy `LivingWastelandMeitou.dll` into the installed `mods/LivingWastelandMeitou` folder. Expect small API fixes (names I could not confirm: `Characters.Cold(slot).Inventory`, `Inventory.All()`, `ItemInstance.Record`).
* **The Lua features.** Not ported. Several are redundant in Meitou: `calm` (the engine already has a quiet period at the first look), the tracker (Meitou has a Quest Log mod and its own settings and save systems). Still to port if wanted: the job-cap message, `body`, `chatter`, `drops`, `recruits`.
* **46 dialogues use features Meitou lists as not built** (DC_IS_OUTNUMBERED 19, DC_WITHIN_TOWN_WALLS 30, EV_BETRAYAL 33, EV_GET_UP_UNNECCESSARY_FIGHT 9 uses). They load, but those conditions compute as "no" there.
* **Unknown:** whether Meitou keeps unknown record types through its merge (we drop ours, so this only matters for new ones), and how its dialogue loader treats a mod condition code with no handler (it computes "no" per its docs, so offers simply do not appear without the DLL).
* **Region-aware offers and distance pay** (the reason for moving at all). Needs the C# mod to read town and region positions; design after the first compile.

## The 30-minute compatibility test

1. Install the Meitou launcher from its releases page; point it at your Kenshi folder; let it install the game.
2. Run `install_meitou.bat` (or copy `build/*` after `python build_mod.py "<Kenshi>" --target meitou`). You do not need the DLL for this test.
3. In the Meitou launcher tick LivingWasteland, Cannibals, Outlaws, Heat (leave KenshiLua and BFrizz disabled). Start a new game from one of our starts.
4. Check: does it load; do our 8 starts show; are barks and the Gate Folk camp there; do barmen and shopkeepers trade; do bounty offers appear (they should NOT appear until the code mod's DLL is installed, since their conditions have no handler); do trade menus still work.
5. Send me the game's log and what you saw.

## What I need from you

* The result of the test above, and the log.
* The Meitou game folder's file list (just the `Meitou.*.dll` names and the version, e.g. from `dir`) so I can match the API, or a zip of that folder's `Meitou.*.dll` files if you are happy to share them.
* If you build the C# project yourself: the compiler errors, if any.
* Whether you want the Lua features ported (and which), or dropped.

## Where to find the Meitou game folder (for the API check)

1. Press the Windows key plus R, type `%LocalAppData%\Meitou\versions` and press Enter. (If that folder does not exist, open the Meitou launcher, go to Settings and read "Install folder"; the game versions are in a `versions` folder inside it.)
2. Open the newest version folder (named like `0.17.1`). Inside are the game's files, including `Meitou.Modding.dll`, `Meitou.Simulation.dll`, `Meitou.Core.dll` and `Meitou.Data.dll`.
3. Send me a screenshot of that folder's file list (or just the version folder's name). The DLLs themselves are only needed if you build the C# mod yourself.

## Lua features: decisions (2026-11-09)

| Feature | Decision |
|---|---|
| `calm`, `chatter` | Drop for Meitou (the engine already does both). Kept for Kenshi. |
| `tracker` | Port the job-cap message and journal later, after seeing whether Meitou's Quest Log shows our quests. |
| `body` | Done as data (`content/body_voices.json`, 20 dialogues on the lost-limb, being-healed and get-up events). Lua off. |
| `drops` | Rhinobot already drops his CPU in vanilla. Cannibal Chief and Grand Wizard now drop a cannibal arm as data. Lua off. |
| `domepapers` | Done as data: the four dome heads carry their papers. Lua off. |
| `recruits`, `notoriety` | Later, if wanted. |
| `heat`, `hunters`, `nemesis`, `outlaws`, `captives`, `base`, `ideology` | Drop. |

## Checked against the wiki page "Writing code mods" (2026-11-09)
The wiki (github.com/VlcekM/MeitouClient/wiki/Writing-code-mods, read from the wiki git repo) matches `docs/modding.md`. Points confirmed for our helper: the mod folder needs a `.mod` of the same name beside `meitou.json` and the DLL (the build writes one); `apiVersion` 2 (builds after the 2026-11-07 nightly); `gameVersion` added to our manifest (`>=0.17.0 <0.18.0`); the launcher asks the player to trust a code mod before it runs; save data lands in `meitou-mods/<id>.dat` inside the save folder, and **importing a save starts mods fresh** (our quest state would reset on an import); `Initialize` runs once on a background thread before any world; conditions also run for previews (ours only read); a mod with a missing DLL is refused with "X.dll is missing" in log.txt, so LivingWastelandMeitou stays unticked until the DLL exists. The `meitou-tools mod-check` command is the headless determinism test we can run once the helper builds.

## Compiling the helper (2026-11-10)
* Meitou 0.17.2 has **no separate `Meitou.Modding.dll`**: the `Meitou.Modding` namespace is inside `MeitouClient.dll`. The project references `Meitou.Modding.dll` when it exists and `MeitouClient.dll` otherwise.
* Built with the .NET 10 SDK (10.0.403) against `MeitouClient.dll`, `Meitou.Simulation.dll`, `Meitou.Core.dll`, `Meitou.Data.dll` from `Meitou-0.17.2-win-x64.zip` (the GitHub release asset). Build: `dotnet build -c Release -p:MeitouDir=<folder with those DLLs>`. Every API name the code uses resolved (`ModContext.AddDialogueCondition/AddDialogueEffect/AddSystem`, `DialogueCheck.Compare`, `DialogueAct.Effect.Value`, `World.Characters.TryResolveNext/Cold`, `Inventory.All/RemoveOne`, `ItemInstance.Record`, `IModSaveData`, `IStateHashed`, `StateHasher`, `Rng.StableHash`).
* Next check, on your machine: `meitou-tools mod-check "<a save folder>" --mod "<Kenshi>\mods\LivingWastelandMeitou"` (from the Meitou version folder) runs the save headless three times with the helper and reports PASS or the first tick that differs.

## Helper additions (2026-11-10, compiled, not run)
* **F10 row** "Living Wasteland: quests": the open jobs on one line, the full list (job and step) in log.txt.
* **log.txt** gets one line per quest step change (`Quest qc_hunt: 0 -> 1`) and per take-item effect, switchable.
* **Settings page** (Esc > Settings > Mods): open-job limit 1 to 20 (default 5), job-limit messages, quest-change logging.
* The job cap is no longer a number in the data on this target: `meitou_target.py` turns the 81 `LW open jobs < 5` checks into condition code 19000, which the helper answers from the settings slider. The Kenshi build keeps the fixed 5.
* Job-cap messages (`world.Post`) when the counter crosses the limit, both ways.
* Not built, on purpose: a region-aware job board. Guild & Quests already does local boards with reach by rank ("boards look a zone farther"), and Quest Log & Town Map shows distance and "Near Me"; no need for our own while those exist.

## Round 2 of helper work (2026-11-10, compiled, unit-tested, not run in a game)
* **Give up a job:** F10 row with a text field ("number or name"). The row queues a `GiveUpJob` command; the helper's system applies it in the tick (the row itself must not touch the world): the job's variable back to 0, the open-job counter one lower, a message posted. Meitou only; the Kenshi build still has no abandon.
* **Unit tests:** the bookkeeping moved into `LwCore.cs`, a class with no engine types. `meitou/LivingWastelandMeitou.Tests` (xunit, 20 tests) compiles it directly and needs no Meitou files: initial values, set/add before and after, save/load round trip, cap messages on crossing only, open-job rules for contracts and quests, names, fixed ordering, give-up by number and by name, the counter never below zero. All pass (`dotnet test -c Release`).
* **`meitou_check.py`:** Meitou's view of a built Meitou target, from what its source reads, without running the game. It runs at the end of every `--target meitou` build and fails the build when a mod code has no handler or a Kenshi-only record is left. First run found:
  * **15 dialogues used `no_target_races`, a field neither Kenshi's exe nor Meitou reads** (Meitou docs: "never read by the exe"). The exclusions (no robot-limb lines at skeletons, no "meat walks" at skeletons) never worked in either engine. Converted to explicit `target_races` lists, and the compiler now refuses `no_target_races`. Side effect: a non-empty target race list adds +2 to the static score (+2 more when it matches), so these 15 now win ties on their events.
  * 4 bail-desk lines use `CHOOSE_PRISONER_BAIL`, which Meitou does not run yet (the bail window). Kenshi only for now.
  * **Correction to the earlier "46 dialogues touch unbuilt features" note:** Meitou master now computes every condition 0-79 except 49 and 70 (21/22, 39, 57, 58 included), so that number is now 0 for us.

## Reading Meitou's dialogue loader in full (2026-11-10): two fixes that apply to both engines
Meitou's `DialogueDatabase` follows the original's rules (each cited to the exe in its docs), so these were bugs in the Kenshi build too:
* **Packages named "Unfinished" are never read**, and their parents are not followed. Cannibal squads list only "Cannibals --Unfinished" as their dialog leader, so in vanilla they never speak, and our 305 attachments to it (cannibal horror lines, Gate Folk talk) were dead in both engines; 52 more went to a "Traders Guild Gate Guard --Unfinished--- --DISCARD--" package. The compiler now creates one live package per unfinished one ("LW live: Cannibals"), adds it beside the unfinished one on every squad template and character that lists it (14 for the cannibals), and attaches there. Effect in play: **cannibals now speak our lines** (they were silent in vanilla), which is new behaviour, not a restoration.
* **Player characters' packages:** the engine gives a player character its CHARACTER `dialogue package player`, else the player faction's `dialog default` ("player character default"), else `5369-gamedata.base`. Our squad lines listed eight "Player ..." packages by hand and missed "player character default" (characters made in the editor, the Nameless faction default), "Player flotsam" and the unique recruits' own player packages (Griffin, Bo, Crumblejon, Pia and others). A new `player:all` package token covers all 23; 102 hand lists were replaced by it. Effect in play: squad banter, body voices and arrival lines now come from every kind of squad member.
* Read by both and confirmed in our data: `speaker`, `interjection`, `repetition limit` (and `once only`), both chances, `target is type`, `locked`, `one at a time`, `monologue`, `score bonus`, the `conditions` / `effects` lists, and the 26 reference lists in `meitou_check.py`. Not read by either: DIALOGUE `for enemies` (we set it nowhere), `no target race` (fixed earlier today).

## Rumours (2026-11-10): talk about what actually happened
* **Pack `x_rumours.json`** (in LivingWastelandOutlaws, because most of its subjects are the outlaws' "killed by player" states and the base mod must not depend on Outlaws): 45 subjects, the 36 named outlaws plus nine vanilla deaths from Dialogue.mod (the Phoenix, Tengu, Esata, Longen, Lady Tsugi, Lords Inaba, Nagata, Ohta, Yoshinaga). Speakers: townsfolk of the factions that cared (the outlaw's bounty factions plus the Traders Guild), on sight, in a town, chance 12 to 14, global 48 h cooldown per line.
* **Kenshi build:** the "lasting" lines only, gated on the world state ("They still argue about who killed Teshin Ro."). Kenshi cannot tell how long ago a state turned true, so no "fresh news" lines there.
* **Meitou build:** "fresh" lines while the state has been true for under 4 or 5 game days ("They say Teshin Ro is dead. Two days, maybe three."), "lasting" lines after that. The helper polls the 45 states every 150 ticks, records the game hour each first turns true (saved, hashed, logged as `rumour: '...' is true from day N`), and answers condition codes 20000+ with the days since. A state that was already true before the mod was installed counts as old news.
* **Compiler:** a `rumours` pack key; the build now knows its target (`comp.target`) and leaves the fresh tier out of the Kenshi build. The build also refuses a dependency cycle between our mods (the first version of this pack made LivingWasteland and Outlaws depend on each other).
* **Release namespaces differ from the source:** in 0.17.2, `PopulationSystem` is `Meitou.Simulation.Population.PopulationSystem` and `IWorldStates` is `Meitou.Simulation.Factions.IWorldStates`; the source clone was older. A small metadata reader (`System.Reflection.Metadata`, nothing in the DLL is run) now checks names against the release files.
* **Two big gaps on Meitou found by `meitou_check.py`'s event report (latest master, 2026-11-10):** Meitou's simulation does not fire **EV_BAR_TALK** (5,820 of our attachments: the bar rumours, patron chatter) or **EV_I_DEFEATED_SQUAD** (1,395: grudge setters, victory lines), nor EV_WINDY, EV_MARKED_FOR_DEATH, EV_I_SEE_RAGDOLL and some slave events. Those lines load but stay silent there. `DialogueSystem.FireEvent(world, npc, target, ev)` is public. **Bar talk is now driven by the helper** (`BarTalk.cs`): every 3 game seconds, for each building a player character is inside (`world.Sight.BuildingOf`), the NPCs in that building are offered event 12 in a rotating order until one speaks; the bar then waits `Bar talk pause` (default 25 s), the speaker four times that, and a bar where nobody had a line is retried after 10 s; at most 2 bars start a line per pass. The dialogue system does the rest (packages, conditions, chance, repetition, one-at-a-time), so only patrons with bar-talk lines speak. Cooldowns are not saved (like Meitou's own weather-event pauses). If Meitou adds bar talk itself, switch ours off in the settings. "Defeated squad" is still silent.

## Notoriety, squad record and town heat (Meitou only)

* **Source of truth:** Meitou's `ICombatHitObserver` (public in 0.17.2) gets every landed blow after the engine applied it, so the helper reads the result: a kill is `VictimWasAlive` and now `Medical.Dead`; a knockout is not-incapacitated before and incapacitated now; a cut limb is the hit part now a `LimbState.Stump` (credited once per character and part). Leaders are the victim's `squad.Leader`; named kills are characters in `world.Uniques`. No Harmony patch is used.
* **Squad look** every game second: each player character's current task (`CharacterHot.Task`) counts as an activity (`Activities.Of`), plus distance walked (jumps over 2000 units ignored), times downed and limbs lost. Records are keyed by character name (a renamed character starts a new record).
* **Titles** come from `content/notoriety.json` (`titles`: id, name, scope member|squad, stat, faction, target, at). Stats: kills (optionally of one faction), knockouts, leaders, named, limbs, downed, limbs_lost, damage, distance, hours:<activity>. Squad titles sum the crew and belong to everyone in it, recruits included. `targets` (list, `|` for alternative record names) makes a named-kill title: one target is that kill (Phoenix: Narko), several need all of them dead (legendary). Named kills are matched by the CHARACTER record's name, from `world.Uniques` or, failing that, the victim's own record. A victim a player character struck and who dies later (bleeding out) is credited to the last player striker, within a game day, unless someone else struck it after the squad (2026-11-12: the last blow decides, as in Recap). `legendary: true` only sorts the catalog. Dialogues with `lw_if: [{"title": id}]` react to holders; the Kenshi build leaves them out.
* **Town heat:** `HeatMap` per town name, two parts: crime (halves every 72 game hours) and blood (every 288), capped at 40, fading only while none of the player's people is in that town. A town's own people (its owner's faction or a squad living there) knocked out add 2 crime, killed add 6 blood. A speaker outside its town (or in a sister town) feels half its faction's hottest town. `meitou_target` rewrites our DC_REPUTATION / DA_AFFECT_REPUTATION references into heat codes 19100 / 19101 (values negated, comparison flipped), so the Heat pack's tiers, bounties and pardons work unchanged on the new scale; a pardon forgives the whole faction.
* **Window:** `StatsUi` (`IUiExtension`, provisional UI API v1): a HUD button toggles a panel of labels; the text is rebuilt in the tick every 3 s (`LwDeeds.Panel`). Never seen drawn: placement and fonts are guesses.

## Squad Record (stand-alone)

* The squad record window as its own Meitou code mod, for players without Living Wasteland: `meitou/SquadRecord` (its project links the helper's `Deeds.cs`, `Notoriety.cs`, `StatsPanel.cs` and builds them with `SQUAD_RECORD_ONLY`, which leaves out heat, dialogue codes and quests). Titles come from `titles.json`, written from `content/notoriety.json` by `make_squad_record.py --zip` (output in `modgen/release/`, git-ignored).
* It turns itself off when the Living Wasteland helper's assembly is loaded (0.17.2 does not know the manifest's `incompatible` key yet, so the check is at runtime).
* Shared stats added for both: food eaten (a bite sets `MedicalState.Fed` to a hundredth of its hunger points, so a rise between looks is a bite) and items crafted (a bench's `CraftState.Done` grows; credited to `Machine.Smith`, its last operator, when that is a player character; output already there when a bench is first seen is not counted). Kinds come from the bench's craft options: weapons, armour, crossbows, robotics, items. Titles can count them (`"stat": "crafted", "kind": "weapons"`).
* Not published: wait for the in-game test of the window.

## Outlaw news and war talk (Meitou only)

* **Outlaw news** (`content/x_world_news.json`, `OutlawNews.cs`): each of the 24 outlaws in outlaws.json is always doing one thing (1 raiding, 2 lying low, 3 gathering men, 4 licking wounds), picked from the world seed, the outlaw and the current 60-game-hour stretch (offset per outlaw). Nothing is saved; the same world always gives the same news. Lines are spoken by the factions that want the outlaw dead (sightings in their towns, chance 6; bar talk, chance 12) and stop once "<name> killed by player" or the new "<name> is dead" (any cause) is true. Condition codes 21000 + i (`lw_if: {"outlaw": name, "doing": k}`). The first step of "outlaws that live"; step two (real camps that move) would use the squad route Guild & Quests shows (plugin/COMPANION_MODS.md).
* **War talk** (`WorldNews.cs`): Meitou's faction campaigns (`PopulationSystem.Wars`, `CampaignInstance.Phase` 0 marching, 1 assault, 2 ended; `Target.Site` an NPC town, `Target.PlayerTown` one of the player's). Condition codes 22000 + j per (faction, against the player or not), `lw_if: {"war": faction, "player": bool}`. 15 factions: townsfolk and bar patrons of the other big factions talk about a march, and warn the player when one heads for the player's town.
* Both are left out of the Kenshi build.

## Recap link (both mods)

* Recap 1.2 (BeatTheCount) added `Recap.RecapApi.AddMoment(caption, place, position, weight, picture, kind)` at our request: any thread, never
  throws, other mods capped at 24 moments a session and half the pages. `RecapLink.cs` calls it by reflection (works on every Meitou version
  and with Recap absent or older), filling parameters by name and type; anything unknown switches the link off with one log line.
* Sent: titles (member 70, squad 85, legendary 95), last stands (80), a town marking the player wanted (65) or hunted (75). Not sent (Recap
  tells them itself): named kills (it now also credits bleed-outs after the squad's last blow), lost limbs, fights.
* Setting "Share moments with Recap" in both mods. Tested with a stand-in RecapApi in the unit tests; not yet with the real Recap 1.2.

## .NET 11 (Meitou master 9a79fb66, 2026-11-11)

* Meitou master and nightlies moved to .NET 11 (SDK 11.0.100, net11.0) and Harmony 3.0.0-rc.5. Its docs: mods target `net11.0` for builds
  after the move, `net10.0` for releases before (0.17.3 and earlier). We do not use Harmony, so only the target framework matters.
* Until a .NET 11 release ships, keep building against the 0.17.x release files (net10.0). When it ships: install the .NET 11 SDK, rebuild
  the helper and Squad Record against that release's DLLs, and if players are split between the two, ship both builds behind a small loader
  that picks one (the Classic/Modern pattern Guild & Quests and Recap use).
* Also on master: an Interface scale setting (50-200%, height/1080 times it). `UiFrameContext` does not expose it to mods yet; our windows
  scale by height/1080 only (right at 100%).
* Roadmap: selected RE_Kenshi plugins are to be built in or ported to the code-mod API from a public wishlist (docs/formats/rekenshi.md);
  their DLLs are still never loaded.
* **2026-11-12: built for 0.17.4.** 0.17.4 is the first release on .NET 11. Both code mods now target `$(MeitouTfm)` (default `net11.0`)
  and are compiled against the 0.17.4 release DLLs with the .NET 11 SDK (11.0.100); nothing we use changed between 0.17.2 and 0.17.4.
  `gameVersion` is `>=0.17.4 <0.18.0` in both manifests (a .NET 11 DLL cannot load on the .NET 10 builds). The unit tests still run on
  .NET 10 (they compile only the pure files). Meitou keeps separate .NET 10 "legacy" packages for older Macs (0.17.5 nightly): our builds
  do not load there; a second net10.0 build behind a loader would cover them if anyone asks.

## Town names in war talk, rumours for bleed-out kills (2026-11-12)

* **War talk names the town.** `content/x_world_news.json` `word_swaps`: one WORD_SWAPS record per warring faction (`/LWWAR_HOLY/` and so
  on) with a line per town (64 real towns and villages from the game data, "(override ...)" variants folded into their town) plus a plain
  "someone's town". Meitou fills a `/NAME/` token from the best line of that record whose conditions hold (docs/game/dialogue.md "Tokens";
  more conditions score higher), so the town under attack beats the fallback. Each town line has the helper condition 24000 + k with value
  1 + the faction's place in `warTowns.factions`: true while that faction's campaign (phase 0/1) targets that town. No Harmony.
* **Rumours count the squad's bleed-out kills.** Meitou sets "<name> killed by player" only for a killing blow. On Meitou each outlaw rumour
  tier has a copy (`rm_..._rec`) that needs the state false and our record's kill instead: condition 25000 + i (the squad killed them) and
  25500 + i (days since, compared like the fresh/lasting tiers). The record keeps each named kill's game hour (save format 6).

## Outlaws on Meitou: zone spawns and moving camps (2026-11-12)

* **Meitou reads "homeless spawns" only from biome groups (BIOME_GROUP, type 95; our `zone` kind).** Our outlaws were added to terrain
  biomes (BIOMES, type 28; our `biome` kind), which Meitou uses only for drawing ground, water and foliage, so **no named outlaw ever spawned
  on Meitou** (vanilla also lists homeless spawns on 46 terrain biomes; Meitou's docs describe the original's wild population per group
  only, so the exe very likely ignores terrain-biome lists too; not a Meitou bug). Fixed on both targets: each outlaw has `zones`, and
  both builds add it to those zones' spawn lists (zones checked against kenshimap.com's camp markers).
* **Moving outlaw camps** built behind a setting (off by default): `OutlawCamps.cs`, `CampRules.cs`, design and engine findings in
  `OUTLAWS_LIVE_DESIGN.md`. Compiled against 0.17.4, 77 unit tests pass, not run in a game.

## Review pass (2026-11-12): bugs fixed

* **Named leaders' own lines were silent** (both engines): a squad leader speaks from its squad's `dialog leader` package before its own
  `dialogue package` (Meitou docs/game/dialogue.md, from the exe). Lines for a named character now also go to the leader packages of the
  squads it leads (the "is character" condition keeps others out). Three outlaws (incl. Tchik of Seven Voices, who had no package at all)
  had taunt/victory lines that could never play; a check over the built data now finds none.
* **Phantom kills:** a struck victim the helper could no longer find (its area unloaded) was credited as a kill, with blood heat. Now
  only a real death counts. Bleed-outs still count: a victim that dies later (bleeding out) goes to the last player striker within a
  game day, and a **named** (unique) victim that dies while its area is unloaded is credited too, read from the engine's unique registry
  (`world.Uniques.StateOf`). An unloaded nobody's death cannot be seen and is not counted. **The last blow decides** (the user's choice,
  matching Recap 1.2): any blow by someone else after the squad's removes the claim. Still different from Recap: we count any later death,
  Recap only blood loss. The struck list is not saved, so a bleed-out across a save and load is missed.
* **Hired mercenaries** (a squad whose AI contract has signal 12, hired to follow, or 15, hired to guard the base; the test Meitou's aggro
  uses for an employer) are on the squad's side: their blows never cancel the squad's claim, and a kill they make (or a later bleed-out
  after their last blow) is credited to the **squad as a whole** (`SquadRecord.CrewKill`: crew named kills, squad titles, rumours,
  history "Your hired blades killed X."), never to a member, so no member title comes from a merc's blow. Their kills of townsfolk heat
  the town like the squad's own.
* **Rumours on the first look:** states already true when the mod first polls a world were stamped "seen now" (fresh news for days);
  they are now old news (`@rumours started` flag, `MarkOld`). Unit test added (78 tests).
* **Activities by task name**, not number (a renumbered CharacterTask enum cannot shift the hours titles any more); numbers still match
  0.17.4.
* **Save reading:** a squad record from an unknown (newer) format no longer leaves the heat reader misaligned; a damaged section starts a
  fresh record with a log line instead of throwing.
* **Camps:** a camp's squad is pinned again each time it loads (new squad id per load).
* **Builds:** our own installed `LivingWasteland*.mod` files are never read as game data (a safety net: only the optional `--with-mods`
  flag, which no script uses, could have read them; the default reads the four base files only); install message no longer says the DLL is missing when it is there; 15 dialogues no longer list the three REMOVED
  zones (warnings 51 -> 4). Log-collector scripts: date stamp works in any Windows date format; the two mods.cfg copies no longer
  overwrite each other.
* Not checked this session: the Lua files' syntax (no Lua tool could be installed); unchanged since the Kenshi play sessions.

## Victory lines, saved bleed-out credit, Squad Record standing down (2026-11-12, compiled, unit-tested, not run)

* **Event 15 (EV_I_DEFEATED_SQUAD) driven by the helper** (Meitou never fires it; "I just won a battle, whole enemy squad is down. Target
  is the person I just defeated"). Our content uses it both ways: the player's squad after a win (squad banter, recruits, grudges;
  ~85) and NPCs who beat the player (boss/outlaw taunts, grudge setters; ~160). `Notoriety.cs`:
  * **Your squad** (setting "Victory lines: your squad", on): when a won fight ends (the record's fight end), one standing fighter (a
    different one by tick) gets event 15 with the last enemy a player character struck as the target.
  * **Enemies** (setting "Victory lines: enemies (test)", OFF): the moment everyone of the squad who fought is down, the nearest standing,
    in-combat, non-hired NPC of up to two squads (within 2500) gets event 15 with the nearest downed member as the target. **This also wakes
    vanilla's own event-15 lines**: law packages arrest a beaten squad ("Law enforcement defeats squad (arrest them)", economy.md), and
    the like. More faithful to Kenshi, but a gameplay change, hence off.
  * `meitou_check.py` counts event 15 as helper-fired (7,439 attachments on helper-fired events with bar talk).
* **Bleed-out credit survives a save** for named victims: they are saved by unique record id (character ids do not survive a load) and
  found again through `world.Uniques` after loading (dead -> credited; back in the world -> normal tracking resumes).
* **Squad Record stands down on the first tick** if the helper's assembly is loaded (its Initialize check could run before the helper
  loads): records nothing, button hidden (`RecordHost.Off`).
* **World units are 0.1 m** (Meitou docs/game/ragdolls.md "world units (0.1 m)"; Guild & Quests divides by 10 for metres). The record
  assumed 100 units a metre: "the Walker" needed 2,000 km while its catalog said 200 km. Now 10 a metre everywhere (window, catalog),
  Walker at 2,000,000 units (200 km). Camp distances in metres: raiding 350 m-1 km from a town, lying low 2.5 km+, others 1 km+; a camp
  moves only with no player within 3 region cells (about 1.4 km). Axes (from Guild & Quests' direction words): +x east, +z south.

## Factions that speak through their characters; a voice for the Nomads (2026-11-12, both engines)

* The compiler found a faction's packages only through its squads' `dialog leader` / `dialog squad` or the faction's `dialog default`.
  29 factions our content names have neither (Nomads 315 dialogues, Machinists, Police, Band of Bones, Kral's Chosen, Berserkers, Crab
  Raiders, Skeletons...): their people speak from their CHARACTER `dialogue package` lists (often the shared civilian ones). Lines naming
  them together with another faction still reached them (shared packages plus the "my faction" check), but a line for such a faction
  alone failed ("no dialogue packages found"), and lines whose other factions used different packages missed them. `_packages()` now
  falls back to the packages of those factions' squad members (leader, squad, squad2); the "my faction" check keeps everyone else out.
  About 2,000 more attachments in each build.
* **Nomads** (`content/nomads.json`): road talk on sight, animal traders on bulls and pack beasts (with a nod to wagons that reads right
  with or without the Wagon mod), goat herders, bar talk drawn from their data (they coexist with Dust Bandits and the Band of Bones),
  a word for the lone traveller. They admire the Walker, Hard to Kill and the Pack-Beast and fear the Long Night and the Ash Tide
  (`_faction_temperaments.json`), and they carry the news of the roads (speakers of the outlaw "where" and "struck" gossip).
* **Wagon** (workshop 3832616735, mhk): an RE_Kenshi plugin (Kenshi only, never on Meitou); player-built wagons only, nothing for NPCs.
  No dialogue condition can see a research or a building (only `DC_PLAYER_TECH_LEVEL`), so lines cannot react to the player's wagon;
  a Kenshi-side Lua check or the plugin's own data could, if its files are read some day.
* **Lore pass (2026-11-12/13, Kenshi wiki via its API: `kenshi.fandom.com/api.php?action=parse&page=<name>&prop=wikitext&format=json`).**
  Nomads rewritten to match the wiki ("Townie", camps open to anyone, pack beast lamp, goats / pack beasts / bonedogs / bulls, friendly
  with the Western Hive and Empire peasants; vanilla already feeds a starving traveller, so no gift of ours). New: a "share our fire"
  talk menu (EV_PLAYER_TALK_TO_ME on non-trader Nomads, three times each: rest (+1 relations), road news, not tonight) and more Nomad
  bands (the "Nomads" squad added to the 11 wiki zones' homeless spawns at about 3% of each zone's weight; vanilla had it in Shem only).
  `content/faction_voices.json`: 25 dialogues for 21 factions in our own words (Police, Band of Bones, Kral's Chosen, Berserkers,
  Machinists, Crab Raiders, Skeleton Bandits, Manhunters, Scavengers, Skeletons, the swamp gangs, Trade Ninjas, Black Dog, the Gorrillo
  Bandits, Outlaws, Old Machines). Factions with NO vanilla dialogue at all stay silent (Thrall Masters, Skeleton Legion, Cat-Lon's
  exiles, Fishmen, Skimmers); Okranites and the Medics Guild have no members. Compiler: `meitou_only` dialogue flag.

## MeitouClient PR #589: docs/modding-practice.md (read 2026-11-13)
"Code mods in practice" by roscatoday (BeatTheCount), plus a prompt for starting a mod with an AI agent. New to us:
* Meitou's assembly version is its release number and .NET binds only to that version or higher: **build against the oldest release a
  mod supports**. A `net10.0` build against v0.17.3 also loads on the .NET 11 releases (observed on v0.17.5 nightly). We build `net11.0`
  against 0.17.4 (gameVersion >=0.17.4); switching to net10.0 against 0.17.3 would cover 0.17.3 and the .NET 10 "legacy" packages too.
* **One setting the settings page cannot show makes `AddSettings` throw and the whole mod is refused.** Checked offline:
  `modgen/meitou/SettingsCheck` builds both mods' groups with the game's internal `ModSettingsGroup(modId, name, target)`: both OK.
* Town data read in an `AddSystem` factory is too early (town overrides apply later): we read it on the first tick or later. OK.
* The Kenshi fonts have ASCII and U+1FBF-U+2019 only: our code and all dialogue text checked, nothing outside that.
* The `AddUi` overlay draws under the game's own windows (they take clicks first): our record window can be covered by them.
* No per-character mod save slot; mods key characters by portrait serial plus record id. **Our record keys by character name** (a renamed
  character starts a new record; two with one name would share one): a known limit, worth changing if it bites.
* Other: values set with AddSettings live in meitou.user.json (do not ship settings files); `--screenshot` runs load only ~1.4 km;
  a local mods folder copy replaces a Workshop one of the same name; SteamCMD `workshop_build_item` uploads work (for Squad Record).

## Contracts by distance, the Headsman, build target (2026-11-13, compiled, unit-tested, not run)
* **Build target:** both code mods are `net10.0` built against **0.17.3** (oldest supported; gameVersion >=0.17.3); they load on the .NET 11
  releases (SettingsCheck loads them against 0.17.4; `release_check.sh` PASS on 0.17.4 and v0.17.5-nightly.20261112.2).
* **The Headsman:** a kill where the victim was already down (`hit.VictimWasIncapacitated`, or `CombatHitSource.Execution` from the
  Execution mod) counts as an execution (MemberRecord.Executions, record format 7); title "the Headsman" at 25; temperaments: Holy Nation
  admires, Shek despise (so Shek givers refuse work), United Cities and the Traders Guild fear.
* **Contracts by distance (Meitou):** each outlaw contract's offer needs condition REACH_BASE (19700) + k: the giver stands within
  "Contract reach (km)" (setting, default 12, 0 = anywhere) of the outlaw's ground (its camp if known, else the nearest cell of its
  zones); unknown distance never hides an offer. The report line gets effect DISTPAY_BASE (19600) + k with the reward as value: the
  helper adds `CampRules.DistanceBonus` (none within 5 km, up to the whole reward again at 25 km) to `world.Player.Money` and posts
  "N cats more for the long road" (setting "Distance pay", on). Kenshi build unchanged.
* **Growing bounties (Meitou):** an outlaw's price grows a tenth per news stretch it spent raiding (`CampRules.RaidingStretches`, last
  1,000 days), up to double. Every 10 s the helper raises each loaded outlaw's live bounty ledger entries (`CharacterCold.Bounties`,
  `BountyEntry.Amount`, never lowered, claimed ones left) to record "bounty amount" x (1 + growth); contracts pay the growth on top of
  the reward (with the distance bonus, one message); bar talk in three tiers (condition TIER_BASE 27500 + k, 0 once the outlaw is dead
  or jailed). Setting "Growing bounties" (on).
* **Bounty hunters (Meitou, setting "Bounty hunters (test)", off):** `content/bounty_hunters.json` (meitou_only pack): faction "Bounty
  Crews" (Mercenary Guild clone, relations only Nameless -100, so hostile to the player alone) and squad "Bounty crew" (Mercenary
  Captain + 3 Mercenaries), lines on EV_I_SEE_ENEMY_PLAYER. `Hunters.cs` / `HunterRules.cs`: a town at heat 24+ ("hunted") sends a
  crew at most every 72 game hours: a persistent stand-in made 2.5-3.5 km from the nearest player character (dry ground, nobody within
  1.4 km), `Job = PlatoonJob.TravelTarget`, `Point` = the player (re-aimed every 10 s), so `PopulationSystem.StandIn` walks it along the
  roads; loaded, its people get `TaskStart.Walk` at the nearest player character (row TravelToTargetPackage, source Goal). After 72 hours
  or when dead the crew is let go (not persistent). Saved by town and last position (ids change on load). F10 row.
* **Memorials (both code mods record; dialogue Meitou only):** the record keeps the fallen (`Fallen`: name, day, place, killer and
  faction from the last NPC blow within a day, kills, days served, titles, avenger; record format 8), feuds (killer's faction, with the
  hour) and history notes; Recap moments "X fell at Y" (80) and "X avenged Y" (85). A death is seen in the once-a-second look (dead
  characters stay in the table until removed). Killing the killer (same unique record, else same name and faction) avenges them:
  history note and the title "the Avenger". Helper (setting "Victory lines: your squad"): after the fight the nearest standing
  squadmate within 250 m gets event 4 (EV_I_SEE_RAGDOLL, never fired by Meitou, no vanilla player lines) with the body as target, so
  `/YOURNAME/` names the fallen (`content/memorials.json`). Codes 27910 target is a dead squadmate, 27911 days since the last loss,
  27912 town of the last loss (word swap /LWLOSS/ "at Squin" / "out there"), 27913 target's faction killed one of ours within 60 days.
  Lines: mourning over the body, remembering on entering towns (10 days), after a win (20 days), muttering at feud factions.
  Window: a fourth tab, "Fallen". Squad Record gets the record, tab and history (no dialogue).
* **Squad record keyed by identity (2026-11-13):** members are keyed by `CharacterCold.PortraitSerial` + `RecordId` (saved with the
  character; MeitouClient docs/modding-practice.md: "portrait serial plus record id") instead of the name. A renamed character keeps its
  record (history: "Beep goes by Beepy now."), two characters with one name get two records ("Beep (2)" as the key, shown as "Beep"),
  and a name-keyed record from an older save is adopted by the first character of that name. Everything printed uses the current name
  (`SquadRecord.Display`). Dialogue conditions use the read-only `PeekKey`. Record format 9.
* **Wounds remembered (2026-11-13):** each member keeps scars (`Scar`: "arm" / "leg" from a new stump of that `BodyPartType`, "down"
  from a knockdown; towns only, at most 12; record format 10). Condition 27914 (SCAR, who = the speaker): what the town the speaker
  stands in did to them (1 arm, 2 leg, 3 knocked down; worst first). `memorials.json`: lines on EV_ENTER_TOWN ("This is where I left my
  arm. Let's not stay long."). History now says where a limb was lost ("Beep lost an arm at Squin.").
* **Outlaw reprisals (2026-11-13, setting "Outlaw reprisals (test)", off):** a named outlaw whose people the squad bloodied (a strike
  with blood, `StruckMark.Bloodied`) and who lived sends a party two to four game days later (`ReprisalRules.Delay`, seeded per outlaw
  and beating; a 72-hour window to set out, once per beating). The party is a Meitou-only squad "<outlaw> reprisal": the outlaw's squad
  clone without the outlaw (a squad with the unique as leader would make the unique twice), same henchmen and faction
  (`camps[].reprisal` in lw_codes.json). It is made and steered like a bounty crew (`Pursuit` in Hunters.cs, shared now), saved in the
  camp book (format 3: reprisals with last position, and the beating already answered), let go after 72 hours. Condition 27600+k
  (REPRISAL): the speaker is one of outlaw k's party; `x_outlaw_gossip.json` gives them EV_I_SEE_ENEMY_PLAYER lines ("Orsa Quillon
  remembers you. We're here to settle it."). F10 "outlaw camps" row lists the parties out. Kenshi build byte-identical.
* **More Recap moments (2026-11-13):** a town hiring bounty hunters (65) and the crew wiped out (70), an outlaw sending men (70) and
  them cut down (70), an outlaw's price reaching tier 2 / 3 (50 / 60; the first pass after a load only notes the tier). Recap keeps 24
  moments a session from other mods, each caption once.
* **Squad Record 0.2.0:** Fallen page, avenging and the Avenger, the Headsman, executions, hired mercenaries' kills, identity keying
  (renames keep the record). `python3 modgen/make_squad_record.py --zip`. Untested in game like everything else.
* **Meitou 0.18.0 (2026-11-13):** `release_check.sh v0.18.0` PASS (both mods compile, shipped DLLs load, settings build, 107 tests).
  Mod API still 2; the changelog's only mod item is "Code mods can add equipment slots". DA 43 CHOOSE_PRISONER_BAIL is built now
  (DialogueSystem.Law.cs), so our 4 uses work there. Manifests widened to `>=0.17.3 <0.19.0`; the DLLs stay built against 0.17.3.
* **Trophies and keepsakes (2026-11-13, setting "Trophies and keepsakes", on; Meitou only):** Meitou 0.18's one new mod call,
  `ModContext.AddEquipmentSlot(new ModSlot(section, fcsSlot, w, h, x, y))`, adds an inventory section for ARMOUR/ITEM records whose FCS
  `slot` is fcsSlot (4, 10, 11 or 15+). `Trophies.cs` calls it by reflection (we build against 0.17.3): section `lw_neck`, slot 11
  (ATTACH_NECK), 2x2 cells, placed by the settings "Neck slot left/top (restart)" (default 12, 60: a guess, the Kenshi character
  window's layout file is not here). Checked: a scratch probe against the 0.18.0 DLLs registers `ModSlot { lw_neck, 11, 2, 2, 12, 60 }`;
  on 0.17.4 it logs "no mod equipment slots" and the items stay in the bag (an ITEM with an unknown slot is not worn there).
  Items (ITEM, slot 11, stackable 1): the 36 named outlaws and their successors carry named tokens in `inventory` (a copy of Animal Teeth,
  valued at a tenth of the bounty). Original token record IDs stay at their existing allocation points. The successor token IDs are
  reserved in the content packs so other records keep their IDs. The inventory route avoids Meitou's death-item behaviour, where taking
  a death item kills a downed character. "Keepsake of the Fallen" (copy of
  Coin) goes into the mourner's bag when a squadmate dies (the nearest standing squadmate after the fight, Notoriety.cs Mourn); 11
  faction charms ("Okran's Token", "Nomad Bead String"...) go with an admiring barman's gift (gen_title_temperaments.py, _charms.json).
  `content/trophies.json` is meitou_only; the tokens are made on the Meitou target only. Kenshi build byte-identical.
* **Cannibals reviewed (2026-11-13): voice, point 4, new systems.**
  - *Voice (both builds):* vanilla's cannibal lines (package "Cannibals --Unfinished", and the Wizard's own "PROTECT GRAN' WIZARD!",
    "Protec' the Gran' Wizard!") are all the tribe's pidgin: "dh" for "th", bery, ungry, bedder, resh, alibe, no articles. Our Grand
    Wizard, Meat Lord and Mother Brine spoke educated English; rewritten in the tribe's tongue (the Wizard says "Gran' Wizard" and shouts
    in capitals, the Meat Lord stays the polite host, Brine keeps her salt and rows). Alder Quill keeps his clean speech (a Holy Nation
    kitchen priest who came over). Every line a Cannibals speaker says in any pack got the accent pass, and the too-fluent ones
    (horror_regions, base_reactions, x_reactions_events, bosses) were rewritten by hand: 372 lines in 12 packs. The Gate Folk and the
    Chief already spoke this way. The Kenshi build changes for this (text only).
  - *Point 4:* Hush's death cost -20 on every Gate Folk sighting (three dialogues, two each: up to -120). Now the enemy variant has no
    effect and the neutral/ally ones only fire above -30; Hush in chains (-10) only above -20; the leaders' deaths (+3) only below 30.
  - *Trophies (Meitou, `cannibals_trophies.json`, loads after cannibals_data.json):* Gran' Wizard's Bell Cord, Meat Lord's Fork, Alder
    Quill's Token, Mother Brine's Token in their owners' inventories; the Gate Sinew Knot from Hush once at relations 40+ (variable "Gate
    charm given").
  - *Recap:* pack variables can carry `moment` / `moment_place` / `moment_weight`: when the helper's set/add effect first moves the
    variable from 0, the moment goes to Recap (lw_codes.json `variableMoments`). Used for "Warden Hush named your squad friends of the Gate."
  - *Titles by faction:* `_faction_temperaments.json` entries may name a `mod`, `packages` and `event`; the generator writes one pack per
    mod (`x_title_temperaments_cannibals.json` in the Cannibals mod, so the base mod does not depend on it). Gate Folk admire the
    healers (Field Medic, Bonesetter, the Last Mercy), despise the Headsman, fear the Eater-Killer; the Cannibals (on enemy sight)
    admire Limb-Taker, the Carver, Bonepicker, despise the Eater-Killer and the Wizard's End, fear the Long Night and the Ash Tide.
  - Reprisal lines for a cannibal outlaw's men (Gaffer Marrowbone) are in pidgin too.
* **Meitou Transport (2026-11-13; mattresspad's framework, github.com/mattresspad/MeitouTransport, GPL-3 framework, CC0 packs):** a
  squad member rides an owned Garru or Pack Beast, or sails a one-seat skiff; content packs (FCS + transport.json + art) pick the animals
  and boats. Player squad only. The rider's position is moved with the mount, so our record counted rides as walking. `TransportLink.cs`
  finds `Meitou.Transport.TransportSystem` among `world.Systems` and reads its private `riders` / `helms` dictionaries by reflection
  (checked against the released 0.3.0-playtest.4 DLL: both `Dictionary<CharacterId, ...>`); seated distance goes to `Ridden` / `Sailed`
  (record format 11), shown in the window; titles the Rider (50 km ridden) and the Boatman (10 km sailed); the Nomads admire the Rider,
  the Swampers the Boatman. In both code mods (Squad Record 0.2.1). Without Transport nothing changes.
* **Alder Quill and Mother Brine wanted (2026-11-13, both builds):** `cannibals_wanted.json` (Cannibals mod): Holy Nation contract on
  Quill (6000), United Cities on Brine (5500), body hand-in as for the outlaws. They live in the cannibal village, not a camp: the
  helper now answers a campNews outlaw with no camp with "every giver may offer" and no distance bonus.
* **The Atlas (2026-11-13, Meitou only):** a fifth tab in the squad record window. `content/_atlas.json` (hand-written, our words after
  the wiki and kenshimap: 55 places, 36 leaders and bosses) + `gen_atlas.py` (adds the 36 named outlaws, and 8768 gossip lines as
  templates, 3727 tied to a person by dialogue id prefix or rumour subject) -> `x_atlas.json` (meitou_only) -> lw_codes.json `atlas`
  (people get their CHARACTER ids in content.py). `Atlas.cs` / `AtlasBook.cs`:
  - places from the game's own knowledge: `world.Player.Towns[i]` (TownKnowledge Discovered / Explored) over `PopulationData.Sites[i]`;
    common knowledge once on the map, the rest once visited, plus who lives there, your heat there, who of yours fell or was maimed there;
  - listening: once a second `DialogueSystem.PublishSpeech()` (the bubbles on show; non-destructive, unlike `World.TakeSaid`, which the
    host consumes for the dialogue log) said by a non-player within 45 m of a squad member, matched against the templates (/TOKEN/ = any
    text, indexed by the first 10 letters; 48 templates start with a token), noted under its person and every place it names (newest 5
    each, all counted; saved);
  - people: hidden ones (Bugmaster, Cat-Lon, the Fog Prince, Savant, Shade, Oni, the Red Sabre Boss, Valamon, Flying Bull, Ghost, Agnu)
    are a blur after one rumour, placed after two, full once the squad has been within 90 m; others show once their place is known or
    they are talked about. Fate from `Uniques.StateOf` (dead / killed by your squad / in chains), bounty live from the ledger when loaded,
    else the posters' amount, and what they are wanted for.
  - Faces: the mod UI (UiNodeKind Panel / Label / Button, a MyGUI skin name) cannot show an image; `--screenshot` could make portraits,
    but there is nothing to show them in. Image node request drafted for #suggestions (plugin/COMPANION_MODS.md; names no mod, Living Wasteland is not public).
* **Harassment raids (2026-11-14, setting "Harassment raids (test)", off):** the "later" idea in OUTLAWS_LIVE_DESIGN.md. `gen_raids.py`
  -> `x_raids.json` (meitou_only): four raids, each a squad cloned from the raider's own (Flotsam raiders from "flotsam patrol",
  Anti-Slaver raiders from "Anti slaver all strong small", Rebel farmer raiders from "empire rebel farmers small", Cannibal hunting party
  from "cannibal hunter patrol") against the Holy Nation, the Slave Traders, the United Cities and the Cannibals. `Raids.cs` /
  `RaidRules.cs`: every 4-9 game days per raid (seeded) a party is made 2.5-3.5 km from one of the target's towns (sites whose
  SiteFactions owner is the target and whose name is in campNews.towns; the cannibal villages were added to that list), unseen, and
  walked to a lurking point about 1.2 km out (Pursuit.Make; never steered at the player). Gone within 48 h = caught; else let go = got
  away. Codes 28000+r (0 none, 1 out, 2 caught, 3 got away; outcomes talked about for 5 days) and 28100+r (the town, word swap
  /LWRAID_X/). Bar talk and in-town sight lines per state, from the raider, the target and the road's people. F10 row "raids". Towns per
  target in the data: Holy Nation 7, Slave Traders 3, United Cities 10, Cannibals 5. No towns change hands. 115 unit tests.
* **Quests paid by route (2026-11-14, the "Distance pay" setting):** all 57 quests on Meitou. The accept line (start), each step's reply and
  the report line carry helper effects 29000+q (note where the line was said; value 1 starts a new route) and 29500+q (pay on the
  reward): the route start -> each step -> the hand-in, measured, and paid like a contract (CampRules.DistanceBonus: nothing under 5 km,
  up to double at 25 km). Points are kept as quest variables "@route <quest> i x|z" in metres (the existing save; no new format).
* **The squad's part in a raid (2026-11-14):** RaidsSystem observes hits. A squad blow on a member of a loaded raid party = broke it; a
  squad blow on the target faction within 150 m of an active party = helped it. When the raid ends: broke -> the target faction +6,
  helped -> the raider faction +6 (`PopulationData.Relations.Add` with the player faction; the game's own relation message), a History
  line. Raid saves format 2.
* **Atlas sweep (2026-11-14):** 79 places, 80 people. Added the slave camps (Stone Camp with Haga, Slave Farm with Ren, Port South with
  Wada), Holy Mines / Farms / Military Bases, Waystations, Ark (the Reavers; Valamon was wrong as a lone Shek outlaw, he leads the
  Reavers there), Grayflayer Village (Big Gray), Iron HQ (Elder), The Old Prison (Gorrillo), Crab Town (the Crab Queen), New Kralia,
  the hive villages, the Settled Nomads, Ashland Dome I (the Head of Agriculture), Deadhive Overrun, Expedition 4, Burn's Tower, the
  Spider Factory; Big Grim of the Hounds.
* **Atlas: who holds a place now (2026-11-14):** the place page reads the town's live owner (`PopulationData.SiteFactions`, which a town
  override changes: a takeover, the game's own or KUO's) against its own record's faction ("Held by X now; it was Y's."), and its state
  from the override's editor label (destroyed, half destroyed, malnourished, prosperous).
* **LivingWastelandTest retired (2026-11-14, user: not needed):** `x_test_checker.json` removed; the guides tell players to leave an old
  copy unticked or delete it; lw_switch.bat still moves an old folder out of the way.
* **Atlas "last seen" and raid moments (2026-11-14):** the Atlas notes where and when the squad last stood within 90 m of each person
  (AtlasBook format 2) and a met person's page says "Last seen by your squad: Squin, day 41". Breaking or helping a raid is a Recap
  moment (65) as well as a History line.
* **Every feature switchable, from our own window (2026-11-14):** new settings "Living Wasteland talk (%)" (0-100, step 10), "Jobs and
  contracts", "Town heat", "Factions react to titles", "Memorials", "The Atlas" (26 settings in all). meitou_target adds condition
  19103 (talk: a roll against the slider, its own salt) to every dialogue of ours that is only ever said unprompted (attached to no
  EV_PLAYER_TALK_TO_ME; zone arrival lines count as unprompted): 5046 of about 5600; and feature switches 19110 jobs (dialogues named
  "... - offer" / "..._offer": new offers only, reports and hand-ins stay), 19111 titles (tm_ / nt_), 19112 memorials (mm_): 399. Heat
  off: the heat condition reads 0 and heat effects do nothing; titles off also lifts the despised job refusal; memorials off clears the
  mourning queue; Atlas off stops listening and blanks the tab.
  A **Settings tab** in the record window (both mods) lists the mod's own `ModSettingsGroup` (from `AddSettings`): On/Off buttons,
  - value +, choices that cycle; "Defaults" and (Living Wasteland) "Record only" (everything off but the record, titles, Recap).
  Meitou keeps a value only when its own page changes it, so `SettingsFile.cs` keeps every non-default value in
  `%LocalAppData%\Meitou\mods\<mod>.settings.txt` and puts it back after AddSettings; the group's Changed event writes it, so a change on
  Meitou's page is kept there too. Checked with a scratch probe against the 0.18.0 DLLs: clicks change values, the file holds them,
  a fresh group gets them back, list choices cycle both ways, Record only and Defaults set what they should.
  With every switch off, Living Wasteland's helper alone is a squad record (the data-mod features find nothing to work on).
* **Guild & Quests and Raid Commander tie-ins (2026-11-14):** see COMPANION_MODS.md. Squad numbers fed by other mods (`SquadRecord`
  crewStats, saved with event keys in record format 12; squad titles read a crew number before the members' sum). 77 titles. 117 tests.
* **Epitaphs, "Save as text", factions in the Atlas (2026-11-14):** each fallen squadmate gets a line from how they lived and died
  (`SquadRecord.Epitaph`: avenged, a title, many kills, long service, a short one, an unseen killer). History and Fallen tabs have
  "Save as text": record, titles, history and the fallen in `Documents\Living Wasteland\Squad story <date>.txt`, written from the tick's
  copies. The Atlas lists the factions the player has met (`PlayerState.KnownFactions`): standing with the squad
  (`PopulationData.Relations.Get`), its wars (relations -50 or lower with another met faction), the titles it admires / fears / despises
  (`x_title_temperaments` packs' `title_views` -> lw_codes temperaments.views), its towns that remember the squad's crimes, its people of
  note, and lines overheard that name it. Workshop browse read (Steam trending and newest; Nexus refuses automated access): ideas noted in
  COMPANION_MODS.md. 118 tests.
* **Lookout calls, squad mood, tavern tales (2026-11-14, Meitou only):**
  - Lookout (`Lookout.cs`, setting "Lookout calls", on): every 3 s, a standing non-player of a faction at -50 or worse with the player's
    within 150 m of a squad member makes the nearest squad member say an EV_SOUND_THE_ALARM line (event 6: no vanilla or LW line used it,
    Meitou never fires it) at that person; `content/lookout.json` (meitou_only): 23 faction lines by target faction plus a generic one,
    a squadmate answers (T_INTERJECTOR1). Once per enemy squad per 6 game hours, none while anyone of the squad fights, 2 minutes between
    calls. The talk slider covers it. Animals (wildlife factions) are not hostile by relations, so they are not called.
  - Mood (`content/squad_mood.json`): for 3 days after a squadmate dies (loss_days), grief lines on entering a biome and anger after a
    won fight, score 25 so they outrank the cheerful banter.
  - Tales (`content/tavern_tales.json`): bar talk about the squad's biggest titles (named kills, End of Kings, Grave-Fillers, the Ash
    Tide, Unbroken, Wanted-Enders, Gatekeepers, Guild's Own, Chiefbreakers), on condition 29700+i ("tale": anyone of the squad or the
    squad itself holds title i; `SquadRecord.AnyHolds`). rm_ ids: the Gossip slider covers them.

