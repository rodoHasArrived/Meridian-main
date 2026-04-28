#!/usr/bin/env python3
"""Compare run-contract outputs against an approved baseline and flag regressions."""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

STATUS_SCORE = {
    "pass": 3,
    "warning": 2,
    "fail": 1,
    "not-run": 0,
}


@dataclass(frozen=True)
class Regression:
    workflow_id: str
    category: str
    severity: str
    message: str


def load_contract(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def index_by_id(items: list[dict], key: str = "id") -> dict[str, dict]:
    return {str(item.get(key)): item for item in items if item.get(key) is not None}


def status_degraded(baseline_status: str, current_status: str) -> bool:
    return STATUS_SCORE.get(current_status, -1) < STATUS_SCORE.get(baseline_status, -1)


def degradation_percent(baseline_value: float, current_value: float, direction: str) -> float | None:
    if baseline_value == 0:
        return None

    change_percent = ((current_value - baseline_value) / abs(baseline_value)) * 100.0

    if direction == "higher_is_better":
        return -change_percent
    if direction == "lower_is_better":
        return change_percent
    return abs(change_percent)


def compare_workflow(baseline: dict, current: dict) -> list[Regression]:
    regressions: list[Regression] = []
    workflow_id = str(baseline.get("workflowId", "unknown"))

    baseline_gate = str(baseline.get("gateStatus", "not-run"))
    current_gate = str(current.get("gateStatus", "not-run"))
    if status_degraded(baseline_gate, current_gate):
        regressions.append(
            Regression(
                workflow_id=workflow_id,
                category="gate_status",
                severity="failure",
                message=f"Gate status regressed from '{baseline_gate}' to '{current_gate}'.",
            )
        )

    baseline_checkpoints = index_by_id(baseline.get("invariantCheckpoints", []))
    current_checkpoints = index_by_id(current.get("invariantCheckpoints", []))

    for checkpoint_id, baseline_checkpoint in baseline_checkpoints.items():
        current_checkpoint = current_checkpoints.get(checkpoint_id)
        if current_checkpoint is None:
            regressions.append(
                Regression(
                    workflow_id=workflow_id,
                    category="missing_step",
                    severity="failure",
                    message=f"Invariant checkpoint '{checkpoint_id}' is missing in current output.",
                )
            )
            continue

        baseline_status = str(baseline_checkpoint.get("status", "not-run"))
        current_status = str(current_checkpoint.get("status", "not-run"))
        if status_degraded(baseline_status, current_status):
            regressions.append(
                Regression(
                    workflow_id=workflow_id,
                    category="missing_step",
                    severity="failure",
                    message=(
                        f"Invariant checkpoint '{checkpoint_id}' regressed from '{baseline_status}' "
                        f"to '{current_status}'."
                    ),
                )
            )

    baseline_metrics = index_by_id(baseline.get("keyMetrics", []))
    current_metrics = index_by_id(current.get("keyMetrics", []))
    tolerance_windows = index_by_id(current.get("acceptedToleranceWindows", []), key="metricId")
    if not tolerance_windows:
        tolerance_windows = index_by_id(baseline.get("acceptedToleranceWindows", []), key="metricId")

    for metric_id, baseline_metric in baseline_metrics.items():
        current_metric = current_metrics.get(metric_id)
        if current_metric is None:
            regressions.append(
                Regression(
                    workflow_id=workflow_id,
                    category="degraded_metric",
                    severity="failure",
                    message=f"Metric '{metric_id}' is missing in current output.",
                )
            )
            continue

        baseline_value = float(baseline_metric.get("value", 0.0))
        current_value = float(current_metric.get("value", 0.0))
        direction = str(current_metric.get("direction") or baseline_metric.get("direction") or "stable")
        degradation = degradation_percent(baseline_value, current_value, direction)
        if degradation is None:
            continue

        tolerance = tolerance_windows.get(metric_id)
        if tolerance is None:
            continue

        warning_threshold = float(tolerance.get("warningDegradationPercent", 0.0))
        failure_threshold = float(tolerance.get("failureDegradationPercent", warning_threshold))

        if degradation >= failure_threshold:
            regressions.append(
                Regression(
                    workflow_id=workflow_id,
                    category="degraded_metric",
                    severity="failure",
                    message=(
                        f"Metric '{metric_id}' degraded by {degradation:.2f}% "
                        f"(failure threshold {failure_threshold:.2f}%)."
                    ),
                )
            )
        elif degradation >= warning_threshold:
            regressions.append(
                Regression(
                    workflow_id=workflow_id,
                    category="degraded_metric",
                    severity="warning",
                    message=(
                        f"Metric '{metric_id}' degraded by {degradation:.2f}% "
                        f"(warning threshold {warning_threshold:.2f}%)."
                    ),
                )
            )

    baseline_artifacts = index_by_id(baseline.get("requiredArtifactPaths", []))
    current_artifacts = index_by_id(current.get("requiredArtifactPaths", []))

    for artifact_id, baseline_artifact in baseline_artifacts.items():
        if not bool(baseline_artifact.get("required", True)):
            continue

        current_artifact = current_artifacts.get(artifact_id)
        if current_artifact is None:
            regressions.append(
                Regression(
                    workflow_id=workflow_id,
                    category="required_artifact",
                    severity="failure",
                    message=f"Required artifact '{artifact_id}' is missing in current output.",
                )
            )

    return regressions


def compare_contracts(baseline_contract: dict, current_contract: dict) -> list[Regression]:
    regressions: list[Regression] = []

    baseline_workflows = {item["workflowId"]: item for item in baseline_contract.get("workflows", [])}
    current_workflows = {item["workflowId"]: item for item in current_contract.get("workflows", [])}

    for workflow_id, baseline_workflow in baseline_workflows.items():
        if workflow_id not in current_workflows:
            regressions.append(
                Regression(
                    workflow_id=workflow_id,
                    category="missing_step",
                    severity="failure",
                    message=f"Workflow '{workflow_id}' is missing from current run-contract output.",
                )
            )
            continue

        regressions.extend(compare_workflow(baseline_workflow, current_workflows[workflow_id]))

    return regressions


def render_markdown_summary(
    baseline_path: Path,
    current_path: Path,
    regressions: list[Regression],
    generated_at_utc: str,
) -> str:
    failure_count = sum(1 for regression in regressions if regression.severity == "failure")
    warning_count = sum(1 for regression in regressions if regression.severity == "warning")

    lines = [
        "# Run-Contract Comparator Summary",
        "",
        f"- Generated (UTC): {generated_at_utc}",
        f"- Baseline: `{baseline_path}`",
        f"- Current: `{current_path}`",
        f"- Failures: **{failure_count}**",
        f"- Warnings: **{warning_count}**",
        "",
        "## Operator Sign-off Packet Summary",
        "",
    ]

    if regressions:
        lines.extend([
            "| Workflow | Category | Severity | Finding |",
            "|----------|----------|----------|---------|",
        ])
        for regression in regressions:
            lines.append(
                f"| `{regression.workflow_id}` | `{regression.category}` | `{regression.severity}` | {regression.message} |"
            )
    else:
        lines.append("No regressions detected against the approved baseline.")

    lines.extend([
        "",
        "## Roadmap Cadence Review Summary",
        "",
        "- Gate trend: " + ("regressed" if failure_count > 0 else "stable"),
        "- Action: " + (
            "Block promotion until failed findings are remediated and re-run comparator."
            if failure_count > 0
            else "Continue cadence with no blocking regression delta."
        ),
        "",
        "## Publishing",
        "",
        "This summary and the machine-readable comparator report were published under the selected artifacts output directory.",
    ])

    return "\n".join(lines) + "\n"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Compare run-contract outputs against the last approved baseline.")
    parser.add_argument("--baseline", required=True, help="Path to the approved baseline run-contract JSON file.")
    parser.add_argument("--current", required=True, help="Path to the current run-contract JSON file.")
    parser.add_argument(
        "--output-dir",
        default=None,
        help="Output directory for comparator artifacts (default: artifacts/run-contract-comparator/<timestamp>).",
    )
    return parser


def main_args(argv: list[str]) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    baseline_path = Path(args.baseline)
    current_path = Path(args.current)

    baseline_contract = load_contract(baseline_path)
    current_contract = load_contract(current_path)
    regressions = compare_contracts(baseline_contract, current_contract)

    generated_at_utc = datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")

    if args.output_dir:
        output_dir = Path(args.output_dir)
    else:
        timestamp = generated_at_utc.replace(":", "").replace("-", "")
        output_dir = Path("artifacts") / "run-contract-comparator" / timestamp

    output_dir.mkdir(parents=True, exist_ok=True)

    summary = {
        "generatedAtUtc": generated_at_utc,
        "baselinePath": str(baseline_path),
        "currentPath": str(current_path),
        "status": "failed" if any(item.severity == "failure" for item in regressions) else "passed",
        "totals": {
            "regressions": len(regressions),
            "failures": sum(1 for item in regressions if item.severity == "failure"),
            "warnings": sum(1 for item in regressions if item.severity == "warning"),
        },
        "findings": [
            {
                "workflowId": item.workflow_id,
                "category": item.category,
                "severity": item.severity,
                "message": item.message,
            }
            for item in regressions
        ],
    }

    summary_json_path = output_dir / "run-contract-comparator.json"
    summary_json_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")

    summary_md_path = output_dir / "run-contract-comparator.md"
    summary_md_path.write_text(
        render_markdown_summary(baseline_path, current_path, regressions, generated_at_utc),
        encoding="utf-8",
    )

    print(f"Comparator report: {summary_json_path}")
    print(f"Comparator markdown: {summary_md_path}")

    return 1 if summary["status"] == "failed" else 0


def main() -> int:
    return main_args(sys.argv[1:])


if __name__ == "__main__":
    sys.exit(main())
