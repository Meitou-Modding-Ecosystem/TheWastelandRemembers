"""Extract the dialogue enums (events, conditions, actions, speakers ...) from KenshiLib's headers into enums.json.

  python extract_enums.py <path to KenshiLib>/Include/kenshi  [out.json]

KenshiLib (github.com/BFrizzleFoShizzle/KenshiLib, MIT-style open source) lists these enums in declaration order, which
is the number the game data files store. The result maps enum name -> {NAME: number} and the reverse.
"""
import json
import os
import re
import sys

WANTED = ("EventTriggerEnum", "DialogConditionEnum", "DialogActionEnum", "TalkerEnum", "ComparisonEnum",
          "CharacterTypeEnum", "itemType", "TaskType", "StandingOrder", "StatsEnumerated", "SlaveStateEnum")


def parse_enum(text, name):
    m = re.search(r"enum\s+" + re.escape(name) + r"\s*\{(.*?)\};", text, re.S)
    if not m:
        return None
    body = re.sub(r"//[^\n]*", "", m.group(1))
    body = re.sub(r"/\*.*?\*/", "", body, flags=re.S)
    out, n = {}, 0
    for part in body.split(","):
        part = part.strip()
        if not part:
            continue
        if "=" in part:
            key, val = [x.strip() for x in part.split("=", 1)]
            try:
                n = int(val, 0)
            except ValueError:
                continue
        else:
            key = part
        out[key] = n
        n += 1
    return out


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    root = argv[1]
    dest = argv[2] if len(argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "enums.json")
    text = ""
    for fn in ("Enums.h", "Dialogue.h"):
        with open(os.path.join(root, fn), encoding="utf-8", errors="replace") as fh:
            text += fh.read() + "\n"
    result = {}
    for name in WANTED:
        e = parse_enum(text, name)
        if e:
            result[name] = e
    with open(dest, "w", encoding="utf-8") as fh:
        json.dump(result, fh, indent=1)
    for k, v in result.items():
        print(f"{k}: {len(v)} entries")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
