"""Execute PostgreSQL payload approval, staging, and packaging preflight boundaries.

Fixture files deliberately are not a database distribution. Most tests replace only
the native version probe; staging, hashing, receipt validation, and rejection use
the production PowerShell implementation. Native probe tests cover that remaining
boundary separately. Installed startup and upgrade remain Windows certification.
"""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


REPO_ROOT = Path(__file__).resolve().parents[2]
LIBRARY = REPO_ROOT / "build/scripts/install/postgresql-payload.ps1"
WRAPPER = REPO_ROOT / "build/scripts/install/resolve-postgresql-payload.ps1"
BUILDER = REPO_ROOT / "build/scripts/install/build-consumer-setup.ps1"
TOOLS = ("postgres", "pg_ctl", "initdb", "psql", "pg_dump", "pg_restore")


class PostgreSqlPayloadTests(unittest.TestCase):
    def setUp(self) -> None:
        self.pwsh = shutil.which("pwsh")
        self.assertIsNotNone(self.pwsh, "PowerShell 7 is required for payload approval tests.")
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "installed" / "17"
        for component in ("bin", "lib", "share"):
            (self.source / component).mkdir(parents=True)
        for tool in TOOLS:
            (self.source / "bin" / f"{tool}.exe").write_text("17.11\n", encoding="utf-8")
        (self.source / "lib" / "postgres.dll").write_bytes(b"fixture library")
        (self.source / "share" / "postgresql.conf.sample").write_bytes(b"fixture configuration")
        self.approval = self.root / "approved.json"
        self.policy = {
            "schemaVersion": 1,
            "version": "17.11",
            "runtime": "win-x64",
            "source": {
                "kind": "github-hosted-runner",
                "path": str(self.source),
                "reference": "https://github.com/actions/runner-images/blob/" + "a" * 40
                + "/images/windows/Windows2025-Readme.md#postgresql",
            },
            "components": ["bin", "lib", "share"],
        }
        self.write_approval()
        self.output = self.root / "staged"
        self.payload = self.output / "win-x64"
        self.receipt = self.output / "win-x64-payload.json"

    def write_approval(self) -> None:
        self.approval.write_text(json.dumps(self.policy, indent=2) + "\n", encoding="utf-8")

    def invoke(self, code: str, *, probe_stub: bool = True) -> subprocess.CompletedProcess[str]:
        runner = self.root / "invoke.ps1"
        runner.write_text(
            "param([string]$Library,[string]$Approval,[string]$Output,[string]$Payload,[string]$Root)\n"
            "$ErrorActionPreference='Stop'\n"
            ". $Library\n"
            + (
                "function Get-PostgreSqlToolVersion {\n"
                "  param([string]$Path)\n"
                "  (Get-Content -LiteralPath $Path -Raw).Trim()\n"
                "}\n"
                if probe_stub else ""
            )
            + code + "\n",
            encoding="utf-8",
        )
        return subprocess.run(
            [self.pwsh, "-NoLogo", "-NoProfile", "-File", str(runner), str(LIBRARY),
             str(self.approval), str(self.output), str(self.payload), str(self.root)],
            cwd=REPO_ROOT, capture_output=True, text=True, timeout=45, check=False,
        )

    def resolve(self, *, runtime: str = "win-x64") -> subprocess.CompletedProcess[str]:
        return self.invoke(
            "Resolve-PostgreSqlPayload -ApprovalPath $Approval -OutputRoot $Output "
            f"-RuntimeIdentifier '{runtime}' | Out-Null"
        )

    def validate(self, *, runtime: str = "win-x64") -> subprocess.CompletedProcess[str]:
        return self.invoke(
            "Assert-PostgreSqlPayload -ApprovalPath $Approval -PayloadRoot $Output "
            f"-RuntimeIdentifier '{runtime}' | Out-Null"
        )

    def assert_success(self, result: subprocess.CompletedProcess[str]) -> None:
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def assert_rejected_before_staging(self, result: subprocess.CompletedProcess[str]) -> None:
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse(self.payload.exists(), "Rejected payload was staged")
        self.assertFalse(self.receipt.exists(), "Rejected payload received a receipt")

    def test_exact_approved_source_is_staged_even_when_newer_major_is_installed(self) -> None:
        newer = self.source.parent / "99" / "bin"
        newer.mkdir(parents=True)
        (newer / "postgres.exe").write_text("99.1\n", encoding="utf-8")
        self.assert_success(self.resolve())
        self.assert_success(self.validate())
        receipt = json.loads(self.receipt.read_text(encoding="utf-8-sig"))
        self.assertEqual(receipt["version"], "17.11")
        self.assertEqual(receipt["runtime"], "win-x64")
        self.assertEqual(receipt["source"], self.policy["source"])
        self.assertEqual(receipt["approvalSha256"], hashlib.sha256(self.approval.read_bytes()).hexdigest())
        expected = [
            {"path": path.relative_to(self.payload).as_posix(), "sizeBytes": path.stat().st_size,
             "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
            for path in sorted(self.payload.rglob("*")) if path.is_file()
        ]
        self.assertEqual(receipt["files"], expected)
        canonical = "".join(f"{entry['sha256']}  {entry['path']}\n" for entry in expected)
        self.assertEqual(receipt["payloadSha256"], hashlib.sha256(canonical.encode("utf-8")).hexdigest())
        self.assertEqual((self.payload / "bin/postgres.exe").read_text(encoding="utf-8"), "17.11\n")
        self.assertEqual(receipt["tools"], [
            {"path": f"bin/{tool}.exe", "version": "17.11"} for tool in TOOLS
        ])
        self.assertIn("runner", receipt)

    def test_unavailable_approved_source_does_not_fall_back_or_mutate_existing_output(self) -> None:
        self.policy["source"]["path"] = str(self.source.parent / "missing")
        self.write_approval()
        self.output.mkdir()
        sentinel = self.output / "existing-release.txt"
        sentinel.write_text("keep me", encoding="utf-8")
        self.assert_rejected_before_staging(self.resolve())
        self.assertEqual(sentinel.read_text(encoding="utf-8"), "keep me")

    def test_patch_and_major_mismatches_on_each_required_tool_fail_before_staging(self) -> None:
        for tool in TOOLS:
            for version in ("17.10", "18.1"):
                with self.subTest(tool=tool, version=version):
                    path = self.source / "bin" / f"{tool}.exe"
                    path.write_text(version, encoding="utf-8")
                    self.assert_rejected_before_staging(self.resolve())
                    path.write_text("17.11\n", encoding="utf-8")

    def test_missing_component_or_required_tool_fails_before_staging(self) -> None:
        paths = [self.source / "lib", self.source / "share"]
        paths.extend(self.source / "bin" / f"{tool}.exe" for tool in TOOLS)
        for path in paths:
            with self.subTest(path=path.name):
                displaced = path.with_name(path.name + ".removed")
                path.rename(displaced)
                try:
                    self.assert_rejected_before_staging(self.resolve())
                finally:
                    displaced.rename(path)

    def test_unapproved_runtime_fails_before_staging(self) -> None:
        self.assert_rejected_before_staging(self.resolve(runtime="win-arm64"))

    def test_payload_tree_addition_removal_and_modification_invalidate_receipt(self) -> None:
        for change in ("add", "remove", "modify"):
            with self.subTest(change=change):
                if self.output.exists():
                    shutil.rmtree(self.output)
                self.assert_success(self.resolve())
                path = self.payload / "lib/postgres.dll"
                if change == "add":
                    (self.payload / "bin/unapproved.dll").write_bytes(b"extra")
                elif change == "remove":
                    path.unlink()
                else:
                    path.write_bytes(b"tampered library")
                result = self.validate()
                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_missing_receipt_is_rejected(self) -> None:
        self.assert_success(self.resolve())
        self.receipt.unlink()
        result = self.validate()
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_tampered_receipt_identity_and_hashes_are_rejected(self) -> None:
        self.assert_success(self.resolve())
        original = self.receipt.read_bytes()
        for key, value in (
            ("schemaVersion", 2),
            ("version", "18.1"), ("runtime", "win-arm64"),
            ("approvalSha256", "0" * 64), ("payloadSha256", "0" * 64),
            ("source", {**self.policy["source"], "path": str(self.source.parent / "18")}),
            ("runner", {}), ("runner", {"imageOS": "win25", "imageVersion": 17}),
        ):
            with self.subTest(field=key):
                receipt = json.loads(original)
                receipt[key] = value
                self.receipt.write_text(json.dumps(receipt), encoding="utf-8")
                result = self.validate()
                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.receipt.write_bytes(original)

    def test_changed_approval_invalidates_previously_resolved_payload(self) -> None:
        self.assert_success(self.resolve())
        self.policy["source"]["reference"] = self.policy["source"]["reference"].replace("a" * 40, "b" * 40)
        self.write_approval()
        result = self.validate()
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_tampered_tool_version_receipt_is_rejected(self) -> None:
        self.assert_success(self.resolve())
        receipt = json.loads(self.receipt.read_text(encoding="utf-8-sig"))
        receipt["tools"][0]["version"] = "18.1"
        self.receipt.write_text(json.dumps(receipt), encoding="utf-8")
        result = self.validate()
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_validation_rechecks_tool_versions_instead_of_trusting_receipt(self) -> None:
        self.assert_success(self.resolve())
        result = self.invoke(
            "function Get-PostgreSqlToolVersion { param([string]$Path) '18.1' }\n"
            "Assert-PostgreSqlPayload -ApprovalPath $Approval -PayloadRoot $Output "
            "-RuntimeIdentifier win-x64 | Out-Null"
        )
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_wrapper_resolves_default_approval_relative_to_script(self) -> None:
        result = subprocess.run(
            [self.pwsh, "-NoLogo", "-NoProfile", "-File", str(WRAPPER),
             "-OutputRoot", str(self.output), "-RuntimeIdentifier", "win-arm64"],
            cwd=self.root, capture_output=True, text=True, timeout=45, check=False,
        )
        self.assert_rejected_before_staging(result)
        self.assertIn("No approved PostgreSQL payload for win-arm64", result.stdout + result.stderr)

    def test_builder_preflight_rejects_missing_or_mismatched_payload_before_any_build_or_output_change(self) -> None:
        self.assert_success(self.resolve())
        original_receipt = self.receipt.read_bytes()
        repository = self.root / "builder-repository"
        scripts = repository / "build/scripts/install"
        scripts.mkdir(parents=True)
        for script in (BUILDER, LIBRARY, BUILDER.with_name("windows-sdk-tools.ps1")):
            shutil.copy2(script, scripts / script.name)
        config = repository / "build/config"
        config.mkdir()
        shutil.copy2(self.approval, config / "postgresql-payload.json")
        output = repository / "artifacts/consumer-setup"
        output.mkdir(parents=True)
        sentinel = output / "existing-release.txt"
        sentinel.write_text("keep me", encoding="utf-8")
        marker = self.root / "build-command-ran"
        runner = self.root / "builder-preflight.ps1"
        runner.write_text(
            "param([string]$Builder,[string]$Payload,[string]$Marker)\n"
            "$ErrorActionPreference='Stop'\n"
            "function npm { Set-Content -LiteralPath $Marker 'npm'; throw 'Unexpected npm call' }\n"
            "function dotnet { Set-Content -LiteralPath $Marker 'dotnet'; throw 'Unexpected dotnet call' }\n"
            "& $Builder -PostgreSqlPayloadRoot $Payload -Runtimes win-x64\n",
            encoding="utf-8",
        )
        for change in ("missing receipt", "wrong version", "changed payload",
                       "output equals payload", "output contains payload", "payload contains output"):
            with self.subTest(change=change):
                self.receipt.write_bytes(original_receipt)
                payload_root = self.output
                if change == "missing receipt":
                    self.receipt.unlink()
                elif change == "wrong version":
                    receipt = json.loads(original_receipt)
                    receipt["version"] = "18.1"
                    self.receipt.write_text(json.dumps(receipt), encoding="utf-8")
                elif change == "changed payload":
                    (self.payload / "lib/postgres.dll").write_bytes(b"tampered")
                elif change == "output equals payload":
                    payload_root = output
                elif change == "output contains payload":
                    payload_root = output / "postgresql"
                elif change == "payload contains output":
                    payload_root = output.parent
                result = subprocess.run(
                    [self.pwsh, "-NoLogo", "-NoProfile", "-File", str(runner),
                     str(scripts / BUILDER.name), str(payload_root), str(marker)],
                    cwd=repository, capture_output=True, text=True, timeout=45, check=False,
                )
                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertFalse(marker.exists(), "Build tool ran before payload rejection")
                self.assertEqual(list(output.iterdir()), [sentinel])
                self.assertEqual(sentinel.read_text(encoding="utf-8"), "keep me")
                expected_error = "PostgreSQL"
                if change == "missing receipt":
                    expected_error = "win-x64-payload.json"
                elif "output" in change:
                    expected_error = "must not overlap"
                self.assertIn(expected_error, result.stdout + result.stderr)

    @unittest.skipIf(os.name == "nt", "Native shell fixtures execute on Unix; staging tests are portable")
    def test_wrapper_executes_all_native_version_probes_and_rejects_mismatch_before_staging(self) -> None:
        for tool in TOOLS:
            executable = self.source / "bin" / f"{tool}.exe"
            executable.write_text(
                "#!/bin/sh\n[ \"$1\" = '--version' ] || exit 90\n"
                f"printf '%s\\n' '{tool} (PostgreSQL) 17.11'\n", encoding="utf-8",
            )
            executable.chmod(0o755)
        command = [self.pwsh, "-NoLogo", "-NoProfile", "-File", str(WRAPPER),
                   "-ApprovalPath", str(self.approval), "-OutputRoot", str(self.output)]
        result = subprocess.run(command, cwd=self.root, capture_output=True, text=True, timeout=45, check=False)
        self.assert_success(result)
        receipt = json.loads(self.receipt.read_text(encoding="utf-8-sig"))
        self.assertEqual(receipt["version"], "17.11")
        shutil.rmtree(self.output)
        executable = self.source / "bin/pg_restore.exe"
        executable.write_text(executable.read_text(encoding="utf-8").replace("17.11", "18.1"), encoding="utf-8")
        result = subprocess.run(command, cwd=self.root, capture_output=True, text=True, timeout=45, check=False)
        self.assert_rejected_before_staging(result)
        self.assertIn("version mismatch", result.stdout + result.stderr)

    @unittest.skipIf(os.name == "nt", "Native shell fixture executes on Unix; staging tests are portable")
    def test_native_version_probe_rejects_failed_or_ambiguous_output(self) -> None:
        executable = self.root / "postgres.exe"
        for output, exit_code, accepted in (
            ("postgres (PostgreSQL) 17.11", 0, True),
            ("postgres (PostgreSQL) 17.11", 7, False),
            ("psql (PostgreSQL) 17.11", 0, False),
            ("unrecognized command", 0, False),
            ("postgres (PostgreSQL) 17.11\npostgres (PostgreSQL) 18.1", 0, False),
        ):
            with self.subTest(output=output, exit_code=exit_code):
                executable.write_text(
                    "#!/bin/sh\n[ \"$1\" = '--version' ] || exit 90\n"
                    f"printf '%s\\n' '{output}'\nexit {exit_code}\n", encoding="utf-8",
                )
                executable.chmod(0o755)
                result = self.invoke(
                    "Get-PostgreSqlToolVersion -Path (Join-Path $Root 'postgres.exe')",
                    probe_stub=False,
                )
                if accepted:
                    self.assert_success(result)
                    self.assertEqual(result.stdout.strip(), "17.11")
                else:
                    self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
