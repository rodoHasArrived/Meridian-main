import json
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WEB_SCREENSHOT_CAPTURE_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "web-screenshot-capture.yml"
RUN_DESKTOP_WORKFLOW_SCRIPT = REPO_ROOT / "scripts" / "dev" / "run-desktop-workflow.ps1"
WEB_SCREENSHOT_ROUTES = REPO_ROOT / "scripts" / "dev" / "web-screenshot-routes.json"
WEB_SCREENSHOT_FIXTURES = REPO_ROOT / "scripts" / "dev" / "web-screenshot-fixtures.json"


class RefreshScreenshotsWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WEB_SCREENSHOT_CAPTURE_WORKFLOW.read_text(encoding="utf-8")
        cls.run_desktop_workflow_script = RUN_DESKTOP_WORKFLOW_SCRIPT.read_text(encoding="utf-8")
        cls.web_screenshot_routes = json.loads(WEB_SCREENSHOT_ROUTES.read_text(encoding="utf-8"))
        cls.web_screenshot_fixtures = json.loads(WEB_SCREENSHOT_FIXTURES.read_text(encoding="utf-8"))

    def test_web_screenshot_job_installs_optional_native_packages(self) -> None:
        self.assertIn("run: npm install --prefix src/Meridian.Ui/dashboard --include=optional", self.workflow)
        self.assertIn("cache-dependency-path: src/Meridian.Ui/dashboard/package.json", self.workflow)
        self.assertNotIn("npm ci", self.workflow)
        self.assertNotIn("package-lock.json", self.workflow)

    def test_wpf_screenshot_job_downloads_prebuilt_binaries_under_src(self) -> None:
        self.assertIn("name: wpf-build-binaries", self.workflow)
        self.assertIn("path: src", self.workflow)

    def test_refresh_workflow_uses_dynamic_screenshot_plan(self) -> None:
        self.assertIn("plan-screenshots:", self.workflow)
        self.assertIn("scripts/dev/screenshot_workflow_plan.py", self.workflow)
        self.assertIn("matrix: ${{ fromJson(needs.plan-screenshots.outputs.desktop_matrix) }}", self.workflow)
        self.assertNotIn("name: manual-data-operations", self.workflow)
        self.assertNotIn("name: manual-research-and-trading", self.workflow)

    def test_desktop_workflow_script_contains_context_selection_automation_elements(self) -> None:
        self.assertIn("ContextSelectionHint", self.run_desktop_workflow_script)
        self.assertIn("ContextSelectionHintButton", self.run_desktop_workflow_script)
        self.assertIn("SwitchContextButton", self.run_desktop_workflow_script)
        self.assertIn("Invoke-AutomationButton -Button $switchContextButton -Description 'switch context'", self.run_desktop_workflow_script)

    def test_web_screenshot_routes_cover_workspace_mega_menu_links(self) -> None:
        captures = self.web_screenshot_routes.get("captures", [])
        captured_paths = {capture.get("path") for capture in captures if capture.get("path")}

        expected_paths = {
            "/trading",
            "/trading/orders",
            "/trading/positions",
            "/trading/risk",
            "/trading/readiness",
            "/portfolio",
            "/portfolio/attribution",
            "/portfolio/brokerage-sync",
            "/accounting",
            "/accounting/reconciliation",
            "/accounting/security-master",
            "/accounting/approvals",
            "/reporting",
            "/reporting/report-packs",
            "/reporting/evidence",
            "/reporting/exports",
            "/strategy",
            "/strategy/promotions",
            "/strategy/research",
            "/strategy/quant-lab",
            "/strategy/designer",
            "/data",
            "/data/watchlist",
            "/data/quotes",
            "/data/backfills",
            "/settings",
            "/settings/preferences",
            "/settings/integrations",
        }

        missing_paths = sorted(expected_paths - captured_paths)
        self.assertEqual([], missing_paths, f"Missing web screenshot routes: {missing_paths}")

    def test_strategy_designer_screenshot_route_has_fixture_evidence(self) -> None:
        captures = self.web_screenshot_routes.get("captures", [])
        designer_capture = next(
            (capture for capture in captures if capture.get("path") == "/strategy/designer"),
            None,
        )

        self.assertIsNotNone(designer_capture)
        self.assertEqual("web-strategy-designer", designer_capture.get("name"))
        self.assertEqual("Strategy Builder Workbench", designer_capture.get("waitForText"))

        fixture_routes = self.web_screenshot_fixtures.get("routes", {})
        self.assertIn("/api/workstation/strategy/designer/templates", fixture_routes)
        self.assertIn("/api/workstation/strategy/designer/field-catalog", fixture_routes)
        self.assertIn("/api/workstation/strategy/designer/drafts", fixture_routes)
        self.assertIn("/api/workstation/strategy/designer/drafts/strategy-designer-fixture-1", fixture_routes)
        self.assertEqual(
            "strategy-designer-fixture-1",
            fixture_routes["/api/workstation/strategy/designer/templates"][0]["document"]["documentId"],
        )


if __name__ == "__main__":
    unittest.main()
