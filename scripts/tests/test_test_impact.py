import importlib.util
from pathlib import Path
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "test_impact.py"
spec = importlib.util.spec_from_file_location("test_impact", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class TestImpactTests(unittest.TestCase):
    def test_plc_change_keeps_old_and_new_protocol_evidence_separate(self):
        plan = module.make_plan(
            ["simulator/VirtualPlc/src/VirtualPlc/Protocol20260911Store.cs"],
            "S01", "abc",
        )
        self.assertIn("plc_legacy", plan["prRequired"])
        self.assertIn("plc_v13", plan["prRequired"])
        self.assertIn("plc_wire", plan["prRequired"])
        self.assertIn("recovery", plan["prRequired"])
        self.assertIn("station_matrix", plan["stationAcceptanceRequired"])

    def test_unfamiliar_source_fails_closed(self):
        plan = module.make_plan(["new-device/Controller.cs"], "S02", "abc")
        self.assertEqual(["new-device/Controller.cs"], plan["unclassifiedFiles"])
        self.assertIn("manual_classification", plan["prRequired"])

    def test_no_change_is_error(self):
        with self.assertRaises(ValueError):
            module.make_plan([], "S01", "abc")

    def test_station_gate_rejects_skips_missing_evidence_and_wrong_commit(self):
        plan = module.make_plan(["docs/testing.md"], "S01", "abc")
        results = [{"id": name, "status": "PASS", "evidence": "ci/run/123"}
                   for name in plan["prRequired"] + plan["stationAcceptanceRequired"]]
        self.assertEqual([], module.check_report(plan, {"head": "abc", "results": results}, "station"))
        results[-1]["status"] = "NOT RUN"
        self.assertTrue(any("station_boundary" in e for e in module.check_report(
            plan, {"head": "abc", "results": results}, "station")))
        results[-1]["status"] = "PASS"
        results[-1]["evidence"] = ""
        self.assertTrue(any("evidence" in e for e in module.check_report(
            plan, {"head": "abc", "results": results}, "station")))
        self.assertTrue(any("head" in e for e in module.check_report(
            plan, {"head": "old", "results": results}, "pr")))

    def test_full_tray_requires_windows_and_recipe_evidence(self):
        plan = module.make_plan(["docs/fullsim-coverage-plan.md"], "S11", "abc")
        self.assertIn("full_tray", plan["stationAcceptanceRequired"])
        self.assertIn("windows_desktop", plan["stationAcceptanceRequired"])


if __name__ == "__main__":
    unittest.main()
