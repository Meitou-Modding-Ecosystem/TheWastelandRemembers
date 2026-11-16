# Authoring guide: dialogue content for the mod

You write JSON packs in `modgen/content/`. `build_mod.py` turns them into Kenshi dialogue records and checks them.
Read `content.py`'s header for the field list. This page tells you *what to write and how it behaves in the game*.

## Check your work (always)
```
python3 modgen/build_mod.py <GAME_DATA_DIR> modgen/content/YOURPACK.json --out <OUT_DIR>
python3 modgen/gamedb.py <GAME_DATA_DIR> list faction            # also: race, character, world_state, package, town, item
python3 modgen/gamedb.py <GAME_DATA_DIR> list character "lord"  # substring filter
```
Zero `ERROR`/`INVALID` lines is required. Fix every `warning` you can (unknown /TOKEN/s, long lines). All names must exist in the
game data exactly as listed (use `list`); only official data is available (no third-party mods). Never edit files outside
`modgen/content/` and never commit anything under the data folder.

## How Kenshi dialogue works (what the engine does with your JSON)
* NPCs have **dialogue packages**: lists of (dialogue, event). When an event happens to an NPC, the engine looks at every
  dialogue it has for that event, drops those whose conditions fail, and plays one. Our compiler attaches your dialogue to
  the packages of the factions you name, so list the right `factions`.
* A **dialogue** has alternative first `lines` (one is picked), conditions, and a `chance` (%) and `repeat` limit. A
  **line** has up to 8 `say` variants (one is picked at random: use 4 to 8 so the world does not repeat itself) and an
  optional `speaker`, `effects`, and `then` (lines that follow it). A normal bark is just one line.
* Conditions that exist for you: `factions` (my faction, any of), `target_factions`, `races`, `target_races`, `in_town_of`
  (the town the speaker is in belongs to this faction), `states` (world states that must be true/false), `characters`
  (the speaker is this unique), `relation` (my relation to the target, e.g. `["<", -20]`), `personality` (one tag), and `if` with
  any `DC_...` condition (see `modgen/enums.json`; value 1 = true, 0 = false, numbers for amounts). `player_only: true`
  restricts a line to when it is aimed at the player.
* **World states** record what happened. `{"name": "Seta killed by player", "npc": "High Inquisitor Seta", "is": "dead",
  "player": true}` creates a state that is true once the player killed that unique character ("is": "dead" | "imprisoned").
  `"player": true` means the player had to be involved. Use `"player": false` for "is dead, by anyone" if you need a
  non-player version. These are the player's deeds: **the whole point of the mod is that the world notices what the player did.**
  Existing game states are reused when the name matches (see `list world_state`); prefer making your own named
  "X killed by player" / "X imprisoned by player" states for every boss you cover (one `world_states` entry each).
* Effects (use sparingly; always attached to a line): `["DA_AFFECT_RELATIONS", n]` (faction relation change, n -15..+10),
  `["DA_ASSIGN_BOUNTY", n]` (bounty in cats; only for law-and-order factions or the dead boss's own faction, 1000-10000),
  `["DA_CRIME_ALARM", 0]`, `["DA_ATTACK_CHASE_FOREVER", 0]`, `["DA_RUN_AWAY", 0]`, `["DA_AFFECT_REPUTATION", n]`.

## Extra fields (lines can carry conditions too)
* On a **line** (not only the dialogue) you may put `states`, `races`, `characters`, `has_package`, `factions` and `if`. A line
  with conditions is only eligible when they hold, so one dialogue can hold many alternatives for different speakers.
* Root `lines` are alternatives (the engine picks one eligible line). A line's `then` follows it; give `"pct": 50` to a follow-up
  that should play only half the time.
* **Zone arrival** (the player's own squad commenting on where they have just arrived): `"event": "EV_ENTER_BIOME"`, `"zones": ["Cannibal
  Plains"]` (use `list zone`; these are the named regions: Cannibal Plains, Ashlands, Skinner's Roam, Fog Islands, The Shrieking Forest,
  Okran's Pride, Floodlands, The Swamp, Deadlands, The Black Desert ...). The speaker is a member of the player's squad, chosen by
  `has_package` on the line: use `list package "Player"` (e.g. `Player THUG`, `Player TOUGH Merc`, `Player CIVILIAN`, `Player SHEK Warrior`,
  `Player HIVER Ronin`, `Player SUNDEMON`). Use several lines for different kinds of squadmate.

## Events you can use, and how they behave
| Event | Who speaks | Use it for |
|---|---|---|
| `EV_BAR_TALK` | NPCs spawned in bars, no target | **Rumours and chatter.** The main tool for "the world talks about it". Pair with `states`, `in_town_of`, `factions`. `repeat` 3 or more. |
| `EV_I_SEE_ENEMY_PLAYER` | an NPC hostile to the player, who sees them | threats, fear, vows of revenge. Add `player_only` only if you want to exclude other squads. |
| `EV_I_SEE_NEUTRAL_SQUAD` | a neutral NPC sees any neutral squad (the player's too) | wary remarks, awe, gossip to the player's face: add `"player_only": true` and use `repeat` 2+ and `chance` 30-50 (it fires constantly). |
| `EV_I_SEE_ALLY_PLAYER` | an allied NPC sees the player | thanks, shared grief or triumph, favours |
| `EV_PLAYER_TALK_TO_ME` | the NPC the player clicks on; the only place with player replies | greetings and topics for specific characters. Use `characters` to target a unique boss. Player replies are lines with `"speaker": "T_TARGET_IF_PLAYER"` under the NPC's line (`then`). |
| `EV_ENTER_TOWN`, `EV_ENTER_BIOME` | the player's own squad | the player's squad commenting (needs `then` with speakers; use sparingly) |
| `EV_I_DEFEATED_SQUAD` | the winner, target = the beaten squad's member | gloating or mercy after a fight |
| `EV_BOUNTY_SPOTTED` | an NPC who recognises a wanted target | reactions to a bounty on the player |
| `EV_MARKED_FOR_DEATH`, `EV_SCREAMING_TORTURE` | cannibals' victims in cages, no target | horror ambience at cannibal camps |
| `EV_WORSHIPING_SOMETHING`, `EV_EATING_SOMETHING_SOUNDS` | NPCs doing that, no target | religious and horror ambience |
| `EV_LOOTING_EVERYTHING`, `EV_WITNESS_LOOTING_ALLY`, `EV_KIDNAPPING_MY_ALLY`, `EV_WITNESS_GENERIC_ASSAULT` | witnesses to crimes, target = the criminal | reactions to what the player does in public |

Rule of thumb for gossip about a boss: three rings. **Local** (his own faction, towns he held): grief, rage, fear, reverence.
**Neighbours** (rivals, trade partners): glee, worry, opportunism. **Far away** (everyone else): vague, delayed,
garbled; one plausible distortion is allowed ("they say he fell to a thousand men"). People nearby know the player did it;
far away they only know *someone* did.

## Voice and tone (this is a mature, bleak game; write to that)
* Short: one or two sentences, usually under 120 characters. No quotation marks. No stage directions. No names of the
  player (he has none). Address him as stranger, outlander, traveller, or with the game's word-swap tokens.
* Distinct voices by faction: Shek are proud and speak of honour and the Stone Golem, call humans flatskins (`/FLATSKIN/`);
  Holy Nation are zealots (Okran, the Flame, purity, heretics; they despise Skeletons and treat non-humans as beasts);
  United Cities are mercantile and legalistic (cats, bounties, the Empire, Tengu); Bandits are blunt and hungry; Cannibals
  speak in their own dialect (use the `/CANNIBAL...../` tokens, see `list` is not available for tokens, but these exist:
  CANNIBALTASTY, CANNIBALCOOK, CANNIBALBODYPART, CANNIBALCURSE, CANNIBALHA, CANNIBALKA, CANNIBALYES, CANNIBALNAME, CANNIBALSOFT,
  CANNIBALTOUGH, CANNIBALCOIN); Skeletons are flat and literal; Hivers speak softly and communally. Look at the registers and
  examples in `server/config/voices.json` (pools) and the lore in `WIKI_NOTES.md`, `DATA_FINDINGS.md`, `research/wiki/*.txt`.
* Lore is half-remembered: refer to it sideways, never explain it. Do not invent history that is not in the research files.
  Do not invent quests, rituals, sacred items or secret passwords. Do not break the fourth wall or use modern slang.
  Money is cats. Profanity is welcome where a speaker would use it, but vary it (the engine tokens `/FUCKING/`, `/BASTARD/`, `/GODDAMN/` swap in
  varied words; plain words also fine).
* Never repeat a line, a comparison or a catchphrase across variants or across dialogues. Each variant should say something
  different, in a different rhythm. No generic filler ("The desert is harsh").
* Consequences must be proportionate. Reputation and relation changes are small and mostly negative for enemies, small
  positive for grateful allies. Bounties only when the speaker's faction would plausibly post one.

## Content to deliver per boss (adapt; a minor figure needs fewer)
1. `world_states`: killed by player, imprisoned/kidnapped by player (skip if the character cannot be imprisoned in the lore files).
2. LOCAL rumours (`EV_BAR_TALK`, own faction / `in_town_of` their towns) for: killed, imprisoned, and the **alive** state
   (`states`: [["X killed by player", false]]) as legend and dread before the player ever meets them. 3-6 dialogues, 4-8 variants.
3. NEIGHBOUR and FAR rumours (`EV_BAR_TALK`) for killed and imprisoned: 2-4 dialogues each, with `factions` chosen from the
   lore (allies, rivals, trade partners; and for FAR a broad list of unrelated factions).
4. To the player's face after the deed (`EV_I_SEE_ENEMY_PLAYER`, `EV_I_SEE_NEUTRAL_SQUAD` with `player_only`, `EV_I_SEE_ALLY_PLAYER`
   as the attitude suggests): 3-6 dialogues, with a sparing effect where the world would really react.
5. Optionally the boss's own talk lines (`EV_PLAYER_TALK_TO_ME` with `characters`): reacting to what the player did to their
   rivals, 2-4 variants each; keep player replies out unless essential.
Group the pack by region and use ids that start with the pack name so they stay unique (e.g. `hn_phoenix_killed_local_bar_1`).

## Outlaws and contracts (named bounty targets and the quests around them)
A pack may also contain:
```
"outlaws": [ {"name": "Rusty Calder", "template": "Dust Boss", "squad_template": "Dust Bandit squad", "faction": "Dust Bandits",
              "bounty": 4500, "combat": 32, "bounty_factions": ["United Cities", "The Holy Nation", "Shek Kingdom"],
              "henchmen": [["Dust Bandit", 3, 5]], "biomes": ["Ancient Hills", "Desert"], "weight": 3,
              "weapons": ["Katana"], "clothing": ["Samurai Boots"], "female": false} ],
"contracts": [ {"id": "calder", "factions": ["United Cities"], "packages": ["shek bounty desk"], "state": "Rusty Calder killed by player",
                "reward": 3000, "offer": [..variants..], "accept": [..player reply..], "accepted": [..], "decline": [..], "report": [..paid..]} ]
```
An **outlaw** becomes a new unique character (cloned from the `template` character for gear and class, so pick a template that fits:
`list character "bandit"`, `list character "boss"` ...), with the given bounty (cats) paid by the `bounty_factions`, a roaming squad
cloned from `squad_template` (`list squad "bandit"`), and a small chance (`weight`, 1-5; the game's own roaming squads use 20-50)
to appear among the roaming squads of each ground `biome` listed (`list biome`; these are terrain types such as Ancient Hills,
Desert, Boneyard, Swamp, Ashlands, not the named zones). He is unique: he only ever spawns once and does not respawn if killed.
The compiler also creates the world states "<Name> killed by player" and "<Name> imprisoned by player" for him, so dialogues can use
them in `states`, and `characters: ["Rusty Calder"]` works in dialogues (taunts, for example) and `contracts` (the state must be
one of those). A **contract** is a quest: a contract giver (the `factions` named, and optionally extra `packages` by name; use
`list package bounty` to find bounty-desk and bounty-hunter packages) offers it when the player talks to them (the target's state is not yet
true); the player may accept (the report-back option then unlocks, and the offer stops being shown); once the target's state is
true the giver pays `reward` cats on the next talk. Write the lines in the giver's voice. Other things people say should hint where
the outlaw is without a map marker: bar rumours (`EV_BAR_TALK`) naming him and the roads or zones he works, from nearby factions.

## Talk dialogues and the talk cursor (found in play, round 2)
The game checks whether you can talk to a character every frame to show the speech-bubble cursor. A random `chance` below 100 on a
talk (EV_PLAYER_TALK_TO_ME) dialogue made that check flip on and off, so the cursor flickered between the arrow and the bubble and the
player had to right-click at the right instant. Rules now enforced by the compiler:
* A talk dialogue with `chance` below 100 is compiled with chance 100. (The build prints how many were changed.) Do NOT give a
  dialogue a negative `score`: no vanilla dialogue has one, and -1 crashed the game while it loaded the data. With equal scores the
  game's own talk dialogue may or may not win a tie; if one of ours hides a vanilla menu, give ours a condition that excludes it.
* Quest and contract offers are no longer spread by chance. They are limited to characters with one fixed personality tag chosen from
  the quest id (`offer_tag`), which is stable for a character, so the cursor does not flicker. Which characters qualify depends on how
  common that tag is; if offers turn out too rare, add a second offer dialogue with a different tag.
* Use chance freely on non-talk events (sightings, bar talk, arrival).


## Voice: British English, the way Kenshi's people talk
Lo-Fi Games is a UK studio and the game's people talk like it: plain, dry, a little blunt. Write lines the same way.
- British spelling: armour, colour, favour, honour, centre, grey, travelled, realise, defence, "got" (not "gotten").
- Everyday British turns, used lightly: "your lot", "reckon", "mind" ("Mind yourself out there."), "going on about it", "hard" for tough,
  "proper" now and then. "Mate" and "bloody" exist in the game but are rare: keep them rare.
- Understatement over drama: "Not badly enough." beats "They will pay for this!"
- Names keep their own spelling (Big Gray, the Grayflayers).
