"""Second build target: the same content packs for the Meitou engine instead of RE_Kenshi + BFrizz's Extra Extensions.

Meitou (https://github.com/VlcekM/MeitouClient) does not load RE_Kenshi plugins, and its dialogue reader ignores references it does not
know. Our quests keep their state in BFrizz variables, a variable record (type 1000) and the references `variable equals`, `variable less
than`, `variable greater than`, `set variable`, `add to variable` and `take item`. Left as they are, the quests would lose their gating.

This pass rewrites them, after the packs are compiled and before the files are written, into what Meitou's code-mod API offers: dialogue
condition and effect CODES (numbers from 1000 up, named in a DIALOG_ACTION's `condition name` / `action name`; the reference's val0 is the
value). The state itself lives in a small C# code mod (modgen/meitou/LivingWastelandMeitou) that reads `lw_codes.json`, the table this pass
writes: which code is which variable or item. Dialogue text, scoring and everything else stay as they are.

Codes (kept apart from each other; Meitou refuses a second mod that claims a code another holds, so the bases are well above 1000):
    condition  CONDITION_BASE + n   compares variable n with the value (compare by 1 '<', 2 '>', else '==')
    condition  JOB_CAP_CODE         the open-job counter is below the cap in the helper's settings (replaces `LW open jobs < 5`)
    condition  GOSSIP_CODE          passes with the "Gossip amount" chance (rumours, outlaw news, war talk and title reactions double their chance
                                    on Meitou and carry this gate, so the slider's 100% is the chance as written)
    condition  HEAT_CONDITION       the heat the speaker's town feels (replaces DC_REPUTATION on the speaker: rep < -14 becomes heat > 14)
    effect     HEAT_EFFECT          value > 0 adds crime heat where the speaker stands, < 0 forgives the speaker's faction (replaces
                                    DA_AFFECT_REPUTATION, value negated)
    condition  TITLE_BASE + i       the subject holds notoriety title i (dialogues with "lw_if" titles; Meitou only)
    condition  OUTLAW_BASE + i      what living outlaw i is doing now (compared with the value; WorldNews.cs)
    condition  WAR_BASE + j         faction j is on campaign (Meitou's PopulationSystem.Wars)
    effect     SET_BASE + n         sets variable n to the value
    effect     ADD_BASE + n         adds the value (may be negative) to variable n
    effect     TAKE_BASE + m        takes `value` of item m from the target (the speaker's partner in the conversation)
Kenshi-only records are dropped (variables, LUA_SCRIPT items) and their references with them.
"""
import json
import os

import builder as B

CONDITION_BASE, SET_BASE, ADD_BASE, TAKE_BASE = 12000, 14000, 16000, 18000
RUMOUR_BASE = 20000          # condition RUMOUR_BASE + k: game days since world state k first turned true, compared with the value
JOB_CAP_CODE = 19000          # "open jobs are below the cap": the helper compares with its settings slider, not a number in the data
HEAT_CONDITION, HEAT_EFFECT = 19100, 19101   # heat the speaker's town feels (compared with the value) / add (>0) or forgive (<0) heat
TITLE_BASE = 19200            # condition TITLE_BASE + i: the subject (`who`) holds notoriety title i
OUTLAW_BASE = 21000           # condition OUTLAW_BASE + i: what living outlaw i is up to (1 raiding, 2 lying low, 3 gathering men, 4 wounded)
WAR_BASE = 22000              # condition WAR_BASE + j: faction j is on campaign against an NPC town (or, for the player entries, the player's)
WARTOWN_BASE = 24000          # condition WARTOWN_BASE + k: the faction numbered by the value (1-based, "warTowns.factions") marches on town k
NAMED_BASE, NAMED_DAYS_BASE = 25000, 25500   # the squad killed named character i (bleed-outs included) / game days since, compared with the value
CAMPTOWN_BASE, CAMPDIR_BASE, STRUCK_BASE = 26000, 26500, 27000   # outlaw k's camp: nearest town (1 + its place in campNews.towns, 0 unknown) / direction from it (1-8 from north clockwise, 9 right by it) / game days since the squad struck the outlaw's people and they lived
TEMPER_ONCE, TEMPER_MARK, DESPISED_CODE = 19410, 19411, 19420   # faction temperaments (TITLES_BY_FACTION.md): condition "this faction has
                              # not yet done kind k for title i" (value = key), effect "mark it done" (value = key), condition "the target holds
                              # a title the speaker's faction despises" (who = target); key = title index * 2 + kind (0 bump, 1 gift) + 1
SQUAD_NEAR_CODE = 27918        # condition: how many other standing squad members are within 50 m of the speaker (solo victory lines)
MOURN_CODE, LOSS_DAYS_CODE, LOSS_TOWN_CODE, FEUD_CODE, SCAR_CODE = 27910, 27911, 27912, 27913, 27914   # memorials: the target is a dead squadmate
                              # (who = target); days since the last loss; the last loss's town (1 + its place in campNews.towns); the
                              # target's faction killed one of the squad (who = target)
FAVOUR_CODE = 27916          # effect: the speaker's faction thinks better of the player by the value, up to the manifest's cap (contract favours)
ALMS_CODE = 27917            # effect: the squad gave `value` cats away (alms and offerings; crew stat "alms")
MARCH_CODE = 27915           # condition: the target marches in a nation's campaign (lookout army lines; Marches.cs)
TALE_BASE = 29700            # condition TALE_BASE + i (title order): anyone of the squad, or the squad itself, holds title i (tavern tales)
QUESTLEG_BASE, QUESTPAY_BASE = 29000, 29500   # quest q: effect "note where this was said" (value 1: the route starts) / "pay the route's distance bonus on a reward of value"
RAID_BASE, RAIDTOWN_BASE = 28000, 28100   # raid r (raids order): its state (0 none, 1 out, 2 caught, 3 got away; recent) / its town (1 + place in campNews.towns)
REPRISAL_BASE = 27600        # condition REPRISAL_BASE + k: the speaker is one of outlaw k's reprisal party (Outlaw reprisals setting)
TIER_BASE = 27500            # condition TIER_BASE + k: how far outlaw k's price has grown (0-3), compared with the value
NEWS_BASE = 29350            # condition NEWS_BASE + k: news slot k is live and its place is within the speaker's reach (News.cs, other mods' news)
RIVAL_BASE = 29400           # condition RIVAL_BASE + k: rival slot k near the speaker (0 none, 1 about, 2 has beaten the squad; Rivals.cs)
SUCCESSOR_BASE = 29500       # condition SUCCESSOR_BASE + k: outlaw k's successor has taken over and is alive (OutlawCamps.cs)
NEARPLACE_BASE = 29900       # condition NEARPLACE_BASE + t: the speaker is near town t (talk about a boss stays near the boss's home)
NEWSREACH_BASE = 29800       # condition NEWSREACH_BASE + k: the speaker is within the "Outlaw news range" of outlaw k's ground (gossip)
DISTPAY_BASE, REACH_BASE = 19600, 19700   # outlaw k (campNews order): effect "pay the distance bonus on a reward of `value`" (the giver is the
                              # speaker); condition "the speaker is within the contract reach of outlaw k's ground"
GIFT_BASE = 19500             # effect GIFT_BASE + g: give `value` of gift item g to the target
GOSSIP_CODE = 19102           # condition: passes with the chance set by the helper's "Gossip amount" slider (value 1 = must pass)
GOSSIP_PREFIXES = ("rm_", "on_", "wn_", "nt_")
TALK_CODE = 19103             # condition: passes with the chance set by the helper's "Living Wasteland talk" slider (every dialogue of ours that is
                              # only ever said unprompted: not one the player starts by talking to someone)
FEATURE_CODES = {"jobs": 19110, "titles": 19111, "memorials": 19112, "alms": 19113}   # condition: the helper's switch for that feature is on
FEATURE_GATES = (("jobs", lambda n: n.endswith(" - offer") or n.endswith("_offer")),       # new quest and contract offers (not reports)
                 ("titles", lambda n: n.startswith(("tm_", "nt_"))),                      # people reacting to the squad's titles
                 ("memorials", lambda n: n.startswith("mm_")),
                 ("alms", lambda n: n.startswith("al_")))                            # mourning, remembering, feuds, scars   # rumours, outlaw news, war talk, title reactions: their chance is doubled and gated by GOSSIP_CODE
DC_REPUTATION, DA_AFFECT_REPUTATION = 3, 5    # Kenshi heat: reputation down = heat up (same scale), so the value and the comparison flip
FLIP = {0: 0, 1: 2, 2: 1}
JOB_VAR, JOB_CAP = "LW open jobs", 5
COND_KEYS = {"variable equals": 0, "variable less than": 1, "variable greater than": 2}     # key -> compare by
DROPPED_TYPES = {1000, 322}
DROPPED_REFS = {"run lua script"}
BFRIZZ_REFS = set(COND_KEYS) | {"set variable", "add to variable", "take item", "take item from squad", "destroy item", "destroy item from squad"}


def _action_record(builder, code):
    rec = builder.action(0)
    for r in builder.records:
        if r["sid"] == rec:
            r["int"] = {"action name": code}
    return rec


def _condition_record(builder, code, compare_by, who=0):
    sid = builder.condition(0, who=0, compare="CE_EQUALS")
    for r in builder.records:
        if r["sid"] == sid:
            r["int"] = {"compare by": compare_by, "condition name": code, "tag": -1, "who": who}
    return sid


def _reputation_to_heat(builders):
    """Our DC_REPUTATION (on the speaker) and DA_AFFECT_REPUTATION records become the helper's heat codes; every reference to them gets its
    value negated (reputation -14 is heat 14), and a condition's comparison flips. A condition record compared with a positive reputation
    anywhere stays native (heat has no positive side)."""
    refs = {}                                      # record sid -> [(ref list, index)]
    for b in builders:
        for r in b.records:
            for key in ("conditions", "effects"):
                lst = r["ref"].get(key)
                if lst:
                    r["ref"][key] = lst = [tuple(e) for e in lst]
                    for i, e in enumerate(lst):
                        refs.setdefault(e[0], []).append((r["ref"][key], i))
    n = {"heat_conditions": 0, "heat_effects": 0}
    for b in builders:
        for r in b.records:
            if r["type"] != B.T_ACTION:
                continue
            ints, uses = r["int"], refs.get(r["sid"], [])
            if ints.get("condition name") == DC_REPUTATION and ints.get("who", 0) == 0 and "action name" not in ints:
                if any(lst[i][1] > 0 for lst, i in uses):
                    continue
                ints["condition name"], ints["compare by"] = HEAT_CONDITION, FLIP.get(ints.get("compare by", 0), 0)
                key = "heat_conditions"
            elif ints.get("action name") == DA_AFFECT_REPUTATION and "condition name" not in ints:
                ints["action name"] = HEAT_EFFECT
                key = "heat_effects"
            else:
                continue
            for lst, i in uses:
                e = lst[i]
                lst[i] = (e[0], -e[1]) + tuple(e[2:])
                n[key] += 1
    return n


def convert(builders, recent_marks=(), lw_marks=(), titles=(), camps=(), lw_effects=(), temperaments=None, hunters=None, trophies=None, var_moments=None, atlas=None, raids=None, title_views=None):
    """Rewrite every builder's records in place; returns the manifest dict for lw_codes.json."""
    variables, order = {}, []                      # sid -> (index, name, initial)
    for b in builders:
        for r in b.records:
            if r["type"] == 1000 and r["sid"] not in variables:
                variables[r["sid"]] = (len(order), r["name"], int(r["int"].get("value", 0)))
                order.append(r["sid"])
    items = {}                                     # item sid -> index

    def item_index(sid):
        return items.setdefault(sid, len(items))

    stats = {"conditions": 0, "sets": 0, "adds": 0, "takes": 0, "dropped_records": 0}
    for b in builders:
        cond_cache, act_cache = {}, {}

        def cond_sid(code, compare_by, who=0):
            key = (code, compare_by, who)
            if key not in cond_cache:
                cond_cache[key] = _condition_record(b, code, compare_by, who)
            return cond_cache[key]

        def act_sid(code):
            if code not in act_cache:
                act_cache[code] = _action_record(b, code)
            return act_cache[code]

        before = len(b.records)
        b.records = [r for r in b.records if r["type"] not in DROPPED_TYPES]
        stats["dropped_records"] += before - len(b.records)
        for r in list(b.records):
            ref = r["ref"]
            for key in DROPPED_REFS & set(ref):
                del ref[key]
            if not (BFRIZZ_REFS & set(ref)):
                continue
            conds, effects = [], []
            for key, compare_by in COND_KEYS.items():
                for e in ref.pop(key, []):
                    n = variables[e[0]][0]
                    if key == "variable less than" and variables[e[0]][1] == JOB_VAR and e[1] == JOB_CAP:
                        conds.append((cond_sid(JOB_CAP_CODE, 0), 1, 0, 0))     # the cap the player set in the mod's settings
                        stats["job_cap"] = stats.get("job_cap", 0) + 1
                        continue
                    conds.append((cond_sid(CONDITION_BASE + n, compare_by), e[1], 0, 0))
                    stats["conditions"] += 1
            for e in ref.pop("set variable", []):
                effects.append((act_sid(SET_BASE + variables[e[0]][0]), e[1], 0, 0))
                stats["sets"] += 1
            for e in ref.pop("add to variable", []):
                effects.append((act_sid(ADD_BASE + variables[e[0]][0]), e[1], 0, 0))
                stats["adds"] += 1
            required_items = {}
            for key in ("take item", "take item from squad"):
                for item, count, *_ in ref.pop(key, []):
                    code = TAKE_BASE + item_index(item)
                    effects.append((act_sid(code), count, 0, 0))
                    required_items[code] = required_items.get(code, 0) + count
                    stats["takes"] += 1
            # Vanilla's target-has-item list only checks presence. Check the exact amount on the line
            # that consumes it, after any native retargeting on the dialogue root. The condition and
            # effect registries are separate, so both can use the manifest's existing item code.
            for code, count in required_items.items():
                if count > 0:
                    conds.append((cond_sid(code, 2, who=1), count - 1, 0, 0))
            for key in ("destroy item", "destroy item from squad"):
                ref.pop(key, None)
            if conds:
                ref["conditions"] = list(ref.get("conditions", [])) + conds
            if effects:
                ref["effects"] = list(ref.get("effects", [])) + effects
    stats.update(_reputation_to_heat(builders))
    rumours, by_state = [], {}
    by_sid_builder = {r["sid"]: (b, r) for b in builders for r in b.records}
    for dialogue_sid, state_sid, state_name, compare_by, days in recent_marks:
        if state_sid not in by_state:
            by_state[state_sid] = RUMOUR_BASE + len(rumours)
            rumours.append({"code": by_state[state_sid], "state": state_sid, "name": state_name})
        b, rec = by_sid_builder[dialogue_sid]
        csid = _condition_record(b, by_state[state_sid], compare_by)
        rec["ref"]["conditions"] = list(rec["ref"].get("conditions", [])) + [(csid, int(days), 0, 0)]
        stats["rumour_conditions"] = stats.get("rumour_conditions", 0) + 1
    title_codes = {t["id"]: TITLE_BASE + i for i, t in enumerate(titles)}
    title_order = [t["id"] for t in titles]

    def temper_key(tid, kind):
        return title_order.index(tid) * 2 + (1 if kind == "gift" else 0) + 1

    camp_outlaws, camp_towns = {}, []
    news_slots = set()                               # news slots the talk uses (codes NEWS_BASE + k)
    rival_slots = set()                              # rival slots the talk uses (codes RIVAL_BASE + k)
    near_places = {}                                 # town -> t (codes NEARPLACE_BASE + t)
    raid_ids = {r["name"]: i for i, r in enumerate(raids or [])}   # raid -> r (codes RAID_BASE / RAIDTOWN_BASE + r)               # outlaw -> k (codes CAMPTOWN/CAMPDIR/STRUCK + k), towns named by camp lines
    gifts = {}                                       # item record id -> g
    quests = {}                                      # quest id -> q (codes QUESTLEG / QUESTPAY + q)
    for line_sid, key, value in lw_effects:
        b, rec = by_sid_builder[line_sid]
        if key.startswith("distpay:"):
            asid, val = _action_record(b, DISTPAY_BASE + camp_outlaws.setdefault(key[8:], len(camp_outlaws))), int(value)
        elif key.startswith("questleg:") or key.startswith("questpay:"):
            q = quests.setdefault(key[9:], len(quests))
            asid, val = _action_record(b, (QUESTLEG_BASE if key.startswith("questleg:") else QUESTPAY_BASE) + q), int(value)
        elif key == "alms":
            asid, val = _action_record(b, ALMS_CODE), int(value)
        elif key == "favour":
            asid, val = _action_record(b, FAVOUR_CODE), int(value)
        elif key.startswith("tempermark:"):
            _, tid, kind = key.split(":")
            asid, val = _action_record(b, TEMPER_MARK), temper_key(tid, kind)
        else:
            item = key[5:]
            asid, val = _action_record(b, GIFT_BASE + gifts.setdefault(item, len(gifts))), int(value)
        rec["ref"]["effects"] = list(rec["ref"].get("effects", [])) + [(asid, val, 0, 0)]
        stats["helper_effects"] = stats.get("helper_effects", 0) + 1
    outlaws, wars = {}, {}                          # name -> code, (faction, player) -> code, in order of first use
    war_towns, war_factions, named = {}, [], {}     # town -> code, attacking factions (value - 1), named character -> index
    for dialogue_sid, key, who, compare_by, value in lw_marks:
        b, rec = by_sid_builder[dialogue_sid]
        if key == "heat":
            code = HEAT_CONDITION
        elif key.startswith("outlaw:"):
            code = outlaws.setdefault(key[7:], OUTLAW_BASE + len(outlaws))
        elif key.startswith("wartown:"):
            town, faction = key[8:].split("|", 1)
            code = war_towns.setdefault(town, WARTOWN_BASE + len(war_towns))
            if faction not in war_factions:
                war_factions.append(faction)
            value = war_factions.index(faction) + 1
        elif key.startswith("named:"):
            code = NAMED_BASE + named.setdefault(key[6:], len(named))
        elif key.startswith("nameddays:"):
            code = NAMED_DAYS_BASE + named.setdefault(key[10:], len(named))
        elif key.startswith("temperonce:"):
            _, tid, kind = key.split(":")
            code = TEMPER_ONCE
            value = temper_key(tid, kind)
        elif key == "mourn":
            code = MOURN_CODE
        elif key == "scar":
            code = SCAR_CODE
        elif key == "marching":
            code = MARCH_CODE
        elif key == "feud":
            code = FEUD_CODE
        elif key == "lossdays":
            code = LOSS_DAYS_CODE
        elif key == "squadnear":
            code = SQUAD_NEAR_CODE
        elif key.startswith("losstownline:"):
            town = key[13:]
            code = LOSS_TOWN_CODE
            if town not in camp_towns:
                camp_towns.append(town)
            value = camp_towns.index(town) + 1
        elif key.startswith("tier:"):
            code = TIER_BASE + camp_outlaws.setdefault(key[5:], len(camp_outlaws))
        elif key.startswith("reprisal:"):
            code = REPRISAL_BASE + camp_outlaws.setdefault(key[9:], len(camp_outlaws))
        elif key.startswith("reach:"):
            code = REACH_BASE + camp_outlaws.setdefault(key[6:], len(camp_outlaws))
        elif key.startswith("news:"):
            code = NEWS_BASE + int(key[5:])
            news_slots.add(int(key[5:]))
        elif key.startswith("rival:"):
            code = RIVAL_BASE + int(key[6:])
            rival_slots.add(int(key[6:]))
        elif key.startswith("successor:"):
            code = SUCCESSOR_BASE + camp_outlaws.setdefault(key[10:], len(camp_outlaws))
        elif key.startswith("nearplace:"):
            code = NEARPLACE_BASE + near_places.setdefault(key[10:], len(near_places))
        elif key.startswith("newsreach:"):
            code = NEWSREACH_BASE + camp_outlaws.setdefault(key[10:], len(camp_outlaws))
        elif key == "despised":
            code = DESPISED_CODE
        elif key.startswith("camptownline:"):
            outlaw, town = key[13:].split("|", 1)
            code = CAMPTOWN_BASE + camp_outlaws.setdefault(outlaw, len(camp_outlaws))
            if town not in camp_towns:
                camp_towns.append(town)
            value = camp_towns.index(town) + 1
        elif key.startswith("tale:"):
            code = TALE_BASE + title_order.index(key[5:])
        elif key.startswith("raid:"):
            code = RAID_BASE + raid_ids.setdefault(key[5:], len(raid_ids))
        elif key.startswith("raidtownline:"):
            raid, town = key[13:].split("|", 1)
            code = RAIDTOWN_BASE + raid_ids.setdefault(raid, len(raid_ids))
            if town not in camp_towns:
                camp_towns.append(town)
            value = camp_towns.index(town) + 1
        elif key.startswith("camptown:"):
            code = CAMPTOWN_BASE + camp_outlaws.setdefault(key[9:], len(camp_outlaws))
        elif key.startswith("campdir:"):
            code = CAMPDIR_BASE + camp_outlaws.setdefault(key[8:], len(camp_outlaws))
        elif key.startswith("struck:"):
            code = STRUCK_BASE + camp_outlaws.setdefault(key[7:], len(camp_outlaws))
        elif key.startswith("war:"):
            faction, player = key[4:].rsplit(":", 1)
            code = wars.setdefault((faction, player == "1"), WAR_BASE + len(wars))
        else:
            code = title_codes[key.split(":", 1)[1]]
        csid = _condition_record(b, code, compare_by, who)
        rec["ref"]["conditions"] = list(rec["ref"].get("conditions", [])) + [(csid, int(value), 0, 0)]
        stats["helper_conditions"] = stats.get("helper_conditions", 0) + 1
    gossip = 0
    for b in builders:
        for r in b.records:
            if r["type"] != B.T_DIALOGUE or not r["name"].startswith(GOSSIP_PREFIXES):
                continue
            r["float"]["chance temporary"] = min(100.0, 2 * float(r["float"].get("chance temporary", 100.0)))
            r["ref"]["conditions"] = list(r["ref"].get("conditions", [])) + [(_condition_record(b, GOSSIP_CODE, 0), 1, 0, 0)]
            gossip += 1
    stats["gossip_dialogues"] = gossip
    # which events each dialogue of ours is said on (EV_PLAYER_TALK_TO_ME: a conversation the player starts)
    talk_to_me = B._enum(B.EV, "EV_PLAYER_TALK_TO_ME", "event")
    events = {}
    for b in builders:
        for _pkg, adds in getattr(b, "_package_adds", {}).items():
            for dsid, ev in adds:
                events.setdefault(dsid, set()).add(ev)
        for _zone, adds in getattr(b, "_zone_adds", {}).items():
            for dsid in adds:
                events.setdefault(dsid, set()).add(-1)
    talk = gated = 0
    for b in builders:
        for r in b.records:
            if r["type"] != B.T_DIALOGUE:
                continue
            evs = events.get(r["sid"])
            if evs and talk_to_me not in evs:
                r["ref"]["conditions"] = list(r["ref"].get("conditions", [])) + [(_condition_record(b, TALK_CODE, 0), 1, 0, 0)]
                talk += 1
            for feature, test in FEATURE_GATES:
                if test(r["name"]):
                    r["ref"]["conditions"] = list(r["ref"].get("conditions", [])) + [(_condition_record(b, FEATURE_CODES[feature], 0), 1, 0, 0)]
                    gated += 1
    stats["talk_dialogues"], stats["feature_gated"] = talk, gated
    manifest = {
        "format": 1,
        "build": open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "BUILD_ID"), encoding="utf-8").read().strip() if os.path.exists(os.path.join(os.path.dirname(os.path.abspath(__file__)), "BUILD_ID")) else "",
        "note": "generated by modgen/meitou_target.py; the C# code mod LivingWastelandMeitou reads this",
        "variables": [{"name": variables[s][1], "initial": variables[s][2], "condition": CONDITION_BASE + variables[s][0],
                       "set": SET_BASE + variables[s][0], "add": ADD_BASE + variables[s][0]} for s in order],
        "takeItems": [{"code": TAKE_BASE + i, "item": sid} for sid, i in items.items()],
        "jobs": {"variable": JOB_VAR, "cap": JOB_CAP, "capCondition": JOB_CAP_CODE},
        "rumours": rumours,
        "outlaws": [{"code": c, "index": c - OUTLAW_BASE, "name": n} for n, c in outlaws.items()],
        "wars": [{"code": c, "faction": f, "player": p} for (f, p), c in wars.items()],
        "warTowns": {"factions": war_factions, "towns": [{"code": c, "town": t} for t, c in war_towns.items()]},
        "namedKills": [{"name": n, "killed": NAMED_BASE + i, "days": NAMED_DAYS_BASE + i} for n, i in named.items()],
        # faction temperaments (TITLES_BY_FACTION.md): the once-per-title memory, the job gate's despise table, the gift items
        "temperaments": {"once": TEMPER_ONCE, "mark": TEMPER_MARK, "despised": DESPISED_CODE, "titles": title_order,
                         "despises": (temperaments or {}), "views": title_views or {}, "gifts": [{"code": GIFT_BASE + g, "item": i} for i, g in gifts.items()]},
        # memorials (Notoriety.cs / Deeds.cs): the squad's fallen, the feuds they leave
        "marches": {"marching": MARCH_CODE},
        "news": {"codes": [NEWS_BASE + k for k in range(max(news_slots) + 1)], "tokens": [f"LWNEWS_{k}" for k in range(max(news_slots) + 1)]} if news_slots else None,
        "rivals": {"codes": [RIVAL_BASE + k for k in range(max(rival_slots) + 1)], "tokens": [f"LWRIVAL_{k}" for k in range(max(rival_slots) + 1)]} if rival_slots else None,
        "nearPlaces": [{"town": t, "code": NEARPLACE_BASE + i} for t, i in near_places.items()],
        "favours": {"code": FAVOUR_CODE, "cap": 40, "alms": ALMS_CODE},
        "memorials": {"mourn": MOURN_CODE, "lossDays": LOSS_DAYS_CODE, "lossTown": LOSS_TOWN_CODE, "feud": FEUD_CODE, "scar": SCAR_CODE, "squadNear": SQUAD_NEAR_CODE},
        # bounty hunters (Hunters.cs): the crew's squad template
        "hunters": hunters,
        # trophies (Trophies.cs): the keepsake item for a fallen squadmate's mourner; the neck slot is FCS slot 11
        "trophies": trophies,
        # quest variables whose first change from 0 is a Recap moment (LwMod.RegisterVariables)
        "variableMoments": var_moments or {},
        # the "Living Wasteland talk" slider and the feature switches (LwMod.RegisterTalk)
        "talk": {"code": TALK_CODE, "features": FEATURE_CODES},
        # quests paid by the length of their route (LwMod.RegisterQuestRoutes): leg and pay effect codes per quest
        "questRoutes": [{"quest": q, "leg": QUESTLEG_BASE + i, "pay": QUESTPAY_BASE + i} for q, i in quests.items()],
        # harassment raids (Raids.cs): the raiding squad template, the faction it harasses, its state and town codes
        "raids": [{"name": r["name"], "squad": r["squad"], "target": r["target"], "raider": r.get("raider", ""), "state": RAID_BASE + raid_ids[r["name"]],
                   "town": RAIDTOWN_BASE + raid_ids[r["name"]]} for r in (raids or [])],
        # the Atlas tab (Atlas.cs): places, people (with their CHARACTER ids) and the gossip lines the helper listens for
        "atlas": {"places": atlas["places"], "people": {n: {"id": p.get("id"), "epithet": p.get("epithet", ""), "place": p.get("place"),
                                                            "region": p.get("region"), "about": p.get("about", ""), "wantedFor": p.get("wanted_for"),
                                                            "hidden": bool(p.get("hidden")), "vague": p.get("vague"), "outlaw": bool(p.get("outlaw"))}
                                                        for n, p in atlas["people"].items()},
                  "lines": atlas["lines"]} if atlas else None,
        # where outlaw camps are and who the squad has struck (OutlawCamps.cs): codes CAMPTOWN/CAMPDIR/STRUCK + k
        "campNews": {"towns": camp_towns, "outlaws": [{"name": n, "town": CAMPTOWN_BASE + k, "dir": CAMPDIR_BASE + k, "struck": STRUCK_BASE + k,
                                                       "pay": DISTPAY_BASE + k, "reach": REACH_BASE + k, "newsReach": NEWSREACH_BASE + k, "successor": SUCCESSOR_BASE + k, "tier": TIER_BASE + k, "reprisal": REPRISAL_BASE + k}
                                                      for n, k in camp_outlaws.items()]},
        # moving outlaw camps (OutlawCamps.cs): the index is the outlaw-news index when the outlaw has news lines, so the camp and the
        # gossip agree on what the outlaw is doing; outlaws without news lines get indexes after those
        "camps": [{"index": outlaws[c["name"]] - OUTLAW_BASE if c["name"] in outlaws else len(outlaws) + i,
                   "name": c["name"], "character": c["character"], "squad": c["squad"], "reprisal": c.get("reprisal"), "biomes": c["biomes"], "successor": c.get("successor"),
                   **({"infiltrators": c["infiltrators"]} if "infiltrators" in c else {})}
                  for i, c in enumerate(camps)],
        "gossip": {"code": GOSSIP_CODE, "dialogues": gossip},
        "notoriety": {"heatCondition": HEAT_CONDITION, "heatEffect": HEAT_EFFECT,
                      "titles": [{"code": title_codes[t["id"]], "id": t["id"], "name": t["name"], "scope": t["scope"], "stat": t["stat"],
                                  "faction": t.get("faction") or t.get("kind"), "targets": t.get("targets") or ([t["target"]] if t.get("target") else None),
                                  "at": int(t["at"]), "legendary": bool(t.get("legendary")), "tale": TALE_BASE + i} for i, t in enumerate(titles)]},
    }
    manifest["stats"] = stats
    return manifest


def write_manifest(manifest, folder):
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, "lw_codes.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=1)
    return path


def leftover_bfrizz(builders):
    """Names of BFrizz-only references or records still present (should be empty after convert)."""
    left = []
    for b in builders:
        for r in b.records:
            if r["type"] in DROPPED_TYPES:
                left.append(f"{r['sid']} type {r['type']}")
            for key in BFRIZZ_REFS | DROPPED_REFS:
                if key in r["ref"]:
                    left.append(f"{r['sid']} {key}")
    return left
