#!/usr/bin/env python3
"""
Read-only audit of provider-integration actor fields recorded before the server bound them to the
signed-in operator (PR rodoHasArrived/Meridian-main#3754).

Until that change the provider-integration routes stored whatever actor the client sent, and the
browser sent the workstation session's desk label ("Meridian Operator", "<strategy> Desk") or a
Settings placeholder ("settings-operator", "settings-screen"). Those records do not name a person.

This script lists them. It never modifies anything:

* the records live in the file-backed store under ``<data root>/_integrations`` (not PostgreSQL);
* nothing else recorded who actually made the request, so there is no correct value to backfill;
* ``approvedBy`` is part of each immutable manifest version's ``contentDigest``, so rewriting it
  would break the references that readiness reviews and connections are pinned to.

Usage:
    python3 build/scripts/audit_provider_integration_actors.py --data-root <path> [--format json]
        [--extra-label "Ops Desk"] [--fail-on-findings]

``--data-root`` may be the storage root (containing ``_integrations``) or ``_integrations`` itself.
Exit code: 0 normally; 1 with ``--fail-on-findings`` when anything is flagged; 2 on bad arguments.
"""
from __future__ import annotations

import argparse
import json
import sys
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Iterable, Iterator

# Actor fields the provider-integration contracts persist (ProviderIntegrationContracts.cs).
ACTOR_FIELDS = frozenset(
    {"approvedBy", "checkedBy", "createdBy", "importedBy", "ownerUserId", "requestedBy", "reviewedBy", "savedBy"}
)

# The server's session displayName fallback and the browser's Settings placeholders.
KNOWN_LABELS = {
    "meridian operator": "session desk label",
    "settings-operator": "settings placeholder",
    "settings-screen": "settings placeholder",
}

# Sibling fields that say when the flagged action happened, reported for context.
TIMESTAMP_FIELDS = (
    "approvedAt", "savedAt", "importedAt", "requestedAt", "checkedAt", "reviewedAt",
    "createdAt", "updatedAt", "startedAt", "completedAt",
)


@dataclass(frozen=True)
class Finding:
    file: str
    json_path: str
    field: str
    value: str
    reason: str
    timestamp: str | None


@dataclass(frozen=True)
class Skipped:
    file: str
    reason: str


def classify(value: Any, extra_labels: frozenset[str]) -> str | None:
    """Why an actor value does not name a person, or None when it looks like a real principal."""
    if not isinstance(value, str):
        return None
    text = value.strip()
    if not text:
        return "blank actor"
    lowered = text.casefold()
    if lowered in KNOWN_LABELS:
        return KNOWN_LABELS[lowered]
    if lowered in extra_labels:
        return "configured desk label"
    # BuildDisplayName: "<strategy name> Desk". Usernames carry no spaces, so a spaced "... Desk" is a label.
    if " " in text and lowered.endswith(" desk"):
        return "strategy desk label"
    return None


def resolve_integrations_root(data_root: Path) -> Path:
    if data_root.name == "_integrations":
        return data_root
    return data_root / "_integrations"


def _walk(node: Any, path: str) -> Iterator[tuple[str, dict[str, Any], str, Any]]:
    if isinstance(node, dict):
        for key, value in node.items():
            child = f"{path}.{key}" if path else key
            yield child, node, key, value
            yield from _walk(value, child)
    elif isinstance(node, list):
        for index, item in enumerate(node):
            yield from _walk(item, f"{path}[{index}]")


def _timestamp(owner: dict[str, Any]) -> str | None:
    for name in TIMESTAMP_FIELDS:
        value = owner.get(name)
        if isinstance(value, str) and value.strip():
            return value
    return None


def scan(root: Path, extra_labels: Iterable[str] = ()) -> tuple[list[Finding], list[Skipped]]:
    labels = frozenset(label.strip().casefold() for label in extra_labels if label.strip())
    findings: list[Finding] = []
    skipped: list[Skipped] = []
    if not root.is_dir():
        return findings, skipped

    for file in sorted(root.rglob("*.json")):
        relative = file.relative_to(root).as_posix()
        try:
            with file.open("r", encoding="utf-8") as handle:  # read-only
                document = json.load(handle)
        except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
            skipped.append(Skipped(relative, f"unreadable: {error.__class__.__name__}"))
            continue

        for json_path, owner, key, value in _walk(document, ""):
            if key not in ACTOR_FIELDS:
                continue
            reason = classify(value, labels)
            if reason is not None:
                findings.append(Finding(relative, json_path, key, value, reason, _timestamp(owner)))
    return findings, skipped


def _render_text(root: Path, findings: list[Finding], skipped: list[Skipped]) -> str:
    lines = [f"Provider-integration actor audit (read-only): {root}"]
    if not root.is_dir():
        lines.append("No _integrations directory found; nothing to audit.")
        return "\n".join(lines)
    lines.append(f"{len(findings)} record field(s) do not name a person; {len(skipped)} file(s) skipped.")
    for finding in findings:
        when = f" at {finding.timestamp}" if finding.timestamp else ""
        lines.append(f"- {finding.file} :: {finding.json_path} = {finding.value!r} ({finding.reason}){when}")
    for item in skipped:
        lines.append(f"! skipped {item.file}: {item.reason}")
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n", 1)[0])
    parser.add_argument("--data-root", required=True, type=Path,
                        help="Storage root containing _integrations, or the _integrations directory itself.")
    parser.add_argument("--format", choices=("text", "json"), default="text")
    parser.add_argument("--extra-label", action="append", default=[],
                        help="Another desk label to flag (repeatable), e.g. a custom displayName.")
    parser.add_argument("--fail-on-findings", action="store_true",
                        help="Exit 1 when any field is flagged.")
    args = parser.parse_args(argv)

    root = resolve_integrations_root(args.data_root)
    findings, skipped = scan(root, args.extra_label)
    if args.format == "json":
        print(json.dumps({
            "root": str(root),
            "rootFound": root.is_dir(),
            "findings": [asdict(finding) for finding in findings],
            "skipped": [asdict(item) for item in skipped],
        }, indent=2))
    else:
        print(_render_text(root, findings, skipped))
    return 1 if args.fail_on_findings and findings else 0


if __name__ == "__main__":
    sys.exit(main())
