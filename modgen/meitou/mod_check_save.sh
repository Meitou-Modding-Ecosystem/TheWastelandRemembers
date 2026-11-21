#!/usr/bin/env bash
# Compare a real Meitou save twice on one thread and once with several workers.
set -euo pipefail
if [[ $# -lt 2 ]]; then
  echo "Usage: MEITOU_TOOLS=/path/to/meitou-tools $0 <save folder> <enabled TWR mod folder> [--ticks N] [--threads N] [--mod <other mod folder>]..." >&2
  exit 2
fi
save=$1
mod=$2
shift 2
if [[ ! -d "$save" || ! -f "$mod/meitou.json" ]]; then
  echo 'Provide a save folder and a built TWR mod folder containing meitou.json.' >&2
  exit 2
fi
tool=${MEITOU_TOOLS:-meitou-tools}
if [[ "$tool" == *.dll ]]; then
  exec "${DOTNET:-dotnet}" "$tool" mod-check "$save" --mod "$mod" "$@"
fi
exec "$tool" mod-check "$save" --mod "$mod" "$@"
