#!/usr/bin/env python3
"""Weekly impact-plan drift review.

Reads accumulated telemetry files from .ai/impact-telemetry/ and reports
on lane skipping rates, total run times, and mapping-quality drift.

A lane with a skip rate of 0 % (always activated) is a drift indicator —
it suggests the trigger patterns for that lane may be too broad or that
another lane's patterns are a superset.  Conversely, a lane that is *never*
activated is a coverage gap candidate.

Usage:
    python3 build/scripts/drift/weekly-drift-review.py [--telemetry-dir DIR] [--days N]
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from datetime import datetime, timedelta, timezone
from pathlib import Path


def _load_telemetry(telemetry_dir: Path, since: datetime) -> list[dict]:
    records = []
    for path in sorted(telemetry_dir.glob("*.json")):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except Exception:
            continue
        ts_str = data.get("plan_timestamp", "")
        try:
            ts = datetime.fromisoformat(ts_str.replace("Z", "+00:00"))
        except ValueError:
            continue
        if ts >= since:
            records.append(data)
    return records


def _analyse(records: list[dict]) -> None:
    if not records:
        print("No telemetry records found in the specified window.")
        return

    total_runs = len(records)
    lane_activation: dict[str, int] = defaultdict(int)
    lane_skip: dict[str, int] = defaultdict(int)
    run_times: list[int] = []

    for rec in records:
        activated = set(rec.get("activated_lanes", []))
        skipped = set(rec.get("skipped_lanes", []))
        all_lanes = activated | skipped
        for lane in all_lanes:
            if lane in activated:
                lane_activation[lane] += 1
            else:
                lane_skip[lane] += 1
        total_ms = rec.get("total_ms")
        if isinstance(total_ms, (int, float)):
            run_times.append(int(total_ms))

    all_lanes = sorted(set(lane_activation) | set(lane_skip))

    print(f"Impact-Plan Drift Review — {total_runs} runs analysed")
    print("=" * 70)
    print(f"{'Lane':<35} {'Activations':>12} {'Skips':>8} {'Activate%':>10}")
    print("-" * 70)

    always_on: list[str] = []
    never_on: list[str] = []

    for lane in all_lanes:
        act = lane_activation[lane]
        skip = lane_skip[lane]
        total = act + skip
        pct = (act / total * 100) if total > 0 else 0.0
        print(f"{lane:<35} {act:>12} {skip:>8} {pct:>9.1f}%")
        if pct >= 99.9:
            always_on.append(lane)
        if pct == 0.0 and total > 0:
            never_on.append(lane)

    print("=" * 70)

    if run_times:
        avg_ms = sum(run_times) // len(run_times)
        median_ms = sorted(run_times)[len(run_times) // 2]
        print(f"\nRun time  — avg: {avg_ms}ms  median: {median_ms}ms  n={len(run_times)}")

    if always_on:
        print(
            f"\n⚠  Always-activated lanes (potential over-broad trigger patterns): "
            f"{', '.join(always_on)}"
        )
    if never_on:
        print(
            f"\n⚠  Never-activated lanes (consider whether trigger patterns are reachable): "
            f"{', '.join(never_on)}"
        )
    if not always_on and not never_on:
        print("\n✔  No drift indicators detected.")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Weekly impact-plan drift review")
    parser.add_argument(
        "--telemetry-dir",
        default=".ai/impact-telemetry",
        metavar="DIR",
        help="Directory containing telemetry JSON files (default: .ai/impact-telemetry)",
    )
    parser.add_argument(
        "--days",
        type=int,
        default=7,
        metavar="N",
        help="Look back N days (default: 7)",
    )
    args = parser.parse_args(argv)

    telemetry_dir = Path(args.telemetry_dir)
    if not telemetry_dir.is_dir():
        print(
            f"Telemetry directory not found: {telemetry_dir}  "
            f"(run impact plans first to accumulate data)",
            file=sys.stderr,
        )
        return 0  # Not a hard failure — first run has no history

    since = datetime.now(tz=timezone.utc) - timedelta(days=args.days)
    records = _load_telemetry(telemetry_dir, since)
    _analyse(records)
    return 0


if __name__ == "__main__":
    sys.exit(main())
