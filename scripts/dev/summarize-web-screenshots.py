#!/usr/bin/env python3
"""Summarize web screenshot evidence, including incomplete and failed runs."""

from __future__ import annotations

import argparse
import html
import json
import math
import os
from pathlib import Path
from typing import Any


def escaped(value: Any, limit: int = 240) -> str:
    """Keep manifest-controlled text inside its Markdown cell."""
    text = " ".join(str(value).split())
    if len(text) > limit:
        text = text[: limit - 1] + "…"
    text = html.escape(text, quote=True)
    for character in "\\`|*_[]~":
        text = text.replace(character, f"&#{ord(character)};")
    return text


def first_line(value: Any) -> str:
    return str(value).splitlines()[0] if str(value).splitlines() else ""


def nonnegative_int(value: Any, default: int = 0) -> int:
    return value if type(value) is int and value >= 0 else default


def duration(value: Any) -> str:
    if type(value) in (int, float) and math.isfinite(value) and value >= 0:
        return f"{value:.2f}s"
    return "—"


def render_summary(manifest_path: Path, environment: dict[str, str]) -> str:
    lines = ["## Web screenshot capture", ""]
    outcomes = [
        ("Capture", "CAPTURE_OUTCOME"),
        ("Validation", "VALIDATION_OUTCOME"),
        ("Refresh PR", "PUBLISH_OUTCOME"),
    ]
    lines.append(" · ".join(f"{label}: **{escaped(environment.get(key) or 'not reported')}**" for label, key in outcomes))
    if environment.get("SOURCE_SHA"):
        lines.extend(["", f"Source commit: {escaped(environment['SOURCE_SHA'])}"])
    server = environment.get("GITHUB_SERVER_URL", "").rstrip("/")
    repository = environment.get("GITHUB_REPOSITORY", "")
    run_id = environment.get("GITHUB_RUN_ID", "")
    attempt = environment.get("GITHUB_RUN_ATTEMPT", "")
    if server and repository and run_id:
        # Plain escaped text avoids constructing a Markdown link from arbitrary values.
        url = f"{server}/{repository}/actions/runs/{run_id}"
        if attempt:
            url += f"/attempts/{attempt}"
        lines.extend(["", f"Workflow run: {escaped(url, 500)}"])

    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        if not isinstance(manifest, dict):
            raise ValueError("manifest root must be an object")
        captures = manifest.get("captures", [])
        if not isinstance(captures, list) or any(not isinstance(capture, dict) for capture in captures):
            raise ValueError("manifest captures must be an array of objects")
    except FileNotFoundError:
        lines.extend(["", "No capture manifest was produced. Setup or capture may have failed before evidence was recorded; inspect the failed step logs."])
        return "\n".join(lines) + "\n"
    except (OSError, UnicodeError, ValueError) as error:
        lines.extend(["", f"The capture manifest could not be read: {escaped(first_line(error), 500)}.", "Capture results are unavailable; inspect the capture step logs."])
        return "\n".join(lines) + "\n"

    passed = sum(capture.get("status") == "passed" for capture in captures)
    failed = sum(capture.get("status") == "failed" for capture in captures)
    selected = max(len(captures), nonnegative_int(manifest.get("selectedCaptureCount"), len(captures)))
    remaining = selected - len(captures)
    lines.extend([
        "",
        f"**{passed} passed · {failed} failed · {remaining} not attempted** ({selected} selected).",
        f"Manifest status: **{escaped(manifest.get('status', 'unknown'))}** · Duration: {duration(manifest.get('durationSeconds'))}.",
    ])
    if manifest.get("error"):
        lines.extend(["", f"Run error: {escaped(first_line(manifest['error']), 600)}"])
    if captures:
        lines.extend([
            "",
            "| Route | Result | Attempts | Duration | Error |",
            "| --- | --- | ---: | ---: | --- |",
        ])
        for capture in captures[:300]:
            identity = capture.get("name") or capture.get("id") or "Unnamed capture"
            route = capture.get("route") or "—"
            status = capture.get("status") or "unknown"
            attempts = nonnegative_int(capture.get("attempts")) or "—"
            error = escaped(first_line(capture.get("error", ""))) or "—"
            lines.append(f"| {escaped(identity)} ({escaped(route)}) | {escaped(status)} | {attempts} | {duration(capture.get('durationSeconds'))} | {error} |")
        if len(captures) > 300:
            lines.extend(["", f"Showing 300 of {len(captures)} results. Download the manifest for the complete catalog."])
    lines.extend(["", "Download the screenshot artifact for PNGs and the full capture manifest. Capture and validation outcomes determine whether the catalog can be published."])
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=Path("artifacts/web-screenshots/manifest.json"))
    parser.add_argument("--output", type=Path, default=Path("artifacts/web-screenshots/summary.md"))
    parser.add_argument("--github-step-summary", type=Path)
    args = parser.parse_args()
    summary = render_summary(args.manifest, dict(os.environ))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(summary, encoding="utf-8")
    if args.github_step_summary:
        with args.github_step_summary.open("a", encoding="utf-8") as handle:
            handle.write(summary)
    print(summary, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
