"""Derive config/item_values.json (base value in cats of every item) from Kenshi's data files.

Base values only: the game multiplies them by quality, trade culture and shop mood, so they are a guide to relative
worth (a leviathan pearl is about 20,000 cats, hashish 800, raw meat 100). Usage (from server/tools):
  python derive_items.py gamedata.base Newwworld.mod Dialogue.mod rebirth.mod "Reactive World.mod"
"""
import json
import os
import sys

import fcs_reader


def derive(paths):
    merged = fcs_reader.merge_files(paths)
    out = {}
    for r in merged.values():
        v = r["int"].get("value", r["float"].get("value"))
        if v and v > 0 and r["name"] and "(override" not in r["name"] and len(r["name"]) < 60:
            key = r["name"].strip().lower()
            if key not in out or v > out[key]:
                out[key] = int(v)
    return out


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    vals = derive(argv[1:])
    dest = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "config", "item_values.json")
    with open(dest, "w", encoding="utf-8") as f:
        json.dump({"_readme": "Base item values in cats, derived by tools/derive_items.py from the user's own game data.",
                   "values": vals}, f, indent=0, ensure_ascii=False, sort_keys=True)
    print(f"wrote {dest}: {len(vals)} items")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
