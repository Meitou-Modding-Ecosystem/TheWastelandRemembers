# Outlaws that live, step two: camps that move (Meitou only; built 2026-11-12 behind the "Outlaw camps (test)" setting, off by default; not play-tested)

Step one is built: outlaw news (`OutlawNews.cs`, `content/x_world_news.json`). Each living named outlaw is always raiding, lying low,
gathering men or licking wounds, picked from the world seed and the date, and townsfolk gossip about it. Step two makes the world match the news.

## What the player would notice

* An outlaw who is **raiding** is camped near the roads and towns of their region; one **lying low** is in a remote corner of it.
* **Gathering men**: their squad is bigger next time you find them (+2). **Licking wounds**: smaller (-1, never below the leader and one).
* They move between stretches (every 2-3 game days), never in sight of your people.
* Kill or jail them and the camp is gone for good, as now.
* On Kenshi nothing changes: outlaws keep appearing through their regions' spawn lists.

## How (Meitou 0.17.2, read from its source and from Guild & Quests' use of the same API)

* **Squads off screen.** A `Platoon` in `world.Platoons` is a squad that outlives its zone. While no player zone is near it, it is a stand-in
  (`PlatoonState.Unloaded`): just a `Position` (X/Z), a template, a size. When a zone near it loads, the population system makes its people
  from `TemplateId` + `Key` + the world seed (`Activating`, then `Loaded`). Meitou creates them like this itself
  (`PopulationRoaming.AddPoolPlatoon`: `Id = world.Platoons.NextId()`, `Origin`, `TemplateId`, `Faction`, `Key`, `Size`, `Position`,
  `Package`/`Job` from `PlatoonJobs.For`), and so does Guild & Quests (rival camps, caravan escorts).
* **Ours.** At the first tick, for each living outlaw: one platoon from the outlaw's squad template (our "<name> squad" record), `Origin -1`
  (wild), `Persistent = true`, a stable `Key` from the outlaw's index, size from the template, position from its region (below). The
  helper's save data keeps outlaw -> last spot (platoon ids change on load; see below).
* **Where.** Candidate spots per outlaw, worked out once: sample the map on a grid, keep points whose biome (`BiomeMap.GroupOf`) is one of the
  outlaw's regions and where `world.Walkability.TryStand` finds ground; split them into "near a town or road" (within ~3000 of a town site)
  and "remote". The activity picks the set; the stretch number picks the spot, so it is deterministic and needs no extra save.
* **Moving.** Every few game seconds: if the outlaw's activity stretch changed, its platoon is `Unloaded`, and no player character is within
  the load range (~6000) of either the old or the new spot, set `Position` to the new spot and adjust `Size`. A loaded platoon is never moved.
* **Death and jail.** `Platoon.Dead`, or the outlaw's unique entry dead or imprisoned (`world.Uniques`): stop managing it.
* **No doubles.** The Meitou build drops the outlaws' `homeless spawns` entries (`meitou_target.py`), so the helper's camps are the only way
  an outlaw appears there. The unique registry already refuses a second copy of a unique character.

## Checked against Meitou 0.17.4 and two mods (2026-11-12, decompiled for understanding only, nothing copied)

Read: the 0.17.4 release DLLs (`Platoon`, `PopulationSystem`, `SaveCapture`), Guild & Quests (rival camps, `Spawned`/`CampPart`) and
Kenshi Ultimate Overhaul's living-world part (`KuoLivingWorld`). Raid Commander was read in an earlier session (notes in COMPANION_MODS).

* **Making a camp (Guild & Quests' recipe, all public API):** `SquadTemplate.From(db.Find(templateId))`, size from
  `SquadFactory.Plan(db, template, population.Settings.SquadSizeMultiplier, world.Seed, key, population.States).TotalMembers`,
  package and job from `PlatoonJobs.For(data.Ai, template)`, then `world.Platoons.Add(new Platoon { Id = world.Platoons.NextId(), Origin,
  TemplateId, Faction, Key, Size, Position, Package, Job, Persistent = true, WaitLeft = 1e9f })`. The huge `WaitLeft` holds the stand-in
  in place (the engine counts it down before giving a stand-in its next goal). Once it is `Loaded`, G&Q pins the squad:
  `squad.HomeCentre = spot; squad.HomeRadius = 60`, so the people stay at the camp instead of wandering.
* **Persistent keeps it:** `PopulationSystem.Kept` never drops a persistent platoon when it unloads; persistent platoons do not count
  towards a zone's population load (`AddPlatoonLoad`), and the off-screen stand-in walker (`StandIn`) leaves them alone when their job is
  None. So the engine will not cull or wander our camp by itself.
* **Moving works.** `Platoon.Position` and `Size` are settable. A camp that has been loaded once keeps its people in `Stored`; when it loads
  again, `SpawnKept` shifts every member by `Position - stored.Anchor` and snaps each to walkable ground (`WalkableSpots.Nearest`), so
  moving the stand-in moves the whole camp. A camp never loaded is simply made fresh at its `Position`.
* **Save and load: ids are NOT stable, and a never-visited camp is not saved.** `SaveCapture.WriteStoredSquads` writes an unloaded platoon
  only if it has `Stored` members (it was loaded once); it then marks it `persistent`. A camp the player never went near is not in the save
  at all, and after a load every platoon gets a new id. Guild & Quests copes by finding its camp each slow tick by template near the spot
  and pitching it again if missing ("pitched again after a load"). **We do the same:** the helper saves only outlaw -> last spot; each slow
  tick it looks for a platoon with the outlaw's template near that spot (or anywhere, since each outlaw template is unique), adopts it, and
  makes a new one if none exists and the outlaw is alive. No double: the unique registry refuses a second copy of the outlaw character,
  and we only make one when none is found.
* **Spots:** G&Q picks a camp spot by angle and distance rolls from the world seed around a town, refuses spots within 5000 of any town
  (`data.Sites`), and accepts one where `Walkability.IsWalkable(x, z)` and `GroundHeight - TerrainHeight < 5` (dry ground, not in water or
  on a roof). It does this at board time for places far from the player, so walkability answers without the zone loaded. We use the same
  test with `BiomeMap` for the region.
* **Out of sight:** G&Q's rule is "unloaded, or loaded and every living player character farther than 3000". We move only unloaded camps,
  and only when no player character is within ~6000 of the old or the new spot.
* **Removing (if ever needed):** the engine's private `PopulationSystem.Despawn(world, platoon, bool, bool)`, by reflection, with a manual
  fallback (G&Q). We should not need it: a dead or jailed outlaw's camp is left to the engine.
* **KUO's route is different and not ours:** it moves armies by starting Meitou's own war campaigns (reflection on the private
  `PopulationSystem.Start(world, warManager, campaign, CampaignTown.OfSite(site), CampaignTown.None, hours)`) and holds an assault with one
  Harmony prefix on `UpdateAssault`. Useful later for harassment parties (a campaign is a ready-made "march somewhere" with the engine doing
  the walking); its strength count (stored size while unloaded, living members while loaded) is the same rule as `PopulationSystem`'s.
* **Raid Commander's route** (loaded people only): `TaskStart.Walk(world, slot, goal, mode, TaskType.TravelToTargetPackage,
  TaskSource.Goal)`. We do not move loaded camps, so not needed for step two.

## Still open (needs the game)

* A mod-made camp has not been seen activating in our game (G&Q's do, which is encouraging).
* Which package suits a camp: the outlaw template's own (via `PlatoonJobs.For`) first; if the squad leaves its spot, a stay-put package.
* Every Meitou update can rename private members; we use none in this plan (reflection only if removal is ever needed).

## Built (2026-11-12)

* **Zones, not terrain biomes.** Meitou reads "homeless spawns" only from BIOME_GROUP records (type 95, our compiler's `zone`), never from
  the terrain BIOMES (type 28, our `biome`), and its region map (`PopulationData.Biomes`, 64 x 64 cells of 4608) is painted with groups. Our
  outlaws only listed terrain biomes, so **on Meitou they never spawned at all**, and very likely not on Kenshi either: Meitou's docs
  (decompile-verified against kenshi_x64.exe) describe the original's wild-squad draw per zone only, and the user does not remember ever
  meeting a named outlaw. Fix: every outlaw in `outlaws.json` and `recruits.json` has a `zones` list (1-3 of Kenshi's named zones), and
  **both builds** add the outlaw's squad to those zones' homeless spawns (`(squad, weight, 0, 0)` like vanilla zone entries; the old
  terrain-biome entries stay, harmless). Zones were checked against kenshimap.com (fan map: camp and town markers with their zones),
  e.g. crab raiders in Greenbeach, reaver camps in The Outlands and Stormgap Coast, berserkers in the Spider Plains, skin bandits in
  Skinner's Roam and Sonorous Dark, skeleton bandits in The Crags / Deadlands / Venge. A build warns about an outlaw with no zones.
* `meitou_target.py` writes `camps` into `lw_codes.json`: news index (same as outlaw news, so camp and gossip agree; outlaws without news
  lines get indexes after them), name, character id, squad id, zone ids. 36 camps (24 outlaws + 12 recruitable outlaws).
* **`OutlawCamps.cs`** (`lw-camps` system, every 300 ticks): per outlaw: stop if the unique is dead or imprisoned, or has joined the player
  (recruits); find its platoon by squad template (persistent first, then nearest the last spot; ids change on load); if none, make one at
  this stretch's spot when no player is within 3 cells (about 1.4 km); if it is loaded, pin the squad home (radius 60); if unloaded, hold it still and, on
  a new stretch, move it when no player is near either end, resizing only a never-loaded stand-in. Any exception switches the feature off
  with a log line and a message. F10 row "Living Wasteland: outlaw camps". Save data: outlaw -> last spot, stretch, activity (CampBook).
* **`CampRules.cs`** (pure, 11 unit tests): spot choice (seeded tries in the outlaw's zone cells, dry ground, town distance per activity:
  raiding 350 m-1 km, lying low 2.5 km+, others 1 km+, fallback any dry spot 300 m+ from a town; world units are 0.1 m), size rule, move safety, cell maths
  matching Meitou's `WeatherAreas`, save round trip.
* **The engine's own spawns stay:** with camps on, the engine may still roll the outlaw's squad from the zone lists; the unique registry
  refuses the second outlaw, so that squad is henchmen only. Once the camps are play-tested, consider dropping the outlaw zone spawns on
  Meitou and turning camps on by default.

## Build order (original plan)

1. `meitou_target.py`: drop outlaw `homeless spawns` on Meitou; write each outlaw's squad template id and regions into `lw_codes.json`.
2. Helper: `OutlawCamps` system (create, move, resize, stop), save data, an F10 row "Living Wasteland: outlaw camps" that writes each camp's
   activity, region and position to log.txt.
3. Pure parts unit-tested (spot choice, size rule); the engine part tested in a Meitou session: find a raiding outlaw, wait for a new
   stretch while far away, find them again elsewhere.

## Later: harassment instead of conquest (from KUO players' feedback, 2026-11-12)

Players of Kenshi Ultimate Overhaul said skirmishes and harassment feel better than whole towns changing hands, and that a small faction
(Flotsam Ninjas) should harass a big one's patrols and caravans rather than conquer it. That is our scale. With the camp machinery above:
small factions' raiding parties placed near an enemy's roads for a few days, with news lines ("Flotsam raiders burned a Holy Nation
patrol near Bad Teeth"), using the same news-from-the-seed approach as outlaw news. No towns change hands; KUO and Meitou's own wars keep that
part. KUO's rule for legal war targets (at war, within 4.5 km of a town the attacker holds, outposts count) is a good model for where a
raiding party may appear.

## Outlaws who react, and gossip that points (built 2026-11-12, compiled, unit-tested, not run)

* **React** (works with or without the camps setting): the camps system also observes hits. A blow by a player character or a hired
  merc on an outlaw or anyone in its squad (platoon of its squad template) is queued; in the slow step the outlaw is **shaken** for two
  stretches (`CampRules.ReactFor`, 120 game hours): **lying low** after a scare, **licking wounds** if anyone was downed or killed. A
  second blow while shaken only adds blood (the first blow's time stands, so one fight moves the camp once). The outlaw news conditions
  read the shaken activity (`OutlawCampsSystem.DoingIn`), so townsfolk talk about them licking their wounds; with camps on, the camp's key
  changes (`CampRules.Key`), so it moves (far off: 2.5 km+ from towns) as soon as nobody of the squad is within 1.4 km.
* **Point** (needs camps on, since only then is the spot known): per outlaw two word swaps, `/LWNEAR_k/` (nearest of the 64 war-talk
  towns, code 26000+k = 1 + its place) and `/LWDIR_k/` (from that town: 1-8 compass from north, 9 "right outside" within 150 m; code
  26500+k). Generated content `content/x_outlaw_gossip.json` (from `gen_outlaw_gossip.py`; do not edit the JSON): "where" lines (bar
  talk 12%, on sight 5% in towns) when the camp is known ("Orsa Quillon's lot are camped east of Squin. Nobody's gone to look.") and
  "struck" lines for five days after the squad hit them and they lived (code 27000+k, days since; dead or jailed outlaws read as never).
  Speakers: the factions that want the outlaw dead plus the Traders Guild; `on_` ids so the Gossip amount slider covers them.
* Axes are +x east, +z south (Guild & Quests' direction words; not yet seen in game: if "north" comes out south in a test, flip z).
