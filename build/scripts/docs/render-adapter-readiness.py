#!/usr/bin/env python3
"""Render the canonical adapter registry; --check rejects missing or stale output."""

from __future__ import annotations

import sys
from pathlib import Path

import yaml

from common import build_arg_parser, generated_header, repo_root, write_text_if_changed
from adapter_readiness import load_registry, validate_registry

REGISTRY = Path("docs/source/data/adapter-readiness.yml")
OUTPUT = Path("docs/source/generated/adapter-readiness-matrix.md")
CAPABILITIES = (
    ("streaming", "Streaming"),
    ("historical", "Historical"),
    ("symbol_search", "Symbol search"),
    ("options", "Options"),
    ("corporate_actions", "Corporate actions"),
    ("brokerage", "Brokerage"),
)


def cell(value: str) -> str:
    return str(value).replace("|", "\\|").replace("\n", " ")


def reference(item: dict) -> str:
    path = item["path"]
    return f"[{cell(item['symbol'])}](../../../{path}) (`{path}`)"


def render_matrix(data: dict) -> str:
    """Pure deterministic projection; rows are ordered by direct adapter folder."""
    schema = data["schema"]
    lines = [
        generated_header(
            "build/scripts/docs/render-adapter-readiness.py",
            [f"{schema['id']}@{schema['version']}"],
            [REGISTRY.as_posix()],
        ).rstrip(),
        "",
        "# Adapter Readiness Matrix",
        "",
        "**Status:** generated",
        "",
        "**Owner:** Data Confidence and Validation",
        "",
        "Source: [adapter readiness registry](../data/adapter-readiness.yml). "
        "Edit the registry and regenerate this view; the "
        "[documentation ownership contract](../../documentation-ownership.md) governs its location.",
        "",
        "Readiness describes the checked-in implementation and cited evidence for each folder's "
        "bounded scope. It does not grant runtime entitlements, certify vendor availability, or "
        "replace [provider validation and operator sign-off](../../reference/provider-validation-matrix.md). "
        "Evidence links identify tests; their presence does not claim a passing live run.",
        "",
        "- **complete:** the stated bounded implementation is covered by targeted repository evidence.",
        "- **partial:** implemented surfaces remain bounded by missing coverage, integration work, or external acceptance.",
        "- **experimental:** exploratory or unofficial integration, or mapper-only assets without a runtime adapter.",
        "- **template-only:** copyable scaffolding without production registration.",
        "",
        "The six capability columns mean implementations of the shared streaming, historical, "
        "symbol-search, options, on-demand corporate-action, and brokerage contracts in "
        "`ProviderCapabilityDescriptorCatalog`. A **No** does not exclude a different integration "
        "surface: resolver and compatibility data-source contracts are listed separately below. "
        "Canonical IDs and aliases come from `ProviderIdentity`; an ID alone grants no capability. "
        "Core and Failover have no independent provider ID.",
        "",
        "| Folder | Canonical ID | Readiness | Streaming | Historical | Symbol search | Options | Corporate actions | Brokerage |",
        "| --- | --- | --- | --- | --- | --- | --- | --- | --- |",
    ]
    adapters = sorted(data["adapters"], key=lambda row: row["folder"])
    for row in adapters:
        values = [
            f"[{row['folder']}](#{row['folder'].lower()})",
            f"`{row['provider_id']}`" if row["provider_id"] else "n/a",
            row["state"],
            *("Yes" if row["capabilities"][key] else "No" for key, _ in CAPABILITIES),
        ]
        lines.append("| " + " | ".join(values) + " |")
    for row in adapters:
        lines.extend([
            "",
            f"## {row['folder']}",
            "",
            f"**Owner:** {row['owner']}",
            "",
            f"**Next action:** {row['next_action']}",
            "",
            "**Aliases:** " + (", ".join(f"`{alias}`" for alias in row["aliases"]) or "None."),
            "",
            f"**Credentials:** {row['requirements']['credentials']}",
            "",
            f"**Optional SDK:** {row['requirements']['optional_sdk']}",
            "",
            "**Risks and external dependencies:**",
            "",
            *(f"- {risk}" for risk in row["risks"]),
            "",
            f"**Degradation / fail-closed behavior:** {row['degradation']}",
            "",
            "**Implementation types:**",
            "",
        ])
        implementations = [
            f"- {label}: `{row['adapter_types'][key]}`"
            for key, label in CAPABILITIES if row["adapter_types"][key]
        ]
        for key, label in (("symbol_resolver", "Symbol resolver"), ("compatibility_data_source", "Compatibility data source")):
            if row["other_types"][key]:
                implementations.append(f"- {label}: `{row['other_types'][key]}` (separate contract)")
        lines.extend(implementations or ["- No shared-contract provider implementation; see the scoped evidence below."])
        lines.extend(["", "**Registration path:**", ""])
        lines.extend(f"- {reference(item)}" for item in row["registration"])
        lines.extend(["", "**Targeted evidence:**", ""])
        lines.extend(f"- {item['kind'].capitalize()}: {reference(item)}" for item in row["evidence"])
    lines.extend([
        "", "## Regeneration and validation", "", "```bash",
        "python build/scripts/docs/render-adapter-readiness.py",
        "python build/scripts/docs/validate-adapter-readiness.py --summary",
        "python build/scripts/docs/render-adapter-readiness.py --check",
        "python -m unittest tests/scripts/test_adapter_readiness.py",
        "```", "",
    ])
    return "\n".join(lines)


def main() -> int:
    parser = build_arg_parser(__doc__)
    parser.add_argument("--check", action="store_true", help="Check committed output without writing it.")
    args = parser.parse_args()
    root = repo_root(args.root)
    try:
        data = load_registry(root)
        errors = validate_registry(root, data)
        if errors:
            for error in errors:
                print(f"ERROR: {error}", file=sys.stderr)
            return 1
        expected = render_matrix(data)
        output = root / OUTPUT
        if args.check:
            if not output.is_file() or output.read_text(encoding="utf-8") != expected:
                print(f"ERROR: {OUTPUT.as_posix()} is missing or stale; run {Path(__file__).name}", file=sys.stderr)
                return 1
            print("Adapter readiness matrix is current.")
        else:
            changed = write_text_if_changed(output, expected)
            print(f"Adapter readiness matrix: {len(data['adapters'])} families, {int(changed)} file(s) changed.")
        return 0
    except (OSError, ValueError, KeyError, TypeError, yaml.YAMLError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
