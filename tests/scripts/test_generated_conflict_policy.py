"""Conflict resolution authority is narrower than generated write authority."""
from __future__ import annotations

import copy
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


resolver = load("conflict_policy_resolver", "build/scripts/resolve-generated-merge-conflicts.py")
runner = load("conflict_policy_generation", "build/scripts/run-maintained-generation.py")
automation = load("conflict_policy_docs", "build/scripts/docs/run-docs-automation.py")


class GeneratedConflictPolicyTests(unittest.TestCase):
    def setUp(self):
        self.policy = resolver.load_conflict_policy()

    def validate_fixture(self, policy):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            # Only inert files are needed to prove that reviewed producer paths
            # exist; none of these copied scripts are executed.
            records = list(self.policy["conflict_recovery"]["files"].values())
            records += list(self.policy["conflict_recovery"]["trees"].values())
            for record in records:
                if record.get("required"):
                    path = record["generator"].split("#", 1)[0]
                    target = root / path
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes((ROOT / path).read_bytes())
            destination = root / "build/scripts/maintained-generation-outputs.json"
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(json.dumps(policy))
            return resolver.load_conflict_policy(destination)

    def test_all_repairable_outputs_have_required_maintained_producers(self):
        required = set()
        for step in runner.discover_steps(ROOT):
            if step.optional:
                continue
            command = step.command
            if command[0] in {"python", "python3"}:
                required.add(command[1])
                if command[1] == "build/scripts/docs/run-docs-automation.py":
                    self.assertIn("--profile", command)
                    profile = command[command.index("--profile") + 1]
                    required.update("build/scripts/docs/" + automation.SCRIPT_CONFIG[name]["script"]
                                    for name in automation.PROFILE_CONFIG[profile])
            if command[:2] == ("npm", "--prefix") and command[-2:] == ("run", "build"):
                required.add(command[2] + "/package.json#scripts.build")
        recovery = self.policy["conflict_recovery"]
        records = list(recovery["files"].items()) + list(recovery["trees"].items())
        for path, record in records:
            if record["strategy"] in {"regenerate", "protected-suffix"}:
                with self.subTest(output=path):
                    self.assertTrue(record["required"])
                    self.assertIn(record["generator"], required)

    def test_protected_and_hybrid_outputs_cannot_be_whole_file_seeds(self):
        for path in list(self.policy["protected_suffixes"]) + list(self.policy["hybrid_files"]):
            with self.subTest(path=path):
                policy = copy.deepcopy(self.policy)
                policy["conflict_recovery"]["files"][path] = {
                    "strategy": "regenerate", "generator": "build/scripts/docs/generate-coverage.py",
                    "required": True,
                }
                with self.assertRaisesRegex(ValueError, "protected content"):
                    self.validate_fixture(policy)

    def test_a_generated_tree_cannot_encompass_protected_content(self):
        policy = copy.deepcopy(self.policy)
        policy["whole_trees"].append("docs")
        policy["conflict_recovery"]["trees"]["docs"] = {
            "strategy": "regenerate", "generator": "build/scripts/docs/generate-structure-docs.py",
            "required": True,
        }
        with self.assertRaisesRegex(ValueError, "protected content"):
            self.validate_fixture(policy)

    def test_missing_optional_or_unknown_producers_cannot_authorize_recovery(self):
        for changes in ({"required": False}, {"generator": "build/missing.py"},
                        {"generator": "src/Meridian.Ui/dashboard/package.json#scripts.unknown"}):
            with self.subTest(changes=changes):
                policy = copy.deepcopy(self.policy)
                policy["conflict_recovery"]["files"]["docs/generated/repository-structure.md"].update(changes)
                with self.assertRaisesRegex(ValueError, "generator"):
                    self.validate_fixture(policy)

    def test_unknown_and_advisory_outputs_require_manual_resolution(self):
        for path in ("docs/generated/new-report.md", "docs/status/api-docs-report.md",
                     "docs/diagrams/uml/Use Case Diagram - Meridian.svg", "src/Example/README.md"):
            with self.subTest(path=path):
                self.assertEqual(resolver.conflict_strategy(path, self.policy), "manual")

    def test_missing_strategy_catalog_fails_closed(self):
        policy = copy.deepcopy(self.policy)
        del policy["conflict_recovery"]
        with self.assertRaisesRegex(ValueError, "Missing or unsupported"):
            self.validate_fixture(policy)


if __name__ == "__main__":
    unittest.main()
