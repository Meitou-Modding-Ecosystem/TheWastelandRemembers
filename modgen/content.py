"""Compile authored content packs (JSON) into a Kenshi .mod with ModBuilder.

A pack is a JSON file:
{
  "pack": "bosses_holy_nation",
  "world_states": [ {"name": "Phoenix killed by player", "npc": "Holy Lord Phoenix", "is": "dead", "player": true} ],
  "dialogues": [ {
      "id": "phoenix_dead_hn_bar",          unique name of the dialogue (shown in FCS)
      "event": "EV_BAR_TALK",               when it can run (an EventTriggerEnum name)
      "factions": ["The Holy Nation"],      my faction (any of); the dialogue is attached to these factions' NPCs
      "packages": ["bar"],                  optional extra dialogue packages by name (or "bar:all" for every bar NPC)
      "target_factions": [], "races": [], "target_races": [], "in_town_of": [],
      "states": [["Phoenix killed by player", true]],     world states that must be true (or false)
      "if": [ {"cond": "DC_RELATIONS", "who": "target", "cmp": "<", "value": -20} ],
      "chance": 60, "repeat": 3, "one_at_a_time": true, "monologue": true,
      "lines": [ {"say": ["variant 1", "variant 2"], "speaker": "T_ME",
                  "effects": [["DA_AFFECT_RELATIONS", -2]], "then": [ <more lines, played after this one> ]} ]
  } ]
}
`say` takes one to eight variants (the game picks one); a line with `"silent": true` says nothing and only runs its effects
(`var_add`/`var_set`/`take_items` need the BFrizz Extra Extensions mod, see AUTHORING.md); a dialogue's `score` raises its priority over other eligible dialogues; the pack's `"mod"` (default LivingWasteland)
chooses which mod file its records go to, and `"variables": [{"name": "...", "initial": 0}]` declares persistent numbers. Top-level `lines` are alternatives (one is chosen); `then`
lines follow the line they are under. Names (factions, races, characters, states, towns, packages) are looked up in the
game data; every problem is collected and reported together, so one run lists everything to fix.
"""
import os
import json
import re
import sys

import builder as B

TOKEN = re.compile(r"/([A-Z_0-9]+)/")
CMP = {"==": "CE_EQUALS", "=": "CE_EQUALS", "<": "CE_LESS_THAN", ">": "CE_MORE_THAN"}
WHO = {"me": 0, "target": 1, "interjector": 2, "0": 0, "1": 1}
STATE_IS = {"dead": 0, "alive": 1, "imprisoned": 2}
# personality tag numbers as stored in the game data (only these seven appear in use)
PERSONALITY = {"Honorable": 1, "Traitorous": 2, "Smart": 5, "Dumb": 6, "Brave": 9, "Fearful": 10, "Maniacal": 14}


# Talk dialogues must be eligible or not eligible consistently: the game checks "can I talk to this character" every frame to show
# the speech-bubble cursor, so a random chance on a talk dialogue makes the cursor flicker and the player has to click at the right
# millisecond. Offers are therefore spread over characters by a fixed personality tag (stable for a character), never by chance.
OFFER_TAGS = [1, 2, 5, 6, 9, 10, 14]


# Open jobs: how many accepted quests and contracts are still unpaid. Offers stop while the player carries this many.
JOB_CAP = 5
JOB_VAR = "LW open jobs"
REWARD_TAILS = ("It pays {r} cats.", "Pay is {r} cats.", "{r} cats, on proof.", "The fee: {r} cats.")


SUCC_OFFER = ["{o}'s gone, but the gang isn't. {s} runs it now, {rel}. They work the old ground.",
              "{s} took over once {o} was gone. {rel_cap}. The raids started again inside a week.",
              "You'd think getting rid of {o} would've ended it. {s} picked the gang up off the ground and kept going."]
SUCC_ACCEPTED = ["They keep to {o}'s old ground. Start there.", "Same roads {o} used to work. You'll find {s} on them."]
SUCC_REPORT = ["{s} too? Then maybe that gang is finished. Here's your pay.", "That's two of that gang's bosses now. Here's your pay."]


def successor_contracts(pack):
    """A contract on each outlaw's successor, made from the outlaw's own contract (same givers, 60% of the pay), offered only once the
    successor has taken over (the "successor:" mark)."""
    out = []
    by_state = {c.get("state"): c for c in pack.get("contracts", [])}
    for o in pack.get("outlaws", []):
        sc, c = o.get("successor"), by_state.get(f"{o['name']} killed by player")
        if not sc or not c:
            continue
        f = dict(o=o["name"], s=sc["name"], rel=sc.get("relation", "one of the gang"), rel_cap=(sc.get("relation", "one of the gang")[:1].upper() + sc.get("relation", "one of the gang")[1:]))
        n = dict({k: v for k, v in c.items() if k in ("factions", "packages", "in_town_of", "chance", "decline")},
                 accept=[f"I'll find {sc['name']}.", "Where do they work now?"],
                 id=c["id"].replace("_contract", "") + "_successor_contract", state=f"{sc['name']} killed by player",
                 reward=max(500, int(c.get("reward", 1000) * 0.6) // 100 * 100), successor_of=o["name"],
                 offer=[t.format(**f) for t in SUCC_OFFER], accepted=[t.format(**f) for t in SUCC_ACCEPTED],
                 report=[t.format(**f) for t in SUCC_REPORT])
        out.append(n)
    return out


def with_reward(say, reward):
    """Offer variants that name the pay, so the player knows the price before accepting."""
    out = []
    for i, t in enumerate(say):
        t = str(t)
        out.append(t if (str(reward) in t or "cats" in t.lower()) else t.rstrip() + " " + REWARD_TAILS[i % len(REWARD_TAILS)].format(r=reward))
    return out


# Contracts and faction rank: a paid contract raises the player's rank with the giver's faction (DA 35, capped), and the dangerous ones
# need a rank first (DC 10 reads the player's rank in the speaker's faction). Vanilla ranks start at 0.
RANK_CAP = 3
HANDIN_TEXT = ["That is the one. Set it down. The bounty is the pay; the Guild adds nothing, and neither do I.",
               "You brought the body itself. Good. The posted price, then, and the matter is closed.",
               "I know that face. Leave it with me. You will be paid what was posted, no more."]


RANK_GATE_TEXT = ["There is heavier work on the books than I would hand a stranger. Finish something smaller for us first.",
                  "I have a name worth real cats. I do not give it to faces I have not seen come back. Earn it.",
                  "Not that one. Not yet. Do a small job for us and ask again."]


def rank_needed(reward):
    """0 for everyday contracts, 1 from 5000 cats, 2 from 6500."""
    return 2 if reward >= 6500 else (1 if reward >= 5000 else 0)


def offer_tag(key):
    return OFFER_TAGS[sum(ord(c) for c in str(key)) % len(OFFER_TAGS)]


class Compiler:
    def __init__(self, mod, db, tokens=None):
        self.b, self.db = mod, db
        self.errors, self.warnings = [], []
        self.states = {}                     # name -> sid (ours or the game's)
        self.created = {}                    # (kind, name) -> sid of records this build creates (outlaws, squads)
        self.tokens = tokens if tokens is not None else {r["name"] for r in db.recs.values() if r["type"] == 84}
        self._pkg_cache = None
        self.stats = {"dialogues": 0, "lines": 0, "variants": 0, "states": 0, "attached": 0, "outlaws": 0, "contracts": 0, "clones": 0, "changes": 0}

    # ---- name lookups ----------------------------------------------------------------------------
    def look(self, kind, name, ctx):
        if (kind, name) in self.created:
            return self.created[(kind, name)]
        found = self.db.find(kind, name)
        if not found:
            self.errors.append(f"{ctx}: no {kind} named '{name}'")
            return None
        if len(found) > 1:
            self.warnings.append(f"{ctx}: {kind} '{name}' matches {len(found)} records, using {found[-1]['sid']}")
        return found[-1]["sid"]

    def looks(self, kind, names, ctx):
        return [s for s in (self.look(kind, n, ctx) for n in names or []) if s]

    # ---- world states ----------------------------------------------------------------------------
    def make_state(self, spec):
        name = spec["name"]
        if name in self.states:
            return self.states[name]
        existing = self.db.find("world_state", name)
        if existing and not spec.get("force_new"):
            self.states[name] = existing[-1]["sid"]
            return self.states[name]
        char = self.look("character", spec["npc"], f"state '{name}'")
        if not char:
            return None
        is_ = STATE_IS[spec.get("is", "dead")]
        sid = self.b.sid()
        r = B._rec(110, sid, name)
        r["bool"] = {"player involvement": 1 if spec.get("player", True) else 0}
        r["str"] = {"notes": spec.get("notes", "")}
        r["ref"]["NPC is"] = [(char, is_, 0, 0)]
        self.b.add(r)
        self.states[name] = sid
        self.stats["states"] += 1
        return sid

    def state_sid(self, name, ctx):
        if name in self.states:
            return self.states[name]
        found = self.db.find("world_state", name)
        if found:
            self.states[name] = found[-1]["sid"]
            return self.states[name]
        self.errors.append(f"{ctx}: unknown world state '{name}' (define it in world_states or use a game one)")
        return None

    # ---- packages --------------------------------------------------------------------------------
    def _packages(self):
        if self._pkg_cache is None:
            by_fac, bars, every = {}, set(), set()
            for r in self.db.recs.values():
                if r["type"] == 52:
                    facs = [e[0] for e in r["ref"].get("faction", [])]
                    pk = [e[0] for k in ("dialog leader", "dialog squad") for e in r["ref"].get(k, [])]
                    for f in facs:
                        by_fac.setdefault(f, set()).update(pk)
                    every.update(pk)
            for r in self.db.recs.values():
                if r["type"] == 10:
                    for e in r["ref"].get("dialog default", []):
                        by_fac.setdefault(r["sid"], set()).add(e[0])
                        every.add(e[0])
            # A faction whose squads carry no dialogue packages (and that has no faction default) speaks through its characters' own
            # `dialogue package` lists, often the shared civilian ones (the Nomads, Machinists, Kral's Chosen...). Lines written for such a
            # faction attach there too; their "my faction" check keeps everyone else who shares those packages from saying them.
            chars = {r["sid"]: r for r in self.db.recs.values() if r["type"] == 1}
            self.voice_fallback = {}
            for r in self.db.recs.values():
                if r["type"] != 52:
                    continue
                for f in [e[0] for e in r["ref"].get("faction", [])]:
                    if by_fac.get(f):
                        continue
                    for key in ("leader", "squad", "squad2"):
                        for e in r["ref"].get(key, []):
                            pk = [x[0] for x in chars.get(e[0], {"ref": {}})["ref"].get("dialogue package", [])]
                            self.voice_fallback.setdefault(f, set()).update(pk)
            for f, pk in self.voice_fallback.items():
                if not by_fac.get(f):
                    by_fac[f] = set(pk)
            squads = {r["sid"]: r for r in self.db.recs.values() if r["type"] == 52}
            for r in self.db.recs.values():
                if r["type"] == 13:
                    for e in r["ref"].get("bar squads", []):
                        sq = squads.get(e[0])
                        if sq:
                            for k in ("dialog leader", "dialog squad"):
                                bars.update(x[0] for x in sq["ref"].get(k, []))
            self._pkg_cache = (by_fac, bars, every)
        return self._pkg_cache

    def leader_packages(self, char_sid):
        """The `dialog leader` packages of every squad template (game data or made by this build) whose leader is this character."""
        if not hasattr(self, "_leads"):
            self._leads = {}
            for r in self.db.recs.values():
                if r["type"] == 52:
                    for e in r["ref"].get("leader", []):
                        self._leads.setdefault(e[0], set()).update(x[0] for x in r["ref"].get("dialog leader", []))
        found = set(self._leads.get(char_sid, set()))
        for b in getattr(self, "all_builders", [self.b]):
            for r in b.records:
                if r["type"] == 52 and any(e[0] == char_sid for e in r["ref"].get("leader", [])):
                    found.update(x[0] for x in r["ref"].get("dialog leader", []))
        return sorted(found)

    def packages_for(self, spec, faction_sids, ctx):
        by_fac, bars, every = self._packages()
        out = set()
        for f in faction_sids:
            out |= by_fac.get(f, set())
        for cname in spec.get("characters", []):
            cands = list(self.db.find("character", cname))
            made = self.created.get(("character", cname))
            if made:
                cands += [r for r in self.b.records if r["sid"] == made]
            for c in cands:
                # A squad leader speaks from its squad's `dialog leader` package first and uses its own `dialogue package` only when the
                # squad has none (Meitou docs/game/dialogue.md, from the exe), so lines for a named leader go to both; the dialogue's
                # "is character" condition keeps other speakers of those packages out.
                pk = [e[0] for e in c["ref"].get("dialogue package", [])] + self.leader_packages(c["sid"])
                if pk:
                    out.update(pk)
                else:
                    self.warnings.append(f"{ctx}: character '{cname}' has no dialogue package of its own and leads no squad with one")
        for p in spec.get("packages", []):
            if p == "player:all":
                out |= self.player_packages()
            elif p == "bar:all":
                out |= bars
            elif p == "all":
                out |= every
            else:
                made = self.created.get(("package", p))
                if made:
                    self.b.known_names[made] = p
                found = [{"sid": made}] if made else self.db.find("package", p)
                if found:
                    out.update(x["sid"] for x in found)
                else:
                    self.errors.append(f"{ctx}: no dialogue package named '{p}'")
        out = {p for p in out if self.db.get(p) or p in self.b.known_names}
        out = {self.live_package(p) for p in out}
        if spec.get("event") == "EV_BAR_TALK" and spec.get("bar_only", True) and faction_sids:
            narrowed = out & bars
            if narrowed:
                out = narrowed
        if not out:
            self.errors.append(f"{ctx}: no dialogue packages found to attach to (factions {spec.get('factions')})")
        return sorted(out)

    # ---- packages the game never reads -------------------------------------------------------------
    # Both Kenshi (FUN_14067e220, Verified in Meitou's decompile notes) and Meitou skip a DIALOGUE_PACKAGE whose name contains "Unfinished",
    # and do not follow its parents either. A squad whose `dialog leader` is "Cannibals --Unfinished" says nothing at all. Anything we attach
    # to such a package is dead. Instead we make one live package of ours per unfinished one, add it next to the unfinished one on every squad
    # template and character that lists it (so the game finds a readable package), and attach there.
    def live_package(self, sid):
        rec = self.db.get(sid)
        name = (rec or {}).get("name", "")
        if "Unfinished" not in name:
            return sid
        if not hasattr(self, "_live"):
            self._live = {}
        if sid in self._live:
            return self._live[sid]
        cur = self.b
        self.b = getattr(self, "base_builder", None) or cur
        try:
            clean = name.replace("--Unfinished---", "").replace("--Unfinished--", "").replace("--Unfinished", "").replace("--DISCARD--", "").strip(" -")
            live = B._rec(73, self.b.sid(), f"LW live: {clean}")
            self.b.add(live)
            self.b.known_names[live["sid"]] = live["name"]
            users = 0
            for r in self.db.recs.values():
                if r["type"] == 52:
                    for key in ("dialog leader", "dialog squad"):
                        if any(e[0] == sid for e in r["ref"].get(key, [])):
                            self.b.add_ref_change(r, key, [(live["sid"], 0, 0, 0)])
                            users += 1
                elif r["type"] == 1 and any(e[0] == sid for e in r["ref"].get("dialogue package", [])):
                    self.b.add_ref_change(r, "dialogue package", [(live["sid"], 0, 0, 0)])
                    users += 1
            self._live[sid] = live["sid"]
            self.stats["live_packages"] = self.stats.get("live_packages", 0) + 1
            self.warnings.append(f"package '{name}' is never read by the game (Unfinished): our dialogues go to '{live['name']}', added beside it on {users} squads/characters")
            return live["sid"]
        finally:
            self.b = cur

    def player_packages(self):
        """Every package a player character can have: CHARACTER `dialogue package player` values named for players, the player faction's
        `dialog default`, and the engine's fallback 5369-gamedata.base ("player character default")."""
        if getattr(self, "_player_pk", None) is None:
            found = set()
            for r in self.db.recs.values():
                if r["type"] == 1:
                    for e in r["ref"].get("dialogue package player", []):
                        p = self.db.get(e[0])
                        if p and re.match(r"(?i)player |player character default|beep \(player\)", p.get("name", "")):
                            found.add(e[0])
            for f in self.db.find("faction", "Nameless"):
                found |= {e[0] for e in f["ref"].get("dialog default", [])}
            if self.db.get("5369-gamedata.base"):
                found.add("5369-gamedata.base")
            self._player_pk = found
        return set(self._player_pk)

    # Shop and bar staff must keep their own talk menu (trade). An always-eligible talk dialogue of ours would replace it, so offers and
    # extra talk menus are never attached to packages that look like traders (found in play: the barman and the trade-goods vendor
    # could no longer be traded with once offers became deterministic).
    TRADE_PKG = re.compile(r"shop|barman|bartender|bar ?keep|trader|vendor|merchant|doctor|medic|smith|clerk|store|stall|caravan|cook|brewer|bench|dealer|market", re.I)

    def not_trade(self, pkgs):
        """Drop trade packages. If that would leave nothing (the giver is only ever a shopkeeper or bar staff, as for the Traders
        Guild contracts), keep them all rather than orphan the dialogue, and count it."""
        keep = []
        for p in pkgs:
            rec = self.db.get(p) if hasattr(self.db, "get") else None
            name = (rec or {}).get("name") or self.b.known_names.get(p, "")
            if self.TRADE_PKG.search(str(name)):
                self.trade_skipped = getattr(self, "trade_skipped", 0) + 1
            else:
                keep.append(p)
        if not keep and pkgs:
            self.trade_fallback = getattr(self, "trade_fallback", 0) + 1
            return list(pkgs)
        return keep

    # ---- conditions --------------------------------------------------------------------------------
    def condition(self, c, ctx):
        cond = c["cond"]
        if cond not in B.DC:
            self.errors.append(f"{ctx}: unknown condition {cond}")
            return None
        who = WHO.get(str(c.get("who", "me")), c.get("who", 0))
        cmp_ = CMP.get(c.get("cmp", "=="), None)
        if cmp_ is None:
            self.errors.append(f"{ctx}: bad comparison {c.get('cmp')}")
            return None
        value = c.get("value", 1)
        value = 1 if value is True else (0 if value is False else value)
        sid = self.b.condition(cond, who=who, compare=cmp_, tag=c.get("tag", -1), stringvar=c.get("var", ""))
        return (sid, int(value))

    # ---- reference conditions shared by dialogues and lines -----------------------------------------
    REF_FIELDS = (("my faction", "factions", "faction"), ("target faction", "target_factions", "faction"),
                  ("in town of", "in_town_of", "faction"), ("my race", "races", "race"),
                  ("target race", "target_races", "race"), ("no target race", "no_target_races", "race"),
                  ("is character", "characters", "character"), ("has package", "has_package", "package"),
                  ("target has item", "target_has_item", "item"), ("target has item type", "target_has_item_type", "item"))

    VAR_CMP = {"==": "variable equals", "=": "variable equals", "<": "variable less than", ">": "variable greater than"}

    def var_sid(self, name, ctx):
        sid = self.created.get(("variable", name))
        if not sid:
            self.errors.append(f"{ctx}: unknown variable '{name}' (declare it in the pack's \"variables\")")
        return sid

    def refs_from(self, spec, ctx):
        refs = {}
        for name, op, value in spec.get("vars", []):          # conditions on persistent variables
            sid = self.var_sid(name, ctx)
            key = self.VAR_CMP.get(op)
            if sid and key:
                refs.setdefault(key, []).append((sid, int(value)))
            elif not key:
                self.errors.append(f"{ctx}: bad variable comparison '{op}'")
        for name, count in spec.get("take_items", []):        # FCSE: the speaker takes items from the player
            sid = self.look("item", name, ctx)
            if sid:
                refs.setdefault("take item", []).append((sid, int(count)))
        for name, count in spec.get("give_items", []):        # vanilla line reference 'give item': the speaker hands the player items
            sid = self.look("item", name, ctx)
            if sid:
                refs.setdefault("give item", []).append((sid, int(count)))
        for key, field in (("add to variable", "var_add"), ("set variable", "var_set")):
            for name, value in spec.get(field, []):             # effects (lines only)
                sid = self.var_sid(name, ctx)
                if sid:
                    refs.setdefault(key, []).append((sid, int(value)))
        for key, field, kind in self.REF_FIELDS:
            sids = self.looks(kind, spec.get(field), ctx)
            if sids:
                refs[key] = [(s, 1) for s in sids]
        states = []
        for name, want in spec.get("states", []):
            sid = self.state_sid(name, ctx)
            if sid:
                states.append((sid, 1 if want else 0))
        if states:
            refs["world state"] = states
        return refs

    # ---- lines -------------------------------------------------------------------------------------
    def check_text(self, t, ctx):
        for tok in TOKEN.findall(t):
            if tok not in self.tokens and tok not in ("HIM", "HE", "YOURNAME", "PLAYERNAME", "MEUS", "MYNAME", "TARGETFACTION", "PLAYERFACTION", "COST"):
                self.warnings.append(f"{ctx}: unknown word-swap token /{tok}/ in '{t[:40]}'")
        if len(t) > 200:
            self.warnings.append(f"{ctx}: long line ({len(t)} chars): '{t[:40]}...'")
        if '"' in t:
            self.warnings.append(f"{ctx}: quotation marks in '{t[:40]}' (the game adds its own)")

    def build_line(self, ln, ctx):
        say = [""] if ln.get("silent") else (ln["say"] if isinstance(ln["say"], list) else [ln["say"]])
        if not 1 <= len(say) <= 8:
            self.errors.append(f"{ctx}: a line needs 1 to 8 variants, has {len(say)}")
            say = say[:8] or [""]
        for t in say:
            self.check_text(t, ctx)
        children = [(self.build_line(c, ctx), c.get("pct", 100)) for c in ln.get("then", [])]   # follow-ups default to always
        conds = [x for x in (self.condition(c, ctx) for c in ln.get("if", [])) if x]
        effects = []
        for e in ln.get("effects", []):
            if e[0] not in B.DA:
                self.errors.append(f"{ctx}: unknown action {e[0]}")
            else:
                effects.append((e[0], e[1] if len(e) > 1 else 0))
        refs = self.refs_from(ln, ctx)
        if ln.get("lua"):               # run these Lua scripts (declared in the pack's "lua_scripts") when the line is spoken
            sids = [self.created.get(("lua", n)) for n in ln["lua"]]
            for n, sid in zip(ln["lua"], sids):
                if not sid:
                    self.errors.append(f"{ctx}: unknown lua script '{n}' (declare it in \"lua_scripts\")")
            refs["run lua script"] = [(x, 0) for x in sids if x]
        for key, names, kind in (("change relations", ln.get("relations"), "faction"),):
            if names:
                refs[key] = [(self.look(kind, n, ctx), v) for n, v in names.items() if self.look(kind, n, ctx)]
        self.stats["lines"] += 1
        self.stats["variants"] += len(say)
        lsid = self.b.line(say, speaker=ln.get("speaker", "T_ME"), effects=effects, conditions=conds,
                           children=children, chance=ln.get("chance", 100.0), refs=refs or None,
                           repetition=ln.get("repeat", 0))
        # "lw_do": helper effects (Meitou only; meitou_target turns them into effect codes):
        # {"mark": title, "kind": "bump"|"gift"} this speaker's faction has now done that for this title (once per title per faction);
        # {"gift": item name} give one of that item to the line's target
        for e in ln.get("lw_do", []):
            if not hasattr(self, "lw_effects"):
                self.lw_effects = []
            if "mark" in e:
                if e["mark"] not in {t["id"] for t in getattr(self, "titles", [])}:
                    self.errors.append(f"{ctx}: unknown title '{e['mark']}'")
                    continue
                self.lw_effects.append((lsid, f"tempermark:{e['mark']}:{e.get('kind', 'bump')}", 1))
            elif "favour" in e:                  # {"favour": n}: the speaker's faction thinks better of the squad by n (capped: contract favours)
                self.lw_effects.append((lsid, "favour", int(e["favour"])))
            elif "alms" in e:                    # {"alms": cats}: the squad gave that many cats away (alms and offerings; squad titles count it)
                self.lw_effects.append((lsid, "alms", int(e["alms"])))
            elif "gift" in e:
                item = self.gift_sid(e["gift"], ctx)
                if item:
                    self.lw_effects.append((lsid, f"gift:{item}", int(e.get("count", 1))))
        return lsid

    def gift_sid(self, name, ctx):
        """An item a line can give: food (ITEM) or a drink or trade good (type 62), by name."""
        if ("item", name) in self.created:            # an item a pack made (trophies.json charms)
            return self.created[("item", name)]
        found = [r for r in self.db.recs.values() if r["name"] == name and r["type"] in (4, 62)]
        if not found:
            self.errors.append(f"{ctx}: no food or trade item named '{name}'")
            return None
        return found[-1]["sid"]

    # ---- dialogues ---------------------------------------------------------------------------------
    CRIER_OPENERS = ["Hear the news! Hear it!", "News from the roads, for any with ears!", "Listen! Word has come.",
                     "Gather round. Here's what's being said.", "Hear this, travellers!", "News! Fresh news!"]

    NATION_HEADS = ("Holy Lord Phoenix", "Emperor Tengu", "Esata the Stone Golem")
    _anchors = None

    def boss_anchor(self, dialogue_id):
        """The home town of the Atlas person a dialogue is about (by its id prefix), or None."""
        if Compiler._anchors is None:
            try:
                a = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "content", "_atlas.json"), encoding="utf-8"))
            except OSError:
                a = {"people": {}, "places": {}}
            places, rows = a.get("places", {}), []
            for name, x in a.get("people", {}).items():
                if name in self.NATION_HEADS or not x.get("rumours"):
                    continue
                town = x.get("place")
                if not town:
                    r = (x.get("region") or "").lower()
                    town = next((n for n, pl in places.items() if r and pl.get("region", "").lower() in r and pl.get("region")), None)
                if town:
                    rows += [(pre, town) for pre in x["rumours"]]
            Compiler._anchors = sorted(rows, key=lambda t: -len(t[0]))
        return next((t for pre, t in Compiler._anchors if dialogue_id.startswith(pre)), None)

    @staticmethod
    def crier_news(dialogue_id):
        """Whether a townsfolk news line is worth a crier calling it out (the speakers already are the factions it concerns):
        fresh rumours of what happened (rm_*_fresh), wars and marches (wn_*), an outlaw raiding or gathering men (on_*_1/_3_sight),
        a harassment raid out or beaten (on_*_out/_caught_sight). Not: older rumours, outlaws lying low or licking wounds, where a camp
        is, the squad's own fights or titles (nt_*): those stay gossip."""
        i = dialogue_id
        return (i.startswith("rm_") and i.endswith("_fresh")) or i.startswith("wn_") or (
            i.startswith("on_") and i.endswith(("_1_sight", "_3_sight", "_out_sight", "_caught_sight")))

    def build_dialogue(self, d):
        ctx = f"dialogue '{d.get('id')}'"
        # Town criers (Meitou only, Criers.cs): every piece of news townsfolk pass on (rumours, outlaw news, war news, title talk) also gets a
        # crier's copy on EV_I_SEE_PLAYER_NICE_BUILDING, an event neither vanilla data nor Meitou uses, which the helper fires on a local
        # it picks as the town's crier: an opening call, then the news, always said when its conditions hold (same id prefix, so the gossip
        # slider still applies), each piece at most once a game day. Only news a town would call out (crier_news): no talk about the squad's
        # own titles or fights, no stale rumours, no idle outlaw gossip.
        if (getattr(self, "target", "kenshi") == "meitou" and d.get("event") == "EV_I_SEE_NEUTRAL_SQUAD" and not d.get("_cry")
                and self.crier_news(str(d.get("id", "")))):
            cry = {k: v for k, v in d.items() if k not in ("lines",)}
            cry.update({"id": f"{d['id']}_cry", "event": "EV_I_SEE_PLAYER_NICE_BUILDING", "chance": 100, "repeat": 24, "score": 10, "_cry": True,
                        "lines": [{"say": self.CRIER_OPENERS, "then": [dict(ln) for ln in d["lines"]]}]})
            self.build_dialogue(cry)
            self.stats["crier_news"] = self.stats.get("crier_news", 0) + 1
        # "lw_if": helper-answered conditions (Meitou only): {"title": id, "who": "target"} the subject holds a notoriety title;
        # {"heat": "<"|">"|"=", "value": n} the heat the speaker's town feels; {"outlaw": name, "doing": 1-4} and {"war": faction, "player": bool}
        # (WorldNews.cs). The Kenshi build has no helper, so it leaves such dialogues out.
        lw_if = d.get("lw_if") or []
        # Talk about a boss stays near the boss's home (Meitou only; the Atlas's people, content/_atlas.json): a line whose id belongs to a
        # person with a home town (or a town in their region) is said only within reach of that town; the nation heads are talked about
        # everywhere, and news of the squad's own deeds (rm_) is distance-gated by its own rules.
        if getattr(self, "target", "kenshi") == "meitou" and not str(d.get("id", "")).startswith("rm_"):
            anchor = self.boss_anchor(str(d.get("id", "")))
            if anchor:
                d = dict(d, lw_if=list(lw_if) + [{"near_place": anchor}])
                lw_if = d["lw_if"]
        if (lw_if or d.get("meitou_only")) and getattr(self, "target", "kenshi") != "meitou":
            self.stats["meitou_only_skipped"] = self.stats.get("meitou_only_skipped", 0) + 1
            return
        for c in lw_if:
            if "not_done" in c and c["not_done"] not in {t["id"] for t in getattr(self, "titles", [])}:
                self.errors.append(f"{ctx}: unknown title '{c['not_done']}'")
                return
            if "title" in c and c["title"] not in {t["id"] for t in getattr(self, "titles", [])}:
                self.errors.append(f"{ctx}: unknown title '{c['title']}'")
                return
            if "war" in c and not self.db.find("faction", c["war"]):
                self.errors.append(f"{ctx}: no faction named '{c['war']}'")
                return
        if d.get("event") not in B.EV:
            self.errors.append(f"{ctx}: unknown event {d.get('event')}")
            return
        if d.get("no_target_races"):
            # "no target race" is a field the FCS offers but neither Kenshi's exe nor Meitou ever reads (Meitou docs/game/dialogue.md), so the
            # exclusion silently does nothing. List the races that may be targeted with target_races instead.
            self.errors.append(f"{ctx}: no_target_races is never read by the game; use target_races with the allowed races")
            return
        if (d.get("target_has_item") or d.get("target_has_item_type")) and d["event"] != "EV_PLAYER_TALK_TO_ME":
            # BFrizz's checkConditions hook uses the speaker's platoon for item checks without a null check: on "I see" events a missing
            # speaker or platoon can null-read (see plugin/SESSION_LEARNINGS.md), so item checks are allowed on talk events only.
            self.errors.append(f"{ctx}: target_has_item is only allowed on EV_PLAYER_TALK_TO_ME, not {d['event']}")
            return
        facs = self.looks("faction", d.get("factions"), ctx)
        refs = self.refs_from(d, ctx)
        extra = list(d.get("if", []))
        if d.get("player_only"):             # the line is aimed at the player, not at any passing NPC squad
            extra.append({"cond": "DC_IS_PLAYER", "who": "target", "value": 1})
        if d.get("relation"):                # shorthand: ["<", -20] is my relation to the target
            extra.append({"cond": "DC_RELATIONS", "who": "me", "cmp": d["relation"][0], "value": d["relation"][1]})
        if d.get("personality"):             # one personality tag name of the speaker (see PERSONALITY below)
            tag = PERSONALITY.get(d["personality"])
            if tag is None:
                self.errors.append(f"{ctx}: unknown personality '{d['personality']}'")
            else:
                extra.append({"cond": "DC_PERSONALITY_TAG", "who": "me", "tag": tag, "value": 1})
        conds = [x for x in (self.condition(c, ctx) for c in extra) if x]
        lines = [(self.build_line(l, ctx), l.get("pct", 0)) for l in d["lines"]]    # root lines are alternatives (0)
        if not lines:
            self.errors.append(f"{ctx}: no lines")
            return
        if d.get("packages_only"):           # keep the faction as a "my faction" check but do not attach to the faction's own packages
            facs = []
        chance, score = d.get("chance", 100.0), d.get("score", 0)
        forced_flag = False
        if d.get("event") == "EV_PLAYER_TALK_TO_ME" and chance < 100:
            chance = 100.0                      # see OFFER_TAGS: a random chance on a talk dialogue flickers the talk cursor
            # NOTE: never use a negative score. No vanilla dialogue has one, and a score of -1 crashed the game while it was
            # loading the data (null read in the game executable, right after the mod list loaded).
            self.forced_talk = getattr(self, "forced_talk", 0) + 1
            forced_flag = True
        if score < 0:
            self.errors.append(f"{ctx}: negative score {score} is not allowed (it crashed the game on load)")
            return
        sid = self.b.dialogue(d["id"], lines, conditions=conds, refs=refs, monologue=d.get("monologue", True),
                              repetition=d.get("repeat", 0), chance=chance,
                              one_at_a_time=d.get("one_at_a_time", d.get("event") in ("EV_I_SEE_NEUTRAL_SQUAD", "EV_I_SEE_ENEMY_PLAYER", "EV_I_SEE_ALLY_PLAYER")), locked=d.get("locked", False), score_bonus=score,
                              for_enemies=d.get("for_enemies", False), target_type=d.get("target_type", 0))
        if d.get("zones"):                 # a zone's arrival dialogue (the player squad speaks when it enters the zone)
            for z in d["zones"]:
                zs = self.db.find("zone", z)
                if not zs:
                    self.errors.append(f"{ctx}: no zone named '{z}'")
                elif zs[-1]["bool"].get("REMOVED"):
                    self.warnings.append(f"{ctx}: zone '{z}' is flagged REMOVED in the game data: skipped")
                else:
                    self.b.attach_zone(zs[-1]["sid"], sid)
                    self.stats["attached"] += 1
        else:
            pkgs = self.packages_for(d, facs, ctx)
            if d.get("event") == "EV_PLAYER_TALK_TO_ME" and forced_flag:
                pkgs = self.not_trade(pkgs)
            for pkg in pkgs:
                self.b.attach(pkg, sid, d["event"])
                self.stats["attached"] += 1
        self.stats["dialogues"] += 1
        if lw_if:
            if not hasattr(self, "lw_marks"):
                self.lw_marks = []
            for c in lw_if:
                who = {"me": 0, "target": 1}[c.get("who", "target" if "title" in c else "me")]
                cmp = {"=": 0, "<": 1, ">": 2}[c.get("cmp", "=")]
                if "title" in c:
                    self.lw_marks.append((sid, "title:" + c["title"], who, 0, int(c.get("value", 1))))
                elif "outlaw" in c:                  # {"outlaw": name, "doing": 1..4}: what that outlaw is up to (WorldNews.cs)
                    self.lw_marks.append((sid, "outlaw:" + c["outlaw"], 0, 0, int(c["doing"])))
                elif "war" in c:                     # {"war": faction, "player": false}: that faction is on campaign (against the player's towns)
                    self.lw_marks.append((sid, f"war:{c['war']}:{1 if c.get('player') else 0}", 0, 0, 1))
                elif "named" in c:                   # {"named": record name}: the squad killed that named character (bleed-outs included)
                    self.lw_marks.append((sid, "named:" + c["named"], 0, 0, 1))
                elif "named_days" in c:              # {"named_days": name, "cmp": "<"|">", "value": n}: game days since the squad killed them
                    self.lw_marks.append((sid, "nameddays:" + c["named_days"], 0, {"=": 0, "<": 1, ">": 2}[c["cmp"]], int(c["value"])))
                elif "not_done" in c:                # {"not_done": title, "kind": "bump"|"gift"}: the speaker's faction has not yet done that
                    self.lw_marks.append((sid, f"temperonce:{c['not_done']}:{c.get('kind', 'bump')}", 0, 0, 1))
                elif "despised" in c:                # {"despised": 1|0}: the target holds a title the speaker's faction despises
                    self.lw_marks.append((sid, "despised", 1, 0, int(c["despised"])))
                elif "fallen_target" in c:           # {"fallen_target": 1}: the target is a dead squadmate (memorials)
                    self.lw_marks.append((sid, "mourn", 1, 0, int(c["fallen_target"])))
                elif "scar_here" in c:               # {"scar_here": 1 arm | 2 leg | 3 knocked down}: this town hurt the speaker before
                    self.lw_marks.append((sid, "scar", 0, 0, int(c["scar_here"])))
                elif "feud_target" in c:             # {"feud_target": 1}: the target's faction killed one of the squad (memorials)
                    self.lw_marks.append((sid, "feud", 1, 0, int(c["feud_target"])))
                elif "loss_days" in c:               # {"loss_days": "<"|">", "value": n}: game days since the squad last lost someone
                    self.lw_marks.append((sid, "lossdays", 0, {"=": 0, "<": 1, ">": 2}[c["loss_days"]], int(c["value"])))
                elif "squad_near" in c:              # {"squad_near": "<"|">"|"=", "value": n}: other standing squad members within 50 m of the speaker
                    self.lw_marks.append((sid, "squadnear", 0, {"=": 0, "<": 1, ">": 2}[c["squad_near"]], int(c["value"])))
                elif "raid" in c:                    # {"raid": name, "value": 1 out | 2 caught | 3 got away}: a harassment raid's state (Raids.cs)
                    self.lw_marks.append((sid, "raid:" + c["raid"], 0, 0, int(c["value"])))
                elif "news" in c:                    # {"news": k}: news slot k (other mods' news, News.cs) is live near the speaker
                    self.lw_marks.append((sid, f"news:{int(c['news'])}", 0, 0, 1))
                elif "rival" in c:                  # {"rival": k, "cmp", "value"}: rival slot k (Rivals.cs): 0 none near, 1 about, 2 has beaten the squad
                    self.lw_marks.append((sid, f"rival:{int(c['rival'])}", 0, {"=": 0, "<": 1, ">": 2}[c.get("cmp", ">")], int(c.get("value", 0))))
                elif "successor" in c:              # {"successor": outlaw}: that outlaw's successor has taken over and is alive
                    self.lw_marks.append((sid, "successor:" + c["successor"], 0, 0, 1))
                elif "near_place" in c:              # {"near_place": town}: the speaker is near that town (talk about a boss, Atlas homes)
                    self.lw_marks.append((sid, "nearplace:" + c["near_place"], 0, 0, 1))
                    self.stats["near_place"] = self.stats.get("near_place", 0) + 1
                elif "marching" in c:                # {"marching": 1}: the target is in a nation's campaign on its way to a town (Marches.cs)
                    self.lw_marks.append((sid, "marching", 1, 0, int(c["marching"])))
                elif "tale" in c:                    # {"tale": title id}: someone of the squad (or the squad itself) holds that title (tavern tales)
                    self.lw_marks.append((sid, "tale:" + c["tale"], 0, 0, 1))
                elif "reprisal" in c:                # {"reprisal": outlaw}: the speaker is one of the outlaw's reprisal party
                    self.lw_marks.append((sid, "reprisal:" + c["reprisal"], 0, 0, 1))
                elif "bounty_tier" in c:             # {"bounty_tier": outlaw, "value": 1-3}: how far the outlaw's price has grown (growing bounties)
                    self.lw_marks.append((sid, "tier:" + c["bounty_tier"], 0, {"=": 0, "<": 1, ">": 2}[c.get("cmp", "=")], int(c["value"])))
                elif "camp" in c:                    # {"camp": outlaw}: the helper knows where that outlaw's camp is (OutlawCamps.cs)
                    self.lw_marks.append((sid, "camptown:" + c["camp"], 0, 2, 0))
                elif "struck" in c:                  # {"struck": outlaw, "cmp": "<", "value": days}: days since the squad hit them and they lived
                    self.lw_marks.append((sid, "struck:" + c["struck"], 0, {"=": 0, "<": 1, ">": 2}[c.get("cmp", "<")], int(c["value"])))
                else:
                    self.lw_marks.append((sid, "heat", who, {"=": 0, "<": 1, ">": 2}[c["heat"]], int(c["value"])))
            # News of a named outlaw travels only so far: a line about one (what they are up to, where their camp is, their price, a fight
            # with the squad) is said only by someone within the "Outlaw news range" of the outlaw's ground (5 km by default: the map is
            # about 30 km across); bar talk and criers included (found in play: one town's bar filled the Atlas with
            # outlaws from across the map).
            for o in dict.fromkeys(c.get(k) for c in lw_if for k in ("outlaw", "camp", "struck", "bounty_tier", "successor") if isinstance(c.get(k), str)):
                self.lw_marks.append((sid, f"newsreach:{o}", 0, 0, 1))
                self.stats["news_reach"] = self.stats.get("news_reach", 0) + 1



    # ---- cloning and changing game records (new factions, camps, squads, leaders) -----------------------
    KEYKIND = {"relations": "faction", "faction": "faction", "bounty factions": "faction", "squad default": "squad",
               "residents": "squad", "bar squads": "squad", "roaming squads": "squad", "special squads": "squad",
               "housemates": "squad", "leader": "character", "squad": "character", "squad2": "character",
               "dialog squad": "package", "dialog leader": "package", "dialogue package": "package",
               "nests": "town", "biomes": "zone", "no-go zones": "zone", "homeless spawns": "squad", "AI packages": "ai_package", "dialogs": "dialogue",
               "weapons": "weapon", "clothing": "armour", "inventory": "item", "death items": "item", "campaigns": "campaign",
               "race": "race", "dialogue package player": "package", "dialogue": "dialogue",
               "town": "town", "faction relations": "faction", "force race": "race", "stats": "stats"}      # keys used by start records

    def resolve_entries(self, key, entries, ctx, kinds=None):
        kind = (kinds or {}).get(key) or self.KEYKIND.get(key)
        out = []
        for e in entries:
            name = e[0]
            vals = list(e[1:]) + [0] * (3 - len(e[1:]))
            if kind:
                sid = self.look(kind, name, ctx)
                if not sid:
                    continue
            else:
                sid = name
            out.append((sid, int(vals[0]), int(vals[1]), int(vals[2])))
        return out

    def make_clone(self, spec):
        """{"clone": {"kind", "from", "name", "bool"/"int"/"float"/"str": {...}, "ref": {key: [[name, v..]..]}, "drop": [keys]}}"""
        ctx = f"clone '{spec.get('name')}'"
        src = self.db.find(spec["kind"], spec["from"])
        if not src:
            self.errors.append(f"{ctx}: no {spec['kind']} named '{spec['from']}' to copy")
            return
        r = self.clone(src[-1], spec["name"])
        for sec in ("bool", "int", "float", "str"):
            r[sec].update(spec.get(sec, {}))
        for k in spec.get("drop", []):
            r["ref"].pop(k, None)
        kinds = spec.get("ref_kinds")          # per-record override of what a ref key points at (a start's "squad" is a squad, not a character)
        for k, entries in spec.get("ref", {}).items():
            r["ref"][k] = self.resolve_entries(k, entries, ctx, kinds)
        for k, entries in spec.get("ref_add", {}).items():
            r["ref"].setdefault(k, []).extend(self.resolve_entries(k, entries, ctx, kinds))
        self.b.add(r)
        self.created[(spec["kind"], spec["name"])] = r["sid"]
        self.stats["clones"] = self.stats.get("clones", 0) + 1
        if spec["kind"] == "character":
            for key, is_ in (("killed", "dead"), ("imprisoned", "imprisoned")):
                if spec.get("states", True):
                    self.make_state({"name": f"{spec['name']} {key} by player", "npc": spec["name"], "is": is_, "player": True})

    def make_package(self, spec):
        """A new dialogue package (a list of dialogues an NPC type can say). `dialogues` are filled in as authors attach to it."""
        r = B._rec(73, self.b.sid(), spec["name"])
        for k in spec.get("inherits", []):
            sid = self.look("package", k, f"package '{spec['name']}'")
            if sid:
                r["ref"].setdefault("inheritsFrom", []).append((sid, 0, 0, 0))
        r["str"] = {"tag": spec.get("tag", "")}
        self.b.add(r)
        self.created[("package", spec["name"])] = r["sid"]

    def make_change(self, spec):
        """{"change": {"kind", "name", "int"/"float"/"bool": {...}, "ref_add": {key: [[name, v..]..]}}} on an existing record."""
        ctx = f"change '{spec.get('name')}'"
        made = self.created.get((spec["kind"], spec["name"]))
        if made:      # a record this build created: edit it in place
            rec = next(r for r in self.b.records if r["sid"] == made)
            for sec in ("bool", "int", "float", "str"):
                rec[sec].update(spec.get(sec, {}))
            for k, v in spec.get("ref_add", {}).items():
                rec["ref"].setdefault(k, []).extend(self.resolve_entries(k, v, ctx))
            self.stats["changes"] = self.stats.get("changes", 0) + 1
            return
        found = self.db.find(spec["kind"], spec["name"])
        if not found:
            self.errors.append(f"{ctx}: no {spec['kind']} named '{spec['name']}'")
            return
        refs = {k: self.resolve_entries(k, v, ctx) for k, v in spec.get("ref_add", {}).items()}
        self.b.add_change(found[-1], ints=spec.get("int"), floats=spec.get("float"), bools=spec.get("bool"), refs=refs)
        self.stats["changes"] = self.stats.get("changes", 0) + 1

    # ---- lore items: new books/notes cloned from a vanilla item, placed in vanilla item lists ----------
    def make_lore_item_lw(self, spec):
        """{"id": "li_x", "name": "...", "from": "vanilla item to copy", "description": "...", "int"/"float"/"bool": {...},
        "place": [["item list name", weight], ...]}. The copy keeps the template's icon, mesh, size and sound;
        "file": {"icon": ...} replaces the icon (a template with none, such as Coin, otherwise leaves the item invisible in the bag).
        A placement adds the item to an existing item list (type 49: shop stock, house contents, loot); the number is its
        weight against that list's other entries (vanilla books use 100; lower is rarer)."""
        name = spec["name"]
        ctx = f"lore item '{name}'"
        desc = spec.get("description", "")
        if not 40 <= len(desc) <= 400:
            self.warnings.append(f"{ctx}: description is {len(desc)} characters")
        kind = spec.get("kind", "item")                      # "item" (books, trinkets), "weapon" or "armour" (stats via int/float)
        src = self.db.find(kind, spec["from"])
        if not src:
            self.errors.append(f"{ctx}: no {kind} named '{spec['from']}' to copy")
            return
        r = self.clone(src[-1], name)
        for sec in ("bool", "int", "float"):
            r[sec].update(spec.get(sec, {}))
        r["str"] = dict(r["str"])
        r["str"].update(spec.get("str", {}))
        r["str"]["description"] = desc.replace("\r\n", "\n").replace("\n", "\r\n")
        if spec.get("file"):                                 # e.g. {"icon": ...}: Coin has no icon, so its copies showed blank
            r["file"] = dict(r["file"])
            r["file"].update(spec["file"])
        self.b.add(r)
        self.created[("item", name)] = r["sid"]
        self.stats["clones"] = self.stats.get("clones", 0) + 1
        for lst, weight in spec.get("place", []):
            found = self.db.by_name.get((49, lst.lower()))
            if not found:
                self.errors.append(f"{ctx}: no item list named '{lst}'")
                continue
            self.b.add_ref_change(found[-1], "items", [(r["sid"], int(weight), 0, 0)])
            self.stats["changes"] = self.stats.get("changes", 0) + 1

    def make_item_list_lw(self, spec):
        """{"name", "count", "items": [[item, weight], ...], "squads": [[squad name, chance], ...]}: a new item list (type 49)
        made from a copy of an existing one, added to the `vendors` of the named squads (their buildings' contents)."""
        name = spec["name"]
        ctx = f"item list '{name}'"
        tmpl = self.db.by_name.get((49, "ancient lab"))
        if not tmpl:
            self.errors.append(f"{ctx}: template list 'ancient lab' not found")
            return
        r = self.clone(tmpl[-1], name)
        entries = []
        for item, weight in spec["items"]:
            sid = self.created.get(("item", item)) or (self.look("item", item, ctx))
            if sid:
                entries.append((sid, int(weight), 0, 0))
        r["ref"] = {"items": entries}
        r["int"]["items count"] = int(spec.get("count", 1))
        self.b.add(r)
        self.created[("item list", name)] = r["sid"]
        for sq, chance in spec.get("squads", []):
            found = self.db.by_name.get((52, sq.lower()))
            if not found:
                self.errors.append(f"{ctx}: no squad named '{sq}'")
                continue
            self.b.add_ref_change(found[-1], "vendors", [(r["sid"], int(chance), 0, 0)])
            self.stats["changes"] = self.stats.get("changes", 0) + 1

    # ---- named outlaws ------------------------------------------------------------------------------
    @staticmethod
    def b_ref_add(rec, key, entry):
        rec["ref"] = dict(rec.get("ref", {}))
        rec["ref"][key] = list(rec["ref"].get(key, [])) + [entry]

    def clone(self, rec, name, sid=None):
        import copy
        r = copy.deepcopy({k: v for k, v in rec.items() if k != "src"})
        r["name"] = name
        r["sid"] = sid or self.b.sid()
        r["id"] = 0
        r["flags"] = B.NEW
        r["tail"] = 0
        return r

    def make_outlaw(self, o):
        """A named, unique character with a bounty, its roaming squad, and spawn entries in the zones' ground biomes."""
        ctx = f"outlaw '{o.get('name')}'"
        name = o["name"]
        tmpl = self.db.find("character", o["template"])
        sq_t = self.db.find("squad", o["squad_template"])
        if not tmpl or not sq_t:
            self.errors.append(f"{ctx}: template character '{o['template']}' or squad '{o['squad_template']}' not found")
            return
        ch = self.clone(tmpl[-1], name)
        ch["bool"].update({"unique": 1, "named": 0})
        ch["float"]["faction importance"] = float(o.get("importance", 0.4))
        bounty = int(o.get("bounty", 3000))
        ch["int"].update({"bounty amount": bounty, "bounty amount fuzz": 0, "bounty chance": 100})
        if "combat" in o:
            ch["int"]["combat stats"] = int(o["combat"])
        if o.get("female") is not None:
            ch["int"]["female chance"] = 100 if o["female"] else 0
        payers = self.looks("faction", o.get("bounty_factions", []), ctx)
        if payers:
            ch["ref"]["bounty factions"] = [(f, 0, 0, 0) for f in payers]
        for key, kind, field in (("weapons", "weapon", "weapons"), ("clothing", "armour", "clothing")):
            if o.get(field):
                sids = self.looks(kind, o[field], ctx)
                ch["ref"][key] = [(x, 1, 100, 0) for x in sids]
        self.b.add(ch)
        self.created[("character", name)] = ch["sid"]
        squad = self.clone(sq_t[-1], f"{name} squad")
        squad["ref"]["leader"] = [(ch["sid"], 1, 0, 0)]
        if o.get("henchmen"):
            hs = []
            for hname, lo, hi in o["henchmen"]:
                h = self.look("character", hname, ctx)
                if h:
                    hs.append((h, lo, hi, 0))
            squad["ref"]["squad"] = hs
        if o.get("faction"):
            f = self.look("faction", o["faction"], ctx)
            if f:
                squad["ref"]["faction"] = [(f, 0, 0, 0)]
        self.b.add(squad)
        self.created[("squad", f"{name} squad")] = squad["sid"]
        for bname in o.get("biomes", []):
            b = self.db.find("biome", bname)
            if not b:
                self.errors.append(f"{ctx}: no ground biome named '{bname}'")
                continue
            # Meitou never reads terrain-biome spawn lists; Quest Log & Town Map would list them as places the outlaw roams. Kenshi keeps them.
            if getattr(self, "target", "kenshi") != "meitou":
                self.b.add_ref_change(b[-1], "homeless spawns", [(squad["sid"], int(o.get("weight", 2)), 100, 0)])
        # Zones (biome groups, Kenshi's named regions). The wild-squad draw reads "homeless spawns" from these: Meitou never reads the
        # terrain-biome lists above, and its docs (decompile-verified) describe the original exe's draw per zone too, so the outlaw is
        # added to its zones on both targets. The terrain-biome entries stay (harmless if unread).
        zone_recs = []
        for zname in o.get("zones", []):
            zs = self.db.find("zone", zname)
            if not zs:
                self.errors.append(f"{ctx}: no zone named '{zname}'")
            elif zs[-1]["bool"].get("REMOVED"):
                self.warnings.append(f"{ctx}: zone '{zname}' is flagged REMOVED in the game data: skipped")
            else:
                zone_recs.append(zs[-1])
        zone_sids = [z["sid"] for z in zone_recs]
        if not zone_recs:
            self.warnings.append(f"{ctx}: no zones, so it probably never spawns (zones carry the wild-squad spawn lists)")
        for z in zone_recs:
            self.b.add_ref_change(z, "homeless spawns", [(squad["sid"], int(o.get("weight", 2)), 0, 0)])
        # for the Meitou helper's moving camps (OutlawCamps.cs): the outlaw, its squad and its zones
        if not hasattr(self, "camps"):
            self.camps = []
        camp = {"name": name, "character": ch["sid"], "squad": squad["sid"], "biomes": zone_sids}
        if getattr(self, "target", "kenshi") == "meitou":
            # the outlaw's token (trophies, Meitou only): in the outlaw's inventory (not death items: taking one of those off a downed
            # character kills them), worn in the helper's neck slot (FCS slot 11, ATTACH_NECK)
            src = self.db.find("item", "Animal Teeth")
            if src:
                tok = self.clone(src[-1], f"{name}'s Token")
                tok["int"] = dict(tok["int"], **{"slot": 11, "stackable": 1, "value": max(50, int(o.get("bounty", 1000)) // 10)})
                tok["str"] = dict(tok["str"], description=(f"A cord of teeth and stamped brass that {name} wore at the throat. Proof enough "
                                                           "for any bounty clerk, and a warning to anyone who sees it on you.\r\n\r\nWorn in the neck slot."))
                self.b.add(tok)
                self.created[("item", tok["name"])] = tok["sid"]
                self.b_ref_add(ch, "inventory", (tok["sid"], 1, 0, 0))
            else:
                self.errors.append(f"{ctx}: no item 'Animal Teeth' for the outlaw's token")
            # the men an outlaw sends after the squad (outlaw reprisals, OutlawCamps.cs): the outlaw's own squad without the outlaw, so
            # the unique character is never made twice; Meitou only (the Kenshi build is unchanged)
            rep = self.clone(sq_t[-1], f"{name} reprisal")
            if squad["ref"].get("squad"):
                rep["ref"]["squad"] = list(squad["ref"]["squad"])
            if squad["ref"].get("faction"):
                rep["ref"]["faction"] = list(squad["ref"]["faction"])
            self.b.add(rep)
            camp["reprisal"] = rep["sid"]
            if name == "Sayo of the Grey Ribbon" and squad["ref"].get("faction"):
                # Reserved ids leave every previously shipped outlaw, dialogue and equipment id in place.
                scout = self.clone(tmpl[-1], "Grey Ribbon Infiltrator", f"950000-{self.b.file}")
                scout["bool"].update({"unique": 0, "named": 0})
                scout["float"]["faction importance"] = 0.0
                scout["int"].update({"combat stats": 52, "stealth stats": 65, "bounty amount": 0, "bounty chance": 0})
                scout["ref"].pop("stats", None)
                self.b.add(scout)
                cell = self.clone(sq_t[-1], "Grey Ribbon Recovery Cell", f"950001-{self.b.file}")
                cell["ref"] = {
                    "faction": list(squad["ref"]["faction"]),
                    "leader": [(scout["sid"], 1, 0, 0)],
                    "squad": [(scout["sid"], 0, 1, 0)],
                }
                cell["bool"] = {"dont multiply": 1}
                cell["int"] = {}
                self.b.add(cell)
                camp["infiltrators"] = cell["sid"]
            # the successor (Meitou only, OutlawCamps.cs): some days after the outlaw dies or is jailed, one of the gang takes over the same
            # ground under their own name. A unique character and squad of its own, never in a spawn list: only the helper places it.
            if o.get("successor"):
                sc = o["successor"]
                sch = self.clone(tmpl[-1], sc["name"])
                sch["bool"].update({"unique": 1, "named": 0})
                sch["float"]["faction importance"] = float(o.get("importance", 0.4))
                sb = max(500, int(bounty * 0.6) // 100 * 100)
                sch["int"].update({"bounty amount": sb, "bounty amount fuzz": 0, "bounty chance": 100, "female chance": 100 if sc.get("female") else 0})
                if "combat" in o:
                    sch["int"]["combat stats"] = max(1, int(o["combat"]) - 5)
                if payers:
                    sch["ref"]["bounty factions"] = [(f, 0, 0, 0) for f in payers]
                for key in ("weapons", "clothing"):
                    if ch["ref"].get(key):
                        sch["ref"][key] = list(ch["ref"][key])
                self.b.add(sch)
                self.created[("character", sc["name"])] = sch["sid"]
                ssq = self.clone(sq_t[-1], f"{sc['name']} squad")
                ssq["ref"]["leader"] = [(sch["sid"], 1, 0, 0)]
                for key in ("squad", "faction"):
                    if squad["ref"].get(key):
                        ssq["ref"][key] = list(squad["ref"][key])
                self.b.add(ssq)
                camp["successor"] = {"name": sc["name"], "character": sch["sid"], "squad": ssq["sid"], "bounty": sb}
                for key, is_ in (("killed", "dead"), ("imprisoned", "imprisoned")):
                    self.make_state({"name": f"{sc['name']} {key} by player", "npc": sc["name"], "is": is_, "player": True})
        self.camps.append(camp)
        for key, is_ in (("killed", "dead"), ("imprisoned", "imprisoned")):
            self.make_state({"name": f"{name} {key} by player", "npc": name, "is": is_, "player": True})
        self.stats["outlaws"] = self.stats.get("outlaws", 0) + 1

    # ---- contracts (a quest chain made of dialogue unlocks) -------------------------------------------
    def make_contract(self, c):
        """An offer the player can accept from a contract giver, and a report-back that pays when the target's
        world state is true. Progress is a persistent variable (BFrizz Extra Extensions): dialogue locks are stored
        per character, so they cannot carry a quest from one NPC to another."""
        ctx = f"contract '{c.get('id')}'"
        state = self.state_sid(c["state"], ctx)
        if not state:
            return
        reward = int(c.get("reward", 1000))
        var = self.new_var(f"Contract {c['id']}")
        jobs = self.jobs_var()
        report_line = self.b.line(c["report"], effects=[("DA_GIVE_MONEY", reward)] + [tuple(e) for e in c.get("report_effects", [])],
                                  refs={"set variable": [(var, 2)], "add to variable": [(jobs, -1)]})
        report = self.b.dialogue(f"{c['id']} - report", [(report_line, 0)], monologue=True,
                                 refs={"world state": [(state, 1)], "variable equals": [(var, 1)]}, score_bonus=10)
        accept = self.b.line(c["accept"], speaker="T_TARGET_IF_PLAYER", children=[(self.b.line(c["accepted"]), 100)],
                             refs={"set variable": [(var, 1)], "add to variable": [(jobs, 1)]})
        decline = self.b.line(c.get("decline", ["Not now."]), speaker="T_TARGET_IF_PLAYER")
        offer_line = self.b.line(with_reward(c["offer"], reward), children=[(accept, 0), (decline, 0)])
        facs = self.looks("faction", c.get("factions"), ctx)
        refs = {"my faction": [(f, 1) for f in facs]} if facs else {}
        refs["world state"] = [(state, 0)]
        refs["variable equals"] = [(var, 0)]
        refs["variable less than"] = [(jobs, JOB_CAP)]
        for k, field, kind in (("in town of", "in_town_of", "faction"),):
            sids = self.looks(kind, c.get(field), ctx)
            if sids:
                refs[k] = [(x, 1) for x in sids]
        tagc = self.condition({"cond": "DC_PERSONALITY_TAG", "who": "me", "tag": offer_tag(c["id"]), "value": 1}, f"contract '{c['id']}'")
        notrader = self.condition({"cond": "DC_IS_A_TRADER", "who": "me", "value": 0}, f"contract '{c['id']}'")   # barmen and shopkeepers never offer work
        # Faction rank (native DC 10 / DA 35, vanilla uses it only for the Shinobi Thieves): every paid contract raises the player's rank with
        # the giver's faction, up to RANK_CAP, and the dangerous contracts are only offered once the player has a rank there.
        need = rank_needed(reward)
        conds = [x for x in (tagc, notrader) if x]
        roots = [(offer_line, 0)]
        if need:
            # the gate lives on the lines, not the dialogue, so the same talk slot shows a refusal instead of silently vanishing
            rc_ok = self.condition({"cond": "DC_FACTION_RANK", "who": "me", "cmp": ">", "value": need - 1}, ctx)
            rc_no = self.condition({"cond": "DC_FACTION_RANK", "who": "me", "cmp": "<", "value": need}, ctx)
            for r in self.b.records:
                if r["sid"] == offer_line and rc_ok:
                    r["ref"]["conditions"] = list(r["ref"].get("conditions", [])) + [(rc_ok[0], rc_ok[1], 0, 0)]
            gate_line = self.b.line(c.get("rank_gate", RANK_GATE_TEXT), conditions=[rc_no] if rc_no else ())
            roots.append((gate_line, 0))
            self.stats["lines"] += 1
        target_outlaw = c["state"][:-len(" killed by player")] if c["state"].endswith(" killed by player") else None
        if getattr(self, "target", "kenshi") == "meitou" and target_outlaw:
            # distance (Meitou helper, OutlawCamps.cs): only givers within reach of the outlaw's ground offer the job, and the report pays
            # a bonus that grows with the distance from the giver to it
            if not hasattr(self, "lw_effects"):
                self.lw_effects = []
            self.lw_effects.append((report_line, f"distpay:{target_outlaw}", reward))
            self.pending_reach = getattr(self, "pending_reach", []) + [target_outlaw]
        if getattr(self, "target", "kenshi") == "meitou":
            # a faction does not hire someone holding a title it despises (TITLES_BY_FACTION.md): the offer line needs "not despised",
            # and a refusal takes its slot otherwise
            if not hasattr(self, "lw_marks"):
                self.lw_marks = []
            self.lw_marks.append((offer_line, "despised", 1, 0, 0))
            refuse = self.b.line(c.get("refuse", ["We don't hire your kind.", "No work for you. Not here."]))
            self.lw_marks.append((refuse, "despised", 1, 0, 1))
            roots.append((refuse, 0))
            self.stats["lines"] += 1
        offer = self.b.dialogue(f"{c['id']} - offer", roots, monologue=True, refs=refs, chance=100.0, conditions=conds)
        if c.get("successor_of"):                  # a successor's contract: only once they have taken over (OutlawCamps.cs)
            self.lw_marks.append((offer, "successor:" + c["successor_of"], 0, 0, 1))
        for o in getattr(self, "pending_reach", []):
            self.lw_marks.append((offer, f"reach:{o}", 0, 0, 1))
        self.pending_reach = []
        for r in self.b.records:
            if r["sid"] == report_line:
                r["ref"]["effects"].append((self.b.action("DA_INCREASE_FACTION_RANK"), RANK_CAP, 0, 0))
        # Native body hand-in (the vanilla bounty desks' DC 4/5 + DA 10 + DA 30): a contract holder who carries the target, alive or dead,
        # to a giver of a faction that posted the bounty gets the bounty paid (half for a corpse), the body taken, and the contract closed.
        # The giver's faction must be in the outlaw's `bounty_factions`, else the condition never passes and only the report pays.
        meitou = getattr(self, "target", "kenshi") == "meitou"
        # Meitou: the giver's faction thinks better of the squad through the helper's favour effect, which stops short of alliance (FAVOUR_CAP):
        # +4 for a reported kill, +5 for a body handed in. Kenshi: the hand-in's native +5 as before.
        handin_line = self.b.line(c.get("handin", HANDIN_TEXT), effects=[("DA_PAY_BOUNTY", 0), ("DA_ARREST_TARGETS_CARRIED_PERSON", 0)]
                                  + ([] if meitou else [("DA_AFFECT_RELATIONS", 5)]),
                                  refs={"set variable": [(var, 2)], "add to variable": [(jobs, -1)]})
        if meitou:
            if not hasattr(self, "lw_effects"):
                self.lw_effects = []
            self.lw_effects += [(report_line, "favour", 4), (handin_line, "favour", 5)]
        hconds = [x for x in (self.condition({"cond": c_, "who": "target", "value": 1}, ctx) for c_ in ("DC_CARRYING_BOUNTY_DEAD",)) if x]
        handin_dead = self.b.dialogue(f"{c['id']} - handin dead", [(handin_line, 0)], monologue=True, score_bonus=12, conditions=hconds,
                                      refs={"variable equals": [(var, 1)], "my faction": [(f, 1) for f in facs]} if facs else {"variable equals": [(var, 1)]})
        hconds2 = [x for x in (self.condition({"cond": "DC_CARRYING_BOUNTY_ALIVE", "who": "target", "value": 1}, ctx),) if x]
        handin_alive = self.b.dialogue(f"{c['id']} - handin alive", [(handin_line, 0)], monologue=True, score_bonus=12, conditions=hconds2,
                                       refs={"variable equals": [(var, 1)], "my faction": [(f, 1) for f in facs]} if facs else {"variable equals": [(var, 1)]})
        self.stats["lines"] += 1
        self.stats["dialogues"] += 2
        cpk = self.packages_for({"factions": c.get("factions"), "packages": c.get("packages", []), "event": "EV_PLAYER_TALK_TO_ME"}, facs, ctx)
        offer_pk = set(self.not_trade(cpk))
        for pkg in cpk:
            if pkg in offer_pk:                       # offers never go to shop and bar staff (they keep their trade menu)
                self.b.attach(pkg, offer, "EV_PLAYER_TALK_TO_ME")
            self.b.attach(pkg, report, "EV_PLAYER_TALK_TO_ME")
            self.b.attach(pkg, handin_dead, "EV_PLAYER_TALK_TO_ME")
            self.b.attach(pkg, handin_alive, "EV_PLAYER_TALK_TO_ME")
            self.stats["attached"] += 3
        self.stats["contracts"] = self.stats.get("contracts", 0) + 1

    # ---- rumours: talk about what actually happened ------------------------------------------------
    def make_rumour(self, spec):
        """{"state", "name", "factions", "fresh": [...], "lasting": [...], "fresh_days", "chance"}: sighting barks about a world state that is true.
        Kenshi build: only the `lasting` lines, gated on the state alone (Kenshi cannot tell how long ago it became true). Meitou build: `fresh`
        lines while the state has been true for under `fresh_days` game days, `lasting` lines after that; the helper keeps when each state
        first turned true and answers the recency conditions (meitou_target turns the marks below into condition codes)."""
        ctx = f"rumour '{spec['state']}'"
        state = self.state_sid(spec["state"], ctx)
        if not state:
            return
        days = int(spec.get("fresh_days", 4))
        tiers = [("lasting", 2, days)]                       # compare by 2 '>': at least `days` old (the helper never-seen counts as very old)
        if getattr(self, "target", "kenshi") == "meitou" and spec.get("fresh"):
            tiers.insert(0, ("fresh", 1, days))             # compare by 1 '<': under `days` days old
        for tier, compare_by, value in tiers:
            lines = spec.get(tier)
            if not lines:
                continue
            for t in lines:
                self.check_text(t, ctx)
            d = {"id": f"rm_{re.sub(r'[^a-z0-9]+', '_', spec['state'].lower()).strip('_')}_{tier}", "event": "EV_I_SEE_NEUTRAL_SQUAD",
                 "factions": spec["factions"], "player_only": True, "chance": spec.get("chance", 14), "repeat": 3, "one_at_a_time": True,
                 "states": [[spec["state"], True]],
                 "if": [{"cond": "DC_IN_A_NON_PLAYER_TOWN", "who": "me", "value": 1}, {"cond": "DC_IN_COMBAT", "who": "me", "value": 0},
                        {"cond": "DC_IS_SLAVE", "who": "me", "value": 0}],
                 "lines": [{"say": lines}]}
            before = len(self.b.records)
            self.build_dialogue(d)
            made = [r for r in self.b.records[before:] if r["type"] == 18 and r["name"] == d["id"]]
            if made and getattr(self, "target", "kenshi") == "meitou":
                if not hasattr(self, "recent_marks"):
                    self.recent_marks = []
                self.recent_marks.append((made[0]["sid"], state, spec["state"], compare_by, value))
            self.stats["rumours"] = self.stats.get("rumours", 0) + 1
            # Meitou: the same lines when our record says the squad killed them but the game's "killed by player" state is false (Meitou
            # credits only a killing blow; our record credits the last striker of a bleed-out). Freshness from the record's kill time.
            if getattr(self, "target", "kenshi") == "meitou" and spec["state"].endswith("killed by player") and spec.get("name"):
                d2 = dict(d, id=d["id"] + "_rec", states=[[spec["state"], False]],
                          lw_if=[{"named": spec["name"]}, {"named_days": spec["name"], "cmp": "<" if compare_by == 1 else ">", "value": value}])
                self.build_dialogue(d2)
                self.stats["rumours"] += 1

    def make_word_swap(self, spec):
        """{"name": "LWWAR_HOLY", "lines": [{"say": "Stack", "wartown": "Stack", "faction": "The Holy Nation"}, {"say": "someone's town"}]}.
        A WORD_SWAPS record (type 84): a line says /NAME/ and the game puts in the best line of this record whose conditions hold (more
        conditions score higher, so a matched town beats the plain fallback). Meitou only: the conditions are the helper's (meitou_target)."""
        if getattr(self, "target", "kenshi") != "meitou":
            return
        name = spec["name"]
        sids = []
        for ln in spec["lines"]:
            lsid = self.build_line({"say": ln["say"]}, f"word swap '{name}'")
            sids.append(lsid)
            if not hasattr(self, "lw_marks"):
                self.lw_marks = []
            if ln.get("wartown"):
                self.lw_marks.append((lsid, f"wartown:{ln['wartown']}|{ln['faction']}", 0, 0, 1))
            elif ln.get("camptown"):             # the outlaw's camp is nearest this town (value: 1 + the town's place, meitou_target)
                self.lw_marks.append((lsid, f"camptownline:{ln['outlaw']}|{ln['camptown']}", 0, 0, 1))
            elif ln.get("raidtown"):             # the raid is (or was) near this town (value: 1 + its place in campNews.towns)
                self.lw_marks.append((lsid, f"raidtownline:{ln['raid']}|{ln['raidtown']}", 0, 0, 1))
            elif ln.get("losstown"):             # the squad's last loss was at this town (value: 1 + its place in campNews.towns)
                self.lw_marks.append((lsid, f"losstownline:{ln['losstown']}", 0, 0, 1))
            elif ln.get("campdir"):              # the camp lies this way from that town (1-8 from north, clockwise; 9 right by it)
                self.lw_marks.append((lsid, f"campdir:{ln['outlaw']}", 0, 0, int(ln['campdir'])))
        sid = self.b.sid()
        r = B._rec(84, sid, name)
        r["int"] = {"repetition limit": 0, "score bonus": 0, "speaker": 0, "target is type": 0}
        r["float"] = {"chance permanent": 100.0, "chance temporary": 100.0}
        r["bool"] = {"persistent use": 0}
        r["ref"]["lines"] = [(x, 100, 0, 0) for x in sids]
        self.b.add(r)
        self.tokens.add(name)
        self.stats["word_swaps"] = self.stats.get("word_swaps", 0) + 1

    def jobs_var(self):
        sid = self.created.get(("variable", JOB_VAR))
        if sid:
            return sid
        cur = self.b                                   # shared by all our mods, so it lives in the base mod (the others depend on it)
        self.b = getattr(self, "base_builder", None) or cur
        try:
            return self.new_var(JOB_VAR)
        finally:
            self.b = cur

    def new_var(self, name):
        sid = self.b.variable(name, 0)
        self.created[("variable", name)] = sid
        return sid

    # ---- quest chains (offer -> steps -> report), each link a locked dialogue unlocked by the one before ----------
    def make_quest(self, q):
        """{"id", "offer": {...}, "steps": [{...}], "report": {...}}. Offer: factions/packages/narrow/in_town_of/states/chance,
        say/accept/accepted/decline. Step (locked until the one before is done): the same targeting fields plus characters,
        races, vars, target_has_item, take_items, say (NPC), reply (player; locks this step, unlocks the next, applies
        take_items/relations/effects), after (NPC). Report (locked; needs the world state `state`): say, reward, effects, and
        optionally its own targeting/take_items/target_has_item (default: the offer giver). Offer, steps and report also take
        "if": [{"cond": "DC_IS_FEMALE", "who": "me", "value": 1}] style conditions on the speaker or the player."""
        qid = q["id"]
        ctx = f"quest '{qid}'"
        offer, steps, rep = q["offer"], q.get("steps", []), q["report"]
        state = self.state_sid(rep["state"], ctx) if rep.get("state") else None
        if rep.get("state") and not state:
            return
        ev = "EV_PLAYER_TALK_TO_ME"
        var = self.new_var(f"Quest {qid}")      # 0 = not taken, 1..n+1 = on that step, 99 = paid

        def targeting(spec):
            facs = self.looks("faction", spec.get("factions"), ctx)
            pk = self.packages_for({"factions": [] if spec.get("narrow") else spec.get("factions"), "packages": spec.get("packages", []),
                                    "characters": spec.get("characters", []), "event": ev}, [] if spec.get("narrow") else facs, ctx)
            return facs, pk

        def dlg_refs(spec, facs, extra_states=()):
            r = {}
            sp = {k: spec[k] for k in ("in_town_of", "races", "characters", "target_has_item", "vars") if k in spec}
            sp["states"] = list(spec.get("states", [])) + list(extra_states)
            r.update(self.refs_from(sp, ctx))
            if facs and (spec.get("narrow") or not spec.get("packages")):
                r["my faction"] = [(f, 1) for f in facs]
            return r

        def conds_of(spec):
            return [x for x in (self.condition(c, ctx) for c in spec.get("if", [])) if x]

        def add_lock(line_sid, key, target):
            for r in self.b.records:
                if r["sid"] == line_sid:
                    r["ref"].setdefault(key, []).append((target, 0, 0, 0))

        # report (at the giver unless the report names its own people)
        keys = ("factions", "packages", "characters", "narrow", "in_town_of", "races", "target_has_item")
        own = any(rep.get(k) for k in ("factions", "packages", "characters"))
        rspec = {k: (rep if own else offer)[k] for k in keys if k in (rep if own else offer)}
        for k in ("in_town_of", "races", "target_has_item"):
            if k in rep:
                rspec[k] = rep[k]
        rfacs, rpk = targeting(rspec)
        rspec["states"] = list(rep.get("states", []))
        rrefs = dlg_refs(rspec, rfacs, extra_states=[(rep["state"], True)] if state else [])
        rline_refs = self.refs_from({"take_items": rep["take_items"]}, ctx) if rep.get("take_items") else {}
        if rep.get("give_items"):                                   # a reward item handed over on the report line
            rline_refs.update(self.refs_from({"give_items": rep["give_items"]}, ctx))
        rline_refs["set variable"] = [(var, 99)]
        rline_refs["add to variable"] = [(self.jobs_var(), -1)]
        rrefs["variable equals"] = [(var, len(steps) + 1)]
        for t in rep["say"]:
            self.check_text(t, ctx)
        rl = self.b.line(rep["say"], effects=[("DA_GIVE_MONEY", int(rep.get("reward", 1000)))] + [tuple(e) for e in rep.get("effects", [])], refs=rline_refs)
        self.quest_leg(rl, qid, int(rep.get("reward", 1000)), pay=True)   # Meitou: the long road pays (from the start through every step to here)
        self.stats["lines"] += 1
        self.stats["variants"] += len(rep["say"])
        report = self.b.dialogue(f"{qid}_report", [(rl, 0)], monologue=True, refs=rrefs, score_bonus=10, conditions=conds_of(rep))
        for pkg in rpk:
            self.b.attach(pkg, report, ev)
            self.stats["attached"] += 1
        self.stats["dialogues"] += 1
        nxt = report
        for i in range(len(steps) - 1, -1, -1):
            st = steps[i]
            sfacs, spk = targeting(st)
            srefs = dlg_refs(st, sfacs)
            for t in st["say"] + st["reply"] + st.get("after", []):
                self.check_text(t, ctx)
            lrefs = {"set variable": [(var, i + 2)]}
            srefs["variable equals"] = [(var, i + 1)]
            lrefs.update(self.refs_from({k: st[k] for k in ("take_items",) if k in st}, ctx))
            if st.get("relations"):
                lrefs["change relations"] = [(self.look("faction", n, ctx), v) for n, v in st["relations"].items() if self.look("faction", n, ctx)]
            kids = []
            if st.get("after"):
                kids = [(self.b.line(st["after"]), 100)]
                self.stats["lines"] += 1
                self.stats["variants"] += len(st["after"])
            reply = self.b.line(st["reply"], speaker="T_TARGET_IF_PLAYER", children=kids, refs=lrefs,
                                effects=[tuple(e) for e in st.get("effects", [])])
            self.quest_leg(reply, qid, 0)                          # Meitou: a leg of the route, where this step was done
            line = self.b.line(st["say"], children=[(reply, 100)])
            self.stats["lines"] += 2
            self.stats["variants"] += len(st["say"]) + len(st["reply"])
            sd = self.b.dialogue(st.get("id") or f"{qid}_step{i + 2}", [(line, 0)], monologue=True, refs=srefs, score_bonus=10, conditions=conds_of(st))
            for pkg in spk:
                self.b.attach(pkg, sd, ev)
                self.stats["attached"] += 1
            self.stats["dialogues"] += 1
            nxt = sd
        # offer
        for t in offer["say"] + offer["accept"] + offer["accepted"] + offer.get("decline", []):
            self.check_text(t, ctx)
        ofacs, opk = targeting(offer)
        orefs = dlg_refs(offer, ofacs, extra_states=[(rep["state"], False)] if state else [])
        accepted = self.b.line(offer["accepted"], refs=self.refs_from({"give_items": offer["give_items"]}, ctx) if offer.get("give_items") else None)
        self.quest_leg(accepted, qid, 1)                           # Meitou: the route starts where the job was taken
        jobs = self.jobs_var()
        accept = self.b.line(offer["accept"], speaker="T_TARGET_IF_PLAYER", children=[(accepted, 100)], refs={"set variable": [(var, 1)], "add to variable": [(jobs, 1)]})
        orefs["variable equals"] = [(var, 0)]
        orefs["variable less than"] = [(jobs, JOB_CAP)]
        decline = self.b.line(offer.get("decline", ["Not now."]), speaker="T_TARGET_IF_PLAYER")
        offer_line = self.b.line(with_reward(offer["say"], int(rep.get("reward", 1000))), children=[(accept, 0), (decline, 0)])
        self.stats["lines"] += 4
        self.stats["variants"] += len(offer["say"]) + len(offer["accept"]) + len(offer["accepted"]) + len(offer.get("decline", [1]))
        ocs = conds_of(offer)
        if ev == "EV_PLAYER_TALK_TO_ME":         # spread talk offers over characters by a stable personality tag, not by chance
            tagc = self.condition({"cond": "DC_PERSONALITY_TAG", "who": "me", "tag": offer_tag(qid), "value": 1}, ctx)
            if tagc:
                ocs = ocs + [tagc]
            notrader = self.condition({"cond": "DC_IS_A_TRADER", "who": "me", "value": 0}, ctx)   # barmen and shopkeepers never offer work
            if notrader:
                ocs = ocs + [notrader]
            ochance = 100.0
        else:
            ochance = min(float(offer.get("chance", 40.0)), 25.0)
        od = self.b.dialogue(f"{qid}_offer", [(offer_line, 0)], monologue=True, refs=orefs, chance=ochance, conditions=ocs)
        if getattr(self, "target", "kenshi") == "meitou":       # no offers to a holder of a title the giver's faction despises
            if not hasattr(self, "lw_marks"):
                self.lw_marks = []
            self.lw_marks.append((od, "despised", 1, 0, 0))
        for pkg in (self.not_trade(opk) if ev == "EV_PLAYER_TALK_TO_ME" else opk):    # offers never go to shop and bar staff
            self.b.attach(pkg, od, ev)
            self.stats["attached"] += 1
        self.stats["dialogues"] += 1
        if offer.get("give_items") and ev == "EV_PLAYER_TALK_TO_ME":
            # The item is handed over on the accept line. With a full inventory the game gives nothing but the quest is still taken,
            # so the giver hands out another while the quest is on its first step (talk to them again).
            rrefs2 = dict(orefs)
            rrefs2.pop("variable less than", None)
            rrefs2["variable equals"] = [(var, 1)]
            rtext = ["You said you lost it? Here. Another. Keep it somewhere that is not full.", "Your hands were full, I think. Take it again. Do not drop it.",
                     "I thought so. Here, once more. Make room first.", "Another, then. Do not make me ask a third time."]
            rl2 = self.b.line(rtext, refs=self.refs_from({"give_items": offer["give_items"]}, ctx))
            rd = self.b.dialogue(f"{qid}_again", [(rl2, 0)], monologue=True, refs=rrefs2, chance=100.0, score_bonus=10,
                                 conditions=[x for x in (self.condition({"cond": "DC_IS_A_TRADER", "who": "me", "value": 0}, ctx),) if x])
            for pkg in self.not_trade(opk):
                self.b.attach(pkg, rd, ev)
                self.stats["attached"] += 1
            self.stats["dialogues"] += 1
            self.stats["lines"] += 1
        self.stats["quests"] = self.stats.get("quests", 0) + 1

    def quest_leg(self, line_sid, qid, value, pay=False):
        """Meitou only: the helper notes where a quest's line was said (value 1 starts the route) or, with pay, pays the route's distance
        bonus on a reward of `value` (QuestRoutes in LwMod.cs; the Distance pay setting)."""
        if getattr(self, "target", "kenshi") != "meitou":
            return
        if not hasattr(self, "lw_effects"):
            self.lw_effects = []
        self.lw_effects.append((line_sid, f"{'questpay' if pay else 'questleg'}:{qid}", value))

    def fix_self_lock(self, line_sid, dialogue_sid):
        for r in self.b.records:
            if r["sid"] == line_sid:
                r["ref"]["locks"] = [(dialogue_sid, 0, 0, 0)]

    def compile(self, pack):
        if pack.get("meitou_only") and getattr(self, "target", "kenshi") != "meitou":
            return True                              # a pack only the Meitou helper uses (bounty crews): left out of the Kenshi build
        for v in pack.get("variables", []):
            self.created[("variable", v["name"])] = self.b.variable(v["name"], v.get("initial", 0))
            if v.get("moment"):                       # Meitou: a Recap moment when the variable first goes from 0 to another value
                if not hasattr(self, "var_moments"):
                    self.var_moments = {}
                self.var_moments[v["name"]] = {"caption": v["moment"], "place": v.get("moment_place", "the wastes"),
                                               "weight": int(v.get("moment_weight", 60))}
        for spec in pack.get("world_states", []):
            self.make_state(spec)
        for spec in pack.get("lua_scripts", []):
            lm = spec.get("mod") or pack.get("lua_mod", "LivingWastelandLua")
            self.created[("lua", spec["name"])] = self.b.lua_script(
                spec["name"], f"./mods/{lm}/scripts/dialogue/{spec['file']}", spec.get("description", ""))
        for spec in pack.get("packages_new", []):
            self.make_package(spec)
        for spec in pack.get("clones", []):
            if spec.get("test_only") and getattr(self, "release", False):
                continue                       # play-test helpers (the Play Test Squad starts): left out of a release build
            try:
                self.make_clone(spec)
            except Exception as e:
                self.errors.append(f"clone '{spec.get('name')}': {type(e).__name__}: {e}")
        lore_items = list(pack.get("lore_items", []))
        # keepsake slots (Trophies.cs KeepsakeBook): copies of the keepsake whose name and description the helper rewrites for each fallen
        if pack.get("keepsake") and int(pack.get("keepsake_slots", 1)) > 1:
            base = next((x for x in lore_items if x.get("name") == pack["keepsake"]), None)
            if base is not None:
                lore_items += [dict(base, name=f"{pack['keepsake']} ({k})") for k in range(2, int(pack["keepsake_slots"]) + 1)]
        for spec in lore_items:
            try:
                self.make_lore_item_lw(spec)
            except Exception as e:
                self.errors.append(f"lore item '{spec.get('name')}': {type(e).__name__}: {e}")
        for spec in pack.get("item_lists", []):
            try:
                self.make_item_list_lw(spec)
            except Exception as e:
                self.errors.append(f"item list '{spec.get('name')}': {type(e).__name__}: {e}")
        for spec in pack.get("changes", []):
            try:
                self.make_change(spec)
            except Exception as e:
                self.errors.append(f"change '{spec.get('name')}': {type(e).__name__}: {e}")
        for o in pack.get("outlaws", []):
            try:
                self.make_outlaw(o)
            except Exception as e:
                self.errors.append(f"outlaw '{o.get('name')}': {type(e).__name__}: {e}")
        for q in pack.get("quests", []):
            try:
                self.make_quest(q)
            except Exception as e:
                self.errors.append(f"quest '{q.get('id')}': {type(e).__name__}: {e}")
        for t in pack.get("titles", []):              # notoriety titles (Meitou helper; lw_titles in lw_codes.json)
            if not hasattr(self, "titles"):
                self.titles = []
            missing = [k for k in ("id", "name", "scope", "stat", "at") if k not in t]
            if t.get("kind") and t.get("kind") not in ("weapons", "armour", "crossbows", "robotics", "items"):
                missing.append("kind (weapons|armour|crossbows|robotics|items)")
            if missing or t["scope"] not in ("member", "squad"):
                self.errors.append(f"title '{t.get('id')}': needs id, name, scope (member|squad), stat, at")
                continue
            if t.get("faction") and not self.db.find("faction", t["faction"]):
                self.errors.append(f"title '{t['id']}': no faction named '{t['faction']}'")
                continue
            groups = t.get("targets") or ([t["target"]] if t.get("target") else [])
            bad = [x for x in groups if not any(self.db.find("character", n.strip()) for n in x.split("|"))]
            if bad:                              # the helper matches a named kill by its CHARACTER record's name
                self.errors.append(f"title '{t['id']}': no character named {bad}")
                continue
            for txt in (t.get("name", ""),):
                self.check_text(txt, f"title '{t['id']}'")
            self.titles.append(t)
        if pack.get("hunters"):                      # the bounty crew squad the helper sends (Hunters.cs); resolved after the pack's clones
            sid = self.created.get(("squad", pack["hunters"]["squad"]))
            if not sid:
                self.errors.append(f"hunters: no squad '{pack['hunters']['squad']}' made by this pack's clones")
            else:
                self.hunters = {"squad": sid}
        if pack.get("atlas"):                        # the Atlas tab's data (gen_atlas.py): people get their CHARACTER record ids for the helper
            atlas = pack["atlas"]
            for name, p in atlas["people"].items():
                sid = self.created.get(("character", name))
                if not sid:
                    found = self.db.find("character", name)
                    sid = found[-1]["sid"] if found else None
                if sid:
                    p["id"] = sid
                else:
                    self.warnings.append(f"atlas: no character named '{name}' (shown without fate or bounty)")
            self.atlas = atlas
        for r in pack.get("raids", []):              # harassment raids (Raids.cs): the raiding squad (made by this pack's clones) and its target
            sid = self.created.get(("squad", r["squad"]))
            if not sid or not self.db.find("faction", r["target"]):
                self.errors.append(f"raid '{r['name']}': no squad '{r['squad']}' made by this pack, or no faction '{r['target']}'")
                continue
            if not hasattr(self, "raids"):
                self.raids = []
            self.raids.append({"name": r["name"], "squad": sid, "target": r["target"], "raider": r.get("raider", "")})
        if pack.get("keepsake"):                     # the keepsake the helper hands a fallen squadmate's mourner (Trophies.cs)
            sid = self.created.get(("item", pack["keepsake"]))
            if not sid:
                self.errors.append(f"keepsake: no item '{pack['keepsake']}' made by this pack")
            else:
                self.trophies = {"keepsake": sid}
                slots = [sid] + [self.created.get(("item", f"{pack['keepsake']} ({k})")) for k in range(2, int(pack.get("keepsake_slots", 1)) + 1)]
                self.trophies["keepsakes"] = [x for x in slots if x]
        if pack.get("warrant"):                      # what a beaten bounty hunters' crew drops (Hunters.cs)
            sid = self.created.get(("item", pack["warrant"]))
            if not sid:
                self.errors.append(f"warrant: no item '{pack['warrant']}' made by this pack")
            else:
                self.trophies = dict(getattr(self, "trophies", None) or {}, warrant=sid)
        if pack.get("title_views"):                  # faction -> the titles it admires, despises, fears (the Atlas's faction pages)
            if not hasattr(self, "title_views"):
                self.title_views = {}
            self.title_views.update(pack["title_views"])
        if pack.get("temperaments"):                 # faction -> titles it despises (gen_title_temperaments.py), for the helper's job gate
            if not hasattr(self, "temperaments"):
                self.temperaments = {}
            self.temperaments.update(pack["temperaments"])
        for w in pack.get("word_swaps", []):        # Meitou only: a /NAME/ token whose text the helper's conditions pick (make_word_swap)
            try:
                self.make_word_swap(w)
            except Exception as e:
                self.errors.append(f"word swap '{w.get('name')}': {type(e).__name__}: {e}")
        for r in pack.get("rumours", []):
            try:
                self.make_rumour(r)
            except Exception as e:
                self.errors.append(f"rumour '{r.get('state')}': {type(e).__name__}: {e}")
        if getattr(self, "target", "kenshi") == "meitou":
            pack = dict(pack, contracts=list(pack.get("contracts", [])) + successor_contracts(pack))
        for c in pack.get("contracts", []):
            try:
                self.make_contract(c)
            except Exception as e:
                self.errors.append(f"contract '{c.get('id')}': {type(e).__name__}: {e}")
        for d in pack.get("dialogues", []):
            try:
                self.build_dialogue(d)
            except Exception as e:      # keep going so one bad entry does not hide the rest
                self.errors.append(f"dialogue '{d.get('id')}': {type(e).__name__}: {e}")
        return not self.errors


def load_pack(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


if __name__ == "__main__":
    print(__doc__)
    sys.exit(0)
