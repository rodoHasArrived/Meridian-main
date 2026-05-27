import importlib.util
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "build" / "scripts" / "docs" / "check-codex-skills.py"

spec = importlib.util.spec_from_file_location("check_codex_skills", MODULE_PATH)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class CheckCodexSkillsTests(unittest.TestCase):
    def test_current_codex_skills_have_required_surfaces(self) -> None:
        findings = module.collect_findings()
        errors = [finding for finding in findings if finding.severity == "error"]

        self.assertEqual([], errors)

    def test_current_skill_dirs_excludes_shared_folder(self) -> None:
        skill_names = [path.name for path in module.current_skill_dirs()]

        self.assertIn("meridian-implementation-assurance", skill_names)
        self.assertNotIn("_shared", skill_names)


if __name__ == "__main__":
    unittest.main()
