from __future__ import annotations

import copy
import hashlib
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
    @staticmethod
    def payload_receipt() -> dict:
        paths = [
            "bin/initdb.exe", "bin/pg_ctl.exe", "bin/pg_dump.exe", "bin/pg_restore.exe",
            "bin/postgres.exe", "bin/psql.exe", "lib/postgresql/example.dll",
        ]
        files = [
            {"path": path, "sizeBytes": len(path), "sha256": hashlib.sha256(path.encode()).hexdigest()}
            for path in paths
        ]
        canonical = "".join(f"{entry['sha256']}  {entry['path']}\n" for entry in files)
        return {
            "schemaVersion": 1,
            "runtime": "win-x64",
            "version": "17.11",
            "source": {
                "kind": "github-hosted-runner",
                "path": r"C:\Program Files\PostgreSQL\17",
                "reference": "https://github.com/actions/runner-images/blob/" + "a" * 40 + "/images/windows/Windows2022-Readme.md",
            },
            "approvalSha256": "a" * 64,
            "payloadSha256": hashlib.sha256(canonical.encode()).hexdigest(),
            "files": files,
            "tools": [{"path": path, "version": "17.11"} for path in paths[:6]],
            "runner": {"imageOS": "win22", "imageVersion": "20261001.1.0"},
        }

    @staticmethod
    def arguments(root: Path, *extra: str):
        return MODULE.parse_args([
            "--project", "consumer-setup", "--runtime", "win-x64",
            "--artifact-root", str(root), "--output", str(root / "release-evidence.json"),
            "--commit-sha", "abc123", *extra,
        ])

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
        self.assertNotIn("postgresqlPayloads", payload)

    def test_manifest_embeds_complete_receipts_and_their_hashes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            receipts = []
            for index in range(2):
                receipt = self.payload_receipt()
                # Inventory order is immaterial; the aggregate digest uses ordinal path order.
                receipt["files"].reverse()
                receipt["additionalProducerEvidence"] = {"index": index}
                path = root / f"postgresql-{index}.json"
                path.write_text(json.dumps(receipt), encoding="utf-8")
                receipts.append((path, receipt))
            args = self.arguments(root, *[
                value for path, _ in receipts
                for value in ("--postgresql-payload-receipt", str(path))
            ])

            manifest = MODULE.build_manifest(args)

            self.assertEqual(manifest["schemaVersion"], 1)
            self.assertEqual(len(manifest["postgresqlPayloads"]), 2)
            for embedded, (path, receipt) in zip(manifest["postgresqlPayloads"], receipts):
                self.assertEqual(embedded, {
                    **receipt, "receiptPath": path.as_posix(),
                    "receiptSha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                })

    def test_consumer_and_installed_startup_evidence_require_payload_receipt(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for extra in ((), ("--project", "web-workstation", "--validation-lane", "web-workstation-installed-startup")):
                with self.subTest(extra=extra), self.assertRaisesRegex(ValueError, "receipt is required"):
                    MODULE.build_manifest(self.arguments(root, *extra))
            manifest = MODULE.build_manifest(self.arguments(root, "--project", "web-workstation"))
            self.assertNotIn("postgresqlPayloads", manifest)

    def test_unavailable_or_unreadable_json_receipt_does_not_write_manifest(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = root / "postgresql.json"
            args = [
                "--project", "consumer-setup", "--runtime", "win-x64",
                "--artifact-root", str(root), "--output", str(root / "release-evidence.json"),
                "--postgresql-payload-receipt", str(path),
            ]
            with self.assertRaises(FileNotFoundError):
                MODULE.main(args)
            self.assertFalse((root / "release-evidence.json").exists())
            path.write_text("{broken json", encoding="utf-8")
            with self.assertRaises(json.JSONDecodeError):
                MODULE.main(args)
            self.assertFalse((root / "release-evidence.json").exists())
            receipt = self.payload_receipt()
            receipt["payloadSha256"] = "b" * 64
            path.write_text(json.dumps(receipt), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "canonical file inventory"):
                MODULE.main(args)
            self.assertFalse((root / "release-evidence.json").exists())

    def test_canonical_digest_uses_case_sensitive_utf16_ordinal_path_order(self) -> None:
        receipt = self.payload_receipt()
        # This order differs from both case-insensitive and Unicode code-point ordering.
        extra_paths = ["share/Z.txt", "share/a.txt", "share/\U00010000.txt", "share/\ue000.txt"]
        ordered = receipt["files"] + [
            {"path": path, "sizeBytes": 1, "sha256": "b" * 64} for path in extra_paths
        ]
        canonical = "".join(f"{entry['sha256']}  {entry['path']}\n" for entry in ordered)
        receipt["payloadSha256"] = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
        receipt["files"] = list(reversed(ordered))
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "postgresql.json"
            path.write_text(json.dumps(receipt), encoding="utf-8")
            embedded = MODULE.load_postgresql_payload_receipt(path, "win-x64")
        self.assertEqual(embedded["payloadSha256"], receipt["payloadSha256"])

    def test_rejects_invalid_or_inconsistent_payload_receipt(self) -> None:
        cases = [
            ("schemaVersion", 2), ("schemaVersion", True), ("runtime", "win-arm64"),
            ("version", "17"), ("version", "latest"), ("source", None),
            ("source", {"kind": "github-hosted-runner", "path": "", "reference": ""}),
            ("source", {"kind": "unknown", "path": "payload", "reference": "source"}),
            ("approvalSha256", "not a hash"), ("payloadSha256", "b" * 64),
            ("files", []), ("files", [{}]), ("runner", {}), ("tools", []),
        ]
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "postgresql.json"
            for field, value in cases:
                with self.subTest(field=field, value=value):
                    receipt = self.payload_receipt()
                    receipt[field] = value
                    path.write_text(json.dumps(receipt), encoding="utf-8")
                    with self.assertRaises(ValueError):
                        MODULE.load_postgresql_payload_receipt(path, "win-x64")

    def test_rejects_unsafe_duplicate_and_mismatched_file_or_tool_entries(self) -> None:
        receipt = self.payload_receipt()
        cases = []
        for unsafe in ("../postgres.exe", "/bin/postgres.exe", r"bin\postgres.exe", "C:/postgres.exe",
                       "bin/./postgres.exe", "bin//postgres.exe", "bin/NUL", "bin/postgres.exe "):
            altered = copy.deepcopy(receipt)
            altered["files"][0]["path"] = unsafe
            cases.append((unsafe, altered))
        for field, value in (("sizeBytes", -1), ("sizeBytes", True), ("sha256", "corrupt")):
            altered = copy.deepcopy(receipt)
            altered["files"][0][field] = value
            cases.append((field, altered))
        altered = copy.deepcopy(receipt)
        altered["files"].append({**altered["files"][0], "path": "BIN/INITDB.EXE"})
        cases.append(("duplicate file", altered))
        altered = copy.deepcopy(receipt)
        altered["files"][0]["sha256"] = "b" * 64
        cases.append(("changed file hash", altered))
        altered = copy.deepcopy(receipt)
        altered["tools"][0]["version"] = "17.10"
        cases.append(("tool version", altered))
        altered = copy.deepcopy(receipt)
        altered["tools"][0]["path"] = "bin/missing.exe"
        cases.append(("uninventoried tool", altered))
        altered = copy.deepcopy(receipt)
        altered["tools"].append(altered["tools"][0])
        cases.append(("duplicate tool", altered))
        altered = copy.deepcopy(receipt)
        altered["tools"].pop()
        cases.append(("missing required tool", altered))

        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "postgresql.json"
            for name, altered in cases:
                with self.subTest(name=name):
                    path.write_text(json.dumps(altered), encoding="utf-8")
                    with self.assertRaises(ValueError):
                        MODULE.load_postgresql_payload_receipt(path, "win-x64")

    def test_receipt_runtime_must_match_manifest(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = root / "postgresql.json"
            path.write_text(json.dumps(self.payload_receipt()), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "match the manifest runtime"):
                MODULE.build_manifest(self.arguments(
                    root, "--postgresql-payload-receipt", str(path), "--runtime", "win-arm64",
                ))


if __name__ == "__main__":
    unittest.main()
