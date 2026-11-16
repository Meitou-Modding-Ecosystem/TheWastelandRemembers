# Titles that change how people treat you, by faction (built 2026-11-12 after the user's review; compiled, not run)

## What exists

* 65 titles (`content/notoriety.json`), member or squad, from the squad record (Meitou only).
* 110 title reactions, all talk, no effects: factions you killed hate the holder ("Okran sees what you have done"), and enemies of your
  enemies approve. They react to WHO you killed. Nothing reacts to WHAT KIND of person a title says you are.

## The idea

Each faction has a temperament: things it admires, fears or despises, whoever the victims were. A title touching one of those changes
how that faction's people treat the holder, a little, in ways that last and stay lore-friendly. "Vanilla but more": no new systems on
screen, no numbers, just people who behave differently.

## Levers we have on Meitou (checked)

| Lever | How | Notes |
|---|---|---|
| Lines | dialogue with `lw_if: title` (exists) | on sight, bar talk, talk menus |
| Relations | `DA_AFFECT_RELATIONS` effect on a line (vanilla action 4) | small, once per title per faction (`repeat`/once only) |
| Gifts | a new helper effect "give item" (the reverse of our take-item effect) | barman's "on the house", a waterskin, a ration |
| Jobs | our offers already have conditions: add "not despised" | a faction's givers will not hire a despised holder |
| Recruits | our recruit dialogues: an extra branch for admired titles | a fighter joins a Last One Standing more readily |
| Guards | heat tiers are helper conditions: a feared holder counts as one tier hotter | guards warn sooner, never arrest for it |
| Prices | **not available**: Meitou has no reputation price factor (docs/game/economy.md); only a Harmony patch could | left out (we avoid Harmony) |

## Faction temperaments (draft for review)

| Faction | Admires | Despises | Fears |
|---|---|---|---|
| **Holy Nation** (zealous; work is Okran's way) | Eater-Killer, Hive-Burner, Swarm-Breaker, the Labourer, Wall-Raiser | the Unbeliever, Narko, Lordslayer, Bug-Burner? | nothing (the faithful do not fear) |
| **Shek Kingdom** (honour in battle) | Last One Standing, the Veteran, the Old Hand, Old Brawler, Hard to Kill, the Unburied, the Breaker | **the Quiet Hand** (sneak attacks: no honour) | nothing; they want the fight |
| **United Cities** (nobles; money and order) | the Bounty Takers, the Wanted-Enders, Kingslayer (Dust King), Reaver-Bane | Samurai-Killer; sneer at the Labourer and Pack-Beast (work is for slaves) | the Long Night, the Ash Tide, the Grave-Fillers |
| **Traders Guild** (business) | the Bounty Takers, Reaver-Bane, Crab-Cracker (safe roads) | Guild-Breaker, Slaver's End (bad for trade) | the Long Night |
| **Tech Hunters** (relics and skill) | the Scavenger, the Maker, the Smith, the Armourer, the Walker | nothing | nothing |
| **Anti-Slavers** | Slaver's End, Hunter of Hunters | nothing | nothing |
| **Flotsam Ninjas** (against the Holy Nation) | the Unbeliever, Narko, Lordslayer | nothing | nothing |
| **Mercenary Guild** | the Veteran, the Old Hand, the Bounty Takers | nothing | nothing |
| **Nomads, Drifters** | the Walker, Hard to Kill | nothing | the Long Night |
| **Swampers** | the Quiet Hand (their kind of work) | Swamp-Drainer (exists as "hated") | nothing |

Bandit factions, cannibals, fogmen and hives keep the existing "hated" lines only (they do not do etiquette).

## What each attitude does

* **Admires:** on sight, now and then, a word of respect (existing tone: short, dry). Once per title per faction: relations +3 the first
  time one of their people speaks to the holder about it. In their towns the barman may give one thing "on the house" once per title
  (drink or ration, never money). Recruits of that faction: one extra willing line for the holder.
* **Despises:** cold lines; once per title per faction relations -3; their job givers will not offer work to the holder ("We don't hire
  your kind."). Never violence: it is contempt, not a war.
* **Fears:** guards of that faction treat the holder as one heat tier hotter (warnings sooner; no arrests or fights from fear alone);
  townsfolk step aside lines.

Relations changes stay small and capped (each title once per faction), so a long game cannot drift a faction into war by titles alone.

## Build plan once approved

1. `content/faction_temperaments.json`: the table above as data; the compiler generates the dialogues (like gen_outlaw_gossip.py),
   so editing the table is all it takes to change a faction.
2. Helper: a "give item" effect code; "not despised" job gate (one condition on the offers of each faction's givers); the feared-holder
   heat tier.
3. Lines: about 3 per (faction, attitude) plus per-title touches where it reads better ("Okran's Pride needs hands like yours.").

## Questions for the user

* Does the table read right? Swap, add or cut freely (it is your play style: which factions you deal with most?).
* Relations bumps (+3/-3 once per title): yes, smaller, or talk only?
* Despised holders refused jobs: yes, or only cold lines?
* Gifts from admiring barmen: yes or no?

## The user's answers (2026-11-12) and what was built

* Table: yes; the Holy Nation **fears** Narko (moved from despises). Relations +3/-3 once per title per faction: yes. Despised holders
  refused jobs: yes. Gifts from admiring barmen: yes, rarely.
* **Data:** `content/_faction_temperaments.json` (hand-edited: per faction admires / despises / fears, its lines, its gift item).
  `gen_title_temperaments.py` writes `content/x_title_temperaments.json` (127 dialogues). "Nomads" has no dialogue packages, so the
  wanderers' row is "Drifters".
* **Per (faction, title):** on-sight lines (chance 10, `nt_` ids under the Gossip slider); admire/despise: a once-only line with
  `DA_AFFECT_RELATIONS` +3/-3 (chance 30); admire: a barman's or shopkeeper's gift (chance 4, `DC_IS_A_TRADER`), once.
* **Helper codes:** 19410 "this faction has not yet done it" (value = title index * 2 + kind + 1; kind 0 bump, 1 gift), 19411 mark done
  (stored as quest variable "@temper faction|key": saved and hashed), 19420 "the target holds a title the speaker's faction despises"
  (who = target), 19500+g give the gift item (`PopulationData.Items.Create`, `Inventory.Add`; nothing if no room, logged).
* **Jobs:** every contract offer line needs "not despised" and gets a refusal line ("We don't hire your kind."); quest offers simply do
  not appear. Only the four factions with a despise list are affected.
* **Fear** is lines only (the heat condition sees only the speaker, so "one tier hotter for a feared holder" was not possible cleanly).
