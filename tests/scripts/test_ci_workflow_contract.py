from __future__ import annotations

import unittest

import yaml
from pathlib import Path

from tests.scripts.workflow_assertions import assert_pinned_action


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "ci.yml"


class CiWorkflowContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def _job_block(self, job_name: str) -> str:
        start = self.workflow.index(f"  {job_name}:")
        next_job = self.workflow.find("\n  ", start + 3)
        while next_job != -1 and self.workflow[next_job + 3 : next_job + 4].isspace():
            next_job = self.workflow.find("\n  ", next_job + 1)
        return self.workflow[start : next_job if next_job != -1 else None]

    def test_legacy_checks_have_single_canonical_owner(self) -> None:
        for job_name in ("dotnet", "browser-workstation", "source-doc-determinism"):
            self.assertNotIn(f"  {job_name}:", self.workflow)
        script = (REPO_ROOT / "scripts/ci.sh").read_text(encoding="utf-8")
        for check in ("Validate AI contract drift", "Validate provider-validation script tests",
                      "Validate AI navigation freshness", "Validate AI handoff checklist schema",
                      "Enforce mode escalation policy", "Enforce validation-floor guard",
                      "Validate workstation bundle", "Generate maintained documentation"):
            self.assertIn(check, script)
        catalog = (REPO_ROOT / "build/scripts/run-maintained-generation.py").read_text(encoding="utf-8")
        self.assertIn("build/scripts/run-maintained-generation.py", script)
        for check in ("Validate roadmap registry", "Validate source READMEs", "Scan source TODOs"):
            self.assertIn(check, catalog)
        self.assertIn("--collect:\"XPlat Code Coverage\"", self.workflow)
        self.assertIn("  merge_group:", self.workflow)

    def test_secret_scan_remains_pull_request_visible(self) -> None:
        secret_block = self._job_block("secret-scan")

        self.assertNotIn("if: github.event_name != 'pull_request'", secret_block)
        gitleaks = assert_pinned_action(self, self.workflow, "secret-scan", "gitleaks/gitleaks-action")
        self.assertEqual(gitleaks.get("env", {}).get("GITLEAKS_VERSION"), "8.25.1")
        self.assertEqual(gitleaks.get("env", {}).get("GITLEAKS_CONFIG"), ".gitleaks.toml")
        checkout = assert_pinned_action(self, self.workflow, "secret-scan", "actions/checkout")
        self.assertEqual(checkout.get("with", {}).get("persist-credentials"), "false")
        self.assertEqual(checkout.get("with", {}).get("fetch-depth"), "0")

    def test_full_coverage_builds_checkout_assets_before_dotnet_evaluation(self) -> None:
        document = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        steps = document["jobs"]["verify-full"]["steps"]
        node = assert_pinned_action(self, self.workflow, "verify-full", "actions/setup-node")
        python = assert_pinned_action(self, self.workflow, "verify-full", "actions/setup-python")
        self.assertEqual(node["with"]["node-version-file"], ".nvmrc")
        self.assertEqual(python["with"]["python-version"], "3.12")
        prepare = next(step for step in steps
                       if step.get("run") == "python build/scripts/ensure-workstation-assets.py")
        restore = next(step for step in steps if "dotnet restore Meridian.sln" in step.get("run", ""))
        tests = next(step for step in steps if "dotnet test Meridian.sln" in step.get("run", ""))
        self.assertLess(steps.index(node), steps.index(prepare))
        self.assertLess(steps.index(python), steps.index(prepare))
        self.assertLess(steps.index(prepare), steps.index(restore))
        self.assertLess(steps.index(restore), steps.index(tests))
        self.assertNotIn("continue-on-error", prepare)
        self.assertFalse(any(step.get("uses", "").startswith("actions/download-artifact@") for step in steps),
                         "Coverage must build assets from its own checkout, without a cross-run bundle")

    def test_dotnet_concurrency_benchmark_prepares_assets_outside_measurement(self) -> None:
        workflow = (REPO_ROOT / ".github/workflows/ci-concurrency-benchmark.yml").read_text(encoding="utf-8")
        steps = yaml.load(workflow, Loader=yaml.BaseLoader)["jobs"]["pairs"]["steps"]
        node = assert_pinned_action(self, workflow, "pairs", "actions/setup-node")
        python = assert_pinned_action(self, workflow, "pairs", "actions/setup-python")
        self.assertNotIn("if", node, "Both measured subjects need Node after bundle untracking")
        self.assertEqual(node["with"]["node-version"], "24")
        self.assertEqual(python["with"]["python-version"], "3.12")
        prepare = next(step for step in steps
                       if step.get("run") == "python build/scripts/ensure-workstation-assets.py")
        restore = next(step for step in steps if "dotnet restore Meridian.sln" in step.get("run", ""))
        measure = next(step for step in steps if "benchmark-ci.py" in step.get("run", ""))
        self.assertEqual(prepare["if"], "matrix.subject == 'dotnet'")
        self.assertLess(steps.index(node), steps.index(prepare))
        self.assertLess(steps.index(python), steps.index(prepare))
        self.assertLess(steps.index(prepare), steps.index(restore))
        self.assertLess(steps.index(restore), steps.index(measure))
        self.assertNotIn("continue-on-error", prepare)

    def test_local_orchestrator_isolates_outputs_and_preserves_hosted_leaf_defaults(self) -> None:
        script = (REPO_ROOT / "scripts/ci.sh").read_text(encoding="utf-8")
        self.assertIn('MERIDIAN_CI_ARTIFACT_ROOT="${MERIDIAN_CI_ARTIFACT_ROOT:-artifacts}"', script)
        self.assertIn('exec "$python_cmd" build/scripts/ci/local_gate.py', script)
        self.assertIn('--isolation-key "$MERIDIAN_CI_ISOLATION_KEY"', script)
        self.assertIn("build/scripts/ensure-workstation-assets.py", script)
        browser = script.split("verify_browser() {", 1)[1].split("verify_docs() {", 1)[0]
        self.assertLess(browser.index("Run dashboard tests"), browser.index("Build dashboard bundle"))
        self.assertLess(browser.index("Build dashboard bundle"), browser.index("Validate workstation bundle"))


if __name__ == "__main__":
    unittest.main()
