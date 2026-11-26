from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
from unittest import TestCase, mock
from zipfile import ZipFile

import make_release


class SteamReleaseTests(TestCase):
    def setUp(self):
        temp = TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.out = self.root / "two words"
        (self.root / "BUILD_ID").write_text("20261125bp\n", encoding="utf-8")
        (self.root / "WORKSHOP_ID").write_text("3839368189\n", encoding="utf-8")
        (self.root / "CHANGELOG.md").write_text(
            "# Changes\n\n## Next build (not built yet)\n- Unreleased work.\n\n"
            "## Build 20261125bp (beta)\n- **A new feature.** The squad remembers it.\n"
            "  A continuation.\n- \x60Setting\x60 also works.\n\n## Build 20261125bo\n- Old note.\n",
            encoding="utf-8",
        )
        self.dll = self.root / "meitou/LivingWastelandMeitou/LivingWastelandMeitou.dll"
        self.dll.parent.mkdir(parents=True)
        self.dll.write_bytes(b"fixture")
        for name in ("save-logs.sh", "The_Wasteland_Remembers_features.txt", "MODDER_API.md"):
            (self.root / name).write_text("", encoding="utf-8")
        self.here = mock.patch.object(make_release, "HERE", str(self.root))
        self.here.start()
        self.addCleanup(self.here.stop)

    def fake_compile(self, args):
        self.assertIn("build_mod.py", args[1])
        fixture = Path(args[-1]) / "LivingWasteland"
        fixture.mkdir(parents=True)
        (fixture / self.dll.name).write_bytes(self.dll.read_bytes())
        (fixture / "_LivingWasteland.info").write_text(
            "<title>LivingWasteland</title><id>0</id>", encoding="utf-8")
        return SimpleNamespace(returncode=0)

    def test_stale_source_dll_is_rejected_before_the_builder_can_run(self):
        with mock.patch.object(make_release, "read_build_stamp", create=True, return_value="20261125bm"):
            with mock.patch.object(make_release.subprocess, "run", side_effect=self.fake_compile) as compiler:
                with self.assertRaisesRegex(SystemExit, "20261125bm.*20261125bp"):
                    make_release.main(["fake-data", "--out", str(self.out)])
                compiler.assert_not_called()
        self.assertFalse(self.out.exists())

    def test_source_check_does_not_allow_a_stale_copy_from_the_builder(self):
        with mock.patch.object(make_release, "read_build_stamp", side_effect=["20261125bp", "20261125bm"]):
            with mock.patch.object(make_release.subprocess, "run", side_effect=self.fake_compile):
                with self.assertRaisesRegex(SystemExit, "20261125bm.*20261125bp"):
                    make_release.main(["fake-data", "--out", str(self.out)])
        self.assertEqual([], list(self.out.glob("*.zip")))

    def test_the_upload_folder_can_name_the_uploading_pc(self):
        with mock.patch.dict(make_release.os.environ, {"TWR_STEAM_FOLDER": "C:\\TWR\\"}):
            make_release.workshop_files(self.out / "stage", "3839368189", "Note.", "Full.")
        vdf = (self.out / "stage" / "twr.vdf").read_text(encoding="utf-8")
        self.assertIn('"contentfolder" "C:\\TWR\\mods\\LivingWasteland"', vdf)

    def test_a_plain_build_heading_keeps_the_first_bullet(self):
        note, _ = make_release.workshop_notes("## Build 20261125bp\n- First.\n- Second.\n", "20261125bp")
        self.assertEqual("First.", note)

    def test_release_writes_the_upload_files_next_to_a_zip_with_the_same_build(self):
        with mock.patch.object(make_release, "read_build_stamp", create=True, return_value="20261125bp"):
            with mock.patch.object(make_release.subprocess, "run", side_effect=self.fake_compile):
                make_release.main(["fake-data", "--out", str(self.out)])

        stem = "TheWastelandRemembers_20261125bp"
        stage = self.out / stem
        vdf = (stage / "twr.vdf").read_text(encoding="utf-8")
        self.assertIn('"appid" "233860"', vdf)
        self.assertIn('"publishedfileid" "3839368189"', vdf)
        self.assertIn(f'"contentfolder" "{(stage / "mods/LivingWasteland").as_posix()}"', vdf)
        self.assertIn('"changenote" "A new feature. The squad remembers it. A continuation."', vdf)
        note = (stage / "change-note.bbcode.txt").read_text(encoding="utf-8")
        self.assertIn("[*][b]A new feature.[/b] The squad remembers it. A continuation.", note)
        self.assertIn("save-logs.sh", note)
        self.assertIn("steamapps/workshop/content/233860/3839368189", note)
        self.assertNotIn("Unreleased work", note)
        self.assertNotIn("Old note", note)
        with ZipFile(self.out / (stem + ".zip")) as archive:
            self.assertEqual(archive.read("mods/LivingWasteland/" + self.dll.name), b"fixture")
            self.assertIn(b"<id>3839368189</id>", archive.read("mods/LivingWasteland/_LivingWasteland.info"))
