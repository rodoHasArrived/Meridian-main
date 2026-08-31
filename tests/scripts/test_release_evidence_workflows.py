from __future__ import annotations

import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
PUBLISH_SMOKE = REPO_ROOT / ".github" / "workflows" / "publish-smoke.yml"
DESKTOP_INSTALLER = REPO_ROOT / ".github" / "workflows" / "desktop-installer-packaging.yml"
DESKTOP_EVALUATION = REPO_ROOT / ".github" / "workflows" / "desktop-evaluation-prerelease.yml"
ROBINHOOD_OPTIONS_SMOKE = REPO_ROOT / ".github" / "workflows" / "robinhood-options-smoke.yml"


class ReleaseEvidenceWorkflowTests(unittest.TestCase):
    @staticmethod
    def _shell_blocks(workflow: str) -> list[str]:
        lines = workflow.splitlines()
        blocks: list[str] = []
        index = 0
        while index < len(lines):
            stripped = lines[index].lstrip()
            key_text = stripped
            if key_text.startswith("- "):
                key_text = key_text[2:].lstrip()
            if not key_text.startswith("run:"):
                index += 1
                continue

            value = key_text.removeprefix("run:").strip()
            if not value.startswith(("|", ">")):
                blocks.append(value)
                index += 1
                continue

            run_indent = len(lines[index]) - len(stripped)
            index += 1
            body: list[str] = []
            while index < len(lines):
                line = lines[index]
                if line.strip() and len(line) - len(line.lstrip()) <= run_indent:
                    break
                body.append(line)
                index += 1
            blocks.append("\n".join(body))

        return blocks

    @staticmethod
    def _job_block(workflow: str, job_name: str) -> str:
        lines = workflow.splitlines()
        marker = f"  {job_name}:"
        try:
            start = lines.index(marker)
        except ValueError as exc:
            raise AssertionError(f"Workflow job not found: {job_name}") from exc

        end = start + 1
        while end < len(lines):
            line = lines[end]
            if line.strip() and len(line) - len(line.lstrip()) <= 2:
                break
            end += 1
        return "\n".join(lines[start:end])

    def test_desktop_evaluation_validates_version_and_keeps_expressions_out_of_shell(self) -> None:
        workflow = DESKTOP_EVALUATION.read_text(encoding="utf-8")

        self.assertIn("EVALUATION_VERSION: ${{ inputs.version }}", workflow)
        self.assertIn(
            r"\A[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z",
            workflow,
        )
        self.assertIn("if ($env:EVALUATION_VERSION -cnotmatch $versionPattern)", workflow)
        self.assertNotIn("MDC_PACKAGE_VERSION: ${{ inputs.version }}", workflow)

        shell_blocks = self._shell_blocks(workflow)
        self.assertTrue(shell_blocks)
        for block in shell_blocks:
            self.assertNotIn("${{", block)

        for artifact_job in ("package", "package-consumer-setup"):
            self.assertIn("\n    needs: preflight", self._job_block(workflow, artifact_job))

    def test_shell_block_scanner_includes_inline_run_scalars(self) -> None:
        workflow = """
jobs:
  test:
    steps:
      - run: echo ${{ inputs.version }}
      - run: |-
          echo safe
"""

        self.assertEqual(
            ["echo ${{ inputs.version }}", "          echo safe"],
            self._shell_blocks(workflow),
        )

    def test_publish_smoke_generates_release_evidence_manifest(self) -> None:
        workflow = PUBLISH_SMOKE.read_text(encoding="utf-8")

        self.assertIn("Generate release evidence manifest", workflow)
        self.assertIn("build/scripts/ci/generate-release-evidence-manifest.py", workflow)
        self.assertIn("--output artifacts/publish/publish-smoke/release-evidence.json", workflow)
        self.assertIn("release-evidence.json", workflow)

    def test_desktop_installer_uploads_release_evidence_manifest(self) -> None:
        workflow = DESKTOP_INSTALLER.read_text(encoding="utf-8")

        self.assertIn("Generate release evidence manifest", workflow)
        self.assertIn("--project desktop-installer", workflow)
        self.assertIn("--output artifacts/release/${{ matrix.runtime }}/release-evidence.json", workflow)
        self.assertIn("artifacts/release/**/release-evidence.json", workflow)

    def test_robinhood_smoke_uses_named_powershell_splatting(self) -> None:
        workflow = ROBINHOOD_OPTIONS_SMOKE.read_text(encoding="utf-8")

        self.assertIn("$smokeArgs = @{", workflow)
        self.assertIn("Configuration = '${{ inputs.configuration }}'", workflow)
        self.assertIn("$smokeArgs.SkipBuild = $true", workflow)
        self.assertNotIn("@('-Configuration'", workflow)


if __name__ == "__main__":
    unittest.main()
