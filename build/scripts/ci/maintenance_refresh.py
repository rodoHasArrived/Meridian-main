#!/usr/bin/env python3
"""Refresh compatible generated documentation and publish a fast-forward-only PR.

Generation runs without write credentials. Publishing consumes a patch at an exact
base commit, checks its narrow output policy, and never runs the generated tree.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


OUTPUTS = (
    "docs/ai/generated/repo-navigation.json",
    "docs/ai/generated/repo-navigation.md",
    "docs/ai/generated/recent-changes.md",
    "docs/generated/repository-structure.md",
    "docs/generated/workflows-overview.md",
    "docs/status/doc-health-dashboard.json",
    "docs/status/doc-health-dashboard.md",
)
BRANCH = "automation/ai-navigation-refresh"


class StaleBranch(ValueError):
    """A fresh pinned run is required; never apply an old patch to a new tree."""


def git(root: Path, *args: str, data: bytes | None = None, env: dict | None = None) -> bytes:
    return subprocess.run(
        ["git", *args], cwd=root, input=data, stdout=subprocess.PIPE,
        stderr=subprocess.PIPE, check=True, env=env,
    ).stdout


def changed_paths(root: Path, before: str, after: str | None = None) -> list[str]:
    refs = [before] + ([after] if after else [])
    return sorted(filter(None, git(root, "diff", "--name-only", "--no-renames", "-z", *refs).decode().split("\0")))


def validate_paths(root: Path, before: str, after: str) -> list[str]:
    paths = changed_paths(root, before, after)
    unexpected = sorted(set(paths) - set(OUTPUTS))
    if unexpected:
        raise ValueError(f"Maintenance refuses handwritten or unauthorized changes: {unexpected}")
    # Outputs are complete, ordinary files. Refuse deletion, symlinks, executable
    # mode changes and submodules even when they use an authorized filename.
    for tree in (before, after):
        for path in paths:
            entry = git(root, "ls-tree", tree, "--", path).decode()
            if not entry.startswith("100644 blob "):
                raise ValueError(f"Maintenance output must remain a regular file: {path}")
    return paths


def generate(root: Path, output: Path) -> dict:
    if git(root, "status", "--porcelain").strip():
        raise ValueError("Generate maintenance outputs in a clean checkout of the pinned default branch")
    base = git(root, "rev-parse", "HEAD").decode().strip()
    for path in OUTPUTS:
        if not git(root, "ls-tree", base, "--", path).startswith(b"100644 blob "):
            raise ValueError(f"Required maintenance output must already be a tracked regular file: {path}")
    commands = (
        ("generate-ai-navigation.py", "--json-output", OUTPUTS[0], "--markdown-output", OUTPUTS[1],
         "--recent-changes-output", OUTPUTS[2], "--summary"),
        ("generate-structure-docs.py", "--output", OUTPUTS[3]),
        ("generate-structure-docs.py", "--workflows-only", "--output", OUTPUTS[4]),
        ("generate-health-dashboard.py", "--output", OUTPUTS[6], "--json-output", OUTPUTS[5]),
        ("check-ai-navigation-freshness.py", "--max-age-days", "14"),
    )
    for script, *arguments in commands:
        subprocess.run([sys.executable, f"build/scripts/docs/{script}", *arguments], cwd=root, check=True)
    paths = changed_paths(root, base)
    extra = git(root, "ls-files", "--others", "--exclude-standard", "-z").decode().split("\0")
    unexpected = (set(paths) | set(filter(None, extra))) - set(OUTPUTS)
    if unexpected:
        raise ValueError(f"Generator changed unauthorized files: {sorted(unexpected)}")
    for path in paths:
        candidate = root / path
        if candidate.is_symlink() or not candidate.is_file():
            raise ValueError(f"Generator removed or replaced a regular output: {path}")
    git(root, "diff", "--check")
    patch = git(root, "diff", "--binary", "--full-index", base, "--", *OUTPUTS)
    report = {
        "schema_version": 1,
        "base_sha": base,
        "paths": paths,
        "patch_sha256": hashlib.sha256(patch).hexdigest(),
        "freshness_max_age_days": 14,
    }
    output.mkdir(parents=True, exist_ok=True)
    (output / "maintenance.patch").write_bytes(patch)
    (output / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Maintenance patch contains {len(paths)} compatible generated outputs at {base}")
    return report


def prepare_tree(root: Path, artifact: Path, expected_base: str) -> tuple[str, list[str]]:
    # Shared writer helper uses an isolated Git index and never extracts or runs
    # files from the supplied artifact in this trusted writer checkout.
    from generated_repair import apply_patch_tree

    report = json.loads((artifact / "report.json").read_text(encoding="utf-8"))
    patch = (artifact / "maintenance.patch").read_bytes()
    if report.get("schema_version") != 1 or report.get("base_sha") != expected_base:
        raise ValueError("Maintenance artifact does not match the trusted base revision")
    if report.get("patch_sha256") != hashlib.sha256(patch).hexdigest():
        raise ValueError("Maintenance patch digest mismatch")
    if report.get("freshness_max_age_days") != 14:
        raise ValueError("Maintenance artifact changed the navigation freshness policy")
    if not patch:
        if report.get("paths") != []:
            raise ValueError("Empty patch reports changed files")
        return git(root, "rev-parse", f"{expected_base}^{{tree}}").decode().strip(), []
    tree = apply_patch_tree(root, expected_base, patch)
    paths = validate_paths(root, expected_base, tree)
    if paths != report.get("paths"):
        raise ValueError("Maintenance artifact file list differs from the actual patch")
    return tree, paths


def remote_head(root: Path, branch: str, env: dict) -> str | None:
    lines = git(root, "ls-remote", "--heads", "origin", f"refs/heads/{branch}", env=env).decode().splitlines()
    return lines[0].split()[0] if lines else None


def make_commit(root: Path, base: str, tree: str, previous: str | None) -> str:
    if previous:
        ancestor = git(root, "merge-base", base, previous).decode().strip()
        validate_paths(root, ancestor, previous)
    parents = ["-p", previous, "-p", base] if previous and previous != base else ["-p", base]
    identity = dict(os.environ, GIT_AUTHOR_NAME="Meridian maintenance", GIT_AUTHOR_EMAIL="maintenance@users.noreply.github.com",
                    GIT_COMMITTER_NAME="Meridian maintenance", GIT_COMMITTER_EMAIL="maintenance@users.noreply.github.com")
    return git(root, "commit-tree", tree, *parents, data=b"chore: refresh generated documentation\n", env=identity).decode().strip()


def publish(root: Path, artifact: Path, expected_base: str, base_branch: str, repository: str) -> dict:
    token = os.environ.get("GH_TOKEN")
    if not token:
        raise ValueError("Missing GitHub App token; configure GENERATED_REPAIR_APP_ID and GENERATED_REPAIR_APP_PRIVATE_KEY. Download the maintenance patch artifact for manual use.")
    git(root, "check-ref-format", f"refs/heads/{base_branch}")
    tree, paths = prepare_tree(root, artifact, expected_base)
    if not paths:
        return {"status": "unchanged", "base_sha": expected_base}
    # Credentials exist only in this writer process. They are neither persisted
    # by checkout nor embedded in a remote URL, a command argument or an artifact.
    with tempfile.TemporaryDirectory(prefix="maintenance-auth-") as temporary:
        askpass = Path(temporary) / "askpass.sh"
        askpass.write_text('#!/bin/sh\ncase "$1" in *Username*) printf "%s\\n" x-access-token ;; *) printf "%s\\n" "$GH_TOKEN" ;; esac\n')
        askpass.chmod(0o700)
        env = dict(os.environ, GIT_ASKPASS=str(askpass), GIT_TERMINAL_PROMPT="0")
        if remote_head(root, base_branch, env) != expected_base:
            raise StaleBranch("Default branch advanced; discard this patch and rerun maintenance")
        previous = remote_head(root, BRANCH, env)
        if previous:
            git(root, "fetch", "--no-tags", "origin", previous, env=env)
        commit = make_commit(root, expected_base, tree, previous)
        if remote_head(root, base_branch, env) != expected_base or remote_head(root, BRANCH, env) != previous:
            raise StaleBranch("Default or maintenance branch advanced; discard this patch and rerun maintenance")
        try:
            git(root, "push", "origin", f"{commit}:refs/heads/{BRANCH}", env=env)
        except subprocess.CalledProcessError:
            if remote_head(root, BRANCH, env) != previous or remote_head(root, base_branch, env) != expected_base:
                raise StaleBranch("A branch advanced during publication; regenerate before retrying") from None
            raise
        if remote_head(root, base_branch, env) != expected_base:
            raise StaleBranch("Default branch advanced during publication; regenerate before opening the pull request")

    # `gh` receives structured JSON over stdin; no branch text enters a shell.
    query = subprocess.run(["gh", "api", "--method", "GET", f"repos/{repository}/pulls", "-f", "state=open",
                            "-f", f"head={repository.split('/')[0]}:{BRANCH}", "-f", f"base={base_branch}"],
                           check=True, capture_output=True, text=True)
    pulls = json.loads(query.stdout)
    body = (
        "Refresh the compatible navigation, repository/workflow structure and documentation health outputs together.\n\n"
        "The existing 14-day AI navigation freshness gate remains enabled. This branch is advanced without force pushes. "
        "A GitHub App token creates the commit and pull request so required CI runs on the new revision.\n\n"
        f"Generated from `{expected_base}`; repair commit `{commit}`.\n"
    )
    payload = {"title": "chore: refresh generated documentation", "body": body}
    endpoint = f"repos/{repository}/pulls"
    method = "POST"
    if pulls:
        endpoint += f"/{pulls[0]['number']}"
        method = "PATCH"
    else:
        payload.update(head=BRANCH, base=base_branch)
    result = subprocess.run(["gh", "api", "--method", method, endpoint, "--input", "-"],
                            input=json.dumps(payload), check=True, capture_output=True, text=True)
    pull = json.loads(result.stdout)
    return {"status": "published", "base_sha": expected_base, "previous_head": previous,
            "head_sha": commit, "pull_request_url": pull["html_url"], "paths": paths}


def publish_with_retry(root: Path, artifact: Path, expected_base: str, base_branch: str,
                       repository: str, retry_count: int) -> dict:
    try:
        return publish(root, artifact, expected_base, base_branch, repository)
    except StaleBranch as error:
        if retry_count >= 1:
            raise ValueError(f"{error}. Automatic retry exhausted; download the patch or dispatch a fresh maintenance run") from error
        payload = {"ref": base_branch, "inputs": {"retry_count": "1"}}
        subprocess.run(["gh", "api", "--method", "POST",
                        f"repos/{repository}/actions/workflows/ai-navigation-refresh.yml/dispatches", "--input", "-"],
                       input=json.dumps(payload), check=True, capture_output=True, text=True)
        return {"status": "retry-queued", "discarded_base_sha": expected_base,
                "reason": str(error), "retry_count": 1}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    subparsers = parser.add_subparsers(dest="command", required=True)
    generation = subparsers.add_parser("generate")
    generation.add_argument("--output", type=Path, default=Path("artifacts/maintenance-refresh"))
    writer = subparsers.add_parser("publish")
    writer.add_argument("--artifact", type=Path, required=True)
    writer.add_argument("--base-sha", required=True)
    writer.add_argument("--base-branch", required=True)
    writer.add_argument("--repository", required=True)
    writer.add_argument("--retry-count", type=int, choices=(0, 1), default=0)
    args = parser.parse_args()
    try:
        if args.command == "generate":
            result = generate(args.root, args.output)
        else:
            result = publish_with_retry(args.root, args.artifact, args.base_sha, args.base_branch, args.repository, args.retry_count)
        print(json.dumps(result, indent=2))
        return 0
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        # Do not print subprocess command arguments or environment containing credentials.
        print(f"Maintenance refresh failed: {error if not isinstance(error, subprocess.CalledProcessError) else 'required git, generator or GitHub API operation failed'}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
