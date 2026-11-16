# One-time variety pass on content/x_world_news.json (already applied): the six outlaw-news sentences every outlaw shared get five or six
# plain alternatives, and each outlaw is given a different one. Kept for reference; running it again changes nothing.
import json, re, sys, zlib
POOLS = {
 "{n} is taking on hands. Hungry men will follow anyone who feeds them.": [
   "{n} is taking on hands.", "{n}'s been recruiting. Paying in food, mostly.", "Heard {n} is hiring. Anyone with a blade and an empty belly.",
   "{n} is gathering people again.", "A lot of hungry men are joining up with {n} lately.", "{n} has been picking up strays off the road."],
 "Word is {n} has more blades than last month.": [
   "{n} has more blades than last month.", "{n}'s gang is getting bigger.", "There are more of {n}'s people around than there used to be.",
   "{n} has a lot more friends these days.", "Word is {n} has been arming new recruits."],
 "Some of the drifters who came through here went off to join {n}.": [
   "A couple of drifters from here went off to join {n}.", "Lost two regulars to {n} this month.", "Some lads from round here ran off to join {n}.",
   "A few drifters left to sign on with {n}.", "Some of the drifters who came through here went off to join {n}."],
 "Some of the drifters who came through here went off to join {n}. Fools.": [
   "A couple of drifters from here went off to join {n}. Idiots.", "Lost two regulars to {n} this month.", "Some lads from round here ran off to join {n}. They won't be back.",
   "A few drifters left to sign on with {n}.", "Some of the drifters who came through here went off to join {n}. Fools."],
 "{n} is hurt. If you wanted that bounty, now is the time.": [
   "{n} is hurt. If you want the bounty, now's your chance.", "{n} got hurt bad. Might be a good time to collect.", "{n}'s still recovering. Easier target right now.",
   "Heard {n} can barely stand. Someone should cash in that bounty.", "{n} is laid up somewhere, wounded."],
 "Someone cut {n} up badly.": [
   "Someone cut {n} up badly.", "{n} took a real beating recently.", "Somebody finally gave {n} a proper thrashing.", "{n}'s gang got mauled.",
   "{n} lost a fight, and a lot of blood with it."],
 "Nobody has seen {n} in days. That's when you worry.": [
   "Nobody's seen {n} in days.", "{n} hasn't been seen in a while.", "No word from {n} lately. Makes people nervous.", "{n}'s gone to ground.",
   "Haven't heard anything about {n} in days."],
}
p = sys.argv[1]
d = json.load(open(p))
names = sorted({c["outlaw"] for x in d["dialogues"] for c in (x.get("lw_if") or []) if isinstance(c.get("outlaw"), str)})
changed = 0
def fix(o, name):
    global changed
    if isinstance(o, dict):
        if "say" in o:
            vs = o["say"] if isinstance(o["say"], list) else [o["say"]]
            out = []
            for i, t in enumerate(vs):
                tpl = t.replace(name, "{n}") if isinstance(t, str) else t
                if tpl in POOLS:
                    pool = POOLS[tpl]
                    new = pool[(names.index(name) + i) % len(pool)].replace("{n}", name)
                    if new != t: changed += 1
                    out.append(new)
                else:
                    out.append(t)
            out = list(dict.fromkeys(out))
            o["say"] = out if isinstance(o["say"], list) else out[0]
        for k, v in o.items():
            if k != "say": fix(v, name)
    elif isinstance(o, list):
        for v in o: fix(v, name)
for x in d["dialogues"]:
    name = next((c["outlaw"] for c in (x.get("lw_if") or []) if isinstance(c.get("outlaw"), str)), None)
    if name: fix(x, name)
json.dump(d, open(p, "w"), indent=1, ensure_ascii=False); open(p, "a").write("\n")
print("rewrote", changed, "lines for", len(names), "outlaws")
