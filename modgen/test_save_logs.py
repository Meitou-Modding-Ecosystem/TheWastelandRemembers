"""Exercise the Linux/macOS bug-report collector with fake launcher and Steam folders."""
import os
import subprocess
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import patch
from zipfile import ZipFile

import make_release

SCRIPT = Path(__file__).with_name("save-logs.sh")


class SaveLogsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="twr log test ")
        self.addCleanup(self.temp.cleanup)
        self.home = Path(self.temp.name)
        self.data = self.home / "XDG share"
        self.env = {
            "PATH": os.environ["PATH"], "HOME": str(self.home), "XDG_DATA_HOME": str(self.data),
            "XDG_CONFIG_HOME": str(self.home / "Config"), "TMPDIR": self.temp.name,
        }

    @staticmethod
    def write(path, contents):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(contents, encoding="utf-8")
        return path

    def run_script(self, *folders):
        return subprocess.run(["bash", SCRIPT, *folders], env=self.env, capture_output=True, text=True, check=False)

    def test_native_and_proton_sources_keep_distinct_logs(self):
        desktop = self.home / "Desktop"
        desktop.mkdir()
        local = self.data / "Meitou"
        self.write(local / "versions/v0.23.2/log.txt", "native Linux")
        self.write(local / "mods/LivingWasteland.settings.txt", "factions=on")
        self.write(local / "launcher.json", "{}")
        self.write(local / "meitou.user.json", "{}")
        self.write(local / "versions/v0.23.2/saves/game.dat", "skip")
        (local / "mods/private.settings.txt").symlink_to(local / "versions/v0.23.2/saves/game.dat")
        self.write(self.home / "Library/Application Support/Meitou/versions/v0.23.2/log.txt", "native macOS")
        self.write(self.home / "Documents/The Wasteland Remembers/TWR-playtest-auto-1.txt", "recent")
        stale = self.write(self.home / "Documents/The Wasteland Remembers/TWR-playtest-old.txt", "old")
        past = time.time() - 16 * 86400
        os.utime(stale, (past, past))

        library = self.home / "Secondary library"
        proton_user = library / "steamapps/compatdata/233860/pfx/drive_c/users/steamuser"
        self.write(proton_user / "AppData/Local/Meitou/versions/v0.23.2/log.txt", "Proton")
        self.write(proton_user / "Documents/The Wasteland Remembers/TWR-playtest-auto-1.txt", "Proton report")
        self.write(self.data / "Steam/steamapps/libraryfolders.vdf", f'"libraryfolders"\n{{\n"path" "{library}"\n}}\n')
        steam = self.home / ".steam"
        steam.mkdir()
        (steam / "steam").symlink_to(self.data / "Steam", target_is_directory=True)
        extra = self.home / "Portable client"
        self.write(extra / "log.txt", "explicit install")

        result = self.run_script(extra)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(list(desktop.glob("*.zip")), [Path(result.stdout.strip().removeprefix("Saved "))])
        with ZipFile(next(desktop.glob("*.zip"))) as archive:
            files = {name: archive.read(name).decode() for name in archive.namelist() if not name.endswith("/")}
        self.assertCountEqual(files.values(), [
            "native Linux", "factions=on", "{}", "{}", "native macOS", "recent", "Proton", "Proton report",
            "explicit install",
        ])
        self.assertTrue(all(not name.startswith("/") for name in files))

    def test_no_sources_leaves_no_empty_zip(self):
        result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("No Meitou logs", result.stderr)
        self.assertFalse(list(self.home.rglob("*.zip")))

    def test_release_packaging_contains_both_collectors(self):
        output = self.home / "Release"
        source = self.home / "Fake checkout"
        self.write(source / "BUILD_ID", "fixture")
        self.write(source / "The_Wasteland_Remembers_features.txt", "fixture")
        self.write(source / "MODDER_API.md", "fixture")
        (source / SCRIPT.name).write_bytes(SCRIPT.read_bytes())

        def fake_build(args):
            files = Path(args[-1]) / "LivingWasteland"
            self.write(files / "LivingWasteland.mod", "fixture")
            return subprocess.CompletedProcess(args, 0)

        with patch.object(make_release, "HERE", str(source)), patch.object(make_release.subprocess, "run", side_effect=fake_build):
            make_release.main(["fake-data", "--out", str(output)])

        with ZipFile(next(output.glob("*.zip"))) as archive:
            self.assertEqual(archive.read("save-logs.sh"), SCRIPT.read_bytes())
            self.assertIn("Save logs for a bug report.bat", archive.namelist())
            self.assertIn(b"bash save-logs.sh", archive.read("README.txt"))

    def test_home_output_and_a_nondefault_proton_prefix(self):
        compat = self.home / "Selected prefix"
        self.env["STEAM_COMPAT_DATA_PATH"] = str(compat)
        self.write(compat / "pfx/drive_c/users/steamuser/AppData/Local/Meitou/log.txt", "selected prefix")
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(list(self.home.glob("*.zip"))), 1)


if __name__ == "__main__":
    unittest.main()
