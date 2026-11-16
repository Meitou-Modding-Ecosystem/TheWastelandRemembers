"""Build every mod file and zip ONLY our own files (the .mod and .info per mod, plus a short README) for sharing.

  python package_release.py "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Kenshi" [--out release]

Reads your game folder only to look names up while building; no game data is copied into the zip.
"""
import argparse
import os
import shutil
import subprocess
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))

README = """Living Wasteland (Kenshi dialogue mods)

More voice for the world of Kenshi, written to feel like it was always in the base game: rumours, boss and mini-boss
reactions, horror regions, named bounty outlaws with contracts, a small cannibal splinter faction (the Gate Folk),
faction heat, squad chatter and quest chains. Pure data: no background program.

Copy each folder into Kenshi\\mods, tick them in the launcher AFTER your other mods, in this order:
  1. LivingWasteland
  2. LivingWastelandCannibals
  3. LivingWastelandOutlaws
  4. LivingWastelandHeat   (needs RE_Kenshi and the BFrizz Extra Extensions workshop mod)
Start a new game or import your save (new world states and spawns need it).
Each mod file also needs the official Kenshi data (gamedata.base, Newwworld, Dialogue, rebirth).
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("kenshi")
    ap.add_argument("--out", default=os.path.join(HERE, "release"))
    a = ap.parse_args()
    build = os.path.join(HERE, "build")
    shutil.rmtree(build, ignore_errors=True)
    r = subprocess.run([sys.executable, os.path.join(HERE, "build_mod.py"), a.kenshi, "--out", build])
    if r.returncode:
        sys.exit("build failed: nothing packaged")
    os.makedirs(a.out, exist_ok=True)
    zp = os.path.join(a.out, "LivingWasteland.zip")
    with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED) as z:
        for mod in sorted(os.listdir(build)):
            folder = os.path.join(build, mod)
            if not os.path.isdir(folder):
                continue
            for fn in os.listdir(folder):
                if fn.endswith((".mod", ".info")):
                    z.write(os.path.join(folder, fn), f"{mod}/{fn}")
        z.writestr("README.txt", README)
    print("wrote", zp)


if __name__ == "__main__":
    main()
