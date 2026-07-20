#!/usr/bin/env python3
"""Validate the structural and evidence contract of a simulated user panel result."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any

SKILL_NAME = "meridian-simulated-user-panel"
HEADINGS = [
    "Executive Summary",
    "Panel",
    "Persona Findings",
    "Cross-Persona Tensions",
    "Owner Actions",
    "Release Recommendation",
    "Confidence Notes",
]
PERSONA_FIELDS = [
    "Liked",
    "Didn't like",
    "Missing or risky",
    "Owner-minded improvement ideas",
    "Adoption verdict",
    "Rubric (1-5 with evidence)",
]
RUBRIC_DIMENSIONS = [
    "Workflow Fit",
    "Trust / Controls",
    "Time-to-Value",
    "Data Confidence",
    "Extensibility",
    "Learning Curve",
]
RECOMMENDATIONS = {
    "design_partner": {"steer", "prototype", "defer"},
    "release_gate": {"ship", "ship_with_caveats", "hold"},
    "usability_lab": {"advance_to_release_gate", "rerun_after_changes", "defer"},
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--text")
    source.add_argument("--file", type=Path)
    parser.add_argument("--mode", choices=sorted(RECOMMENDATIONS))
    parser.add_argument("--summary", action="store_true")
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def section_bounds(text: str) -> tuple[dict[str, str], list[str]]:
    matches = list(re.finditer(r"^##\s+(.+?)\s*$", text, flags=re.MULTILINE))
    sections: dict[str, str] = {}
    order: list[str] = []
    for index, match in enumerate(matches):
        heading = match.group(1).strip()
        start = match.end()
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        sections[heading] = text[start:end].strip()
        order.append(heading)
    return sections, order


def nonempty_label(section: str, label: str) -> bool:
    match = re.search(rf"^-\s*{re.escape(label)}:\s*(.+)$", section, flags=re.IGNORECASE | re.MULTILINE)
    return bool(match and match.group(1).strip())


def parse_personas(persona_section: str) -> list[tuple[str, str]]:
    matches = list(re.finditer(r"^###\s+(.+?)\s*$", persona_section, flags=re.MULTILINE))
    personas: list[tuple[str, str]] = []
    for index, match in enumerate(matches):
        start = match.end()
        end = matches[index + 1].start() if index + 1 < len(matches) else len(persona_section)
        personas.append((match.group(1).strip(), persona_section[start:end].strip()))
    return personas


def parse_recommendation(section: str) -> str | None:
    match = re.search(r"^-\s*Recommendation:\s*`?([a-z_]+)`?\s*$", section, flags=re.IGNORECASE | re.MULTILINE)
    if match:
        return match.group(1).lower()
    for options in RECOMMENDATIONS.values():
        for option in options:
            if re.search(rf"\b{re.escape(option)}\b", section, flags=re.IGNORECASE):
                return option
    return None


def parse_mode(summary: str, explicit_mode: str | None) -> str | None:
    if explicit_mode:
        return explicit_mode
    match = re.search(r"^-\s*Mode:\s*`?([a-z_]+)`?\s*$", summary, flags=re.IGNORECASE | re.MULTILINE)
    return match.group(1).lower() if match else None


def parse_evidence_status(summary: str) -> str | None:
    match = re.search(r"^-\s*Evidence status:\s*`?([a-z_]+)`?\s*$", summary, flags=re.IGNORECASE | re.MULTILINE)
    return match.group(1).lower() if match else None


def validate(text: str, explicit_mode: str | None = None) -> dict[str, Any]:
    failures: list[str] = []
    has_receipt = f"Skill: `{SKILL_NAME}`" in text or f"Skill: {SKILL_NAME}" in text
    if not has_receipt:
        failures.append(f"missing skill selection receipt for {SKILL_NAME}")

    sections, actual_order = section_bounds(text)
    missing_headings = [heading for heading in HEADINGS if heading not in sections]
    failures.extend(f"missing heading ## {heading}" for heading in missing_headings)
    present_required = [heading for heading in actual_order if heading in HEADINGS]
    if present_required != [heading for heading in HEADINGS if heading in sections]:
        failures.append("review-contract headings are out of order")

    summary = sections.get("Executive Summary", "")
    mode = parse_mode(summary, explicit_mode)
    if mode not in RECOMMENDATIONS:
        failures.append("Executive Summary must declare a valid Mode")
    evidence_status = parse_evidence_status(summary)
    if evidence_status not in {"sufficient", "partial", "insufficient"}:
        failures.append("Executive Summary must declare Evidence status")
    disclaimer_text = f"{summary}\n{sections.get('Confidence Notes', '')}".lower()
    if "simulated persona feedback" not in disclaimer_text or "not observed user research" not in disclaimer_text:
        failures.append("missing simulation disclaimer")

    panel_lines = re.findall(r"^-\s+(.+)$", sections.get("Panel", ""), flags=re.MULTILINE)
    panel_roles = []
    for line in panel_lines:
        role = re.split(r"\s+[—–-]\s+|\s+\((?:canonical|advisory|custom)\)", line, maxsplit=1, flags=re.IGNORECASE)[0].strip()
        if role:
            panel_roles.append(role)
    if len(set(panel_roles)) < 4:
        failures.append("Panel must name at least four distinct roles")

    personas = parse_personas(sections.get("Persona Findings", ""))
    if len({name for name, _ in personas}) < 4:
        failures.append("Persona Findings must contain at least four distinct persona sections")
    for persona, body in personas:
        for field in PERSONA_FIELDS:
            if field == "Rubric (1-5 with evidence)":
                if not re.search(r"^-\s*Rubric \(1-5 with evidence\):\s*$", body, flags=re.IGNORECASE | re.MULTILINE):
                    failures.append(f"{persona}: missing {field}")
            elif not nonempty_label(body, field):
                failures.append(f"{persona}: missing or empty {field}")
        for dimension in RUBRIC_DIMENSIONS:
            match = re.search(
                rf"^-\s*{re.escape(dimension)}:\s*([1-5])/5\s*[-—–:]\s*(.+)$",
                body,
                flags=re.IGNORECASE | re.MULTILINE,
            )
            if not match or len(match.group(2).strip()) < 5:
                failures.append(f"{persona}: invalid or unsupported rubric score for {dimension}")

    owner = sections.get("Owner Actions", "")
    bucket_positions = []
    for bucket in ("Now", "Next", "Later"):
        match = re.search(rf"^-\s*{bucket}:\s*(.*)$", owner, flags=re.IGNORECASE | re.MULTILINE)
        if not match:
            failures.append(f"Owner Actions missing {bucket} bucket")
        else:
            bucket_positions.append(match.start())
    if len(bucket_positions) == 3 and bucket_positions != sorted(bucket_positions):
        failures.append("Owner Action buckets are out of order")

    recommendation = parse_recommendation(sections.get("Release Recommendation", ""))
    if mode in RECOMMENDATIONS and recommendation not in RECOMMENDATIONS[mode]:
        failures.append(f"recommendation {recommendation!r} is invalid for mode {mode}")
    if mode == "release_gate":
        if evidence_status == "insufficient" and recommendation != "hold":
            failures.append("release_gate with insufficient evidence must recommend hold")
        if recommendation == "ship" and evidence_status != "sufficient":
            failures.append("ship requires sufficient evidence")

    confidence = sections.get("Confidence Notes", "")
    for label in ("Verified", "Inferred", "Missing evidence"):
        if not nonempty_label(confidence, label):
            failures.append(f"Confidence Notes missing or empty {label}")

    return {
        "status": "pass" if not failures else "fail",
        "failures": failures,
        "summary": {
            "skill": SKILL_NAME,
            "mode": mode,
            "evidence_status": evidence_status,
            "recommendation": recommendation,
            "heading_count": len(HEADINGS) - len(missing_headings),
            "panel_role_count": len(set(panel_roles)),
            "persona_count": len({name for name, _ in personas}),
            "failure_count": len(failures),
            "receipt_count": 1 if has_receipt else 0,
        },
    }


def main() -> int:
    args = parse_args()
    text = args.file.read_text(encoding="utf-8") if args.file else args.text or ""
    payload = validate(text, args.mode)
    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print(
            f"simulated_user_output_check status={payload['status']} "
            f"personas={payload['summary']['persona_count']} failures={payload['summary']['failure_count']}"
        )
        if args.summary:
            for failure in payload["failures"]:
                print(f"- {failure}")
    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
