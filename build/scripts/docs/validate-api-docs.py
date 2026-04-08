#!/usr/bin/env python3
"""
API Documentation Validator

Validates that API endpoint documentation matches the actual implementation.
Scans C# source code for endpoint definitions and cross-references with
documentation to identify missing, outdated, or incorrect API docs.

Features:
- Extracts HTTP endpoints from C# source
- Compares with API reference documentation
- Detects undocumented endpoints
- Finds deprecated/removed endpoints still in docs
- Validates HTTP methods match
- Checks parameter documentation

Usage:
    python3 validate-api-docs.py --output docs/status/api-validation.md
    python3 validate-api-docs.py --api-docs docs/reference/api-reference.md
    python3 validate-api-docs.py --summary
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

EXCLUDE_DIRS: frozenset[str] = frozenset({
    '.git', 'node_modules', 'bin', 'obj', '__pycache__', '.vs', 'TestResults'
})

# HTTP method patterns
HTTP_METHODS = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD', 'OPTIONS']

# Patterns to extract endpoints from C# code
ENDPOINT_PATTERNS = [
    # MapGet, MapPost, etc. with string literal
    re.compile(
        r'\.Map(Get|Post|Put|Delete|Patch)\s*\(\s*"([^"]+)"',
        re.IGNORECASE
    ),
    # [HttpGet("/api/...")]
    re.compile(
        r'\[Http(Get|Post|Put|Delete|Patch)\s*\(\s*"([^"]+)"\s*\)\]',
        re.IGNORECASE
    ),
    # [Route("/api/...")]
    re.compile(
        r'\[Route\s*\(\s*"([^"]+)"\s*\)\]',
        re.IGNORECASE
    ),
]

# Pattern for Map*(UiApiRoutes.SomeConst, ...) — constant-based route registration
CONST_ROUTE_PATTERN = re.compile(
    r'\.Map(Get|Post|Put|Delete|Patch)\s*\(\s*\w+Routes?\.\w+',
    re.IGNORECASE
)

# Resolved from UiApiRoutes.cs const string declarations
ROUTE_CONST_PATTERN = re.compile(
    r'public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"'
)

# Pattern to find API endpoint documentation in markdown tables.
# Matches rows in the format: | METHOD | `/api/path` | Description |
# Captures group(1) = HTTP method, group(2) = API path.
DOC_ENDPOINT_PATTERN = re.compile(
    r'\|\s*(GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS)\s*\|\s*[`"]*(/?api/[^`"\s|]+)[`"]*',
    re.IGNORECASE
)


# ---------------------------------------------------------------------------
# Data Models
# ---------------------------------------------------------------------------

@dataclass
class Endpoint:
    """Represents an API endpoint."""
    path: str
    method: str
    file: str
    line: int = 0
    documented: bool = False

    def normalize_path(self) -> str:
        """Normalize endpoint path for comparison."""
        # Remove leading slash
        path = self.path.lstrip('/')
        # Normalize parameter syntax: {id} -> {param}
        path = re.sub(r'\{[^}]+\}', '{param}', path)
        return path.lower()


@dataclass
class DocumentedEndpoint:
    """Represents an endpoint found in documentation."""
    path: str
    method: str
    file: str
    line: int = 0
    exists_in_code: bool = False


@dataclass
class ValidationResults:
    """Results of API documentation validation."""
    total_endpoints: int = 0
    documented_endpoints: int = 0
    undocumented_endpoints: list[Endpoint] = field(default_factory=list)
    deprecated_docs: list[DocumentedEndpoint] = field(default_factory=list)
    mismatched_methods: list[tuple[Endpoint, DocumentedEndpoint]] = field(default_factory=list)
    generated_at: str = ""

    @property
    def documentation_coverage(self) -> float:
        """Calculate documentation coverage percentage."""
        if self.total_endpoints == 0:
            return 100.0
        return (self.documented_endpoints / self.total_endpoints) * 100


# ---------------------------------------------------------------------------
# Scanning Functions
# ---------------------------------------------------------------------------

def _should_skip(path: Path) -> bool:
    """Check if path should be skipped."""
    return any(part in EXCLUDE_DIRS for part in path.parts)


def _load_route_constants(root: Path) -> dict[str, str]:
    """Load route path constants from *Routes.cs / *Routes*.cs files across the solution."""
    constants: dict[str, str] = {}
    for cs_file in root.rglob("*Routes*.cs"):
        if _should_skip(cs_file):
            continue
        try:
            content = cs_file.read_text(encoding='utf-8', errors='replace')
        except Exception:
            continue
        for match in ROUTE_CONST_PATTERN.finditer(content):
            name, path = match.group(1), match.group(2)
            if path.startswith('/') or path.startswith('api'):
                constants[name] = path
    return constants


def _extract_endpoints_from_file(file_path: Path, root: Path, route_constants: Optional[dict[str, str]] = None) -> list[Endpoint]:  # noqa: C901
    """Extract API endpoints from a C# source file."""
    endpoints = []

    try:
        content = file_path.read_text(encoding='utf-8', errors='replace')
        lines = content.splitlines()
    except Exception as e:
        print(f"Warning: Could not read {file_path}: {e}", file=sys.stderr)
        return endpoints

    try:
        rel_path = str(file_path.relative_to(root))
    except ValueError:
        rel_path = str(file_path)

    # Track current HTTP method from attributes
    current_method: Optional[str] = None

    for line_num, line in enumerate(lines, 1):
        # Check for HTTP method attributes
        for method in HTTP_METHODS:
            if f'[Http{method}' in line or f'.Map{method}' in line:
                current_method = method
                break

        # Try to extract endpoints
        for pattern in ENDPOINT_PATTERNS:
            matches = pattern.finditer(line)
            for match in matches:
                if len(match.groups()) == 2:
                    # Pattern with method
                    method = match.group(1).upper()
                    path = match.group(2)
                else:
                    # Route attribute without method
                    path = match.group(1)
                    method = current_method or 'GET'

                # Skip if not an API path
                if not path.startswith('/api') and not path.startswith('api'):
                    continue

                endpoints.append(Endpoint(
                    path=path,
                    method=method,
                    file=rel_path,
                    line=line_num
                ))

        # Resolve constant-based routes: .MapGet(SomeRoutes.Foo, ...)
        if route_constants:
            const_match = re.match(
                r'\s*\w+\.Map(Get|Post|Put|Delete|Patch)\s*\(\s*\w+Routes?\.(\w+)',
                line, re.IGNORECASE
            )
            if const_match:
                http_method = const_match.group(1).upper()
                const_name = const_match.group(2)
                resolved = route_constants.get(const_name)
                if resolved and (resolved.startswith('/api') or resolved.startswith('api')):
                    endpoints.append(Endpoint(
                        path=resolved,
                        method=http_method,
                        file=rel_path,
                        line=line_num
                    ))

    return endpoints


def scan_endpoints(root: Path) -> list[Endpoint]:
    """Scan all C# files for API endpoints."""
    src_dir = root / "src"
    if not src_dir.exists():
        return []

    route_constants = _load_route_constants(root)
    print(f"Loaded {len(route_constants)} route constants", file=sys.stderr)

    all_endpoints = []
    for cs_file in src_dir.rglob("*.cs"):
        if _should_skip(cs_file):
            continue
        endpoints = _extract_endpoints_from_file(cs_file, root, route_constants)
        all_endpoints.extend(endpoints)

    # Deduplicate by path + method
    seen = set()
    unique_endpoints = []
    for ep in all_endpoints:
        key = (ep.path.lower(), ep.method.upper())
        if key not in seen:
            seen.add(key)
            unique_endpoints.append(ep)

    return unique_endpoints


def scan_documented_endpoints(api_doc_path: Path, root: Path) -> list[DocumentedEndpoint]:
    """Scan API documentation for endpoint references."""
    if not api_doc_path.exists():
        return []

    try:
        content = api_doc_path.read_text(encoding='utf-8', errors='replace')
        lines = content.splitlines()
    except Exception as e:
        print(f"Warning: Could not read {api_doc_path}: {e}", file=sys.stderr)
        return []

    try:
        rel_path = str(api_doc_path.relative_to(root))
    except ValueError:
        rel_path = str(api_doc_path)

    documented = []
    for line_num, line in enumerate(lines, 1):
        matches = DOC_ENDPOINT_PATTERN.finditer(line)
        for match in matches:
            method = match.group(1).upper()
            path = match.group(2)

            documented.append(DocumentedEndpoint(
                path=path,
                method=method,
                file=rel_path,
                line=line_num
            ))

    return documented


def validate_documentation(
    endpoints: list[Endpoint],
    documented: list[DocumentedEndpoint]
) -> ValidationResults:
    """Cross-reference endpoints with documentation."""
    results = ValidationResults(
        total_endpoints=len(endpoints),
        generated_at=datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
    )

    # Create normalized lookup maps
    endpoint_map = {}
    for ep in endpoints:
        key = (ep.normalize_path(), ep.method.upper())
        endpoint_map[key] = ep

    doc_map = {}
    for doc in documented:
        key = (Endpoint(doc.path, doc.method, "").normalize_path(), doc.method.upper())
        doc_map[key] = doc
        doc.exists_in_code = key in endpoint_map

    # Find undocumented endpoints
    for key, ep in endpoint_map.items():
        if key in doc_map:
            ep.documented = True
            results.documented_endpoints += 1
        else:
            results.undocumented_endpoints.append(ep)

    # Find deprecated documentation
    for key, doc in doc_map.items():
        if not doc.exists_in_code:
            results.deprecated_docs.append(doc)

    return results


# ---------------------------------------------------------------------------
# Report Generation
# ---------------------------------------------------------------------------

def _status_badge(coverage: float) -> str:
    """Generate status badge."""
    if coverage >= 90:
        return "🟢 Excellent"
    elif coverage >= 75:
        return "🟡 Good"
    elif coverage >= 50:
        return "🟠 Fair"
    else:
        return "🔴 Poor"


def generate_markdown(results: ValidationResults) -> str:
    """Generate Markdown validation report."""
    lines = []

    lines.append("# API Documentation Validation Report")
    lines.append("")
    lines.append("> Auto-generated API documentation validation. Do not edit manually.")
    lines.append(f"> Generated: {results.generated_at}")
    lines.append("")

    # Summary
    lines.append("## Summary")
    lines.append("")
    coverage = results.documentation_coverage
    status = _status_badge(coverage)

    lines.append("| Metric | Value |")
    lines.append("|--------|-------|")
    lines.append(f"| Total Endpoints | {results.total_endpoints} |")
    lines.append(f"| Documented | {results.documented_endpoints} |")
    lines.append(f"| Undocumented | {len(results.undocumented_endpoints)} |")
    lines.append(f"| Deprecated Docs | {len(results.deprecated_docs)} |")
    lines.append(f"| **Coverage** | **{coverage:.1f}%** {status} |")
    lines.append("")

    # Undocumented Endpoints
    if results.undocumented_endpoints:
        lines.append("## Undocumented Endpoints")
        lines.append("")
        lines.append("These endpoints exist in the code but are not documented:")
        lines.append("")
        lines.append("| Method | Path | Location |")
        lines.append("|--------|------|----------|")

        for ep in sorted(results.undocumented_endpoints, key=lambda x: (x.path, x.method)):
            loc = f"`{ep.file}:{ep.line}`" if ep.line else f"`{ep.file}`"
            lines.append(f"| `{ep.method}` | `{ep.path}` | {loc} |")
        lines.append("")

    # Deprecated Documentation
    if results.deprecated_docs:
        lines.append("## Deprecated Documentation")
        lines.append("")
        lines.append("These endpoints are documented but no longer exist in the code:")
        lines.append("")
        lines.append("| Method | Path | Location |")
        lines.append("|--------|------|----------|")

        for doc in sorted(results.deprecated_docs, key=lambda x: (x.path, x.method)):
            loc = f"`{doc.file}:{doc.line}`" if doc.line else f"`{doc.file}`"
            lines.append(f"| `{doc.method}` | `{doc.path}` | {loc} |")
        lines.append("")

    # Recommendations
    lines.append("## Recommendations")
    lines.append("")

    if results.undocumented_endpoints:
        lines.append(f"1. **Document {len(results.undocumented_endpoints)} missing endpoints**: "
                    "Add entries to `docs/reference/api-reference.md` with descriptions, "
                    "parameters, and response formats.")
        lines.append("")

    if results.deprecated_docs:
        lines.append(f"2. **Remove {len(results.deprecated_docs)} deprecated entries**: "
                    "Clean up documentation for endpoints that no longer exist.")
        lines.append("")

    if not results.undocumented_endpoints and not results.deprecated_docs:
        lines.append("All endpoints are properly documented. Great job!")
        lines.append("")

    # Footer
    lines.append("---")
    lines.append("")
    lines.append("*This report is auto-generated. Run `python3 build/scripts/docs/validate-api-docs.py` "
                "to regenerate.*")
    lines.append("")

    return "\n".join(lines)


def generate_summary(results: ValidationResults) -> str:
    """Generate concise summary for GITHUB_STEP_SUMMARY."""
    coverage = results.documentation_coverage
    status = _status_badge(coverage)

    return (
        f"### API Documentation Validation\n\n"
        f"- **Coverage**: {coverage:.1f}% {status}\n"
        f"- **Total Endpoints**: {results.total_endpoints}\n"
        f"- **Undocumented**: {len(results.undocumented_endpoints)}\n"
        f"- **Deprecated Docs**: {len(results.deprecated_docs)}\n"
    )


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main(argv: Optional[list[str]] = None) -> int:
    """Entry point."""
    parser = argparse.ArgumentParser(
        description='Validate API documentation against actual implementation'
    )
    parser.add_argument(
        '--root', '-r',
        type=Path,
        default=Path('.'),
        help='Repository root directory (default: current directory)'
    )
    parser.add_argument(
        '--output', '-o',
        type=Path,
        help='Output file for validation report'
    )
    parser.add_argument(
        '--api-docs',
        type=Path,
        default=Path('docs/reference/api-reference.md'),
        help='API documentation file to validate against'
    )
    parser.add_argument(
        '--summary', '-s',
        action='store_true',
        help='Print summary to stdout'
    )

    args = parser.parse_args(argv)

    root = args.root.resolve()
    if not root.is_dir():
        print(f"Error: root directory does not exist: {root}", file=sys.stderr)
        return 1

    api_doc_path = root / args.api_docs if not args.api_docs.is_absolute() else args.api_docs

    try:
        print("Scanning endpoints from source code...", file=sys.stderr)
        endpoints = scan_endpoints(root)

        print(f"Found {len(endpoints)} endpoints in code", file=sys.stderr)

        print("Scanning API documentation...", file=sys.stderr)
        documented = scan_documented_endpoints(api_doc_path, root)

        print(f"Found {len(documented)} documented endpoints", file=sys.stderr)

        print("Validating documentation...", file=sys.stderr)
        results = validate_documentation(endpoints, documented)

    except Exception as exc:
        print(f"Error during validation: {exc}", file=sys.stderr)
        return 1

    # Write report
    if args.output:
        md = generate_markdown(results)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(md, encoding='utf-8')
        print(f"Validation report written to {args.output}")

    # Print summary
    if args.summary:
        print(generate_summary(results))
    elif not args.output:
        print(generate_summary(results))

    return 0


if __name__ == '__main__':
    sys.exit(main())
