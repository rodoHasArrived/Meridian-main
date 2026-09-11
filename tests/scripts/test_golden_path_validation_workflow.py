import os
import re
import subprocess
import sys
import textwrap
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "golden-path-validation.yml"


class GoldenPathValidationWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def job(self, job_id: str) -> str:
        match = re.search(
            rf"^  {re.escape(job_id)}:\n(?P<body>.*?)(?=^  [\w-]+:\n|\Z)",
            self.workflow,
            re.MULTILINE | re.DOTALL,
        )
        self.assertIsNotNone(match, f"Missing workflow job {job_id}")
        return match.group("body")

    def gate_script(self) -> str:
        gate = self.job("pilot-acceptance")
        self.assertIn("        shell: python\n", gate)
        match = re.search(r"^        run: \|\n((?:^          .*\n|^\n)+)", gate, re.MULTILINE)
        self.assertIsNotNone(match, "Missing executable acceptance gate")
        return textwrap.dedent(match.group(1))

    def test_workflow_runs_pilot_acceptance_harness(self) -> None:
        self.assertIn("Run pilot acceptance harness", self.workflow)
        self.assertIn("FullyQualifiedName~PilotAcceptanceHarnessTests", self.workflow)
        self.assertIn("tests/Meridian.Tests/Integration/EndpointTests/PilotAcceptanceHarnessTests.cs", self.workflow)

    def test_workflow_uses_current_dotnet_sdk(self) -> None:
        self.assertIn("DOTNET_VERSION: '10.0.x'", self.workflow)
        self.assertNotIn("DOTNET_VERSION: '9.0.x'", self.workflow)

    def test_workflow_publishes_pilot_readiness_artifact(self) -> None:
        self.assertIn("generate-pilot-readiness-dashboard.py", self.workflow)
        self.assertIn("artifacts/pilot-acceptance/latest/pilot-readiness.md", self.workflow)
        self.assertIn("artifacts/pilot-acceptance/latest/pilot-readiness-dashboard.md", self.workflow)
        self.assertIn("name: pilot-acceptance-evidence", self.workflow)

    def test_workflow_validates_pilot_readiness_dashboard_renderer(self) -> None:
        self.assertIn("Validate pilot readiness dashboard renderer", self.workflow)
        self.assertIn(
            "python3 -m unittest build/scripts/docs/tests/test_pilot_readiness_dashboard.py",
            self.workflow,
        )

    def test_all_evidence_jobs_can_start_in_parallel(self) -> None:
        for job_id in ["browser-w4-parity", "wpf-w4-acceptance", "pilot-harness"]:
            with self.subTest(job=job_id):
                header = self.job(job_id).split("    steps:\n", 1)[0]
                self.assertNotRegex(header, r"(?m)^    (needs|if):")
        self.assertIn("    timeout-minutes: 35\n", self.job("pilot-harness"))
        self.assertNotIn("continue-on-error:", self.workflow)

    def test_parallel_jobs_keep_all_acceptance_checks(self) -> None:
        expected_commands = {
            "browser-w4-parity": [
                "npm --prefix src/Meridian.Ui/dashboard run test:w4",
            ],
            "wpf-w4-acceptance": [
                'dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter "Category=W4Acceptance" --logger "trx;LogFilePrefix=wpf-w4-acceptance" --results-directory artifacts/test-results/wpf-w4-acceptance /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true',
            ],
            "pilot-harness": [
                'dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PilotAcceptanceHarnessTests" --logger "trx;LogFilePrefix=pilot-acceptance" --results-directory artifacts/test-results/pilot-acceptance /p:EnableWindowsTargeting=true',
                "python3 -m unittest build/scripts/docs/tests/test_pilot_readiness_dashboard.py",
                "python3 -m unittest tests/scripts/test_generate_dk1_pilot_parity_packet.py",
                "python3 -m unittest tests/scripts/test_prepare_dk1_operator_signoff.py",
                "python3 build/scripts/docs/generate-pilot-readiness-dashboard.py --output artifacts/pilot-acceptance/latest/pilot-readiness-dashboard.md --json-output artifacts/pilot-acceptance/latest/pilot-readiness-dashboard.json",
            ],
        }
        for job_id, commands in expected_commands.items():
            for command in commands:
                with self.subTest(job=job_id, command=command):
                    self.assertIn(command, self.job(job_id))

    def test_acceptance_gate_preserves_stable_check_and_requires_every_job(self) -> None:
        gate = self.job("pilot-acceptance")
        self.assertIn("    name: Pilot Acceptance Evidence\n", gate)
        self.assertIn("    if: always()\n", gate)
        self.assertIn("    timeout-minutes: 5\n", gate)
        needs = re.search(r"^    needs:\n((?:^      - .*\n)+)", gate, re.MULTILINE)
        self.assertIsNotNone(needs)
        self.assertEqual(
            re.findall(r"^      - (.*)$", needs.group(1), re.MULTILINE),
            ["browser-w4-parity", "wpf-w4-acceptance", "pilot-harness"],
        )
        for variable, job_id in {
            "BROWSER_RESULT": "browser-w4-parity",
            "WPF_RESULT": "wpf-w4-acceptance",
            "PILOT_RESULT": "pilot-harness",
        }.items():
            self.assertIn(f"{variable}: ${{{{ needs.{job_id}.result }}}}", gate)
        self.assertNotIn("actions/checkout", gate)
        self.assertNotIn("dotnet ", gate)

    def test_acceptance_gate_passes_only_when_all_evidence_succeeds(self) -> None:
        script = self.gate_script()
        success = dict.fromkeys(["BROWSER_RESULT", "WPF_RESULT", "PILOT_RESULT"], "success")
        cases = [("all succeeded", success, 0)]
        for variable in success:
            for result in ["failure", "cancelled", "skipped", "unknown", ""]:
                cases.append((f"{variable}={result!r}", {**success, variable: result}, 1))
            missing = {key: value for key, value in success.items() if key != variable}
            cases.append((f"{variable} missing", missing, 1))
        base_environment = {key: value for key, value in os.environ.items() if key not in success}
        for label, results, expected_exit in cases:
            with self.subTest(case=label):
                completed = subprocess.run(
                    [sys.executable, "-c", script],
                    env={**base_environment, **results},
                    capture_output=True,
                    text=True,
                    timeout=10,
                    check=False,
                )
                self.assertEqual(completed.returncode, expected_exit, completed.stdout + completed.stderr)
                if expected_exit:
                    self.assertIn("::error::", completed.stdout)

    def test_parallel_jobs_keep_failure_diagnostics(self) -> None:
        for job_id, step_name, artifact in [
            ("wpf-w4-acceptance", "Upload WPF W4 acceptance evidence", "wpf-w4-acceptance-evidence"),
            ("pilot-harness", "Upload pilot acceptance evidence", "pilot-acceptance-evidence"),
        ]:
            with self.subTest(job=job_id):
                upload = self.job(job_id).split(f"      - name: {step_name}\n", 1)[1]
                self.assertIn("        if: always()\n", upload)
                self.assertIn(f"          name: {artifact}\n", upload)
                self.assertIn("          if-no-files-found: ignore\n", upload)
                self.assertIn("          retention-days: 30\n", upload)

    def test_workflow_triggers_on_w4_browser_and_desktop_surfaces(self) -> None:
        for watched_path in [
            "src/Meridian.Ui/dashboard/package.json",
            "src/Meridian.Ui/dashboard/src/screens/**",
            "src/Meridian.Wpf/**",
            "tests/Meridian.Wpf.Tests/**",
        ]:
            self.assertIn(watched_path, self.workflow)

    def test_workflow_triggers_on_shared_golden_path_surfaces(self) -> None:
        for watched_path in [
            "src/Meridian.Application/Commands/LedgerCliCommand.cs",
            "src/Meridian.FinancialOperations/Ledger/TextJournal/**",
            "src/Meridian.Contracts/Workstation/**",
            "src/Meridian.Execution/**",
            "src/Meridian.Ledger/**",
            "src/Meridian.Strategies/**",
            "src/Meridian.Ui.Shared/**",
            "tests/Meridian.Tests/Application/Commands/LedgerCliCommandTests.cs",
        ]:
            self.assertIn(watched_path, self.workflow)


if __name__ == "__main__":
    unittest.main()
