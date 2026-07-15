from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "ci" / "generate-release-evidence-manifest.py"

SPEC = importlib.util.spec_from_file_location("generate_release_evidence_manifest", SCRIPT_PATH)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules["generate_release_evidence_manifest"] = MODULE
SPEC.loader.exec_module(MODULE)


class ReleaseEvidenceManifestTests(unittest.TestCase):
    def test_manifest_records_artifact_hashes_and_validation_lanes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            artifact_root = root / "artifacts" / "release" / "win-x64"
            artifact_root.mkdir(parents=True)
            package = artifact_root / "Meridian.Desktop.msix"
            package.write_text("package", encoding="utf-8")
            output = artifact_root / "release-evidence.json"

            args = MODULE.parse_args(
                [
                    "--project",
                    "desktop",
                    "--runtime",
                    "win-x64",
                    "--artifact-root",
                    str(artifact_root),
                    "--output",
                    str(output),
                    "--version",
                    "1.0.0",
                    "--workflow-run-id",
                    "123",
                    "--validation-lane",
                    "verify-desktop-release-preflight",
                    "--commit-sha",
                    "abc123",
                ]
            )

            manifest = MODULE.build_manifest(args)
            expected_hash = MODULE.sha256_file(package)

        self.assertEqual(manifest["schemaVersion"], 1)
        self.assertEqual(manifest["commitSha"], "abc123")
        self.assertEqual(manifest["validationLanes"], ["verify-desktop-release-preflight"])
        self.assertEqual(len(manifest["files"]), 1)
        self.assertEqual(manifest["files"][0]["path"], package.as_posix())
        self.assertEqual(manifest["files"][0]["sha256"], expected_hash)

    def test_main_writes_manifest_json(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            artifact_root = root / "publish"
            artifact_root.mkdir()
            (artifact_root / "app.exe").write_text("binary", encoding="utf-8")
            output = root / "manifest.json"

            exit_code = MODULE.main(
                [
                    "--project",
                    "collector",
                    "--runtime",
                    "win-x64",
                    "--artifact-root",
                    str(artifact_root),
                    "--output",
                    str(output),
                    "--commit-sha",
                    "abc123",
                ]
            )

            payload = json.loads(output.read_text(encoding="utf-8"))

        self.assertEqual(exit_code, 0)
        self.assertEqual(payload["project"], "collector")
        self.assertEqual(payload["runtime"], "win-x64")
        self.assertEqual(len(payload["files"]), 1)


if __name__ == "__main__":
    unittest.main()
