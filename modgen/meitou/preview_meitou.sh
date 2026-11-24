#!/usr/bin/env bash
# Builds Meitou from the head of its main branch into the release cache, as a "preview" release, so release_check.sh and the world tests can
# run against hooks merged but not yet in a nightly (2026-11-24: the character lifecycle observers, VlcekM/MeitouClient#1370). Prints the
# tag to pass to release_check.sh. A preview is a source build (engine assemblies stamped 1.0.0.0), not a release: check the next nightly
# again when it ships.
#   CACHE=<release cache> modgen/meitou/preview_meitou.sh        then  modgen/meitou/release_check.sh <printed tag>
set -eu
DOTNET="${DOTNET:-dotnet}"
CACHE="${CACHE:-${TMPDIR:-/tmp}/meitou-releases}"
REPO=https://github.com/VlcekM/MeitouClient
SRC="$CACHE/meitou-source"
# the version the newest nightly leads to (a nightly tag is v<version>-nightly.<date>)
VER=$(git ls-remote --tags "$REPO" | grep -oE 'refs/tags/v[0-9]+\.[0-9]+\.[0-9]+-nightly' | sed 's|refs/tags/v||; s|-nightly||' | sort -V | tail -1)
if [ ! -d "$SRC/.git" ]; then
  GIT_LFS_SKIP_SMUDGE=1 git clone -q --depth 1 --filter=blob:none --sparse "$REPO" "$SRC"
  (cd "$SRC" && GIT_LFS_SKIP_SMUDGE=1 git sparse-checkout set src assets/branding docs)
else
  (cd "$SRC" && GIT_LFS_SKIP_SMUDGE=1 git fetch -q --depth 1 origin HEAD && git reset -q --hard FETCH_HEAD)
fi
STAMP=$(cd "$SRC" && git log -1 --format=%cd --date=format:%Y%m%d)
HEAD=$(cd "$SRC" && git rev-parse --short HEAD)
OUT="$CACHE/build-$HEAD"
"$DOTNET" build "$SRC/src/Meitou.Game/Meitou.Game.csproj" -c Release -o "$OUT" >"$CACHE/preview-build.log" 2>&1 \
  || { grep -E " error " "$CACHE/preview-build.log" | sort -u | head -5; echo "FAIL: Meitou $HEAD does not build"; exit 1; }
TAG="$VER-preview.master$STAMP"
DIR="$CACHE/$TAG"
mkdir -p "$DIR"
for f in MeitouClient.dll Meitou.Simulation.dll Meitou.Core.dll Meitou.Data.dll 0Harmony.dll Silk.NET.Core.dll Silk.NET.Maths.dll \
         BepuPhysics.dll BepuUtilities.dll; do
  [ -f "$OUT/$f" ] && cp "$OUT/$f" "$DIR/"
done
rm -rf "$OUT"
echo "Meitou $HEAD ($STAMP) built as v$TAG"
echo "next: modgen/meitou/release_check.sh v$TAG"
