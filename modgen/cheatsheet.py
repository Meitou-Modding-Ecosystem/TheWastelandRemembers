"""Write TEST_CHEATSHEET.md: where to find the outlaws, contracts and the Gate camp, read straight from the content packs.

  python cheatsheet.py            (writes modgen/TEST_CHEATSHEET.md)
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))


def load(name):
    return json.load(open(os.path.join(HERE, "content", name), encoding="utf-8"))


def main():
    out = ["# Test cheat sheet (generated from the content packs)", ""]
    o = load("outlaws.json")
    contracts = {c["state"]: c for c in o["contracts"]}
    out += ["## Named outlaws (rare, among roaming bandits; use the Forgotten Construction Set to spawn one for a quick test)", "",
            "| Outlaw | Faction | Bounty | Lives in | Contract from | Reward |", "|---|---|---|---|---|---|"]
    for x in o["outlaws"]:
        c = contracts.get(x["name"] + " killed by player", {})
        out.append(f"| {x['name']} | {x['faction']} | {x['bounty']} | {', '.join(x['biomes'])} | "
                   f"{', '.join(c.get('factions', []))} | {c.get('reward', '')} |")
    out += ["", "Contract offers appear in bars of the listed factions. Accept, kill the outlaw, then talk to the same kind of person to be paid.", ""]
    d = load("cannibals_data.json")
    out += ["## The Gate (Gate Folk camp)", "",
            "* Placed with nests in: " + ", ".join(sorted({c['name'] for c in d.get('changes', []) if c.get('kind') == 'zone'})) + " (edge of cannibal land).",
            "* Needs a new game or an import. Warden Hush is the gate-keeper.",
            "* Peace: carry 3 or more Raw Meat and talk to Warden Hush (needs the Heat file and BFrizz Extra Extensions). Repeat to become acquaintance, then friend.",
            "* Alder Quill (bounty 20000) and Mother Brine (bounty 18000) live in the cannibal nest town.", ""]
    c = load("x_courier.json")
    out += ["## Courier errands (offer chance 12 percent per talk, giver must like you; item appears when you accept)", "",
            "| Errand | Gives | Giver | Carry to | Pay |", "|---|---|---|---|---|"]
    for q in c["quests"]:
        o_, st = q["offer"], q["steps"][0]
        giver = ", ".join(o_.get("packages") or o_.get("factions") or o_.get("races") or [])
        dest = ", ".join(st.get("packages") or st.get("factions") or st.get("races") or [])
        item = ", ".join(i[0] for i in o_.get("give_items", []))
        out.append(f"| {q['id']} | {item} | {giver} | {dest} | {q['report'].get('reward', '')} |")
    out += ["", "Reward items: the Sealed Note errand also hands over the Old Empire Katana, the Knotted Cord errand the Mended Longcoat (both modest, below Meitou).", ""]
    out += ["## Quick checks for the newest packs", "",
            "* Talk menus (tm_): talk to a Starving Bandit holding Bread/Dried Meat in your pack; talk to a Dust Bandit follower for the 150-cat toll; bluff needs a squad of 3+.",
            "* Grudges (gm_): beat a Dust Bandit squad, walk away, meet another Dust Bandit later. One remark, then it clears.",
            "* Event reactions (re_): fail an assassination near a guard; carry off a captive in front of a witness; steal in view of a shopkeeper.",
            "* Bribery and tolls (gb_): carry a backpack past Dust Bandits or a Holy Nation paladin as a woman alone, then with a man.",
            "* Boss voices (bv_): talk to Esata, Bayan, Phoenix, Tengu or Grim at low and high relations (Bayan's alliance menu must stay reachable).",
            "* Lua body module (optional): set body = true in lw/config.lua; get a squad member to lose a limb or bleed.", ""]
    out += ["## Play Test Squad starts (pick one on the new-game screen: Hub, Ashlands, Gate, Drifter's Last (Dust Bandits))", "",
            "Four characters (fighter, medic, scout, crafter) with 95 in every stat starting together (Hub by default, or Dome II, Flotsam Village or Drifter's Last for the copies) with 15,000 cats each, bandages, food and 6 Raw Meat. Locations cannot differ per member in a start, so use the Cheat Menu teleport:",
            "",
            "| To test | Go to |", "|---|---|",
            "| Bars, recruits, shops | The Hub, Heng, Bast, Shark, Mourn, World's End |",
            "| The Gate camp, Warden Hush | Northern Coast or Hidden Forest edge (carry 3 Raw Meat) |",
            "| Grudges, tolls, starvers | Desert and Ancient Hills (Dust Bandits); Swamp (Red Sabres); coast (Reavers) |",
            "| Horror regions | Skinner's Roam, Cannibal Plains, Fog Islands, the Pits |",
            "| Domes | Ashlands (I: Head of Agriculture, II: Rhinobot, III: General Jang, IV: General Hat-12) |", ""]
    path = os.path.join(HERE, "TEST_CHEATSHEET.md")
    open(path, "w", encoding="utf-8").write("\n".join(out))
    print("wrote", path)


if __name__ == "__main__":
    main()
