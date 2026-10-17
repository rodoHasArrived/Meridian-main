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

    def test_publish_smoke_only_installs_node_for_browser_publish(self) -> None:
        workflow = PUBLISH_SMOKE.read_text(encoding="utf-8")
        setup = workflow.split("      - name: Set up Node.js\n", 1)[1].split("      - name:", 1)[0]
        self.assertIn("if: inputs.project == 'web-workstation'", setup)
        self.assertIn("cache-dependency-path: src/Meridian.Ui/dashboard/package-lock.json", setup)
        for step_name in ("Run publish script", "Generate release evidence manifest"):
            with self.subTest(step=step_name):
                step = workflow.split(f"      - name: {step_name}\n", 1)[1].split("      - name:", 1)[0]
                self.assertNotIn("        if:", step)
        self.assertIn("if: inputs.project == 'web-workstation' && inputs.runtime == 'win-x64'", workflow)
        self.assertIn("Start the published WebWorkstation artifact with required authentication", workflow)

    def test_desktop_installer_uploads_release_evidence_manifest(self) -> None:
        workflow = DESKTOP_INSTALLER.read_text(encoding="utf-8")

        self.assertIn("Generate release evidence manifest", workflow)
        self.assertIn("--project desktop-installer", workflow)
        self.assertIn("--output artifacts/release/${{ matrix.runtime }}/desktop-installer-${{ matrix.runtime }}-release-evidence.json", workflow)
        self.assertIn("verify-release-promotion.py", workflow)
        self.assertIn("files: artifacts/publish-release/*", workflow)

    def test_payload_consumers_resolve_approval_before_building_and_record_receipt(self) -> None:
        consumers = (
            (DESKTOP_INSTALLER, "build-consumer-setup.ps1"),
            (DESKTOP_EVALUATION, "build-consumer-setup.ps1"),
            (PUBLISH_SMOKE, "build/scripts/publish/publish.ps1"),
        )
        for path, build_script in consumers:
            with self.subTest(workflow=path.name):
                workflow = path.read_text(encoding="utf-8")
                self.assertIn("build/scripts/install/resolve-postgresql-payload.ps1", workflow)
                self.assertLess(workflow.index("resolve-postgresql-payload.ps1"), workflow.index(build_script))
                self.assertIn("--postgresql-payload-receipt", workflow)
                self.assertNotIn("Get-ChildItem 'C:\\Program Files\\PostgreSQL'", workflow)
                self.assertNotIn("Sort-Object { [int]($_.Name", workflow)

    def test_installed_startup_and_upgrade_certification_keep_the_payload_evidence_chain(self) -> None:
        smoke = PUBLISH_SMOKE.read_text(encoding="utf-8")
        self.assertIn("Assert-PostgreSqlPayload -PayloadRoot artifacts/postgresql-payload", smoke)
        self.assertIn("smoke-web-workstation-install.ps1", smoke)
        self.assertIn("artifacts/postgresql-payload/win-x64-payload.json", smoke)
        installer = DESKTOP_INSTALLER.read_text(encoding="utf-8")
        self.assertIn("certify-consumer-install-lifecycle.ps1", installer)
        self.assertIn("consumer-setup-win-x64-postgresql-payload.json", installer)

    def test_consumer_checksums_only_include_published_assets(self) -> None:
        workflow = DESKTOP_INSTALLER.read_text(encoding="utf-8")
        checksums = workflow.split("      - name: Generate consumer checksums\n", 1)[1].split("      - name:", 1)[0]
        upload = workflow.split("      - name: Upload consumer setup artifact\n", 1)[1].split("      - name:", 1)[0]
        published_assets = (
            "Meridian-Setup.exe",
            "consumer-setup-win-x64-sbom.spdx.json",
            "consumer-setup-win-x64-release-evidence.json",
        )
        approved_names = ", ".join(f"'{name}'" for name in published_assets)
        self.assertIn(f"$_.Name -in @({approved_names})", checksums)
        self.assertNotIn("-like", checksums)
        self.assertNotIn("*", checksums)
        self.assertNotIn("postgresql-payload.json", checksums)
        for name in published_assets:
            self.assertIn(f"artifacts/consumer-setup/{name}", upload)

    def test_consumer_certification_is_a_separate_required_promotion_gate(self) -> None:
        workflow = DESKTOP_INSTALLER.read_text(encoding="utf-8")
        consumer = self._job_block(workflow, "certify-installed-consumer")
        release = self._job_block(workflow, "release")

        self.assertIn("needs: [eligibility, build-consumer-setup]", consumer)
        self.assertIn("runs-on: windows-latest", consumer)
        self.assertIn("ref: ${{ github.sha }}", consumer)
        self.assertIn("name: meridian-consumer-setup-${{ github.run_id }}-${{ github.run_attempt }}", consumer)
        self.assertIn("-CurrentPackage artifacts/current-consumer/Meridian-Setup.exe", consumer)
        self.assertIn("certify-consumer-install-lifecycle.ps1", consumer)
        self.assertIn("resolve-consumer-predecessor.py", consumer)
        self.assertIn("-PredecessorEvidencePath artifacts/consumer-certification/consumer-predecessor.json", consumer)
        self.assertIn("consumer-setup-win-x64-lifecycle.json", consumer)
        self.assertIn("if: always()", consumer)
        self.assertIn("retention-days: 90", consumer)
        self.assertNotIn("TrustSelfSignedRoot", consumer)
        for block in self._shell_blocks(consumer):
            self.assertNotIn("${{", block)
        needs_line = next(line for line in release.splitlines() if line.strip().startswith("needs:"))
        self.assertIn("certify-installed-consumer", needs_line)
        self.assertIn("name: consumer-install-certification-win-x64-${{ github.run_id }}-${{ github.run_attempt }}", release)
        self.assertIn("path: artifacts/certification", release)

    def test_robinhood_smoke_uses_named_powershell_splatting(self) -> None:
        workflow = ROBINHOOD_OPTIONS_SMOKE.read_text(encoding="utf-8")

        self.assertIn("$smokeArgs = @{", workflow)
        self.assertIn("Configuration = '${{ inputs.configuration }}'", workflow)
        self.assertIn("$smokeArgs.SkipBuild = $true", workflow)
        self.assertNotIn("@('-Configuration'", workflow)


if __name__ == "__main__":
    unittest.main()
