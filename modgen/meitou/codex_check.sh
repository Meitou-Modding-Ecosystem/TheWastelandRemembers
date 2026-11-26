#!/usr/bin/env bash
# Every check this repository runs, offline, for an assistant working from a snapshot (CODEX_BRIEF.md section 4):
#   1. builds the mod DLL against Meitou 0.17.3 (the oldest supported release) into LivingWastelandMeitou/, as a release does;
#   2. release_check.sh against each bundled release: both mods compile, every setting loads, RefCheck on the built DLLs (members and
#      engine assembly versions; RefCheck/check_versions.sh proves the version check on a real DLL), unit tests,
#      world tests (the real Rivalry Core included);
#   3. the Python tests of the data compiler;
#   4. the text checks on content/*.json: valid JSON, detrope.py --check and contractions.py --check (both REWRITE every content file
#      without --check: never run them bare).
#
#   modgen/meitou/codex_check.sh                    # bundled releases (meitou-releases/ beside the snapshot's modgen folder)
#   CACHE=<dir> modgen/meitou/codex_check.sh v0.22.1 # another folder of release DLLs, or given tags (downloads what's missing)
#
# Needs the .NET 10 and 11 SDKs (dotnet on PATH, or DOTNET=<path>), python3, unzip and curl (only for downloads). Building the .mod data
# file needs the game's own files, which the snapshot doesn't have; content.py and build_mod.py can't be run here.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
MODGEN="$(cd "$HERE/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export CACHE="${CACHE:-$(cd "$MODGEN/.." && pwd)/meitou-releases}"
export DOTNET DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
tags=("$@")
[ ${#tags[@]} -gt 0 ] || tags=(v0.22.1 v0.17.3)
fail=0

# Each world-test run checks both directions against the actual previous Steam DLL when it is in the release cache.
# A provided path or a release check requiring rollback must fail visibly if the file is missing.
export TWR_PREVIOUS_STEAM_BUILD="${TWR_PREVIOUS_STEAM_BUILD:-20261125bo}"
rollback_candidate="${TWR_PREVIOUS_STEAM_DLL:-$CACHE/twr-steam-$TWR_PREVIOUS_STEAM_BUILD/LivingWastelandMeitou.dll}"
if [ -f "$rollback_candidate" ]; then
  export TWR_PREVIOUS_STEAM_DLL="$rollback_candidate"
  echo "== Save rollback: helper $TWR_PREVIOUS_STEAM_BUILD ($rollback_candidate)"
  echo "   SHA-256: $(sha256sum "$rollback_candidate" | cut -d' ' -f1)"
elif [ -n "${TWR_PREVIOUS_STEAM_DLL:-}" ] || [ "${TWR_REQUIRE_ROLLBACK:-0}" = 1 ]; then
  echo "FAIL: save rollback requires $rollback_candidate"
  exit 2
else
  echo "== Save rollback: previous Steam DLL unavailable at $rollback_candidate"
  echo "   Running current save fixtures only. Use TWR_REQUIRE_ROLLBACK=1 for a release."
fi

echo "== 1. the mod DLL, built against Meitou 0.17.3"
if [ ! -f "$CACHE/0.17.3/MeitouClient.dll" ]; then echo "FAIL: no Meitou 0.17.3 DLLs in $CACHE/0.17.3"; exit 2; fi
OUT="$(mktemp -d)"
for p in LivingWastelandMeitou SquadRecord; do                        # the settings check loads both built DLLs
  if "$DOTNET" build "$HERE/$p/$p.csproj" -c Release --no-incremental -p:MeitouDir="$CACHE/0.17.3" -o "$OUT/$p" >"$OUT/$p.log" 2>&1; then
    cp "$OUT/$p/$p.dll" "$HERE/$p/$p.dll"
    echo "   $p built ($(grep -cE ': warning CS' "$OUT/$p.log") warning lines)"
  else
    grep -E ": error CS" "$OUT/$p.log" | sort -u | head -20; echo "FAIL: $p does not build"; exit 1
  fi
done
rm -rf "$HERE/SettingsCheck/bin" "$HERE/SettingsCheck/obj"      # it copies the DLLs at build time: an earlier build can lack them

echo "== 2. release checks"
for t in "${tags[@]}"; do
  "$HERE/release_check.sh" "$t" || fail=1
done

echo "== 2b. RefCheck's engine-version check on a real DLL (RefCheck/check_versions.sh)"
"$HERE/RefCheck/check_versions.sh" || fail=1

echo "== 3. data compiler, packaging and log collector tests (python, no game files needed)"
if (cd "$MODGEN" && python3 -m unittest -q test_meitou_target test_meitou_check test_make_release test_save_logs >"$OUT/py.log" 2>&1); then echo "   passed"; else tail -15 "$OUT/py.log"; fail=1; fi

echo "== 4. text checks (content/*.json)"
bad=0
for f in "$MODGEN"/content/*.json; do
  python3 -c "import json,sys; json.load(open(sys.argv[1], encoding='utf-8'))" "$f" 2>/dev/null || { echo "   not valid JSON: $(basename "$f")"; bad=1; }
done
[ $bad -eq 0 ] && echo "   every content file is valid JSON" || fail=1
echo "   detrope.py --check (trailing punchlines; a share, not a pass/fail):"
(cd "$MODGEN" && python3 detrope.py --check | tail -1 | sed 's/^/      /')
echo "   contractions.py --check (lines a voice would contract; new lines should add none):"
(cd "$MODGEN" && python3 contractions.py --check | sed 's/^/      /' | head -12)

[ $fail -eq 0 ] && echo "ALL PASS (a build and tests, not a play test)" || echo "FAIL: see above"
exit $fail
