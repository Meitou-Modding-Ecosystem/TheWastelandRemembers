"""Read Kenshi Forgotten Construction Set data files (.base and .mod, format version 16).

The layout was worked out from gamedata.base (all 9,399 records parse to the exact end of the file):
  header   int version(16), int mod version, four strings, int x3 (stamp, record count, 0)
  record   int type, int id, string name, string stringId, int flags, then sections in this order:
           bool (key, byte), float, int, vec3, vec4, string (key, string), file (key, string),
           reference (key, count, count x [string stringId, int, int, int]), instance (count x [id, target,
           3 floats, 4 floats, count x string]), one trailing int (absent on the last record)
Type numbers are not named in the file; use `types` to see which fcs.def section a type number matches.

Usage (from server/tools):
  python fcs_reader.py FILE types                    # type number, best-matching fcs.def section, count, samples
  python fcs_reader.py FILE find TEXT [--type N]     # records whose name, stringId or any text contains TEXT
  python fcs_reader.py FILE show STRINGID            # everything stored in one record
  python fcs_reader.py FILE lines [--limit N]        # dialogue text lines (type 19) with their stringIds
The files are Kenshi's property: keep extracts out of public repositories and out of releases.
"""
import argparse
import collections
import os
import re
import struct
import sys


class _Reader:
    def __init__(self, data):
        self.d, self.p = data, 0

    def i(self):
        v = struct.unpack_from("<i", self.d, self.p)[0]
        self.p += 4
        return v

    def f(self):
        v = struct.unpack_from("<f", self.d, self.p)[0]
        self.p += 4
        return v

    def b(self):
        v = self.d[self.p]
        self.p += 1
        return v

    def s(self):
        n = self.i()
        if n < 0 or n > 1_000_000:
            raise ValueError(f"bad string length {n} at offset {self.p}")
        v = self.d[self.p:self.p + n].decode("utf-8", "replace")
        self.p += n
        return v


def _v17_strings(data, hdr_len):
    """The length-prefixed strings inside a version 17 header (author, description, dependencies, ...)."""
    out, p, end = [], 12, 8 + hdr_len
    while p + 4 <= end and len(out) < 4:
        n = struct.unpack_from("<i", data, p)[0]
        if n < 0 or p + 4 + n > end:
            break
        out.append(data[p + 4:p + 4 + n].decode("utf-8", "replace"))
        p += 4 + n
    return out


def parse(path):
    """Returns {'version', 'header', 'records', 'end', 'size'}; each record is a dict of its sections."""
    with open(path, "rb") as fh:
        data = fh.read()
    r = _Reader(data)
    version = r.i()
    if version == 16:
        header = {"mod_version": r.i(), "strings": [r.s() for _ in range(4)], "ints": [r.i(), r.i(), r.i()]}
    elif version == 17:
        # version 17 starts with a header length; the block holds mod version, author, description, dependencies,
        # references and two more ints. Records start after the length, then [stamp, record count, x].
        hdr_len = r.i()
        r.p = 8 + hdr_len
        header = {"mod_version": struct.unpack_from("<i", data, 8)[0], "header_len": hdr_len, "strings": _v17_strings(data, hdr_len),
                  "raw": data[:8 + hdr_len], "ints": [r.i(), r.i()]}
        if r.p + 4 <= len(data) and header["ints"][1] > 0:
            header["ints"].append(r.i())
    else:
        raise ValueError(f"unsupported file version {version} (only 16 and 17 are understood)")
    count = header["ints"][1]
    recs = []
    for _ in range(count):
        rec = {"type": r.i(), "id": r.i(), "name": r.s(), "sid": r.s(), "flags": r.i() & 0xFFFFFFFF}
        rec["bool"] = {r.s(): r.b() for _ in range(r.i())}
        rec["float"] = {r.s(): r.f() for _ in range(r.i())}
        rec["int"] = {r.s(): r.i() for _ in range(r.i())}
        rec["vec3"] = {r.s(): (r.f(), r.f(), r.f()) for _ in range(r.i())}
        rec["vec4"] = {r.s(): (r.f(), r.f(), r.f(), r.f()) for _ in range(r.i())}
        rec["str"] = {r.s(): r.s() for _ in range(r.i())}
        rec["file"] = {r.s(): r.s() for _ in range(r.i())}
        refs = {}
        for _ in range(r.i()):
            key = r.s()
            refs[key] = [(r.s(), r.i(), r.i(), r.i()) for _ in range(r.i())]
        rec["ref"] = refs
        inst = []
        for _ in range(r.i()):
            iid, tgt = r.s(), r.s()
            pos, rot = (r.f(), r.f(), r.f()), (r.f(), r.f(), r.f(), r.f())
            inst.append((iid, tgt, pos, rot, [r.s() for _ in range(r.i())]))
        rec["inst"] = inst
        rec["tail"] = r.i() if r.p + 4 <= len(data) else None   # the int after each record (absent on the last one)
        recs.append(rec)
    return {"version": version, "header": header, "records": recs, "end": r.p, "size": len(data)}


_REMOVED = 2147483647     # a reference value of INT_MAX in a changed record marks that reference as removed
_VALUE_SECTIONS = ("bool", "float", "int", "vec3", "vec4", "str", "file")


def merge_files(paths):
    """Load several files in load order and return {stringId: record}.

    A record whose flags have bit 0 set is a change to an earlier record: only the fields it lists are replaced,
    reference lists are merged by target id (a target whose first value is INT_MAX is removed), and bit 1 means the
    record was renamed (the changed record carries the new name). Other records are new and replace nothing.
    """
    merged = {}
    for path in paths:
        label = os.path.basename(path)
        for rec in parse(path)["records"]:
            rec["src"] = label
            old = merged.get(rec["sid"])
            if old is None or not (rec["flags"] & 1):
                merged[rec["sid"]] = rec
                continue
            new = dict(old)
            new["name"] = rec["name"] or old["name"]
            new["src"] = label
            for sec in _VALUE_SECTIONS:
                if rec[sec]:
                    merged_sec = dict(old[sec])
                    merged_sec.update(rec[sec])
                    new[sec] = merged_sec
            refs = {k: list(v) for k, v in old["ref"].items()}
            for key, entries in rec["ref"].items():
                cur = {e[0]: e for e in refs.get(key, [])}
                for e in entries:
                    if e[1] == _REMOVED:
                        cur.pop(e[0], None)
                    else:
                        cur[e[0]] = e
                refs[key] = list(cur.values())
            new["ref"] = refs
            if rec["inst"]:
                inst = {i[0]: i for i in old["inst"]}
                for i in rec["inst"]:
                    inst[i[0]] = i
                new["inst"] = list(inst.values())
            merged[rec["sid"]] = new
    return merged


def load_def_sections(def_path):
    """{section name: set(field names)} from fcs.def."""
    secs, cur = {}, None
    with open(def_path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            line = line.rstrip("\n")
            m = re.match(r"\[([A-Za-z0-9_,]+)\]", line)
            if m:
                cur = m.group(1)
                secs[cur] = set()
                continue
            m = re.match(r"^([^:\t][^:]*):", line)
            if cur and m:
                secs[cur].add(m.group(1).strip())
    return secs


def field_keys(rec):
    keys = set()
    for sec in ("bool", "float", "int", "vec3", "vec4", "str", "file", "ref"):
        keys |= set(rec[sec])
    return keys


def type_summary(records, def_sections=None):
    """[(type, best fcs.def section or '?', count, sample names)] sorted by type number."""
    keys, names = collections.defaultdict(set), collections.defaultdict(list)
    for rec in records:
        keys[rec["type"]] |= field_keys(rec)
        names[rec["type"]].append(rec["name"])
    out = []
    for t in sorted(keys):
        best = "?"
        if def_sections and keys[t]:
            best = max(def_sections.items(), key=lambda kv: len(keys[t] & kv[1]) - 0.01 * len(kv[1]))[0]
        out.append((t, best, len(names[t]), names[t][:3]))
    return out


def dialogue_lines(records):
    """[(stringId, [text0, text1, ...])] for type 19 records."""
    out = []
    for rec in records:
        if rec["type"] == 19 and rec["str"]:
            texts = [v for k, v in sorted(rec["str"].items()) if k.startswith("text") and v]
            if texts:
                out.append((rec["sid"], texts))
    return out


def find(records, text, type_=None):
    low = text.lower()
    for rec in records:
        if type_ is not None and rec["type"] != type_:
            continue
        hay = [rec["name"], rec["sid"]] + list(rec["str"].values())
        if any(low in str(h).lower() for h in hay):
            yield rec


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("file")
    ap.add_argument("cmd", choices=["types", "find", "show", "lines"])
    ap.add_argument("arg", nargs="?")
    ap.add_argument("--type", type=int)
    ap.add_argument("--limit", type=int, default=50)
    ap.add_argument("--def", dest="deffile", default=None, help="path to fcs.def (guessed next to the file)")
    a = ap.parse_args(argv)
    data = parse(a.file)
    recs = data["records"]
    if a.cmd == "types":
        deff = a.deffile or os.path.join(os.path.dirname(os.path.abspath(a.file)), "fcs.def")
        secs = load_def_sections(deff) if os.path.exists(deff) else None
        for t, sec, n, sample in type_summary(recs, secs):
            print(f"{t:4d}  {n:5d}  {sec[:60]:60s}  {sample}")
    elif a.cmd == "find":
        for rec in list(find(recs, a.arg or "", a.type))[:a.limit]:
            print(rec["type"], rec["sid"], "|", rec["name"])
    elif a.cmd == "show":
        for rec in recs:
            if rec["sid"] == a.arg:
                for k, v in rec.items():
                    print(f"{k}: {v}")
    else:
        for sid, texts in dialogue_lines(recs)[:a.limit]:
            print(sid, "|", " / ".join(texts))
    return 0


if __name__ == "__main__":
    sys.exit(main())
