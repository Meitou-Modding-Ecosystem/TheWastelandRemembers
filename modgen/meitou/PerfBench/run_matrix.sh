#!/usr/bin/env bash
set -euo pipefail
if [ "$#" -ne 1 ]; then
  echo "Usage: CACHE=<release cache> bash run_matrix.sh <built TWR mod folder>" >&2
  exit 2
fi

here="$(cd "$(dirname "$0")" && pwd)"
cache="${CACHE:-${TMPDIR:-/tmp}/meitou-releases}"
dotnet="${DOTNET:-dotnet}"
mod="$(cd "$1" && pwd)"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
cp "$here"/*.cs "$here/PerfBench.csproj" "$scratch/"
export DOTNET_TieredCompilation=0

for version in 0.24.0 0.23.2 0.17.3; do
  engine="$cache/$version"
  if [ ! -f "$engine/MeitouClient.dll" ]; then
    echo "Missing Meitou $version in $cache. Prepare it with codex_check.sh." >&2
    exit 2
  fi
  tfm=net11.0
  [ "$version" != 0.17.3 ] || tfm=net10.0
  echo "== PerfBench Meitou $version"
  "$dotnet" build "$scratch/PerfBench.csproj" -c Release -p:Engine="$engine" -p:MeitouTfm="$tfm" -o "$scratch/$version"
  "$dotnet" "$scratch/$version/PerfBench.dll" "$engine" "$mod" profile-full
  if [ "$version" != 0.17.3 ]; then
    "$dotnet" "$scratch/$version/PerfBench.dll" "$engine" "$mod" busy
  fi
done
