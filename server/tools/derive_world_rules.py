"""Derive config/world_rules.json (world states and town overrides) from Kenshi's data files.

Kenshi stores 'what the world looks like now' as world states (all-of conditions such as 'Phoenix is dead',
'player is ally of Shek Kingdom') and town overrides (a replacement faction for a town while certain world states
are true or false). This extracts only that logic (names, numbers, ids), not any game text, so the server can work
out which towns have changed hands from the deaths and captures the plugin reports.

Usage (from server/tools), files in load order:
  python derive_world_rules.py gamedata.base Newwworld.mod Dialogue.mod rebirth.mod "Reactive World.mod"
Writes ../config/world_rules.json. Files are the user's own copies of the game data; the output holds ids and
names only.
"""
import json
import os
import sys

import fcs_reader

WORLD_STATE_TYPE = None  # found by matching fcs.def's WORLD_EVENT_STATE fields


def _find_types(recs):
    """Type numbers of TOWN and WORLD_EVENT_STATE records, recognised by their field names."""
    town = state = None
    for r in recs:
        keys = set(r["ref"]) | set(r["bool"]) | set(r["int"])
        if state is None and ({"NPC is", "player ally"} & set(r["ref"])) and "player involvement" in r["bool"]:
            state = r["type"]
        if town is None and "override town" in r["ref"]:
            town = r["type"]
        if town is not None and state is not None:
            break
    return town, state


def derive(paths):
    merged = fcs_reader.merge_files(paths)
    recs = list(merged.values())
    town_t, state_t = _find_types(recs)
    if town_t is None or state_t is None:
        raise SystemExit("could not find town and world state records in these files")

    def name_of(sid):
        r = merged.get(sid)
        return r["name"] if r else sid

    states = {}
    for r in recs:
        if r["type"] != state_t:
            continue
        conds = []
        for key, kind in (("NPC is", "npc"), ("NPC is NOT", "npc_not"), ("player ally", "ally"),
                          ("player enemy", "enemy"), ("town okay", "town_ok")):
            for sid, value, _, _ in r["ref"].get(key, []):
                conds.append([kind, name_of(sid), value])
        states[r["sid"]] = {"name": r["name"], "involvement": bool(r["bool"].get("player involvement")), "conds": conds}

    towns, base_names = {}, {}
    for r in recs:
        if r["type"] == town_t and "(override" not in r["name"]:
            base_names.setdefault(r["name"].strip().lower(), []).append(r)
    for low, rs in base_names.items():
        if len(rs) > 1:
            towns[low] = {"ambiguous": True}
            continue
        base = rs[0]
        fac = [name_of(a[0]) for a in base["ref"].get("faction", [])]
        overrides = []
        for sid, *_ in base["ref"].get("override town", []):
            o = merged.get(sid)
            if not o:
                continue
            ofac = [name_of(a[0]) for a in o["ref"].get("faction", [])]
            sts = [[a[0], a[1]] for a in o["ref"].get("world state", []) if a[0] in states]
            overrides.append({"name": o["name"], "faction": ofac[0] if ofac else None, "states": sts})
        if overrides:
            towns[low] = {"name": base["name"], "faction": fac[0] if fac else None, "overrides": overrides}
    used = {sid for t in towns.values() for o in t.get("overrides", []) for sid, _ in o["states"]}
    return {"states": {k: v for k, v in states.items() if k in used}, "towns": towns}


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    out = derive(argv[1:])
    out = {"_readme": "Derived by tools/derive_world_rules.py from the user's own game data. States are all-of conditions "
                      "(kind, name, value: npc 1 alive, 0 dead, 2 imprisoned; npc_not 1 not alive; ally/enemy 1 yes, 0 no). "
                      "Overrides apply while their listed states are true (value 1) or false (value 0).", **out}
    dest = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "config", "world_rules.json")
    with open(dest, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1, ensure_ascii=False)
    print(f"wrote {dest}: {len(out['states'])} states, {len(out['towns'])} towns")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
