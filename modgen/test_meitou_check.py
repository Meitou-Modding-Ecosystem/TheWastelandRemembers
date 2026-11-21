import json
import os
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


if __name__ == "__main__":
    unittest.main()
