#!/usr/bin/env python3
"""Validate AI handoff checklist discoverability and structure."""

from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import List


H = Path("docs/ai/agent-handoff-checklist.md")
HANDOFF_REQUIRED_SECTIONS = ("required handoff packet",)
HANDOFF_REQUIRED_FIELDS = (
    "scope",
    "inputs loaded",
    "changes made",
    "validation",
    "open risks",
    "next lane",
    "required context",
    "optional context",
)
REQUIRED_LINK_TARGETS = [
    Path("CLAUDE.md"),
    Path(".codex/AGENTS.md"),
    Path(".github/copilot-instructions.md"),
    Path("docs/ai/README.md"),
    Path("docs/ai/assistant-workflow-contract.md"),
    Path("docs/ai/codex/README.md"),
    Path("docs/ai/copilot/instructions.md"),
    Path("docs/ai/skills/README.md"),
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Check that key AI guidance files reference the handoff checklist.")
    parser.add_argument("--output", help="Optional markdown output path for the check report.")
    parser.add_argument("--json-output", help="Optional machine-readable JSON output path for the check report.")
    parser.add_argument(
        "--strict",
        action="store_true",
        help="Require the handoff packet template fields in `docs/ai/agent-handoff-checklist.md`.",
    )
    return parser.parse_args()


def markdown_link_for(path: Path) -> str:
    return f"`{path.as_posix()}`"


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def parse_hand_off_fields(checklist_text: str) -> dict[str, str]:
    """Extract labeled handoff packet fields from the checklist body."""
    fields: dict[str, str] = {}
    pattern = re.compile(r"^\s*[-*]\s*`([^`]+)`\s*:\s*(.*)$")
    for line in checklist_text.splitlines():
        match = pattern.match(line)
        if not match:
            continue
        label, value = match.groups()
        fields[label.strip().lower()] = value.strip()
    return fields


def run(
    root: Path,
    output_path: Path | None = None,
    json_path: Path | None = None,
    *,
    strict: bool = False,
) -> int:
    start = datetime.now(timezone.utc).isoformat()
    failures: List[str] = []
    details: List[dict[str, str]] = []
    checklist_path = root / H
    if not checklist_path.exists():
        msg = f"Missing required checklist file: {markdown_link_for(H)}"
        failures.append(msg)
        details.append({"target": markdown_link_for(H), "status": "missing", "message": msg})
    else:
        checklist_text = read_text(checklist_path)
        checklist_text_l = checklist_text.lower()
        if not all(section in checklist_text_l for section in HANDOFF_REQUIRED_SECTIONS):
            msg = "Checklist file missing required section: `Required Handoff Packet`."
            failures.append(msg)
            details.append({"target": markdown_link_for(H), "status": "missing-section", "message": msg})
        else:
            details.append({"target": markdown_link_for(H), "status": "ok", "message": "Checklist and required section present."})

        if strict:
            parsed_fields = parse_hand_off_fields(checklist_text)
            missing_fields = [field for field in HANDOFF_REQUIRED_FIELDS if field not in parsed_fields]
            empty_fields = [field for field in parsed_fields if field in HANDOFF_REQUIRED_FIELDS and not parsed_fields[field]]
            if missing_fields:
                msg = (
                    "Checklist missing required handoff packet fields: "
                    + ", ".join(f"`{field}`" for field in missing_fields)
                )
                failures.append(msg)
                details.append(
                    {
                        "target": markdown_link_for(H),
                        "status": "missing-field",
                        "message": msg,
                    }
                )
            elif empty_fields:
                msg = "Checklist has empty required handoff packet field(s): " + ", ".join(
                    f"`{field}`" for field in empty_fields
                )
                failures.append(msg)
                details.append(
                    {
                        "target": markdown_link_for(H),
                        "status": "empty-field",
                        "message": msg,
                    }
                )
            else:
                details.append({"target": markdown_link_for(H), "status": "ok", "message": "All required handoff fields are present."})

    for path in REQUIRED_LINK_TARGETS:
        full_path = root / path
        if not full_path.exists():
            msg = f"{markdown_link_for(path)} does not exist."
            failures.append(f"{markdown_link_for(path)} does not reference the shared handoff checklist.")
            details.append({"target": markdown_link_for(path), "status": "missing-file", "message": msg})
            continue

        text = read_text(full_path).lower()
        needle = "agent-handoff-checklist.md"
        if needle not in text:
            msg = f"{markdown_link_for(path)} does not reference the shared handoff checklist."
            failures.append(msg)
            details.append({"target": markdown_link_for(path), "status": "missing-reference", "message": msg})
        else:
            details.append({"target": markdown_link_for(path), "status": "ok", "message": "Checklist reference present."})

    status = "pass" if not failures else "fail"
    if status == "pass":
        failures_text = ["No failures detected."]
    else:
        failures_text = failures

    output_payload = {
        "status": status,
        "generatedAtUtc": start,
        "checksTotal": len(details),
        "checksPassed": sum(1 for item in details if item["status"] == "ok"),
        "checksFailed": sum(1 for item in details if item["status"] != "ok"),
        "failures": failures_text,
        "checks": details,
    }

    output = [f"# AI Handoff Checklist Compliance: {status}", ""]

    if failures:
        output.append("## Failures")
        output.append("")
        for failure in failures:
            output.append(f"- {failure}")
    else:
        output.append("## Result")
        output.append("")
        output.append("- All required host guidance files reference the shared handoff checklist.")

    report = "\n".join(output) + "\n"
    if json_path is not None:
        json_path.parent.mkdir(parents=True, exist_ok=True)
        json_path.write_text(json.dumps(output_payload, indent=2) + "\n", encoding="utf-8")

    if output_path is not None:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(report, encoding="utf-8")
    else:
        print(report, end="")

    return 1 if failures else 0


def main() -> int:
    args = parse_args()
    root = Path(__file__).resolve().parents[3]
    output = Path(args.output) if args.output else None
    json_output = Path(args.json_output) if args.json_output else None
    return run(root, output, json_output, strict=args.strict)


if __name__ == "__main__":
    raise SystemExit(main())
