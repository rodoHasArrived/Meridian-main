from __future__ import annotations

import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
PUBLISH_SMOKE = REPO_ROOT / ".github" / "workflows" / "publish-smoke.yml"
DESKTOP_INSTALLER = REPO_ROOT / ".github" / "workflows" / "desktop-installer-packaging.yml"
ROBINHOOD_OPTIONS_SMOKE = REPO_ROOT / ".github" / "workflows" / "robinhood-options-smoke.yml"


class ReleaseEvidenceWorkflowTests(unittest.TestCase):
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
