from __future__ import annotations

import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "meridian-ci.yml"


class MeridianCiWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def test_quality_gate_is_stable_aggregator(self) -> None:
        self.assertIn("quality-gate:", self.workflow)
        self.assertIn("name: quality-gate", self.workflow)
        self.assertIn("needs:", self.workflow)
        self.assertIn("- verify-dotnet", self.workflow)
        self.assertIn("- verify-browser", self.workflow)
        self.assertIn("- verify-docs", self.workflow)
        self.assertIn("- verify-workflows", self.workflow)
        self.assertIn("Evaluate Meridian CI lanes", self.workflow)

    def test_parallel_lanes_use_scripts_ci_named_lanes(self) -> None:
        for lane in ("verify-dotnet", "verify-browser", "verify-docs", "verify-workflows"):
            with self.subTest(lane=lane):
                self.assertIn(f"bash scripts/ci.sh --lane {lane}", self.workflow)

    def test_dotnet_lane_uses_bounded_test_concurrency_without_changing_gate(self) -> None:
        dotnet_job = self.workflow.split("  verify-dotnet:\n", 1)[1].split("  verify-browser:\n", 1)[0]
        self.assertIn('MERIDIAN_CI_TEST_MAX_PARALLEL: "2"', dotnet_job)
        self.assertIn("bash scripts/ci.sh --lane verify-dotnet", dotnet_job)
        self.assertNotIn("continue-on-error", dotnet_job)
        gate = self.workflow.split("  quality-gate:\n", 1)[1]
        self.assertIn("- verify-dotnet", gate)
        self.assertIn('if [[ "$result" != "success" ]]', gate)

    def test_lane_artifacts_are_uploaded(self) -> None:
        self.assertIn("artifacts/ci-summary/", self.workflow)
        self.assertIn("artifacts/build-logs/", self.workflow)
        self.assertIn("artifacts/test-results/dotnet/", self.workflow)
        self.assertIn("src/Meridian.Ui/wwwroot/workstation/", self.workflow)

    def test_browser_lane_runs_full_dashboard_contract_checks(self) -> None:
        self.assertIn("bash scripts/ci.sh --lane verify-browser", self.workflow)

        ci_script = (REPO_ROOT / "scripts" / "ci.sh").read_text(encoding="utf-8")
        self.assertIn("Generate UI contracts", ci_script)
        self.assertIn("npm --prefix src/Meridian.Ui/dashboard run lint", ci_script)
        self.assertIn("npm --prefix src/Meridian.Ui/dashboard run typecheck:strict", ci_script)
        self.assertIn("npm --prefix src/Meridian.Ui/dashboard run test", ci_script)

    def test_lanes_do_not_queue_behind_other_jobs(self):
        import yaml
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        jobs = workflow["jobs"]
        # Every job is a fresh hosted-runner queue wait, so the lanes run and report their
        # verdicts in one job each, with no builder or shard hop in front of them.
        for removed in ("build-workstation", "dotnet-shards", "browser-shards"):
            self.assertNotIn(removed, jobs)
        for lane in ("verify-dotnet", "verify-browser", "verify-docs", "verify-workflows"):
            with self.subTest(lane=lane):
                self.assertEqual(jobs[lane]["name"], lane)
                self.assertNotIn("needs", jobs[lane])
                self.assertNotIn("if", jobs[lane])

    def test_dotnet_lane_prepares_workstation_assets_before_msbuild(self):
        import yaml
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        steps = workflow["jobs"]["verify-dotnet"]["steps"]
        names = [step.get("name") for step in steps]
        # ensure-workstation-assets.py installs from the lockfile, builds and validates the
        # bundle inside the lane, so Node must be ready before it runs.
        self.assertLess(names.index("Set up Node.js"), names.index("Run .NET lane"))
        ci_script = (REPO_ROOT / "scripts" / "ci.sh").read_text(encoding="utf-8")
        lane = ci_script.split("verify_dotnet() {\n", 1)[1].split("\n}\n", 1)[0]
        self.assertLess(lane.index("build/scripts/ensure-workstation-assets.py"),
                        lane.index("dotnet restore Meridian.sln"))

    def test_dotnet_lane_classifies_changes_in_job_and_gate_requires_agreement(self):
        import yaml
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        job = workflow["jobs"]["verify-dotnet"]
        steps = {step["name"]: step for step in job["steps"]}
        detect = steps["Detect .NET changes"]
        self.assertEqual(detect["id"], "changes")
        self.assertIn("build/scripts/ci/detect-dotnet-changes.py", detect["run"])
        self.assertIn('echo "dotnet=true"', detect["run"])
        self.assertEqual(job["outputs"]["dotnet-required"], "${{ steps.changes.outputs.dotnet }}")
        # Only the change decision may skip work; never a cache or other signal.
        guard = "steps.changes.outputs.dotnet == 'true'"
        for name in ("Check out repository", "Set up .NET SDK", "Cache NuGet packages", "Run .NET lane"):
            self.assertEqual(steps[name]["if"], guard)
        for name in ("Require the complete roster", "Upload .NET lane artifacts"):
            self.assertEqual(steps[name]["if"], f"always() && {guard}")
        # No separate changes job: every lane would otherwise wait for one more runner.
        self.assertNotIn("changes", workflow["jobs"])
        gate = workflow["jobs"]["quality-gate"]["steps"][0]
        self.assertEqual(gate["env"]["DOTNET_LANE_DECISION"], "${{ needs.verify-dotnet.outputs.dotnet-required }}")
        self.assertIn('if [[ "$DOTNET_LANE_DECISION" != "true" && "$DOTNET_LANE_DECISION" != "false" ]]; then',
                      gate["run"])
        # The integration lane classifies independently; its gate requires the two to agree.
        integration = workflow["jobs"]["integration-gate"]["steps"][0]
        self.assertEqual(integration["env"]["DOTNET_LANE_DECISION"],
                         "${{ needs.verify-dotnet.outputs.dotnet-required }}")
        self.assertIn('if [[ "$INTEGRATION_DECISION" != "$DOTNET_LANE_DECISION" ]]; then', integration["run"])

    def test_dotnet_lane_proves_the_complete_roster(self):
        import yaml
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        job = workflow["jobs"]["verify-dotnet"]
        self.assertEqual(job["env"]["MERIDIAN_CI_DOTNET_SHARD"], "1/1")
        steps = {step["name"]: step for step in job["steps"]}
        merge = steps["Require the complete roster"]["run"]
        self.assertIn("build/scripts/ci/merge-dotnet-shard-summaries.py", merge)
        self.assertIn("--expected-shards 1", merge)
        names = list(steps)
        self.assertLess(names.index("Run .NET lane"), names.index("Require the complete roster"))
        # Release promotion requires this exact evidence name for the lane.
        self.assertEqual(
            steps["Upload .NET lane artifacts"]["with"]["name"],
            "meridian-ci-dotnet-${{ github.run_number }}-${{ github.run_attempt }}",
        )
        self.assertNotIn("continue-on-error", str(job))

    def test_first_dotnet_shard_owns_static_checks_and_unsharded_runs_keep_them(self) -> None:
        ci_script = (REPO_ROOT / "scripts" / "ci.sh").read_text(encoding="utf-8")
        lane = ci_script.split("verify_dotnet() {\n", 1)[1].split("\n}\n", 1)[0]
        self.assertIn('if [[ "$dotnet_shard_index" -le 1 ]]; then\n    verify_dotnet_static', lane)
        self.assertIn("dotnet restore Meridian.sln", lane)
        self.assertIn("build/scripts/ci/run-dotnet-ci-tests.py", lane)
        static = ci_script.split("verify_dotnet_static() {\n", 1)[1].split("\n}\n", 1)[0]
        self.assertIn("dotnet format whitespace Meridian.sln --verify-no-changes", static)
        self.assertIn("dotnet build Meridian.WebWorkstation.slnf", static)
        self.assertIn("check-file-size.py", static)
        # Unset means the whole lane: parse_lane_shard reports index 0 for it.
        self.assertIn('dotnet_shard_index="$(parse_lane_shard MERIDIAN_CI_DOTNET_SHARD verify-dotnet)" || exit 2', ci_script)

    def test_browser_lane_proves_every_test_file(self):
        import yaml
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        job = workflow["jobs"]["verify-browser"]
        self.assertEqual(job["env"]["MERIDIAN_CI_BROWSER_SHARD"], "1/1")
        steps = {step["name"]: step for step in job["steps"]}
        # The browser lane always runs: no change classification may skip any step.
        self.assertNotIn("if", steps["Run browser workstation lane"])
        merge = steps["Require every dashboard test file"]
        self.assertEqual(merge["if"], "always()")
        self.assertIn("src/Meridian.Ui/dashboard/scripts/merge-vitest-shards.mjs", merge["run"])
        self.assertIn("--expected-shards 1", merge["run"])
        upload = steps["Upload browser lane artifacts"]
        self.assertEqual(upload["if"], "always()")
        # Release promotion requires this exact evidence name for the lane.
        self.assertEqual(upload["with"]["name"],
                         "meridian-ci-browser-${{ github.run_number }}-${{ github.run_attempt }}")
        self.assertNotIn("continue-on-error", str(job))

    def test_first_browser_shard_owns_static_checks_and_unsharded_runs_keep_them(self) -> None:
        ci_script = (REPO_ROOT / "scripts" / "ci.sh").read_text(encoding="utf-8")
        lane = ci_script.split("verify_browser() {\n", 1)[1].split("\n}\n", 1)[0]
        guarded = lane.split('if [[ "$browser_shard_index" -le 1 ]]; then\n')
        self.assertEqual(len(guarded), 3)
        # Every shard installs from the lockfile, generates contracts and runs its tests.
        for always in ("npm --prefix src/Meridian.Ui/dashboard ci --include=optional", "Generate UI contracts"):
            self.assertIn(always, guarded[0])
        between = guarded[1].split("\n  fi\n", 1)
        self.assertIn("run lint", between[0])
        self.assertIn("run typecheck:strict", between[0])
        self.assertIn("npm --prefix src/Meridian.Ui/dashboard run test", between[1])
        self.assertIn('-- --shard ${MERIDIAN_CI_BROWSER_SHARD}', between[1])
        self.assertIn("run build", guarded[2])
        self.assertIn("check-workstation-assets.py", guarded[2])
        self.assertIn('browser_shard_index="$(parse_lane_shard MERIDIAN_CI_BROWSER_SHARD verify-browser)" || exit 2', ci_script)
        # The runner and the verdict share one definition of the test-file roster.
        scripts = REPO_ROOT / "src" / "Meridian.Ui" / "dashboard" / "scripts"
        for name in ("run-vitest-stable.mjs", "merge-vitest-shards.mjs"):
            self.assertIn('from "./vitest-files.mjs"', (scripts / name).read_text(encoding="utf-8"))

    def test_docs_lane_keeps_subset_receipt_out_of_tracked_status_docs(self) -> None:
        ci_script = (REPO_ROOT / "scripts" / "ci.sh").read_text(encoding="utf-8")

        self.assertIn('handoff_summary_json="${ci_summary_dir}/ai-handoff-docs-automation-summary.json"', ci_script)
        self.assertIn('--summary-json "$handoff_summary_json"', ci_script)
        self.assertNotIn("--json-output docs/status/docs-automation-summary.json", ci_script)

    def test_promtool_checksum_is_verified_before_extracting_or_running_download(self) -> None:
        install = self.workflow.split("      - name: Install promtool\n", 1)[1].split("      - name:", 1)[0]
        self.assertIn('PROMETHEUS_VERSION: "3.1.0"', install)
        self.assertIn("9a9d1e115d1745826b13aec3f1409780b9fcf1d4206746cb4faee46ca5add70c", install)
        self.assertLess(install.index("sha256sum --check"), install.index("tar -xzf"))
        self.assertIn("set -euo pipefail", install)


if __name__ == "__main__":
    unittest.main()
