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

## Version 1

| Member | What it does |
|---|---|
| `int Version` (const) | 1 |
| `string Build` | The mod's build id, e.g. `20261115aj` |
| `bool Ready` | A game is loaded and the mod is running |
| `double Heat(string town)` | How much a town has it in for the player, 0 to 40. 0 when unknown or the Town heat setting is off |
| `string HeatTier(string town)` | `quiet`, `noticed` (6+), `wanted` (14+), `hunted` (24+: the town may hire bounty hunters) |
| `IReadOnlyList<IReadOnlyDictionary<string, object>> HeatTowns()` | Every town with heat: `Town`, `Faction`, `Heat`, `Tier` |
| `bool AddHeat(string town, double amount, string reason)` | Adds heat in a town (its name as the game shows it). Fades like crime heat, half every three game days. Clamped to 0 to 40; 24 makes it hunted. The reason goes to the log |
| `bool Forgive(string faction, double amount, string reason)` | Takes heat off every town of a faction (a pardon, a paid debt) |
| `IReadOnlyList<...> Rivals()` | Rivals the player has been told about: `Name`, `Faction`, `Phase` (`recovering`, `hunting`, `dead`, `jailed`, `lost`), `X`, `Z`, `Returns`, `Wins` |
| `IReadOnlyList<...> Hunts()` | Everyone out hunting the squad now: `Kind` (`bounty crew`, `reprisal`, `rival`), `Name`, `X`, `Z` |
| `bool HuntUnderway()` | Someone sent by this mod is hunting the squad right now |
| `bool SendsHunters` | This mod may send hunters (bounty crews, reprisals or rivals are on). If your mod has hunters of its own, hold back while this is true so the player isn't hunted twice over |
| `IReadOnlyList<string> SquadTitles()` | Title ids held by the squad or any member (e.g. `narko`) |
| `bool Note(string text)` | Writes a line to the squad's History tab |
| `bool Moment(string caption, string place, int weight)` | Sends a moment to BeatTheCount's Recap through this mod's link (weight 0 to 100). False when Recap isn't installed |

## Ideas it's meant for
- A bank or a debt: on a missed payment, `AddHeat(town, 24, "unpaid loan")` and the town hires a bounty crew. `Forgive(faction, 24, "debt paid")` calls it off.
- A faction that grows or rebuilds: `Note(...)` and `Moment(...)` put it in the player's story; `HuntUnderway()` and `SendsHunters` keep
  two mods from sending hunters at once.
- Anything that wants to react to the player's reputation: `HeatTier(town)`, `SquadTitles()`.

Questions or requests for more: ask in The Wasteland Remembers' thread on the Meitou Discord.
