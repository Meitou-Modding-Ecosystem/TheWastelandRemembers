"""Contractions, by voice: another tell of machine-written dialogue is that nobody contracts ("I do not", "That is", "It is not").

Kenshi's own dialogue contracts about 90% of the time; ours did about 12%. Vanilla's formal speakers are formal by faction, so the share of
lines contracted here follows what each voice does in the game's own text (measured from the base game's dialogue lines):
  Holy Nation 50%  ·  Shek (Kingdom, Kral's Chosen, Band of Bones, Berserkers) 67%  ·  Hive, skeletons, Machinists 78%  ·  everyone else 90%
Pidgin speakers (Cannibals, Gate Folk, Fogmen) are left as written. A line spoken by several factions counts as general speech unless they
all share a formal voice. Which lines are contracted follows a hash of the text (the same lines every run; running it again changes nothing).

Rules: negatives contract anywhere ("It is not." -> "It isn't."); "I am", "it is", "that is", "you are" and the like only when a word follows
(never "Yes, it is." -> "it's."), and never before a comma ("That is, ..."). Capitals are kept ("Do not" -> "Don't").

  python contractions.py [DATA_DIR]   # rewrites content/*.json; with the game's data folder, bosses get their own faction's voice
Generators call contract_pack() on what they write.
"""
import glob
import json
import os
import re
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))

NEG = [("do not", "don't"), ("does not", "doesn't"), ("did not", "didn't"), ("is not", "isn't"), ("are not", "aren't"), ("was not", "wasn't"),
       ("were not", "weren't"), ("cannot", "can't"), ("can not", "can't"), ("will not", "won't"), ("would not", "wouldn't"),
       ("could not", "couldn't"), ("should not", "shouldn't"), ("has not", "hasn't"), ("have not", "haven't"), ("had not", "hadn't"),
       ("must not", "mustn't")]
POS = [("I am", "I'm"), ("it is", "it's"), ("that is", "that's"), ("you are", "you're"), ("we are", "we're"), ("they are", "they're"),
       ("there is", "there's"), ("what is", "what's"), ("he is", "he's"), ("she is", "she's"), ("I will", "I'll"), ("you will", "you'll"),
       ("we will", "we'll"), ("they will", "they'll"), ("I would", "I'd"), ("who is", "who's"), ("here is", "here's"), ("where is", "where's")]

HOLY, SHEK, MACHINE, DEFAULT, SKIP = 50, 67, 78, 90, None
FACTION_VOICE = [
    (r"cannibal(?! hunter)|gate folk|fog ?m[ae]n|fog prince", SKIP),
    (r"holy(?! nation outlaws)|okran|paladin|inquisit|priest|phoenix", HOLY),
    (r"shek|kral|band of bones|berserker|esata", SHEK),
    (r"hive|skeleton|machinist|robot|second empire", MACHINE),
]


def voice_of(names):
    """The share for a dialogue spoken by these factions / packages / characters' factions; None leaves it as written."""
    voices = set()
    for n in names:
        v = DEFAULT
        for pat, val in FACTION_VOICE:
            if re.search(pat, n, re.I):
                v = val
                break
        voices.add(v)
    if not voices:
        return DEFAULT
    if SKIP in voices:
        return SKIP                     # a pidgin speaker among them: leave the line as written
    return voices.pop() if len(voices) == 1 else DEFAULT


def _case(src, dst):
    return dst[0].upper() + dst[1:] if src[0].isupper() else dst


PARTICIPLES = r"(?:seen|heard|been|had|never|got|done|lost|killed|known|told|met|walked|eaten|found|made|said|taken|given|left|kept|buried|fought|lived|watched|waited|tried|come|gone)"
HAVE = [("I have", "I've"), ("you have", "you've"), ("we have", "we've"), ("they have", "they've")]

# "they say" is in a few hundred of our lines and once in the base game: some of them hear it another way (chosen by a hash of the line)
HEARSAY_END = [", they say.", ", I heard.", ", so I hear.", ", people say."]
HEARSAY_START = ["They say ", "I heard ", "Word is, ", "People say "]


def hearsay(t):
    k = zlib.crc32(("hearsay:" + t).encode("utf-8")) % 100
    pick = 0 if k < 40 else 1 + k % 3
    if ", they say." in t:
        t = t.replace(", they say.", HEARSAY_END[pick], 1)
    if t.startswith("They say ") and not t.startswith("They say,"):
        t = HEARSAY_START[pick] + t[len("They say "):]
    return t


def contract(t):
    for a, b in HAVE:
        t = re.sub(r"\b" + a + r"\b(?=\s+" + PARTICIPLES + r"\b)", lambda m, b=b: _case(m.group(0), b), t, flags=0 if a[0] == "I" else re.I)
    for a, b in NEG:
        t = re.sub(r"\b" + a + r"\b", lambda m: _case(m.group(0), b), t, flags=re.I)
    for a, b in POS:
        # only with a word after it: "Yes, it is." and "That is, ..." stay
        t = re.sub(r"\b" + a + r"\b(?=\s+[A-Za-z0-9'/])", lambda m: _case(m.group(0), b) if m.group(0)[:1] != "i" or a[0] != "I" else b, t,
                   flags=re.I if a[0] != "I" else 0)
    return t


def _say(o, share, changes):
    vs = o if isinstance(o, list) else [o]
    out = []
    for v in vs:
        if not isinstance(v, str) or share is None:
            out.append(v)
            continue
        c = hearsay(v)
        if zlib.crc32(v.encode("utf-8")) % 100 < share:
            c = contract(c)
        if c != v:
            changes.append((v, c))
        out.append(c)
    return out if isinstance(o, list) else out[0]


def contract_pack(obj, char_faction=None, changes=None, share=DEFAULT):
    """Contracts every "say" in a pack in place (the speaker's voice from the nearest dialogue's factions, packages and characters)."""
    changes = [] if changes is None else changes
    if isinstance(obj, dict):
        names = list(obj.get("factions") or []) + list(obj.get("packages") or [])
        for c in obj.get("characters") or []:
            names.append((char_faction or {}).get(c, c))
        if names:
            share = voice_of(names)
        if obj.get("speaker") in ("T_TARGET_IF_PLAYER", "T_INTERJECTOR1", "T_INTERJECTOR2"):
            share_here = DEFAULT if obj.get("speaker") == "T_TARGET_IF_PLAYER" else share
        else:
            share_here = share
        if "say" in obj:
            obj["say"] = _say(obj["say"], share_here, changes)
        for k, v in obj.items():
            if k != "say":
                contract_pack(v, char_faction, changes, share)
    elif isinstance(obj, list):
        for v in obj:
            contract_pack(v, char_faction, changes, share)
    return changes


def character_factions(data_dir):
    """Character name -> faction name from the game data (bosses speak in their own faction's voice)."""
    sys.path.insert(0, HERE)
    import gamedb
    db = gamedb.GameDB(data_dir)
    out = {}
    for r in db.recs.values():
        if r.get("type") != 1:              # CHARACTER
            continue
        for ref in (r.get("ref") or {}).get("faction", []):
            f = db.get(ref[0] if isinstance(ref, (list, tuple)) else ref)
            if f:
                out.setdefault(r["name"], f["name"])
    return out


GENERATED = ("x_outlaw_gossip.json", "x_raids.json", "x_title_temperaments.json", "x_title_temperaments_cannibals.json", "x_atlas.json")


def main(argv):
    data = argv[0] if argv else None
    chars = character_factions(data) if data else {}
    total = []
    for f in sorted(glob.glob(os.path.join(HERE, "content", "*.json"))):
        if os.path.basename(f).startswith(("cannibals", "x_title_temperaments_cannibals")):
            continue                        # the Cannibals mod: pidgin throughout
        if os.path.basename(f) in GENERATED:
            continue                        # their generators run this themselves (rerun them instead)
        pack = json.load(open(f, encoding="utf-8"))
        ch = contract_pack(pack, chars)
        if ch:
            total += [(os.path.basename(f), a, b) for a, b in ch]
            with open(f, "w", encoding="utf-8") as fh:
                json.dump(pack, fh, indent=1, ensure_ascii=False)
                fh.write("\n")
    print(f"{len(total)} lines contracted ({len(chars)} characters' factions known)")
    for f, a, b in total[:: max(1, len(total) // 20)][:20]:
        print(f"  {f}: {a!r}\n      -> {b!r}")


if __name__ == "__main__":
    main(sys.argv[1:])
