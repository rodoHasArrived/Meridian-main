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
        dotnet_job = self.workflow.split("  dotnet-shards:\n", 1)[1].split("  verify-dotnet:\n", 1)[0]
        # Runner fan-out is not a process-count promotion: each runner keeps two processes.
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

    def test_dotnet_consumes_same_run_workstation_assets_before_msbuild(self):
        import yaml
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        shards = workflow["jobs"]["dotnet-shards"]
        # The .NET shards need the bundle, not the browser test verdict, so they must not
        # queue behind the browser lane. quality-gate still requires both lanes.
        self.assertEqual(shards["needs"], ["changes", "build-workstation"])
        self.assertNotIn("needs", workflow["jobs"]["browser-shards"])
        steps = shards["steps"]
        download = next(step for step in steps if step.get("name") == "Download same-revision workstation assets")
        run = next(step for step in steps if step.get("name") == "Run .NET lane")
        self.assertLess(steps.index(download), steps.index(run))
        builder = workflow["jobs"]["build-workstation"]
        self.assertNotIn("needs", builder)
        self.assertNotIn("if", builder)
        build = next(step for step in builder["steps"]
                     if step.get("name") == "Build and validate workstation assets")
        # ensure-workstation-assets.py installs from the lockfile, builds and validates.
        self.assertEqual(build["run"], "python build/scripts/ensure-workstation-assets.py")
        producer = next(step for step in builder["steps"]
                        if step.get("name") == "Upload same-revision workstation assets")
        self.assertLess(builder["steps"].index(build), builder["steps"].index(producer))
        # A failed .NET job may be rerun without rerunning its successful
        # producer. The consumer must use that producer's ID instead of the
        # consumer's newer github.run_attempt.
        self.assertEqual(download["with"]["artifact-ids"], "${{ needs.build-workstation.outputs.workstation-artifact-id }}")
        self.assertEqual(builder["outputs"]["workstation-artifact-id"], "${{ steps.workstation.outputs.artifact-id }}")
        self.assertEqual(producer["id"], "workstation")
        self.assertNotIn("name", download["with"])
        self.assertNotIn("github.run_attempt", download["with"]["artifact-ids"])
        self.assertIn("github.sha", producer["with"]["name"])
        self.assertNotIn("run-id", download["with"])
        self.assertEqual(producer["with"]["if-no-files-found"], "error")

    def test_dotnet_shards_cover_the_roster_and_report_one_verdict(self):
        import yaml
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        shards = workflow["jobs"]["dotnet-shards"]
        indices = shards["strategy"]["matrix"]["shard"]
        total = len(indices)
        self.assertEqual(indices, [str(index) for index in range(1, total + 1)])
        self.assertEqual(shards["strategy"]["fail-fast"], "false")
        self.assertEqual(shards["env"]["MERIDIAN_CI_DOTNET_SHARD"], "${{ matrix.shard }}/" + str(total))
        self.assertEqual(shards["if"], "needs.changes.outputs.dotnet == 'true'")
        # Every leg publishes its own immutable evidence ID.
        self.assertEqual(sorted(shards["outputs"]), [f"artifact-id-{index}" for index in range(1, total + 1)])
        upload = next(step for step in shards["steps"] if step.get("name") == "Upload .NET shard artifacts")
        self.assertEqual(upload["if"], "always()")
        self.assertIn("${{ matrix.shard }}", upload["with"]["name"])

        verdict = workflow["jobs"]["verify-dotnet"]
        self.assertEqual(verdict["name"], "verify-dotnet")
        self.assertEqual(verdict["needs"], ["changes", "dotnet-shards"])
        # always(): a failed or cancelled shard must fail the lane, not skip its verdict.
        self.assertEqual(verdict["if"], "always() && needs.changes.outputs.dotnet == 'true'")
        steps = {step["name"]: step for step in verdict["steps"]}
        merge = steps["Require the complete roster across shards"]["run"]
        self.assertIn("build/scripts/ci/merge-dotnet-shard-summaries.py", merge)
        self.assertIn(f"--expected-shards {total}", merge)
        self.assertNotIn("if", steps["Require the complete roster across shards"])
        download = steps["Download shard evidence"]["with"]["artifact-ids"]
        for index in range(1, total + 1):
            self.assertIn(f"needs.dotnet-shards.outputs.artifact-id-{index}", download)
        result = steps["Require every shard to succeed"]
        self.assertEqual(result["env"]["SHARDS_RESULT"], "${{ needs.dotnet-shards.result }}")
        self.assertIn('"$SHARDS_RESULT" != "success"', result["run"])
        # Release promotion requires this exact evidence name for the lane.
        self.assertEqual(
            steps["Upload .NET lane artifacts"]["with"]["name"],
            "meridian-ci-dotnet-${{ github.run_number }}-${{ github.run_attempt }}",
        )
        for job in (shards, verdict):
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

    def test_browser_shards_cover_the_test_files_and_report_one_verdict(self):
        import yaml
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        shards = workflow["jobs"]["browser-shards"]
        indices = shards["strategy"]["matrix"]["shard"]
        total = len(indices)
        self.assertEqual(indices, [str(index) for index in range(1, total + 1)])
        self.assertEqual(shards["strategy"]["fail-fast"], "false")
        self.assertEqual(shards["env"]["MERIDIAN_CI_BROWSER_SHARD"], "${{ matrix.shard }}/" + str(total))
        # The browser lane always runs: no change classification may skip it.
        self.assertNotIn("if", shards)
        self.assertNotIn("needs", shards)
        self.assertEqual(sorted(shards["outputs"]), [f"artifact-id-{index}" for index in range(1, total + 1)])
        upload = next(step for step in shards["steps"] if step.get("name") == "Upload browser shard artifacts")
        self.assertEqual(upload["if"], "always()")
        self.assertIn("${{ matrix.shard }}", upload["with"]["name"])

        verdict = workflow["jobs"]["verify-browser"]
        self.assertEqual(verdict["name"], "verify-browser")
        self.assertEqual(verdict["needs"], ["browser-shards"])
        self.assertEqual(verdict["if"], "always()")
        steps = {step["name"]: step for step in verdict["steps"]}
        merge = steps["Require every dashboard test file across shards"]["run"]
        self.assertIn("src/Meridian.Ui/dashboard/scripts/merge-vitest-shards.mjs", merge)
        self.assertIn(f"--expected-shards {total}", merge)
        self.assertNotIn("if", steps["Require every dashboard test file across shards"])
        download = steps["Download shard evidence"]["with"]["artifact-ids"]
        for index in range(1, total + 1):
            self.assertIn(f"needs.browser-shards.outputs.artifact-id-{index}", download)
        result = steps["Require every shard to succeed"]
        self.assertEqual(result["env"]["SHARDS_RESULT"], "${{ needs.browser-shards.result }}")
        self.assertIn('"$SHARDS_RESULT" != "success"', result["run"])
        # Release promotion requires this exact evidence name for the lane.
        self.assertEqual(
            steps["Upload browser lane artifacts"]["with"]["name"],
            "meridian-ci-browser-${{ github.run_number }}-${{ github.run_attempt }}",
        )
        for job in (shards, verdict):
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
