"""Protect the shared database gate's failure handling and untrusted PR boundary."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github/workflows"
ACTION = ROOT / ".github/actions/service-backed-integrations/action.yml"
ACTION_USES = "./.github/actions/service-backed-integrations"


def load_script(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_").removesuffix(".py"),
                                                  ROOT / "build/scripts/ci" / name)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_workflow(name):
    return yaml.load((WORKFLOWS / name).read_text(encoding="utf-8"), Loader=yaml.BaseLoader)


class ServiceBackedIntegrationsWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.shared = load_workflow("service-backed-integrations.yml")
        cls.ci = load_workflow("meridian-ci.yml")
        cls.production = load_workflow("production-certification.yml")
        cls.job = cls.shared["jobs"]["deterministic-integrations"]
        cls.gate = cls.ci["jobs"]["integration-gate"]
        # The suite commands live in one composite action that both callers run.
        cls.action = yaml.load(ACTION.read_text(encoding="utf-8"), Loader=yaml.BaseLoader)
        cls.steps = cls.action["runs"]["steps"]
        cls.callers = {"service-backed-integrations.yml": cls.job, "meridian-ci.yml": cls.gate}

    def action_step(self, job):
        steps = [step for step in job["steps"] if step.get("uses") == ACTION_USES]
        self.assertEqual(len(steps), 1)
        return steps[0]

    def test_same_implementation_runs_for_pr_merge_group_and_certification(self):
        self.assertIn("pull_request", self.ci["on"])
        self.assertFalse(self.ci["on"]["pull_request"], "Integration coverage applies to PRs targeting any branch")
        self.assertIn("merge_group", self.ci["on"])
        for event in ("pull_request", "merge_group"):
            config = self.ci["on"][event] or {}
            self.assertNotIn("paths", config)
            self.assertNotIn("paths-ignore", config)
        # Certification calls the reusable workflow; Meridian CI's integration-gate runs the same
        # composite action directly, so the check that reports the suites is the job that runs them.
        production = self.production["jobs"]["deterministic-integrations"]
        self.assertEqual(production["uses"], "./.github/workflows/service-backed-integrations.yml")
        self.assertNotIn("service-backed-integrations", self.ci["jobs"])
        for caller in (production, self.gate):
            self.assertNotIn("secrets", caller)
            self.assertNotIn("continue-on-error", caller)
        self.assertEqual(self.action["runs"]["using"], "composite")
        for name, job in self.callers.items():
            with self.subTest(caller=name):
                step = self.action_step(job)
                self.assertEqual(step["id"], "integrations")
                self.assertEqual(step["if"], "steps.changes.outputs.dotnet == 'true'")
                self.assertEqual(step["with"]["postgres-container-id"], "${{ job.services.postgres.id }}")
                self.assertEqual(job["timeout-minutes"], "90")
        self.assertEqual(self.action_step(self.gate)["with"]["artifact-name"], "meridian-ci-integrations")
        # Certification always runs its integrations; only Meridian CI classifies docs-only PRs.
        self.assertNotIn("if", production)
        self.assertNotIn("skip-when-docs-only", production.get("with", {}))
        self.assertNotIn("needs", self.gate, "The integration gate must not queue behind another job.")
        self.assertNotIn("if", self.gate)
        option = self.shared["on"]["workflow_call"]["inputs"]["skip-when-docs-only"]
        self.assertEqual((option["type"], option["default"]), ("boolean", "false"))
        detect = next(step for step in self.job["steps"] if step.get("id") == "changes")
        self.assertEqual(detect["env"]["CLASSIFY"],
                         "${{ inputs.skip-when-docs-only && github.event_name == 'pull_request' }}")
        self.assertIn('if [[ "$CLASSIFY" == "true" ]]', detect["run"])
        self.assertIn("build/scripts/ci/detect-dotnet-changes.py", detect["run"])
        self.assertIn('echo "dotnet=true"', detect["run"])
        for job in self.callers.values():
            self.assertEqual(job["outputs"]["dotnet-required"], "${{ steps.changes.outputs.dotnet }}")
        for key in ("artifact-id", "artifact-name", "receipt-sha256"):
            self.assertEqual(self.job["outputs"][key], "${{ steps.integrations.outputs." + key + " }}")
            self.assertEqual(self.gate["outputs"][key], "${{ steps.evidence.outputs." + key + " }}")
            self.assertEqual(self.ci["on"]["workflow_call"]["outputs"][f"integration-{key}"]["value"],
                             "${{ jobs.integration-gate.outputs." + key + " }}")

    def test_forks_use_event_commit_read_only_token_and_no_production_secrets(self):
        for workflow in (self.shared, self.ci):
            self.assertEqual(workflow["permissions"], {"contents": "read"})
            self.assertNotIn("pull_request_target", workflow["on"])
        self.assertNotIn("secrets", self.shared["on"]["workflow_call"])
        for name, job in self.callers.items():
            with self.subTest(caller=name):
                checkout = next(step for step in job["steps"] if step.get("uses", "").startswith("actions/checkout@"))
                self.assertEqual(checkout["with"]["ref"], "${{ github.sha }}")
                self.assertEqual(checkout["with"]["persist-credentials"], "false")
        for path in (WORKFLOWS / "service-backed-integrations.yml", ACTION):
            self.assertNotRegex(path.read_text(encoding="utf-8"), r"\$\{\{[^}]*\bsecrets\.")

    def test_disposable_postgres_configuration_matches_certification(self):
        # Both callers declare the same service and environment the action relies on.
        self.assertEqual(self.gate["services"], self.job["services"])
        self.assertEqual(self.gate["env"], self.job["env"])
        service = self.job["services"]["postgres"]
        self.assertRegex(service["image"], r"^postgres:17@sha256:[0-9a-f]{64}$")
        self.assertEqual(service["image"], self.production["jobs"]["recovery-drill"]["services"]["postgres"]["image"])
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

    def test_restore_and_test_failures_never_short_circuit_the_other_projects(self):
        ordered = ["restore-meridian", "restore-direct-lending", "restore-demo",
                   "build-meridian", "database-meridian",
                   "test-meridian", "test-direct-lending", "test-demo"]
        independent = [step for step in self.steps if step.get("id") in set(ordered)]
        self.assertEqual([step["id"] for step in independent], ordered)
        for step in independent:
            # !cancelled() overrides GitHub's implicit success() status check;
            # the sole prerequisite is SDK availability, never another suite's result.
            self.assertEqual(step["if"], "${{ !cancelled() && steps.dotnet.outcome == 'success' }}")
            self.assertNotIn("continue-on-error", step)
            if step["id"] not in {"database-meridian", "test-meridian"}:
                self.assertEqual(step["run"].count("dotnet "), 1)
        projects = {
            "meridian": "tests/Meridian.Tests/Meridian.Tests.csproj",
            "direct-lending": "tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj",
            "demo": "tests/Meridian.Demo.Tests/Meridian.Demo.Tests.csproj",
        }
        for name, path in projects.items():
            for action in ("restore", "test"):
                if (action, name) == ("test", "meridian"):
                    continue
                step = next(step for step in independent if step["id"] == f"{action}-{name}")
                self.assertIn(f"dotnet {action} {path}", step["run"])

    def test_meridian_suite_runs_as_declared_processes_from_one_build(self):
        by_id = {step["id"]: step for step in self.steps if "id" in step}
        self.assertEqual(by_id["build-meridian"]["run"],
                         "dotnet build tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore")
        self.assertIn("createdb --username postgres meridian_tax_lot", by_id["database-meridian"]["run"])
        self.assertEqual(by_id["database-meridian"]["env"]["POSTGRES_CONTAINER_ID"], "${{ inputs.postgres-container-id }}")
        run = by_id["test-meridian"]["run"]
        self.assertIn("python build/scripts/ci/run-meridian-integrations.py", run)
        self.assertIn("--results-dir artifacts/test-results/production-certification/meridian-integrations", run)

        evidence = load_script("integration_evidence.py")
        processes = evidence.SUITES["meridian-integrations"]["processes"]
        # The two filter terms are exact complements, so the processes select the suite once.
        terms = sorted(process["filter"] for process in processes.values())
        self.assertEqual(terms, [f"FullyQualifiedName!~{evidence.TAX_LOT_JOURNAL_CLASS}",
                                 f"FullyQualifiedName~{evidence.TAX_LOT_JOURNAL_CLASS}"])
        self.assertEqual(sorted(process["database"] for process in processes.values()),
                         ["meridian", "meridian_tax_lot"])
        self.assertTrue(list((ROOT / "tests/Meridian.Tests/Storage").glob("AtomicTaxLotJournalStoreTests*.cs")))
        validate = next(step for step in self.steps if "validate-test-results.py" in step.get("run", ""))
        capture = next(step for step in self.steps if "pg_dump" in step.get("run", ""))
        for name, process in processes.items():
            self.assertIn(f"--require-trx-prefix meridian-integrations-{name}", validate["run"])
            self.assertIn(process["database"], capture["run"].split("for database in ", 1)[1].split(";", 1)[0].split())
        # Each database's evidence is required by the receipt, never optional.
        self.assertEqual(len(evidence.SCHEMA_FILES), 6)
        self.assertIn("meridian_tax_lot-database-schema.sql", evidence.SCHEMA_FILES)

    def test_runner_isolates_each_process_database_output_and_filter(self):
        runner = load_script("run-meridian-integrations.py")
        contract = runner.load_contract()
        environment = {
            "MERIDIAN_LEDGER_CONNECTION_STRING": "Host=localhost;Port=5432;Database=meridian;Username=postgres;Password=x",
            "MERIDIAN_REPORTING_CONNECTION_STRING": "Host=localhost;Database=meridian",
            "UNRELATED": "Database=meridian;",
        }
        isolated = runner.process_environment(environment, "meridian_tax_lot", "/private")
        self.assertEqual(isolated["MERIDIAN_LEDGER_CONNECTION_STRING"],
                         "Host=localhost;Port=5432;Database=meridian_tax_lot;Username=postgres;Password=x")
        self.assertEqual(isolated["MERIDIAN_REPORTING_CONNECTION_STRING"], "Host=localhost;Database=meridian_tax_lot")
        self.assertEqual(isolated["UNRELATED"], "Database=meridian;")
        self.assertEqual({isolated[name] for name in ("TMPDIR", "TMP", "TEMP")}, {"/private"})
        primary = runner.process_environment(environment, "meridian", "/other")
        self.assertEqual(primary["MERIDIAN_LEDGER_CONNECTION_STRING"], environment["MERIDIAN_LEDGER_CONNECTION_STRING"])
        self.assertEqual(primary["TMPDIR"], "/other")
        # A process that could not be isolated must not run against the shared database.
        for broken in ({}, {"MERIDIAN_LEDGER_CONNECTION_STRING": "Host=localhost;Database=other"},
                       {"MERIDIAN_LEDGER_CONNECTION_STRING": "Database=meridian;Database=meridian"}):
            with self.subTest(broken=broken), self.assertRaises(ValueError):
                runner.process_environment(broken, "meridian_tax_lot", "/private")

        commands = {
            name: runner.build_command(contract, name, configuration="Release", results_dir=Path("results"),
                                       settings="tests/coverlet.runsettings",
                                       output=Path("/copy") if name == "tax-lot-journal" else None)
            for name in contract["processes"]
        }
        for name, command in commands.items():
            self.assertEqual(command[:3], ["dotnet", "test", "tests/Meridian.Tests/Meridian.Tests.csproj"])
            for flag in ("--no-build", "--no-restore"):
                self.assertIn(flag, command)
            self.assertEqual(command[command.index("--collect") + 1], "XPlat Code Coverage")
            self.assertEqual(command[command.index("--settings") + 1], "tests/coverlet.runsettings")
            self.assertEqual(command[command.index("--logger") + 1], f"trx;LogFilePrefix=meridian-integrations-{name}")
            self.assertEqual(command[command.index("--filter") + 1],
                             f"(Category=Integration&Category!=LiveProvider)&({contract['processes'][name]['filter']})")
        # Coverage instruments assemblies in place: the processes must not share an output.
        self.assertEqual(commands["tax-lot-journal"][-2:], ["--output", "/copy"])
        self.assertNotIn("--output", commands["remainder"])

        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temporary:
            built = Path(temporary) / "bin" / "Release" / "net10.0"
            built.mkdir(parents=True)
            with self.assertRaises(ValueError):
                runner.isolated_output(built, "tax-lot-journal")
            (built / "Meridian.Tests.dll").write_text("assembly")
            copy = runner.isolated_output(built, "tax-lot-journal")
            self.assertEqual(copy, built.with_name("net10.0-tax-lot-journal"))
            self.assertEqual((copy / "Meridian.Tests.dll").read_text(), "assembly")

    def test_moved_demo_cases_keep_service_backed_coverage_and_distinct_evidence(self):
        demo = next(step for step in self.steps if step.get("id") == "test-demo")
        self.assertIn('--filter "Category=Integration&Category!=LiveProvider"', demo["run"])
        self.assertIn('--collect:"XPlat Code Coverage"', demo["run"])
        self.assertIn("--settings tests/coverlet.runsettings", demo["run"])
        self.assertIn("--results-directory artifacts/test-results/production-certification/demo-integrations",
                      demo["run"])
        self.assertIn('--logger "trx;LogFilePrefix=demo-integrations"', demo["run"])
        self.assertIn("--no-restore", demo["run"])

    def test_validation_and_evidence_run_even_after_test_failure(self):
        # The docs-only guard sits on each caller's action step; inside the action these steps
        # run whatever happened to the suites.
        guard = "always()"
        for step in self.steps:
            if step.get("run"):
                self.assertEqual(step["shell"], "bash", step["name"])
        validate = next(step for step in self.steps if "validate-test-results.py" in step.get("run", ""))
        self.assertEqual(validate["if"], guard)
        for prefix in ("meridian-integrations", "meridian-integrations-tax-lot-journal",
                       "meridian-integrations-remainder", "direct-lending-integrations", "demo-integrations"):
            self.assertIn(f"--require-trx-prefix {prefix}", validate["run"])
        evidence = next(step for step in self.steps if "pg_dump" in step.get("run", ""))
        upload = next(step for step in self.steps if step.get("uses", "").startswith("actions/upload-artifact@"))
        self.assertEqual(evidence["if"], guard)
        self.assertEqual(upload["if"], guard)
        self.assertEqual(upload["with"]["if-no-files-found"], "error")

    def test_companion_gate_runs_the_suites_and_requires_complete_evidence(self):
        self.assertEqual(self.gate["name"], "integration-gate")
        self.assertNotIn("continue-on-error", self.gate)
        # It overrides Meridian CI's workflow-wide Docker opt-out for the service-backed suites.
        self.assertEqual(self.ci["env"]["MERIDIAN_DISABLE_DOCKER_TESTS"], "true")
        self.assertEqual(self.gate["env"]["MERIDIAN_DISABLE_DOCKER_TESTS"], "false")
        step = next(step for step in self.gate["steps"] if step.get("id") == "evidence")
        self.assertEqual(step["if"], "always()")
        self.assertEqual(step["env"], {
            "DOTNET_REQUIRED": "${{ steps.changes.outputs.dotnet }}",
            "INTEGRATION_OUTCOME": "${{ steps.integrations.outcome }}",
            "INTEGRATION_OUTPUTS": "${{ toJSON(steps.integrations.outputs) }}",
        })
        self.assertIs(self.gate["steps"][-1], step)
        script = step["run"].split("<<'PY'\n", 1)[1].rsplit("PY", 1)[0]

        def run_gate(required="true", outcome="success", outputs=None):
            with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temporary:
                output = Path(temporary) / "output"
                environment = dict(os.environ, DOTNET_REQUIRED=required, INTEGRATION_OUTCOME=outcome,
                                   INTEGRATION_OUTPUTS=json.dumps(outputs if outputs is not None else complete),
                                   GITHUB_OUTPUT=str(output), GITHUB_STEP_SUMMARY=str(Path(temporary) / "summary.md"))
                result = subprocess.run([sys.executable, "-c", script], env=environment,
                                        capture_output=True, text=True, check=False)
                return result.returncode, output.read_text() if output.exists() else ""

        complete = {"artifact-id": "7", "artifact-name": "meridian-ci-integrations-1-1", "receipt-sha256": "a" * 64}
        code, written = run_gate()
        self.assertEqual(code, 0)
        self.assertEqual(written.splitlines(), [f"{key}={value}" for key, value in complete.items()])
        # The only accepted result without evidence: the job's own classification found a
        # docs-only pull request, so the suites never ran.
        self.assertEqual(run_gate(required="false", outcome="skipped", outputs={})[0], 0)
        for required in ("", "maybe"):
            with self.subTest(required=required):
                self.assertNotEqual(run_gate(required=required)[0], 0)
        for outcome in ("failure", "cancelled", "skipped", ""):
            with self.subTest(outcome=outcome):
                self.assertNotEqual(run_gate(outcome=outcome)[0], 0)
        for key, bad in (("artifact-id", "0"), ("artifact-name", "-bad"), ("receipt-sha256", "A" * 64)):
            with self.subTest(output=key):
                self.assertNotEqual(run_gate(outputs=dict(complete, **{key: bad}))[0], 0)
                self.assertNotEqual(run_gate(outputs={k: v for k, v in complete.items() if k != key})[0], 0)
        self.assertEqual(self.ci["jobs"]["quality-gate"]["needs"],
                         ["verify-dotnet", "verify-browser", "verify-docs-workflows"])

    def test_evidence_gate_accepts_docs_only_only_when_the_caller_opted_in(self):
        step = self.shared["jobs"]["evidence-gate"]["steps"][0]
        self.assertEqual(step["env"]["SKIP_WHEN_DOCS_ONLY"], "${{ inputs.skip-when-docs-only }}")
        script = step["run"].split("<<'PY'\n", 1)[1].rsplit("PY", 1)[0]

        def run_gate(fresh_outputs, opted_in="true", fresh="success", reuse="skipped"):
            with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temporary:
                environment = dict(os.environ, FRESH_RESULT=fresh, REUSE_RESULT=reuse,
                                   FRESH_OUTPUTS=json.dumps(fresh_outputs), REUSE_OUTPUTS="{}",
                                   SKIP_WHEN_DOCS_ONLY=opted_in,
                                   GITHUB_OUTPUT=str(Path(temporary) / "output"))
                return subprocess.run([sys.executable, "-c", script], env=environment,
                                      capture_output=True, text=True, check=False).returncode

        docs_only = {"dotnet-required": "false", "artifact-id": "", "artifact-name": "", "receipt-sha256": ""}
        self.assertEqual(run_gate(docs_only), 0)
        # A caller that never opted in cannot pass without evidence, nor can a .NET change.
        self.assertNotEqual(run_gate(docs_only, opted_in="false"), 0)
        self.assertNotEqual(run_gate(dict(docs_only, **{"dotnet-required": "true"})), 0)
        self.assertNotEqual(run_gate(docs_only, fresh="failure"), 0)
        self.assertNotEqual(run_gate(docs_only, fresh="skipped", reuse="skipped"), 0)
        complete = {"dotnet-required": "true", "artifact-id": "7", "artifact-name": "meridian-ci-integrations-1-1",
                    "receipt-sha256": "a" * 64}
        self.assertEqual(run_gate(complete), 0)

    def test_reuse_requires_explicit_producer_outputs_and_validated_bytes(self):
        inputs = self.shared["on"]["workflow_call"]["inputs"]
        fields = ("artifact-id", "artifact-name", "receipt-sha256", "result")
        for field in fields:
            self.assertEqual(inputs[f"reuse-{field}"]["default"], "")
            self.assertIn(f"inputs.reuse-{field} == ''", self.job["if"])
        reuse = self.shared["jobs"]["reuse-integrations"]
        for field in fields:
            self.assertIn(f"inputs.reuse-{field} != ''", reuse["if"])
        download = next(step for step in reuse["steps"]
                        if step.get("uses", "").startswith("actions/download-artifact@"))
        self.assertEqual(download["with"]["artifact-ids"], "${{ inputs.reuse-artifact-id }}")
        self.assertNotIn("run-id", download["with"], "Reuse is restricted to this coordinated run.")
        validate = next(step for step in reuse["steps"] if step.get("id") == "receipt")
        self.assertIn("integration_evidence.py reuse", validate["run"])
        upload = next(step for step in reuse["steps"] if step.get("id") == "upload")
        self.assertNotIn("if", upload, "Never retain unvalidated evidence as a passing certification.")
        self.assertEqual(upload["with"]["retention-days"], "${{ inputs.retention-days }}")
        receipt = next(step for step in self.steps if step.get("id") == "receipt")
        # No status function means GitHub's implicit success(): receipts follow passing steps only.
        self.assertNotIn("if", receipt, "Only create passing receipts after all test/schema steps succeed.")
        outputs = self.action["outputs"]
        self.assertEqual(outputs["artifact-id"]["value"], "${{ steps.upload.outputs.artifact-id }}")
        self.assertEqual(outputs["artifact-name"]["value"], "${{ steps.receipt.outputs.artifact-name }}")
        self.assertEqual(outputs["receipt-sha256"]["value"], "${{ steps.receipt.outputs.receipt-sha256 }}")

    def test_standalone_certification_remains_fresh_and_release_passes_receipt(self):
        inputs = self.production["on"]["workflow_call"]["inputs"]
        for field in ("artifact-id", "artifact-name", "receipt-sha256", "result"):
            self.assertEqual(inputs[f"integration-{field}"]["default"], "")
        coordinator = load_workflow("desktop-installer-packaging.yml")
        production = coordinator["jobs"]["production"]
        self.assertIn("ci", production["needs"])
        for field in ("artifact-id", "artifact-name", "receipt-sha256"):
            self.assertEqual(production["with"][f"integration-{field}"],
                             "${{ needs.ci.outputs.integration-" + field + " }}")
        self.assertEqual(production["with"]["integration-result"], "${{ needs.ci.result }}")


if __name__ == "__main__":
    unittest.main()
