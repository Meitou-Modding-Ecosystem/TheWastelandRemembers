#!/usr/bin/env bash
# Is a Meitou release safe for our code mods? Builds both mods against it, loads our shipped DLLs through the settings check, runs the
# unit tests and the world tests (the mod's systems in synthetic worlds, against that release). For sessions (needs the .NET 10 and 11 SDKs on PATH, or DOTNET=<path to dotnet>); the user does not run this.
#
#   modgen/meitou/release_check.sh                 # the newest full release
#   modgen/meitou/release_check.sh v0.17.5         # a given tag (nightlies too: v0.17.5-nightly.20261112)
#   CACHE=/some/dir modgen/meitou/release_check.sh # where release DLLs are kept (default: a temp folder)
#
# Exit 0 and "PASS" when everything held. The shipped DLLs are not changed (builds go to a scratch output folder).
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
DOTNET="${DOTNET:-dotnet}"
CACHE="${CACHE:-${TMPDIR:-/tmp}/meitou-releases}"
REPO=https://github.com/VlcekM/MeitouClient
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

TAG="${1:-}"
if [ -z "$TAG" ]; then
  TAG=$(git ls-remote --tags "$REPO" | grep -oE 'refs/tags/v[0-9]+\.[0-9]+\.[0-9]+$' | sed 's|refs/tags/||' | sort -V | tail -1)
fi
[ -n "$TAG" ] || { echo "could not find a release tag"; exit 2; }
VER="${TAG#v}"
DIR="$CACHE/$VER"
echo "== Meitou $TAG"

if [ ! -f "$DIR/MeitouClient.dll" ]; then
  mkdir -p "$DIR"
  if ! curl -fsSL -o "$DIR/m.zip" "$REPO/releases/download/$TAG/Meitou-$VER-win-x64.zip"; then
    echo "FAIL: no win-x64 zip for $TAG"; exit 2
  fi
  unzip -qjo "$DIR/m.zip" '*/MeitouClient.dll' '*/Meitou.Simulation.dll' '*/Meitou.Core.dll' '*/Meitou.Data.dll' '*/0Harmony.dll' -d "$DIR" || { echo "FAIL: unexpected zip layout"; exit 2; }
  rm -f "$DIR/m.zip"
fi

# The release's own target framework decides how we compile against it (a .NET 11 release's DLLs need net11.0 to compile against).
TFM=net10.0
if strings "$DIR/MeitouClient.dll" | grep -q "NETCoreApp,Version=v11"; then TFM=net11.0; fi
echo "   its runtime: $TFM"
# the manifests' gameVersion range must let this release load (Meitou refuses a mod outside it, whatever its code)
for m in LivingWastelandMeitou SquadRecord; do
  range=$(python3 -c "import json,sys;print(json.load(open(sys.argv[1])).get('gameVersion',''))" "$HERE/$m/meitou.json")
  if ! python3 - "$range" "${VER%%-*}" <<'PY'
import sys, re
rng, ver = sys.argv[1], tuple(int(x) for x in sys.argv[2].split('.'))
ok = True
for op, v in re.findall(r'(>=|<=|<|>|=)\s*([0-9.]+)', rng):
    v = tuple(int(x) for x in v.split('.'))
    ok &= {'>=': ver >= v, '<=': ver <= v, '<': ver < v, '>': ver > v, '=': ver == v}[op]
sys.exit(0 if ok else 1)
PY
  then echo "FAIL: $m's meitou.json gameVersion \"$range\" does not allow $VER"; exit 1; fi
  echo "   $m manifest: \"$range\" allows $VER"
done

fail=0
OUT="$(mktemp -d)"
for p in LivingWastelandMeitou SquadRecord; do
  log="$OUT/$p.log"
  "$DOTNET" build "$HERE/$p/$p.csproj" -c Release --no-incremental -p:MeitouDir="$DIR" -p:MeitouTfm="$TFM" -o "$OUT/$p" >"$log" 2>&1
  errs=$(grep -cE ": error CS" "$log"); warns=$(grep -E ": warning CS" "$log" | sort -u | wc -l)
  if [ "$errs" -gt 0 ] || ! grep -q "Build succeeded" "$log"; then
    echo "   $p: does NOT compile against $TAG:"; grep -oE "[A-Za-z]+\.cs\([0-9,]+\): error [A-Z0-9]+: [^[]+" "$log" | sort -u | head -10 | sed 's/^/      /'
    fail=1
  else
    echo "   $p: compiles ($warns warnings)"
  fi
done

# Our shipped DLLs (built against the oldest supported release) loaded the way the game loads them; settings built as AddSettings does.
if [ "$TFM" = net11.0 ]; then
  if "$DOTNET" run --project "$HERE/SettingsCheck/SettingsCheck.csproj" -c Release -p:MeitouDir="$DIR" >"$OUT/settings.log" 2>&1; then
    sed -n 's/^/   settings: /p' "$OUT/settings.log" | grep -E "OK|THROWS"
  else
    echo "   settings check FAILED:"; grep -E "THROWS|rror" "$OUT/settings.log" | head -5 | sed 's/^/      /'; fail=1
  fi
else
  echo "   settings check skipped (it runs on net11.0 releases)"
fi

if "$DOTNET" test "$HERE/LivingWastelandMeitou.Tests/LivingWastelandMeitou.Tests.csproj" -c Release >"$OUT/tests.log" 2>&1; then
  grep -oE "Passed: +[0-9]+" "$OUT/tests.log" | head -1 | sed 's/^/   unit tests: /'
else
  echo "   unit tests FAILED"; grep -E "Failed |error" "$OUT/tests.log" | head -5 | sed 's/^/      /'; fail=1
fi
# the real Rivalry Core for the party-claim world tests (RealRivalryCoreWorldTests): a pinned, published MeitouMods build, checked against
# its SHA-256. It targets .NET 11, so only net11.0 releases run them; a failed download skips them with a note, it never fails the check.
RIVALRY_TAG=rivalry-core-v0.6.0
RIVALRY_ZIP=RivalryCore-0.6.0.zip
RIVALRY_SHA=f2e90568678b6a8803d78ac3f254c172e024a2ebecbbc15ae1c157aaa4ebabd4
unset RIVALRY_CORE_DIR
if [ "$TFM" = net11.0 ]; then
  RDIR="$CACHE/$RIVALRY_TAG"
  if [ ! -f "$RDIR/RivalryCore/RivalryCore.dll" ]; then
    mkdir -p "$RDIR"
    if curl -fsSL -o "$RDIR/$RIVALRY_ZIP" "https://github.com/Meitou-Modding-Ecosystem/MeitouMods/releases/download/$RIVALRY_TAG/$RIVALRY_ZIP" \
      && echo "$RIVALRY_SHA  $RDIR/$RIVALRY_ZIP" | sha256sum -c --quiet - 2>/dev/null; then
      unzip -qo "$RDIR/$RIVALRY_ZIP" -d "$RDIR"
    else
      rm -rf "$RDIR"
    fi
  fi
  if [ -f "$RDIR/RivalryCore/RivalryCore.dll" ]; then
    export RIVALRY_CORE_DIR="$RDIR/RivalryCore"; echo "   Rivalry Core: $RIVALRY_TAG (checksum verified) for the party-claim world tests"
  else
    echo "   Rivalry Core: $RIVALRY_TAG not available (download or checksum failed); its world tests skipped"
  fi
fi
# the world tests: the mod's engine-side systems in small synthetic worlds, against this release's own DLLs
if "$DOTNET" test "$HERE/LivingWastelandMeitou.WorldTests/LivingWastelandMeitou.WorldTests.csproj" -c Release -p:MeitouDir="$DIR" -p:MeitouTfm="$TFM" >"$OUT/worldtests.log" 2>&1; then
  grep -oE "Passed: +[0-9]+" "$OUT/worldtests.log" | head -1 | sed 's/^/   world tests: /'
else
  echo "   world tests FAILED"; grep -E "\[FAIL\]|Failed |error" "$OUT/worldtests.log" | head -5 | sed 's/^/      /'; fail=1
fi

rm -rf "$OUT"
if [ $fail -eq 0 ]; then echo "PASS: $TAG is safe for our mods (as far as building and loading tell; not a play test)"; else echo "FAIL: see above"; fi
exit $fail
