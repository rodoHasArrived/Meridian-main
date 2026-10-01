"""Protect the shared database gate's failure handling and untrusted PR boundary."""
from pathlib import Path
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github/workflows"


def load_workflow(name):
    return yaml.load((WORKFLOWS / name).read_text(encoding="utf-8"), Loader=yaml.BaseLoader)


class ServiceBackedIntegrationsWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.shared = load_workflow("service-backed-integrations.yml")
        cls.ci = load_workflow("meridian-ci.yml")
        cls.production = load_workflow("production-certification.yml")
        cls.job = cls.shared["jobs"]["deterministic-integrations"]
        cls.steps = cls.job["steps"]

    def test_same_implementation_runs_for_pr_merge_group_and_certification(self):
        self.assertEqual(self.ci["on"]["pull_request"]["branches"], ["main"])
        self.assertIn("merge_group", self.ci["on"])
        for event in ("pull_request", "merge_group"):
            config = self.ci["on"][event] or {}
            self.assertNotIn("paths", config)
            self.assertNotIn("paths-ignore", config)
        for caller in (self.ci["jobs"]["service-backed-integrations"],
                       self.production["jobs"]["deterministic-integrations"]):
            self.assertEqual(caller["uses"], "./.github/workflows/service-backed-integrations.yml")
            self.assertNotIn("if", caller)
            self.assertNotIn("secrets", caller)
            self.assertNotIn("continue-on-error", caller)

    def test_forks_use_event_commit_read_only_token_and_no_production_secrets(self):
        for workflow in (self.shared, self.ci):
            self.assertEqual(workflow["permissions"], {"contents": "read"})
            self.assertNotIn("pull_request_target", workflow["on"])
        self.assertNotIn("secrets", self.shared["on"]["workflow_call"])
        checkout = next(step for step in self.steps if step.get("uses", "").startswith("actions/checkout@"))
        self.assertEqual(checkout["with"]["ref"], "${{ github.sha }}")
        self.assertEqual(checkout["with"]["persist-credentials"], "false")
        self.assertNotRegex((WORKFLOWS / "service-backed-integrations.yml").read_text(encoding="utf-8"),
                            r"\$\{\{[^}]*\bsecrets\.")

    def test_disposable_postgres_configuration_matches_certification(self):
        service = self.job["services"]["postgres"]
        self.assertEqual(service["image"], "postgres:17")
        self.assertEqual(service["env"], {"POSTGRES_USER": "postgres", "POSTGRES_PASSWORD": "meridian-certification", "POSTGRES_DB": "meridian"})
        self.assertEqual(service["ports"], ["5432:5432"])
        self.assertNotIn("volumes", service)
        for option in ('--health-cmd "pg_isready -U postgres -d meridian"',
                       "--health-interval 2s", "--health-timeout 5s", "--health-retries 50"):
            self.assertIn(option, service["options"])
        connections = {name: value for name, value in self.job["env"].items() if name.endswith("_CONNECTION_STRING")}
        domains = ("LEDGER", "ASSET_OPERATIONS", "DIRECT_LENDING", "FUND_ACCOUNTS",
                   "FUND_STRUCTURE", "REPORTING", "SCOPED_ACCESS", "SECURITY_MASTER")
        self.assertEqual(set(connections), {f"MERIDIAN_{domain}_CONNECTION_STRING" for domain in domains})
        self.assertEqual(set(connections.values()), {"Host=localhost;Port=5432;Database=meridian;Username=postgres;Password=meridian-certification"})
        self.assertEqual(self.job["env"]["MERIDIAN_DISABLE_DOCKER_TESTS"], "false")

    def test_restore_and_test_failures_never_short_circuit_the_other_project(self):
        independent = [step for step in self.steps if step.get("id") in
                       {"restore-meridian", "restore-direct-lending", "test-meridian", "test-direct-lending"}]
        self.assertEqual([step["id"] for step in independent],
                         ["restore-meridian", "restore-direct-lending", "test-meridian", "test-direct-lending"])
        for step in independent:
            # !cancelled() overrides GitHub's implicit success() status check;
            # the sole prerequisite is SDK availability, never another suite's result.
            self.assertEqual(step["if"], "${{ !cancelled() && steps.dotnet.outcome == 'success' }}")
            self.assertNotIn("continue-on-error", step)
            self.assertEqual(step["run"].count("dotnet "), 1)
        self.assertIn("tests/Meridian.Tests/Meridian.Tests.csproj", independent[2]["run"])
        self.assertIn("tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj", independent[3]["run"])

    def test_validation_and_evidence_run_even_after_test_failure(self):
        validate = next(step for step in self.steps if "validate-test-results.py" in step.get("run", ""))
        self.assertEqual(validate["if"], "always()")
        for prefix in ("meridian-integrations", "direct-lending-integrations"):
            self.assertIn(f"--require-trx-prefix {prefix}", validate["run"])
        evidence = next(step for step in self.steps if "pg_dump" in step.get("run", ""))
        upload = next(step for step in self.steps if step.get("uses", "").startswith("actions/upload-artifact@"))
        self.assertEqual(evidence["if"], "always()")
        self.assertEqual(upload["if"], "always()")
        self.assertEqual(upload["with"]["if-no-files-found"], "error")

    def test_companion_gate_always_reports_and_requires_success(self):
        gate = self.ci["jobs"]["integration-gate"]
        self.assertEqual(gate["name"], "integration-gate")
        self.assertEqual(gate["if"], "always()")
        self.assertEqual(gate["needs"], "service-backed-integrations")
        step = gate["steps"][0]
        self.assertEqual(step["env"]["INTEGRATION_RESULT"], "${{ needs.service-backed-integrations.result }}")
        self.assertIn('if [[ "$INTEGRATION_RESULT" != "success" ]]; then', step["run"])
        self.assertIn("exit 1", step["run"])
        self.assertNotIn("continue-on-error", gate)
        self.assertEqual(self.ci["jobs"]["quality-gate"]["needs"],
                         ["verify-dotnet", "verify-browser", "verify-docs", "verify-workflows"])


if __name__ == "__main__":
    unittest.main()
