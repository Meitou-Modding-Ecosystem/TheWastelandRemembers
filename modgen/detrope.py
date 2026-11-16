"""Thin out one tell of machine-written dialogue: the trailing punchline ("X's lot took a cart near the Hook. Took the driver too.").

Kenshi's own dialogue ends 17% of its lines with a sentence of four words or fewer after a longer one; ours had twice that. This drops the
punchline from about half of the lines that qualify, chosen by a hash of the text (the same lines every run), so the rhythm comes down to
vanilla's without flattening every line. A line qualifies when:
  * it has two or more sentences and the last one is four words or fewer and not a question,
  * what is left is six or more words and ends a sentence (not "?" or ":"),
  * it is not the player's own reply (speaker T_TARGET_IF_PLAYER), and
  * the shortened line is not already another variant of the same line, and
  * the punchline names nobody (no capitalised word after its first, no /TOKEN/) and does not turn the story ("Then ...").

  python detrope.py            # rewrites content/*.json in place and prints what changed (counts, and a sample)
  python detrope.py --check    # only counts
Generators call detrope_pack() on what they write, so regenerated files come out the same.
"""
import glob
import json
import os
import re
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
SHARE = 75          # percent of the qualifying lines that lose their punchline


def _sentences(t):
    return [p for p in re.split(r"(?<=[.!?])\s+", t.strip()) if p]


def two_beat(t):
    parts = _sentences(t)
    return len(parts) >= 2 and len(parts[-1].split()) <= 4


def shorten(t):
    """The line without its punchline, or None when it does not qualify or the hash keeps it."""
    parts = _sentences(t)
    if len(parts) < 2:
        return None
    last, rest = parts[-1], " ".join(parts[:-1])
    if len(last.split()) > 4 or last.endswith("?") or len(rest.split()) < 6 or rest.endswith(("?", ":")):
        return None
    words = last.split()
    if "/" in last or words[0].strip(",.!") in ("Then", "Unless", "Except") \
            or any(w[:1].isupper() and w.strip(",.!'s") not in ("I", "I'm", "I'd", "I'll", "Okran") for w in words[1:]):
        return None                     # it carries a name, a placeholder or the turn of the story: the line needs it
    if zlib.crc32(t.encode("utf-8")) % 100 >= SHARE:
        return None
    return rest


def _fix_say(say, changes):
    variants = say if isinstance(say, list) else [say]
    out, seen = [], set(v for v in variants if isinstance(v, str))
    for v in variants:
        if isinstance(v, str) and (s := shorten(v)) is not None and s not in seen:
            changes.append((v, s))
            seen.add(s)
            out.append(s)
        else:
            out.append(v)
    return out if isinstance(say, list) else out[0]


def detrope_pack(obj, changes=None):
    """Rewrites every NPC "say" in a pack (dicts and lists, any depth) in place; returns the list of (before, after)."""
    changes = [] if changes is None else changes
    if isinstance(obj, dict):
        if "say" in obj and obj.get("speaker") != "T_TARGET_IF_PLAYER":
            obj["say"] = _fix_say(obj["say"], changes)
        for k, v in obj.items():
            if k != "say":
                detrope_pack(v, changes)
    elif isinstance(obj, list):
        for v in obj:
            detrope_pack(v, changes)
    return changes


def _count(obj):
    n = tb = 0
    stack = [obj]
    while stack:
        o = stack.pop()
        if isinstance(o, dict):
            for k, v in o.items():
                if k == "say":
                    for t in (v if isinstance(v, list) else [v]):
                        if isinstance(t, str):
                            n += 1
                            tb += two_beat(t)
                else:
                    stack.append(v)
        elif isinstance(o, list):
            stack.extend(o)
    return n, tb


def main(argv):
    check = "--check" in argv
    files = sorted(glob.glob(os.path.join(HERE, "content", "*.json")))
    total = tb_before = tb_after = 0
    all_changes = []
    for f in files:
        data = json.load(open(f, encoding="utf-8"))
        n, tb = _count(data)
        total += n
        tb_before += tb
        if not check:
            ch = detrope_pack(data)
            if ch:
                all_changes += [(os.path.basename(f), a, b) for a, b in ch]
                with open(f, "w", encoding="utf-8") as fh:
                    json.dump(data, fh, indent=1, ensure_ascii=False)
                    fh.write("\n")
        tb_after += _count(data)[1]
    print(f"lines {total}: two-beat {tb_before} ({100 * tb_before / max(1, total):.0f}%)"
          + ("" if check else f" -> {tb_after} ({100 * tb_after / max(1, total):.0f}%), {len(all_changes)} lines shortened"))
    for f, a, b in all_changes[:: max(1, len(all_changes) // 25)][:25]:
        print(f"  {f}: {a!r}\n      -> {b!r}")


if __name__ == "__main__":
    main(sys.argv[1:])
