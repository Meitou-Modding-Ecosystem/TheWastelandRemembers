"""Synthetic record check for the Meitou dialogue converter (no vanilla files needed)."""
import sys
import types
import unittest

# builder writes through fcs_writer only when it exports a file. The snapshot lacks that tool.
sys.modules.setdefault("fcs_writer", types.ModuleType("fcs_writer"))

import builder
import content
import meitou_target


class GivesAndTakes(unittest.TestCase):
    def test_multi_item_handover_checks_quantity_on_the_consuming_line(self):
        b = builder.ModBuilder("fixture")
        item = "meat-test"
        line_id = b.line("Here are the rations.", refs={"take item": [(item, 3)]})
        b.dialogue("rations_report", [(line_id, 0)], refs={"target has item": [(item, 1)]})

        manifest = meitou_target.convert([b])
        records = {record["sid"]: record for record in b.records}
        line = records[line_id]
        code = manifest["takeItems"][0]["code"]
        self.assertEqual(item, manifest["takeItems"][0]["item"])
        self.assertIn((code, 3), [(records[r[0]]["int"]["action name"], r[1]) for r in line["ref"]["effects"]])
        self.assertIn((code, 2, 1, 2), [
            (records[r[0]]["int"]["condition name"], records[r[0]]["int"]["compare by"], records[r[0]]["int"]["who"], r[1])
            for r in line["ref"].get("conditions", [])
        ])


class RecoveryCells(unittest.TestCase):
    def test_sayos_cell_has_at_most_two_generic_scouts_and_preserves_old_ids(self):
        class FixtureDb:
            records = {
                ("character", "Sand Ninja"): builder._rec(0, "ninja", "Sand Ninja"),
                ("squad", "Sand ninja cell"): builder._rec(1, "cell", "Sand ninja cell"),
                ("faction", "Sand ninjas"): builder._rec(14, "sand", "Sand ninjas"),
                ("biome", "Desert"): builder._rec(95, "desert", "Desert"),
                ("zone", "Shem"): builder._rec(95, "shem", "Shem"),
                ("item", "Animal Teeth"): builder._rec(7, "teeth", "Animal Teeth"),
            }

            def find(self, kind, name):
                record = self.records.get((kind, name))
                return [record] if record else []

        template = FixtureDb.records[("squad", "Sand ninja cell")]
        template["ref"] = {"slaves": [("unwanted", 1, 1, 0)], "squad2": [("extra", 5, 8, 0)]}
        spec = {
            "name": "Sayo of the Grey Ribbon", "template": "Sand Ninja", "squad_template": "Sand ninja cell",
            "faction": "Sand ninjas", "biomes": ["Desert"], "zones": ["Shem"],
        }
        b = builder.ModBuilder("fixture")
        compiler = content.Compiler(b, FixtureDb(), tokens=set())
        compiler.target = "meitou"
        compiler.make_outlaw(spec)
        self.assertFalse(compiler.errors)
        camp = compiler.camps[0]
        manifest = meitou_target.convert([b], camps=compiler.camps)
        self.assertEqual(camp["infiltrators"], manifest["camps"][0]["infiltrators"])
        records = {record["sid"]: record for record in b.records}
        squad = records[camp["infiltrators"]]
        scout_id = squad["ref"]["leader"][0][0]
        self.assertEqual({"leader", "squad", "faction"}, squad["ref"].keys())
        self.assertEqual([(scout_id, 1, 0, 0)], squad["ref"]["leader"])
        self.assertEqual([(scout_id, 0, 1, 0)], squad["ref"]["squad"])
        self.assertTrue(squad["bool"]["dont multiply"])
        self.assertFalse(records[scout_id]["bool"]["unique"])
        self.assertEqual(65, records[scout_id]["int"]["stealth stats"])
        self.assertEqual("1-fixture.mod", camp["character"])
        self.assertEqual("2-fixture.mod", camp["squad"])
        self.assertEqual("4-fixture.mod", camp["reprisal"])


class OutlawTokens(unittest.TestCase):
    def test_custom_names_and_stories_keep_the_old_item_and_inventory_ids(self):
        class FixtureDb:
            kinds = {"character": 0, "squad": 1, "item": 7}

            def find(self, kind, name):
                if kind not in self.kinds:
                    return []
                return [builder._rec(self.kinds[kind], f"{kind}-test", name)]

        def cook(token):
            b = builder.ModBuilder("fixture")
            c = content.Compiler(b, FixtureDb(), tokens=set())
            c.target = "meitou"
            spec = {"name": "Orsa Quillon", "template": "Dust Boss", "squad_template": "Dust Bandit squad"}
            if token is not None:
                spec["token"] = token
            c.make_outlaw(spec)
            self.assertFalse(c.errors)
            return b.records, c.created

        old, _ = cook(None)
        name = "Orsa's Knotted Toll Rope"
        new, aliases = cook({"name": name, "description": "The knots mark Orsa's toll road."})
        self.assertEqual([r["sid"] for r in old], [r["sid"] for r in new])
        token = next(r for r in new if r["name"] == name)
        self.assertEqual("3-fixture.mod", token["sid"])
        self.assertEqual(11, token["int"]["slot"])
        self.assertIn("The knots mark Orsa's toll road.", token["str"]["description"])
        self.assertIn((token["sid"], 1, 0, 0), new[0]["ref"]["inventory"])
        self.assertEqual(token["sid"], aliases[("item", "Orsa Quillon's Token")])

    def test_a_successor_token_adds_one_reserved_record_without_shifting_old_ids(self):
        class FixtureDb:
            kinds = {"character": 0, "squad": 1, "item": 7}

            def find(self, kind, name):
                if kind not in self.kinds:
                    return []
                return [builder._rec(self.kinds[kind], f"{kind}-test", name)]

        def cook(token):
            b = builder.ModBuilder("fixture")
            c = content.Compiler(b, FixtureDb(), tokens=set())
            c.target = "meitou"
            successor = {"name": "Benna Quillon", "story": "She took over the road toll."}
            if token is not None:
                successor["token"] = token
            c.make_outlaw({"name": "Orsa Quillon", "template": "Dust Boss", "squad_template": "Dust Bandit squad",
                           "successor": successor})
            self.assertFalse(c.errors)
            return b.records, c.camps[0]["successor"]["character"]

        old, _ = cook(None)
        new, actor = cook({"name": "Benna's Re-knotted Toll Rope", "id": 951100})
        token = "951100-fixture.mod"
        self.assertEqual([r["sid"] for r in old], [r["sid"] for r in new if r["sid"] != token])
        records = {record["sid"]: record for record in new}
        self.assertIn(token, records)
        self.assertEqual("Benna's Re-knotted Toll Rope", records[token]["name"])
        self.assertIn("She took over the road toll.", records[token]["str"]["description"])
        self.assertIn((token, 1, 0, 0), records[actor]["ref"]["inventory"])

    def test_all_current_outlaws_have_a_different_named_token(self):
        import json
        from pathlib import Path

        root = Path(__file__).with_name("content")
        records = [entry for pack in ("outlaws.json", "recruits.json")
                   for entry in json.loads((root / pack).read_text(encoding="utf-8"))["outlaws"]]
        self.assertEqual(36, len(records))
        self.assertEqual(len(records), len({r["token"]["name"] for r in records}))
        self.assertTrue(all(r["token"]["description"] for r in records))
        successors = [r["successor"]["token"] for r in records]
        self.assertEqual(36, len({token["name"] for token in successors}))
        self.assertEqual(set(range(951100, 951136)), {token["id"] for token in successors})


class Criers(unittest.TestCase):
    def test_a_crier_copy_reaches_towns_whose_faction_has_no_news_of_its_own(self):
        # The Hub (Holy Nation Outlaws) and the prosperous United Cities towns (Empire Peasants) had no crier lines: their criers stayed
        # silent in play. A news line for drifters or the United Cities is now called out there too.
        class NoDb:
            def find(self, kind, name):
                return []

        compiler = content.Compiler(builder.ModBuilder("fixture"), NoDb(), tokens=set())
        compiler.target = "meitou"
        copies = []
        compiler.build_dialogue = copies.append          # the copy is built through self: catch it instead
        news = {"id": "wn_test_march", "event": "EV_I_SEE_NEUTRAL_SQUAD", "factions": ["Drifters", "United Cities"],
                "lines": [{"say": ["The Holy Nation marches."]}]}
        try:
            content.Compiler.build_dialogue(compiler, news)
        except Exception:
            pass                                         # building the news line itself needs game records; only its copy is checked
        self.assertEqual(1, len(copies))
        self.assertEqual(["Drifters", "United Cities", "Holy Nation Outlaws", "Empire Peasants"], copies[0]["factions"])
        self.assertEqual(["Drifters", "United Cities"], news["factions"])

class RetiredPrototype(unittest.TestCase):
    def test_guides_with_retired_lua_install_steps_are_marked_historical(self):
        from pathlib import Path

        folder = Path(__file__).parent
        command = "install_all_with_lua.bat"
        for guide in sorted(folder.glob("*PLAY*.md")):
            text = guide.read_text(encoding="utf-8")
            if command not in text:
                continue
            with self.subTest(guide=guide.name):
                self.assertIn("**Historical:**", text.partition("\n## ")[0])
                self.assertIn("MEITOU_PLAY_GUIDE.md", text.partition("\n## ")[0])

    def test_build_cli_no_longer_offers_the_prototype_installer(self):
        import contextlib
        import io
        from unittest.mock import patch

        # The CLI's help does not need the optional game-data reader.
        with patch.dict(sys.modules, {"fcs_reader": types.ModuleType("fcs_reader")}):
            import build_mod
            output = io.StringIO()
            with contextlib.redirect_stdout(output), self.assertRaises(SystemExit) as exit_status:
                build_mod.main(["--help"])

        self.assertEqual(0, exit_status.exception.code)
        self.assertIn("--install", output.getvalue())
        self.assertIn("--target {kenshi,meitou}", output.getvalue())
        self.assertNotIn("--with-lua", output.getvalue())


if __name__ == "__main__":
    unittest.main()
