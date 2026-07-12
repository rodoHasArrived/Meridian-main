#!/usr/bin/env python3
"""Write compact CI lane summaries from step status and known artifacts."""

from __future__ import annotations

import argparse
import json
import re
from dataclasses import dataclass
from pathlib import Path


KNOWN_PATTERNS: list[tuple[re.Pattern[str], str]] = [
    (re.compile(r"error NU1008"), "Central package management violation; remove Version= from PackageReference."),
    (re.compile(r"error NETSDK1100"), "Windows targeting is missing; add EnableWindowsTargeting=true."),
    (re.compile(r"error CS\d+"), "C# compile error."),
    (re.compile(r"error FS\d+"), "F# compile error."),
    (re.compile(r"error MSB\d+"), "MSBuild error."),
    (re.compile(r"Generated UI contract drift|generate-ui-api-routes-ts|generate-workspace-catalog-ts", re.IGNORECASE), "Generated UI contract drift was detected."),
    (re.compile(r"ESLint|npm .*run lint|lint .*failed", re.IGNORECASE), "Dashboard lint failed."),
    (re.compile(r"strictNullChecks|typecheck:strict|tsc .*--noEmit", re.IGNORECASE), "Dashboard strict TypeScript check failed."),
    (re.compile(r"npm ERR!|ERR_PNPM|vitest.*failed|failed.*vitest|Test Files\s+\d+\s+failed", re.IGNORECASE), "Dashboard npm or Vitest lane failed."),
    (re.compile(r"git diff .*exit-code|Render .*check drift|generated documentation drift|docs drift", re.IGNORECASE), "Generated documentation drift was detected."),
    (re.compile(r"Workflow hygiene checks failed|Lane manifest validation failed|actionlint", re.IGNORECASE), "Workflow hygiene or lane manifest validation failed."),
    (re.compile(r"System\.OutOfMemoryException"), "Build/test process ran out of memory."),
    (re.compile(r"VBCSCompiler|CS2012|being used by another process", re.IGNORECASE), "Likely compiler or output file lock contention."),
    (re.compile(r"dotnet_filter .* too broad|dotnet_filter is required"), "Targeted Test filter validation failed."),
    (re.compile(r"Mode '.*' requires runner=|Unsupported dotnet_project|Unsupported dotnet_filter", re.IGNORECASE), "Targeted Test mode or input validation failed."),
]

STEP_GATED_FINDINGS: dict[str, tuple[str, ...]] = {
    "Generated UI contract drift was detected.": ("Generated UI contract drift gate",),
    "Dashboard lint failed.": ("Lint dashboard source",),
    "Dashboard strict TypeScript check failed.": ("Enforce strictNullChecks on dashboard source",),
    "Dashboard npm or Vitest lane failed.": (
        "Install dashboard dependencies from lockfile",
        "Run dashboard tests",
        "Build dashboard bundle",
    ),
    "Generated documentation drift was detected.": ("Render roadmap/source docs and check drift",),
    "Workflow hygiene or lane manifest validation failed.": (
        "Validate lane manifest",
        "Validate workflow hygiene",
    ),
}

INTERESTING_LINE_RE = re.compile(
    r"(error\s+[A-Z]+[0-9]+|error:|failed|exception|Traceback|Unsupported|too broad|does not exist|drift|npm ERR!)",
    re.IGNORECASE,
)


@dataclass(frozen=True)
class StepResult:
    name: str
    exit_code: int
    duration_seconds: int

    @property
    def status(self) -> str:
        return "passed" if self.exit_code == 0 else "failed"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Summarize Meridian CI lane artifacts.")
    parser.add_argument("--lane", required=True, help="Lane id being summarized.")
    parser.add_argument("--exit-code", type=int, required=True, help="Overall lane exit code.")
    parser.add_argument("--steps-tsv", required=True, help="TSV file written by scripts/ci.sh.")
    parser.add_argument("--output", required=True, help="Markdown summary output path.")
    parser.add_argument("--github-step-summary", help="Optional GitHub step summary path.")
    parser.add_argument("--build-log", action="append", default=[], help="Build or validation log to inspect.")
    parser.add_argument("--dotnet-summary-json", help="run-dotnet-ci-tests JSON summary.")
    return parser.parse_args()


def parse_steps(path: Path) -> list[StepResult]:
    if not path.exists():
        return []
    steps: list[StepResult] = []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if not line.strip():
            continue
        parts = line.split("\t")
        if len(parts) != 3:
            continue
        name, exit_code, duration = parts
        try:
            steps.append(StepResult(name=name, exit_code=int(exit_code), duration_seconds=int(duration)))
        except ValueError:
            continue
    return steps


def read_text(path: Path) -> str:
    if not path.exists():
        return ""
    return path.read_text(encoding="utf-8", errors="replace")


def classify_known_patterns(text: str, steps: list[StepResult] | None = None) -> list[str]:
    failed_step_names = tuple(step.name for step in steps or [] if step.exit_code != 0)
    findings: list[str] = []
    for pattern, message in KNOWN_PATTERNS:
        if pattern.search(text):
            gated_steps = STEP_GATED_FINDINGS.get(message)
            if gated_steps and steps is not None:
                if not any(step_name == gated_step for step_name in failed_step_names for gated_step in gated_steps):
                    continue
            findings.append(message)
    return findings


def first_interesting_lines(text: str, *, limit: int = 8) -> list[str]:
    lines: list[str] = []
    for raw_line in text.splitlines():
        line = raw_line.strip()
        if not line or not INTERESTING_LINE_RE.search(line):
            continue
        if len(line) > 220:
            line = line[:217] + "..."
        lines.append(line)
        if len(lines) >= limit:
            break
    return lines


def load_dotnet_summary(path: Path | None) -> tuple[list[str], list[str]]:
    if path is None or not path.exists():
        return [], []

    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return [], ["Failed to read or parse .NET test summary."]
    if not isinstance(payload, dict):
        return [], ["Invalid .NET test summary format."]

    failed_projects = []
    results = payload.get("results", [])
    if not isinstance(results, list):
        results = []
    for result in results:
        if not isinstance(result, dict):
            continue
        exit_code = result.get("exit_code")
        if exit_code is None or exit_code == 0:
            continue
        name = result.get("name", "Unknown")
        result_path = result.get("path", "Unknown")
        failed_projects.append(f"{name} ({result_path}) exited {exit_code}")
    totals = [
        f"Total .NET slices: {payload.get('total', 0)}",
        f"Passed: {payload.get('passed', 0)}",
        f"Failed: {payload.get('failed', 0)}",
    ]
    return totals, failed_projects


def build_summary(
    *,
    lane: str,
    exit_code: int,
    steps: list[StepResult],
    dotnet_totals: list[str],
    failed_dotnet: list[str],
    known_findings: list[str],
    interesting_lines: list[str],
) -> str:
    result = "passed" if exit_code == 0 else "failed"
    lines = [
        f"### Meridian CI lane: `{lane}`",
        "",
        f"- Result: `{result}`",
        f"- Exit code: `{exit_code}`",
    ]

    if steps:
        lines.extend(["", "| Step | Status | Exit code | Duration (s) |", "| --- | --- | ---: | ---: |"])
        for step in steps:
            lines.append(f"| {step.name} | {step.status} | {step.exit_code} | {step.duration_seconds} |")

    if dotnet_totals:
        lines.extend(["", "#### .NET test summary", ""])
        lines.extend(f"- {entry}" for entry in dotnet_totals)

    if failed_dotnet:
        lines.extend(["", "#### Failing .NET slices", ""])
        lines.extend(f"- {entry}" for entry in failed_dotnet)

    if known_findings:
        lines.extend(["", "#### Known failure classification", ""])
        for finding in dict.fromkeys(known_findings):
            lines.append(f"- {finding}")

    if interesting_lines:
        lines.extend(["", "#### First useful failure lines", ""])
        lines.extend(f"- `{line}`" for line in interesting_lines)

    lines.extend(
        [
            "",
            "#### Artifact roots",
            "",
            "- `artifacts/ci-summary/`",
            "- `artifacts/build-logs/` when a build lane ran",
            "- `artifacts/test-results/` when a test lane ran",
        ]
    )
    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    args = parse_args()
    step_path = Path(args.steps_tsv)
    output_path = Path(args.output)
    dotnet_summary_path = Path(args.dotnet_summary_json) if args.dotnet_summary_json else None

    steps = parse_steps(step_path)
    log_texts = [read_text(Path(path)) for path in args.build_log]
    combined_logs = "\n".join(log_texts)
    dotnet_totals, failed_dotnet = load_dotnet_summary(dotnet_summary_path)

    summary = build_summary(
        lane=args.lane,
        exit_code=args.exit_code,
        steps=steps,
        dotnet_totals=dotnet_totals,
        failed_dotnet=failed_dotnet,
        known_findings=classify_known_patterns(combined_logs, steps),
        interesting_lines=first_interesting_lines(combined_logs),
    )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(summary, encoding="utf-8")

    if args.github_step_summary:
        github_summary_path = Path(args.github_step_summary)
        with github_summary_path.open("a", encoding="utf-8") as handle:
            handle.write("\n")
            handle.write(summary)

    print(f"Wrote CI summary: {output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
