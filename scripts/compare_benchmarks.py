#!/usr/bin/env python3
import argparse
import json
import sys
from pathlib import Path


def load_report(results_dir: Path) -> dict:
    json_files = sorted(results_dir.rglob("*.json"))
    if not json_files:
        raise FileNotFoundError(f"No BenchmarkDotNet JSON results found under {results_dir}")

    with json_files[0].open("r", encoding="utf-8") as handle:
        return json.load(handle)


def benchmark_name(item: dict) -> str:
    name = item.get("MethodTitle") or item.get("Method") or item.get("FullName") or "unknown"
    params = item.get("Parameters")
    if params:
        name = f"{name} ({params})"
    return name


def benchmark_key(item: dict) -> str:
    return item.get("FullName") or benchmark_name(item)


def mean_ns(item: dict) -> float:
    return float(item.get("Statistics", {}).get("Mean", 0) or 0)


def allocated_bytes(item: dict) -> float:
    return float(item.get("Memory", {}).get("BytesAllocatedPerOperation", 0) or 0)


def format_ns(value: float) -> str:
    if value >= 1_000_000_000:
        return f"{value / 1_000_000_000:.2f} s"
    if value >= 1_000_000:
        return f"{value / 1_000_000:.2f} ms"
    if value >= 1_000:
        return f"{value / 1_000:.2f} us"
    return f"{value:.1f} ns"


def render_report(current: dict, baseline: dict | None, warn_threshold: float, fail_threshold: float) -> tuple[str, bool]:  # noqa: C901
    lines: list[str] = ["## Benchmark Regression Report", ""]

    current_benchmarks = current.get("Benchmarks", [])
    if not current_benchmarks:
        lines.append("_No current benchmark results found._")
        return "\n".join(lines), False

    lines.extend([
        "### Current Results",
        "",
        "| Benchmark | Mean | Allocated |",
        "|-----------|------|-----------|",
    ])

    for item in current_benchmarks:
        lines.append(f"| {benchmark_name(item)} | {format_ns(mean_ns(item))} | {allocated_bytes(item):,.0f} B |")

    if baseline is None:
        lines.extend(["", "_Baseline comparison unavailable._"])
        return "\n".join(lines), False

    baseline_map = {
        benchmark_key(item): {
            "name": benchmark_name(item),
            "mean": mean_ns(item),
            "alloc": allocated_bytes(item),
        }
        for item in baseline.get("Benchmarks", [])
    }

    regressions: list[tuple[str, float, float, float, float, float]] = []
    warnings: list[tuple[str, float, float, float, float, float]] = []
    improvements: list[tuple[str, float, float, float, float, float]] = []

    for item in current_benchmarks:
        key = benchmark_key(item)
        if key not in baseline_map or baseline_map[key]["mean"] <= 0:
            continue

        current_mean = mean_ns(item)
        baseline_mean = baseline_map[key]["mean"]
        change_pct = ((current_mean - baseline_mean) / baseline_mean) * 100
        record = (
            benchmark_name(item),
            change_pct,
            current_mean,
            baseline_mean,
            allocated_bytes(item),
            baseline_map[key]["alloc"],
        )

        if change_pct >= fail_threshold:
            regressions.append(record)
        elif change_pct >= warn_threshold:
            warnings.append(record)
        elif change_pct <= -warn_threshold:
            improvements.append(record)

    lines.extend(["", "### Comparison vs baseline", ""])

    if regressions:
        lines.extend([
            f"**Failing regressions (≥ {fail_threshold:.1f}% slower):**",
            "",
            "| Benchmark | Change | Current | Baseline | Current Alloc | Baseline Alloc |",
            "|-----------|--------|---------|----------|---------------|----------------|",
        ])
        for name, change, current_mean, baseline_mean, current_alloc, baseline_alloc in sorted(regressions, key=lambda row: -row[1]):  # noqa: E501
            lines.append(
                f"| {name} | +{change:.1f}% | {format_ns(current_mean)} | {format_ns(baseline_mean)} | "
                f"{current_alloc:,.0f} B | {baseline_alloc:,.0f} B |"
            )

    if warnings:
        lines.extend([
            "",
            f"**Warnings (≥ {warn_threshold:.1f}% slower and < {fail_threshold:.1f}%):**",
            "",
            "| Benchmark | Change | Current | Baseline | Current Alloc | Baseline Alloc |",
            "|-----------|--------|---------|----------|---------------|----------------|",
        ])
        for name, change, current_mean, baseline_mean, current_alloc, baseline_alloc in sorted(warnings, key=lambda row: -row[1]):  # noqa: E501
            lines.append(
                f"| {name} | +{change:.1f}% | {format_ns(current_mean)} | {format_ns(baseline_mean)} | "
                f"{current_alloc:,.0f} B | {baseline_alloc:,.0f} B |"
            )

    if improvements:
        lines.extend([
            "",
            f"**Improvements (≥ {warn_threshold:.1f}% faster):**",
            "",
            "| Benchmark | Change |",
            "|-----------|--------|",
        ])
        for name, change, *_ in sorted(improvements, key=lambda row: row[1]):
            lines.append(f"| {name} | {change:.1f}% |")

    if not regressions and not warnings and not improvements:
        lines.append("No material regressions or improvements detected.")

    lines.extend([
        "",
        f"**Summary:** {len(regressions)} failing regressions, {len(warnings)} warnings, {len(improvements)} improvements.",
    ])

    return "\n".join(lines), bool(regressions)


def main() -> int:
    parser = argparse.ArgumentParser(description="Compare BenchmarkDotNet result sets.")
    parser.add_argument("--current", required=True, help="Directory containing current BenchmarkDotNet JSON results.")
    parser.add_argument("--baseline", help="Directory containing baseline BenchmarkDotNet JSON results.")
    parser.add_argument("--warn-threshold", type=float, default=10.0)
    parser.add_argument("--fail-threshold", type=float, default=25.0)
    parser.add_argument("--summary-out", help="Optional markdown output path.")
    args = parser.parse_args()

    current = load_report(Path(args.current))
    baseline = load_report(Path(args.baseline)) if args.baseline else None
    summary, has_failure = render_report(current, baseline, args.warn_threshold, args.fail_threshold)

    if args.summary_out:
        Path(args.summary_out).write_text(summary + "\n", encoding="utf-8")
    else:
        print(summary)

    return 1 if has_failure else 0


if __name__ == "__main__":
    sys.exit(main())
