"""Builds the public release of The Wasteland Remembers (Meitou): ONE mod folder (all the data in LivingWasteland.mod, the code mod's
DLL, manifest and codes beside it), ready to copy into Kenshi's mods folder or to upload as one Workshop item, plus a README and a log
saver for bug reports, in one zip.

  python make_release.py GAME_DATA_DIR [--out DIR]

* data built with --release (the Play Test starts and other test-only records are left out);
* the code mod ships its DLL, manifest and codes only (its C# sources stay in the repository);
* each mod's .info gets a readable title;
* the build id (BUILD_ID) is in the zip's name and in lw_codes.json, and the log says it at start.
"""
import argparse
import os
import re
import shutil
import subprocess
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
MODS = ["LivingWasteland"]
TITLES = {"LivingWasteland": "[Meitou] The Wasteland Remembers"}
CODE_KEEP = (".dll", ".mod", ".json", ".info")

README = """THE WASTELAND REMEMBERS  (early version {build})
A Meitou mod for Kenshi. Early release: things will be rough in places, and feedback and bug reports are very welcome.

REQUIREMENTS
- MeitouClient 0.17.3 to 0.22 (0.21 or later recommended: stolen goods, the right-click uses of keepsakes and warrants and the test features need
  it; Atlas faces and squad roles need 0.19). It does not run on plain Kenshi.

INSTALL
1. Copy the "LivingWasteland" folder in "mods" into your Kenshi mods folder
   (for Steam: C:\\Program Files (x86)\\Steam\\steamapps\\common\\Kenshi\\mods). Subscribing on the Workshop does this for you.
2. In the Meitou launcher's Mods page, tick LivingWasteland and keep it BELOW your other mods.
3. Nearly every feature can be switched off in game (U > Settings), town heat and outlaw contracts included.
4. A new game is recommended; in an existing save your squad record starts from zero.

IN GAME
- Press U for the squad record (or the button at the top right). Settings are in its Settings tab, or Esc > Settings > Mods.
- "Added talk amount" and "Rumour and gossip amount" set how much of the new talk you hear (both 40% by default).

REPORTING A BUG
1. Quit Meitou (before starting it again: the log is kept only until the next start).
2. Double-click "Save logs for a bug report.bat". It puts one zip on your Desktop.
   It holds Meitou's log and settings (the paths in them include your Windows user name).
3. Send that zip with a few words on what happened, and a screenshot if you have one.

KNOWN IN THIS VERSION
- The trophy neck slot has no label yet (Meitou's slot API has no label field).
- Still being tested and off by default (U > Settings, search "test"): moving outlaw camps, harassment raids, marks on the world map,
  factions' change of heart in History, outpost history and couriers, town handovers, prices by reputation, recruiting beaten
  rivals, keepsake stories, prisoner cases, deals and recovery parties, Western Hive envoys, and visible neck charms.
- A squad running at very high Athletics can outpace bounty crews: they walk.
- To see which build you have: press F10 and pick "Wasteland Remembers: squad record".

CREDITS
- The Shek charm and Holy Nation beads worn at the neck (setting "Visible neck charms") are 3D models by Mazal Tofuson.
"""

LOG_BAT = r"""@echo off
rem Collects the Meitou game's logs, TWR's settings and its play-test logs (last 14 days) into ONE zip on your Desktop, for a bug report.
rem Run it after you quit Meitou.
setlocal
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmm"') do set STAMP=%%i
set OUT=%USERPROFILE%\Desktop\WastelandRemembers_logs_%STAMP%
mkdir "%OUT%" 2>nul
powershell -NoProfile -Command "Get-ChildItem \"$env:LOCALAPPDATA\Meitou\versions\" -Directory -ErrorAction SilentlyContinue | ForEach-Object { $l = Join-Path $_.FullName 'log.txt'; if (Test-Path $l) { Copy-Item $l (Join-Path '%OUT%' ('log_' + $_.Name + '.txt')) } }"
powershell -NoProfile -Command "$d = [Environment]::GetFolderPath('MyDocuments'); foreach ($f in @((Join-Path $d 'The Wasteland Remembers'), (Join-Path $env:LOCALAPPDATA 'The Wasteland Remembers'))) { Get-ChildItem $f -Filter 'TWR-playtest-*.txt' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-14) } | Copy-Item -Destination '%OUT%' }"
copy "%LOCALAPPDATA%\Meitou\mods\*.settings.txt" "%OUT%\" >nul 2>&1
copy "%LOCALAPPDATA%\Meitou\meitou.user.json" "%OUT%\" >nul 2>&1
copy "%LOCALAPPDATA%\Meitou\launcher.json" "%OUT%\" >nul 2>&1
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%OUT%.zip' -Force"
rmdir /s /q "%OUT%"
echo.
echo Saved: %OUT%.zip
echo Send that one file with your bug report.
pause
"""


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("data")
    ap.add_argument("--out", default=os.path.join(HERE, "release"))
    a = ap.parse_args(argv)
    build = open(os.path.join(HERE, "BUILD_ID"), encoding="utf-8").read().strip()
    stage = os.path.join(a.out, "stage")
    shutil.rmtree(stage, ignore_errors=True)
    built = os.path.join(stage, "built")
    r = subprocess.run([sys.executable, os.path.join(HERE, "build_mod.py"), a.data, "--target", "meitou", "--release", "--single", "--out", built])
    if r.returncode:
        sys.exit("build failed")
    pack = os.path.join(stage, "pack")
    for m in MODS:
        src, dst = os.path.join(built, m), os.path.join(pack, "mods", m)
        os.makedirs(dst)
        for f in os.listdir(src):
            if f.endswith(CODE_KEEP):
                shutil.copy2(os.path.join(src, f), dst)
        info = os.path.join(dst, f"_{m}.info")
        if os.path.exists(info):
            t = open(info, encoding="utf-8").read().replace(f"<title>{m}</title>", f"<title>{TITLES[m]}</title>")
            # the Workshop item this mod is uploaded to (WORKSHOP_ID), so FCS updates it instead of making a new item
            wid = os.path.join(HERE, "WORKSHOP_ID")
            if os.path.exists(wid):
                t = re.sub(r"<id>\d+</id>", f"<id>{open(wid, encoding='utf-8').read().strip()}</id>", t, count=1)
            open(info, "w", encoding="utf-8").write(t)
    # the neck charm models (assets/neck_charms, by Mazal Tofuson): game files only, beside the helper (Meitou finds a bone mesh by bare
    # file name in the mods' folders); shown only with "Visible neck charms" on
    charms = os.path.join(HERE, "assets", "neck_charms")
    for f in sorted(os.listdir(charms)) if os.path.isdir(charms) else []:      # the public repository has no models
        if f.endswith((".mesh", ".dds", ".material", ".json")):
            shutil.copy2(os.path.join(charms, f), os.path.join(pack, "mods", MODS[0]))
    with open(os.path.join(pack, "README.txt"), "w", encoding="utf-8", newline="\r\n") as fh:
        fh.write(README.format(build=build))
    with open(os.path.join(pack, "Save logs for a bug report.bat"), "w", encoding="utf-8", newline="\r\n") as fh:
        fh.write(LOG_BAT)
    shutil.copy2(os.path.join(HERE, "The_Wasteland_Remembers_features.txt"), os.path.join(pack, "Features.txt"))
    shutil.copy2(os.path.join(HERE, "MODDER_API.md"), os.path.join(pack, "For modders - API.md"))
    zpath = os.path.join(a.out, f"TheWastelandRemembers_{build}.zip")
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for root, _, files in os.walk(pack):
            for f in files:
                p = os.path.join(root, f)
                z.write(p, os.path.relpath(p, pack))
    shutil.rmtree(stage)
    print(zpath)


if __name__ == "__main__":
    main(sys.argv[1:])
