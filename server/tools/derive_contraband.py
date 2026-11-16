"""Derive config/contraband.json (what is illegal in which trade culture) from Kenshi's data files.

Every faction and some towns have a trade culture: the Empire bans hashish, bloodrum and hemp; the Holy Nation bans
hashish, ancient science books, AI cores and a list of heretical books; Shek ban hashish; and so on. This extracts
only the item names, the culture of each faction and town, and which culture lists which items, so NPCs can notice
what the player carries. Usage (from server/tools), files in load order:
  python derive_contraband.py gamedata.base Newwworld.mod Dialogue.mod rebirth.mod "Reactive World.mod"
"""
import json
import os
import sys

import fcs_reader


def derive(paths):
    merged = fcs_reader.merge_files(paths)
    recs = list(merged.values())
    targets = {a[0] for r in recs for a in r["ref"].get("trade culture", [])}
    cultures = {}
    for sid in targets:
        r = merged.get(sid)
        if not r:
            continue
        cultures[sid] = {
            "name": r["name"],
            "illegal": sorted({merged[a[0]]["name"] for a in r["ref"].get("illegal goods", []) if a[0] in merged}),
            "forbidden": sorted({merged[a[0]]["name"] for a in r["ref"].get("forbidden items", []) if a[0] in merged}),
            "illegal_buildings": sorted({merged[a[0]]["name"] for a in r["ref"].get("illegal buildings", []) if a[0] in merged}),
        }
    factions, towns = {}, {}
    for r in recs:
        refs = r["ref"].get("trade culture")
        if not refs or refs[0][0] not in cultures:
            continue
        cname = cultures[refs[0][0]]["name"]
        if "(override" in r["name"]:
            continue
        # a town record has a 'faction' reference; a faction record does not
        if "faction" in r["ref"]:
            towns[r["name"].lower()] = cname
        else:
            factions[r["name"].lower()] = cname
    return {"cultures": {v["name"]: {k: v[k] for k in ("illegal", "forbidden", "illegal_buildings")} for v in cultures.values()},
            "factions": factions, "towns": towns}


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    out = derive(argv[1:])
    out = {"_readme": "Derived by tools/derive_contraband.py from the user's own game data: item names only.", **out}
    dest = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "config", "contraband.json")
    with open(dest, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1, ensure_ascii=False)
    print(f"wrote {dest}: {len(out['cultures'])} cultures, {len(out['factions'])} factions, {len(out['towns'])} towns")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
