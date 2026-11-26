# PerfBench: TWR's performance benchmark

Written by Codex for roadmap item 1.0 (the performance hotfix, build bo). Headless: synthetic Meitou worlds, no renderer, no save.
Times are per pass or per tick, so they don't predict frames per second; compare builds against each other on the same machine.

## Run
```bash
export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet DOTNET_TieredCompilation=0
ENGINE=/tmp/meitou-releases/0.23.2                       # a Meitou release folder (release_check.sh fills the cache)
TWR=modgen/release/stage/built/LivingWasteland          # a built mod folder (make_release.py), or one unzipped from a release
dotnet build modgen/meitou/PerfBench/PerfBench.csproj -c Release -p:Engine="$ENGINE" -o /tmp/perfbench
dotnet /tmp/perfbench/PerfBench.dll "$ENGINE" "$TWR" bench          # small isolated operations (criers, panels)
dotnet /tmp/perfbench/PerfBench.dll "$ENGINE" "$TWR" details        # developed Atlas, squad sample with panels, content digests
dotnet /tmp/perfbench/PerfBench.dll "$ENGINE" "$TWR" profile-full   # 1,500 characters, 100 squads, per-system tick times
dotnet /tmp/perfbench/PerfBench.dll "$ENGINE" "$TWR" busy           # eight outposts, an ongoing defence, custody, developed Atlas and log
dotnet /tmp/perfbench/PerfBench.dll "$ENGINE" "$TWR" busy-budget    # guard against repeated outpost visitor scans (allocation ceiling)
```
Run old and new builds alternately, several times each: the first case after a cold start is slower.
`held` and `held-transitions` are for Codex's Meitou engine patch (05) and need an engine built from source.

## Baseline: build bo on Meitou 0.23.2 (Codex's runs, 2026-11-25)
| Case | Time | Allocations |
|---|---:|---:|
| Crier scan, 2,000 NPCs, 24 squadmates | 56 µs/pass | 1,384 B/pass |
| Developed Atlas, full publication (64 towns, 320 people) | 325 to 372 µs/pass | 348,880 B/pass |
| Developed Atlas, existing pages reused (Lighter checks) | 142 to 163 µs/pass | 191,416 B/pass |
| Squad sample plus both panels, 1,500 characters, 30 squadmates | 65 to 69 µs/pass | 80,062 B/pass |
| Ordinary squad sample | 8 µs/pass | 5,472 B/pass |
| Whole world, 1,500 characters, 100 squads (`profile-full`) | 240 µs/tick, p99 489 | 76,062 B/tick |

bm, before the fixes: crier 119 µs, Atlas 1,170 µs, squad sample with panels 72 µs.
**A new build regresses if any case is clearly slower than this (more than about 20%, after repeats) or allocates more per pass.**
The content digests (`details`) must match between builds unless the change was meant to alter what the Atlas shows.

## Tier 1 lifecycle checkpoint (2026-11-25, Meitou 0.23.2)

Public beta plus patches 01–04 compared with that same source plus 06–08. The native-dispatch preview (05) has its own benchmark. Three alternating repeats used the same BN data and Meitou runtime.

| Case | Through 04, median µs | Lifecycle, median µs | Bytes per pass or tick |
|---|---:|---:|---:|
| Crier pass, 2,000 NPCs | 56.44 | 56.13 | 1,384 both |
| Full Atlas publication | 334.76 | 331.88 | 348,880 both |
| Atlas reusing existing details | 145.77 | 144.16 | 191,416 both |
| Squad sample and panels | 67.46 | 62.87 | 80,062 both |
| Ordinary squad sample | 8.06 | 7.75 | 5,472 both |
| Disabled panel snapshot, including reflection | 0.05 | 0.03 | 112 before, 32 after |
| Whole synthetic world tick | 250.16 | 241.18 | 76,062 both |

The content digests match. Whole-world p99 was 522.30 µs before and 515.90 µs after. The checkpoint clears the timing and allocation gate. The scenario measures the normal crowded-world tick. It does not model sustained NPC removals or large fights. Small differences in the timings should be interpreted in light of repeat-to-repeat variation.

## Build bp follow-up checkpoint (2026-11-25, Meitou 0.23.2)

Landed bp and patches 10–14 use the same bp Workshop data and 0.23.2 release assemblies. Medians from three alternating runs.

| Case | Landed bp, µs | With follow-up, µs | Bytes per pass or tick |
|---|---:|---:|---:|
| Lookout scan, 2,000 NPCs | 26.27 | 27.05 | 3,512 both |
| Crier scan, 2,000 NPCs | 55.01 | 55.40 | 1,384 both |
| Full Atlas publication | 335.05 | 326.14 | 348,880 both |
| Atlas reusing existing details | 145.91 | 139.28 | 191,416 both |
| Squad sample and both panels | 74.16 | 63.11 | 80,062 both |
| Ordinary squad sample | 8.05 | 7.78 | 5,472 both |
| Whole synthetic world tick | 242.59 | 244.69 | 76,062 both |

The three display digests match in every run. Whole-world p99 was 510.10 microseconds before and 496.10 after. The separate initial veto system added 3 bytes per tick in this fixture. Sharing the existing platoon-fates system brought the count back to the bp baseline, with 29 mod systems on both sides. The shared system's measured phase time was about 0.13 microseconds per tick.

Run `dotnet /tmp/perfbench/PerfBench.dll "$ENGINE" "$TWR" retention` for the separate removal callback check. This exercises Meitou's orphan-veto dispatcher with an unrelated party, a live tracked crew and the same crew after expiry. The respective medians were 0.03, 0.08 and 0.04 microseconds per call, with zero bytes allocated in each case.

## Kindness deeds and conversation news checkpoint (2026-11-26, Meitou 0.23.2)

Patches 15 to 17 versus the bp follow-up, three alternating runs with the same Workshop data.

| Case | Before, µs | After, µs | Bytes per pass or tick |
|---|---:|---:|---:|
| Lookout scan, 2,000 NPCs | 26.76 | 26.42 | 3,512 both |
| Crier scan, 2,000 NPCs | 56.44 | 55.42 | 1,384 both |
| Full Atlas publication | 346.62 | 336.66 | 348,880 both |
| Atlas reusing existing details | 132.35 | 141.57 | 191,416 both |
| Squad sample and both panels | 66.57 | 69.77 | 80,062 both |
| Ordinary squad sample | 7.78 | 8.08 | 5,472 both |
| Whole synthetic world tick | 247.52 | 241.83 | 76,062 both |

The display digests match and both sides registered 29 systems. The diagnostics text export naturally varies slightly with its text and timestamps. The crowded-market fixture has idle kindness and no conversation clicks.

## Active kindness checkpoint (2026-11-26, Meitou 0.23.2)

Earlier conversation-news checkpoint against the Atlas and reunion work. Three alternating runs. An initial run with concurrent builds showed short-case timing noise, so this comparison ran without builds beside it.

| Case | Before, µs | After, µs | Bytes per pass or tick |
|---|---:|---:|---:|
| Lookout scan, 2,000 NPCs | 26.36 | 26.91 | 3,512 both |
| Crier scan, 2,000 NPCs | 54.37 | 54.38 | 1,384 both |
| Full Atlas publication | 319.35 | 328.30 | 348,880 both |
| Atlas reusing existing details | 132.16 | 135.94 | 191,416 both |
| Squad sample and both panels | 65.29 | 63.67 | 80,062 both |
| Ordinary squad sample | 7.70 | 8.07 | 5,472 both |
| Whole synthetic world tick | 242.42 | 249.40 | 76,062 both |

All three display digests matched. The small bounded-export byte variation remained within its timestamp and text noise.

Run `dotnet /tmp/perfbench/PerfBench.dll "$ENGINE" "$TWR" kindness` with `kindness_lines.json` in the mod folder. The active fixture has 1,500 non-player characters, six remembered people and 25 squad positions. Looking for homes, checking approaches and checking six ready reunions outside speaking range each allocated zero bytes per pass. Separate medians were 0.48, 0.42 and 0.57 microseconds.

## Task bars, trade and heat checkpoint (2026-11-26, Meitou 0.23.2)

Patches 26 and 33 used five alternating full-world runs. The median changed from 253.34 to 248.61 µs per tick, with 76,061 bytes per tick for both. The quiet market fixture includes no news clicks.

The later comparisons used three alternating runs per helper on the same CPU and the same build bp Workshop data:

| Case | Earlier | With the index | Allocation before → after |
|---|---:|---:|---:|
| Trade quote, 75 title rules | 3.49 µs | 0.64 µs | 344 → 240 B |
| Faction heat query, 100 towns | 12.35 µs | 0.01 µs | 528 → 0 B |
| Heat decay across 100 towns | 5.46 µs | 6.74 µs | 696 → 696 B |

Use the `trade` or `heat` mode in place of `bench`. The quote remains 108 cats. The heat fixture returns a faction echo of 12 and local heat of 24 on both helpers. The trade figures measure one prepared payload through the real helper filter. The tick benchmarks are synthetic and exclude game rendering.


## Busy fixture, build br follow-up

The fixture uses 1,500 loaded actors, eight public outposts, a real campaign still in its combat phase, one confirmed prisoner case
and 395 published Atlas pages. Full publication and reuse show the cost of rebuilding details alongside the shared publication work.
The export writes and atomically replaces a real temporary report after seven synthetic game days of feature events. This is a
reproducible stress sample. It does not measure a player's own developed save or rendering.
