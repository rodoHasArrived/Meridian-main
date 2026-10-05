#!/usr/bin/env python3
"""Resolve and verify a production consumer EXE predecessor independently of MSIX."""

from __future__ import annotations

import argparse
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from typing import Mapping, Sequence


PACKAGE_NAME = "Meridian-Setup.exe"
CHECKSUM_NAME = "consumer-setup-win-x64-SHA256SUMS"


def version_key(tag: str) -> tuple:
    match = re.fullmatch(
        r"v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*))?",
        tag,
    )
    if not match:
        raise ValueError(f"Invalid production release tag: {tag!r}")
    major, minor, patch, suffix = match.groups()
    identifiers = tuple((0, int(part)) if part.isdigit() else (1, part)
                        for part in suffix.split(".")) if suffix else ()
    return int(major), int(minor), int(patch), suffix is None, identifiers


def gh_json(endpoint: str):
    result = subprocess.run(["gh", "api", endpoint], check=True, capture_output=True, text=True)
    return json.loads(result.stdout)


def all_pages(endpoint: str) -> list[dict]:
    """Do not treat a query failure or malformed page as an empty release history."""
    result = []
    page = 1
    while True:
        items = gh_json(f"{endpoint}?per_page=100&page={page}")
        if not isinstance(items, list) or any(not isinstance(item, dict) for item in items):
            raise ValueError(f"Incomplete or malformed GitHub response for {endpoint}")
        if not items:
            return result
        result.extend(items)
        page += 1


def production_version(release: dict) -> tuple | None:
    if not isinstance(release.get("draft"), bool):
        raise ValueError("Release history contains an invalid draft state")
    if release["draft"] or not release.get("published_at"):
        return None
    tag = release.get("tag_name")
    if not isinstance(tag, str):
        raise ValueError("Release history contains an invalid tag")
    # Evaluation uses eval-v* and carries a self-signed identity. Production v* RCs
    # use the protected signing workflow and remain eligible predecessors.
    if not tag.startswith("v"):
        return None
    try:
        datetime.fromisoformat(release["published_at"].replace("Z", "+00:00"))
    except (AttributeError, ValueError) as error:
        raise ValueError(f"Invalid publication time for {tag}") from error
    return version_key(tag)


def digest(path: Path) -> str:
    result = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def download_asset(repository: str, asset: dict, destination: Path) -> None:
    if not isinstance(asset.get("id"), int) or not isinstance(asset.get("size"), int) or asset["size"] <= 0:
        raise ValueError(f"Invalid asset metadata for {asset.get('name')}")
    with destination.open("wb") as output:
        subprocess.run(["gh", "api", f"repos/{repository}/releases/assets/{asset['id']}",
                        "-H", "Accept: application/octet-stream"], stdout=output, check=True)
    if destination.stat().st_size != asset["size"]:
        raise ValueError(f"Incomplete download of {asset['name']}")


def expected_digest(checksums: Path) -> str:
    matches = []
    for line in checksums.read_text(encoding="utf-8-sig").splitlines():
        parsed = re.fullmatch(r"([0-9A-Fa-f]{64})\s+\*?(.+)", line)
        if parsed and parsed[2] == PACKAGE_NAME:
            matches.append(parsed[1].lower())
    if len(matches) != 1:
        raise ValueError(f"Checksum manifest must contain exactly one digest for {PACKAGE_NAME}")
    return matches[0]


def require_single_asset(assets: list[dict], name: str, tag: str) -> dict:
    matches = [asset for asset in assets if asset.get("name") == name]
    if len(matches) != 1:
        raise ValueError(f"Release {tag} must publish exactly one {name} asset")
    return matches[0]


def resolve(args: argparse.Namespace, environment: Mapping[str, str] | None = None) -> dict:
    environment = os.environ if environment is None else environment
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", args.repository):
        raise ValueError("A repository must be owner/name")
    current_version = version_key(args.current_tag)
    if not args.current_commit or re.search(r"\s", args.current_commit):
        raise ValueError("A current commit is required")
    if environment.get("GITHUB_SHA") and environment["GITHUB_SHA"] != args.current_commit:
        raise ValueError("Current commit differs from GITHUB_SHA")
    for field in ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT"):
        if not re.fullmatch(r"[1-9]\d*", environment.get(field, "")):
            raise ValueError(f"{field} is required to bind predecessor evidence")
    if args.prior_release_tag:
        if version_key(args.prior_release_tag) >= current_version:
            raise ValueError("Configured predecessor must be older than the current tag")
    for path in (args.output_dir, args.evidence):
        if "\n" in path or "\r" in path:
            raise ValueError("Output paths must not contain line breaks")

    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    # A failed retry must not leave previously resolved bytes available to the harness.
    for name in (PACKAGE_NAME, CHECKSUM_NAME):
        (output_dir / name).unlink(missing_ok=True)

    releases = all_pages(f"repos/{args.repository}/releases")
    eligible = []
    seen_tags = set()
    for release in releases:
        previous_version = production_version(release)
        if previous_version is None or previous_version >= current_version:
            continue
        tag = release["tag_name"]
        if tag in seen_tags:
            raise ValueError(f"Ambiguous release history for {tag}")
        seen_tags.add(tag)
        if not isinstance(release.get("id"), int):
            raise ValueError(f"Invalid release identity for {tag}")
        # Read every asset page instead of trusting the embedded release listing.
        assets = all_pages(f"repos/{args.repository}/releases/{release['id']}/assets")
        eligible.append((previous_version, release, assets))

    consumer = [item for item in eligible if any(asset.get("name") == PACKAGE_NAME for asset in item[2])]
    evidence = {
        "schemaVersion": 1,
        "project": "consumer-setup",
        "runtime": "win-x64",
        "sourceCommit": args.current_commit,
        "workflowRunId": environment["GITHUB_RUN_ID"],
        "workflowRunAttempt": environment["GITHUB_RUN_ATTEMPT"],
        "currentTag": args.current_tag,
        "firstRelease": not consumer,
        "priorReleaseTag": None,
        "priorPackage": None,
        "eligibleReleaseCount": len(eligible),
        "consumerReleaseCount": len(consumer),
        "reason": "",
    }
    if args.prior_release_tag:
        selected = next((item for item in eligible if item[1]["tag_name"] == args.prior_release_tag), None)
        if selected is None:
            raise ValueError("Configured predecessor is not an eligible published production release")
        if not any(asset.get("name") == PACKAGE_NAME for asset in selected[2]):
            raise ValueError(f"Configured predecessor {args.prior_release_tag} published no {PACKAGE_NAME}")
        if selected[1]["tag_name"] != max(consumer, key=lambda item: item[0])[1]["tag_name"]:
            raise ValueError("Configured predecessor is not the latest eligible consumer release (N-1)")
    elif consumer:
        selected = max(consumer, key=lambda item: item[0])
    else:
        evidence["reason"] = (
            "Published production predecessors exist, but none published Meridian-Setup.exe; consumer first release."
            if eligible else "No published production predecessor exists; consumer first release."
        )
        return evidence

    _, release, assets = selected
    tag = release["tag_name"]
    package_asset = require_single_asset(assets, PACKAGE_NAME, tag)
    checksum_asset = require_single_asset(assets, CHECKSUM_NAME, tag)
    with tempfile.TemporaryDirectory(prefix="consumer-predecessor-", dir=output_dir) as temporary:
        package = Path(temporary) / PACKAGE_NAME
        checksums = Path(temporary) / CHECKSUM_NAME
        download_asset(args.repository, package_asset, package)
        download_asset(args.repository, checksum_asset, checksums)
        expected = expected_digest(checksums)
        if digest(package) != expected:
            raise ValueError(f"Consumer predecessor digest mismatch for {tag}")
        package.replace(output_dir / PACKAGE_NAME)
        checksums.replace(output_dir / CHECKSUM_NAME)
    evidence.update(
        firstRelease=False,
        priorReleaseTag=tag,
        priorPackage={"name": PACKAGE_NAME, "path": (output_dir / PACKAGE_NAME).resolve().as_posix(), "sha256": expected},
        reason="Configured published consumer predecessor." if args.prior_release_tag else "Latest published consumer predecessor.",
    )
    return evidence


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--current-tag", required=True)
    parser.add_argument("--current-commit", required=True)
    parser.add_argument("--prior-release-tag", default="")
    parser.add_argument("--output-dir", default="artifacts/prior-consumer")
    parser.add_argument("--evidence", default="artifacts/consumer-lifecycle/predecessor.json")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    path = Path(args.evidence)
    path.unlink(missing_ok=True)
    try:
        evidence = resolve(args)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        if os.environ.get("GITHUB_OUTPUT"):
            with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
                prior = evidence["priorPackage"]
                output.write(f"prior_path={prior['path'] if prior else ''}\n"
                             f"prior_tag={evidence['priorReleaseTag'] or ''}\n"
                             f"first_release={str(evidence['firstRelease']).lower()}\n")
        print(f"Consumer predecessor: {evidence['priorReleaseTag'] or 'first release'}; evidence: {path}")
        return 0
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        path.unlink(missing_ok=True)
        print(f"Consumer predecessor resolution failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
