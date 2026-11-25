"""
Tests for build/scripts/audit_provider_integration_actors.py
Run with: python3 -m pytest build/scripts/tests/test_audit_provider_integration_actors.py -v
"""
from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).parent.parent))
import audit_provider_integration_actors as audit


def _write(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(document, indent=2), encoding="utf-8")


@pytest.fixture()
def store(tmp_path: Path) -> Path:
    root = tmp_path / "_integrations"
    # A manifest version approved under the old session label (digest-bound; must stay untouched).
    _write(root / "manifest-versions" / "abc123" / "3.json", {
        "manifestId": "vendor-csv", "manifestVersion": 3, "approvedBy": "Meridian Operator",
        "approvedAt": "2026-11-20T10:00:00Z", "contentDigest": "sha256:deadbeef"})
    # Tenant-partitioned connection with a strategy desk label and a real owner.
    _write(root / "tenants" / "t1" / "connections" / "c1.json", {
        "connectionId": "c1", "ownerUserId": "Momentum Alpha Desk", "createdBy": "dana.ops",
        "updatedAt": "2026-11-21T09:00:00Z"})
    # A sync run with a Settings placeholder nested in a list, and a real requester.
    _write(root / "sync-runs" / "r1.json", {
        "syncRunId": "r1", "requestedBy": "ops.lead",
        "handoffs": [{"requestedBy": "settings-operator", "requestedAt": "2026-11-22T08:00:00Z"}]})
    # A quarantine decision with a blank reviewer and an unrelated "...Desk" non-actor field.
    _write(root / "quarantine" / "q1.json", {"reviewedBy": "  ", "note": "Routed to Trade Desk"})
    (root / "broken.json").write_text("{not json", encoding="utf-8")
    return tmp_path


def _digest(root: Path) -> dict[str, str]:
    return {p.as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.rglob("*")) if p.is_file()}


def test_flags_only_actor_fields_that_do_not_name_a_person(store: Path) -> None:
    findings, skipped = audit.scan(audit.resolve_integrations_root(store))

    flagged = {(f.file, f.json_path, f.value, f.reason) for f in findings}
    assert flagged == {
        ("manifest-versions/abc123/3.json", "approvedBy", "Meridian Operator", "session desk label"),
        ("tenants/t1/connections/c1.json", "ownerUserId", "Momentum Alpha Desk", "strategy desk label"),
        ("sync-runs/r1.json", "handoffs[0].requestedBy", "settings-operator", "settings placeholder"),
        ("quarantine/q1.json", "reviewedBy", "  ", "blank actor"),
    }
    assert [s.file for s in skipped] == ["broken.json"]


def test_reports_when_the_flagged_action_happened(store: Path) -> None:
    findings, _ = audit.scan(audit.resolve_integrations_root(store))
    by_field = {f.json_path: f.timestamp for f in findings}
    assert by_field["approvedBy"] == "2026-11-20T10:00:00Z"
    assert by_field["handoffs[0].requestedBy"] == "2026-11-22T08:00:00Z"
    assert by_field["reviewedBy"] is None


def test_never_modifies_the_store(store: Path) -> None:
    before = _digest(store)
    audit.main(["--data-root", str(store), "--format", "json"])
    assert _digest(store) == before


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("Meridian Operator", "session desk label"),
        ("meridian operator", "session desk label"),
        ("Momentum Alpha Desk", "strategy desk label"),
        ("settings-screen", "settings placeholder"),
        ("", "blank actor"),
        ("dana.ops", None),
        ("desk", None),
        ("helpdesk", None),
        (None, None),
        (42, None),
    ],
)
def test_classification(value: object, expected: str | None) -> None:
    assert audit.classify(value, frozenset()) == expected


def test_extra_labels_are_flagged(store: Path) -> None:
    findings, _ = audit.scan(audit.resolve_integrations_root(store), ["dana.ops"])
    assert ("tenants/t1/connections/c1.json", "createdBy", "configured desk label") in {
        (f.file, f.json_path, f.reason) for f in findings
    }


def test_accepts_the_integrations_directory_itself_and_a_missing_store(tmp_path: Path, store: Path, capsys) -> None:
    assert audit.main(["--data-root", str(store / "_integrations"), "--fail-on-findings"]) == 1
    assert "4 record field(s) do not name a person; 1 file(s) skipped." in capsys.readouterr().out

    assert audit.main(["--data-root", str(tmp_path / "empty"), "--fail-on-findings"]) == 0
    assert "No _integrations directory found" in capsys.readouterr().out


def test_json_output_is_machine_readable(store: Path, capsys) -> None:
    assert audit.main(["--data-root", str(store), "--format", "json"]) == 0
    payload = json.loads(capsys.readouterr().out)
    assert payload["rootFound"] is True
    assert len(payload["findings"]) == 4
    assert payload["skipped"] == [{"file": "broken.json", "reason": "unreadable: JSONDecodeError"}]
