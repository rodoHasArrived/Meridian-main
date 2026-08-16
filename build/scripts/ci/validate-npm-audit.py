#!/usr/bin/env python3
"""Fail-closed npm audit gate with an explicit, reviewed accepted-advisory register.

`npm audit --audit-level=high` exits non-zero while any high/critical advisory exists,
including advisories the security register has formally risk-accepted (for example an
advisory whose vulnerable code path is unreachable in the shipped artifact and which has
no compatible upstream fix). Left raw, one accepted advisory keeps the production
certification dependency gate permanently red, which hides every new advisory behind an
expected failure.

This gate keeps the lane fail-closed while making acceptance explicit and auditable:

- every advisory at or above the failure level must match an acceptance entry that names
  the advisory (GHSA id), package, severity ceiling, owner, rationale, and review-by date;
- acceptance entries expire: past `review_by`, the advisory fails the gate again until it
  is re-reviewed;
- acceptance entries that no longer match any reported advisory fail the gate so the
  register cannot rot after upstream fixes land;
- a missing, unreadable, or error-shaped audit report fails the gate (an absent scan is
  not a clean scan).

Acceptance entries must point back to the central registry
(`docs/security/known-vulnerabilities.md`); this script enforces the machine-readable
mirror stored in `build/config/security/npm-audit-accepted-advisories.json`.
"""

from __future__ import annotations

import argparse
import datetime as _dt
import json
import re
import sys
from pathlib import Path

SEVERITY_RANK = {"info": 0, "low": 1, "moderate": 2, "high": 3, "critical": 4}
GHSA_PATTERN = re.compile(r"GHSA(?:-[23456789cfghjmpqrvwx]{4}){3}", re.IGNORECASE)
REQUIRED_ACCEPTANCE_FIELDS = (
    "id",
    "ghsa",
    "package",
    "max_severity",
    "reason",
    "owner",
    "accepted_on",
    "review_by",
)


class GateFailure(Exception):
    """Raised for gate-level failures that should exit 1 with a clear message."""


def canonical_ghsa(value: str) -> str:
    """Return the canonical GHSA presentation: uppercase prefix, lowercase suffix."""
    return "GHSA-" + value[len("GHSA-") :].lower()


def parse_arguments(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--audit-json", required=True, type=Path, help="Path to `npm audit --json` output.")
    parser.add_argument(
        "--accepted",
        required=True,
        type=Path,
        help="Path to the accepted-advisory register (JSON).",
    )
    parser.add_argument(
        "--fail-level",
        default="high",
        choices=("low", "moderate", "high", "critical"),
        help="Lowest severity that fails the gate when not accepted (default: high).",
    )
    parser.add_argument("--output", type=Path, help="Optional path for the machine-readable gate decision.")
    parser.add_argument(
        "--today",
        help="Override the evaluation date (YYYY-MM-DD, UTC). Intended for tests.",
    )
    return parser.parse_args(argv)


def load_json(path: Path, description: str) -> dict:
    if not path.is_file():
        raise GateFailure(f"{description} not found at {path}; failing closed.")
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise GateFailure(f"{description} at {path} is unreadable or invalid JSON ({error}); failing closed.")
    if not isinstance(payload, dict):
        raise GateFailure(f"{description} at {path} must be a JSON object; failing closed.")
    return payload


def parse_date(value: object, field: str, entry_id: str) -> _dt.date:
    if not isinstance(value, str):
        raise GateFailure(f"Acceptance entry '{entry_id}' field '{field}' must be an ISO date string.")
    try:
        return _dt.date.fromisoformat(value)
    except ValueError:
        raise GateFailure(f"Acceptance entry '{entry_id}' field '{field}' is not a valid ISO date: {value!r}.")


def load_acceptances(path: Path) -> list[dict]:
    register = load_json(path, "Accepted-advisory register")
    entries = register.get("accepted")
    if not isinstance(entries, list):
        raise GateFailure(f"Accepted-advisory register at {path} must contain an 'accepted' array.")
    validated: list[dict] = []
    seen_ghsa: set[str] = set()
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            raise GateFailure(f"Accepted-advisory entry #{index} must be an object.")
        entry_id = str(entry.get("id") or f"#{index}")
        for field in REQUIRED_ACCEPTANCE_FIELDS:
            value = entry.get(field)
            if not isinstance(value, str) or not value.strip():
                raise GateFailure(f"Acceptance entry '{entry_id}' is missing required field '{field}'.")
        if not GHSA_PATTERN.fullmatch(entry["ghsa"]):
            raise GateFailure(f"Acceptance entry '{entry_id}' has an invalid GHSA id: {entry['ghsa']!r}.")
        ghsa = canonical_ghsa(entry["ghsa"])
        if ghsa in seen_ghsa:
            raise GateFailure(f"Acceptance entry '{entry_id}' duplicates GHSA id {ghsa}.")
        seen_ghsa.add(ghsa)
        if entry["max_severity"] not in SEVERITY_RANK:
            raise GateFailure(
                f"Acceptance entry '{entry_id}' has unknown max_severity {entry['max_severity']!r}."
            )
        parse_date(entry["accepted_on"], "accepted_on", entry_id)
        parse_date(entry["review_by"], "review_by", entry_id)
        validated.append({**entry, "ghsa": ghsa})
    return validated


def extract_ghsa(advisory: dict) -> str | None:
    for candidate in (advisory.get("url"), advisory.get("github_advisory_id")):
        if isinstance(candidate, str):
            match = GHSA_PATTERN.search(candidate)
            if match:
                return canonical_ghsa(match.group(0))
    return None


def resolve_advisories(package: str, vulnerabilities: dict, visited: set[str]) -> list[dict]:
    """Resolve a package's advisory dicts, following chained via-strings to root causes."""
    if package in visited:
        return []
    visited.add(package)
    entry = vulnerabilities.get(package)
    if not isinstance(entry, dict):
        return []
    advisories: list[dict] = []
    for via in entry.get("via", []):
        if isinstance(via, dict):
            advisories.append(via)
        elif isinstance(via, str):
            advisories.extend(resolve_advisories(via, vulnerabilities, visited))
    return advisories


def evaluate(
    audit: dict,
    acceptances: list[dict],
    fail_level: str,
    today: _dt.date,
) -> dict:
    if "error" in audit:
        raise GateFailure(f"npm audit reported an error payload: {audit['error']!r}; failing closed.")
    if audit.get("auditReportVersion") != 2:
        raise GateFailure(
            "Unsupported npm audit report version "
            f"{audit.get('auditReportVersion')!r}; expected 2. Failing closed."
        )
    vulnerabilities = audit.get("vulnerabilities")
    if not isinstance(vulnerabilities, dict):
        raise GateFailure("npm audit output has no 'vulnerabilities' object; failing closed.")

    threshold = SEVERITY_RANK[fail_level]
    flagged: dict[tuple[str, str], dict] = {}
    for package, entry in vulnerabilities.items():
        if not isinstance(entry, dict):
            raise GateFailure(f"npm audit entry for {package!r} is malformed; failing closed.")
        severity = str(entry.get("severity", "")).lower()
        if SEVERITY_RANK.get(severity, SEVERITY_RANK["critical"]) < threshold:
            continue
        advisories = resolve_advisories(package, vulnerabilities, set())
        if not advisories:
            raise GateFailure(
                f"Package {package!r} is flagged {severity} but no advisory could be resolved; failing closed."
            )
        for advisory in advisories:
            ghsa = extract_ghsa(advisory)
            advisory_package = str(advisory.get("name") or package)
            advisory_severity = str(advisory.get("severity") or severity).lower()
            if SEVERITY_RANK.get(advisory_severity, SEVERITY_RANK["critical"]) < threshold:
                # The flagged package inherits its severity from a higher-severity chain
                # member; the lower-severity chained advisory is not itself gated.
                continue
            key = (ghsa or f"source:{advisory.get('source')}", advisory_package)
            flagged.setdefault(
                key,
                {
                    "ghsa": ghsa,
                    "package": advisory_package,
                    "severity": advisory_severity,
                    "title": advisory.get("title"),
                    "url": advisory.get("url"),
                    "flagged_through": sorted({package}),
                },
            )
            if package not in flagged[key]["flagged_through"]:
                flagged[key]["flagged_through"] = sorted({*flagged[key]["flagged_through"], package})

    unaccepted: list[dict] = []
    accepted_in_use: list[dict] = []
    matched_entry_ids: set[str] = set()
    for advisory in flagged.values():
        match = next(
            (
                entry
                for entry in acceptances
                if advisory["ghsa"] is not None
                and entry["ghsa"] == advisory["ghsa"]
                and entry["package"] == advisory["package"]
            ),
            None,
        )
        if match is None:
            unaccepted.append({**advisory, "failure": "no acceptance entry"})
            continue
        if SEVERITY_RANK[advisory["severity"]] > SEVERITY_RANK[match["max_severity"]]:
            unaccepted.append(
                {**advisory, "failure": f"severity exceeds accepted ceiling {match['max_severity']}"}
            )
            continue
        if today > parse_date(match["review_by"], "review_by", match["id"]):
            unaccepted.append(
                {**advisory, "failure": f"acceptance {match['id']} expired on {match['review_by']}"}
            )
            continue
        matched_entry_ids.add(match["id"])
        accepted_in_use.append({**advisory, "acceptance_id": match["id"], "review_by": match["review_by"]})

    stale_entries = [entry["id"] for entry in acceptances if entry["id"] not in matched_entry_ids]
    return {
        "fail_level": fail_level,
        "evaluated_on": today.isoformat(),
        "metadata": audit.get("metadata", {}).get("vulnerabilities", {}),
        "unaccepted": sorted(unaccepted, key=lambda item: (item["package"], item["ghsa"] or "")),
        "accepted_in_use": sorted(accepted_in_use, key=lambda item: (item["package"], item["ghsa"] or "")),
        "stale_acceptances": sorted(stale_entries),
        "passed": not unaccepted and not stale_entries,
    }


def main(argv: list[str] | None = None) -> int:
    arguments = parse_arguments(argv)
    try:
        today = (
            _dt.date.fromisoformat(arguments.today)
            if arguments.today
            else _dt.datetime.now(_dt.timezone.utc).date()
        )
        audit = load_json(arguments.audit_json, "npm audit output")
        acceptances = load_acceptances(arguments.accepted)
        decision = evaluate(audit, acceptances, arguments.fail_level, today)
    except GateFailure as failure:
        print(f"npm-audit gate: FAIL — {failure}", file=sys.stderr)
        if arguments.output:
            arguments.output.parent.mkdir(parents=True, exist_ok=True)
            arguments.output.write_text(
                json.dumps({"passed": False, "gate_error": str(failure)}, indent=2) + "\n",
                encoding="utf-8",
            )
        return 1

    if arguments.output:
        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(json.dumps(decision, indent=2) + "\n", encoding="utf-8")

    for advisory in decision["accepted_in_use"]:
        print(
            "npm-audit gate: accepted "
            f"{advisory['ghsa']} ({advisory['package']}, {advisory['severity']}) "
            f"under {advisory['acceptance_id']} — review by {advisory['review_by']}."
        )
    for advisory in decision["unaccepted"]:
        print(
            "npm-audit gate: UNACCEPTED "
            f"{advisory['ghsa'] or 'unknown-advisory'} ({advisory['package']}, {advisory['severity']}): "
            f"{advisory['failure']}",
            file=sys.stderr,
        )
    for entry_id in decision["stale_acceptances"]:
        print(
            f"npm-audit gate: STALE acceptance {entry_id} no longer matches any reported advisory; "
            "remove it from the register.",
            file=sys.stderr,
        )

    if decision["passed"]:
        print("npm-audit gate: PASS — every gated advisory is explicitly accepted and unexpired.")
        return 0
    print("npm-audit gate: FAIL — see unaccepted or stale entries above.", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
