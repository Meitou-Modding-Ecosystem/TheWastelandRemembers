"""Compile content packs into the mod file and check the result.

  python build_mod.py DATA_DIR [PACK.json ...] [--out build] [--name LivingWasteland]

DATA_DIR holds gamedata.base and the mod files (read-only; your mods.cfg sets the load order). With no packs given,
every content/*.json (except names starting with _) is compiled. The output is <out>/<name>/<name>.mod. Exit status 1 if anything must be fixed.
"""
import argparse
import glob
import os
import sys

import builder as B
import content
import gamedb
import validate

HERE = os.path.dirname(os.path.abspath(__file__))


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("data_dir")
    ap.add_argument("packs", nargs="*")
    ap.add_argument("--out", default=os.path.join(HERE, "build"))
    ap.add_argument("--name", default="LivingWasteland")
    ap.add_argument("--author", default="")
    ap.add_argument("--install", action="store_true", help="copy the built mod folders into <Kenshi>/mods (data_dir must be your Kenshi folder)")
    ap.add_argument("--skip", action="append", default=[], help="leave out a mod file by name, e.g. LivingWastelandHeat")
    ap.add_argument("--target", choices=("kenshi", "meitou"), default="kenshi",
                    help="kenshi (default): RE_Kenshi + BFrizz's Extra Extensions. meitou: the Meitou engine, quest state in the LivingWastelandMeitou code mod (meitou_target.py)")
    ap.add_argument("--release", action="store_true", help="leave out play-test helpers (clones marked test_only: the Play Test Squad starts)")
    ap.add_argument("--single", action="store_true", help="meitou: everything in ONE mod folder (LivingWasteland: all the data in one .mod, the code mod's DLL, manifest and codes beside it), for a single Workshop item")
    ap.add_argument("--with-mods", action="store_true", help="also resolve names from your other mods (the result then depends on them)")
    a = ap.parse_args(argv)
    packs = a.packs or sorted(p for p in glob.glob(os.path.join(HERE, "content", "*.json")) if not os.path.basename(p).startswith("_"))
    if not packs:
        print("no content packs found")
        return 1
    db = gamedb.GameDB(a.data_dir, masters_only=not a.with_mods)
    builders = {}

    def builder_for(modname):
        if modname not in builders:
            title = a.name if modname == a.name else modname
            builders[modname] = B.ModBuilder(title, author=a.author, depends=["gamedata.base", "Newwworld.mod", "Dialogue.mod", "rebirth.mod"],
                                             description="Faction-aware reactions, rumours and consequences for the world of Kenshi.")
        return builders[modname]

    comp = content.Compiler(builder_for(a.name), db)
    comp.base_builder = comp.b
    comp.target = a.target
    comp.release = a.release
    comp.all_builders = builders.values()     # live view: squads made by any of our mods (leader_packages)
    ok = True
    for p in packs:
        pack = content.load_pack(p)
        comp.b = builder_for(a.name if a.single else pack.get("mod", a.name))
        before = dict(comp.stats)
        ok &= comp.compile(pack)
        delta = {k: comp.stats[k] - before.get(k, 0) for k in comp.stats}
        print(f"{os.path.basename(p)} -> {comp.b.mod_name}: {delta}")
    for w in comp.warnings:
        print("warning:", w)
    for e in comp.errors:
        print("ERROR:", e)
    meitou_manifest = None
    if a.target == "meitou":
        import meitou_target
        meitou_manifest = meitou_target.convert(list(builders.values()), getattr(comp, "recent_marks", []),
                                                 getattr(comp, "lw_marks", []), getattr(comp, "titles", []),
                                                 getattr(comp, "camps", []), getattr(comp, "lw_effects", []),
                                                 getattr(comp, "temperaments", None), getattr(comp, "hunters", None), getattr(comp, "trophies", None),
                                                 getattr(comp, "var_moments", None), getattr(comp, "atlas", None),
                                                 getattr(comp, "raids", None), getattr(comp, "title_views", None))
        left = meitou_target.leftover_bfrizz(list(builders.values()))
        for l in left:
            print("ERROR: BFrizz-only data left in the Meitou build:", l)
            comp.errors.append(l)
        print("meitou target:", meitou_manifest["stats"], f"({len(meitou_manifest['variables'])} variables, {len(meitou_manifest['takeItems'])} take-item codes)")
    # a mod that points at records another of our mods created must depend on it
    files = {m.file for m in builders.values()}
    problems = []
    for m in builders.values():
        for r in m.records:
            for entries in r["ref"].values():
                for e in entries:
                    owner = e[0].split("-", 1)[-1]
                    if owner in files and owner != m.file and owner not in m.depends:
                        m.depends.append(owner)
    for m in builders.values():                 # two of our mods depending on each other cannot both load
        for d in m.depends:
            other = next((x for x in builders.values() if x.file == d), None)
            if other and m.file in other.depends:
                comp.errors.append(f"dependency cycle: {m.file} and {other.file} depend on each other (move the pack that links them into the later mod)")
                ok = False
    for m in list(builders.values()):
        if not m.records:                       # nothing was written into this mod, so there is no file to make
            del builders[m.mod_name]
        elif m.mod_name in a.skip:
            del builders[m.mod_name]
    for m in builders.values():
        path = m.write(a.out, db)
        problems += validate.check(path, db, extra=[b2.parsed(db)["records"] for b2 in builders.values() if b2 is not m], mod_codes=meitou_manifest is not None)
        print(f"wrote {path} ({os.path.getsize(path) // 1024} KB, depends on {', '.join(m.depends)})")
    for pr in problems:
        print("INVALID:", pr)
    if meitou_manifest is not None:
        import shutil
        code_dir = os.path.join(a.out, "LivingWastelandMeitou")
        template = os.path.join(HERE, "meitou", "LivingWastelandMeitou")
        shutil.rmtree(code_dir, ignore_errors=True)
        if a.single:
            # the code beside the data in the one folder: DLL, manifest (its id stays LivingWastelandMeitou: settings and save data keep)
            os.makedirs(code_dir)
            for f in os.listdir(template):
                if f.endswith((".dll", ".json")):
                    shutil.copy2(os.path.join(template, f), code_dir)
        else:
            shutil.copytree(template, code_dir, ignore=shutil.ignore_patterns("bin", "obj"))
            B.ModBuilder("LivingWastelandMeitou", author=a.author, depends=["gamedata.base"],
                         description="C# code mod for the Meitou engine: quest state for Living Wasteland.").write(a.out)
        meitou_target.write_manifest(meitou_manifest, code_dir)
        print(f"wrote the Meitou code mod folder {code_dir}")
        import meitou_check
        print("\n---- Meitou's view of this build (meitou_check.py) ----")
        if meitou_check.check(a.out):
            problems.append("meitou_check: quest-breaking problems (see above)")
        if a.single:
            # the checks read the code folder as built; then its files join the data folder and the code folder goes
            one = os.path.join(a.out, a.name)
            for f in os.listdir(code_dir):
                shutil.copy2(os.path.join(code_dir, f), one)
            shutil.rmtree(code_dir)
            print(f"one folder: {one} ({', '.join(sorted(os.listdir(one)))})")
    if a.install and ok and not problems:
        import shutil
        mods_dir = os.path.join(a.data_dir, "mods")
        if not os.path.isdir(mods_dir):
            print(f"cannot install: {mods_dir} does not exist (give your Kenshi folder as DATA_DIR)")
        else:
            for m in builders.values():
                dest = os.path.join(mods_dir, m.mod_name)
                if os.path.isdir(dest):
                    shutil.rmtree(dest)
                shutil.copytree(os.path.join(a.out, m.mod_name), dest)
                print(f"installed {dest}")
            if meitou_manifest is not None:
                dest = os.path.join(mods_dir, "LivingWastelandMeitou")
                if os.path.isdir(dest):
                    shutil.rmtree(dest)
                shutil.copytree(os.path.join(a.out, "LivingWastelandMeitou"), dest)
                has_dll = os.path.isfile(os.path.join(dest, "LivingWastelandMeitou.dll"))
                print(f"installed {dest}" + ("" if has_dll else " (WARNING: LivingWastelandMeitou.dll is missing: quests will not work)"))
            print("Start Kenshi, tick the new mods in the launcher (they need to load AFTER your other mods) and click OK.")
    print(f"\n{comp.stats}")
    print(f"talk dialogues with a random chance made always-eligible: {getattr(comp, 'forced_talk', 0)}")
    print(f"offer/talk attachments skipped because the package belongs to shop or bar staff: {getattr(comp, 'trade_skipped', 0)} (dialogues that had only trade packages and kept them: {getattr(comp, 'trade_fallback', 0)})")
    return 0 if ok and not problems else 1


if __name__ == "__main__":
    sys.exit(main())
