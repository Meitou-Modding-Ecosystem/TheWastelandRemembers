# The Wasteland Remembers: interop notes

For mod makers working alongside The Wasteland Remembers (TWR), a Meitou-only Kenshi mod. This repo holds what other mods need: what TWR
does, how to talk to it, what is planned for working together, and what we learnt about Meitou's engine along the way. TWR's own source
stays in its own repo; nothing here needs it.

- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3839368189
- Supports MeitouClient 0.17.3 up to (not including) 0.21.0. Played on 0.20.0; builds and loads checked on 0.20.1.
- Current build: 20261115aj (the API below is in it).

## What TWR does (and so what other mods shouldn't do twice)

| Area | TWR | Notes for other mods |
|---|---|---|
| Rivals | A survivor your squad beat (the actual character) is renamed for it ("Hollin One-Arm"), recovers a day or two, then hunts the squad across the map, a little stronger each return. At most three. Ends when they die or are jailed. | Personal grudges. Faction-level nemeses (named leaders) are a different thing and can sit beside it. |
| Town heat | Each town remembers crimes against it (0 to 40; tiers quiet / noticed 6+ / wanted 14+ / hunted 24+). Crime heat halves in about three game days, blood heat in twelve. A faction's other towns feel half. | Readable and writable through the API. A debt, a broken deal or a betrayal can add heat. |
| Bounty hunters | A hunted town hires a crew (its own faction, "Bounty Crews"), placed 2.5 to 3.5 km from the player, which tracks the player across the map and attacks. At most one crew per town every 3 game days; a crew gives up after 3 days. Beaten when nobody in it is left standing. | Use `AddHeat` to set a town's hunters on the player instead of building another hunter. |
| Outlaws | 36 named outlaws with contracts, bounties that grow while they raid, news of what they are up to. Hurt their people and they may send a reprisal party. When one dies, someone from their gang takes over a few days later. | Uniques; TWR leaves other mods' named characters alone. |
| News and talk | Town criers, bar talk, rumours of what really happened (kept near where it happened), lookouts, faction voices. The Atlas lists places, people and factions the squad has heard of. | The natural place to show other mods' events to the player (planned: incoming events, below). |
| Wars | Reads Kenshi's own campaigns and marches and talks about them. Steps aside when Kenshi Ultimate Overhaul's war layer is loaded. | A mod that simulates war (Dustlife) owns it; TWR will step aside for it the same way and report it. |
| Raids | Small harassment raids between factions (test feature, off by default); nothing changes hands. | |
| Squad record | Kills by faction, titles from deeds (75), history, the fallen, a Recap link. | Titles are readable through the API. |
| Economy | Only alms, contract pay and bounties. No production, supply or garrisons. | Free ground for an economy mod. |

## Talking to TWR: the API (version 1)

A public static class, `LivingWasteland.MeitouMod.WastelandApi`, read by reflection so no mod depends on TWR. Full reference:
[MODDER_API.md](MODDER_API.md). In short:

- Heat: `Heat(town)`, `HeatTier(town)`, `HeatTowns()`, `AddHeat(town, amount, reason)`, `Forgive(faction, amount, reason)`
- Who is after the player: `Rivals()`, `Hunts()`, `HuntUnderway()`, `SendsHunters`
- The squad's story: `SquadTitles()`, `Note(text)` (History tab), `Moment(caption, place, weight)` (BeatTheCount's Recap)

Plain types only (strings, numbers, bools, lists of string-keyed dictionaries). Nothing throws; before a game loads, reads come back empty and
writes return false. `Version` only grows; nothing is removed or changes meaning.

## Planned for working together (issues welcome)

1. **Incoming events (built in 20261116ak: `Report`, API version 2).** Another mod reports something that happened (a town fell, a garrison grew, a town is short of food, a siege began,
   surplus squads set out); TWR turns it into town talk, rumours, the Atlas and Recap moments, near where it happened, so the world talks
   about it once.
2. **One hunter at a time.** A shared rule for who sends squads after the player: check `SendsHunters` / `HuntUnderway()` before sending
   your own, and a way for another mod to tell TWR to hold back.
3. **Leaders left alone.** A way to tell TWR's rival picker to skip another mod's named leaders.
4. **`SendHunters(town, reason)`.** A crew sent at once, without touching the town's heat (for a bank's debt enforcement and the like).
5. **Moving pursuers onto engine hooks.** Once MeitouClient PR #786 (SpawnPlatoon, HoldPlatoon) is in, TWR's crews, reprisals and rivals
   move onto it, with a fallback for older Meitou versions.

## What we learnt about Meitou's engine (from play tests on 0.19 and 0.20)

Useful to anyone moving squads or tracking characters. Checked in play, but engine behaviour changes between versions: re-check on yours.

- **Saves drop squads that never loaded.** The save keeps platoons that have loaded once, and campaign platoons. A squad you spawned that
  never came near the player is gone after a reload. Place it again once per loaded game, but never one you saw this session that is now
  missing: that one was beaten (Meitou removes a squad whose members are all dead), and re-placing it brings a killed crew straight back.
- **Platoon ids change on load, and character handles get reused.** When a character unloads, its slot can go to someone else (in play, a
  respawning crew member was counted as a rival). Check something saved with the character (its name) before acting on a handle.
- **Off-screen squads and jobs.** A travel job walks the stand-in to its home town and clears the point. A persistent platoon with no job
  never moves. A short-range patrol job walks to its point, so moving the point to the player every few seconds steers it. Near the player
  Meitou barely moves stand-ins (about 70 m an hour in play), so a hand nudge at walking pace until it loads helps.
- **Loaded squads.** Their own AI decides; walk orders alone lose to it. `StartContract` works on any non-player squad, but a running
  contract can't be re-aimed or replaced. The vanilla Bodyguard package's "follow mission target" goal follows properly from 0.19.1. The
  assault goal only picks targets within its roam reach (about 650 m), its home town or an existing fight; `AiSystem.OrderAssault` (the
  dialogue's Assault) works within about 60 m.
- **Fights end early.** "Nobody in combat" happens while the beaten lie down; they get up and fight on. Treat a fight as over only when
  everyone the player fought is dead, down or 50 m away (other hostiles nearby don't count, so a busy road or a high population setting
  doesn't hold it open).
- **Act while the character is loaded.** Renaming, equipping or marking a character only works while it is loaded. Do it at once and delay
  only what the player is told.
- **Kill credit.** Tell "killed by the player's squad" from "died" before telling the player; deaths by others read as the player's deed
  otherwise.

## Contact

The Wasteland Remembers' thread on the Meitou Discord, or issues here.
