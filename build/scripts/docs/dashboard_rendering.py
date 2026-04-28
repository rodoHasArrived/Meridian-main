"""Shared helpers for dashboard generators.

JSON is the canonical source of truth. Dashboards should generate JSON first,
then render Markdown from that JSON payload to avoid drift between formats.
"""

from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Sequence


def current_utc_timestamp() -> str:
    """Return an ISO-8601 UTC timestamp string."""
    return datetime.now(timezone.utc).isoformat()


def write_canonical_json(payload: dict[str, Any], json_output: Path) -> dict[str, Any]:
    """Write canonical JSON payload and return the same payload."""
    json_output.parent.mkdir(parents=True, exist_ok=True)
    json_output.write_text(json.dumps(payload, indent=2, default=str) + "\n", encoding="utf-8")
    return payload


def load_canonical_json(json_output: Path) -> dict[str, Any]:
    """Load canonical JSON payload from disk."""
    return json.loads(json_output.read_text(encoding="utf-8"))


def render_markdown_from_json(
    *,
    json_payload: dict[str, Any],
    render_body: Callable[[dict[str, Any]], str],
    data_sources: list[str],
) -> str:
    """Render deterministic markdown using canonical JSON payload.

    ``render_body`` should accept the parsed JSON payload and return markdown body
    content for the specific dashboard.
    """
    lines = [
        "> Auto-generated from canonical JSON payload.",
        f"> Generated: {json_payload.get('generated_at', current_utc_timestamp())}",
        f"> Data sources: {', '.join(data_sources)}",
        "",
    ]
    lines.append(render_body(json_payload).rstrip())
    lines.append("")
    return "\n".join(lines)


def _has_glob_pattern(pattern: str) -> bool:
    return any(char in pattern for char in "*?[")


def _repo_relative(root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def _resolve_pattern(root: Path, pattern: str) -> list[Path]:
    if _has_glob_pattern(pattern):
        return sorted(path for path in root.glob(pattern) if path.is_file())

    path = root / pattern
    return [path] if path.is_file() else []


def _read_combined_text(paths: Sequence[Path]) -> str:
    chunks: list[str] = []
    for path in paths:
        try:
            chunks.append(path.read_text(encoding="utf-8", errors="replace"))
        except OSError:
            continue
    return "\n".join(chunks)


def _term_status(text: str, terms: Sequence[str], mode: str) -> tuple[list[str], list[str]]:
    if not terms:
        return [], []

    haystack = text.casefold()
    found = [term for term in terms if term.casefold() in haystack]
    if mode == "any":
        return found, [] if found else list(terms)

    missing = [term for term in terms if term.casefold() not in haystack]
    return found, missing


def build_text_signal_dashboard(
    *,
    root: Path,
    dashboard: str,
    title: str,
    description: str,
    checks: Sequence[dict[str, Any]],
) -> dict[str, Any]:
    """Build a weighted dashboard from repository text evidence checks."""
    root = root.resolve()
    evaluated: list[dict[str, Any]] = []
    total_weight = 0
    passed_weight = 0

    for check in checks:
        weight = int(check.get("weight", 1))
        total_weight += weight
        patterns = [str(pattern) for pattern in check.get("paths", [])]
        path_mode = str(check.get("path_mode", "all"))
        term_mode = str(check.get("term_mode", "all"))

        matched_paths: list[Path] = []
        missing_patterns: list[str] = []
        for pattern in patterns:
            matches = _resolve_pattern(root, pattern)
            if matches:
                matched_paths.extend(matches)
            else:
                missing_patterns.append(pattern)

        if path_mode == "any":
            paths_ok = bool(matched_paths)
        else:
            paths_ok = not missing_patterns

        combined_text = _read_combined_text(matched_paths)
        found_terms, missing_terms = _term_status(
            combined_text,
            [str(term) for term in check.get("terms", [])],
            term_mode,
        )

        status = "pass" if paths_ok and not missing_terms else "gap"
        score = weight if status == "pass" else 0
        passed_weight += score

        evaluated.append(
            {
                "id": str(check.get("id", "")),
                "category": str(check.get("category", "Evidence")),
                "label": str(check.get("label", "")),
                "status": status,
                "weight": weight,
                "score": score,
                "path_mode": path_mode,
                "term_mode": term_mode,
                "patterns": patterns,
                "matched_paths": [_repo_relative(root, path) for path in matched_paths],
                "missing_patterns": missing_patterns,
                "terms": [str(term) for term in check.get("terms", [])],
                "found_terms": found_terms,
                "missing_terms": missing_terms,
                "detail": str(check.get("detail", "")),
                "remediation": str(check.get("remediation", "")),
            }
        )

    score_percent = round((passed_weight / total_weight) * 100, 1) if total_weight else 0.0
    passed_checks = sum(1 for check in evaluated if check["status"] == "pass")

    return {
        "dashboard": dashboard,
        "title": title,
        "description": description,
        "generated_at": current_utc_timestamp(),
        "root": root.name,
        "score_percent": score_percent,
        "passed_weight": passed_weight,
        "total_weight": total_weight,
        "summary": {
            "check_count": len(evaluated),
            "passed_checks": passed_checks,
            "gap_checks": len(evaluated) - passed_checks,
            "missing_source_count": sum(
                len(check["missing_patterns"]) for check in evaluated
            ),
            "missing_term_count": sum(len(check["missing_terms"]) for check in evaluated),
        },
        "checks": evaluated,
    }


def _format_paths(paths: Sequence[str], *, limit: int = 3) -> str:
    if not paths:
        return "-"

    head = [f"`{path}`" for path in paths[:limit]]
    if len(paths) > limit:
        head.append(f"+{len(paths) - limit} more")
    return ", ".join(head)


def _format_missing(check: dict[str, Any]) -> str:
    parts: list[str] = []
    if check.get("missing_patterns"):
        parts.append("sources: " + ", ".join(f"`{item}`" for item in check["missing_patterns"]))
    if check.get("missing_terms"):
        parts.append("terms: " + ", ".join(f"`{item}`" for item in check["missing_terms"]))
    return "; ".join(parts) if parts else "-"


def render_text_signal_dashboard_body(payload: dict[str, Any]) -> str:
    """Render a generic weighted evidence dashboard body."""
    summary = payload.get("summary", {})
    lines = [
        f"# {payload.get('title', 'Evidence Dashboard')}",
        "",
        str(payload.get("description", "")),
        "",
        "## Summary",
        "",
        "| Metric | Value |",
        "|---|---:|",
        f"| Score | {payload.get('score_percent', 0.0)}% |",
        f"| Passed checks | {summary.get('passed_checks', 0)} |",
        f"| Gap checks | {summary.get('gap_checks', 0)} |",
        f"| Missing evidence sources | {summary.get('missing_source_count', 0)} |",
        f"| Missing expected terms | {summary.get('missing_term_count', 0)} |",
        "",
        "## Evidence Checks",
        "",
        "| Category | Check | Status | Score | Evidence | Missing |",
        "|---|---|---|---:|---|---|",
    ]

    for check in payload.get("checks", []):
        status = "Pass" if check.get("status") == "pass" else "Gap"
        score = f"{check.get('score', 0)}/{check.get('weight', 0)}"
        evidence = _format_paths(check.get("matched_paths", []))
        missing = _format_missing(check)
        lines.append(
            f"| {check.get('category', '')} | {check.get('label', '')} | "
            f"{status} | {score} | {evidence} | {missing} |"
        )

    gaps = [check for check in payload.get("checks", []) if check.get("status") != "pass"]
    if gaps:
        lines.extend(["", "## Follow-up Queue", ""])
        for check in gaps:
            remediation = check.get("remediation") or "Refresh the source evidence and rerun this dashboard."
            lines.append(f"- **{check.get('label', 'Check')}**: {remediation}")
    else:
        lines.extend(["", "## Follow-up Queue", "", "No gaps detected by this dashboard."])

    lines.extend(["", "---", "", "*This dashboard is auto-generated. Do not edit manually.*", ""])
    return "\n".join(lines)


def render_text_signal_dashboard_markdown(
    payload: dict[str, Any],
    *,
    data_sources: list[str],
) -> str:
    return render_markdown_from_json(
        json_payload=payload,
        render_body=render_text_signal_dashboard_body,
        data_sources=data_sources,
    )


def text_signal_dashboard_summary(payload: dict[str, Any]) -> str:
    summary = payload.get("summary", {})
    return (
        f"{payload.get('dashboard', 'dashboard')}: "
        f"{payload.get('score_percent', 0.0)}% "
        f"({summary.get('passed_checks', 0)}/"
        f"{summary.get('check_count', 0)} checks passed)"
    )
