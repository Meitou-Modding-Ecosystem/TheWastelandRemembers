"""Structural checks on a generated .mod, against the game data it will be loaded after.

Catches what would show up as a broken or empty dialogue in the Forgotten Construction Set or in game: references to
records that do not exist or have the wrong type, unknown enum numbers, lines with no text, dialogues no NPC can reach,
and any difference between the file as written and the file as read back.
"""
import os
import sys

import builder as B
import fcs_writer
import fcs_reader

REF_TYPES = {
    "variable equals": {1000}, "variable less than": {1000}, "variable greater than": {1000}, "add to variable": {1000}, "set variable": {1000}, "take item": {4}, "relations": {10}, "squad default": {52}, "residents": {52}, "roaming squads": {52}, "nests": {13}, "dialog squad": {73}, "dialog leader": {73}, "inheritsFrom": {73}, "lines": {19}, "unlocks": {18}, "locks": {18}, "unlock but keep me": {18}, "homeless spawns": {52}, "bounty factions": {10}, "leader": {1}, "arrival dialog": {18}, "has package": {73}, "target has item": {4}, "conditions": {31}, "run lua script": {322}, "effects": {31}, "dialogs": {18}, "my faction": {10}, "target faction": {10},
    "in town of": {10}, "change relations": {10}, "my race": {7}, "my subrace": {7}, "target race": {7},
    "no target race": {7}, "world state": {110}, "NPC is": {1}, "NPC is NOT": {1}, "is character": {1},
}


def check(path, db, extra=(), mod_codes=False):
    """extra: record lists of our other mod files that this one may point at."""
    problems = []
    with open(path, "rb") as fh:
        raw = fh.read()
    parsed = fcs_reader.parse(path)
    if fcs_writer.write(parsed) != raw:
        problems.append("file does not survive a read-and-write round trip")
    ours = {r["sid"]: r for r in parsed["records"]}
    others = {r["sid"]: r for recs in extra for r in recs}
    attached = set()

    def get(sid):
        return ours.get(sid) or others.get(sid) or db.get(sid)

    for r in parsed["records"]:
        ctx = f"{r['name']} <{r['sid']}>"
        for key, entries in r["ref"].items():
            want = REF_TYPES.get(key)
            for e in entries:
                tgt = get(e[0])
                if tgt is None:
                    problems.append(f"{ctx}: '{key}' points at missing record {e[0]}")
                elif want and tgt["type"] not in want:
                    problems.append(f"{ctx}: '{key}' points at {e[0]} of type {tgt['type']}, expected {sorted(want)}")
                if key == "arrival dialog":
                    attached.add(e[0])
                if key == "dialogs":
                    if not 1 <= e[1] < B.EV["EV_MAX"]:
                        problems.append(f"{ctx}: bad event number {e[1]} for {e[0]}")
                    attached.add(e[0])
        if r["type"] == 31:
            i = r["int"]
            if "condition name" in i and not (0 < i["condition name"] < B.DC["DC_END"] or (mod_codes and i["condition name"] >= 1000)):
                problems.append(f"{ctx}: bad condition number {i['condition name']}")
            if "action name" in i and not (0 <= i["action name"] < B.DA["DA_END"] + 1 or (mod_codes and i["action name"] >= 1000)):
                problems.append(f"{ctx}: bad action number {i['action name']}")
        if r["type"] == 19:
            silent_ok = r["ref"].get("effects") or r["ref"].get("add to variable") or r["ref"].get("set variable") or r["ref"].get("unlocks") or r["ref"].get("locks")
            if not silent_ok and not any(v.strip() for k, v in r["str"].items() if k.startswith("text")):
                problems.append(f"{ctx}: line has no text")
        if r["type"] == 18 and not r["ref"].get("lines"):
            problems.append(f"{ctx}: dialogue has no lines")
    for r in parsed["records"]:
        if r["type"] == 18 and r["sid"] not in attached:
            problems.append(f"{r['name']} <{r['sid']}>: dialogue is not attached to any dialogue package, so no NPC will use it")
    return problems


if __name__ == "__main__":
    import gamedb
    if len(sys.argv) < 3:
        print("usage: python validate.py DATA_DIR MOD_FILE")
        sys.exit(1)
    for p in check(sys.argv[2], gamedb.GameDB(sys.argv[1])):
        print("INVALID:", p)
