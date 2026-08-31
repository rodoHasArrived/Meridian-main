#!/usr/bin/env python3
"""Validate the active Meridian GitHub Actions surface.

The checks here are intentionally small and dependency-free so they can run in
GitHub Actions and in a fresh local checkout without installing tooling first.
"""

from __future__ import annotations

import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
WORKFLOW_DIR = REPO_ROOT / ".github" / "workflows"

CURRENT_ACTION_REFS = {
    "actions/checkout": "v7.0.1",
    "actions/setup-dotnet": "v5.4.0",
    "actions/cache": "v6.1.0",
    "actions/upload-artifact": "v7.0.1",
    "actions/setup-node": "v6.4.0",
    "actions/setup-python": "v7.0.0",
}

ACTIVE_DOC_ROOTS = [
    REPO_ROOT / ".github" / "workflows",
    REPO_ROOT / "README.md",
    REPO_ROOT / "docs" / "developer",
    REPO_ROOT / "docs" / "development",
]
WORKFLOW_MANIFEST_PATH = REPO_ROOT / "docs" / "status" / "workflow-manifest.json"

OLD_WORKFLOW_FILENAMES = {
    "benchmark.yml",
    "bottleneck-detection.yml",
    "build-observability.yml",
    "code-quality.yml",
    "desktop-builds.yml",
    "docker.yml",
    "export-standalone-exe.yml",
    "nightly.yml",
    "pr-checks.yml",
    "prompt-generation.yml",
    "release.yml",
    "reusable-dotnet-build.yml",
    "scheduled-maintenance.yml",
    "security.yml",
    "test-matrix.yml",
    "validate-workflows.yml",
}


def iter_text_files(paths: list[Path]) -> list[Path]:
    files: list[Path] = []
    for path in paths:
        if path.is_file():
            files.append(path)
            continue
        if path.is_dir():
            for child in path.rglob("*"):
                if child.is_file() and child.suffix.lower() in {".md", ".py", ".ps1", ".sh", ".yml", ".yaml"}:
                    files.append(child)
    return sorted(files)


def git_ls_files() -> list[str]:
    result = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
    )
    return [entry.decode("utf-8") for entry in result.stdout.split(b"\0") if entry]


def fail(message: str, failures: list[str]) -> None:
    failures.append(message)


def check_duplicate_workflow_names(failures: list[str]) -> None:
    names: dict[str, list[str]] = defaultdict(list)
    for workflow in sorted(WORKFLOW_DIR.glob("*.yml")) + sorted(WORKFLOW_DIR.glob("*.yaml")):
        text = workflow.read_text(encoding="utf-8")
        match = re.search(r"^name:\s*['\"]?(.+?)['\"]?\s*$", text, flags=re.MULTILINE)
        name = match.group(1).strip() if match else workflow.stem
        names[name].append(workflow.name)

    for name, files in sorted(names.items()):
        if len(files) > 1:
            fail(f"Duplicate workflow name '{name}' in {', '.join(files)}.", failures)


def check_action_refs(failures: list[str]) -> None:
    uses_pattern = re.compile(r"uses:\s*([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)@([A-Za-z0-9_.-]+)")
    for workflow in sorted(WORKFLOW_DIR.glob("*.yml")) + sorted(WORKFLOW_DIR.glob("*.yaml")):
        text = workflow.read_text(encoding="utf-8")
        for action, version in uses_pattern.findall(text):
            expected = CURRENT_ACTION_REFS.get(action)
            if expected and version != expected:
                fail(f"{workflow.relative_to(REPO_ROOT)} uses {action}@{version}; expected @{expected}.", failures)


def check_local_uses_exist(failures: list[str]) -> None:
    local_uses = re.compile(r"uses:\s*(\./[^\s#]+)")
    for workflow in sorted(WORKFLOW_DIR.glob("*.yml")) + sorted(WORKFLOW_DIR.glob("*.yaml")):
        for ref in local_uses.findall(workflow.read_text(encoding="utf-8")):
            target = (REPO_ROOT / ref[2:]).resolve()
            if not target.exists():
                fail(f"{workflow.relative_to(REPO_ROOT)} references missing local action/workflow {ref}.", failures)


def check_active_docs_do_not_reference_removed_workflows(failures: list[str]) -> None:
    # Match whole filenames. A plain substring test reports any workflow whose name merely ends
    # with a removed one — `desktop-evaluation-prerelease.yml` contains `release.yml` — which
    # fails the gate for a file that exists and is correctly documented.
    patterns = {
        old_name: re.compile(rf"(?<![\w./-]){re.escape(old_name)}(?![\w])")
        for old_name in OLD_WORKFLOW_FILENAMES
    }
    for path in iter_text_files(ACTIVE_DOC_ROOTS):
        text = path.read_text(encoding="utf-8", errors="replace")
        for old_name in sorted(OLD_WORKFLOW_FILENAMES):
            if patterns[old_name].search(text):
                fail(f"{path.relative_to(REPO_ROOT)} still references removed workflow {old_name}.", failures)


def check_no_stale_absolute_paths(failures: list[str]) -> None:
    stale_path_patterns = [
        re.compile(r"C:\\Users\\", re.IGNORECASE),
        re.compile(r"OneDrive\\Documents", re.IGNORECASE),
        re.compile(r"Documents\\Desktop", re.IGNORECASE),
    ]
    for path in iter_text_files(ACTIVE_DOC_ROOTS):
        text = path.read_text(encoding="utf-8", errors="replace")
        for pattern in stale_path_patterns:
            if pattern.search(text):
                fail(f"{path.relative_to(REPO_ROOT)} contains stale local absolute path text.", failures)
                break


def check_no_committed_generated_artifacts(failures: list[str]) -> None:
    generated_parts = {"bin", "obj", "TestResults"}
    generated_roots = {
        "dist/",
        "publish/",
        "artifacts/bin/",
        "artifacts/obj/",
        "artifacts/publish/",
        "artifacts/desktop-workflows/",
    }

    for tracked in git_ls_files():
        normalized = tracked.replace("\\", "/")
        parts = set(Path(normalized).parts)
        if generated_parts.intersection(parts):
            fail(f"Generated build/test artifact is tracked: {tracked}", failures)
            continue
        if any(normalized.startswith(root) for root in generated_roots):
            fail(f"Generated workflow artifact is tracked: {tracked}", failures)
        if normalized.endswith((".trx", ".binlog")):
            fail(f"Generated diagnostic artifact is tracked: {tracked}", failures)


def check_no_manual_workflow_counts_in_docs(failures: list[str]) -> None:
    scoped_roots = [
        REPO_ROOT / "README.md",
        REPO_ROOT / "docs" / "developer",
        REPO_ROOT / "docs" / "development",
    ]

    banned_patterns = [
        re.compile(r"\b(?:one|two|three|four|five|six|seven|eight|nine|ten|\d+)\s+workflows?\b", re.IGNORECASE),
        re.compile(r"\bactive\s+lanes?\b", re.IGNORECASE),
    ]
    allowlist_substrings = {
        "docs/status/workflow-manifest.json",
        "docs/generated/workflow-command-reference.md",
        "docs/status/workflow-validation-summary.json",
    }

    for path in iter_text_files(scoped_roots):
        if path.suffix.lower() != ".md":
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for pattern in banned_patterns:
            for match in pattern.finditer(text):
                line_start = text.rfind("\n", 0, match.start()) + 1
                line_end = text.find("\n", match.end())
                if line_end == -1:
                    line_end = len(text)
                line = text[line_start:line_end]
                if any(token in line for token in allowlist_substrings):
                    continue
                fail(
                    f"{path.relative_to(REPO_ROOT)} contains manual workflow inventory wording ('{match.group(0)}'). "
                    "Reference docs/status/workflow-manifest.json and generated workflow artifacts instead.",
                    failures,
                )
                break


def check_workflow_manifest_exists(failures: list[str]) -> None:
    if not WORKFLOW_MANIFEST_PATH.exists():
        fail("Missing canonical workflow manifest at docs/status/workflow-manifest.json.", failures)


def main() -> int:
    failures: list[str] = []

    check_duplicate_workflow_names(failures)
    check_action_refs(failures)
    check_local_uses_exist(failures)
    check_active_docs_do_not_reference_removed_workflows(failures)
    check_no_stale_absolute_paths(failures)
    check_no_committed_generated_artifacts(failures)
    check_workflow_manifest_exists(failures)
    check_no_manual_workflow_counts_in_docs(failures)

    if failures:
        print("Workflow hygiene checks failed:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1

    print("Workflow hygiene checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
