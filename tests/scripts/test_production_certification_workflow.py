import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

import yaml


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "production-certification.yml"
INTEGRATIONS_PATH = REPO_ROOT / ".github" / "workflows" / "service-backed-integrations.yml"
COVERLET_SETTINGS_PATH = REPO_ROOT / "tests" / "coverlet.runsettings"
LIVE_PROVIDER_TESTS = (
    REPO_ROOT / "tests" / "Meridian.Tests" / "Integration" / "YahooFinancePcgPreferredIntegrationTests.cs",
    REPO_ROOT / "tests" / "Meridian.Tests" / "Integration" / "ConfigurableTickerDataCollectionTests.cs",
)


class ProductionCertificationWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")
        cls.integrations = INTEGRATIONS_PATH.read_text(encoding="utf-8")
        cls.coverlet_settings = COVERLET_SETTINGS_PATH.read_text(encoding="utf-8")

    def test_certifies_main_pushes_without_path_filters_and_retains_other_triggers(self) -> None:
        # BaseLoader keeps the YAML 1.1 word "on" as a string, as GitHub does.
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        triggers = workflow["on"]
        self.assertEqual(triggers["push"]["branches"], ["main"])
        self.assertNotIn("tags", triggers["push"])
        self.assertIn("workflow_call", triggers)
        self.assertNotIn("paths", triggers["push"])
        self.assertNotIn("paths-ignore", triggers["push"])
        self.assertEqual(triggers["schedule"], [{"cron": "17 3 * * 0"}])
        self.assertIn("workflow_dispatch", triggers)
        self.assertEqual(workflow["concurrency"]["cancel-in-progress"], "false")

    def test_runs_service_backed_integrations_with_cobertura_coverage(self) -> None:
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        caller = workflow["jobs"]["deterministic-integrations"]
        self.assertEqual(caller["uses"], "./.github/workflows/service-backed-integrations.yml")
        self.assertEqual(caller["with"], {"artifact-name": "production-certification", "retention-days": "90"})
        self.assertIn("image: postgres:17", self.integrations)
        self.assertIn('MERIDIAN_DISABLE_DOCKER_TESTS: "false"', self.integrations)
        self.assertEqual(self.integrations.count('--collect:"XPlat Code Coverage"'), 2)
        self.assertEqual(self.integrations.count("--settings tests/coverlet.runsettings"), 2)
        self.assertIn('"Category=Integration&Category!=LiveProvider"', self.integrations)
        self.assertIn('"Category=Integration"', self.integrations)

        # Coverlet 10 rejects DeterministicReport with its OpenCover reporter.
        # Cobertura is the repository's consumed coverage format, so keep the
        # certification collector deterministic and limited to that reporter.
        self.assertIn("<Format>cobertura</Format>", self.coverlet_settings)
        self.assertNotIn("opencover", self.coverlet_settings.casefold())
        self.assertIn("<DeterministicReport>true</DeterministicReport>", self.coverlet_settings)

    def test_live_provider_tests_are_explicitly_owned_by_the_exclusion_category(self) -> None:
        for test_path in LIVE_PROVIDER_TESTS:
            self.assertIn('[Trait("Category", "LiveProvider")]', test_path.read_text(encoding="utf-8"))

    def test_fails_for_every_skipped_trx_result(self) -> None:
        self.assertIn("validate-test-results.py", self.integrations)
        self.assertIn("--require-trx-prefix meridian-integrations", self.integrations)
        self.assertIn("--require-trx-prefix direct-lending-integrations", self.integrations)

    def test_postgres_client_tools_match_the_service_major(self) -> None:
        # Integrations use clients inside the service container; the recovery drill
        # installs the matching major because its recovery script calls local tools.
        self.assertIn("image: postgres:17", self.workflow)
        self.assertIn("image: postgres:17", self.integrations)
        self.assertIn('docker exec -e PGPASSWORD=meridian-certification -u postgres "$POSTGRES_CONTAINER_ID" "$@"', self.integrations)
        self.assertIn("postgresql-client-17", self.workflow)
        self.assertIn("/usr/lib/postgresql/17/bin", self.workflow)

    def test_captures_schema_and_migration_ledger_evidence(self) -> None:
        self.assertIn("pg_dump", self.integrations)
        self.assertIn("database-schema.sql", self.integrations)
        self.assertIn("database-table-inventory.csv", self.integrations)
        self.assertIn("migration-ledger-inventory.csv", self.integrations)

    def test_scans_nuget_and_npm_dependencies(self) -> None:
        self.assertIn("dotnet list Meridian.sln package --vulnerable --include-transitive", self.workflow)
        self.assertIn("npm audit --json", self.workflow)

    def test_recovery_drill_uses_checkpoint_outputs_and_documented_objective_budgets(self) -> None:
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        steps = workflow["jobs"]["recovery-drill"]["steps"]
        checkpoint = next(step for step in steps if step.get("id") == "recovery_checkpoint")
        drill = next(step for step in steps if step["name"] == "Execute encrypted backup and restore drill")
        self.assertLess(steps.index(checkpoint), steps.index(drill))
        self.assertEqual(drill["env"]["RECOVERY_POINT_AT_UTC"], "${{ steps.recovery_checkpoint.outputs.point }}")
        self.assertEqual(drill["env"]["RECOVERY_POINT_EVIDENCE"], "${{ steps.recovery_checkpoint.outputs.evidence }}")
        self.assertIn("-LastVerifiedRecoverablePointAtUtc $env:RECOVERY_POINT_AT_UTC", drill["run"])
        self.assertIn("-RecoverablePointEvidence $env:RECOVERY_POINT_EVIDENCE", drill["run"])
        self.assertIn("-MaximumRpoSeconds 3600", drill["run"])
        self.assertIn("-MaximumRtoSeconds 7200", drill["run"])
        self.assertNotIn("operatorAcceptedAtUtc", self.workflow)

    def test_npm_advisories_gate_through_the_reviewed_acceptance_register(self) -> None:
        self.assertIn("validate-npm-audit.py", self.workflow)
        self.assertIn("npm-audit-accepted-advisories.json", self.workflow)
        self.assertIn("--fail-level high", self.workflow)
        register = (
            REPO_ROOT / "build" / "config" / "security" / "npm-audit-accepted-advisories.json"
        ).read_text(encoding="utf-8")
        self.assertIn("docs/security/known-vulnerabilities.md", register)


class ProductionCertificationCheckpointTests(unittest.TestCase):
    """Execute the workflow's checkpoint steps with controlled PostgreSQL reads.

    These tests exercise evidence binding and rejection paths; the hosted drill
    remains responsible for proving actual PostgreSQL backup and restoration.
    """

    def setUp(self) -> None:
        self.pwsh = shutil.which("pwsh")
        self.assertIsNotNone(self.pwsh, "PowerShell 7 is required for recovery checkpoint tests.")
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        steps = yaml.load(WORKFLOW_PATH.read_text(), Loader=yaml.BaseLoader)["jobs"]["recovery-drill"]["steps"]
        self.seed_script = next(step["run"] for step in steps if step.get("id") == "recovery_checkpoint")
        self.verify_script = next(step["run"] for step in steps if step["name"] == "Verify restored business and file state")
        helper_path = Path("build/scripts/recovery/recovery-evidence.ps1")
        (self.root / helper_path.parent).mkdir(parents=True)
        shutil.copyfile(REPO_ROOT / helper_path, self.root / helper_path)
        tools = self.root / "bin"
        tools.mkdir()
        for name in ("psql", "createdb"):
            tool = tools / name
            tool.write_text(
                "#!/usr/bin/env python3\n"
                "import json, os, pathlib, sys\n"
                "with pathlib.Path(os.environ['RECOVERY_TEST_CALLS']).open('a') as output:\n"
                "    output.write(json.dumps(sys.argv[1:]) + '\\n')\n"
                "if '--command' in sys.argv and sys.argv[-1].startswith('SELECT'):\n"
                "    if os.environ.get('RECOVERY_TEST_FAIL_READ') == '1': sys.exit(17)\n"
                "    print(os.environ['RECOVERY_TEST_ROW'])\n",
                encoding="utf-8",
            )
            tool.chmod(0o755)
        self.point = "2026-01-01T00:00:00.123456Z"
        self.environment = {
            **os.environ,
            "PATH": str(tools) + os.pathsep + os.environ.get("PATH", ""),
            "GITHUB_OUTPUT": str(self.root / "outputs"),
            "GITHUB_SHA": "a" * 40,
            "GITHUB_RUN_ID": "1234",
            "GITHUB_RUN_ATTEMPT": "2",
            "GITHUB_SERVER_URL": "https://github.com",
            "GITHUB_REPOSITORY": "owner/repository",
            "RECOVERY_TEST_CALLS": str(self.root / "calls.jsonl"),
            "RECOVERY_TEST_ROW": self.point + "|expected",
        }
        self.source = self.root / "artifacts/recovery/source-data"
        self.restored = self.root / "artifacts/recovery/restore-data"

    def run_step(self, script: str) -> subprocess.CompletedProcess:
        path = self.root / "step.ps1"
        path.write_text(script, encoding="utf-8")
        return subprocess.run(
            [self.pwsh, "-NoLogo", "-NoProfile", "-File", str(path)],
            cwd=self.root, env=self.environment, capture_output=True, text=True, timeout=30,
        )

    def seed_and_restore_fixture(self) -> None:
        seeded = self.run_step(self.seed_script)
        self.assertEqual(0, seeded.returncode, seeded.stdout + seeded.stderr)
        shutil.copytree(self.source, self.restored, dirs_exist_ok=True)

    def test_checkpoint_binds_committed_database_read_and_file_hash_to_retained_evidence(self) -> None:
        self.seed_and_restore_fixture()
        checkpoint_path = self.source / "recovery-checkpoint.json"
        checkpoint = json.loads(checkpoint_path.read_text(encoding="utf-8-sig"))
        self.assertEqual(self.point, checkpoint["lastVerifiedRecoverablePointAtUtc"])
        self.assertEqual("a" * 40, checkpoint["sourceCommit"])
        self.assertEqual(("1234", "2"), (checkpoint["workflowRunId"], checkpoint["workflowRunAttempt"]))
        self.assertEqual(("meridian", "recovery_probe", 1, "expected"), (
            checkpoint["database"], checkpoint["table"], checkpoint["rowId"], checkpoint["value"],
        ))
        self.assertEqual(hashlib.sha256(b"encrypted-vault-probe").hexdigest(), checkpoint["vaultSha256"])
        outputs = (self.root / "outputs").read_text()
        self.assertIn("point=" + self.point, outputs)
        self.assertIn("/actions/runs/1234/attempts/2#production-recovery-drill-1234/source-data/recovery-checkpoint.json", outputs)
        self.assertIn("sha256=" + hashlib.sha256(checkpoint_path.read_bytes()).hexdigest(), outputs)
        calls = [json.loads(line) for line in (self.root / "calls.jsonl").read_text().splitlines()]
        self.assertIn("clock_timestamp()", calls[0][-1])
        self.assertTrue(calls[2][-1].startswith("SELECT to_char(checkpoint_at"))
        restored = self.run_step(self.verify_script)
        self.assertEqual(0, restored.returncode, restored.stdout + restored.stderr)

    def test_missing_or_mismatched_committed_state_never_publishes_checkpoint(self) -> None:
        for row in ("", self.point + "|wrong-value"):
            with self.subTest(row=row):
                self.environment["RECOVERY_TEST_ROW"] = row
                result = self.run_step(self.seed_script)
                self.assertNotEqual(0, result.returncode)
                self.assertFalse((self.source / "recovery-checkpoint.json").exists())
                self.assertFalse((self.root / "outputs").exists())

    def test_restored_checkpoint_database_and_vault_must_match_the_recovery_unit(self) -> None:
        self.seed_and_restore_fixture()
        for relative_path in ("recovery-checkpoint.json", "credentials/provider-vault.dat"):
            with self.subTest(path=relative_path):
                (self.restored / relative_path).write_text("tampered")
                result = self.run_step(self.verify_script)
                self.assertNotEqual(0, result.returncode)
                shutil.copyfile(self.source / relative_path, self.restored / relative_path)
        self.environment["RECOVERY_TEST_ROW"] = "2026-01-02T00:00:00.123456Z|expected"
        result = self.run_step(self.verify_script)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Restored business state does not match", result.stderr)

    def test_database_read_failure_cannot_become_checkpoint_evidence(self) -> None:
        self.environment["RECOVERY_TEST_FAIL_READ"] = "1"
        result = self.run_step(self.seed_script)
        self.assertNotEqual(0, result.returncode)
        self.assertFalse((self.source / "recovery-checkpoint.json").exists())
        self.assertIn("Committed recovery checkpoint read failed", result.stderr)


if __name__ == "__main__":
    unittest.main()
