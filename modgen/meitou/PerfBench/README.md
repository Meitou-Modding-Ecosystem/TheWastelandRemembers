# PerfBench: TWR's performance benchmark

Written by Codex for roadmap item 1.0 (the performance hotfix, build bo). Headless: synthetic Meitou worlds, no renderer, no save.
Times are per pass or per tick, so they don't predict frames per second; compare builds against each other on the same machine.

## Run
```bash
export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet DOTNET_TieredCompilation=0
ENGINE=/tmp/meitou-releases/0.23.2                       # a Meitou release folder (release_check.sh fills the cache)
TWR=modgen/release/stage/built/LivingWasteland          # a built mod folder (make_release.py), or one unzipped from a release
dotnet build modgen/meitou/PerfBench/PerfBench.csproj -c Release -p:Engine="$ENGINE" -o /tmp/perfbench
dotnet /tmp/perfbench/Check.dll "$ENGINE" "$TWR" bench          # small isolated operations (criers, panels)
dotnet /tmp/perfbench/Check.dll "$ENGINE" "$TWR" details        # developed Atlas, squad sample with panels, content digests
dotnet /tmp/perfbench/Check.dll "$ENGINE" "$TWR" profile-full   # 1,500 characters, 100 squads, per-system tick times
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
