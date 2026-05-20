from __future__ import annotations

import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "dev" / "SharedCheckpoint.ps1"


class SharedCheckpointTests(unittest.TestCase):
    def test_checkpoint_resume_requires_existing_artifacts(self) -> None:
        pwsh = shutil.which("pwsh") or shutil.which("powershell")
        if pwsh is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            checkpoint_path = temp_path / "workflow.checkpoint.json"
            artifact_path = temp_path / "evidence.json"
            artifact_path.write_text("{}", encoding="utf-8")

            result = self._run_checkpoint_probe(
                pwsh,
                checkpoint_path=checkpoint_path,
                artifact_path=artifact_path,
                remove_artifact=True,
            )

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual("True", result.stdout.strip())

    def test_checkpoint_resume_rejects_forged_success_without_artifacts(self) -> None:
        pwsh = shutil.which("pwsh") or shutil.which("powershell")
        if pwsh is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            checkpoint_path = temp_path / "workflow.checkpoint.json"
            artifact_path = temp_path / "evidence.json"

            result = self._run_checkpoint_probe(
                pwsh,
                checkpoint_path=checkpoint_path,
                artifact_path=artifact_path,
                forge_success=True,
            )

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual("True", result.stdout.strip())

    @staticmethod
    def _run_checkpoint_probe(
        powershell: str,
        *,
        checkpoint_path: Path,
        artifact_path: Path,
        remove_artifact: bool = False,
        forge_success: bool = False,
    ) -> subprocess.CompletedProcess[str]:
        command = [powershell, "-NoProfile"]
        if Path(powershell).name.lower().startswith("powershell"):
            command.extend(["-ExecutionPolicy", "Bypass"])

        if forge_success:
            setup = (
                "$checkpoint = Initialize-MeridianCheckpoint "
                f"-Workflow 'test' -CheckpointPath '{checkpoint_path}' -InputObject @{{ scenario = 'forge' }}; "
                "$checkpoint.Data.steps['validate'] = [ordered]@{ "
                "state = 'succeeded'; "
                "inputHash = $checkpoint.Data.inputHash; "
                "artifactPointers = @() "
                "}; "
                "Save-MeridianCheckpointContext -Context $checkpoint; "
            )
        else:
            setup = (
                "$checkpoint = Initialize-MeridianCheckpoint "
                f"-Workflow 'test' -CheckpointPath '{checkpoint_path}' -InputObject @{{ scenario = 'artifact' }}; "
                "Start-MeridianCheckpointStep -Context $checkpoint -StepId 'validate' -Description 'Validate evidence'; "
                f"Complete-MeridianCheckpointStep -Context $checkpoint -StepId 'validate' -ArtifactPointers @('{artifact_path}'); "
            )
            if remove_artifact:
                setup += f"Remove-Item -LiteralPath '{artifact_path}' -Force; "

        command.extend(
            [
                "-Command",
                (
                    "$ErrorActionPreference = 'Stop'; "
                    f". '{SCRIPT_PATH}'; "
                    f"{setup}"
                    "$reloaded = Initialize-MeridianCheckpoint "
                    f"-Workflow 'test' -CheckpointPath '{checkpoint_path}' -InputObject $checkpoint.Data.inputSnapshot; "
                    "Test-MeridianCheckpointStepShouldRun -Context $reloaded -StepId 'validate'"
                ),
            ]
        )
        return subprocess.run(command, capture_output=True, text=True)


if __name__ == "__main__":
    unittest.main()
