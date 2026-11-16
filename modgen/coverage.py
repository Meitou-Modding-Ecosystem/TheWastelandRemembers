"""List named (unique) characters in the official game data that no content pack mentions.

  python coverage.py DATA_DIR
"""
import glob
import os
import sys

import gamedb

HERE = os.path.dirname(os.path.abspath(__file__))


def main():
    db = gamedb.GameDB(sys.argv[1], masters_only=True)
    text = ""
    for p in glob.glob(os.path.join(HERE, "content", "[!_]*.json")):
        text += open(p, encoding="utf-8").read().lower()
    seen, missing = set(), []
    for r in db.recs.values() if isinstance(db.recs, dict) else db.recs:
        if r["type"] != 1 or not r["bool"].get("unique") or r["name"] in seen:
            continue
        seen.add(r["name"])
        if r["name"].lower() not in text:
            missing.append(r["name"])
    print(f"{len(seen)} unique characters, {len(missing)} not mentioned by any pack:")
    for n in sorted(missing):
        print(" ", n)


if __name__ == "__main__":
    main()
