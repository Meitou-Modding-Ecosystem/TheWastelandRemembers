"""Put the stand-alone Squad Record mod together (Meitou only): python make_squad_record.py [--out release] [--zip]

Squad Record is the Living Wasteland helper's squad record window on its own: per-character stats and titles, no quests, heat or dialogue. It is
built from the helper's shared C# files with SQUAD_RECORD_ONLY (meitou/SquadRecord/SquadRecord.csproj). This copies the built DLL (or the
committed one) and its manifest, writes titles.json from the titles in content/notoriety.json, a header-only SquadRecord.mod so the launcher
lists it, and a README. Nothing from the game is copied. Do not enable it next to LivingWastelandMeitou (it turns itself off if you do).
"""
import argparse
import json
import os
import shutil
import zipfile

import builder as B

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "meitou", "SquadRecord")

README = """Squad Record (Meitou code mod) v{version}, early test version: not yet played in a game.

A record of what each of your people has done, and the titles they earn from it.

Open it: the U key (change it or turn it off in the settings), the "Squad record" button near the top right of the screen, or F10 > "Squad record" (which also writes it to log.txt).
Record page: kills (and of which factions), knockouts, leaders and named characters killed, limbs cut off and lost, damage dealt and taken,
times downed, fights (won, last one standing), distance walked (and ridden or sailed, with Meitou Transport), food eaten, items crafted (weapons, armour, crossbows, robotics, other), and
hours spent fighting, on first aid, working, building, hauling, looting and sneak-attacking, and the downed finished off.
Kills by mercenaries you hired count for the squad. A renamed character keeps their record; two people with the same name get one each.
History page: your squad's story, newest first: first kills, titles earned, named kills, lost limbs, big fights and lost ones.
Fallen page: squadmates who died: when, where, who killed them, what they did and how long they served, and who avenged them.
Titles page: all {titles} titles, how each is earned and who holds it. Member titles go to the one who earned them; squad titles count the
whole squad and belong to everyone in it, recruits included. Some are named kills: whoever kills Holy Lord Phoenix becomes Narko.

Settings: Esc > Settings > Mods > Squad Record (title messages, button position, hotkey, sharing moments with Recap, logging).
With Recap 1.2 or later installed, titles, last stands and avenged squadmates also appear in its "Previously on your adventure" recap.
New in 0.2.16: notices wait until a skill or a standing stops changing, so a fight shows one notice, not dozens.
New in 0.2.15: plainer window text; epitaphs state facts (who avenged them, their title, kills and days with the squad).
New in 0.2.14: a cleaner Record page: a header per person, labelled lines, only what has happened.
New in 0.2.13: days in the History and the Fallen match the game's own day count (they were one ahead).
New in 0.2.12: "Save as text" writes to Documents\\Squad Record; works on Meitou 0.19.
New in 0.2.11: the Settings tab is highlighted when open (it was not), and Save as text sits at the bottom of the window.
New in 0.2.10: the packaged DLL is the current build again (0.2.6 to 0.2.9 zips held an older one).
New in 0.2.9: floating notices when someone gains a skill level, and when a faction's standing with you goes up (green) or down (red); in the settings.
New in 0.2.8: the Settings tab has a search box (click it, then type) and shows what a setting does when you hover over it or click its name.
New in 0.2.7: a title outgrown (the Hard Hand once you are Bonepicker) is marked "(outgrown)" on the Titles page; the higher one is what counts.
New in 0.2.6: the U key opens and closes the window (U, N, O or off in the settings).
New in 0.2.5: an epitaph for every fallen squadmate; "Save as text" writes your squad's whole story to Documents\\Living Wasteland.
New in 0.2.4: with Guild & Quests or Raid Commander installed, guild ranks and seals and raids on your base go into the History
(and Recap), with squad titles for them.
New in 0.2.3: a Settings tab in the window (changes apply at once and are kept).
New in 0.2.2: two limbs cut by one blow (as the Brutality mod does) both count; a limb already lost does not.
New in 0.2.1: with Meitou Transport installed, rides and voyages are counted on their own (not as walking), with the Rider and
Boatman titles.
New in 0.2.0: the Fallen page, avenging, the Headsman and Avenger titles, the downed finished off, hired mercenaries' kills, records that
survive renames. Saves from 0.1.0 carry over.
Install:
  1. Open the zip and drag the SquadRecord folder into Kenshi's mods folder, usually
     C:\\Program Files (x86)\\Steam\\steamapps\\common\\Kenshi\\mods
     (it must end up as mods\\SquadRecord\\SquadRecord.mod, not inside another folder).
  2. Open the Meitou launcher, tick Squad Record in the Mods list, press Play. If it asks whether to trust the mod's code, say yes.
  The record starts from zero in a game started before installing it; it is kept in your saves.
Works with Meitou 0.17.3 to 0.18.x. If something goes wrong the record switches itself off and writes the error to log.txt
(paste %LocalAppData%\\Meitou into File Explorer's address bar to find it); please send that file. Known limits: kills from bleeding out are credited to the last of your people who hit the victim,
within a game day; healing counts time spent on first aid, not health restored.
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(HERE, "release"))
    ap.add_argument("--zip", action="store_true")
    a = ap.parse_args()
    manifest = json.load(open(os.path.join(SRC, "meitou.json"), encoding="utf-8"))
    dll = os.path.join(SRC, "SquadRecord.dll")             # the shipped build (built against the oldest supported Meitou); never bin/, which
    if not os.path.isfile(dll):                              # can hold an older local build
        raise SystemExit("build it first: dotnet build -c Release -p:MeitouDir=<Meitou folder> in meitou/SquadRecord")
    titles = json.load(open(os.path.join(HERE, "content", "notoriety.json"), encoding="utf-8"))["titles"]
    out_titles = [{"id": t["id"], "name": t["name"], "scope": t["scope"], "stat": t["stat"], "faction": t.get("faction") or t.get("kind"),
                   "targets": t.get("targets") or ([t["target"]] if t.get("target") else None), "at": int(t["at"]),
                   "legendary": bool(t.get("legendary"))} for t in titles
                  if t["stat"] not in ("alms",)]                # Living Wasteland only: nothing in Squad Record can earn them

    folder = os.path.join(a.out, "SquadRecord")
    shutil.rmtree(folder, ignore_errors=True)
    os.makedirs(folder)
    shutil.copy(dll, folder)
    shutil.copy(os.path.join(SRC, "meitou.json"), folder)
    with open(os.path.join(folder, "titles.json"), "w", encoding="utf-8") as fh:
        json.dump(out_titles, fh, indent=1, ensure_ascii=False)
    with open(os.path.join(folder, "README.txt"), "w", encoding="utf-8") as fh:
        fh.write(README.format(version=manifest["version"], titles=len(out_titles)))
    B.ModBuilder("SquadRecord", depends=["gamedata.base"],
                 description="Meitou code mod: a per-character record of kills, injuries, work and titles.").write(a.out)
    print(f"wrote {folder} ({len(out_titles)} titles)")
    if a.zip:
        path = os.path.join(a.out, f"SquadRecord-{manifest['version']}.zip")
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
            for name in sorted(os.listdir(folder)):
                z.write(os.path.join(folder, name), f"SquadRecord/{name}")
        print("zipped", path)


if __name__ == "__main__":
    main()
