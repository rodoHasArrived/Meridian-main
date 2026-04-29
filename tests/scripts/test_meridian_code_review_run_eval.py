from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SKILL_DIR = REPO_ROOT / ".claude" / "skills" / "meridian-code-review"
EVALS_PATH = SKILL_DIR / "evals" / "evals.json"

sys.path.insert(0, str(SKILL_DIR))

from scripts.run_eval import load_eval_set, normalize_eval_set  # noqa: E402
from scripts.utils import parse_skill_md  # noqa: E402


class MeridianCodeReviewRunEvalTests(unittest.TestCase):
    def test_current_skill_creator_eval_manifest_loads_as_trigger_cases(self) -> None:
        eval_set = load_eval_set(EVALS_PATH)

        with EVALS_PATH.open(encoding="utf-8") as fp:
            raw_manifest = json.load(fp)

        self.assertEqual(len(eval_set), len(raw_manifest["evals"]))
        self.assertEqual(eval_set[0]["query"], raw_manifest["evals"][0]["prompt"])
        self.assertTrue(all(item["should_trigger"] is True for item in eval_set))

    def test_skill_frontmatter_loads_with_utf8_content(self) -> None:
        name, description, content = parse_skill_md(SKILL_DIR)

        self.assertEqual(name, "meridian-code-review")
        self.assertIn("Meridian", description)
        self.assertIn("##", content)

    def test_legacy_trigger_eval_list_still_loads(self) -> None:
        eval_set = normalize_eval_set([
            {"query": "Review this provider", "should_trigger": True},
            {"prompt": "Write a generic roadmap", "should_trigger": False},
        ])

        self.assertEqual(eval_set[0]["query"], "Review this provider")
        self.assertTrue(eval_set[0]["should_trigger"])
        self.assertEqual(eval_set[1]["query"], "Write a generic roadmap")
        self.assertFalse(eval_set[1]["should_trigger"])

    def test_invalid_manifest_shape_reports_clear_error(self) -> None:
        with self.assertRaisesRegex(ValueError, "Eval item 1 must be an object"):
            normalize_eval_set(["Review this provider"])

        with self.assertRaisesRegex(ValueError, "must define a non-empty 'query' or 'prompt'"):
            normalize_eval_set({"evals": [{"id": 42, "assertions": []}]})

        with self.assertRaisesRegex(ValueError, "'should_trigger' must be a boolean"):
            normalize_eval_set({"evals": [{"id": 42, "prompt": "Review this", "should_trigger": "yes"}]})


if __name__ == "__main__":
    unittest.main()
