from __future__ import annotations

import re
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "meridian-ci.yml"
LANES = ("verify-dotnet", "verify-browser", "verify-docs", "verify-workflows")
# The docs and workflow lanes share one job; each lane is still its own scripts/ci.sh run.
LANE_JOBS = {
    "verify-dotnet": "verify-dotnet",
    "verify-browser": "verify-browser",
    "verify-docs": "verify-docs-workflows",
    "verify-workflows": "verify-docs-workflows",
}


class MeridianCiCacheContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def _job_block(self, job_name: str) -> str:
        match = re.search(
            rf"^  {re.escape(job_name)}:\s*\n(?:(?!^  [\w-]+:).)*",
            self.workflow,
            flags=re.MULTILINE | re.DOTALL,
        )
        self.assertIsNotNone(match, f"Missing required job: {job_name}")
        return match.group()

    def _step_block(self, job: str, step_name: str) -> str:
        match = re.search(
            rf"^      - name: {re.escape(step_name)}\s*\n(?:(?!^      - ).)*",
            job,
            flags=re.MULTILINE | re.DOTALL,
        )
        self.assertIsNotNone(match, f"Missing required step: {step_name}")
        return match.group()

    def test_nuget_cache_hash_covers_case_sensitive_config_and_shared_build_inputs(self) -> None:
        cache = self._step_block(self._job_block("verify-dotnet"), "Cache NuGet packages")
        hash_call = re.search(r"hashFiles\(([^)]+)\)", cache)
        self.assertIsNotNone(hash_call, "NuGet cache must track dependency inputs")
        patterns = set(re.findall(r"['\"]([^'\"]+)['\"]", hash_call.group(1)))

        # Check directory entries, since Path.exists() alone cannot catch a Linux-only
        # case mismatch when this regression test runs on Windows.
        self.assertIn("NuGet.Config", {entry.name for entry in REPO_ROOT.iterdir()})
        self.assertIn("**/NuGet.Config", patterns)
        self.assertNotIn("nuget.config", patterns)
        self.assertTrue((REPO_ROOT / "tests" / "Directory.Build.props").is_file())
        for input_pattern in (
            "**/*.csproj",
            "**/*.fsproj",
            "**/*.props",
            "**/*.targets",
            "**/packages.lock.json",
            "**/*.packages.lock.json",
            "global.json",
        ):
            with self.subTest(input=input_pattern):
                self.assertIn(input_pattern, patterns)

    def test_cache_hits_cannot_skip_named_validation_lanes(self) -> None:
        for lane, step_name in (
            ("verify-dotnet", "Run .NET lane"),
            ("verify-browser", "Run browser workstation lane"),
            ("verify-docs", "Run docs/source/AI lane"),
            ("verify-workflows", "Run workflow hygiene lane"),
        ):
            with self.subTest(lane=lane):
                job = self._job_block(LANE_JOBS[lane])
                step = self._step_block(job, step_name)
                self.assertNotRegex(job, r"(?m)^    (?:if|needs):")
                self.assertNotRegex(job, r"(?m)^\s+if:.*cache")
                if lane == "verify-dotnet":
                    # Only the docs-only change decision may skip the .NET lane.
                    self.assertIn("        if: steps.changes.outputs.dotnet == 'true'\n", step)
                elif lane == "verify-workflows":
                    # It shares a job with the docs lane and must still run when that lane fails;
                    # only cancellation may skip it.
                    self.assertIn("        if: ${{ !cancelled() }}\n", step)
                    self.assertEqual(len(re.findall(r"(?m)^        if:", step)), 1)
                else:
                    self.assertNotRegex(step, r"(?m)^        if:")
                self.assertRegex(
                    step,
                    rf"(?m)^        run: bash scripts/ci\.sh --lane {re.escape(lane)}\s*$",
                )

    def test_pip_cache_restores_downloads_and_still_installs_dependencies(self) -> None:
        job = self._job_block("verify-docs-workflows")
        setup = self._step_block(job, "Set up Python")
        install = self._step_block(job, "Install docs script dependencies")
        self.assertIn("cache: pip", setup)
        self.assertIn("build/scripts/docs/requirements.txt", setup)
        self.assertIn("build/scripts/ci/requirements.txt", setup)
        self.assertNotRegex(install, r"(?m)^        if:")
        self.assertRegex(
            install,
            r"(?m)^        run: python -m pip install --requirement build/scripts/ci/requirements\.txt\s*$",
        )
        # One install serves both lanes, so the CI requirements must keep the docs ones.
        ci_requirements = (REPO_ROOT / "build/scripts/ci/requirements.txt").read_text(encoding="utf-8")
        self.assertRegex(ci_requirements, r"(?m)^-r \.\./docs/requirements\.txt\s*$")
        self.assertLess(job.index(setup), job.index(install))
        for lane in ("verify-docs", "verify-workflows"):
            with self.subTest(lane=lane):
                self.assertLess(job.index(install), job.index(f"bash scripts/ci.sh --lane {lane}"))

    def test_browser_upload_contains_actual_vite_output_and_failure_logs(self) -> None:
        dashboard = REPO_ROOT / "src" / "Meridian.Ui" / "dashboard"
        vite_config = (dashboard / "vite.config.ts").read_text(encoding="utf-8")
        out_dir = re.search(r"\boutDir:\s*['\"]([^'\"]+)['\"]", vite_config)
        self.assertIsNotNone(out_dir, "Vite build output must have an explicit location")
        build_path = (dashboard / out_dir.group(1)).resolve().relative_to(REPO_ROOT).as_posix()

        upload = self._step_block(
            self._job_block("verify-browser"), "Upload browser lane artifacts"
        )
        self.assertIn(f"            {build_path}/\n", upload)
        self.assertIn("            artifacts/build-logs/\n", upload)
        self.assertIn("            artifacts/ci-summary/\n", upload)
        self.assertIn("        if: always()", upload)
        self.assertNotIn("src/Meridian.Ui/dashboard/dist/", upload)

    def test_quality_gate_requires_lanes_or_explicit_docs_only_skip(self) -> None:
        gate = self._job_block("quality-gate")
        dependencies = re.search(r"(?m)^    needs:\n((?:      - [\w-]+\n)+)", gate)
        self.assertIsNotNone(dependencies)
        self.assertEqual(set(re.findall(r"- ([\w-]+)", dependencies.group(1))), set(LANE_JOBS.values()))
        self.assertRegex(gate, r"(?m)^    name: quality-gate$")
        self.assertRegex(gate, r"(?m)^    if: always\(\)$")
        for job in set(LANE_JOBS.values()):
            with self.subTest(job=job):
                self.assertIn("${{ needs." + job + ".result }}", gate)
        # The shared job reports each lane's own step outcome.
        self.assertIn("${{ needs.verify-docs-workflows.outputs.docs-result }}", gate)
        self.assertIn("${{ needs.verify-docs-workflows.outputs.workflows-result }}", gate)
        shared = self._job_block("verify-docs-workflows")
        self.assertIn("docs-result: ${{ steps.docs.outcome }}", shared)
        self.assertIn("workflows-result: ${{ steps.workflows.outcome }}", shared)
        self.assertIn(f"for lane in {' '.join(LANES)}; do", gate)
        self.assertIn('"$result" == "success" && "$DOTNET_LANE_DECISION" == "false"', gate)
        self.assertIn('if [[ "$result" != "success" ]]; then\n                failed=1', gate)
        self.assertIn('if [[ "$DOTNET_LANE_DECISION" != "true" && "$DOTNET_LANE_DECISION" != "false" ]]; then', gate)
        self.assertIn('if [[ "$failed" -ne 0 ]]; then', gate)
        self.assertIn("            exit 1", gate)
        self.assertNotRegex(self.workflow, r"(?m)^\s*(?:paths|paths-ignore|continue-on-error):")


if __name__ == "__main__":
    unittest.main()
