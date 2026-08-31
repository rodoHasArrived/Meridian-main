import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "production-certification.yml"
COVERLET_SETTINGS_PATH = REPO_ROOT / "tests" / "coverlet.runsettings"
LIVE_PROVIDER_TESTS = (
    REPO_ROOT / "tests" / "Meridian.Tests" / "Integration" / "YahooFinancePcgPreferredIntegrationTests.cs",
    REPO_ROOT / "tests" / "Meridian.Tests" / "Integration" / "ConfigurableTickerDataCollectionTests.cs",
)


class ProductionCertificationWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")
        cls.coverlet_settings = COVERLET_SETTINGS_PATH.read_text(encoding="utf-8")

    def test_runs_on_release_tags_schedule_and_manual_dispatch(self) -> None:
        self.assertIn("workflow_dispatch:", self.workflow)
        self.assertIn("schedule:", self.workflow)
        self.assertIn('cron: "17 3 * * 0"', self.workflow)
        self.assertIn('tags:\n      - "v*"', self.workflow)

    def test_runs_service_backed_integrations_with_cobertura_coverage(self) -> None:
        self.assertIn("image: postgres:17", self.workflow)
        self.assertIn('MERIDIAN_DISABLE_DOCKER_TESTS: "false"', self.workflow)
        self.assertIn('--collect:"XPlat Code Coverage"', self.workflow)
        self.assertIn("--settings tests/coverlet.runsettings", self.workflow)
        self.assertIn('"Category=Integration&Category!=LiveProvider"', self.workflow)
        self.assertIn('"Category=Integration"', self.workflow)

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
        self.assertIn("validate-test-results.py", self.workflow)
        self.assertIn("--require-trx-prefix meridian-integrations", self.workflow)
        self.assertIn("--require-trx-prefix direct-lending-integrations", self.workflow)

    def test_postgres_client_tools_match_the_service_major(self) -> None:
        # pg_dump refuses to dump a newer server, so both PostgreSQL-service jobs
        # (deterministic integrations' schema capture and the recovery drill) must
        # install client tools matching the postgres:17 service containers.
        self.assertIn("image: postgres:17", self.workflow)
        self.assertGreaterEqual(self.workflow.count("postgresql-client-17"), 2)
        self.assertIn("/usr/lib/postgresql/17/bin", self.workflow)

    def test_captures_schema_and_migration_ledger_evidence(self) -> None:
        self.assertIn("pg_dump", self.workflow)
        self.assertIn("database-schema.sql", self.workflow)
        self.assertIn("database-table-inventory.csv", self.workflow)
        self.assertIn("migration-ledger-inventory.csv", self.workflow)

    def test_scans_nuget_and_npm_dependencies(self) -> None:
        self.assertIn("dotnet list Meridian.sln package --vulnerable --include-transitive", self.workflow)
        self.assertIn("npm audit --json", self.workflow)

    def test_npm_advisories_gate_through_the_reviewed_acceptance_register(self) -> None:
        self.assertIn("validate-npm-audit.py", self.workflow)
        self.assertIn("npm-audit-accepted-advisories.json", self.workflow)
        self.assertIn("--fail-level high", self.workflow)
        register = (
            REPO_ROOT / "build" / "config" / "security" / "npm-audit-accepted-advisories.json"
        ).read_text(encoding="utf-8")
        self.assertIn("docs/security/known-vulnerabilities.md", register)


if __name__ == "__main__":
    unittest.main()
