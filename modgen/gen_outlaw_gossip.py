"""Writes content/x_outlaw_gossip.json: gossip about where each outlaw's camp is, and about outlaws the squad struck (Meitou only).

    python gen_outlaw_gossip.py

Per outlaw (outlaws.json and recruits.json):
  * two word swaps the helper fills: /LWNEAR_k/ (the town nearest the camp) and /LWDIR_k/ ("east of", "right outside" ...),
    picked by the helper's camp conditions (OutlawCamps.cs, codes CAMPTOWN / CAMPDIR in meitou_target.py);
  * "where" lines (bar talk and on sight) while the helper knows the camp ("camp" lw_if: the Outlaw camps setting is on);
  * "struck" lines for five game days after the squad hit the outlaw's people and the outlaw lived ("struck" lw_if).
Speakers are the factions that want the outlaw dead, plus the Traders Guild and the Nomads (who carry the news of the roads). Ids start with "on_" so the Gossip amount slider covers them.
Lines avoid pronouns (the recruitable outlaws have no fixed sex). The towns are the war-talk towns (x_world_news.json word swaps).
Edit the LINES below, run this, rebuild; the JSON is generated, do not edit it by hand.
"""
import json
import os
from detrope import detrope_pack
from contractions import contract_pack

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(HERE, "content")

DIRS = ["north of", "north-east of", "east of", "south-east of", "south of", "south-west of", "west of", "north-west of", "right outside"]

# where an outlaw's camp is, and that the squad hurt them; each outlaw gets three of a pool (a different three per outlaw)
WHERE_BAR = ["{n}'s lot are camped /DIR/ /NEAR/. Nobody's gone to look.",
             "Smoke /DIR/ /NEAR/ these last nights. {n}, they say.",
             "You want {n}? Try /DIR/ /NEAR/.",
             "A trader saw {n}'s fires /DIR/ /NEAR/.",
             "{n} has a camp /DIR/ /NEAR/ these days.",
             "If you're after {n}, they've been sleeping /DIR/ /NEAR/."]
WHERE_SIGHT = ["{n} is camped /DIR/ /NEAR/, if you're asking.",
               "Try /DIR/ /NEAR/ if you're looking for {n}.",
               "Last I heard, {n} was /DIR/ /NEAR/.",
               "{n}'s people are holed up /DIR/ /NEAR/."]
STRUCK_BAR = ["Somebody bloodied {n}'s crew. They've gone to ground.",
              "Someone gave {n}'s people a hiding. They're keeping their heads down now.",
              "{n}'s crew got hurt recently. They've pulled back.",
              "Heard {n} lost some people in a fight."]
STRUCK_SIGHT = ["Was that you, with {n}? They've pulled back since.",
                "You're the ones who hit {n}'s crew, aren't you?",
                "Heard you roughed up {n}'s people."]


def pick(pool, k, n=3):
    """n entries of a pool for outlaw k, a different set per outlaw (rotating)."""
    return [pool[(k + i * 2) % len(pool)] for i in range(min(n, len(pool)))]


# a growing bounty, by tier; each outlaw gets two of the pool (a different two per outlaw), so the same sentence is not said of every outlaw
PRICE = {1: ["The price on {n} went up again.", "{n}'s bounty keeps climbing. Somebody will try for it soon.",
             "The Cities raised the bounty on {n}.", "{n} is worth more than last month, if you can catch them.",
             "Every caravan {n} takes puts a few more cats on that bounty.", "Saw a fresh poster for {n}. Bigger number on it."],
         2: ["{n} is worth more dead than most farms earn in a year.", "Bounty hunters talk about {n} now.",
             "Half the mercenaries in the bar want a go at {n}.", "That bounty on {n} is getting serious.",
             "People are starting to plan around the price on {n}.", "Somebody's going to get rich off {n} one day."],
         3: ["There's a fortune on {n}'s head and nobody's collected it.", "{n}. Biggest price on the road.",
             "You could retire on what {n}'s head is worth.", "Nobody's managed to bring in {n}, and plenty have tried.",
             "The bounty on {n} is the kind people die chasing.", "Whoever takes {n} won't need to work again."]}


# outlaw reprisals: what the outlaw's men say when they find the squad (the helper's reprisal parties, Outlaw reprisals setting)
REPRISAL = ["{n} remembers you. We're here to settle it.",
            "{n} sends regards. You won't like them.",
            "You bloodied {n}'s people. Now it's your turn.",
            "There they are. {n} wants them on the ground."]

# the same for a cannibal outlaw's men, in the tribe's tongue (vanilla cannibals speak this way: "Dhis one tasty", "Bery ungry")
REPRISAL_CANNIBAL = ["{n} remember you. We come settle.",
                     "{n} send hello. You no like.",
                     "You hurt {n} people. Now your turn.",
                     "Dhere dhey are! {n} want dhem in pot."]

TOWN_CHECKS = [{"cond": "DC_IN_A_NON_PLAYER_TOWN", "who": "me", "value": 1},
               {"cond": "DC_IN_COMBAT", "who": "me", "value": 0},
               {"cond": "DC_IS_SLAVE", "who": "me", "value": 0}]


def outlaws():
    out = []
    for f in ("outlaws.json", "recruits.json"):
        for o in json.load(open(os.path.join(CONTENT, f), encoding="utf-8"))["outlaws"]:
            out.append(o)
    return out


def towns():
    news = json.load(open(os.path.join(CONTENT, "x_world_news.json"), encoding="utf-8"))
    seen = []
    for w in news["word_swaps"]:
        for ln in w["lines"]:
            if ln.get("wartown") and ln["wartown"] not in seen:
                seen.append(ln["wartown"])
    return seen


def slug(name):
    return "".join(c.lower() if c.isalnum() else "_" for c in name).strip("_")


SUCC_BAR = ["{o}'s gone. The gang belongs to {s} now, {rel}.", "Reckoned the roads would go quiet after {o}. Then {s} took over.",
            "{s} has {o}'s old crew now. The raids never really stopped.", "Nobody missed {o} for long, with {s} running things.",
            "{s} sits in {o}'s old place at the fire now."]
SUCC_SIGHT = ["Mind yourself out there. {s} took over {o}'s lot.", "If you were hoping {o}'s gang was done, it isn't. {s} has it now.",
              "{s}'s people are on the roads now. They used to be {o}'s."]


def main():
    town_list = towns()
    pack = {"pack": "x_outlaw_gossip", "mod": "LivingWastelandOutlaws",
            "note": "GENERATED by gen_outlaw_gossip.py (do not edit by hand). Meitou only: where outlaw camps are, and outlaws the squad struck.",
            "dialogues": [], "word_swaps": []}
    for k, o in enumerate(outlaws()):
        name, s = o["name"], slug(o["name"])
        near, way = f"LWNEAR_{k}", f"LWDIR_{k}"
        pack["word_swaps"].append({"name": near, "lines": [{"say": t, "camptown": t, "outlaw": name} for t in town_list] + [{"say": "the old roads"}]})
        pack["word_swaps"].append({"name": way, "lines": [{"say": d, "campdir": i + 1, "outlaw": name} for i, d in enumerate(DIRS)] + [{"say": "out past"}]})
        # the Nomads walk between towns and carry the news of the roads
        speakers = list(dict.fromkeys((o.get("bounty_factions") or []) + ["Traders Guild", "Nomads"]))

        def fill(lines):
            return [ln.format(n=name).replace("/NEAR/", f"/{near}/").replace("/DIR/", f"/{way}/") for ln in lines]

        for kind, cond, bar, sight in (("where", {"camp": name}, WHERE_BAR, WHERE_SIGHT),
                                       ("struck", {"struck": name, "cmp": "<", "value": 5}, STRUCK_BAR, STRUCK_SIGHT)):
            pack["dialogues"].append({"id": f"on_{s}_{kind}_bar", "event": "EV_BAR_TALK", "factions": speakers, "one_at_a_time": True,
                                      "lw_if": [cond], "lines": [{"say": fill(pick(bar, k))}], "chance": 9, "repeat": 4})
            pack["dialogues"].append({"id": f"on_{s}_{kind}_sight", "event": "EV_I_SEE_NEUTRAL_SQUAD", "player_only": True,
                                      "factions": speakers, "one_at_a_time": True, "lw_if": [cond], "lines": [{"say": fill(pick(sight, k))}],
                                      "chance": 3, "repeat": 4, "if": TOWN_CHECKS})
        for tier, lines in PRICE.items():
            pack["dialogues"].append({"id": f"on_{s}_price{tier}_bar", "event": "EV_BAR_TALK", "factions": speakers, "one_at_a_time": True,
                                      "lw_if": [{"bounty_tier": name, "value": tier}], "lines": [{"say": [ln.format(n=name) for ln in (lines[k % len(lines)], lines[(k + 2) % len(lines)])]}],
                                      "chance": 8, "repeat": 3})
        if o.get("successor"):
            # the successor (OutlawCamps.cs): talk once one of the gang has taken over the dead outlaw's ground
            sc = o["successor"]
            f = dict(o=name, s=sc["name"], rel=sc.get("relation", "one of the gang"))
            pack["dialogues"].append({"id": f"on_{s}_succ_bar", "event": "EV_BAR_TALK", "factions": speakers, "one_at_a_time": True,
                                      "lw_if": [{"successor": name}], "lines": [{"say": [t.format(**f) for t in SUCC_BAR]}], "chance": 9, "repeat": 3})
            pack["dialogues"].append({"id": f"on_{s}_succ_sight", "event": "EV_I_SEE_NEUTRAL_SQUAD", "player_only": True, "factions": speakers,
                                      "one_at_a_time": True, "lw_if": [{"successor": name}], "lines": [{"say": [t.format(**f) for t in SUCC_SIGHT]}],
                                      "chance": 3, "repeat": 2, "if": TOWN_CHECKS})
        if o.get("faction"):
            pack["dialogues"].append({"id": f"on_{s}_reprisal", "event": "EV_I_SEE_ENEMY_PLAYER", "factions": [o["faction"]],
                                      "lw_if": [{"reprisal": name}], "lines": [{"say": [ln.format(n=name) for ln in (REPRISAL_CANNIBAL if o["faction"] == "Cannibals" else REPRISAL)]}],
                                      "chance": 100, "repeat": 2})
    path = os.path.join(CONTENT, "x_outlaw_gossip.json")
    with open(path, "w", encoding="utf-8") as fh:
        detrope_pack(pack)                 # the same punchline pass as content/ (detrope.py)
        contract_pack(pack)                # contractions by voice (contractions.py)
        json.dump(pack, fh, indent=1, ensure_ascii=False)
        fh.write("\n")
    print(f"wrote {path}: {len(pack['dialogues'])} dialogues, {len(pack['word_swaps'])} word swaps, {len(town_list)} towns")


if __name__ == "__main__":
    main()
