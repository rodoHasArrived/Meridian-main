"""Protect enabled IB reconnect execution and evidence when tests fail or runs repeat."""
import fnmatch
from pathlib import Path
import shlex
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = ROOT / ".github/workflows/ibapi-smoke.yml"
RESULTS_DIR = "artifacts/test-results/ibapi-smoke"
TRX_PREFIX = "ibapi-runtime-reconnect"


class IbApiSmokeWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # BaseLoader preserves GitHub's string key "on" instead of YAML 1.1's boolean.
        cls.workflow = yaml.load(WORKFLOW_PATH.read_text(encoding="utf-8"), Loader=yaml.BaseLoader)
        cls.job = cls.workflow["jobs"]["ibapi-smoke"]
        cls.steps = cls.job["steps"]

    def test_reconnect_slice_builds_with_stub_enabled_and_writes_trx(self):
        tests = [step for step in self.steps if step.get("run", "").startswith("dotnet test ")]
        self.assertEqual(len(tests), 1)
        step = tests[0]
        args = shlex.split(step["run"])

        self.assertEqual(args[:3], ["dotnet", "test", "tests/Meridian.Tests/Meridian.Tests.csproj"])
        self.assertIn("-p:EnableIbApiSmoke=true", args)
        self.assertEqual(args[args.index("--filter") + 1], "FullyQualifiedName~IBMarketDataClientRuntimeReconnectTests")
        self.assertEqual(args[args.index("--logger") + 1], f"trx;LogFileName={TRX_PREFIX}.trx")
        self.assertEqual(args[args.index("--results-directory") + 1], RESULTS_DIR)
        # The test assembly must be rebuilt with IBAPI_SMOKE, not reused from a default build.
        self.assertNotIn("--no-build", args)
        self.assertNotIn("if", step)
        self.assertNotIn("continue-on-error", step)
        self.assertNotIn("continue-on-error", self.job)

    def test_compile_smoke_precedes_runtime_regression(self):
        compile_step = next(step for step in self.steps if "build-ibapi-smoke.ps1" in step.get("run", ""))
        test_step = next(step for step in self.steps if step.get("run", "").startswith("dotnet test "))
        self.assertLess(self.steps.index(compile_step), self.steps.index(test_step))
        self.assertNotIn("continue-on-error", compile_step)

    def test_failed_or_empty_tests_still_require_passing_trx_evidence(self):
        test = next(step for step in self.steps if step.get("run", "").startswith("dotnet test "))
        step = next(step for step in self.steps if "validate-test-results.py" in step.get("run", ""))
        args = shlex.split(step["run"])

        self.assertLess(self.steps.index(test), self.steps.index(step))
        self.assertEqual(step["if"], "always()")
        self.assertEqual(args[:2], ["python", "build/scripts/ci/validate-test-results.py"])
        self.assertEqual(args[args.index("--results-dir") + 1], RESULTS_DIR)
        self.assertEqual(args[args.index("--require-trx-prefix") + 1], TRX_PREFIX)
        self.assertEqual(args[args.index("--output") + 1], f"{RESULTS_DIR}/test-evidence.json")
        self.assertNotIn("continue-on-error", step)

    def test_evidence_upload_runs_after_failure_and_is_unique_per_attempt(self):
        validate = next(step for step in self.steps if "validate-test-results.py" in step.get("run", ""))
        upload = next(step for step in self.steps if step.get("uses", "").startswith("actions/upload-artifact@"))

        self.assertLess(self.steps.index(validate), self.steps.index(upload))
        self.assertEqual(upload["if"], "always()")
        self.assertEqual(upload["with"]["path"].rstrip("/"), RESULTS_DIR)
        self.assertEqual(upload["with"]["if-no-files-found"], "error")
        self.assertIn("${{ github.run_number }}", upload["with"]["name"])
        self.assertIn("${{ github.run_attempt }}", upload["with"]["name"])
        self.assertNotIn("continue-on-error", upload)

    def test_pr_and_main_pushes_cover_smoke_dependencies_and_regression_tests(self):
        required_paths = [
            "src/Meridian.Infrastructure/Adapters/InteractiveBrokers/EnhancedIBConnectionManager.IBApi.cs",
            "src/Meridian.Infrastructure/Meridian.Infrastructure.csproj",
            "src/Meridian.IbApi.SmokeStub/IBApiSmokeStub.cs",
            "src/Meridian.IbApi.SmokeStub/Meridian.IbApi.SmokeStub.csproj",
            "tests/Meridian.Tests/Infrastructure/Providers/IBMarketDataClientContractTests.cs",
            "tests/Meridian.Tests/Infrastructure/Providers/IBHistoricalProviderContractTests.cs",
            "tests/Meridian.Tests/Infrastructure/Providers/IBRuntimeGuidanceTests.cs",
            "tests/Meridian.Tests/Meridian.Tests.csproj",
            "scripts/dev/build-ibapi-smoke.ps1",
            "build/scripts/ci/validate-test-results.py",
            "tests/scripts/test_ibapi_smoke_workflow.py",
            "tests/scripts/test_validate_test_results.py",
            ".github/workflows/ibapi-smoke.yml",
        ]
        self.assertEqual(self.workflow["on"]["push"]["branches"], ["main"])
        for event in ("pull_request", "push"):
            patterns = self.workflow["on"][event]["paths"]
            for path in required_paths:
                with self.subTest(event=event, path=path):
                    self.assertTrue(any(fnmatch.fnmatchcase(path, pattern) for pattern in patterns),
                                    f"{event} must run when {path} changes")


if __name__ == "__main__":
    unittest.main()
