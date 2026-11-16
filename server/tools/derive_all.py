"""Rebuild every game-derived config file from the user's own Kenshi install, honouring their real load order.

  python derive_all.py "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Kenshi"

Finds data\\gamedata.base, Newwworld.mod, Dialogue.mod and rebirth.mod, then every mod listed in data\\mods.cfg
(looked up in <Kenshi>\\mods\\<name>\\ and in the Steam Workshop folder next to the game), merges them in order and
writes: config/world_rules.json, config/contraband.json, config/faction_graph.json, config/item_values.json.
Run it again after changing mods. The outputs hold ids, names and numbers only; they come from files that belong to
Kenshi's makers, so keep them out of public releases and let each player generate their own.
"""
import glob
import os
import sys

import derive_contraband
import derive_factions
import derive_items
import derive_world_rules

BASE_FILES = ["gamedata.base", "Newwworld.mod", "Dialogue.mod", "rebirth.mod"]


def _workshop_dirs(kenshi_dir):
    steamapps = os.path.dirname(os.path.dirname(os.path.abspath(kenshi_dir)))
    return [os.path.join(steamapps, "workshop", "content", "233860")]


def find_mod(kenshi_dir, filename):
    stem = os.path.splitext(filename)[0]
    cands = [os.path.join(kenshi_dir, "mods", stem, filename), os.path.join(kenshi_dir, "mods", filename)]
    cands += glob.glob(os.path.join(kenshi_dir, "mods", "*", filename))
    for w in _workshop_dirs(kenshi_dir):
        cands += glob.glob(os.path.join(w, "*", filename))
    for c in cands:
        if os.path.isfile(c):
            return c
    return None


def read_mod_list(kenshi_dir):
    path = os.path.join(kenshi_dir, "data", "mods.cfg")
    if not os.path.isfile(path):
        return []
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        return [ln.strip() for ln in f if ln.strip().lower().endswith(".mod")]


def collect_paths(kenshi_dir):
    """([paths in load order], [warnings])"""
    paths, warnings = [], []
    for f in BASE_FILES:
        p = os.path.join(kenshi_dir, "data", f)
        if os.path.isfile(p):
            paths.append(p)
        else:
            warnings.append(f"missing base file {f}")
    for name in read_mod_list(kenshi_dir):
        if name in BASE_FILES:
            continue
        p = find_mod(kenshi_dir, name)
        if p:
            paths.append(p)
        else:
            warnings.append(f"mod not found (skipped): {name}")
    return paths, warnings


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    kenshi = argv[1]
    paths, warnings = collect_paths(kenshi)
    for w in warnings:
        print("warning:", w)
    if len(paths) < 4:
        print("Need at least the four base files; check the folder path.")
        return 1
    # derive_* modules print and write on their own; each re-reads the same list
    for mod in (derive_world_rules, derive_contraband, derive_factions, derive_items):
        mod.main([mod.__name__] + paths)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
