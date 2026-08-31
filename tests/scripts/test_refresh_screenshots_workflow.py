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
        self.assertIn("run: npm ci --prefix src/Meridian.Ui/dashboard --include=optional", self.web_workflow)
        self.assertIn("cache-dependency-path: src/Meridian.Ui/dashboard/package-lock.json", self.web_workflow)
        self.assertIn("find \"$OUTPUT_DIR\" -maxdepth 1 -type f -name '*.png' -delete", self.web_workflow)
        self.assertIn("scripts/dev/validate-screenshot-captures.py", self.web_workflow)
        self.assertIn("--surface web", self.web_workflow)
        self.assertIn("--require-fresh", self.web_workflow)
        self.assertIn("pull-requests: write", self.web_workflow)
        self.assertIn("uses: peter-evans/create-pull-request@v8", self.web_workflow)
        capture_step = self.web_workflow.split("- name: Capture web screenshots", 1)[1].split(
            "- name: Validate web screenshot captures", 1
        )[0]
        self.assertNotIn("continue-on-error: true", capture_step)
        self.assertIn("continue-on-error: true", self.web_workflow)
        self.assertIn("branch: automation/web-screenshot-capture", self.web_workflow)
        self.assertIn("base: ${{ github.event.repository.default_branch }}", self.web_workflow)
        self.assertIn("title: \"chore: refresh web workstation screenshot catalog\"", self.web_workflow)
        # The clean, lockfile-pinned install must not regress back to a mutable
        # `npm install`, which can silently drift the dependency tree in CI.
        self.assertNotIn("npm install --prefix", self.web_workflow)
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
            "accountingTrialBalanceLegacy",
            "dataAlertsLegacy",
            "dataSecurityMasterLegacy",
            "dataWatchlistLegacy",
            "settingsIntegrations",
            "settingsFeatureCoverage",
            "settingsAlpacaProviderSetup",
            "settingsBackendCapabilityCoverage",
            "settingsDiagnosticEndpoints",
            "strategyFormulaWorkbenchLegacy",
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
            "/accounting/trial-balance",
            "/data/alerts",
            "/data/security-master",
            "/data/security-master/*",
            "/data/watchlist",
            "/overview/*",
            "/research/*",
            "/data-operations/*",
            "/governance/*",
            "/strategy/formula-workbench",
        }
        explicit_pages = {
            self.screenshot_coverage_path(path)
            for path in route_paths
            if "*" not in path and path not in compatibility_redirect_routes
        }

        missing_paths = sorted(explicit_pages - captured_paths)
        self.assertEqual([], missing_paths, f"Missing explicit app route screenshots: {missing_paths}")

    def test_first_run_activation_gate_passes_during_capture(self) -> None:
        """The app shell blocks every route behind /api/workstation/first-run/ until it
        reports a completed activation. Without these fixtures the Playwright mock answers
        404, the shell renders the activation gate instead of .workstation-frame, and every
        capture times out (two 120s waits per route until the job ceiling)."""
        fixture_routes = self.web_screenshot_fixtures.get("routes", {})

        self.assertIn("/api/workstation/first-run/", fixture_routes)
        first_run = fixture_routes["/api/workstation/first-run/"]
        self.assertIs(True, first_run.get("isComplete"))
        self.assertFalse(first_run.get("workspace", {}).get("isSample"))

        self.assertIn("/api/demo/mode", fixture_routes)
        self.assertIn("enabled", fixture_routes["/api/demo/mode"])

    def test_capture_script_fails_fast_on_systemic_shell_mount_failures(self) -> None:
        """A shell that never mounts fails every route at the full navigation timeout
        (twice per route) until the CI job ceiling cancels the run. The capture script
        must label the frame-mount timeout and abort early when the failure is systemic."""
        self.assertIn("App shell failed to mount", self.web_screenshot_capture_script)
        self.assertIn("shellMountFailureLimit", self.web_screenshot_capture_script)
        self.assertIn("Aborting capture run", self.web_screenshot_capture_script)

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

    def test_asset_detail_capture_auto_opens_an_exact_fixture_match(self) -> None:
        captures = self.web_screenshot_routes.get("captures", [])
        asset_capture = next(
            (capture for capture in captures if capture.get("id") == "W02D"),
            None,
        )

        self.assertIsNotNone(asset_capture)
        assert asset_capture is not None
        self.assertEqual("/portfolio/asset-detail?symbol=AAPL", asset_capture.get("path"))

        fixture_routes = self.web_screenshot_fixtures.get("routes", {})
        for fixture_path in (
            "/api/workstation/security-master/securities",
            "/api/workstation/security-master/securities/sec-dev-001",
            "/api/workstation/security-master/securities/sec-dev-001/identity",
            "/api/security-master/sec-dev-001/corporate-actions",
            "/api/security-master/sec-dev-001/trading-parameters",
        ):
            self.assertIn(fixture_path, fixture_routes)
            self.assertIn(fixture_path, asset_capture.get("requiredApiRoutes", []))

        self.assertEqual(
            "sec-dev-001",
            fixture_routes["/api/workstation/security-master/securities"][0]["securityId"],
        )
        self.assertEqual(
            "AAPL",
            fixture_routes["/api/workstation/security-master/securities"][0]["classification"]["primaryIdentifierValue"],
        )

        accounting_asset_capture = next(
            (capture for capture in captures if capture.get("id") == "W03B1"),
            None,
        )
        self.assertIsNotNone(accounting_asset_capture)
        assert accounting_asset_capture is not None
        self.assertEqual(
            "/accounting/security-master/detail?symbol=AAPL",
            accounting_asset_capture.get("path"),
        )
        self.assertTrue(
            set(asset_capture.get("requiredApiRoutes", [])).issubset(
                set(accounting_asset_capture.get("requiredApiRoutes", []))
            )
        )

    def test_accounting_journal_and_capital_captures_use_workbench_fixtures(self) -> None:
        captures = {
            capture.get("id"): capture
            for capture in self.web_screenshot_routes.get("captures", [])
        }
        fixture_routes = self.web_screenshot_fixtures.get("routes", {})
        journal_fixture_path = "/api/ledger/journal-entry-workbench"
        capital_fixture_path = "/api/ledger/private-capital/capital-account-workbench"

        self.assertIn(journal_fixture_path, fixture_routes)
        self.assertIn(capital_fixture_path, fixture_routes)
        self.assertIn(journal_fixture_path, captures["W03J"].get("requiredApiRoutes", []))
        self.assertIn(journal_fixture_path, captures["W03J1"].get("requiredApiRoutes", []))
        self.assertIn(capital_fixture_path, captures["W03K"].get("requiredApiRoutes", []))
        self.assertEqual(
            "/accounting/journal-entries/detail?journalEntryId=manual-je-fixture",
            captures["W03J1"].get("path"),
        )
        self.assertEqual(
            "manual-je-fixture",
            fixture_routes[journal_fixture_path]["drafts"][0]["journalEntryId"],
        )
        self.assertEqual(1, fixture_routes[capital_fixture_path]["investorAccountCount"])
        self.assertEqual(1, len(fixture_routes[capital_fixture_path]["investorAccounts"]))

    def test_accounting_detail_and_close_captures_use_typed_fixtures(self) -> None:
        captures = {
            capture.get("id"): capture
            for capture in self.web_screenshot_routes.get("captures", [])
        }
        fixture_routes = self.web_screenshot_fixtures.get("routes", {})
        trial_balance_path = "/api/workstation/runs/run-42/ledger/trial-balance"
        close_calendar_path = "/api/workstation/operations/continuity/close-calendar"

        self.assertIn(trial_balance_path, fixture_routes)
        self.assertIn(close_calendar_path, fixture_routes)
        self.assertIn(trial_balance_path, captures["W03I1"].get("requiredApiRoutes", []))
        self.assertIn(close_calendar_path, captures["W03I3"].get("requiredApiRoutes", []))
        self.assertEqual("acct-cash", fixture_routes[trial_balance_path][0]["financialAccountId"])
        self.assertEqual("workflow-close-1", fixture_routes[close_calendar_path]["items"][0]["workflowId"])
        self.assertEqual(
            "Pending review",
            fixture_routes["/api/workstation/accounting"]["closePlans"][0]["approvals"][0]["status"],
        )
        self.assertIn("Open source journal entry", captures["W03I1"].get("waitForTexts", []))
        self.assertIn("Open Operations Continuity", captures["W03I3"].get("waitForTexts", []))
        self.assertEqual(
            "Month-end investment income adjustment",
            captures["W03J1"].get("waitForText"),
        )
        self.assertIn("Retained posting evidence 1", captures["W03J1"].get("waitForTexts", []))
        self.assertIn("Evidence Workbench", captures["W03I5"].get("waitForTexts", []))
        self.assertIn("Document intake", captures["W03I5"].get("waitForTexts", []))

    def test_accounting_capture_contracts_use_current_operator_labels(self) -> None:
        captures = {
            capture.get("id"): capture
            for capture in self.web_screenshot_routes.get("captures", [])
        }

        self.assertEqual("Close Cockpit", captures["W03"].get("waitForText"))
        self.assertEqual("Reconciliation Casework", captures["W03A"].get("waitForText"))
        self.assertEqual("Approval queue and audit gate", captures["W03C"].get("waitForText"))
        self.assertEqual("External GL Reconciliation", captures["W03D"].get("waitForText"))
        self.assertIn("Read-only import ready", captures["W03D"].get("waitForTexts", []))
        self.assertEqual("Configure", captures["W03F"].get("waitForText"))
        self.assertEqual(["Activation checklist"], captures["W03F"].get("waitForTexts"))
        readiness_path = "/api/accounting-system/production-readiness"
        self.assertIn(readiness_path, captures["W03F"].get("requiredApiRoutes", []))
        self.assertEqual("Ready", self.web_screenshot_fixtures["routes"][readiness_path]["status"])
        self.assertEqual(100, self.web_screenshot_fixtures["routes"][readiness_path]["score"])

    def test_reporting_capture_contracts_use_visible_governed_states(self) -> None:
        captures = {
            capture.get("id"): capture
            for capture in self.web_screenshot_routes.get("captures", [])
        }

        self.assertEqual("Report Packs", captures["W04A"].get("waitForText"))
        self.assertIn("Governed export queue", captures["W04C"].get("waitForTexts", []))
        self.assertNotIn("Run a governed report now", captures["W04C"].get("waitForTexts", []))
        self.assertIn("Review approval details", captures["W04E"].get("waitForTexts", []))
        self.assertNotIn("report-run-board-202605", captures["W04E"].get("waitForTexts", []))

        # The run-detail capture renders the governed screen, which loads its run from the
        # authoritative reporting endpoints instead of the workstation reporting read model.
        self.assertIn("Governed lifecycle", captures["W04I2"].get("waitForTexts", []))
        self.assertIn("Immutable release artifacts", captures["W04I2"].get("waitForTexts", []))
        self.assertIn(
            "Governed report run unavailable",
            captures["W04I2"].get("waitForAbsentTexts", []),
        )

        governed_run_path = "/api/fund-structure/reporting/runs/report-run-investor-202605"
        self.assertIn(governed_run_path, captures["W04I2"].get("requiredApiRoutes", []))
        self.assertEqual(
            "Released",
            self.web_screenshot_fixtures["routes"][governed_run_path]["governanceState"],
        )

    def test_data_import_and_settings_captures_use_canonical_task_routes(self) -> None:
        captures = {
            capture.get("id"): capture
            for capture in self.web_screenshot_routes.get("captures", [])
        }
        captured_paths = {
            capture.get("path")
            for capture in self.web_screenshot_routes.get("captures", [])
        }

        # 77 after retiring W05E (web-strategy-formula-workbench): the Quant Lab Formulas tab is
        # withdrawn while /strategy/quant-lab?view=formulas stays in UNWIRED_WORKSTATION_ROUTES, so
        # the capture navigated to a route the screen now canonicalizes away and waited for text
        # that no longer renders.
        # 78 after adding W03O (web-accounting-capital-calls) for the new
        # /accounting/capital-calls issuance screen.
        # 80 after adding W02F (web-portfolio-loan-book) for the new /portfolio/loan-book
        # screen, which gives the browser the direct-lending read surface that previously
        # only the desktop client reached.
        self.assertEqual(80, len(captures))
        self.assertNotIn("W05E", captures)
        self.assertEqual("/data/import", captures["W06I"].get("path"))
        self.assertEqual("/data/operations", captures["W06J"].get("path"))
        self.assertEqual("/data/assurance", captures["W06K"].get("path"))
        self.assertIn("Governed file import", captures["W06I"].get("waitForTexts", []))
        self.assertIn("Selected Backfill", captures["W06C"].get("waitForTexts", []))
        self.assertEqual("/settings/accounting-systems", captures["W07B"].get("path"))
        self.assertEqual("/settings/providers/alpaca/setup", captures["W07C"].get("path"))
        self.assertEqual("/settings/diagnostics/advanced", captures["W07D"].get("path"))
        self.assertEqual(
            "/settings/diagnostics/advanced#diagnostic-endpoints",
            captures["W07E"].get("path"),
        )
        self.assertEqual(
            "/settings/diagnostics/advanced#runtime-feature-capabilities",
            captures["W07I"].get("path"),
        )
        self.assertEqual("/settings/providers/alpaca/advanced", captures["W07J"].get("path"))
        self.assertEqual([], captures["W07E"].get("waitForAbsentTexts", []))
        self.assertEqual([], captures["W07I"].get("waitForAbsentTexts", []))
        self.assertTrue({"W07B1", "W07C0", "W07D0"}.isdisjoint(captures))

        legacy_paths = {
            "/settings/integrations",
            "/settings#alpaca-provider-setup",
            "/settings#backend-capability-coverage",
            "/settings#diagnostic-endpoints",
            "/settings/feature-coverage",
        }
        self.assertTrue(legacy_paths.isdisjoint(captured_paths))

    def test_data_capture_uses_fixture_owned_import_and_diagnostics(self) -> None:
        captures = {
            capture.get("id"): capture
            for capture in self.web_screenshot_routes.get("captures", [])
        }
        fixture_routes = self.web_screenshot_fixtures.get("routes", {})
        data_fixture = fixture_routes["/api/workstation/data"]
        upload_catalog = data_fixture.get("uploadTemplates", {})

        self.assertEqual([".csv"], upload_catalog.get("acceptedFileExtensions"))
        self.assertEqual(
            "broker-execution-import",
            upload_catalog.get("templates", [])[0].get("templateId"),
        )
        self.assertIn("Broker execution import", captures["W06I"].get("waitForTexts", []))

        diagnostic_routes = {
            "/api/quality/dashboard",
            "/api/providers/capability-matrix",
            "/api/security-master/corporate-actions/inbox",
            "/api/security-master/quality-report/latest",
        }
        self.assertTrue(diagnostic_routes.issubset(fixture_routes))
        self.assertTrue(diagnostic_routes.issubset(captures["W06"].get("requiredApiRoutes", [])))
        self.assertEqual([], fixture_routes["/api/providers/capability-matrix"]["discoveryFailures"])
        self.assertEqual([], fixture_routes["/api/security-master/corporate-actions/inbox"]["errors"])
        self.assertEqual(0, fixture_routes["/api/security-master/quality-report/latest"]["violationCount"])

        provider_connections = fixture_routes["/api/providers/connections"]
        alpaca = next(connection for connection in provider_connections if connection.get("providerId") == "alpaca")
        self.assertEqual(
            "/workstation/settings/providers/alpaca/setup",
            alpaca.get("actionHref"),
        )
        self.assertEqual("Verified", alpaca.get("verificationState"))
        self.assertEqual("LocalEncryptedStore", alpaca.get("credentialSource"))
        self.assertTrue(str(alpaca.get("lastVerifiedAt", "")).startswith("2026-07-13"))

        routing_trust = fixture_routes["/api/provider-routing/trust-snapshots"][0]
        self.assertEqual(0.92, routing_trust.get("score"))
        self.assertTrue(routing_trust.get("isCertificationFresh"))
        self.assertTrue(str(fixture_routes["/api/status"].get("lastHeartbeatUtc", "")).startswith("2026-07-13"))

    def test_reporting_capture_fixture_has_coherent_readiness_schedule_and_scope(self) -> None:
        fixture_routes = self.web_screenshot_fixtures.get("routes", {})
        reporting_fixture = fixture_routes["/api/workstation/reporting"]
        reporting_summary = reporting_fixture["reporting"]

        self.assertEqual(1, reporting_fixture["reconciliationQueue"][0]["openBreakCount"])
        self.assertEqual(1, len(reporting_fixture["breakQueue"]))
        self.assertEqual("Active", reporting_summary["schedules"][0]["state"])
        self.assertEqual(1, reporting_summary["accessAudit"]["visibleScheduleCount"])
        self.assertEqual([], reporting_summary["accessAudit"]["denialReasons"])

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

    def test_web_screenshot_routes_do_not_reuse_an_unqualified_route_state(self) -> None:
        captures_by_path: dict[str, list[dict]] = {}
        for capture in self.web_screenshot_routes.get("captures", []):
            route_path = str(capture.get("path", "")).strip()
            if route_path:
                captures_by_path.setdefault(route_path, []).append(capture)

        ambiguous_paths: list[str] = []
        for route_path, captures in captures_by_path.items():
            if len(captures) < 2:
                continue

            variants = [str(capture.get("variant", "")).strip() for capture in captures]
            if not all(variants) or len(set(variants)) != len(variants):
                ambiguous_paths.append(route_path)

        self.assertEqual(
            [],
            sorted(ambiguous_paths),
            "Duplicate screenshot paths must declare distinct non-empty variant values",
        )

    def test_web_screenshot_api_mocks_do_not_intercept_vite_source_modules(self) -> None:
        self.assertIn('await page.route("**/api/**"', self.web_screenshot_capture_script)
        self.assertIn('if (!pathname.startsWith("/api/"))', self.web_screenshot_capture_script)
        self.assertIn("return route.continue();", self.web_screenshot_capture_script)

    def test_web_screenshot_capture_script_enforces_route_coverage(self) -> None:
        self.assertIn(
            "findMissingCaptureRoutePaths(allCaptures, routeCatalogPath, appShellPath)",
            self.web_screenshot_capture_script,
        )
        self.assertIn("WORKSTATION_ROUTE_CATALOG", self.web_screenshot_capture_script)
        self.assertIn("screenshotCoveragePath", self.web_screenshot_capture_script)
        for compatibility_route_key in (
            "dataSecurityMasterLegacy",
            "settingsIntegrations",
            "settingsFeatureCoverage",
            "settingsAlpacaProviderSetup",
            "settingsBackendCapabilityCoverage",
            "settingsDiagnosticEndpoints",
        ):
            self.assertIn(compatibility_route_key, self.web_screenshot_capture_script)
        # Coverage gaps are still enforced, but reported at the end of the run as a
        # non-zero failure instead of aborting before any screenshot is captured.
        self.assertIn("Live route(s) without a screenshot capture entry", self.web_screenshot_capture_script)
        self.assertIn("assertCaptureRouteStateIdentity(allCaptures)", self.web_screenshot_capture_script)
        self.assertIn("must use a unique route state", self.web_screenshot_capture_script)

    def test_web_screenshot_capture_script_excludes_all_compatibility_redirect_routes(self) -> None:
        # The JS coverage-exclusion lists must match the compatibility redirect
        # routes the coverage tests exclude below; otherwise the capture run fails
        # with "Live route(s) without a screenshot capture entry" for routes that
        # intentionally forward to an already-captured canonical screen.
        for compatibility_route_key in (
            "accountingTrialBalanceLegacy",
            "dataAlertsLegacy",
            "dataWatchlistLegacy",
            "strategyFormulaWorkbenchLegacy",
        ):
            self.assertIn(compatibility_route_key, self.web_screenshot_capture_script)
        for compatibility_app_route in (
            '"/accounting/trial-balance"',
            '"/data/alerts"',
            '"/data/watchlist"',
            '"/strategy/formula-workbench"',
        ):
            self.assertIn(compatibility_app_route, self.web_screenshot_capture_script)

    def test_web_screenshot_capture_script_honors_declared_route_redirects(self) -> None:
        # Captures that intentionally redirect to a canonical screen assert the
        # redirect destination instead of failing the strict path-identity check.
        self.assertIn("capture.expectedRedirectPath", self.web_screenshot_capture_script)
        self.assertIn(
            "new URL(toRouteUrl(baseUrl, capture.expectedRedirectPath.trim())).pathname",
            self.web_screenshot_capture_script,
        )

    def test_web_screenshot_evidence_captures_declare_expected_redirect(self) -> None:
        # The accounting/data evidence entry points are compatibility routes that
        # redirect to the reporting evidence workbench; declaring the redirect lets
        # each capture assert the intended destination and still produce an
        # entry-point-specific screenshot.
        captures = {
            capture.get("id"): capture
            for capture in self.web_screenshot_routes.get("captures", [])
        }
        for capture_id in ("W03I5", "W03L", "W06F"):
            self.assertEqual(
                "/reporting/evidence",
                captures[capture_id].get("expectedRedirectPath"),
                f"{capture_id} must declare its redirect to the reporting evidence workbench",
            )

    def test_web_screenshot_capture_script_isolates_per_route_failures(self) -> None:
        # A screen that fails to render is recorded and skipped so the run continues
        # through the remaining screens, then exits non-zero with a consolidated
        # summary. One broken screen must never abort the whole catalog.
        self.assertIn("::warning::Skipped ", self.web_screenshot_capture_script)
        self.assertIn("did not render correctly and were skipped", self.web_screenshot_capture_script)
        self.assertIn("Screenshot capture summary:", self.web_screenshot_capture_script)
        self.assertIn("route(s) failed to render", self.web_screenshot_capture_script)
        self.assertIn("readiness-timeout-ms", self.web_screenshot_capture_script)

        capture_loop = self.web_screenshot_capture_script.split("for (const capture of captures)", 1)[1]
        capture_loop = capture_loop.split("const proxyErrors = logs.filter", 1)[0]
        self.assertNotIn("throw error;", capture_loop)

    def test_web_screenshot_workflow_uploads_partial_catalog_on_failure(self) -> None:
        upload_step = self.web_workflow.split("- name: Upload screenshot artifacts", 1)[1].split(
            "- name:", 1
        )[0]
        self.assertIn("if: ${{ always() }}", upload_step)
        self.assertIn("if-no-files-found: ignore", upload_step)

    def test_web_screenshot_workflow_uploads_capture_manifest_for_diagnostics(self) -> None:
        # The run manifest (per-route status, render errors, dev-server log tail)
        # must be uploaded on every run so a failed capture can be diagnosed
        # without re-running the workflow.
        manifest_step = self.web_workflow.split("- name: Upload capture manifest", 1)
        self.assertEqual(2, len(manifest_step), "Upload capture manifest step is missing")
        manifest_step = manifest_step[1].split("- name:", 1)[0]
        self.assertIn("if: ${{ always() }}", manifest_step)
        self.assertIn("artifacts/web-screenshots/manifest.json", manifest_step)
        self.assertIn("if-no-files-found: warn", manifest_step)

    def test_web_screenshot_capture_script_retries_transient_route_failures(self) -> None:
        # A single flaky render must not fail the whole catalog: each route is
        # retried a bounded number of times (fresh context per attempt) before
        # it is recorded as failed.
        self.assertIn("captureRouteWithRetries(", self.web_screenshot_capture_script)
        self.assertIn('values.get("capture-retries")', self.web_screenshot_capture_script)
        self.assertIn("const captureAttempts = captureRetries + 1;", self.web_screenshot_capture_script)
        self.assertIn("maxAttemptsPerCapture", self.web_screenshot_capture_script)
        self.assertIn("attempt ${attempt}/${maxAttempts} failed", self.web_screenshot_capture_script)
        # The retry helper must open a fresh context per attempt.
        self.assertIn("result.attempts = attempt;", self.web_screenshot_capture_script)

    def test_web_screenshot_capture_script_fails_fast_when_dev_server_exits(self) -> None:
        # If the Vite dev server dies during startup, fail immediately with the
        # log tail instead of polling for the full navigation timeout.
        self.assertIn("Dev server exited before becoming ready", self.web_screenshot_capture_script)
        self.assertIn(
            "await waitForServer(`${normalizeBaseUrl(baseUrl)}/`, timeoutMs, server, logs);",
            self.web_screenshot_capture_script,
        )

    def test_web_screenshot_capture_script_hardens_chromium_launch(self) -> None:
        # Full-page screenshots of large routes can crash Chromium on a small
        # /dev/shm; disable the shared-memory transport in CI containers.
        self.assertIn("--disable-dev-shm-usage", self.web_screenshot_capture_script)

    def test_web_screenshot_capture_script_supports_targeted_capture_filters(self) -> None:
        self.assertIn('valueLists.get("capture")', self.web_screenshot_capture_script)
        self.assertIn('valueLists.get("capture-id")', self.web_screenshot_capture_script)
        self.assertIn('valueLists.get("capture-name")', self.web_screenshot_capture_script)
        self.assertIn("selectCaptures(allCaptures, captureSelectors)", self.web_screenshot_capture_script)
        self.assertIn("selectedCaptureCount", self.web_screenshot_capture_script)
        self.assertIn("totalCaptureCount", self.web_screenshot_capture_script)

    def test_web_screenshot_capture_script_fails_fast_on_browser_render_errors(self) -> None:
        self.assertIn("createPageErrorTracker(page)", self.web_screenshot_capture_script)
        self.assertIn("waitForCaptureStep", self.web_screenshot_capture_script)
        self.assertIn("Maximum update depth exceeded", self.web_screenshot_capture_script)
        self.assertIn("Meridian workstation route failed to render", self.web_screenshot_capture_script)
        self.assertIn("hit a browser render error", self.web_screenshot_capture_script)
        self.assertIn("error.stack ?? error.message", self.web_screenshot_capture_script)

    def test_web_screenshot_capture_waits_for_transitional_states_to_clear(self) -> None:
        self.assertIn("collectCaptureWaitForAbsentTexts(capture)", self.web_screenshot_capture_script)
        self.assertIn("waiting for transitional text", self.web_screenshot_capture_script)

        captures = {
            capture.get("id"): capture
            for capture in self.web_screenshot_routes.get("captures", [])
        }
        expected_absent_text = {
            "W01C": "Refreshing risk controls",
            "W01D": "Awaiting readiness payload",
            "W05C": "Scanning source for runtime parameters",
            "W06A": "Loading symbols…",
        }
        for capture_id, text in expected_absent_text.items():
            self.assertIn(text, captures[capture_id].get("waitForAbsentTexts", []))

        fixture_routes = self.web_screenshot_fixtures.get("routes", {})
        for fixture_path in (
            "/api/risk/rules",
            "/api/risk/rules/DrawdownCircuitBreaker/config",
        ):
            self.assertIn(fixture_path, fixture_routes)
            self.assertIn(fixture_path, captures["W01C"].get("requiredApiRoutes", []))

    def test_web_screenshot_capture_script_disables_vite_hmr(self) -> None:
        self.assertIn('MERIDIAN_SCREENSHOT_CAPTURE: "true"', self.web_screenshot_capture_script)

    def test_web_screenshot_capture_script_isolates_browser_context_per_capture(self) -> None:
        # The app shell persists workflow-continuity, activity, and focus state
        # across navigations, so captures sharing one browser context would
        # depend on visit order instead of showing each route's first-load state.
        self.assertIn("const context = await browser.newContext();", self.web_screenshot_capture_script)
        self.assertIn("await context.close();", self.web_screenshot_capture_script)
        self.assertNotIn("const page = await browser.newPage();", self.web_screenshot_capture_script)

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
