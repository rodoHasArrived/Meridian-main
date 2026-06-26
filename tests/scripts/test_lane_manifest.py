from __future__ import annotations

import importlib.util
import json
import sys
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "ci" / "check-lane-manifest.py"
MANIFEST_PATH = REPO_ROOT / "build" / "ci" / "lane-manifest.json"

SPEC = importlib.util.spec_from_file_location("check_lane_manifest", SCRIPT_PATH)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules["check_lane_manifest"] = MODULE
SPEC.loader.exec_module(MODULE)


class LaneManifestTests(unittest.TestCase):
    def test_manifest_declares_stable_required_quality_gate(self) -> None:
        payload = MODULE.load_manifest(MANIFEST_PATH)

        self.assertEqual(payload["requiredStatusCheck"]["workflow"], "Meridian CI")
        self.assertEqual(payload["requiredStatusCheck"]["job"], "quality-gate")
        self.assertEqual(payload["requiredStatusCheck"]["checkName"], "Meridian CI / quality-gate")

    def test_manifest_contains_required_lane_ids(self) -> None:
        payload = MODULE.load_manifest(MANIFEST_PATH)
        lane_ids = {lane["id"] for lane in payload["lanes"]}

        self.assertTrue(MODULE.REQUIRED_LANES.issubset(lane_ids))
        self.assertIn("verify-dotnet", lane_ids)
        self.assertIn("verify-browser", lane_ids)
        self.assertIn("verify-docs", lane_ids)
        self.assertIn("verify-workflows", lane_ids)

    def test_manifest_validates_without_errors(self) -> None:
        payload = MODULE.load_manifest(MANIFEST_PATH)

        self.assertEqual(MODULE.validate_manifest(payload), [])

    def test_only_quality_gate_is_required_aggregator(self) -> None:
        payload = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
        aggregators = [lane["id"] for lane in payload["lanes"] if lane["requiredCheckRole"] == "aggregator"]

        self.assertEqual(aggregators, ["quality-gate"])


if __name__ == "__main__":
    unittest.main()
