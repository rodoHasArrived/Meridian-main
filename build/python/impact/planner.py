"""Change-impact execution planner for Meridian.

Reads the canonical lane-map, matches changed files against trigger patterns,
and produces a machine-readable execution plan with reasons for every included
command.  The plan is also rendered as a Bash script that records per-step
timing telemetry.
"""
from __future__ import annotations

import fnmatch
import json
import os
import subprocess
import sys
import time
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


LANE_MAP_PATH = Path(__file__).parent / "lane_map.json"
TELEMETRY_DIR_REL = ".ai/impact-telemetry"


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------


@dataclass
class PlannedGate:
    name: str
    command: str
    reason: str = "always-on safety gate"


@dataclass
class PlannedStep:
    name: str
    command: str
    lane: str


@dataclass
class ActivatedLane:
    name: str
    reason: str  # "triggered by: <file>" or "all-lanes mode"
    steps: list[PlannedStep] = field(default_factory=list)


@dataclass
class ImpactPlan:
    timestamp: str
    base: str
    head: str
    changed_files: list[str]
    all_lanes_mode: bool
    safety_gates: list[PlannedGate]
    activated_lanes: list[ActivatedLane]
    skipped_lanes: list[str]
    telemetry: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        return {
            "_schema": "meridian-impact-plan/1.0",
            "timestamp": self.timestamp,
            "base": self.base,
            "head": self.head,
            "changed_files": self.changed_files,
            "all_lanes_mode": self.all_lanes_mode,
            "safety_gates": [
                {"name": g.name, "command": g.command, "reason": g.reason}
                for g in self.safety_gates
            ],
            "activated_lanes": [
                {
                    "name": al.name,
                    "reason": al.reason,
                    "steps": [
                        {"name": s.name, "command": s.command, "lane": s.lane}
                        for s in al.steps
                    ],
                }
                for al in self.activated_lanes
            ],
            "skipped_lanes": self.skipped_lanes,
            "telemetry": self.telemetry,
        }


# ---------------------------------------------------------------------------
# Pattern matching
# ---------------------------------------------------------------------------


def _matches_any(path: str, patterns: list[str]) -> bool:
    """Return True if *path* matches at least one glob *pattern*."""
    for pattern in patterns:
        # Normalise double-star globs for fnmatch
        if fnmatch.fnmatch(path, pattern):
            return True
        # Handle "src/Foo/**" matching "src/Foo/Bar/Baz.cs"
        if pattern.endswith("/**"):
            prefix = pattern[:-3]
            if path == prefix or path.startswith(prefix + "/"):
                return True
    return False


def _first_triggering_file(changed: list[str], patterns: list[str]) -> str | None:
    for f in changed:
        if _matches_any(f, patterns):
            return f
    return None


# ---------------------------------------------------------------------------
# Planner
# ---------------------------------------------------------------------------


class ImpactPlanner:
    def __init__(self, lane_map_path: Path = LANE_MAP_PATH) -> None:
        with lane_map_path.open(encoding="utf-8") as fh:
            self._map: dict[str, Any] = json.load(fh)

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    def compute_changed_files(self, base: str, head: str) -> list[str]:
        """Return the list of files changed between *base* and *head* refs."""
        result = subprocess.run(
            ["git", "diff", "--name-only", f"{base}...{head}"],
            capture_output=True,
            text=True,
            check=False,
        )
        if result.returncode != 0:
            # Fall back to single-commit diff
            result = subprocess.run(
                ["git", "diff", "--name-only", f"{base}", f"{head}"],
                capture_output=True,
                text=True,
                check=False,
            )
        if result.returncode != 0:
            return []
        return [f for f in result.stdout.strip().splitlines() if f]

    def plan(
        self,
        changed_files: list[str],
        *,
        base: str = "origin/main",
        head: str = "HEAD",
        all_lanes: bool = False,
    ) -> ImpactPlan:
        """Build an execution plan for *changed_files*.

        Parameters
        ----------
        changed_files:
            Paths relative to repo root that have changed.
        base, head:
            Refs used when computing the diff (informational only at this point).
        all_lanes:
            When True every lane is activated regardless of trigger patterns
            (used for pushes to main and manual workflow dispatches).
        """
        timestamp = datetime.now(tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

        safety_gates = [
            PlannedGate(name=sg["name"], command=sg["command"])
            for sg in self._map.get("safety_gates", [])
        ]

        activated_lanes: list[ActivatedLane] = []
        skipped_lanes: list[str] = []

        for lane_def in self._map.get("lanes", []):
            name: str = lane_def["name"]
            if all_lanes:
                reason = "all-lanes mode"
            else:
                trigger_file = _first_triggering_file(
                    changed_files, lane_def.get("trigger_patterns", [])
                )
                if trigger_file is None:
                    skipped_lanes.append(name)
                    continue
                reason = f"triggered by: {trigger_file}"

            steps = [
                PlannedStep(
                    name=s["name"],
                    command=s["command"],
                    lane=name,
                )
                for s in lane_def.get("steps", [])
            ]
            activated_lanes.append(ActivatedLane(name=name, reason=reason, steps=steps))

        return ImpactPlan(
            timestamp=timestamp,
            base=base,
            head=head,
            changed_files=changed_files,
            all_lanes_mode=all_lanes,
            safety_gates=safety_gates,
            activated_lanes=activated_lanes,
            skipped_lanes=skipped_lanes,
        )


# ---------------------------------------------------------------------------
# Shell-script emitter
# ---------------------------------------------------------------------------


def emit_shell_script(plan: ImpactPlan, telemetry_path: str | None = None) -> str:
    """Return a Bash script string that executes the plan and records telemetry."""
    activated_names = [al.name for al in plan.activated_lanes]
    skipped_str = ", ".join(plan.skipped_lanes) if plan.skipped_lanes else "none"
    telemetry_file = (
        telemetry_path
        if telemetry_path
        else f"{TELEMETRY_DIR_REL}/{plan.timestamp.replace(':', '-')}.json"
    )

    lines: list[str] = [
        "#!/usr/bin/env bash",
        f"# Meridian Impact Plan — {plan.timestamp}",
        f"# Base: {plan.base}  Head: {plan.head}",
        f"# Changed files: {len(plan.changed_files)}",
        f"# Activated lanes: {', '.join(activated_names) if activated_names else 'none'}",
        f"# Skipped lanes: {skipped_str}",
        "",
        "set -Eeuo pipefail",
        "",
        "mkdir -p artifacts/build-logs artifacts/test-results/dotnet .ai/impact-telemetry",
        "",
        "_impact_start=$(date +%s%3N)",
        "_step_timings=()",
        "",
        "_run_step() {",
        "    local _name=$1",
        "    local _cmd=$2",
        "    local _t0; _t0=$(date +%s%3N)",
        "    printf '\\n[impact] %-40s ...\\n' \"$_name\"",
        '    if bash -o pipefail -c "$_cmd"; then',
        "        local _elapsed=$(( $(date +%s%3N) - _t0 ))",
        "        printf '[impact] %-40s OK  (%dms)\\n' \"$_name\" \"$_elapsed\"",
        '        _step_timings+=("{\\"name\\":\\"$_name\\",\\"status\\":\\"passed\\",\\"ms\\":$_elapsed}")',
        "    else",
        "        local _elapsed=$(( $(date +%s%3N) - _t0 ))",
        "        printf '[impact] %-40s FAILED (%dms)\\n' \"$_name\" \"$_elapsed\" >&2",
        '        _step_timings+=("{\\"name\\":\\"$_name\\",\\"status\\":\\"failed\\",\\"ms\\":$_elapsed}")',
        "        return 1",
        "    fi",
        "}",
        "",
    ]

    # Safety gates
    lines.append("# ── Safety gates (always-on) " + "─" * 51)
    for gate in plan.safety_gates:
        lines.append(f'_run_step {json.dumps(gate.name)} {json.dumps(gate.command)}')
    lines.append("")

    # Activated lanes
    if plan.activated_lanes:
        lines.append("# ── Impacted lanes " + "─" * 60)
        for al in plan.activated_lanes:
            lines.append(f"# Lane: {al.name}  ({al.reason})")
            for step in al.steps:
                step_id = f"{al.name}/{step.name}"
                lines.append(f'_run_step {json.dumps(step_id)} {json.dumps(step.command)}')
            lines.append("")
    else:
        lines.append("# No impacted lanes — only safety gates executed.")
        lines.append("")

    # Telemetry footer
    lines += [
        "_impact_elapsed=$(( $(date +%s%3N) - _impact_start ))",
        "",
        "# Write telemetry artifact",
        f"_telemetry_file={json.dumps(telemetry_file)}",
        "mkdir -p \"$(dirname \"$_telemetry_file\")\"",
        '_timings_json=$(IFS=,; echo "[${_step_timings[*]}]")',
        "cat > \"$_telemetry_file\" << _TEOF",
        "{",
        f'  "plan_timestamp": "{plan.timestamp}",',
        f'  "base": {json.dumps(plan.base)},',
        f'  "head": {json.dumps(plan.head)},',
        f'  "activated_lanes": {json.dumps(activated_names)},',
        f'  "skipped_lanes": {json.dumps(plan.skipped_lanes)},',
        '  "total_ms": $_impact_elapsed,',
        '  "steps": $_timings_json',
        "}",
        "_TEOF",
        "",
        'printf \'\\n[impact] Plan complete in %dms\\n\' "$_impact_elapsed"',
        'printf \'[impact] Telemetry written to %s\\n\' "$_telemetry_file"',
    ]

    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------------------
# GitHub Actions output emitter
# ---------------------------------------------------------------------------


def emit_github_outputs(plan: ImpactPlan, output_file: str) -> None:
    """Write lane activation flags to the GitHub Actions output file."""
    activated = {al.name for al in plan.activated_lanes}
    all_known: list[str] = [al.name for al in plan.activated_lanes] + plan.skipped_lanes

    with open(output_file, "a", encoding="utf-8") as fh:
        for lane_name in all_known:
            key = lane_name.replace("-", "_")
            value = "true" if lane_name in activated else "false"
            fh.write(f"{key}={value}\n")


# ---------------------------------------------------------------------------
# Text summary
# ---------------------------------------------------------------------------


def print_plan_summary(plan: ImpactPlan) -> None:
    """Print a human-readable summary of the plan to stdout."""
    print(f"\nMeridian Impact Plan — {plan.timestamp}")
    print(f"  Base : {plan.base}")
    print(f"  Head : {plan.head}")
    print(f"  Changed files: {len(plan.changed_files)}")
    if plan.changed_files:
        for f in plan.changed_files[:10]:
            print(f"    • {f}")
        if len(plan.changed_files) > 10:
            print(f"    … and {len(plan.changed_files) - 10} more")
    print()
    print(f"  Safety gates ({len(plan.safety_gates)}):")
    for g in plan.safety_gates:
        print(f"    ✔ {g.name}")
    print()
    if plan.activated_lanes:
        print(f"  Activated lanes ({len(plan.activated_lanes)}):")
        for al in plan.activated_lanes:
            print(f"    ► {al.name}  — {al.reason}")
            for s in al.steps:
                print(f"        • {s.name}")
    else:
        print("  No impacted lanes activated.")
    if plan.skipped_lanes:
        print()
        print(f"  Skipped lanes ({len(plan.skipped_lanes)}): {', '.join(plan.skipped_lanes)}")
    print()
