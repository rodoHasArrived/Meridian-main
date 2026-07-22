#!/usr/bin/env python3
"""Generate a release evidence manifest for Meridian publish artifacts."""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Sequence


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


def build_manifest(args: argparse.Namespace) -> dict[str, object]:
    artifact_root = Path(args.artifact_root)
    output_path = Path(args.output)
    return {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "commitSha": args.commit_sha or current_commit_sha(),
        "workflowRunId": args.workflow_run_id,
        "project": args.project,
        "runtime": args.runtime,
        "version": args.version,
        "artifactRoot": artifact_root.as_posix(),
        "validationLanes": args.validation_lane,
        "sbomPaths": [Path(path).as_posix() for path in args.sbom],
        "files": collect_files(artifact_root, output_path),
    }


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
