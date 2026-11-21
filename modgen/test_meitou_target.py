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


if __name__ == "__main__":
    unittest.main()
