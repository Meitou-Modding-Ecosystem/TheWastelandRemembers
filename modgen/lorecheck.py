"""Search the game's own text (item and character descriptions, dialogue lines, names) for a word or phrase.

  python lorecheck.py DATA_DIR "narko" [more terms ...] [--max 15]

Use it before calling something 'invented lore': if the game's own data mentions it, it is canon.
Reads your local game files only; prints short excerpts, writes nothing.
"""
import sys

import gamedb


def main():
    a = [x for x in sys.argv[1:]]
    mx = 15
    if "--max" in a:
        i = a.index("--max")
        mx = int(a[i + 1])
        del a[i:i + 2]
    db = gamedb.GameDB(a[0], masters_only=True)
    for term in a[1:]:
        t = term.lower()
        n = 0
        print(f"== {term}")
        for rec in db.recs.values():
            for k, v in rec["str"].items():
                if t in v.lower():
                    i = v.lower().index(t)
                    print(f"  [{rec['type']}] {rec['name']} / {k}: ...{v[max(0, i-70):i+90].replace(chr(10), ' ')}...")
                    n += 1
                    break
            if n >= mx:
                break
        if not n:
            print("  (nothing in the official data)")


if __name__ == "__main__":
    main()
