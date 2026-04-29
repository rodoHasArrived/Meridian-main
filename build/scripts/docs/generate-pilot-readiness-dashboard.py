#!/usr/bin/env python3
"""Generate the DK1 pilot readiness dashboard.

Usage:
    python3 generate-pilot-readiness-dashboard.py \
      --output docs/status/pilot-readiness-dashboard.md \
      --json-output docs/status/pilot-readiness-dashboard.json
    python3 generate-pilot-readiness-dashboard.py --summary
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Optional, Sequence

from dashboard_rendering import (
    build_text_signal_dashboard,
    load_canonical_json,
    render_markdown_from_json,
    render_text_signal_dashboard_body,
    text_signal_dashboard_summary,
    write_canonical_json,
)


DATA_SOURCES = [
    "docs/status/provider-validation-matrix.md",
    "docs/status/dk1-pilot-parity-runbook.md",
    "docs/status/kernel-readiness-dashboard.md",
    "artifacts/pilot-acceptance/latest/pilot-readiness.json",
    "scripts/dev/*dk1*",
    "tests/scripts/test_*dk1*",
]

PILOT_ACCEPTANCE_ARTIFACT = "artifacts/pilot-acceptance/latest/pilot-readiness.json"

CHECKS = [
    {
        "id": "pilot-acceptance-artifact",
        "category": "Golden Path Evidence",
        "label": "Pilot acceptance artifact proves all eight golden-path stage gates",
        "paths": [PILOT_ACCEPTANCE_ARTIFACT],
        "terms": [
            '"allStagesReady": true',
            '"readyStageCount": 8',
            '"stageGates"',
            '"evidenceGraph"',
            '"GovernedReportPack"',
        ],
        "weight": 4,
        "remediation": (
            "Run PilotAcceptanceHarnessTests to regenerate the pilot readiness artifact "
            "before claiming golden-path readiness."
        ),
    },
    {
        "id": "provider-matrix",
        "category": "Provider Evidence",
        "label": "Pilot provider matrix covers Alpaca, Robinhood, Yahoo, and Wave 1 status",
        "paths": ["docs/status/provider-validation-matrix.md"],
        "terms": ["Alpaca", "Robinhood", "Yahoo", "Wave 1"],
        "weight": 2,
        "remediation": "Refresh the provider validation matrix before claiming DK1 pilot readiness.",
    },
    {
        "id": "parity-runbook",
        "category": "Provider Evidence",
        "label": "DK1 parity runbook names generated packet and run-date artifact requirements",
        "paths": ["docs/status/dk1-pilot-parity-runbook.md"],
        "terms": [
            "dk1-pilot-parity-packet",
            "operator sign-off",
            "artifacts/provider-validation/_automation",
        ],
        "weight": 2,
        "remediation": "Document the packet and sign-off binding workflow in the DK1 runbook.",
    },
    {
        "id": "kernel-signoff",
        "category": "Operator Sign-off",
        "label": "Kernel dashboard records signed packet-bound DK1 operator sign-off",
        "paths": ["docs/status/kernel-readiness-dashboard.md"],
        "terms": [
            "operatorSignoff.status=signed",
            "operatorSignoff.validForDk1Exit=true",
            "ready-for-operator-review",
        ],
        "weight": 3,
        "remediation": "Update the kernel dashboard with the current signed, packet-bound DK1 evidence.",
    },
    {
        "id": "automation-scripts",
        "category": "Automation",
        "label": "Provider validation, packet generation, and sign-off scripts are present",
        "paths": [
            "scripts/dev/run-wave1-provider-validation.ps1",
            "scripts/dev/generate-dk1-pilot-parity-packet.ps1",
            "scripts/dev/prepare-dk1-operator-signoff.ps1",
        ],
        "weight": 2,
        "remediation": "Restore the DK1 provider-validation automation scripts or update the runbook.",
    },
    {
        "id": "packet-tests",
        "category": "Automation",
        "label": "DK1 packet and sign-off scripts have focused regression tests",
        "paths": [
            "tests/scripts/test_generate_dk1_pilot_parity_packet.py",
            "tests/scripts/test_prepare_dk1_operator_signoff.py",
        ],
        "weight": 2,
        "remediation": "Add focused Python tests for the DK1 packet and sign-off generators.",
    },
    {
        "id": "readiness-handoff",
        "category": "Trading Readiness",
        "label": "Pilot posture is consumed by the shared trading readiness lane",
        "paths": [
            "docs/plans/paper-trading-cockpit-reliability-sprint.md",
            "src/Meridian.Ui.Shared/Services/Dk1TrustGateReadinessService.cs",
        ],
        "terms": [
            "/api/workstation/trading/readiness",
            "ProviderTrustGate",
            "operator sign-off",
        ],
        "weight": 2,
        "remediation": "Keep the DK1 trust-gate handoff wired into the shared trading readiness contract.",
    },
]


def build_dashboard(root: Path) -> dict:
    payload = build_text_signal_dashboard(
        root=root,
        dashboard="pilot-readiness",
        title="Pilot Readiness Dashboard",
        description=(
            "Tracks whether DK1 pilot evidence, packet-bound operator sign-off, "
            "the trading readiness handoff, and local golden-path acceptance "
            "artifact remain present and synchronized."
        ),
        checks=CHECKS,
    )
    payload["pilot_acceptance_artifact"] = load_pilot_acceptance_artifact(root)
    return payload


def load_pilot_acceptance_artifact(root: Path) -> dict[str, Any]:
    root = root.resolve()
    artifact_path = root / PILOT_ACCEPTANCE_ARTIFACT
    relative_path = artifact_path.relative_to(root).as_posix()
    if not artifact_path.is_file():
        return {
            "status": "not_generated",
            "path": relative_path,
            "detail": (
                "Run PilotAcceptanceHarnessTests to generate the golden-path "
                "pilot readiness artifact."
            ),
            "stage_gates": [],
            "evidence_edge_count": 0,
        }

    try:
        artifact = json.loads(artifact_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        return {
            "status": "unreadable",
            "path": relative_path,
            "detail": f"Could not read pilot readiness artifact: {exc}",
            "stage_gates": [],
            "evidence_edge_count": 0,
        }

    return load_pilot_acceptance_artifact_from_payload(artifact, relative_path)


def load_pilot_acceptance_artifact_from_payload(
    artifact: dict[str, Any],
    relative_path: str,
) -> dict[str, Any]:
    stage_gates = [
        {
            "stage": str(gate.get("stage", "")),
            "label": str(gate.get("label", "")),
            "status": str(gate.get("status", "")),
            "evidence_ids": [str(item) for item in gate.get("evidenceIds", [])],
            "blockers": [str(item) for item in gate.get("blockers", [])],
            "validation": str(gate.get("validation", "")),
        }
        for gate in artifact.get("stageGates", [])
        if isinstance(gate, dict)
    ]

    return {
        "status": "loaded",
        "path": relative_path,
        "generated_at_utc": artifact.get("generatedAtUtc"),
        "all_stages_ready": bool(artifact.get("allStagesReady", False)),
        "ready_stage_count": int(artifact.get("readyStageCount", 0)),
        "total_stage_count": int(artifact.get("totalStageCount", len(stage_gates))),
        "stage_gates": stage_gates,
        "evidence_edge_count": len(artifact.get("evidenceGraph", [])),
        "key_evidence": {
            "provider_evidence_id": artifact.get("providerEvidenceId"),
            "dataset_evidence_id": artifact.get("datasetEvidenceId"),
            "research_run_id": artifact.get("researchRunId"),
            "paper_session_id": artifact.get("paperSessionId"),
            "replay_verification_audit_id": artifact.get("replayVerificationAuditId"),
            "portfolio_evidence_id": artifact.get("portfolioEvidenceId"),
            "ledger_evidence_id": artifact.get("ledgerEvidenceId"),
            "reconciliation_run_id": artifact.get("reconciliationRunId"),
            "report_pack_id": artifact.get("reportPackId"),
        },
    }


def _format_evidence_ids(evidence_ids: Sequence[str], *, limit: int = 3) -> str:
    if not evidence_ids:
        return "-"

    head = [f"`{item}`" for item in evidence_ids[:limit]]
    if len(evidence_ids) > limit:
        head.append(f"+{len(evidence_ids) - limit} more")
    return ", ".join(head)


def render_pilot_acceptance_artifact_section(payload: dict[str, Any]) -> str:
    artifact = payload.get("pilot_acceptance_artifact", {})
    lines = ["## Pilot Acceptance Artifact", ""]
    if artifact.get("status") != "loaded":
        lines.extend(
            [
                "| Field | Value |",
                "| --- | --- |",
                f"| Status | {artifact.get('status', 'not_generated')} |",
                f"| Path | `{artifact.get('path', PILOT_ACCEPTANCE_ARTIFACT)}` |",
                f"| Detail | {artifact.get('detail', 'No pilot artifact loaded.')} |",
                "",
            ]
        )
        return "\n".join(lines)

    key_evidence = artifact.get("key_evidence", {})
    lines.extend(
        [
            "| Field | Value |",
            "| --- | --- |",
            f"| Status | {artifact.get('status')} |",
            f"| Path | `{artifact.get('path')}` |",
            f"| Generated | {artifact.get('generated_at_utc', '-')} |",
            f"| Stages ready | {artifact.get('ready_stage_count', 0)}/{artifact.get('total_stage_count', 0)} |",
            f"| All stages ready | {artifact.get('all_stages_ready', False)} |",
            f"| Evidence graph edges | {artifact.get('evidence_edge_count', 0)} |",
            f"| Dataset evidence | `{key_evidence.get('dataset_evidence_id', '-')}` |",
            f"| Paper session | `{key_evidence.get('paper_session_id', '-')}` |",
            f"| Portfolio evidence | `{key_evidence.get('portfolio_evidence_id', '-')}` |",
            f"| Ledger evidence | `{key_evidence.get('ledger_evidence_id', '-')}` |",
            f"| Report pack | `{key_evidence.get('report_pack_id', '-')}` |",
            "",
            "### Stage Gates",
            "",
            "| Stage | Status | Evidence | Validation |",
            "| --- | --- | --- | --- |",
        ]
    )
    for gate in artifact.get("stage_gates", []):
        lines.append(
            f"| {gate.get('label', gate.get('stage', ''))} | {gate.get('status', '')} | "
            f"{_format_evidence_ids(gate.get('evidence_ids', []))} | {gate.get('validation', '')} |"
        )

    blockers = [
        blocker
        for gate in artifact.get("stage_gates", [])
        for blocker in gate.get("blockers", [])
    ]
    lines.extend(["", "### Artifact Follow-up", ""])
    if blockers:
        for blocker in blockers:
            lines.append(f"- {blocker}")
    else:
        lines.append("No stage blockers were recorded in the latest pilot artifact.")

    lines.append("")
    return "\n".join(lines)


def render_pilot_readiness_dashboard_markdown(payload: dict[str, Any]) -> str:
    body = render_text_signal_dashboard_body(payload)
    footer = "\n---\n\n_This dashboard is auto-generated. Do not edit manually._\n"
    artifact_section = "\n" + render_pilot_acceptance_artifact_section(payload)
    if footer in body:
        body = body.replace(footer, artifact_section + footer)
    else:
        body = body.rstrip() + artifact_section

    return render_markdown_from_json(
        json_payload=payload,
        render_body=lambda _: body,
        data_sources=DATA_SOURCES,
    )


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate the DK1 pilot readiness dashboard.")
    parser.add_argument("--output", "-o", type=Path, help="Markdown output path.")
    parser.add_argument("--json-output", "-j", type=Path, help="JSON output path.")
    parser.add_argument("--root", "-r", type=Path, default=Path("."), help="Repository root.")
    parser.add_argument("--summary", "-s", action="store_true", help="Print a CLI summary.")
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = _build_parser().parse_args(argv)
    root = args.root.resolve()
    if not root.is_dir():
        print(f"Error: root directory does not exist: {root}", file=sys.stderr)
        return 1
    if args.output and not args.json_output:
        print(
            "Error: --output requires --json-output so markdown is rendered from canonical JSON.",
            file=sys.stderr,
        )
        return 1

    payload = build_dashboard(root)

    if args.json_output:
        write_canonical_json(payload, args.json_output)
        print(f"JSON dashboard written to {args.json_output}")

    if args.output:
        canonical = load_canonical_json(args.json_output)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            render_pilot_readiness_dashboard_markdown(canonical),
            encoding="utf-8",
        )
        print(f"Markdown dashboard written to {args.output}")

    if args.summary or (not args.output and not args.json_output):
        print(text_signal_dashboard_summary(payload))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
