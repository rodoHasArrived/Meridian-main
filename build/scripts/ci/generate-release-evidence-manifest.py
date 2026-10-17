#!/usr/bin/env python3
"""Generate a release evidence manifest for Meridian publish artifacts."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Sequence


POSTGRESQL_TOOLS = {
    "bin/postgres.exe", "bin/pg_ctl.exe", "bin/initdb.exe", "bin/psql.exe",
    "bin/pg_dump.exe", "bin/pg_restore.exe",
}


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate a Meridian release evidence manifest.")
    parser.add_argument("--project", required=True, help="Published Meridian project or package family.")
    parser.add_argument("--runtime", required=True, help="Runtime identifier, for example win-x64.")
    parser.add_argument("--artifact-root", required=True, help="Root directory containing publish artifacts.")
    parser.add_argument("--output", required=True, help="Manifest JSON output path.")
    parser.add_argument("--version", default="", help="Package or smoke version.")
    parser.add_argument("--workflow-run-id", default="", help="GitHub Actions run id.")
    parser.add_argument("--validation-lane", action="append", default=[], help="Validation lane name.")
    parser.add_argument("--sbom", action="append", default=[], help="SBOM file path included in the evidence bundle.")
    parser.add_argument(
        "--postgresql-payload-receipt", action="append", default=[],
        help="Validated PostgreSQL payload receipt to include in release evidence (repeatable).",
    )
    parser.add_argument("--commit-sha", default="", help="Commit SHA. Defaults to git rev-parse HEAD.")
    return parser.parse_args(argv)


def current_commit_sha() -> str:
    try:
        return subprocess.run(
            ["git", "rev-parse", "HEAD"],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()
    except (subprocess.CalledProcessError, FileNotFoundError):
        return ""


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def collect_files(root: Path, output_path: Path) -> list[dict[str, object]]:
    files: list[dict[str, object]] = []
    if not root.exists():
        raise FileNotFoundError(f"artifact root does not exist: {root}")

    output_resolved = output_path.resolve()
    for path in sorted(root.rglob("*")):
        if not path.is_file():
            continue
        if path.resolve() == output_resolved:
            continue
        files.append(
            {
                "path": path.as_posix(),
                "sizeBytes": path.stat().st_size,
                "sha256": sha256_file(path),
            }
        )
    return files


def load_postgresql_payload_receipt(path: Path, runtime: str) -> dict[str, object]:
    """Validate receipt consistency; hashes describe the payload resolved by the producer."""
    raw = path.read_bytes()
    receipt = json.loads(raw.decode("utf-8-sig"))

    def require(condition: bool, message: str) -> None:
        if not condition:
            raise ValueError(f"invalid PostgreSQL payload receipt {path}: {message}")

    def valid_hash(value: object) -> bool:
        return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None

    def safe_path(value: object) -> bool:
        if not isinstance(value, str) or not value:
            return False
        if re.search(r'[\\<>:"|?*\x00-\x1f\ud800-\udfff]', value):
            return False
        return all(
            part not in ("", ".", "..") and not part.endswith((" ", "."))
            and re.fullmatch(r"(?i:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?", part) is None
            for part in value.split("/")
        )

    require(isinstance(receipt, dict), "expected a JSON object")
    require(type(receipt.get("schemaVersion")) is int and receipt["schemaVersion"] == 1,
            "schemaVersion must be 1")
    require(receipt.get("runtime") == "win-x64" and receipt["runtime"] == runtime,
            "runtime must be win-x64 and match the manifest runtime")
    version = receipt.get("version")
    require(isinstance(version, str) and re.fullmatch(r"[1-9][0-9]*\.[0-9]+", version) is not None,
            "version must be an exact PostgreSQL major.minor version")
    source = receipt.get("source")
    require(isinstance(source, dict), "source must be an object")
    require(source.get("kind") == "github-hosted-runner", "unsupported source kind")
    for field in ("path", "reference"):
        require(isinstance(source.get(field), str) and bool(source[field].strip()),
                f"source.{field} must be a nonempty string")
    for field in ("approvalSha256", "payloadSha256"):
        require(valid_hash(receipt.get(field)), f"{field} must be a lowercase SHA256 hash")
    runner = receipt.get("runner")
    require(isinstance(runner, dict), "runner must be an object")
    for field in ("imageOS", "imageVersion"):
        require(isinstance(runner.get(field), str), f"runner.{field} must be a string")

    files = receipt.get("files")
    require(isinstance(files, list) and bool(files), "files must be a nonempty array")
    file_paths: set[str] = set()
    windows_paths: set[str] = set()
    for entry in files:
        require(isinstance(entry, dict), "file entries must be objects")
        name = entry.get("path")
        require(safe_path(name), "file paths must be safe relative paths using forward slashes")
        require(name.casefold() not in windows_paths, f"duplicate file path: {name}")
        file_paths.add(name)
        windows_paths.add(name.casefold())
        require(type(entry.get("sizeBytes")) is int and entry["sizeBytes"] >= 0,
                f"sizeBytes must be a nonnegative integer: {name}")
        require(valid_hash(entry.get("sha256")), f"invalid file SHA256: {name}")

    # .NET StringComparer.Ordinal orders UTF-16 code units, including non-ASCII filenames.
    ordered_files = sorted(files, key=lambda entry: entry["path"].encode("utf-16-be"))
    canonical = "".join(f"{entry['sha256']}  {entry['path']}\n" for entry in ordered_files)
    require(hashlib.sha256(canonical.encode("utf-8")).hexdigest() == receipt["payloadSha256"],
            "payloadSha256 does not match the canonical file inventory")

    tools = receipt.get("tools")
    require(isinstance(tools, list), "tools must be an array")
    tool_paths: set[str] = set()
    for tool in tools:
        require(isinstance(tool, dict), "tool entries must be objects")
        name = tool.get("path")
        require(safe_path(name) and name in file_paths, "tool path must identify an inventoried file")
        require(name not in tool_paths, f"duplicate tool path: {name}")
        tool_paths.add(name)
        require(tool.get("version") == version, f"tool version does not match payload version: {name}")
    require(POSTGRESQL_TOOLS <= tool_paths, "required PostgreSQL tools are missing")

    return {
        **receipt,
        "receiptPath": path.as_posix(),
        "receiptSha256": hashlib.sha256(raw).hexdigest(),
    }


def build_manifest(args: argparse.Namespace) -> dict[str, object]:
    artifact_root = Path(args.artifact_root)
    output_path = Path(args.output)
    receipts = args.postgresql_payload_receipt
    requires_payload = args.project == "consumer-setup" or (
        args.project == "web-workstation" and "web-workstation-installed-startup" in args.validation_lane
    )
    if requires_payload and not receipts:
        raise ValueError("PostgreSQL payload receipt is required for consumer setup and installed-startup evidence")
    payloads = [load_postgresql_payload_receipt(Path(path), args.runtime) for path in receipts]
    manifest = {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "commitSha": args.commit_sha or current_commit_sha(),
        "workflowRunId": args.workflow_run_id,
        "workflowRunAttempt": os.environ.get("GITHUB_RUN_ATTEMPT", ""),
        "workflowRunUrl": (f"https://github.com/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{args.workflow_run_id}/attempts/{os.environ.get('GITHUB_RUN_ATTEMPT', '1')}"
                           if os.environ.get("GITHUB_REPOSITORY") and args.workflow_run_id else ""),
        "project": args.project,
        "runtime": args.runtime,
        "version": args.version,
        "artifactRoot": artifact_root.as_posix(),
        "validationLanes": args.validation_lane,
        "sbomPaths": [Path(path).as_posix() for path in args.sbom],
        "files": collect_files(artifact_root, output_path),
    }
    if payloads:
        manifest["postgresqlPayloads"] = payloads
    return manifest


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    output_path = Path(args.output)
    manifest = build_manifest(args)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote release evidence manifest: {output_path}")
    print(f"Files: {len(manifest['files'])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
