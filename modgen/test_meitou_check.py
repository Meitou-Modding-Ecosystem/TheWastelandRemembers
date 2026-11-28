import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

import meitou_check


class HandlersTest(unittest.TestCase):
    def test_take_item_codes_count_as_conditions_too(self):
        # meitou_target adds a quantity gate (a condition on the take code) to each line that takes items; the helper answers it
        # (LwMod.RegisterTakes), so the check must not call it unhandled. It did, and failed every release build.
        with tempfile.TemporaryDirectory() as d:
            os.makedirs(os.path.join(d, "LivingWastelandMeitou"))
            with open(os.path.join(d, "LivingWastelandMeitou", "lw_codes.json"), "w", encoding="utf-8") as f:
                json.dump({"variables": [], "takeItems": [{"code": 18000, "item": "x"}]}, f)
            _, conds, effects = meitou_check.handlers(d)
        self.assertIn(18000, conds)
        self.assertIn(18000, effects)


class CheckMatrixTest(unittest.TestCase):
    def test_default_check_runs_all_supported_clients_and_compares_the_latest_references(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            meitou = root / "meitou"
            (meitou / "RefCheck").mkdir(parents=True)
            shutil.copyfile(Path(__file__).parent / "meitou/codex_check.sh", meitou / "codex_check.sh")
            for file, code in {
                "release_check.sh": 'printf "client %s\\n" "$@"',
                "RefCheck/check_versions.sh": 'printf "reference %s\\n" "$@"',
                "dotnet": 'out="${@: -1}"; mkdir -p "$out"; touch "$out/${out##*/}.dll"',
            }.items():
                path = meitou / file
                path.write_text(f"#!/usr/bin/env bash\n{code}\n")
                path.chmod(0o755)
            for name in ("LivingWastelandMeitou", "SquadRecord", "cache/0.17.3"):
                (meitou / name).mkdir(parents=True)
            (meitou / "cache/0.17.3/MeitouClient.dll").touch()
            for name in ("meitou_target", "meitou_check", "make_release", "save_logs"):
                (root / f"test_{name}.py").touch()
            (root / "test_meitou_target.py").write_text(
                "from unittest import TestCase\nclass Fixture(TestCase):\n    def test_compiler(self): pass\n")
            for name in ("detrope.py", "contractions.py"):
                (root / name).touch()
            (root / "content").mkdir()
            (root / "content/demo.json").write_text("{}")
            env = dict(os.environ, CACHE=str(meitou / "cache"), DOTNET=str(meitou / "dotnet"),
                       TWR_REQUIRE_ROLLBACK="0", TWR_PREVIOUS_STEAM_DLL="", TMPDIR=directory)
            result = subprocess.run(["bash", str(meitou / "codex_check.sh")], env=env,
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual([line for line in result.stdout.splitlines() if line.startswith("client ")],
                             ["client v0.24.1", "client v0.24.0", "client v0.23.2", "client v0.17.3"])
            self.assertEqual([line for line in result.stdout.splitlines() if line.startswith("reference ")],
                             ["reference 0.24.0", "reference 0.23.2"])


if __name__ == "__main__":
    unittest.main()
