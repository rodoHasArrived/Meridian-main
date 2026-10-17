from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "dev" / "desktop_screen_blueprint_checklist.py"
CHECKLIST_PATH = REPO_ROOT / "docs" / "plans" / "desktop-workstation-screen-blueprint.checklist.json"
WORKFLOWS_PATH = REPO_ROOT / "scripts" / "dev" / "desktop-workflows.json"


def _load_module():
    spec = importlib.util.spec_from_file_location("desktop_screen_blueprint_checklist", SCRIPT_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load script module from {SCRIPT_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class DesktopScreenBlueprintChecklistTests(unittest.TestCase):
    def test_checklist_is_valid_and_tracks_all_blueprint_screens(self) -> None:
        module = _load_module()

        errors, summary = module.validate_checklist(CHECKLIST_PATH, WORKFLOWS_PATH)

        self.assertEqual([], errors)
        self.assertEqual(20, summary["screenCount"])
        self.assertGreaterEqual(summary["workflowCoveredScreenCount"], 10)
        self.assertEqual(5, summary["workspaceCounts"]["Trading"])
        self.assertEqual(4, summary["workspaceCounts"]["Settings"])

    def test_script_summary_command_succeeds(self) -> None:
        result = subprocess.run(
            [sys.executable, str(SCRIPT_PATH), "--summary"],
            cwd=REPO_ROOT,
            check=False,
            capture_output=True,
            text=True,
        )

        self.assertEqual(0, result.returncode, result.stderr + result.stdout)
        self.assertIn("Desktop screen blueprint checklist: 20 screens", result.stdout)
        self.assertIn("Workflow-covered screens:", result.stdout)

    def test_empty_redirect_or_prose_only_blueprint_cannot_satisfy_screen_contract(self) -> None:
        module = _load_module()
        heading = "### 1. Security Master Workspace"
        for blueprint_text in ("", "# Archived Legacy Plan\nSee the replacement plan.\n", f"See `{heading}`.\n"):
            with self.subTest(blueprint=blueprint_text), tempfile.TemporaryDirectory() as temp_dir:
                root = Path(temp_dir)
                (root / "blueprint.md").write_text(blueprint_text, encoding="utf-8")
                (root / "tests").mkdir()
                (root / "tests/evidence.py").write_text("# Focused evidence fixture\n", encoding="utf-8")
                checklist_path = root / "checklist.json"
                checklist_path.write_text(json.dumps({
                    "blueprintPath": "blueprint.md",
                    "allowedMaturityLabels": sorted(module.VALID_MATURITY_LABELS),
                    "screens": [{
                        "id": "desktop-screen-security-master",
                        "title": "Security Master Workspace",
                        "status": "partial",
                        "maturity": "Needs verification",
                        "rootWorkspace": "Data",
                        "blueprintHeading": heading,
                        "validationCommands": ["python tests/evidence.py"],
                        "maturityEvidence": [{
                            "path": "tests/evidence.py",
                            "kind": "focused-test",
                            "summary": "Fixture evidence",
                        }],
                    }],
                }), encoding="utf-8")
                workflows_path = root / "workflows.json"
                workflows_path.write_text('{"workflows": []}\n', encoding="utf-8")

                with patch.object(module, "REPO_ROOT", root):
                    errors, _ = module.validate_checklist(checklist_path, workflows_path)

                self.assertEqual([
                    f"desktop-screen-security-master blueprintHeading was not found in the blueprint: {heading}"
                ], errors)


if __name__ == "__main__":
    unittest.main()
