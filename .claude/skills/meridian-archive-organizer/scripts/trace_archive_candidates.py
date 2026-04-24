#!/usr/bin/env python3
"""Trace archive candidates and separate strong vs weak references."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[4]
SKIP_DIRS = {
    ".claude",
    ".codex",
    ".git",
    ".vs",
    ".playwright-cli",
    "bin",
    "obj",
    "node_modules",
    "__pycache__",
}
TEXT_SUFFIXES = {
    "",
    ".cs",
    ".csproj",
    ".editorconfig",
    ".fs",
    ".fsproj",
    ".fsx",
    ".gitignore",
    ".json",
    ".md",
    ".props",
    ".ps1",
    ".py",
    ".sln",
    ".targets",
    ".toml",
    ".txt",
    ".xml",
    ".xaml",
    ".yaml",
    ".yml",
}
DATED_NAME_RE = re.compile(r"(19|20)\d{2}[-_](0[1-9]|1[0-2])(?:[-_](0[1-9]|[12]\d|3[01]))?")
TREE_LINE_RE = re.compile(r"^\s*[│├└]")
SCRATCH_NAME_RE = re.compile(
    r"(^\.playwright-cli$)|(\.log$)|(_stdout\.txt$)|(_stderr\.txt$)|(^scratch[-_.])|([-_.]scratch\.)",
    re.IGNORECASE,
)

WEAK_EXACT_PATHS = {
    ".github/agents/documentation-agent.md",
    "docs/ai/copilot/instructions.md",
    "docs/status/TODO.md",
    "docs/status/api-docs-report.md",
    "docs/status/badge-sync-report.md",
    "docs/status/coverage-report.md",
    "docs/status/docs-automation-summary.md",
    "docs/status/example-validation.md",
    "docs/status/health-dashboard.md",
    "docs/status/link-repair-report.md",
    "docs/status/metrics-dashboard.md",
    "docs/status/rules-report.md",
}
WEAK_PREFIXES = (
    "docs/generated/",
)
STRONG_STATUS_EXACT = {
    "docs/status/FEATURE_INVENTORY.md",
    "docs/status/IMPROVEMENTS.md",
    "docs/status/OPPORTUNITY_SCAN.md",
    "docs/status/README.md",
    "docs/status/ROADMAP.md",
    "docs/status/ROADMAP_COMBINED.md",
    "docs/status/TARGET_END_PRODUCT.md",
    "docs/status/production-status.md",
}
STRONG_PREFIXES = (
    "docs/architecture/",
    "docs/development/",
    "docs/evaluations/",
    "docs/operations/",
    "docs/plans/",
    "docs/providers/",
    "docs/reference/",
)
AUTOMATION_OWNER_PATHS = (
    ".github/workflows/documentation.yml",
    "build/scripts/docs/run-docs-automation.py",
    "docs/development/documentation-automation.md",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--path", action="append", required=True, help="Candidate path relative to repo root.")
    parser.add_argument(
        "--repo-root",
        default=str(ROOT_DIR),
        help="Repository root to scan. Defaults to the current Meridian workspace root.",
    )
    parser.add_argument("--limit", type=int, default=12, help="Maximum number of references to show per target.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    return parser.parse_args()


def to_repo_relative(path: Path, repo_root: Path) -> str:
    try:
        return path.resolve().relative_to(repo_root.resolve()).as_posix()
    except ValueError:
        return path.as_posix().lstrip("./")


def classify_zone(relative_path: str) -> str:
    if relative_path.startswith("archive/docs/"):
        return "archive-docs"
    if relative_path.startswith("archive/code/"):
        return "archive-code"
    if relative_path.startswith("docs/"):
        return "active-docs"
    if relative_path.startswith("src/") or relative_path.startswith("tests/"):
        return "active-code"
    return "repo-root-or-other"


def infer_lane(relative_path: str, target_path: Path, scratch: bool, automation_owned: bool) -> str:
    if scratch:
        return "local-scratch"
    if automation_owned:
        return "automation-owned-surface"
    if target_path.suffix.lower() in {".cs", ".fs", ".xaml"} or relative_path.startswith(("src/", "tests/")):
        return "retired-code"
    return "dated-doc-snapshot"


def is_scratch_target(relative_path: str, target_path: Path) -> bool:
    parts = relative_path.split("/")
    if any(part == ".playwright-cli" for part in parts):
        return True
    return bool(SCRATCH_NAME_RE.search(target_path.name))


def iter_text_files(repo_root: Path):
    for file_path in repo_root.rglob("*"):
        if not file_path.is_file():
            continue
        if any(part in SKIP_DIRS for part in file_path.relative_to(repo_root).parts[:-1]):
            continue
        if file_path.stat().st_size > 2_000_000:
            continue
        if file_path.suffix.lower() not in TEXT_SUFFIXES:
            continue
        yield file_path


def candidate_tokens(relative_path: str, target_path: Path) -> list[str]:
    tokens = {relative_path, relative_path.replace("/", "\\"), target_path.name}
    return sorted(token for token in tokens if token)


def is_active_status_doc(relative_path: str) -> bool:
    if relative_path in STRONG_STATUS_EXACT:
        return True
    return relative_path.startswith("docs/status/FULL_IMPLEMENTATION_TODO_")


def is_weak_reference_path(relative_path: str) -> bool:
    if relative_path in WEAK_EXACT_PATHS:
        return True
    return any(relative_path.startswith(prefix) for prefix in WEAK_PREFIXES)


def is_automation_owned_status_json(relative_path: str) -> bool:
    return relative_path.startswith("docs/status/") and relative_path.endswith(".json")


def line_has_reference(line: str, tokens: list[str]) -> bool:
    return any(token in line for token in tokens)


def classify_reference_strength(reference_path: str, line: str, candidate_relative: str) -> str:
    stripped = line.strip()
    lowered = stripped.lower()
    candidate_is_status_json = is_automation_owned_status_json(candidate_relative)
    readme_inventory_path = reference_path.startswith("docs/") and reference_path.endswith("/README.md")

    if TREE_LINE_RE.match(stripped):
        return "weak"
    if "auto-generated" in lowered or "generated tree" in lowered:
        return "weak"
    if "prior review context" in lowered:
        return "weak"
    if ("historical" in lowered or "snapshot" in lowered) and readme_inventory_path:
        return "weak"
    if reference_path.startswith("archive/"):
        return "weak"
    if is_weak_reference_path(reference_path):
        return "weak"
    if candidate_is_status_json and reference_path in AUTOMATION_OWNER_PATHS:
        if "docs/status/" in line or candidate_relative.split("/")[-1] in line or "json-output" in lowered or "output-json" in lowered:
            return "strong"
    if reference_path in STRONG_STATUS_EXACT or is_active_status_doc(reference_path):
        return "strong"
    if any(reference_path.startswith(prefix) for prefix in STRONG_PREFIXES):
        return "strong"
    if reference_path.startswith("docs/status/") and reference_path.endswith(".json"):
        return "weak"
    if reference_path.startswith(".github/workflows/") or reference_path.startswith("build/scripts/"):
        return "strong"
    if reference_path.startswith("docs/") or reference_path.startswith(".github/"):
        return "strong"
    return "strong"


def initialize_candidate_state(candidate_input: str, repo_root: Path) -> dict[str, object]:
    target_path = (repo_root / candidate_input).resolve()
    exists = target_path.exists()
    is_dir = target_path.is_dir()
    reference_path = target_path if exists else Path(candidate_input)
    candidate_relative = to_repo_relative(reference_path, repo_root)
    return {
        "path": candidate_input,
        "target_path": target_path,
        "exists": exists,
        "is_dir": is_dir,
        "candidate_relative": candidate_relative,
        "zone": classify_zone(candidate_relative),
        "dated_name": bool(DATED_NAME_RE.search(reference_path.name)),
        "scratch": is_scratch_target(candidate_relative, reference_path),
        "tokens": candidate_tokens(candidate_relative, reference_path),
        "references": [],
        "strong_ref_count": 0,
        "weak_ref_count": 0,
        "owner_reference_found": False,
    }


def scan_references(states: list[dict[str, object]], repo_root: Path, limit: int) -> None:
    searchable_states = [state for state in states if state["exists"] and not state["is_dir"]]
    if not searchable_states:
        return

    for file_path in iter_text_files(repo_root):
        reference_relative = to_repo_relative(file_path, repo_root)
        try:
            text = file_path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            try:
                text = file_path.read_text(encoding="utf-8-sig")
            except UnicodeDecodeError:
                continue

        candidate_states = [
            state
            for state in searchable_states
            if reference_relative != state["candidate_relative"] and any(token in text for token in state["tokens"])
        ]
        if not candidate_states:
            continue

        lines = text.splitlines()
        for state in candidate_states:
            for line_number, line in enumerate(lines, start=1):
                if not line_has_reference(line, state["tokens"]):
                    continue
                strength = classify_reference_strength(reference_relative, line, state["candidate_relative"])
                if strength == "strong":
                    state["strong_ref_count"] += 1
                    if reference_relative in AUTOMATION_OWNER_PATHS:
                        state["owner_reference_found"] = True
                else:
                    state["weak_ref_count"] += 1
                if len(state["references"]) < limit:
                    state["references"].append(
                        {
                            "strength": strength,
                            "file": reference_relative,
                            "line": line_number,
                            "excerpt": line.strip()[:220],
                        }
                    )


def recommend(
    relative_path: str,
    zone: str,
    target_path: Path,
    dated_name: bool,
    scratch: bool,
    automation_owned: bool,
    strong_count: int,
    weak_count: int,
) -> tuple[str, list[str]]:
    reasons: list[str] = []
    if zone.startswith("archive-"):
        reasons.append("Path already lives under archive/.")
        return "already-archived", reasons
    if scratch:
        reasons.append("Local scratch or hidden tool output should be deleted or ignored, not archived.")
        return "delete", reasons
    if automation_owned:
        reasons.append("Machine-readable docs/status artifact is owned by repo automation.")
        return "active", reasons
    if strong_count > 0:
        reasons.append("Active strong references still point at this target.")
        return "active", reasons
    if zone == "active-docs" and dated_name:
        reasons.append("Dated active doc has no strong references and is a good archive-doc candidate.")
        return "archive-doc", reasons
    if zone == "active-docs" and weak_count > 0:
        reasons.append("Only weak references were found; human-maintained entrypoints no longer keep it active.")
        return "archive-doc", reasons
    if zone == "active-code":
        reasons.append("Code paths require explicit build and inclusion checks before archiving.")
        return "needs-review", reasons
    reasons.append("No deterministic safe move was proven.")
    return "needs-review", reasons


def finalize_candidate_state(state: dict[str, object]) -> dict[str, object]:
    target_path = state["target_path"]
    candidate_relative = state["candidate_relative"]
    automation_owned = is_automation_owned_status_json(candidate_relative) and state["owner_reference_found"]

    recommendation, reasons = recommend(
        candidate_relative,
        state["zone"],
        target_path,
        state["dated_name"],
        state["scratch"],
        automation_owned,
        state["strong_ref_count"],
        state["weak_ref_count"],
    )
    lane = infer_lane(candidate_relative, target_path, state["scratch"], automation_owned)

    if not state["exists"]:
        reasons.append("Target does not currently exist.")

    return {
        "path": state["path"],
        "absolute_path": str(target_path),
        "exists": state["exists"],
        "is_dir": state["is_dir"],
        "zone": state["zone"],
        "lane": lane,
        "dated_name": state["dated_name"],
        "recommendation": recommendation,
        "strong_ref_count": state["strong_ref_count"],
        "weak_ref_count": state["weak_ref_count"],
        "reasons": reasons,
        "references": state["references"],
    }


def trace_candidates(candidate_inputs: list[str], repo_root: Path, limit: int) -> list[dict[str, object]]:
    states = [initialize_candidate_state(candidate_input, repo_root) for candidate_input in candidate_inputs]
    scan_references(states, repo_root, limit)
    return [finalize_candidate_state(state) for state in states]


def print_markdown(results: list[dict[str, object]]) -> None:
    for result in results:
        print(f"## {result['path']}")
        print(f"- recommendation: `{result['recommendation']}`")
        print(f"- lane: `{result['lane']}`")
        print(f"- zone: `{result['zone']}`")
        print(f"- dated_name: `{str(result['dated_name']).lower()}`")
        print(f"- strong_ref_count: `{result['strong_ref_count']}`")
        print(f"- weak_ref_count: `{result['weak_ref_count']}`")
        for reason in result["reasons"]:
            print(f"- reason: {reason}")
        refs = result["references"]
        if refs:
            print("- references:")
            for reference in refs:
                safe_excerpt = reference["excerpt"].encode("ascii", "replace").decode("ascii")
                print(
                    f"  - [{reference['strength']}] {reference['file']}:{reference['line']} :: {safe_excerpt}"
                )
        else:
            print("- references: none captured")
        print()


def main() -> int:
    args = parse_args()
    repo_root = Path(args.repo_root).resolve()
    results = trace_candidates(args.path, repo_root, args.limit)
    payload = {"repo_root": str(repo_root), "results": results}
    if args.json:
        json.dump(payload, sys.stdout, indent=2)
        sys.stdout.write("\n")
    else:
        print_markdown(results)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
