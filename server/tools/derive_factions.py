"""Derive config/faction_graph.json (faction traits and relations) from Kenshi's data files.

For every faction: trustworthiness (-100 to 100: does it keep its word), how readily its squads flee, whether it
heals strangers, whether it posts bounties, its default relation to outsiders, and its relations to other factions
(allies at 50 or more, enemies at -50 or less, and factions it merely coexists with). Numbers and names only.
Usage (from server/tools), files in load order:
  python derive_factions.py gamedata.base Newwworld.mod Dialogue.mod rebirth.mod "Reactive World.mod"
"""
import json
import os
import sys

import fcs_reader

SKIP = ("DCR", "zSpawner")
SKIP_NAMES = {"generation template", "No Faction", "FACTION", "Spawners", "Character Spawner"}


def derive(paths):
    merged = fcs_reader.merge_files(paths)
    recs = list(merged.values())

    def name_of(sid):
        r = merged.get(sid)
        return r["name"] if r else sid

    out = {}
    for r in recs:
        if not ("relations" in r["ref"] or "coexistence" in r["ref"] or "trustworthy" in r["float"]):
            continue
        if "residents" not in r["ref"] and "squads" not in r["ref"] and "trustworthy" not in r["float"]:
            continue
        n = r["name"]
        if n.startswith(SKIP) or n in SKIP_NAMES or "relations" not in r["ref"] and "trustworthy" not in r["float"]:
            continue
        rel = [(name_of(a[0]), a[1]) for a in r["ref"].get("relations", [])]
        out[n.lower()] = {
            "name": n,
            "trust": r["float"].get("trustworthy", 0.0),
            "flee_squad": round(r["float"].get("run away ratio of squad size", 0.0), 2),
            "flee_enemy": round(r["float"].get("run away ratio relative to enemy", 0.0), 2),
            "heals_strangers": bool(r["bool"].get("heals strangers")),
            "offers_bounties": bool(r["bool"].get("offers bounties")),
            "default_relation": r["int"].get("default relation", 0),
            "allies": sorted(n2 for n2, v in rel if v >= 50),
            "enemies": sorted(n2 for n2, v in rel if v <= -50),
            "coexist": sorted(name_of(a[0]) for a in r["ref"].get("coexistence", [])),
        }
    return out


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    facs = derive(argv[1:])
    dest = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "config", "faction_graph.json")
    with open(dest, "w", encoding="utf-8") as f:
        json.dump({"_readme": "Derived by tools/derive_factions.py from the user's own game data: numbers and faction names only.",
                   "factions": facs}, f, indent=1, ensure_ascii=False)
    print(f"wrote {dest}: {len(facs)} factions")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
