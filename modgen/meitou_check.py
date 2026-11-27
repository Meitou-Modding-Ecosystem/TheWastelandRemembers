"""Meitou's view of our built data, without running the game.

Reads the .mod files of a `build_mod.py --target meitou` build and reports what Meitou's dialogue system would ignore or not answer, from
what its own source reads (MeitouClient master of 2026-11-08, the code behind the 0.17.2 release):

* reference keys on DIALOGUE / DIALOGUE_LINE records that `DialogueDatabase` never reads (they are silently ignored there);
* condition numbers below 1000 that `DialogueSystem.Conditions` does not compute (they read as "no");
* effect numbers below 1000 that `DialogueSystem.Effects` does not run;
* mod codes (1000 and up) that no handler in lw_codes.json answers (a condition then reads "no", an effect does nothing);
* BFrizz-only records left over (variables, LUA_SCRIPT).

    python meitou_check.py BUILD_DIR            (BUILD_DIR holds the mod folders; default: ./build)

Exit code 1 when a mod code has no handler or a BFrizz record is left: those break quests. The rest is a report.
"""
import collections
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "server", "tools"))
import fcs_reader  # noqa: E402

# Keys Meitou's DialogueDatabase reads from DIALOGUE / DIALOGUE_LINE records (src/Meitou.Data/Gameplay/Dialogue/DialogueDatabase.cs).
READ_REFS = {"lines", "conditions", "effects", "world state", "unlocks", "unlock but keep me", "locks", "interrupt", "give item",
             "change relations", "crowd trigger", "trigger campaign", "change AI", "AI contract", "target has item", "target has item type",
             "target race", "target faction", "target carrying character", "my subrace", "my race", "my faction", "is character",
             "in town of", "has package", "races"}
# Conditions DialogueSystem.Conditions computes (all of 0..79 except these); 23 and 26 are folded into tag masks at load.
DC_NOT_BUILT = {49: "DC_TOWN_HAS_FORTIFICATIONS_WALLS", 70: "DC_USING_MY_TRAINING_EQUIPMENT"}
# Effects DialogueSystem.Effects (and Ambient for 12) runs; the original's executor also does nothing for 22, 26, 27, 31, 44-46, 50, 51.
DA_NOT_BUILT = {15: "DA_CLEAR_AI", 22: "DA_MATES_KILL_ME (no-op in the original too)", 26: "DA_FREE_TARGET_SLAVE (no-op in the original too)",
                27: "DA_MERGE_WITH_SIMILAR_SQUADS (no-op in the original too)", 31: "DA_ATTACK_TOWN (no-op in the original too)",
                39: "DA_VICTORY_PHASE", 43: "CHOOSE_PRISONER_BAIL", 44: "CHOOSE_CONSCRIPTION", 45: "CHOOSE_RECRUITING",
                46: "CHOOSE_HIRING_CONTRACT", 50: "DA_PLAYER_SELL_PRISONERS", 51: "DA_PLAYER_SURRENDER_MEMBER_DIFFERENT_RACE", 58: "DA_KNOCKOUT"}
KENSHI_ONLY_TYPES = {1000: "BFrizz variable", 322: "LUA_SCRIPT"}
# Events Meitou's simulation fires, from a search of its source for the event constants and fire calls plus its docs (Observed, not exhaustive):
# a dialogue on any other event is attached fine but may never be said there. 48 is the biome arrival banter (DialogueSystem.Arrival).
# Rechecked 2026-11-10 (master e596744) through every fire path: FireEvent, Fire, and the deferred DialogueEvent commands (cages, slaves, torture,
# town stalkers), which added 10, 11, 26, 35, 37, 38, 39.
MEITOU_EVENTS = {1, 2, 3, 8, 9, 10, 11, 13, 14, 17, 18, 19, 20, 21, 22, 23, 24, 26, 27, 30, 32, 33, 34, 35, 36, 37, 38, 39, 41, 42, 43, 44, 45,
                 46, 47, 48, 49, 51, 53, 56, 57, 58, 59, 60, 61, 62, 63, 65, 66, 67, 69, 70, 71, 73, 74}
# Events our helper (LivingWastelandMeitou) fires itself: 12 bar talk, for NPCs in the building a player character is in (BarTalk.cs).
HELPER_EVENTS = {4, 6, 12, 15, 72}     # 4 I_SEE_RAGDOLL (mourning), 6 SOUND_THE_ALARM (lookout calls, Lookout.cs), 12 bar talk (BarTalk), 15 I_DEFEATED_SQUAD (victory lines), 72 I_SEE_PLAYER_NICE_BUILDING (town criers, Criers.cs)


def load(build):
    mods = {}
    for name in sorted(os.listdir(build)):
        path = os.path.join(build, name, name + ".mod")
        if os.path.isfile(path):
            p = fcs_reader.parse(path)
            mods[name] = p["records"] if isinstance(p, dict) and "records" in p else p
    return mods


def handlers(build):
    path = os.path.join(build, "LivingWastelandMeitou", "lw_codes.json")
    if not os.path.isfile(path):
        return None, set(), set()
    m = json.load(open(path, encoding="utf-8"))
    conds = {v["condition"] for v in m["variables"]}
    if m.get("jobs", {}).get("capCondition"):
        conds.add(m["jobs"]["capCondition"])
    conds |= {r["code"] for r in m.get("rumours", [])}
    effects = {v["set"] for v in m["variables"]} | {v["add"] for v in m["variables"]} | {t["code"] for t in m["takeItems"]}
    conds |= {t["code"] for t in m["takeItems"]}    # LwMod.RegisterTakes answers each take code as a condition too (how many the bag holds)
    if n := m.get("notoriety"):
        conds |= {n["heatCondition"]} | {t["code"] for t in n.get("titles", [])}
        effects.add(n["heatEffect"])
    conds |= {o["code"] for o in m.get("outlaws", [])} | {w["code"] for w in m.get("wars", [])}
    if m.get("gossip"):
        conds.add(m["gossip"]["code"])
    conds |= {t["code"] for t in m.get("warTowns", {}).get("towns", [])}
    conds |= set((m.get("rivals") or {}).get("codes", []))
    conds |= set((m.get("news") or {}).get("codes", []))
    conds |= {n["killed"] for n in m.get("namedKills", [])} | {n["days"] for n in m.get("namedKills", [])}
    conds |= {c[k] for c in m.get("campNews", {}).get("outlaws", []) for k in ("town", "dir", "struck", "reach", "newsReach", "successor", "tier", "reprisal") if k in c}
    effects |= {c["pay"] for c in m.get("campNews", {}).get("outlaws", []) if "pay" in c}
    conds |= {r[k] for r in m.get("raids", []) for k in ("state", "town")}
    effects |= {r[k] for r in m.get("questRoutes", []) for k in ("leg", "pay")}
    effects |= {m["favours"]["code"], m["favours"].get("alms", m["favours"]["code"])} if m.get("favours") else set()
    conds |= ({m["talk"]["code"]} | set(m["talk"]["features"].values())) if m.get("talk") else set()
    conds |= {t["tale"] for t in m.get("notoriety", {}).get("titles", []) if "tale" in t}
    conds |= set(m.get("marches", {}).values())
    conds |= {n["code"] for n in m.get("nearPlaces", [])}
    if mem := m.get("memorials"):
        conds |= set(mem.values())
    if t := m.get("temperaments"):
        conds |= {t["once"], t["despised"]} | ({t["giver"]} if t.get("giver") else set())
        effects |= {t["mark"]} | {g["code"] for g in t.get("gifts", [])}
    return m, conds, effects


def check(build):
    mods = load(build)
    manifest, cond_ok, eff_ok = handlers(build)
    by_sid = {r["sid"]: r for recs in mods.values() for r in recs}
    ignored, dc_bad, da_bad, code_bad, kenshi_only = (collections.defaultdict(list) for _ in range(5))
    counts = collections.Counter()

    def owner(r):
        return r.get("name") or r["sid"]

    for mod, recs in mods.items():
        for r in recs:
            if r["type"] in KENSHI_ONLY_TYPES:
                kenshi_only[KENSHI_ONLY_TYPES[r["type"]]].append(f"{mod}: {owner(r)}")
            if r["type"] not in (18, 19):
                continue
            counts["dialogues" if r["type"] == 18 else "lines"] += 1
            for key in r["ref"]:
                if key not in READ_REFS:
                    ignored[key].append(owner(r))
            for kind, table in (("conditions", "condition name"), ("effects", "action name")):
                for e in r["ref"].get(kind, []):
                    a = by_sid.get(e[0])
                    if not a:
                        continue
                    n = a["int"].get(table)
                    if n is None:
                        continue
                    if n >= 1000:
                        known = cond_ok if kind == "conditions" else eff_ok
                        if n not in known:
                            code_bad[f"{kind[:-1]} {n}"].append(owner(r))
                    elif kind == "conditions" and n in DC_NOT_BUILT:
                        dc_bad[DC_NOT_BUILT[n]].append(owner(r))
                    elif kind == "effects" and n in DA_NOT_BUILT:
                        da_bad[DA_NOT_BUILT[n]].append(owner(r))

    def section(title, table, note):
        print(f"\n{title}: {sum(len(v) for v in table.values())} uses" + (f" ({note})" if note else ""))
        for k, v in sorted(table.items(), key=lambda x: -len(x[1])):
            print(f"  {k}: {len(v)}  e.g. {', '.join(sorted(set(v))[:3])}")

    print(f"Meitou check of {build}: {len(mods)} mods, {counts['dialogues']} dialogues, {counts['lines']} lines")
    print("handlers:", "lw_codes.json missing" if manifest is None else f"{len(cond_ok)} condition codes, {len(eff_ok)} effect codes")
    section("References Meitou's dialogue reader ignores", ignored, "silently dropped there")
    section("Conditions Meitou does not compute", dc_bad, "read as no")
    section("Effects Meitou does not run", da_bad, "do nothing")
    section("Mod codes with no handler", code_bad, "BREAKS quests")
    section("Kenshi-only records left in the Meitou build", kenshi_only, "should be none")
    broken = bool(code_bad) or bool(kenshi_only) or manifest is None
    import builder as B
    names = {v: k for k, v in B.EV.items()}
    used = collections.Counter()
    for recs in mods.values():
        for r in recs:
            if r["type"] == 73:
                for e in r["ref"].get("dialogs", []):
                    for ev in e[1:4]:
                        if 0 < ev <= 75:
                            used[ev] += 1
    quiet = {names.get(ev, ev): n for ev, n in used.items() if ev not in MEITOU_EVENTS | HELPER_EVENTS}
    helped = sum(n for ev, n in used.items() if ev in HELPER_EVENTS)
    print(f"\nAttachments on events our helper fires (bar talk, victory lines): {helped}")
    print(f"Attachments on events Meitou may not fire yet: {sum(quiet.values())} of {sum(used.values())} (search of its source, not exhaustive)")
    for k, n in sorted(quiet.items(), key=lambda x: -x[1]):
        print(f"  {k}: {n}")
    print("\nresult:", "PROBLEMS that break quests" if broken else "no quest-breaking problems")
    return 1 if broken else 0


if __name__ == "__main__":
    sys.exit(check(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "build")))
