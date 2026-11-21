#!/usr/bin/env bash
# RefCheck's assembly-version check, on a real DLL: Squad Record built against a newer release references that release's engine version,
# which .NET will not bind to an older release even where every member matches (RivalryCore#121: the mod is refused at load). Checked
# against the older release it must fail with TOO NEW; against the release it was built on it must pass. The name-only check passed both.
#   CACHE=<folder of release DLL folders> modgen/meitou/RefCheck/check_versions.sh [newer] [older]
set -u
HERE="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
CACHE="${CACHE:-/tmp/meitou-releases}"
NEW="${1:-0.22.1}" OLD="${2:-0.22.0}"
for v in "$NEW" "$OLD"; do [ -f "$CACHE/$v/MeitouClient.dll" ] || { echo "SKIP: no Meitou $v DLLs in $CACHE"; exit 0; }; done
OUT="$(mktemp -d)"
TFM=net10.0; strings "$CACHE/$NEW/MeitouClient.dll" | grep -q "NETCoreApp,Version=v11" && TFM=net11.0
"$DOTNET" build "$HERE/SquadRecord/SquadRecord.csproj" -c Release --no-incremental -p:MeitouDir="$CACHE/$NEW" -p:MeitouTfm=$TFM \
  -o "$OUT/sr" >"$OUT/build.log" 2>&1 || { grep -E ": error" "$OUT/build.log" | head -5; echo "FAIL: Squad Record does not build on $NEW"; exit 1; }
check() { "$DOTNET" run --project "$HERE/RefCheck/RefCheck.csproj" -c Release -- "$OUT/sr/SquadRecord.dll" "$CACHE/$1" >"$OUT/$1.log" 2>&1; }
fail=0
if check "$OLD"; then echo "FAIL: a DLL built on $NEW passed against $OLD"; fail=1
elif ! grep -q "TOO NEW" "$OUT/$OLD.log"; then echo "FAIL: it failed against $OLD, but not for its engine version:"; grep MISSING "$OUT/$OLD.log" | head -3; fail=1
else echo "   built on $NEW, checked on $OLD: refused ($(grep -c 'TOO NEW' "$OUT/$OLD.log") engine assemblies too new)"; fi
if check "$NEW"; then echo "   built on $NEW, checked on $NEW: passes"; else echo "FAIL: it fails against the release it was built on:"; tail -3 "$OUT/$NEW.log"; fail=1; fi
rm -rf "$OUT"
exit $fail
