import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "ai_edit_tool.py"
SPEC = importlib.util.spec_from_file_location("ai_edit_tool", MODULE_PATH)
ai_edit_tool = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = ai_edit_tool
SPEC.loader.exec_module(ai_edit_tool)


class AiEditToolTests(unittest.TestCase):
    def make_repo(self):
        temp = tempfile.TemporaryDirectory()
        root = Path(temp.name)
        (root / "Meridian.sln").write_text("", encoding="utf-8")
        return temp, root

    def plan(self, root, recipe, scope=None):
        return ai_edit_tool.build_plan(root, recipe, scope or {"paths": ["."]})

    def test_literal_replacement_plan_and_apply(self):
        temp, root = self.make_repo()
        self.addCleanup(temp.cleanup)
        target = root / "src" / "Sample.cs"
        target.parent.mkdir()
        target.write_text("alpha beta alpha\n", encoding="utf-8")

        recipe = {
            "kind": "literal_replace",
            "include": ["src/**/*.cs"],
            "exclude": [],
            "find": "alpha",
            "replace": "omega",
            "maxFiles": 1,
            "maxEdits": 2,
        }

        plan = self.plan(root, recipe, {"paths": ["src"]})
        self.assertEqual(1, len(plan["files"]))
        self.assertEqual(2, len(plan["files"][0]["edits"]))

        plan_path = root / "plan.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        status = ai_edit_tool.apply_plan(root, plan_path)
        self.assertEqual(1, status["fileCount"])
        self.assertEqual("omega beta omega\n", target.read_text(encoding="utf-8"))

    def test_regex_replacement_uses_capture_groups(self):
        temp, root = self.make_repo()
        self.addCleanup(temp.cleanup)
        target = root / "src" / "Sample.ts"
        target.parent.mkdir()
        target.write_text("const symbol = 'MSFT';\n", encoding="utf-8")

        recipe = {
            "kind": "regex_replace",
            "include": ["src/**/*.ts"],
            "exclude": [],
            "find": r"'([A-Z]+)'",
            "replace": r'"\1"',
            "maxFiles": 1,
            "maxEdits": 1,
        }

        plan = self.plan(root, recipe, {"paths": ["src"]})
        edit = plan["files"][0]["edits"][0]
        self.assertEqual("'MSFT'", edit["before"])
        self.assertEqual('"MSFT"', edit["after"])

    def test_anchored_block_requires_exact_anchors(self):
        temp, root = self.make_repo()
        self.addCleanup(temp.cleanup)
        target = root / "README.md"
        target.write_text("start\nbody\nstart\nend\n", encoding="utf-8")
        recipe = {
            "kind": "anchored_block_replace",
            "include": ["*.md"],
            "exclude": [],
            "startAnchor": "start",
            "endAnchor": "end",
            "replace": "start\nnew\nend",
            "maxFiles": 1,
            "maxEdits": 1,
        }

        with self.assertRaises(ai_edit_tool.EditToolError):
            self.plan(root, recipe, {"paths": ["."]})

    def test_anchored_block_fails_when_anchor_missing(self):
        temp, root = self.make_repo()
        self.addCleanup(temp.cleanup)
        target = root / "README.md"
        target.write_text("start\nbody\n", encoding="utf-8")
        recipe = {
            "kind": "anchored_block_replace",
            "include": ["*.md"],
            "exclude": [],
            "startAnchor": "start",
            "endAnchor": "end",
            "replace": "start\nnew\nend",
            "maxFiles": 1,
            "maxEdits": 1,
        }

        with self.assertRaises(ai_edit_tool.EditToolError):
            self.plan(root, recipe, {"paths": ["."]})

    def test_generated_scope_rejected_by_default(self):
        temp, root = self.make_repo()
        self.addCleanup(temp.cleanup)
        generated = root / "docs" / "generated"
        generated.mkdir(parents=True)
        (generated / "x.md").write_text("alpha\n", encoding="utf-8")
        recipe = {
            "kind": "literal_replace",
            "include": ["docs/**/*.md"],
            "exclude": [],
            "find": "alpha",
            "replace": "beta",
            "maxFiles": 1,
            "maxEdits": 1,
        }

        with self.assertRaises(ai_edit_tool.EditToolError):
            self.plan(root, recipe, {"paths": ["docs/generated"]})

    def test_hash_drift_rejected_before_apply(self):
        temp, root = self.make_repo()
        self.addCleanup(temp.cleanup)
        target = root / "sample.py"
        target.write_text("alpha\n", encoding="utf-8")
        recipe = {
            "kind": "literal_replace",
            "include": ["*.py"],
            "exclude": [],
            "find": "alpha",
            "replace": "beta",
            "maxFiles": 1,
            "maxEdits": 1,
        }
        plan = self.plan(root, recipe, {"paths": ["."]})
        plan_path = root / "plan.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        target.write_text("changed\n", encoding="utf-8")

        with self.assertRaises(ai_edit_tool.EditToolError):
            ai_edit_tool.apply_plan(root, plan_path)

    def test_max_edit_count_enforced(self):
        temp, root = self.make_repo()
        self.addCleanup(temp.cleanup)
        target = root / "sample.py"
        target.write_text("alpha alpha\n", encoding="utf-8")
        recipe = {
            "kind": "literal_replace",
            "include": ["*.py"],
            "exclude": [],
            "find": "alpha",
            "replace": "beta",
            "maxFiles": 1,
            "maxEdits": 1,
        }

        with self.assertRaises(ai_edit_tool.EditToolError):
            self.plan(root, recipe, {"paths": ["."]})

    def test_max_file_count_enforced(self):
        temp, root = self.make_repo()
        self.addCleanup(temp.cleanup)
        (root / "one.py").write_text("alpha\n", encoding="utf-8")
        (root / "two.py").write_text("alpha\n", encoding="utf-8")
        recipe = {
            "kind": "literal_replace",
            "include": ["*.py"],
            "exclude": [],
            "find": "alpha",
            "replace": "beta",
            "maxFiles": 1,
            "maxEdits": 3,
        }

        with self.assertRaises(ai_edit_tool.EditToolError):
            self.plan(root, recipe, {"paths": ["."]})


if __name__ == "__main__":
    unittest.main()
