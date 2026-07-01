import json
import re
import unittest
from pathlib import Path
from urllib.parse import urlsplit


REPO_ROOT = Path(__file__).resolve().parents[2]
WEB_SCREENSHOT_CAPTURE_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "web-screenshot-capture.yml"
DESKTOP_SCREENSHOT_CAPTURE_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "desktop-screenshot-capture.yml"
DESKTOP_WORKFLOW_RUNNER_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "desktop-workflow-runner.yml"
RUN_DESKTOP_WORKFLOW_SCRIPT = REPO_ROOT / "scripts" / "dev" / "run-desktop-workflow.ps1"
CAPTURE_DESKTOP_SCREENSHOTS_SCRIPT = REPO_ROOT / "scripts" / "dev" / "capture-desktop-screenshots.ps1"
WEB_SCREENSHOT_ROUTES = REPO_ROOT / "scripts" / "dev" / "web-screenshot-routes.json"
WEB_SCREENSHOT_FIXTURES = REPO_ROOT / "scripts" / "dev" / "web-screenshot-fixtures.json"
WEB_SCREENSHOT_CAPTURE_SCRIPT = REPO_ROOT / "scripts" / "dev" / "capture-web-screenshots.mjs"
DESKTOP_WORKFLOWS = REPO_ROOT / "scripts" / "dev" / "desktop-workflows.json"
WORKSTATION_ROUTE_CATALOG = REPO_ROOT / "src" / "Meridian.Ui" / "dashboard" / "src" / "lib" / "workspace.ts"
WORKSTATION_APP_SHELL = REPO_ROOT / "src" / "Meridian.Ui" / "dashboard" / "src" / "app.tsx"


class RefreshScreenshotsWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.web_workflow = WEB_SCREENSHOT_CAPTURE_WORKFLOW.read_text(encoding="utf-8")
        cls.desktop_screenshot_workflow = DESKTOP_SCREENSHOT_CAPTURE_WORKFLOW.read_text(encoding="utf-8")
        cls.desktop_workflow_runner = DESKTOP_WORKFLOW_RUNNER_WORKFLOW.read_text(encoding="utf-8")
        cls.run_desktop_workflow_script = RUN_DESKTOP_WORKFLOW_SCRIPT.read_text(encoding="utf-8")
        cls.capture_desktop_screenshots_script = CAPTURE_DESKTOP_SCREENSHOTS_SCRIPT.read_text(encoding="utf-8")
        cls.web_screenshot_routes = json.loads(WEB_SCREENSHOT_ROUTES.read_text(encoding="utf-8"))
        cls.web_screenshot_fixtures = json.loads(WEB_SCREENSHOT_FIXTURES.read_text(encoding="utf-8"))
        cls.web_screenshot_capture_script = WEB_SCREENSHOT_CAPTURE_SCRIPT.read_text(encoding="utf-8")
        cls.desktop_workflows = json.loads(DESKTOP_WORKFLOWS.read_text(encoding="utf-8"))
        cls.workstation_route_catalog = WORKSTATION_ROUTE_CATALOG.read_text(encoding="utf-8")
        cls.workstation_app_shell = WORKSTATION_APP_SHELL.read_text(encoding="utf-8")

    def test_web_screenshot_job_installs_optional_native_packages(self) -> None:
        self.assertIn("run: npm install --prefix src/Meridian.Ui/dashboard --include=optional", self.web_workflow)
        self.assertIn("cache-dependency-path: src/Meridian.Ui/dashboard/package-lock.json", self.web_workflow)
        self.assertIn("find \"$OUTPUT_DIR\" -maxdepth 1 -type f -name '*.png' -delete", self.web_workflow)
        self.assertIn("scripts/dev/validate-screenshot-captures.py", self.web_workflow)
        self.assertIn("--surface web", self.web_workflow)
        self.assertIn("--require-fresh", self.web_workflow)
        self.assertIn("pull-requests: write", self.web_workflow)
        self.assertIn("uses: peter-evans/create-pull-request@v7", self.web_workflow)
        self.assertIn("continue-on-error: true", self.web_workflow)
        self.assertIn("branch: automation/web-screenshot-capture", self.web_workflow)
        self.assertIn("base: ${{ github.event.repository.default_branch }}", self.web_workflow)
        self.assertIn("title: \"chore: refresh web workstation screenshot catalog\"", self.web_workflow)
        self.assertNotIn("npm ci", self.web_workflow)
        self.assertNotIn("git push", self.web_workflow)
        self.assertNotIn("<<<<<<<", self.web_workflow)
        self.assertNotIn(">>>>>>>", self.web_workflow)

    def test_desktop_screenshot_job_runs_capture_script_and_keeps_artifacts(self) -> None:
        self.assertIn("scripts/dev/capture-desktop-screenshots.ps1", self.desktop_screenshot_workflow)
        self.assertIn("scripts/dev/validate-screenshot-captures.py", self.desktop_screenshot_workflow)
        self.assertIn("--surface desktop", self.desktop_screenshot_workflow)
        self.assertIn("--require-fresh", self.desktop_screenshot_workflow)
        self.assertNotIn("continue-on-error: true", self.desktop_screenshot_workflow)
        self.assertIn("if: ${{ success() && inputs.commit == true }}", self.desktop_screenshot_workflow)
        self.assertIn("name: desktop-screenshots-${{ github.run_number }}", self.desktop_screenshot_workflow)

    def test_desktop_screenshot_wrapper_uses_fresh_artifact_root_by_default(self) -> None:
        self.assertIn("[string]$OutputRoot", self.capture_desktop_screenshots_script)
        self.assertIn("capture-{0}-{1}", self.capture_desktop_screenshots_script)
        self.assertIn("'-OutputRoot', $workflowArtifactRoot", self.capture_desktop_screenshots_script)
        self.assertNotIn("'-OutputRoot', 'artifacts/desktop-workflows'", self.capture_desktop_screenshots_script)

    def test_desktop_workflow_runner_exposes_manual_capture_workflows(self) -> None:
        self.assertIn("manual-data", self.desktop_workflow_runner)
        self.assertIn("manual-strategy-and-trading", self.desktop_workflow_runner)
        self.assertIn("manual-accounting", self.desktop_workflow_runner)
        self.assertIn("scripts/dev/run-desktop-workflow.ps1", self.desktop_workflow_runner)

    def test_desktop_strategy_runs_workflows_use_canonical_checklist_id(self) -> None:
        serialized = json.dumps(self.desktop_workflows)

        self.assertIn("desktop-screen-strategy-runs", serialized)
        self.assertNotIn("desktop-screen-strategy-research", serialized)

    def test_desktop_workflow_script_contains_context_selection_automation_elements(self) -> None:
        self.assertIn("ContextSelectionHint", self.run_desktop_workflow_script)
        self.assertIn("ContextSelectionHintButton", self.run_desktop_workflow_script)
        self.assertIn("SwitchContextButton", self.run_desktop_workflow_script)
        self.assertIn("Invoke-AutomationButton -Button $switchContextButton -Description 'switch context'", self.run_desktop_workflow_script)

    def test_web_screenshot_routes_cover_workstation_route_catalog_pages(self) -> None:
        captures = self.web_screenshot_routes.get("captures", [])
        captured_paths = {
            self.screenshot_coverage_path(capture.get("path"))
            for capture in captures
            if capture.get("path")
        }
        route_catalog = self.extract_workstation_route_catalog()

        compatibility_redirect_routes = {
            "dataSecurityMasterLegacy",
        }
        expected_paths = {
            self.screenshot_coverage_path(path)
            for key, path in route_catalog.items()
            if key not in compatibility_redirect_routes
        }
        expected_paths.add("/")

        missing_paths = sorted(expected_paths - captured_paths)
        self.assertEqual([], missing_paths, f"Missing web screenshot routes: {missing_paths}")

    def test_web_screenshot_routes_cover_explicit_app_routes(self) -> None:
        captures = self.web_screenshot_routes.get("captures", [])
        captured_paths = {
            self.screenshot_coverage_path(capture.get("path"))
            for capture in captures
            if capture.get("path")
        }
        route_paths = set(re.findall(r'<Route\s+path="([^"]+)"', self.workstation_app_shell))
        compatibility_redirect_routes = {
            "/data/security-master",
            "/data/security-master/*",
            "/overview/*",
            "/research/*",
            "/data-operations/*",
            "/governance/*",
        }
        explicit_pages = {
            self.screenshot_coverage_path(path)
            for path in route_paths
            if "*" not in path and path not in compatibility_redirect_routes
        }

        missing_paths = sorted(explicit_pages - captured_paths)
        self.assertEqual([], missing_paths, f"Missing explicit app route screenshots: {missing_paths}")

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
            [
                "/api/workstation/strategy",
                "/api/workstation/strategy/designer/templates",
                "/api/workstation/strategy/designer/field-catalog",
                "/api/workstation/strategy/designer/drafts",
            ],
            designer_capture.get("requiredApiRoutes"),
        )
        self.assertIn("Equity momentum breakout", designer_capture.get("waitForTexts", []))
        self.assertEqual(
            "strategy-designer-fixture-1",
            fixture_routes["/api/workstation/strategy/designer/templates"][0]["document"]["documentId"],
        )

    def test_web_screenshot_routes_require_fixture_coverage_for_each_capture(self) -> None:
        captures = self.web_screenshot_routes.get("captures", [])
        fixture_routes = self.web_screenshot_fixtures.get("routes", {})
        fixture_route_names = set(fixture_routes.keys())

        self.assertGreater(len(captures), 0)
        for capture in captures:
            capture_name = capture.get("name", "<unnamed>")
            required_routes = capture.get("requiredApiRoutes")
            wait_for_texts = capture.get("waitForTexts")

            self.assertIsInstance(required_routes, list, f"{capture_name} must define requiredApiRoutes")
            self.assertGreater(len(required_routes), 0, f"{capture_name} must require at least one fixture route")
            self.assertIsInstance(wait_for_texts, list, f"{capture_name} must define waitForTexts")
            self.assertGreater(len(wait_for_texts), 0, f"{capture_name} must require at least one specific wait text")

            for required_route in required_routes:
                self.assertIn(
                    required_route,
                    fixture_route_names,
                    f"{capture_name} requires fixture route '{required_route}' that is missing from web-screenshot-fixtures.json",
                )

    def test_web_screenshot_api_mocks_do_not_intercept_vite_source_modules(self) -> None:
        self.assertIn('await page.route("**/api/**"', self.web_screenshot_capture_script)
        self.assertIn('if (!pathname.startsWith("/api/"))', self.web_screenshot_capture_script)
        self.assertIn("return route.continue();", self.web_screenshot_capture_script)

    def test_web_screenshot_capture_script_enforces_route_coverage(self) -> None:
        self.assertIn(
            "assertCaptureRouteCoverage(captures, routeCatalogPath, appShellPath)",
            self.web_screenshot_capture_script,
        )
        self.assertIn("WORKSTATION_ROUTE_CATALOG", self.web_screenshot_capture_script)
        self.assertIn("screenshotCoveragePath", self.web_screenshot_capture_script)
        self.assertIn("dataSecurityMasterLegacy", self.web_screenshot_capture_script)
        self.assertIn("Web screenshot route coverage is incomplete", self.web_screenshot_capture_script)

    @staticmethod
    def screenshot_coverage_path(route_path: str) -> str:
        parsed = urlsplit(route_path)
        path = parsed.path or "/"
        if not path.startswith("/"):
            path = f"/{path}"
        return f"{path}#{parsed.fragment}" if parsed.fragment else path

    def extract_workstation_route_catalog(self) -> dict[str, str]:
        match = re.search(
            r"export const WORKSTATION_ROUTE_CATALOG = \{(?P<body>.*?)\} as const;",
            self.workstation_route_catalog,
            flags=re.DOTALL,
        )
        self.assertIsNotNone(match, "WORKSTATION_ROUTE_CATALOG block was not found")
        assert match is not None

        return {
            key: path
            for key, path in re.findall(r'\n\s*([A-Za-z0-9_]+):\s*"([^"]+)"', match.group("body"))
        }


if __name__ == "__main__":
    unittest.main()
