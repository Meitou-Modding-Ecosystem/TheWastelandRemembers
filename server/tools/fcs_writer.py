"""Write Kenshi Forgotten Construction Set data files (.mod), the inverse of fcs_reader.parse.

  python fcs_writer.py roundtrip FILE...      # parse each file, write it back, report whether the bytes are identical

`write(parsed)` takes the dict that fcs_reader.parse returns (possibly with records added or edited) and returns bytes.
Records are written exactly as read, so an unchanged file comes back byte for byte (checked on the base game and the
mods in this repo). Use modgen.py to build new records safely instead of editing dicts by hand.
"""
import struct
import sys

import fcs_reader

_I = struct.Struct("<i")
_F = struct.Struct("<f")


def _s(out, text):
    raw = text.encode("utf-8")
    out += _I.pack(len(raw)) + raw


def write_header(parsed):
    out = bytearray()
    h = parsed["header"]
    if parsed["version"] == 16:
        out += _I.pack(16) + _I.pack(h["mod_version"])
        for s in h["strings"]:
            _s(out, s)
        out += b"".join(_I.pack(x) for x in h["ints"][:3])
    else:
        out += h["raw"]
        out += b"".join(_I.pack(x) for x in h["ints"])
    return out


def write_record(out, rec, last=False):
    out += _I.pack(rec["type"]) + _I.pack(rec["id"])
    _s(out, rec["name"])
    _s(out, rec["sid"])
    out += _I.pack(rec["flags"] - (1 << 32) if rec["flags"] >= (1 << 31) else rec["flags"])
    out += _I.pack(len(rec["bool"]))
    for k, v in rec["bool"].items():
        _s(out, k)
        out.append(v)
    out += _I.pack(len(rec["float"]))
    for k, v in rec["float"].items():
        _s(out, k)
        out += _F.pack(v)
    out += _I.pack(len(rec["int"]))
    for k, v in rec["int"].items():
        _s(out, k)
        out += _I.pack(v)
    for sec, n in (("vec3", 3), ("vec4", 4)):
        out += _I.pack(len(rec[sec]))
        for k, v in rec[sec].items():
            _s(out, k)
            out += b"".join(_F.pack(x) for x in v)
    for sec in ("str", "file"):
        out += _I.pack(len(rec[sec]))
        for k, v in rec[sec].items():
            _s(out, k)
            _s(out, v)
    out += _I.pack(len(rec["ref"]))
    for k, entries in rec["ref"].items():
        _s(out, k)
        out += _I.pack(len(entries))
        for sid, a, b, c in entries:
            _s(out, sid)
            out += _I.pack(a) + _I.pack(b) + _I.pack(c)
    out += _I.pack(len(rec["inst"]))
    for iid, tgt, pos, rot, extra in rec["inst"]:
        _s(out, iid)
        _s(out, tgt)
        out += b"".join(_F.pack(x) for x in pos) + b"".join(_F.pack(x) for x in rot)
        out += _I.pack(len(extra))
        for e in extra:
            _s(out, e)
    if not last:
        out += _I.pack(rec.get("tail") or 0)


def write(parsed):
    parsed["header"]["ints"][1] = len(parsed["records"])
    out = write_header(parsed)
    n = len(parsed["records"])
    for i, rec in enumerate(parsed["records"]):
        write_record(out, rec, last=(i == n - 1))
    return bytes(out)


def roundtrip(path):
    with open(path, "rb") as fh:
        orig = fh.read()
    new = write(fcs_reader.parse(path))
    if new == orig:
        return True, "identical"
    n = next((i for i in range(min(len(new), len(orig))) if new[i] != orig[i]), min(len(new), len(orig)))
    return False, f"differs at byte {n} (sizes {len(orig)} / {len(new)})"


def main(argv):
    if len(argv) < 3 or argv[1] != "roundtrip":
        print(__doc__)
        return 1
    ok_all = True
    for p in argv[2:]:
        ok, msg = roundtrip(p)
        ok_all &= ok
        print(("OK   " if ok else "FAIL ") + p + "  " + msg)
    return 0 if ok_all else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
