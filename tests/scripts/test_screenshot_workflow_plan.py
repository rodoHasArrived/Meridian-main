import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
PLAN_SCRIPT = REPO_ROOT / "scripts" / "dev" / "screenshot_workflow_plan.py"


def load_plan_module():
    spec = importlib.util.spec_from_file_location("screenshot_workflow_plan", PLAN_SCRIPT)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load screenshot_workflow_plan.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ScreenshotWorkflowPlanTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.planner = load_plan_module()

    def write_json(self, root: Path, name: str, payload: dict) -> Path:
        path = root / name
        path.write_text(json.dumps(payload), encoding="utf-8")
        return path

    def test_plan_derives_desktop_matrix_from_workflow_definitions(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            desktop = self.write_json(
                root,
                "desktop-workflows.json",
                {
                    "workflows": [
                        {
                            "name": "screenshot-catalog",
                            "publish": {"directory": "docs/screenshots/desktop"},
                            "steps": [{"captureName": "catalog-a"}],
                        },
                        {
                            "name": "debug-startup",
                            "steps": [{"captureName": "debug-a"}],
                        },
                        {
                            "name": "manual-new-operator-flow",
                            "includeInManual": True,
                            "steps": [{"captureName": "01-entry"}, {"captureName": "02-review"}],
                        },
                    ]
                },
            )
            web = self.write_json(root, "web-routes.json", {"outputRoot": "docs/web", "captures": []})

            plan = self.planner.build_plan(
                surface_mode="desktop",
                workflow_mode="all",
                desktop_workflows_path=desktop,
                web_routes_path=web,
                desktop_output_root="custom/desktop",
            )

        matrix = plan["desktopMatrix"]["workflow"]
        self.assertEqual(["screenshot-catalog", "manual-new-operator-flow"], [item["name"] for item in matrix])
        self.assertEqual("custom/desktop", matrix[0]["output"])
        self.assertEqual("custom/desktop/manuals/manual-new-operator-flow", matrix[1]["output"])
        self.assertIn("custom/desktop/catalog-a.png", plan["expectedFiles"])
        self.assertIn("custom/desktop/manuals/manual-new-operator-flow/02-review.png", plan["expectedFiles"])
        self.assertNotIn("custom/desktop/manuals/debug-startup/debug-a.png", plan["expectedFiles"])

    def test_plan_uses_definition_defaults_for_output_roots(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            desktop = self.write_json(
                root,
                "desktop-workflows.json",
                {
                    "workflows": [
                        {
                            "name": "screenshot-catalog",
                            "publish": {"directory": "docs/from-definition/desktop"},
                            "steps": [{"captureName": "catalog-a"}],
                        }
                    ]
                },
            )
            web = self.write_json(
                root,
                "web-routes.json",
                {"outputRoot": "docs/from-definition/web", "captures": [{"name": "web-a", "path": "/"}]},
            )

            plan = self.planner.build_plan(
                surface_mode="all",
                workflow_mode="catalog",
                desktop_workflows_path=desktop,
                web_routes_path=web,
            )

        self.assertEqual("docs/from-definition/desktop", plan["desktopOutputRoot"])
        self.assertEqual("docs/from-definition/web", plan["webOutputRoot"])
        self.assertEqual(
            ["docs/from-definition/desktop/catalog-a.png", "docs/from-definition/web/web-a.png"],
            plan["expectedFiles"],
        )

    def test_web_only_plan_does_not_require_desktop_matrix_entries(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            desktop = self.write_json(root, "desktop-workflows.json", {"workflows": []})
            web = self.write_json(
                root,
                "web-routes.json",
                {"outputRoot": "docs/screenshots/web", "captures": [{"name": "web-a", "path": "/"}]},
            )

            plan = self.planner.build_plan(
                surface_mode="web",
                workflow_mode="all",
                desktop_workflows_path=desktop,
                web_routes_path=web,
            )

        self.assertFalse(plan["includeDesktop"])
        self.assertTrue(plan["includeWeb"])
        self.assertEqual({"workflow": []}, plan["desktopMatrix"])
        self.assertEqual(["docs/screenshots/web/web-a.png"], plan["expectedFiles"])


if __name__ == "__main__":
    unittest.main()
