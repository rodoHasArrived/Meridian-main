#!/usr/bin/env python3
"""Validate global warning suppression inventory metadata in Directory.Build.props."""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REQUIRED_FIELDS = ("Owner", "Justification", "RatchetPlan")


def _read_text(element: ET.Element, tag: str) -> str:
    child = element.find(tag)
    return (child.text or "").strip() if child is not None else ""


def _validate_inventory(
    root: ET.Element, item_name: str, *, allow_empty: bool = False
) -> tuple[list[str], list[tuple[str, str, str, str]]]:
    failures: list[str] = []
    rows: list[tuple[str, str, str, str]] = []

    for item_group in root.findall("ItemGroup"):
        for item in item_group.findall(item_name):
            code = (item.attrib.get("Include") or "").strip()
            if not code:
                failures.append(f"{item_name} entry is missing Include value.")
                continue

            fields = {name: _read_text(item, name) for name in REQUIRED_FIELDS}
            missing = [name for name, value in fields.items() if not value]
            if missing:
                failures.append(f"{item_name} '{code}' is missing metadata: {', '.join(missing)}.")
                continue

            rows.append((code, fields["Owner"], fields["Justification"], fields["RatchetPlan"]))

    if not rows and not allow_empty:
        failures.append(f"No {item_name} entries found.")

    return failures, rows


def main() -> int:
    repo_root = Path(__file__).resolve().parents[3]
    props_path = repo_root / "Directory.Build.props"

    root = ET.fromstring(props_path.read_text(encoding="utf-8"))

    failures: list[str] = []
    warning_failures, warning_rows = _validate_inventory(root, "MeridianGlobalNoWarn")
    #零 advisory suppressions is the state the security policy wants, not a gate failure: an
    # accepted NuGet risk should be removed the moment its advisory stops applying. Requiring at
    # least one entry meant retiring the last acceptance failed this check.
    advisory_failures, advisory_rows = _validate_inventory(
        root, "MeridianNuGetAuditSuppression", allow_empty=True
    )
    failures.extend(warning_failures)
    failures.extend(advisory_failures)

    no_warn_values = [node.text or "" for node in root.findall(".//PropertyGroup/NoWarn")]
    if not any("MeridianGlobalNoWarn" in value for value in no_warn_values):
        failures.append("NoWarn must include the MeridianGlobalNoWarn item list to keep inventory authoritative.")

    audit_values = [node.attrib.get("Include", "") for node in root.findall(".//ItemGroup/NuGetAuditSuppress")]
    if advisory_rows and not any("@(MeridianNuGetAuditSuppression)" in value for value in audit_values):
        failures.append("NuGetAuditSuppress must include @(MeridianNuGetAuditSuppression).")
    declared_advisories = root.findall("ItemGroup/MeridianNuGetAuditSuppression")
    if not declared_advisories and audit_values:
        failures.append(
            "NuGetAuditSuppress is wired with no MeridianNuGetAuditSuppression entries behind it; "
            "remove the NuGetAuditSuppress item so suppressions can only arrive through the inventory."
        )

    if failures:
        print("Warning suppression inventory validation failed:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1

    print("Warning suppression inventory:")
    if not advisory_rows:
        print("- no NuGet advisory suppressions (nothing is being suppressed)")
    for code, owner, _justification, _plan in warning_rows:
        print(f"- {code} (owner: {owner})")
    for advisory, owner, _justification, _plan in advisory_rows:
        print(f"- advisory {advisory} (owner: {owner})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
