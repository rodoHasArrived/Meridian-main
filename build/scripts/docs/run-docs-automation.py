#!/usr/bin/env python3
"""Documentation automation orchestrator.

Provides a single entry point for running the repository documentation
automation scripts with selectable profiles and machine-readable summary output.

Examples:
    python3 build/scripts/docs/run-docs-automation.py --profile quick --dry-run
    python3 build/scripts/docs/run-docs-automation.py --profile full --json-output docs/status/docs-automation-summary.json
    python3 build/scripts/docs/run-docs-automation.py --scripts scan-todos,validate-examples
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Sequence


# Path constants for JSON output coordination
TODO_SCAN_JSON_PATH = "docs/status/todo-scan-results.json"
TODO_ISSUE_SUMMARY_PATH = "docs/status/todo-issue-creation-summary.json"


@dataclass
class ScriptResult:
    """Execution result for a single documentation automation script."""

    name: str
    command: List[str]
    return_code: int
    duration_seconds: float
    status: str
    output_file: str | None = None
    error: str | None = None


SCRIPT_CONFIG: Dict[str, Dict[str, Sequence[str] | str]] = {
    "scan-todos": {
        "script": "scan-todos.py",
        "args": ["--output", "docs/status/TODO.md", "--json-output", "docs/status/todo-scan-results.json"],
        "output": "docs/status/TODO.md",
    },
    "generate-structure-docs": {
        "script": "generate-structure-docs.py",
        "args": ["--output", "docs/generated/repository-structure.md"],
        "output": "docs/generated/repository-structure.md",
    },
    "generate-health-dashboard": {
        "script": "generate-health-dashboard.py",
        "args": ["--output", "docs/status/health-dashboard.md", "--json-output", "docs/status/health-dashboard.json"],
        "output": "docs/status/health-dashboard.md",
    },
    "repair-links": {
        "script": "repair-links.py",
        "args": ["--output", "docs/status/link-repair-report.md"],
        "output": "docs/status/link-repair-report.md",
    },
    "validate-examples": {
        "script": "validate-examples.py",
        "args": ["--output", "docs/status/example-validation.md"],
        "output": "docs/status/example-validation.md",
    },
    "check-ai-inventory": {
        "script": "check-ai-inventory.py",
        "args": [
            "--output",
            "docs/status/ai-inventory-report.md",
            "--json-output",
            "docs/status/ai-inventory-report.json",
        ],
        "output": "docs/status/ai-inventory-report.md",
    },
    "generate-coverage": {
        "script": "generate-coverage.py",
        "args": ["--output", "docs/status/coverage-report.md"],
        "output": "docs/status/coverage-report.md",
    },
    "generate-changelog": {
        "script": "generate-changelog.py",
        "args": ["--output", "docs/status/CHANGELOG.md", "--recent", "50"],
        "output": "docs/status/CHANGELOG.md",
    },
    "rules-engine": {
        "script": "rules-engine.py",
        "args": ["--rules", "build/rules/doc-rules.yaml", "--output", "docs/status/rules-report.md"],
        "output": "docs/status/rules-report.md",
    },
    "validate-api-docs": {
        "script": "validate-api-docs.py",
        "args": ["--output", "docs/status/api-docs-report.md"],
        "output": "docs/status/api-docs-report.md",
    },
    "generate-dependency-graph": {
        "script": "generate-dependency-graph.py",
        "args": ["--output", "docs/generated/project-dependencies.md", "--format", "markdown"],
        "output": "docs/generated/project-dependencies.md",
    },
    "sync-readme-badges": {
        "script": "sync-readme-badges.py",
        "args": ["--output", "docs/status/badge-sync-report.md"],
        "output": "docs/status/badge-sync-report.md",
    },
    "generate-metrics-dashboard": {
        "script": "generate-metrics-dashboard.py",
        "args": ["--output", "docs/status/metrics-dashboard.md", "--json-output", "docs/status/metrics-dashboard.json"],
        "output": "docs/status/metrics-dashboard.md",
    },
    "generate-workflow-manifest": {
        "script": "generate-workflow-manifest.py",
        "args": [],
        "output": "docs/status/workflow-validation-summary.json",
    },
    "create-todo-issues": {
        "script": "create-todo-issues.py",
        "args": ["--scan-json", TODO_SCAN_JSON_PATH, "--output-json", TODO_ISSUE_SUMMARY_PATH],
        "output": TODO_ISSUE_SUMMARY_PATH,
    },
}

PROFILE_CONFIG: Dict[str, List[str]] = {
    "quick": ["scan-todos", "validate-examples", "repair-links", "check-ai-inventory", "generate-workflow-manifest"],
    "core": [
        "scan-todos",
        "generate-structure-docs",
        "generate-health-dashboard",
        "validate-examples",
        "check-ai-inventory",
        "generate-coverage",
        "generate-workflow-manifest",
    ],
    "full": [
        "scan-todos",
        "generate-structure-docs",
        "generate-health-dashboard",
        "repair-links",
        "validate-examples",
        "check-ai-inventory",
        "generate-coverage",
        "generate-changelog",
        "rules-engine",
        "validate-api-docs",
        "generate-dependency-graph",
        "sync-readme-badges",
        "generate-metrics-dashboard",
        "generate-workflow-manifest",
    ],
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run documentation automation scripts with profiles.")
    parser.add_argument(
        "--profile",
        choices=sorted(PROFILE_CONFIG.keys()),
        default="core",
        help="Named script profile to execute (default: core).",
    )
    parser.add_argument(
        "--scripts",
        help="Comma-separated script names to run instead of a profile.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print what would run without executing scripts.",
    )
    parser.add_argument(
        "--continue-on-error",
        action="store_true",
        help="Continue running remaining scripts even if one fails.",
    )
    parser.add_argument(
        "--json-output",
        help="Optional JSON output path for run summary.",
    )
    parser.add_argument(
        "--summary-output",
        help="Optional Markdown output path for run summary.",
    )
    parser.add_argument(
        "--auto-create-todos",
        action="store_true",
        help="After scan-todos, create GitHub issues for untracked TODO items.",
    )
    parser.add_argument(
        "--todo-repo",
        help="Repository slug (owner/repo) for TODO issue creation; defaults to GITHUB_REPOSITORY.",
    )
    parser.add_argument(
        "--todo-token",
        help="GitHub token for TODO issue creation; defaults to GITHUB_TOKEN / GH_TOKEN.",
    )
    parser.add_argument(
        "--todo-max-issues",
        type=int,
        default=20,
        help="Maximum TODO issues to create when --auto-create-todos is enabled.",
    )
    return parser.parse_args()


def resolve_selected_scripts(args: argparse.Namespace) -> List[str]:
    if args.scripts:
        selected = [name.strip() for name in args.scripts.split(",") if name.strip()]
    else:
        selected = PROFILE_CONFIG[args.profile]

    unknown = [name for name in selected if name not in SCRIPT_CONFIG]
    if unknown:
        raise ValueError(f"Unknown script names: {', '.join(unknown)}")

    # Validation: --auto-create-todos requires scan-todos
    if args.auto_create_todos and "scan-todos" not in selected:
        raise ValueError(
            "--auto-create-todos requires scan-todos to be in the selected scripts. "
            "Either add scan-todos to --scripts or use a profile that includes it."
        )

    return selected


def run_script_with_args(name: str, root: Path, extra_args: Sequence[str] | None = None) -> ScriptResult:
    config = SCRIPT_CONFIG[name]
    script_path = root / "build" / "scripts" / "docs" / str(config["script"])
    command = ["python3", str(script_path), *[str(arg) for arg in config.get("args", [])]]
    if extra_args:
        command.extend(str(arg) for arg in extra_args)

    started = time.perf_counter()
    proc = subprocess.run(command, cwd=root, capture_output=True, text=True)
    duration = time.perf_counter() - started

    if proc.returncode == 0:
        status = "success"
        error = None
    else:
        status = "failed"
        stderr = proc.stderr.strip()
        stdout = proc.stdout.strip()
        error = stderr or stdout or "Script exited with non-zero status"

    return ScriptResult(
        name=name,
        command=command,
        return_code=proc.returncode,
        duration_seconds=round(duration, 3),
        status=status,
        output_file=str(config.get("output")) if config.get("output") else None,
        error=error,
    )


def run_script(name: str, root: Path) -> ScriptResult:
    return run_script_with_args(name, root, extra_args=None)


def write_markdown_summary(path: Path, results: Iterable[ScriptResult], dry_run: bool) -> None:
    rows = list(results)
    lines = [
        "# Documentation Automation Run Summary",
        "",
        "> Auto-generated by `build/scripts/docs/run-docs-automation.py`.",
        "",
        f"- Dry run: `{str(dry_run).lower()}`",
        "",
        "| Script | Status | Duration (s) | Output |",
        "|--------|--------|--------------|--------|",
    ]
    for result in rows:
        output = result.output_file or "-"
        lines.append(
            f"| `{result.name}` | `{result.status}` | `{result.duration_seconds:.3f}` | `{output}` |"
        )

    failures = [r for r in rows if r.status == "failed"]
    if failures:
        lines.append("")
        lines.append("## Failures")
        lines.append("")
        for failed in failures:
            lines.append(f"- `{failed.name}`: {failed.error or 'Unknown error'}")

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:  # noqa: C901
    args = parse_args()
    root = Path(__file__).resolve().parents[3]

    try:
        selected = resolve_selected_scripts(args)
    except ValueError as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 2

    print(f"Selected scripts ({len(selected)}): {', '.join(selected)}")

    results: List[ScriptResult] = []

    if args.dry_run:
        for name in selected:
            config = SCRIPT_CONFIG[name]
            script_path = root / "build" / "scripts" / "docs" / str(config["script"])
            command = ["python3", str(script_path), *[str(arg) for arg in config.get("args", [])]]
            # For scan-todos, add JSON output if --auto-create-todos is set
            if name == "scan-todos" and args.auto_create_todos:
                command.extend(["--json-output", TODO_SCAN_JSON_PATH])
            results.append(
                ScriptResult(
                    name=name,
                    command=command,
                    return_code=0,
                    duration_seconds=0.0,
                    status="planned",
                    output_file=str(config.get("output")) if config.get("output") else None,
                )
            )
            print(f"[dry-run] {' '.join(command)}")

        if args.auto_create_todos and "scan-todos" in selected:
            issue_cmd = [
                "python3",
                str(root / "build" / "scripts" / "docs" / "create-todo-issues.py"),
                "--scan-json",
                TODO_SCAN_JSON_PATH,
                "--output-json",
                TODO_ISSUE_SUMMARY_PATH,
                "--max-issues",
                str(args.todo_max_issues),
                "--dry-run",
            ]
            if args.todo_repo:
                issue_cmd.extend(["--repo", args.todo_repo])
            results.append(
                ScriptResult(
                    name="create-todo-issues",
                    command=issue_cmd,
                    return_code=0,
                    duration_seconds=0.0,
                    status="planned",
                    output_file=TODO_ISSUE_SUMMARY_PATH,
                )
            )
            print(f"[dry-run] {' '.join(issue_cmd)}")
    else:
        for name in selected:
            extra_args: List[str] = []
            if name == "scan-todos" and args.auto_create_todos:
                extra_args.extend(["--json-output", TODO_SCAN_JSON_PATH])

            print(f"Running {name}...")
            result = run_script_with_args(name, root, extra_args=extra_args)
            results.append(result)
            print(f" -> {result.status} ({result.duration_seconds:.3f}s)")

            if result.status != "success" and not args.continue_on_error:
                print("Stopping due to failure. Use --continue-on-error to continue.", file=sys.stderr)
                break

        any_failed = any(r.status == "failed" for r in results)

        if args.auto_create_todos and "scan-todos" in selected:
            scan_result = next((result for result in results if result.name == "scan-todos"), None)
            if any_failed and not args.continue_on_error:
                print(
                    "Skipping create-todo-issues because a prior script failed "
                    "(use --continue-on-error to override).",
                    file=sys.stderr,
                )
            elif scan_result is None or scan_result.status != "success":
                print("Skipping create-todo-issues because scan-todos did not succeed.", file=sys.stderr)
            else:
                # Build additional args beyond SCRIPT_CONFIG defaults
                issue_args: List[str] = ["--max-issues", str(args.todo_max_issues)]
                if args.todo_repo:
                    issue_args.extend(["--repo", args.todo_repo])
                if args.todo_token:
                    issue_args.extend(["--token", args.todo_token])

                print("Running create-todo-issues...")
                todo_result = run_script_with_args("create-todo-issues", root, extra_args=issue_args)
                results.append(todo_result)
                print(f" -> {todo_result.status} ({todo_result.duration_seconds:.3f}s)")

                if todo_result.status != "success" and not args.continue_on_error:
                    print("Stopping due to failure. Use --continue-on-error to continue.", file=sys.stderr)

    if args.summary_output:
        write_markdown_summary(Path(args.summary_output), results, args.dry_run)
        print(f"Wrote markdown summary: {args.summary_output}")

    if args.json_output:
        payload = {
            "dry_run": args.dry_run,
            "profile": args.profile,
            "selected_scripts": selected,
            "results": [asdict(result) for result in results],
        }
        out = Path(args.json_output)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
        print(f"Wrote JSON summary: {args.json_output}")

    failed = [r for r in results if r.status == "failed"]
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
