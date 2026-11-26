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
        cls.steps = cls.job["steps"]

    def test_same_implementation_runs_for_pr_merge_group_and_certification(self):
        self.assertIn("pull_request", self.ci["on"])
        self.assertFalse(self.ci["on"]["pull_request"], "Integration coverage applies to PRs targeting any branch")
        self.assertIn("merge_group", self.ci["on"])
        for event in ("pull_request", "merge_group"):
            config = self.ci["on"][event] or {}
            self.assertNotIn("paths", config)
            self.assertNotIn("paths-ignore", config)
        for caller in (self.ci["jobs"]["service-backed-integrations"],
                       self.production["jobs"]["deterministic-integrations"]):
            self.assertEqual(caller["uses"], "./.github/workflows/service-backed-integrations.yml")
            self.assertNotIn("secrets", caller)
            self.assertNotIn("continue-on-error", caller)
        # Certification always runs its integrations. Meridian CI opts into in-job
        # classification, which only a docs-only pull request can make false.
        self.assertNotIn("if", self.production["jobs"]["deterministic-integrations"])
        self.assertNotIn("skip-when-docs-only", self.production["jobs"]["deterministic-integrations"].get("with", {}))
        caller = self.ci["jobs"]["service-backed-integrations"]
        self.assertNotIn("needs", caller, "The integration lane must not queue behind another job.")
        self.assertNotIn("if", caller)
        self.assertEqual(caller["with"]["skip-when-docs-only"], "true")
        option = self.shared["on"]["workflow_call"]["inputs"]["skip-when-docs-only"]
        self.assertEqual((option["type"], option["default"]), ("boolean", "false"))
        detect = next(step for step in self.steps if step.get("id") == "changes")
        self.assertEqual(detect["env"]["CLASSIFY"],
                         "${{ inputs.skip-when-docs-only && github.event_name == 'pull_request' }}")
        self.assertIn('if [[ "$CLASSIFY" == "true" ]]', detect["run"])
        self.assertIn("build/scripts/ci/detect-dotnet-changes.py", detect["run"])
        self.assertIn('echo "dotnet=true"', detect["run"])
        self.assertEqual(self.job["outputs"]["dotnet-required"], "${{ steps.changes.outputs.dotnet }}")

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
        self.assertEqual(by_id["database-meridian"]["env"]["POSTGRES_CONTAINER_ID"], "${{ job.services.postgres.id }}")
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
        guard = "always() && steps.changes.outputs.dotnet == 'true'"
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

    def test_companion_gate_always_reports_and_requires_success(self):
        gate = self.ci["jobs"]["integration-gate"]
        self.assertEqual(gate["name"], "integration-gate")
        self.assertEqual(gate["if"], "always()")
        self.assertEqual(gate["needs"], ["verify-dotnet", "service-backed-integrations"])
        step = gate["steps"][0]
        call = "needs.service-backed-integrations.outputs"
        self.assertEqual(step["env"], {
            "DOTNET_LANE_DECISION": "${{ needs.verify-dotnet.outputs.dotnet-required }}",
            "INTEGRATION_DECISION": "${{ " + call + ".dotnet-required }}",
            "INTEGRATION_RESULT": "${{ needs.service-backed-integrations.result }}",
            "FRESH_SUCCEEDED": "${{ " + call + ".fresh-succeeded }}",
            "ARTIFACT_ID": "${{ " + call + ".fresh-artifact-id }}",
            "ARTIFACT_NAME": "${{ " + call + ".fresh-artifact-name }}",
            "RECEIPT_SHA256": "${{ " + call + ".fresh-receipt-sha256 }}",
        })
        self.assertIn('if [[ "$INTEGRATION_RESULT" != "success" ]]; then', step["run"])
        self.assertIn("exit 1", step["run"])
        self.assertNotIn("continue-on-error", gate)
        # Release callers read the gate's validated outputs, never the raw call outputs.
        for field in ("artifact-id", "artifact-name", "receipt-sha256"):
            self.assertEqual(gate["outputs"][field], "${{ steps.evidence.outputs." + field + " }}")
        self.assertEqual(step["id"], "evidence")
        evidence = {"ARTIFACT_ID": "7", "ARTIFACT_NAME": "meridian-ci-integrations-1-1", "RECEIPT_SHA256": "a" * 64}

        def run_gate(integration, lane="true", decided="true", fresh="true", **overrides):
            with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temporary:
                output = Path(temporary) / "output"
                environment = dict(os.environ, DOTNET_LANE_DECISION=lane, INTEGRATION_DECISION=decided,
                                   INTEGRATION_RESULT=integration, FRESH_SUCCEEDED=fresh,
                                   GITHUB_STEP_SUMMARY=str(Path(temporary) / "summary.md"),
                                   GITHUB_OUTPUT=str(output), **dict(evidence, **overrides))
                code = subprocess.run(["bash", "-c", step["run"]], env=environment,
                                      capture_output=True, text=True, check=False).returncode
                written = output.read_text() if output.exists() else ""
                return code, written

        code, written = run_gate("success")
        self.assertEqual(code, 0)
        self.assertEqual(written, "artifact-id=7\nartifact-name=meridian-ci-integrations-1-1\n"
                                  f"receipt-sha256={'a' * 64}\n")
        # The only accepted docs-only result: the integration lane succeeded, classified the
        # change as docs-only, and verify-dotnet independently reached the same decision.
        empty = {"ARTIFACT_ID": "", "ARTIFACT_NAME": "", "RECEIPT_SHA256": ""}
        self.assertEqual(run_gate("success", lane="false", decided="false", **empty), (0, ""))
        for integration, lane, decided in (
                ("failure", "true", "true"), ("cancelled", "true", "true"),
                ("skipped", "true", "true"), ("skipped", "false", "false"),
                ("success", "false", "true"), ("success", "true", "false"),
                ("success", "", "false"), ("success", "false", ""),
                ("failure", "false", "false"), ("cancelled", "false", "false")):
            with self.subTest(integration=integration, lane=lane, decided=decided):
                self.assertNotEqual(run_gate(integration, lane, decided)[0], 0)
        # evidence-gate's checks now live here: the fresh job itself must have succeeded (a
        # reusable call whose jobs were all skipped still reports success) and a .NET run must
        # bind complete, well-formed evidence.
        for fresh in ("", "false", "success", "True"):
            with self.subTest(fresh=fresh):
                self.assertNotEqual(run_gate("success", fresh=fresh)[0], 0)
        for field, bad in (("ARTIFACT_ID", ""), ("ARTIFACT_ID", "0"), ("ARTIFACT_ID", "7x"),
                           ("ARTIFACT_NAME", ""), ("ARTIFACT_NAME", "-lead"), ("ARTIFACT_NAME", "a b"),
                           ("RECEIPT_SHA256", ""), ("RECEIPT_SHA256", "A" * 64), ("RECEIPT_SHA256", "a" * 63)):
            with self.subTest(field=field, bad=bad):
                self.assertEqual(run_gate("success", **{field: bad}), (1, ""))
        self.assertEqual(self.ci["jobs"]["quality-gate"]["needs"],
                         ["verify-dotnet", "verify-browser", "verify-docs", "verify-workflows"])

    def test_meridian_ci_verifies_evidence_in_its_gate_to_save_a_runner_hand_off(self):
        caller = self.ci["jobs"]["service-backed-integrations"]
        self.assertEqual(caller["with"]["caller-verifies-evidence"], "true")
        option = self.shared["on"]["workflow_call"]["inputs"]["caller-verifies-evidence"]
        self.assertEqual((option["type"], option["default"]), ("boolean", "false"))
        outputs = self.shared["on"]["workflow_call"]["outputs"]
        self.assertEqual(outputs["fresh-succeeded"]["value"], "${{ jobs.deterministic-integrations.outputs.succeeded }}")
        job = self.shared["jobs"]["deterministic-integrations"]
        self.assertEqual(job["outputs"]["succeeded"], "${{ steps.completed.outputs.succeeded }}")
        completed = job["steps"][-1]
        self.assertEqual(completed["id"], "completed")
        self.assertNotIn("if", completed, "Implicit success(): set only when every earlier step succeeded.")
        self.assertEqual(completed["run"], 'echo "succeeded=true" >> "$GITHUB_OUTPUT"')
        for field in ("artifact-id", "artifact-name", "receipt-sha256"):
            self.assertEqual(outputs[f"fresh-{field}"]["value"],
                             "${{ jobs.deterministic-integrations.outputs." + field + " }}")
            # Certification and release callers keep evidence-gate's verified outputs.
            self.assertEqual(outputs[field]["value"], "${{ jobs.evidence-gate.outputs." + field + " }}")
        condition = self.shared["jobs"]["evidence-gate"]["if"]
        self.assertTrue(condition.startswith("always() && (!inputs.caller-verifies-evidence"))
        for field in ("artifact-id", "artifact-name", "receipt-sha256", "result"):
            self.assertIn(f"inputs.reuse-{field} != ''", condition, "Reuse runs always keep evidence-gate.")
        self.assertNotIn("caller-verifies-evidence", self.production["jobs"]["deterministic-integrations"].get("with", {}))

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
        self.assertEqual(receipt["if"], "steps.changes.outputs.dotnet == 'true'",
                         "Only create passing receipts after all test/schema steps succeed.")

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
