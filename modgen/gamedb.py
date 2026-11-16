"""Load Kenshi game data (base + mods, in load order) and look records up by name; decode dialogues to readable text.

  python gamedb.py DATA_DIR explain "dialogue name"      # decode a dialogue: event(s), conditions, lines, effects
  python gamedb.py DATA_DIR find KIND "exact name"       # KIND: faction race character town world_state dialogue package ...
  python gamedb.py DATA_DIR list KIND [substring]        # every name of that kind (official game files only)
DATA_DIR holds gamedata.base and the mod files (mods.cfg is read for load order if present; else base + all .mod).
The game data is Kenshi's property: this reads it locally and writes nothing.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "server", "tools"))
import fcs_reader  # noqa: E402

ENUMS = json.load(open(os.path.join(HERE, "enums.json"), encoding="utf-8"))
REV = {k: {v: n for n, v in e.items()} for k, e in ENUMS.items()}

# record type numbers (from fcs.def, confirmed on gamedata.base)
T = {"character": 1, "weapon": 2, "armour": 3, "item": 4, "race": 7, "faction": 10, "town": 13, "dialogue": 18,
     "line": 19, "action": 31, "package": 73, "squad": 52, "world_state": 110, "campaign": 93, "ai_package": 72,
     "biome": 28, "zone": 95, "start": 64, "stats": 25, "personality": 26, "faction_template": 78, "squad_template2": 63}


def load(data_dir, extra=(), masters_only=False):
    """data_dir is either a Kenshi install folder (data/gamedata.base, mods.cfg, mods/...) or a flat folder of data files."""
    if os.path.isfile(os.path.join(data_dir, "data", "gamedata.base")):
        import derive_all
        paths, warnings = derive_all.collect_paths(data_dir)
        if masters_only:
            paths = [p for p in paths if os.path.basename(p) in derive_all.BASE_FILES]
        # never read our own earlier build as game data (an installed LivingWasteland*.mod in the mod list would be compiled on top of)
        ours = [p for p in paths if os.path.basename(p).lower().startswith("livingwasteland")]
        paths = [p for p in paths if p not in ours]
        warnings = [w for w in warnings if "livingwasteland" not in w.lower()]
        if ours:
            print(f"note: left out {len(ours)} installed Living Wasteland mod(s) from the game data (they are what this build replaces)")
        for w in warnings:
            print("warning:", w)
        return fcs_reader.merge_files(paths + list(extra))
    base = os.path.join(data_dir, "gamedata.base")
    paths = [base]
    for core in ("Newwworld.mod", "Dialogue.mod", "rebirth.mod"):      # the master files load first, in this order
        if os.path.exists(os.path.join(data_dir, core)):
            paths.append(os.path.join(data_dir, core))
    cfg = os.path.join(data_dir, "mods.cfg")
    names = []
    if os.path.exists(cfg) and not masters_only:
        names = [l.strip() for l in open(cfg, encoding="utf-8", errors="replace") if l.strip().endswith(".mod")]
    for n in names:
        if n.lower().startswith("livingwasteland"):
            continue                                    # our own earlier build is never game data
        p = os.path.join(data_dir, n)
        if os.path.exists(p):
            paths.append(p)
    if len(paths) == 1 and not masters_only:
        paths += sorted(os.path.join(data_dir, f) for f in os.listdir(data_dir) if f.endswith(".mod"))
    seen = set()
    paths = [p for p in paths if not (p in seen or seen.add(p))]
    paths += list(extra)
    return fcs_reader.merge_files(paths)


class GameDB:
    def __init__(self, data_dir, extra=(), masters_only=False):
        """masters_only: only gamedata.base and the three official master files, so a mod built against it never
        depends on a third-party mod."""
        self.recs = load(data_dir, extra, masters_only)
        self.by_name = {}
        for r in self.recs.values():
            self.by_name.setdefault((r["type"], r["name"].lower()), []).append(r)

    def get(self, sid):
        return self.recs.get(sid)

    def find(self, kind, name):
        t = T[kind] if isinstance(kind, str) else kind
        return self.by_name.get((t, name.lower()), [])

    def one(self, kind, name):
        found = self.find(kind, name)
        if len(found) != 1:
            raise KeyError(f"{kind} '{name}': {len(found)} matches" + (": " + ", ".join(r['sid'] for r in found) if found else ""))
        return found[0]

    def label(self, sid):
        r = self.recs.get(sid)
        return f"{r['name']} <{sid}>" if r else f"?{sid}"

    # ---- dialogue decoding ----
    def explain_action(self, sid):
        r = self.recs.get(sid)
        if not r:
            return f"?{sid}"
        i = r["int"]
        if "action name" in i and i["action name"]:
            return f"ACTION {REV['DialogActionEnum'].get(i['action name'], i['action name'])}"
        if "condition name" in i:
            cond = REV["DialogConditionEnum"].get(i["condition name"], i["condition name"])
            return f"COND {cond} who={i.get('who')} cmp={REV['ComparisonEnum'].get(i.get('compare by'), i.get('compare by'))} tag={i.get('tag')} str={r['str'].get('stringvar', '')!r}"
        return f"ACTION/COND? {i}"

    def explain_dialogue(self, r):
        out = [f"DIALOGUE {r['name']} <{r['sid']}>  {dict(r['bool'])} {dict(r['int'])}"]
        for key, entries in r["ref"].items():
            for e in entries:
                if key == "conditions":
                    out.append(f"  condition {self.explain_action(e[0])}  value={e[1]}")
                elif key == "lines":
                    line = self.recs.get(e[0])
                    out.append(f"  line <{e[0]}> pct={e[1]}")
                    if line:
                        texts = [v for k, v in sorted(line["str"].items()) if k.startswith("text") and v]
                        out.append(f"    speaker={REV['TalkerEnum'].get(line['int'].get('speaker', 0))} {dict(line['int'])}")
                        for t in texts[:8]:
                            out.append(f"    \"{t}\"")
                        for k2, ents2 in line["ref"].items():
                            for e2 in ents2:
                                out.append(f"    {k2}: " + (self.explain_action(e2[0]) if k2 == "effects" else self.label(e2[0])) + f" v={e2[1:]}")
                else:
                    out.append(f"  {key}: {self.label(e[0])} v={e[1:]}")
        return "\n".join(out)


def main(argv):
    if len(argv) < 4:
        print(__doc__)
        return 1
    db = GameDB(argv[1], masters_only=True)
    cmd = argv[2]
    if cmd == "explain":
        for r in db.find("dialogue", argv[3]):
            print(db.explain_dialogue(r))
            print()
    elif cmd == "find":
        for r in db.find(argv[3], argv[4]) if len(argv) > 4 else []:
            print(r["sid"], "|", r["name"])
    elif cmd == "list":      # list KIND [substring]: every record name of that kind (official files only unless --all)
        want = (argv[4] if len(argv) > 4 else "").lower()
        for name in sorted({r["name"] for r in db.recs.values() if r["type"] == T[argv[3]] and want in r["name"].lower()}):
            print(name)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
