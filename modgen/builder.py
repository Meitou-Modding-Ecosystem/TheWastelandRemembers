"""Build Kenshi mod data (.mod) from Python: dialogues, lines, conditions, actions, and attaching them to NPCs.

The encodings here were read off the game's own data (gamedata.base and the Dialogue/Reactive World/rebirth mods), and
`check_roundtrip.py` re-encodes real dialogues through this builder and compares them field by field.

A dialogue is a tree. The root DIALOGUE record lists its first lines (`lines`) and its conditions (`conditions`, plus
`my faction`, `target faction`, `my race`, `world state` ... reference lists). Each DIALOGUE_LINE has up to eight text
variants (the game picks one at random), a speaker (T_ME / T_TARGET / T_TARGET_IF_PLAYER), optional effects (actions)
and optional child lines (the replies and continuations). A DIALOGUE only runs when an NPC's dialogue package lists it
for an event (EV_...), so `attach` adds it to existing packages with a change record.

Everything this module writes is new records plus change records that only append to existing dialogue packages.
"""
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "server", "tools"))
import fcs_writer  # noqa: E402

ENUMS = json.load(open(os.path.join(HERE, "enums.json"), encoding="utf-8"))
EV, DC, DA, TK, CE = (ENUMS[k] for k in ("EventTriggerEnum", "DialogConditionEnum", "DialogActionEnum", "TalkerEnum",
                                          "ComparisonEnum"))
T_DIALOGUE, T_LINE, T_ACTION, T_PACKAGE = 18, 19, 31, 73
NEW, CHANGED = 0x10, 0x11          # record flags in version 17 files
STAMP = 5007294                    # the data-version stamp every current mod file carries
MAX_VARIANTS = 8


def _rec(type_, sid, name, flags=NEW):
    return {"type": type_, "id": 0, "name": name, "sid": sid, "flags": flags, "bool": {}, "float": {}, "int": {},
            "vec3": {}, "vec4": {}, "str": {}, "file": {}, "ref": {}, "inst": [], "tail": 0}


def _enum(table, value, kind):
    if isinstance(value, int):
        return value
    if value not in table:
        raise KeyError(f"unknown {kind} '{value}'")
    return table[value]


class ModBuilder:
    def __init__(self, mod_name, author="", description="", depends=("gamedata.base",), start_id=1):
        self.mod_name = mod_name
        self.file = mod_name + ".mod"
        self.author, self.description = author, description
        self.depends = list(depends)
        self.records = []
        self._next = start_id
        self._package_adds = {}      # package sid -> [(dialogue sid, event)]
        self._zone_adds = {}         # zone sid -> [dialogue sid]
        self._ref_changes = {}       # record sid -> {"rec": base record, "refs": {key: [entries]}}
        self._seen_names = set()
        self.known_names = {}        # sids of records created by our other mods -> their names (for change records)

    # ---- ids -----------------------------------------------------------------------------------
    def sid(self):
        n = self._next
        self._next += 1
        return f"{n}-{self.file}"

    def add(self, rec):
        self.records.append(rec)
        return rec["sid"]

    # ---- building blocks -------------------------------------------------------------------------
    def condition(self, cond, who=0, compare="CE_EQUALS", tag=-1, stringvar=""):
        """A condition record. The value it is compared with goes on the reference that uses it (see `dialogue`)."""
        sid = self.sid()
        r = _rec(T_ACTION, sid, f"DIALOG_ACTION{sid.split('-')[0]}")
        r["int"] = {"compare by": _enum(CE, compare, "comparison"), "condition name": _enum(DC, cond, "condition"),
                    "tag": tag, "who": who}
        r["str"] = {"stringvar": stringvar}
        return self.add(r)

    def action(self, act):
        """An action (effect) record. Its value (money, relation change, bounty ...) goes on the reference that uses it."""
        sid = self.sid()
        r = _rec(T_ACTION, sid, f"DIALOG_ACTION{sid.split('-')[0]}")
        r["int"] = {"action name": _enum(DA, act, "action")}
        return self.add(r)

    def line(self, texts, speaker="T_ME", effects=(), conditions=(), children=(), chance=100.0, refs=None,
             repetition=0, score_bonus=0):
        """A dialogue line. `texts`: 1 to 8 variants. `effects`: [(action name, value)]. `conditions`: [(condition
        sid, value)]. `children`: [(line sid, percent)] (replies or continuation). `refs`: extra reference lists."""
        if isinstance(texts, str):
            texts = [texts]
        texts = [t for t in texts if t is not None]
        if not 1 <= len(texts) <= MAX_VARIANTS:
            raise ValueError(f"a line needs 1 to {MAX_VARIANTS} text variants, got {len(texts)}")
        sid = self.sid()
        r = _rec(T_LINE, sid, f"DIALOGUE_LINE{sid.split('-')[0]}")
        r["float"] = {"chance permanent": 100.0, "chance temporary": float(chance)}
        r["int"] = {"repetition limit": repetition, "score bonus": score_bonus, "speaker": _enum(TK, speaker, "speaker"),
                    "target is type": 0}
        r["str"] = {f"text{i}": t for i, t in enumerate(texts)}
        if effects:
            r["ref"]["effects"] = [(self.action(a), v, 0, 0) for a, v in effects]
        if conditions:
            r["ref"]["conditions"] = [(c, v, 0, 0) for c, v in conditions]
        if children:
            r["ref"]["lines"] = [(c, pct, 0, 0) for c, pct in children]
        for k, entries in (refs or {}).items():
            r["ref"][k] = [(e[0], e[1] if len(e) > 1 else 0, 0, 0) for e in entries]
        return self.add(r)

    def dialogue(self, name, lines, conditions=(), refs=None, monologue=True, repetition=0, chance=100.0,
                 locked=False, one_at_a_time=False, for_enemies=False, score_bonus=0, target_type=0):
        """A DIALOGUE root. `lines`: [(line sid, percent)]. `conditions`: [(condition sid, value)]. `refs`: extra
        reference lists such as {'my faction': [(faction sid, 1)], 'world state': [(state sid, 1)]}."""
        if name in self._seen_names:
            raise ValueError(f"dialogue name used twice: {name}")
        self._seen_names.add(name)
        sid = self.sid()
        r = _rec(T_DIALOGUE, sid, name)
        r["bool"] = {"for enemies": int(for_enemies), "locked": int(locked), "monologue": int(monologue),
                     "one at a time": int(one_at_a_time)}
        r["float"] = {"chance permanent": 100.0, "chance temporary": float(chance)}
        r["int"] = {"repetition limit": repetition, "score bonus": score_bonus, "target is type": target_type}
        if conditions:
            r["ref"]["conditions"] = [(c, v, 0, 0) for c, v in conditions]
        r["ref"]["lines"] = [(l, pct, 0, 0) for l, pct in lines]
        for k, entries in (refs or {}).items():
            r["ref"][k] = [(e[0], e[1] if len(e) > 1 else 0, 0, 0) for e in entries]
        return self.add(r)

    def add_change(self, base_rec, ints=None, floats=None, bools=None, strs=None, refs=None):
        """A change record for an existing record: only the listed fields change; ref entries are appended."""
        slot = self._ref_changes.setdefault(base_rec["sid"], {"rec": base_rec, "refs": {}})
        for sec, vals in (("int", ints), ("float", floats), ("bool", bools), ("str", strs)):
            if vals:
                slot.setdefault("vals", {}).setdefault(sec, {}).update(vals)
        for key, entries in (refs or {}).items():
            slot["refs"].setdefault(key, []).extend(entries)

    def add_ref_change(self, base_rec, key, entries):
        """Append reference entries to an existing record (a change record). Entries for one record are merged."""
        slot = self._ref_changes.setdefault(base_rec["sid"], {"rec": base_rec, "refs": {}})
        slot["refs"].setdefault(key, []).extend(entries)

    def variable(self, name, initial=0):
        """A persistent number (needs the BFrizz Extra Extensions mod; record type 1000, field 'value')."""
        sid = self.sid()
        r = _rec(1000, sid, name)
        r["int"] = {"value": int(initial)}
        return self.add(r)

    def lua_script(self, name, file, description=""):
        """A LUA_SCRIPT item (record type 322, KenshiLua). `file` is the path the game loads, e.g.
        ./mods/<Mod>/scripts/dialogue/x.lua. A dialogue line runs it through the reference 'run lua script'."""
        sid = self.sid()
        r = _rec(322, sid, name)
        r["str"] = {"description": description}
        r["file"] = {"file": file}
        return self.add(r)

    def attach_zone(self, zone_sid, dialogue_sid):
        """Add a dialogue to a zone's arrival dialogues (spoken by the player's squad when it enters the zone)."""
        self._zone_adds.setdefault(zone_sid, []).append(dialogue_sid)

    def attach(self, package_sid, dialogue_sid, event):
        """Add a dialogue to an existing dialogue package for an event (EV_...). Written as a change record."""
        self._package_adds.setdefault(package_sid, []).append((dialogue_sid, _enum(EV, event, "event")))

    # ---- output ----------------------------------------------------------------------------------
    def _header(self):
        def s(x):
            b = x.encode("utf-8")
            return struct.pack("<i", len(b)) + b
        body = (struct.pack("<i", 1) + s(self.author) + s(self.description) + s(",".join(self.depends)) + s("")
                + struct.pack("<i", 1) + b"\x00" * 5)
        return struct.pack("<ii", 17, len(body)) + body

    def package_names(self, db):
        return {sid: (db.get(sid) or {}).get("name", "") for sid in self._package_adds}

    def parsed(self, db=None):
        recs = list(self.records)
        for pkg_sid, adds in self._package_adds.items():
            name = self.known_names.get(pkg_sid) or ((db.get(pkg_sid) or {}).get("name", "") if db else "")
            r = _rec(T_PACKAGE, pkg_sid, name, CHANGED)
            r["ref"]["dialogs"] = [(d, ev, 0, 0) for d, ev in adds]
            recs.append(r)
        for zone_sid, adds in self._zone_adds.items():
            zr = db.get(zone_sid) if db else None
            r = _rec(95, zone_sid, zr["name"] if zr else "", CHANGED)
            r["ref"]["arrival dialog"] = [(d, 0, 0, 0) for d in adds]
            recs.append(r)
        for sid, slot in self._ref_changes.items():
            base = slot["rec"]
            r = _rec(base["type"], sid, base["name"], CHANGED)
            r["ref"] = {k: list(v) for k, v in slot["refs"].items()}
            for sec, vals in slot.get("vals", {}).items():
                r[sec] = dict(vals)
            recs.append(r)
        return {"version": 17, "header": {"raw": self._header(), "ints": [STAMP, len(recs), 0]}, "records": recs}

    def write(self, out_dir, db=None):
        """Write <out_dir>/<mod>/<mod>.mod and its .info file. Returns the .mod path."""
        folder = os.path.join(out_dir, self.mod_name)
        os.makedirs(folder, exist_ok=True)
        path = os.path.join(folder, self.file)
        with open(path, "wb") as fh:
            fh.write(fcs_writer.write(self.parsed(db)))
        with open(os.path.join(folder, f"_{self.mod_name}.info"), "w", encoding="utf-8") as fh:
            fh.write('<?xml version="1.0"?>\n<ModData xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
                     'xmlns:xsd="http://www.w3.org/2001/XMLSchema">\n  <id>0</id>\n'
                     f"  <mod>{self.mod_name}</mod>\n  <title>{self.mod_name}</title>\n  <tags />\n"
                     "  <visibility>0</visibility>\n  <lastUpdate xsi:nil=\"true\" />\n</ModData>")
        return path
