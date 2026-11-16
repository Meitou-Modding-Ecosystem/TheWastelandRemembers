"""Writes content/x_atlas.json: the Atlas tab's data (Meitou only; Atlas.cs in the helper).

    python gen_atlas.py

From content/_atlas.json (hand-edited places and people, in our own words) plus:
  * every named outlaw (outlaws.json, recruits.json) as a person: their zones as the region, their contract's first offer line as what
    people say about them, the factions that want them; rumours are the outlaw gossip and news lines (ids on_<slug>_) and the rumour
    pack's lines about them (x_rumours.json);
  * every gossip line the helper listens for, as a template (/TOKEN/ = any text), with the person it is about when known: bar talk, the
    bosses packs, the rumours, outlaw news and gossip. When a squad member is near enough to hear one, it goes into the Atlas.
Edit _atlas.json, run this, rebuild; the JSON is generated, do not edit it by hand.
"""
import glob
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(HERE, "content")
ALIAS = {"Phoenix": "Holy Lord Phoenix", "Esata": "Esata the Stone Golem"}   # x_rumours subject -> person
MIN_LEN = 24                                                                  # shorter lines are too easily confused


def slug(name):
    return "".join(c.lower() if c.isalnum() else "_" for c in name).strip("_")


SHORT = {"Cannibal Grand Wizard": ["Grand Wizard", "Gran' Wizard"], "Holy Lord Phoenix": ["Phoenix"], "Emperor Tengu": ["Tengu"],
         "Esata the Stone Golem": ["Esata"], "High Inquisitor Valtena": ["Valtena"], "Sand Ninja Oni": ["Oni"], "Mad Cat-Lon": ["Cat-Lon"],
         "Tora The Fearless": ["Tora"], "Red Sabre Boss": ["Sabre Boss"], "Boss Simion": ["Simion"], "Crab Queen": ["Crab Queen"]}
COMMON = {"Lord", "Lady", "Boss", "King", "Queen", "Master", "Mistress", "Slave", "Big", "Mother", "Elder", "Head", "Prince", "Ghost", "Shade",
          "Mad", "High", "Moll", "Seto", "Savant"}


def aliases(people):
    """(alias, person) pairs: the full name, SHORT's forms, and a surname (last word, 4+ letters, not a title or common word) no one else has."""
    out = [(n, n) for n in people]
    for n, forms in SHORT.items():
        out += [(f, n) for f in forms if n in people]
    last = {}
    for n in people:
        w = n.split()
        if len(w) >= 2 and len(w[-1]) >= 4 and w[-1] not in COMMON and w[-1][0].isupper():
            last.setdefault(w[-1], []).append(n)
    out += [(w, ns[0]) for w, ns in last.items() if len(ns) == 1]
    return out


# outlaws roam their ground: the Atlas says so in their epithet and adds a line on how they move (no one is found in one place for long)
ROAMERS = {
    "Orsa Quillon": ("a roaming dust boss", "Her band camps a few nights at a time along the trade roads, then moves on."),
    "Ferrant Dray": ("a wandering mercenary captain", "His company sells its swords from camp to camp and never winters in one place."),
    "Hask Morrow": ("a wandering deserter", "Since leaving the Nation he sleeps rough, a different hill every few nights."),
    "Sarn Ashvael": ("a roaming starving bandit", "His people follow the caravans and the food, wherever they go."),
    "Veldt Harrow": ("a travelling slaver", "He moves his pens along the desert roads, selling as he goes."),
    "Borun Vahl": ("a roaming berserker", "He goes where the fighting is and stays until it runs out."),
    "Ilsa Hearthmourn": ("a wandering heretic", "She preaches from camp to camp, always a step ahead of the Inquisition."),
    "Hodd Crake": ("a roaming cannibal hunter", "He follows the cannibal raids south and back north again."),
    "Pim Sillery": ("a travelling tech thief", "He drifts from ruin to ruin with whatever he can carry."),
    "Teshin Ro": ("a wandering swordsman", "He walks the border roads looking for duels and for work."),
    "Ysolde Gorehollow": ("a roaming blood raider", "Her raiders sweep the Ashlands and never stay to be found."),
    "Marta Cull": ("a raiding reaver captain", "Her crew lands on a different stretch of coast each time."),
    "Teodric Clackjaw": ("a roaming crab raider", "His raiders come up the beaches and are gone before the patrols arrive."),
    "Maruka Tal-Khesh": ("a wandering exile of Kral's Chosen", "She marches from canyon to canyon looking for a worthy fight."),
    "Aldous Peel": ("a travelling skin collector", "He walks the Roam looking for new skins and never sleeps twice in the same place."),
    "Mister Tuppence": ("a wandering skeleton bandit", "He doesn't tire and doesn't stop, so his camp is wherever he is."),
    "Ulla Weeper": ("a roaming shrieker", "Her band moves through the forest and the swamp, and you hear them before you see them."),
    "Dunmar Quell": ("a roaming Red Sabre", "He moves between the swamp villages, collecting what he calls tax."),
    "Gammer Tuck": ("a wandering rebel captain", "His rebels move from farm to farm, taking the harvest as they go."),
    "Sayo of the Grey Ribbon": ("a wandering sand ninja", "She takes work in one town and is gone by the next."),
    "Cuddy Pell": ("a drifting madman", "He wanders the flooded coast with no plan anyone can follow."),
    "Tchik of Seven Voices": ("a roaming hiveless drone", "He leads his band through the northern wastes, never back the same way."),
    "Elric Vosk": ("a wandering sellsword", "He follows the work across the desert and the border country."),
    "Pyke Ludlow": ("a roaming slave-freer", "He moves between the slave roads, hitting the caravans and vanishing."),
    "Orrin Vask": ("a travelling slaver", "He moves his stock along the desert roads, town to town."),
    "Aldric Roane": ("a wandering deserter", "He keeps moving through the Nation's hills, away from his old order."),
    "Karrash Red-Hand": ("a roaming berserker", "He drifts from fight to fight across the plains."),
    "Veth of the Closed Hand": ("a roaming hiveless drone", "His band crosses the northern wastes and rarely stops."),
    "Hollis Gantry": ("a travelling tech thief", "He goes from ruin to ruin, selling what he digs up."),
    "Gaffer Marrowbone": ("a roaming cannibal chief", "His raiders range across the plains, never long in one village."),
    "Rennik Salt": ("a roaming dust boss", "His band camps by the roads for a few nights at a time, then moves on."),
    "Slate Fennick": ("a wandering outlaw captain", "His swordsmen roam the border country looking for easy marks."),
    "Brannoch Teel": ("a travelling slave hunter", "He rides the roads after runaways, wherever they run."),
    "Hob Lenner": ("a raiding reaver", "His crew comes in from the coast and is gone by morning."),
    "Dolan Pike": ("a roaming starving bandit", "His people follow the caravans, starving or not."),
    "Isolde Marr": ("a wandering mercenary captain", "The company goes wherever the pay is, camp to camp."),
}


def says(lines):
    for ln in lines or []:
        if not ln.get("speaker") and ln.get("say") is not None and not ln.get("silent"):
            for s in (ln["say"] if isinstance(ln["say"], list) else [ln["say"]]):
                if isinstance(s, str):
                    yield s
        yield from says(ln.get("then"))


def main():
    src = json.load(open(os.path.join(CONTENT, "_atlas.json"), encoding="utf-8"))
    people = dict(src["people"])
    contracts = {}
    for f in ("outlaws.json", "recruits.json"):
        d = json.load(open(os.path.join(CONTENT, f), encoding="utf-8"))
        for c in d.get("contracts", []):
            contracts[c["state"]] = c
    for f in ("outlaws.json", "recruits.json"):
        for o in json.load(open(os.path.join(CONTENT, f), encoding="utf-8"))["outlaws"]:
            c = contracts.get(f"{o['name']} killed by player")
            by = o.get("bounty_factions") or []
            people.setdefault(o["name"], {
                "epithet": ROAMERS.get(o["name"], ("a roaming outlaw",))[0], "region": " and ".join((o.get("zones") or [])[:2]) or "the wastes", "outlaw": True,
                "about": ((o.get("about") or (c["offer"][0] if c else "An outlaw with a price on their head.")) + (" " + ROAMERS[o["name"]][1] if o["name"] in ROAMERS else "")),
                "wanted_for": ("Wanted by " + ", ".join(by) + ".") if by else "",
                "rumours": [f"on_{slug(o['name'])}_"]})
            if o.get("successor"):
                sc = o["successor"]
                rel = sc.get("relation", "one of the gang")
                short = o["name"].split()[0] if " of " in o["name"] else o["name"].split()[-1]
                people.setdefault(sc["name"], {
                    "epithet": f"who took over {o['name']}'s gang", "region": " and ".join((o.get("zones") or [])[:2]) or "the wastes",
                    "outlaw": True,
                    "about": (sc.get("story", "") + f" Took over {o['name']}'s gang. For now the price on them is smaller than {short}'s was.").strip(),
                    "wanted_for": ("Wanted by " + ", ".join(by) + ".") if by else "",
                    "rumours": [f"on_{slug(o['name'])}_succ_"]})
    prefixes = sorted(((pre, name) for name, p in people.items() for pre in p.get("rumours", [])), key=lambda t: -len(t[0]))

    lines, seen = [], set()
    def add(subject, text):
        text = text.strip()
        if len(text) < MIN_LEN or (subject, text) in seen:
            return
        seen.add((subject, text))
        lines.append([subject, text])
    for f in sorted(glob.glob(os.path.join(CONTENT, "*.json"))):
        base = os.path.basename(f)
        if base.startswith("_") or base == "x_atlas.json":
            continue
        d = json.load(open(f, encoding="utf-8"))
        for x in d.get("dialogues", []):
            subject = next((n for pre, n in prefixes if x["id"].startswith(pre)), "")
            if not subject and x.get("event") != "EV_BAR_TALK":
                continue
            for s in says(x.get("lines")):
                add(subject, s)
        for r in d.get("rumours", []):
            subject = ALIAS.get(r.get("name"), r.get("name"))
            for s in (r.get("fresh") or []) + (r.get("lasting") or []):
                add(subject if subject in people else "", s)
    # a line with no subject that names someone short ("Peel's lot", "the Grand Wizard", "Valtena") is about them: the helper only finds full names
    alias = aliases(people)
    for ln in lines:
        if not ln[0]:
            hit = {n for a, n in alias if re.search(r"\b" + re.escape(a) + r"\b", ln[1])}
            if len(hit) == 1:
                ln[0] = hit.pop()
    lines[:] = [ln for i, ln in enumerate(lines) if ln[0] or not any(o[0] and o[1] == ln[1] for o in lines)]
    for name, p in people.items():
        p.pop("rumours", None)
    pack = {"pack": "x_atlas", "mod": "LivingWasteland", "meitou_only": True,
            "note": "GENERATED by gen_atlas.py from _atlas.json and the gossip packs (do not edit by hand). Meitou only.",
            "atlas": {"places": src["places"], "people": people, "lines": lines}}
    path = os.path.join(CONTENT, "x_atlas.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(pack, fh, indent=1, ensure_ascii=False)
        fh.write("\n")
    told = sum(1 for s, _ in lines if s)
    print(f"wrote {path}: {len(src['places'])} places, {len(people)} people, {len(lines)} lines ({told} about someone)")
    quiet = [n for n in people if not any(s == n for s, _ in lines)]
    if quiet:
        print("people with no gossip lines yet:", ", ".join(quiet))


if __name__ == "__main__":
    main()
