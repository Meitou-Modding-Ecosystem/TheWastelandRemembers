#!/usr/bin/env bash
# Run after quitting Meitou. Optional arguments are additional Meitou data or install folders.
set -euo pipefail

if ! command -v zip >/dev/null; then
  printf '%s\n' 'Install the zip command, then run this script again.' >&2
  exit 1
fi

scratch=$(mktemp -d "${TMPDIR:-/tmp}/twr-logs.XXXXXXXX")
trap 'rm -rf "$scratch"' EXIT
mkdir "$scratch/files"
copied=0
engine_sources=()
report_sources=()

copy_file() {
  local source=$1 target=$2
  if [[ -f "$source" && ! -L "$source" ]]; then
    mkdir -p "$(dirname "$target")"
    cp "$source" "$target"
    copied=$((copied + 1))
  fi
}

collect_meitou() {
  [[ -d "$1" ]] || return 0
  local root previous target version file
  root=$(cd -P -- "$1" && pwd)
  for previous in "${engine_sources[@]+"${engine_sources[@]}"}"; do
    [[ "$root" != "$previous" ]] || return 0
  done
  # Prefix each installation to preserve multiple different copies of the same version.
  engine_sources+=("$root")
  target="$scratch/files/meitou-${#engine_sources[@]}"
  copy_file "$root/log.txt" "$target/log.txt"
  copy_file "$root/launcher.json" "$target/launcher.json"
  copy_file "$root/meitou.user.json" "$target/meitou.user.json"
  for version in "$root"/versions/*; do
    [[ -d "$version" && ! -L "$version" ]] || continue
    copy_file "$version/log.txt" "$target/versions/${version##*/}/log.txt"
  done
  for file in "$root"/mods/*.settings.txt; do
    copy_file "$file" "$target/mods/${file##*/}"
  done
}

collect_reports() {
  [[ -d "$1" ]] || return 0
  local root previous target file
  root=$(cd -P -- "$1" && pwd)
  for previous in "${report_sources[@]+"${report_sources[@]}"}"; do
    [[ "$root" != "$previous" ]] || return 0
  done
  report_sources+=("$root")
  target="$scratch/files/playtest-${#report_sources[@]}"
  for file in "$root"/TWR-playtest-*.txt "$root"/"Squad chronicle "*.html; do
    if [[ -f "$file" && ! -L "$file" && -n $(find "$file" -mtime -14 -print) ]]; then
      copy_file "$file" "$target/${file##*/}"
    fi
  done
}

collect_prefix() {
  local local_data user
  for user in "$1"/pfx/drive_c/users/*; do
    [[ -d "$user" ]] || continue
    local_data="$user/AppData/Local"
    collect_meitou "$local_data/Meitou"
    collect_reports "$local_data/The Wasteland Remembers"
    collect_reports "$user/Documents/The Wasteland Remembers"
    collect_reports "$user/My Documents/The Wasteland Remembers"
  done
}

collect_steam() {
  local steam=$1 library
  collect_prefix "$steam/steamapps/compatdata/233860"
  [[ -r "$steam/steamapps/libraryfolders.vdf" ]] || return 0
  while IFS= read -r library; do
    collect_prefix "$library/steamapps/compatdata/233860"
  done < <(awk -F '"' '$2 == "path" && $4 ~ /^\// { print $4 }' "$steam/steamapps/libraryfolders.vdf")
}

local_data="${XDG_DATA_HOME:-$HOME/.local/share}"
mac_data="$HOME/Library/Application Support"
collect_meitou "$local_data/Meitou"
collect_meitou "$mac_data/Meitou"
for root in "$@"; do
  collect_meitou "$root"
done

documents="$HOME/Documents"
if command -v xdg-user-dir >/dev/null; then
  documents=$(xdg-user-dir DOCUMENTS 2>/dev/null || printf '%s' "$documents")
fi
collect_reports "${documents:-$HOME/Documents}/The Wasteland Remembers"
collect_reports "$HOME/Documents/The Wasteland Remembers"
collect_reports "$HOME/The Wasteland Remembers"
collect_reports "$local_data/The Wasteland Remembers"
collect_reports "$mac_data/The Wasteland Remembers"

if [[ -n "${STEAM_COMPAT_DATA_PATH:-}" ]]; then
  collect_prefix "$STEAM_COMPAT_DATA_PATH"
fi
for steam in "$local_data/Steam" "$HOME/.steam/steam" "$HOME/.steam/root" "$mac_data/Steam" \
  "$HOME/.var/app/com.valvesoftware.Steam/data/Steam"; do
  collect_steam "$steam"
done

if ((copied == 0)); then
  printf '%s\n' 'No Meitou logs, settings, or recent TWR playtest reports were found.' >&2
  exit 1
fi

destination="$HOME/Desktop"
if [[ ! -d "$destination" || ! -w "$destination" ]]; then
  destination="$HOME"
fi
name="WastelandRemembers_logs_$(date +%Y%m%d_%H%M%S)"
out="$destination/$name.zip"
suffix=1
while [[ -e "$out" || -L "$out" ]]; do
  out="$destination/$name-$suffix.zip"
  suffix=$((suffix + 1))
done
(cd "$scratch/files" && zip -q -r "$scratch/report.zip" .)
mv "$scratch/report.zip" "$out"
printf 'Saved %s\n' "$out"
