# The Wasteland Remembers: API for other Meitou mods

The Wasteland Remembers (from build 20261115aj) has a small public class other mods can read and call, so mods can work together without
depending on each other. Find it by reflection; if the mod isn't installed you get null and carry on.

```csharp
static readonly Type? Wr = AppDomain.CurrentDomain.GetAssemblies()
    .FirstOrDefault(a => a.GetName().Name == "LivingWastelandMeitou")
    ?.GetType("LivingWasteland.MeitouMod.WastelandApi");

static object? Call(string name, params object[] args) => Wr?.GetMethod(name)?.Invoke(null, args);
static object? Get(string name) => Wr?.GetProperty(name)?.GetValue(null);

// examples
bool there = Wr is not null && (bool)(Get("Ready") ?? false);
double heat = (double)(Call("Heat", "Squin") ?? 0.0);
Call("AddHeat", "Stack", 24.0, "unpaid loan");          // Stack now hunts the player; its bounty crews do the rest
```

Assemblies load in any order, so look the type up on first use (in a tick), not in your mod's Initialize. Call from the game thread (a
system tick, a dialogue condition or effect). Nothing throws: before a game is loaded the reads come back empty and the writes return
false. Only plain types are used: strings, numbers, bools, and lists of string-keyed dictionaries. Positions are world units on the map
(x, z).

`Version` goes up when something is added. Nothing is removed and nothing changes meaning, so check `Version >= n` for what you need.

## Versions 1 to 4

| Member | What it does |
|---|---|
| `int Version` (const) | 4 (3 from build 20261116ap, 2 from 20261116ak, 1 before) |
| `string Build` | The mod's build id, e.g. `20261115aj` |
| `bool Ready` | A game is loaded and the mod is running |
| `double Heat(string town)` | How much a town has it in for the player, 0 to 40. 0 when unknown or the Town heat setting is off |
| `string HeatTier(string town)` | `quiet`, `noticed` (6+), `wanted` (14+), `hunted` (24+: the town may hire bounty hunters) |
| `IReadOnlyList<IReadOnlyDictionary<string, object>> HeatTowns()` | Every town with heat: `Town`, `Faction`, `Heat`, `Tier`, and (version 4) `Node`, its stable id |
| `bool AddHeat(string town, double amount, string reason)` | Adds heat in a town (its name as the game shows it). Fades like crime heat, half every three game days. Clamped to 0 to 40; 24 makes it hunted. The reason goes to the log |
| `bool Forgive(string faction, double amount, string reason)` | Takes heat off every town of a faction (a pardon, a paid debt) |
| `IReadOnlyList<...> Rivals()` | Rivals the player has been told about: `Name`, `Faction`, `Phase` (`recovering`, `hunting`, `dead`, `jailed`, `lost`), `X`, `Z`, `Returns`, `Wins` |
| `IReadOnlyList<...> Hunts()` | Everyone out hunting the squad now: `Kind` (`bounty crew`, `reprisal`, `rival`), `Name`, `X`, `Z` |
| `bool HuntUnderway()` | Someone sent by this mod is hunting the squad right now |
| `bool SendsHunters` | This mod may send hunters (bounty crews, reprisals or rivals are on). If your mod has hunters of its own, hold back while this is true so the player isn't hunted twice over |
| `IReadOnlyList<string> SquadTitles()` | Title ids held by the squad or any member (e.g. `narko`) |
| `bool Note(string text)` | Writes a line to the squad's History tab |
| `bool Moment(string caption, string place, int weight)` | Sends a moment to BeatTheCount's Recap through this mod's link (weight 0 to 100). False when Recap isn't installed |

Recap owns screenshot and clip capture. See [recovery and Recap integration](RECOVERY_AND_RECAP.md) for the positioned custody events, renderer requirements and PZA compatibility notes.
| `bool Report(string kind, string place, string faction, string text, int weight, string source)` | **Version 2.** Something happened in the world; towns near it talk about it. Kinds with a line of their own (leave `text` empty): `town-fell`, `siege-began`, `siege-broken`, `raid-sent`, `garrison-grew`, `town-short`, `caravan-lost`, `warlord-fell`, `warlord-rose`; any other kind needs a `text`. `place`: a town's name as the game shows it; `faction`: a faction's name or empty; `weight` 0 to 100 (the weightiest live news is talked about; 60+ is also posted when the squad hears it); `source`: your mod's id. The same kind at the same place within 12 game hours is taken once, whoever reports it. False (reason in the log) for an unknown town, nothing to say, a repeat, or the setting off |
| `bool SendHunters(string town, string reason)` | **Version 3.** A town sends a bounty crew after the squad now, for your reason (an unpaid loan, a broken contract), whatever its heat. The crew sets out within a few game seconds, tracks the squad wherever it is and is beaten once nobody in it is left standing; with Rivalry Core installed it waits its turn for the one hostile-party slot, a game day at most. False when the Bounty hunters setting is off, the town is unknown, or that town already has a crew out or asked for |
| `bool LeaveAlone(string name, string faction, string source)` | **Version 3.** A character your mod named (a war leader, a raid captain): TWR never makes it a rival or renames it. By name and faction as the game shows them; an empty faction covers the name in any faction. Saved with the game: call it when you name the character. The game's own named characters are always left alone |
| `IReadOnlyList<...> Outlaws()` | **Version 4.** TWR's named outlaws: `Name`, `Faction`, `Status` (`alive`, `succeeded`: dead or jailed and one of the gang runs it now, `dead`, `jailed`, `joined`: the player's now), `Successor` (who runs the gang now, or empty), `Mood` (`raiding`, `lying-low`, `gathering`, `wounded`; empty when nobody runs it), `Bounty` (cats, grown while raiding), `Regions` (home BIOME_GROUP ids, comma-separated), `HasCamp` / `CampX` / `CampZ` (a live camp, with "Moving outlaw camps" on), `Known` (the squad has heard of them) |
| `string OutlawAt(float x, float z, float within = 2000)` | **Version 4.** The outlaw (or successor) whose ground a map point is on or nearest, within `within` world units; empty when none |
| `bool HideoutStruck(string name, string by, bool bloodied, string source)` | **Version 4.** Your band struck an outlaw's camp: the outlaw lies low (or licks its wounds), as after the squad's blows, and no reprisal comes after the squad. False for an unknown or dead outlaw |
| `string TownNode(string town)` | **Version 4.** A town's stable id `town:<InstanceId>` (unchanged when a town is taken over and renamed), from its name; empty when unknown |

## Ideas it's meant for
- A bank or a debt: on a missed payment, `SendHunters(town, "unpaid loan")` sends a crew now; `AddHeat(town, 24, "unpaid loan")` makes
  the town hunted, and it hires crews at its own pace. `Forgive(faction, 24, "debt paid")` cools it off.
- Named leaders of your own: `LeaveAlone(name, faction, "your.mod")` when you name one, so TWR never turns it into a rival.
- A war or an economy mod: `Report("town-fell", "Okran's Shield", "United Cities", "", 80, "dustlife")` and the towns near it talk
  about it; the squad hears it when it comes within reach (History, the Atlas's News section, Recap).
- A faction that grows or rebuilds: `Note(...)` and `Moment(...)` put it in the player's story; `HuntUnderway()` and `SendsHunters` keep
  two mods from sending hunters at once.
- Anything that wants to react to the player's reputation: `HeatTier(town)`, `SquadTitles()`.

Questions or requests for more: ask in The Wasteland Remembers' thread on the Meitou Discord.
