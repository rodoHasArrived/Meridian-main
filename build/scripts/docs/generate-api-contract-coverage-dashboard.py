#!/usr/bin/env python3
"""Generate the API contract coverage dashboard.

Usage:
    python3 generate-api-contract-coverage-dashboard.py \
      --output docs/status/api-contract-coverage-dashboard.md \
      --json-output docs/status/api-contract-coverage-dashboard.json
    python3 generate-api-contract-coverage-dashboard.py --summary
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path
from typing import Optional, Sequence

from dashboard_rendering import (
    current_utc_timestamp,
    load_canonical_json,
    render_markdown_from_json,
    write_canonical_json,
)


EXCLUDE_DIRS = {".git", ".vs", "bin", "obj", "node_modules", "__pycache__", "TestResults"}
HTTP_METHODS = {"GET", "POST", "PUT", "DELETE", "PATCH"}

ROUTE_CONST_RE = re.compile(r'public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"')
MAP_ROUTE_RE = re.compile(
    r"\.Map(?P<method>Get|Post|Put|Delete|Patch)\s*\(\s*(?P<route>[^,\n]+)",
    re.IGNORECASE,
)
PUBLIC_CONTRACT_RE = re.compile(
    r"^\s*public\s+(?:sealed\s+|partial\s+|readonly\s+)*"
    r"(?:record\s+(?:class\s+|struct\s+)?|class\s+|enum\s+)(?P<name>[A-Z]\w*)",
    re.MULTILINE,
)
ROUTE_CONSTRAINT_RE = re.compile(r"\{([^}:]+):[^}]+\}")

DATA_SOURCES = [
    "src/**/*.cs endpoint mappings",
    "src/Meridian.Contracts/Api/UiApiRoutes.cs",
    "src/Meridian.Contracts/Workstation/*.cs",
    "docs/**/*.md",
]


def _should_skip(path: Path) -> bool:
    return any(part in EXCLUDE_DIRS for part in path.parts)


def _iter_files(root_dir: Path, suffix: str) -> list[Path]:
    if not root_dir.is_dir():
        return []

    results: list[Path] = []
    for current, dirs, files in os.walk(root_dir):
        dirs[:] = [name for name in dirs if name not in EXCLUDE_DIRS]
        current_path = Path(current)
        for name in files:
            if name.endswith(suffix):
                results.append(current_path / name)
    return sorted(results)


def _rel(root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def _read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def _normalize_route(path: str) -> str:
    value = path.strip().strip('"').strip("'")
    value = ROUTE_CONSTRAINT_RE.sub(r"{\1}", value)
    if value.startswith("api/"):
        value = "/" + value
    return value.casefold()


def _load_route_constants(root: Path) -> dict[str, str]:
    routes_file = root / "src" / "Meridian.Contracts" / "Api" / "UiApiRoutes.cs"
    text = _read_text(routes_file)
    return {match.group(1): match.group(2) for match in ROUTE_CONST_RE.finditer(text)}


def _resolve_route(expr: str, route_constants: dict[str, str]) -> str | None:
    value = expr.strip()
    if value.startswith('"') and value.endswith('"'):
        return value.strip('"')
    if value.startswith("UiApiRoutes."):
        name = value.split(".", 1)[1].strip()
        return route_constants.get(name)
    return None


def _scan_endpoints(root: Path) -> list[dict[str, object]]:
    route_constants = _load_route_constants(root)
    endpoints: list[dict[str, object]] = []
    seen: set[tuple[str, str]] = set()
    src_dir = root / "src"
    if not src_dir.is_dir():
        return endpoints

    for file_path in _iter_files(src_dir, ".cs"):
        text = _read_text(file_path)
        for match in MAP_ROUTE_RE.finditer(text):
            method = match.group("method").upper()
            route = _resolve_route(match.group("route"), route_constants)
            if not route:
                continue
            if not (route.startswith("/api") or route.startswith("api") or route in {"/health", "/ready", "/live"}):
                continue

            key = (method, _normalize_route(route))
            if key in seen:
                continue
            seen.add(key)
            endpoints.append(
                {
                    "method": method,
                    "path": route if route.startswith("/") else f"/{route}",
                    "source": f"{_rel(root, file_path)}:{text[:match.start()].count(chr(10)) + 1}",
                    "documented": False,
                }
            )

    return endpoints


def _scan_workstation_contracts(root: Path) -> list[dict[str, object]]:
    contracts_dir = root / "src" / "Meridian.Contracts" / "Workstation"
    contracts: list[dict[str, object]] = []
    if not contracts_dir.is_dir():
        return contracts

    for file_path in sorted(contracts_dir.glob("*.cs")):
        text = _read_text(file_path)
        for match in PUBLIC_CONTRACT_RE.finditer(text):
            contracts.append(
                {
                    "name": match.group("name"),
                    "source": f"{_rel(root, file_path)}:{text[:match.start()].count(chr(10)) + 1}",
                    "documented": False,
                }
            )
    return contracts


def _load_docs_text(root: Path) -> str:
    docs_dir = root / "docs"
    if not docs_dir.is_dir():
        return ""

    chunks: list[str] = []
    for path in _iter_files(docs_dir, ".md"):
        chunks.append(_read_text(path))
    normalized = "\n".join(chunks)
    return ROUTE_CONSTRAINT_RE.sub(r"{\1}", normalized).casefold()


def build_dashboard(root: Path) -> dict:
    root = root.resolve()
    docs_text = _load_docs_text(root)
    endpoints = _scan_endpoints(root)
    contracts = _scan_workstation_contracts(root)

    for endpoint in endpoints:
        route = _normalize_route(str(endpoint["path"]))
        endpoint["documented"] = route in docs_text

    for contract in contracts:
        contract["documented"] = str(contract["name"]).casefold() in docs_text

    endpoint_total = len(endpoints)
    endpoint_documented = sum(1 for endpoint in endpoints if endpoint["documented"])
    contract_total = len(contracts)
    contract_documented = sum(1 for contract in contracts if contract["documented"])
    endpoint_percent = round((endpoint_documented / endpoint_total) * 100, 1) if endpoint_total else 100.0
    contract_percent = round((contract_documented / contract_total) * 100, 1) if contract_total else 100.0
    score = round((endpoint_percent * 0.6) + (contract_percent * 0.4), 1)

    return {
        "dashboard": "api-contract-coverage",
        "title": "API Contract Coverage Dashboard",
        "generated_at": current_utc_timestamp(),
        "score_percent": score,
        "endpoint_coverage_percent": endpoint_percent,
        "contract_coverage_percent": contract_percent,
        "summary": {
            "endpoint_count": endpoint_total,
            "documented_endpoint_count": endpoint_documented,
            "undocumented_endpoint_count": endpoint_total - endpoint_documented,
            "workstation_contract_count": contract_total,
            "documented_workstation_contract_count": contract_documented,
            "undocumented_workstation_contract_count": contract_total - contract_documented,
        },
        "endpoints": endpoints,
        "workstation_contracts": contracts,
    }


def _status(value: bool) -> str:
    return "Documented" if value else "Gap"


def _render_body(payload: dict) -> str:
    summary = payload.get("summary", {})
    endpoints = payload.get("endpoints", [])
    contracts = payload.get("workstation_contracts", [])
    endpoint_gaps = [endpoint for endpoint in endpoints if not endpoint.get("documented")]
    contract_gaps = [contract for contract in contracts if not contract.get("documented")]

    lines = [
        "# API Contract Coverage Dashboard",
        "",
        "Tracks whether mapped API routes and workstation DTO contracts are visible in the Markdown documentation set.",
        "",
        "## Summary",
        "",
        "| Metric | Value |",
        "|---|---:|",
        f"| Weighted score | {payload.get('score_percent', 0.0)}% |",
        f"| Endpoint coverage | {payload.get('endpoint_coverage_percent', 0.0)}% |",
        f"| Workstation contract coverage | {payload.get('contract_coverage_percent', 0.0)}% |",
        f"| Endpoints documented | {summary.get('documented_endpoint_count', 0)} / {summary.get('endpoint_count', 0)} |",
        f"| Workstation contracts documented | {summary.get('documented_workstation_contract_count', 0)} / {summary.get('workstation_contract_count', 0)} |",
        "",
        "## Endpoint Coverage",
        "",
        "| Method | Route | Status | Source |",
        "|---|---|---|---|",
    ]

    for endpoint in sorted(endpoints, key=lambda item: (str(item["path"]), str(item["method"]))):
        lines.append(
            f"| `{endpoint['method']}` | `{endpoint['path']}` | "
            f"{_status(bool(endpoint['documented']))} | `{endpoint['source']}` |"
        )

    lines.extend(
        [
            "",
            "## Workstation Contract Coverage",
            "",
            "| Contract | Status | Source |",
            "|---|---|---|",
        ]
    )
    for contract in sorted(contracts, key=lambda item: str(item["name"])):
        lines.append(
            f"| `{contract['name']}` | {_status(bool(contract['documented']))} | "
            f"`{contract['source']}` |"
        )

    lines.extend(["", "## Follow-up Queue", ""])
    if endpoint_gaps:
        lines.append(f"- Document or intentionally suppress {len(endpoint_gaps)} mapped endpoint gap(s).")
    if contract_gaps:
        lines.append(f"- Document or intentionally suppress {len(contract_gaps)} workstation contract gap(s).")
    if not endpoint_gaps and not contract_gaps:
        lines.append("No API contract coverage gaps detected.")

    lines.extend(["", "---", "", "*This dashboard is auto-generated. Do not edit manually.*", ""])
    return "\n".join(lines)


def generate_markdown_from_json_payload(payload: dict) -> str:
    return render_markdown_from_json(
        json_payload=payload,
        render_body=_render_body,
        data_sources=DATA_SOURCES,
    )


def generate_summary(payload: dict) -> str:
    summary = payload.get("summary", {})
    return (
        "api-contract-coverage: "
        f"{payload.get('score_percent', 0.0)}% "
        f"({summary.get('documented_endpoint_count', 0)}/"
        f"{summary.get('endpoint_count', 0)} endpoints, "
        f"{summary.get('documented_workstation_contract_count', 0)}/"
        f"{summary.get('workstation_contract_count', 0)} workstation contracts documented)"
    )


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate the API contract coverage dashboard.")
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
        args.output.write_text(generate_markdown_from_json_payload(canonical), encoding="utf-8")
        print(f"Markdown dashboard written to {args.output}")

    if args.summary or (not args.output and not args.json_output):
        print(generate_summary(payload))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
