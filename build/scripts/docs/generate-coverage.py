#!/usr/bin/env python3
"""
Documentation Coverage Report Generator

Analyzes the Meridian codebase to determine how well
source-level constructs (public types, API endpoints, configuration
keys, provider implementations, and ADR references) are reflected in
the project documentation.

Produces a Markdown report with per-category coverage tables,
a list of undocumented items, and actionable recommendations.

Usage:
    python3 generate-coverage.py
    python3 generate-coverage.py --root /path/to/repo --output report.md
    python3 generate-coverage.py --summary  # print summary for GITHUB_STEP_SUMMARY
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Set, Tuple


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

EXCLUDE_DIRS: Set[str] = {
    ".git",
    "node_modules",
    "bin",
    "obj",
    "__pycache__",
    ".pytest_cache",
    ".vs",
}
STABLE_GENERATED_AT = "1970-01-01 00:00:00 UTC"

CS_FILE_EXTENSIONS: Tuple[str, ...] = (".cs",)

DOC_FILE_EXTENSIONS: Tuple[str, ...] = (".md",)
# The corpus is an allowlist of roots that exist to describe contracts, not a denylist of prose.
#
# Subtracting prose was tried first and does not converge: four review rounds on #2703 each found a
# document where root-, file-, or heading-level filtering guessed wrong, because these roots
# interleave description and argument inside single documents. A delivery plan can state a DTO's
# complete field set in order to argue that the DTO is missing something.
#
# docs/generated/database/** is included because it is the PostgreSQL data-object catalog
# (docs/generated/README.md), and pages such as
# docs/generated/database/contracts/ledger-contracts-page-01.md carry real field-level reference
# documentation. An earlier revision excluded that subtree: 41 files left the corpus and 763
# genuinely documented types were marked as gaps. It is reference material and belongs here.
#
# The two self-referential reports below stay excluded regardless. Neither is reachable under the
# current allowlist, but the guard is kept so widening the allowlist cannot silently reintroduce
# them: repository-structure.md lists every path in the repository, so a type would count as
# documented purely because its own source file exists, and documentation-coverage.md is this
# generator's own output, so a type would count by being reported as undocumented.
DOC_CONTENT_INCLUDE_PREFIXES: Tuple[str, ...] = (
    "docs/reference/",
    "docs/generated/database/",
    # docfx.json writes generated API reference here. It is in the allowlist so this report's own
    # primary remediation -- "generate API docs with DocFX" for the public-type gap -- can actually
    # move the number it is printed next to. Leaving it out made the advice inert.
    "docs/docfx/api/",
)

DOC_CONTENT_EXCLUDE_PREFIXES: Tuple[str, ...] = (
    "docs/status/",
    "docs/generated/documentation-coverage.md",
    "docs/generated/repository-structure.md",
)

# Regex: public (static )?(sealed )?(partial )?(class|interface|record|enum) Name
PUBLIC_TYPE_RE = re.compile(
    r"^\s*(?:\[.*?\]\s*)*"                          # optional attributes
    r"public\s+"
    r"(?:static\s+)?"
    r"(?:sealed\s+)?"
    r"(?:partial\s+)?"
    r"(?:abstract\s+)?"
    r"(class|interface|record|enum)\s+"
    r"([A-Z]\w*)",                                   # type name
    re.MULTILINE,
)

# Route-style endpoint patterns
ROUTE_ATTRIBUTE_RE = re.compile(
    r'\[\s*(?:Http(?:Get|Post|Put|Delete|Patch)|Route)\s*\(\s*"([^"]+)"\s*\)',
)
# The receiver is captured because minimal-API routes are written relative to a `MapGroup`, and
# the group has to be composed back on before the route can be compared with a document. Empty
# route strings are allowed — `group.MapGet("", …)` maps the group's own path.
# Anchored on a literal `.` so the engine can skip between candidates. Capturing the receiver in
# the pattern instead — `(\w*)\s*\.\s*…` — removes that anchor, because `\w*` matches empty at
# every position; that alone took this generator from 1.2s to 7s. The receiver is recovered by
# walking backwards from the match, which is bounded by the identifier's own length.
MAP_ENDPOINT_RE = re.compile(
    r'\.(?:MapGet|MapPost|MapPut|MapDelete|MapPatch)\s*\(\s*"([^"]*)"',
)
# `var group = app.MapGroup("/api/banking")`, or `.MapGroup(UiApiRoutes.HistoricalData)`. Groups
# nest, so the receiver is captured too and prefixes are composed transitively.
MAP_GROUP_RE = re.compile(
    r'var\s+(\w+)\s*=\s*(\w+)\s*\.\s*MapGroup\s*\(\s*(?:"([^"]*)"|([\w.]+))\s*\)',
)
ROUTE_CONST_RE = re.compile(
    r'(?:public|private|internal|protected)?\s*(?:static\s+)?const\s+string\s+(\w+)\s*=\s*"([^"]*)"',
)
# `{versionId:guid}` in source is `{versionId}` in the API reference. The sibling api-contract
# dashboard already normalises this; without it a boundary-checked route can never match its own
# documented spelling.
ROUTE_CONSTRAINT_RE = re.compile(r"\{([^}:]+):[^}]+\}")

# ADR reference in source: [ImplementsAdr("ADR-001", ...)]
ADR_REF_RE = re.compile(r'ImplementsAdr\s*\(\s*"(ADR-\d+)"')

# ADR file naming: 001-provider-abstraction.md -> ADR-001
ADR_FILE_RE = re.compile(r"^(\d{3})-.*\.md$")


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

@dataclass
class SourceItem:
    """A source-level construct that may or may not be documented."""

    name: str
    file_path: str
    line: int = 0
    documented: bool = False


@dataclass
class CategoryResult:
    """Coverage result for a single category."""

    category: str
    total: int = 0
    documented: int = 0
    items: List[SourceItem] = field(default_factory=list)

    @property
    def undocumented_items(self) -> List[SourceItem]:
        return [item for item in self.items if not item.documented]

    @property
    def coverage_pct(self) -> float:
        if self.total == 0:
            return 100.0
        return (self.documented / self.total) * 100.0


@dataclass
class CoverageReport:
    """Full documentation coverage report."""

    categories: List[CategoryResult] = field(default_factory=list)
    generated_at: str = ""

    @property
    def overall_total(self) -> int:
        return sum(c.total for c in self.categories)

    @property
    def overall_documented(self) -> int:
        return sum(c.documented for c in self.categories)

    @property
    def overall_pct(self) -> float:
        if self.overall_total == 0:
            return 100.0
        return (self.overall_documented / self.overall_total) * 100.0


# ---------------------------------------------------------------------------
# Utility helpers
# ---------------------------------------------------------------------------

def _should_skip(path: Path) -> bool:
    """Return True if *path* is inside an excluded directory."""
    for part in path.parts:
        if part in EXCLUDE_DIRS:
            return True
    return False


def _collect_files(root: Path, extensions: Tuple[str, ...]) -> List[Path]:
    """Recursively collect files matching *extensions*, honouring exclusions."""
    results: List[Path] = []
    for current, dirs, files in os.walk(root):
        dirs[:] = sorted(
            (name for name in dirs if name not in EXCLUDE_DIRS),
            key=str.casefold,
        )
        current_path = Path(current)
        if _should_skip(current_path):
            continue
        for file_name in sorted(files, key=str.casefold):
            if file_name.endswith(extensions):
                results.append(current_path / file_name)
    return sorted(results, key=lambda path: path.as_posix().casefold())


def _read_text_safe(path: Path) -> str:
    """Read file text, returning empty string on failure."""
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def _rel(path: Path, root: Path) -> str:
    """Return a portable relative path string."""
    try:
        return path.relative_to(root).as_posix()
    except ValueError:
        return path.as_posix()


# ---------------------------------------------------------------------------
# Analysis: Public types
# ---------------------------------------------------------------------------

def _scan_public_types(root: Path) -> List[SourceItem]:
    """Scan C# source files for public class / interface / record / enum."""
    src_dir = root / "src"
    if not src_dir.is_dir():
        return []

    items: List[SourceItem] = []
    seen: Set[str] = set()

    for cs_file in _collect_files(src_dir, CS_FILE_EXTENSIONS):
        text = _read_text_safe(cs_file)
        for match in PUBLIC_TYPE_RE.finditer(text):
            type_name = match.group(2)
            if type_name in seen:
                continue
            seen.add(type_name)
            line_num = text[:match.start()].count("\n") + 1
            items.append(
                SourceItem(
                    name=type_name,
                    file_path=_rel(cs_file, root),
                    line=line_num,
                )
            )
    return items


_IDENTIFIER_TOKEN_RE = re.compile(r"[0-9A-Za-z_]+")

# Characters that continue a name or a route, so a hit touching one of them is a hit on something
# longer. `/` counts only on the trailing side: `docs/api-reference` refers to the thing after the
# slash, while `/api/backfill/run` inside `/api/backfill/run/{id}` is a different route.
_NAME_BEFORE = frozenset("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_-")
_NAME_AFTER = frozenset("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_-/")
_SEGMENT_CHARS = frozenset("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_")


def _joins_another_segment(text: str, at: int, step: int, separator: Optional[str]) -> bool:
    """True when `text[at]` is a separator binding the term to a further segment.

    Only meaningful for keys built from segments — `IB.Port` is one key and `IB.Port.Timeout` is a
    different one, so the dot between them continues a name rather than ending it. The same dot at
    the end of a sentence does not, which is why this asks what lies on the *far* side of the
    separator instead of adding `.` to the boundary sets: `set IB.Port.` names the key, while
    `IB.Port.Timeout` and `Parent.IB.Port` name something else.
    """
    if separator is None or not 0 <= at < len(text) or text[at] != separator:
        return False
    neighbour = at + step
    return 0 <= neighbour < len(text) and text[neighbour] in _SEGMENT_CHARS


def _names_term(text: str, term: str, segment_separator: Optional[str] = None) -> bool:
    """True when `text` names `term` itself, rather than containing it inside something longer.

    The single statement of the boundary rule for the checks that scan a small, fixed set of
    documents. `_check_type_documentation` cannot use it — ~8,000 types against the whole corpus
    has to be decided by the index below, or the generator times out — but endpoints, config keys,
    and providers are a few hundred items against two or three files, where a walk is cheaper than
    building an index that models routes and dotted keys as well as identifiers.

    `segment_separator` extends the rule for names built from segments; see
    `_joins_another_segment`. Routes pass none, because `/` is already handled asymmetrically by
    the boundary sets — a leading slash is a boundary, a trailing one continues the path.

    Walks occurrences with `find` rather than compiling a regex per term: `re.search` with
    lookarounds costs a full corpus rescan for every item, which is the shape of the regression
    that made the earlier per-item scan untenable.
    """
    if not term:
        return False
    start = text.find(term)
    while start != -1:
        end = start + len(term)
        before_ok = start == 0 or (
            text[start - 1] not in _NAME_BEFORE
            and not _joins_another_segment(text, start - 1, -1, segment_separator)
        )
        after_ok = end == len(text) or (
            text[end] not in _NAME_AFTER
            and not _joins_another_segment(text, end, 1, segment_separator)
        )
        if before_ok and after_ok:
            return True
        start = text.find(term, start + 1)
    return False


def _documented_name_index(doc_contents: Dict[str, str]) -> frozenset:
    """Every identifier the documentation corpus names, tokenized in one pass.

    Splitting on non-identifier characters is the boundary rule expressed as membership:
    `DailyPortfolioPriceMark` is one token, so `PriceMark` is absent, while `` `PriceMark` `` and
    `PriceMark.` both yield it. Doing this per type instead — one regex scan of the corpus for each
    of ~8,000 public types — pushed this generator past its docs-automation timeout.
    """
    names: set = set()
    for content in doc_contents.values():
        names.update(_IDENTIFIER_TOKEN_RE.findall(content))
    return frozenset(names)


def _check_type_documentation(
    items: List[SourceItem],
    doc_contents: Dict[str, str],
) -> CategoryResult:
    """Mark items as documented if any doc file names them.

    The test used to be `item.name in content`, so a name counted whenever its characters
    appeared anywhere. Adding a design blueprint that merely *mentions* `MarkPriceQuote`,
    `MarkPriceQualityPolicy`, or `DailyMarkToMarketRequest` dropped all three off the
    undocumented list without a line of reference documentation being written — making
    documentation debt look paid by incidental mentions, and moving the metric in the
    reassuring direction while nothing improved.

    A boundary check does not distinguish a reference doc from a design doc — a blueprint
    naming a type on its own still counts. What it removes is the accidental hit: a name
    inside a longer name, inside a file path, or inside a member it does not own.
    """
    documented_names = _documented_name_index(doc_contents)
    for item in items:
        item.documented = item.name in documented_names

    documented = sum(1 for i in items if i.documented)
    return CategoryResult(
        category="Public Classes / Interfaces",
        total=len(items),
        documented=documented,
        items=items,
    )


# ---------------------------------------------------------------------------
# Analysis: API endpoints
# ---------------------------------------------------------------------------

def _load_route_constants(root: Path) -> Dict[str, str]:
    """`UiApiRoutes` constants, so `MapGroup(UiApiRoutes.HistoricalData)` resolves to its path."""
    text = _read_text_safe(root / "src" / "Meridian.Contracts" / "Api" / "UiApiRoutes.cs")
    return {name: value for name, value in ROUTE_CONST_RE.findall(text)}


def _join_route(prefix: str, route: str) -> str:
    """Compose a group prefix with a route relative to it."""
    combined = f"{prefix.rstrip('/')}/{route.strip().lstrip('/')}" if prefix else route.strip()
    combined = re.sub(r"/{2,}", "/", combined)
    if len(combined) > 1:
        combined = combined.rstrip("/")
    if combined and not combined.startswith("/"):
        combined = "/" + combined
    return ROUTE_CONSTRAINT_RE.sub(r"{\1}", combined)


def _receiver_before(text: str, dot: int) -> str:
    """The identifier immediately left of the `.` at `dot`, or "" when there is none.

    `group.MapGet(…)` yields `group`; `app.MapGroup("/x").MapGet(…)` yields "" because the
    receiver is an expression rather than a name, and an unnamed receiver correctly resolves to no
    prefix. Reads backwards over the identifier only, so it costs the identifier's length.
    """
    end = dot
    while end > 0 and text[end - 1] in " \t":
        end -= 1
    start = end
    while start > 0 and (text[start - 1].isalnum() or text[start - 1] == "_"):
        start -= 1
    return text[start:end]


def _route_spellings(route: str) -> Tuple[str, ...]:
    """The spellings of `route` a document may legitimately use.

    The reference writes most routes with a leading slash but not all — `api/backfill/run` appears
    bare — so both forms count for a full path. The slashless form is **not** offered for anything
    else, because for an unresolved relative fragment it degrades to a bare word: `/rules` becomes
    `rules`, which matches the last segment of `/api/risk/rules` and credits a route this scan
    could not resolve. That is worse than a wrong number, because it hides exactly the
    unresolved-prefix gap the group composition above exists to expose.
    """
    stripped = route.strip().lstrip("/")
    if not stripped:
        return ()
    if stripped.startswith("api/"):
        return (f"/{stripped}", stripped)
    return (f"/{stripped}",)


def _lookup_prefix(declarations: List[Tuple[int, str, str]], variable: str, before: int) -> str:
    """The prefix bound to `variable` by the nearest declaration above `before`.

    Position matters because one file routinely declares `var group` once per mapping method:
    `HistoricalEndpoints.cs` binds it to `/api/historical` at line 20 and to `""` at line 173.
    Keying by name alone lets the last declaration in the file claim every endpoint above it.
    """
    best = ""
    best_at = -1
    for at, name, prefix in declarations:
        if name == variable and best_at < at < before:
            best, best_at = prefix, at
    return best


def _group_prefixes(text: str, route_constants: Dict[str, str]) -> List[Tuple[int, str, str]]:
    """Every `MapGroup` declaration in a file, as (offset, variable, full path).

    Groups nest — `var sub = group.MapGroup("/runtime")` — so a prefix is resolved against the
    receiver's own prefix at that point in the file.
    """
    declarations: List[Tuple[int, str, str]] = []
    for match in MAP_GROUP_RE.finditer(text):
        variable, receiver, literal, constant = match.groups()
        if literal is not None:
            own: Optional[str] = literal
        elif constant in ("string.Empty", "String.Empty"):
            # A group that adds nothing but still nests: `group.MapGroup(string.Empty)` in
            # `FundStructureEndpoints.cs:25` inherits `/api/fund-structure`. Treating it as
            # unresolved would drop the parent prefix with it.
            own = ""
        else:
            # `UiApiRoutes.HistoricalData` is stored under its bare name, and several files declare
            # their own `const string RoutePrefix`, so the last segment is what resolves.
            own = route_constants.get(constant.rsplit(".", 1)[-1])

        if own is None:
            # An unresolved constant would silently compose a wrong path, so the group is skipped
            # and its endpoints keep their relative routes rather than being mis-composed.
            continue
        parent = _lookup_prefix(declarations, receiver, match.start())
        declarations.append((match.start(), variable, _join_route(parent, own)))
    return declarations


def _scan_endpoints(root: Path) -> List[SourceItem]:
    """Scan source for HTTP API route definitions, composing `MapGroup` prefixes.

    Minimal-API routes are written relative to their group: `EnvironmentDesignerEndpoints` maps
    `/api/environment-designer` and then `/runtime/versions/{versionId:guid}` beneath it, while
    `docs/reference/api-reference.md` documents the full `/api/environment-designer/runtime/...`.
    Recording only the child made 263 of 319 endpoints unmatchable against the documents they are
    checked against — a substring test papered over that by matching the fragment anywhere inside
    the full path, which credits `/complete` to any documented route containing it and, once the
    match is boundary-checked, credits nothing at all. Composing the prefix is what makes the two
    sides comparable; the matching rule is then free to be strict.

    Known limitation: a group passed as a *method argument* is not resolved. `RiskEndpoints.cs:78`
    calls `MapRiskRoutes(app.MapGroup("/api/risk"), …)`, so the routes inside that method keep
    their relative form. Following it needs the callee's signature, and the same file maps the
    same routes again under `/api/v1/risk`, so there is no single prefix to choose. Measured cost
    at the time of writing: 6 endpoints of 325 — the `/rules` and `/escalations` families — read as
    undocumented although `api-reference.md` documents them under `/api/risk/…`. Of the 26 routes
    that stay relative, the rest are mapped on `app` directly and are correct as they stand.

    That undercount is the honest failure mode and is deliberately preferred to the alternative.
    Until `_route_spellings` stopped offering the slashless form for relative routes, those same 6
    were reported as *documented*: `/rules` degraded to the bare word `rules`, which matches the
    last segment of `/api/risk/rules`. A limitation that inflates coverage hides itself; one that
    deflates it shows up as a gap somebody can act on.
    """
    src_dir = root / "src"
    if not src_dir.is_dir():
        return []

    route_constants = _load_route_constants(root)
    items: List[SourceItem] = []
    seen: Set[str] = set()

    for cs_file in _collect_files(src_dir, CS_FILE_EXTENSIONS):
        text = _read_text_safe(cs_file)

        # Only files that declare a group need the constant sweep. Scanning every `.cs` file for
        # `const string` — and copying the 855-entry shared table for each — took this generator
        # from 1.2s to 12.2s; barely 60 files in the repository call `MapGroup` at all.
        if "MapGroup" in text:
            # File-local `const string RoutePrefix = "…"` shadows the shared table, which is how
            # `MoneyMarketFundEndpoints` and `WorkstationEndpoints.SecurityMasterWorkbench`
            # declare their group paths.
            local_constants = dict(route_constants)
            local_constants.update(dict(ROUTE_CONST_RE.findall(text)))
            prefixes = _group_prefixes(text, local_constants)
        else:
            prefixes = []

        for pattern in (ROUTE_ATTRIBUTE_RE, MAP_ENDPOINT_RE):
            for match in pattern.finditer(text):
                if pattern is MAP_ENDPOINT_RE:
                    receiver = _receiver_before(text, match.start())
                    prefix = _lookup_prefix(prefixes, receiver, match.start()) if receiver else ""
                    route = _join_route(prefix, match.group(1))
                else:
                    route = match.group(1)
                if not route or route in seen:
                    continue
                seen.add(route)
                line_num = text[:match.start()].count("\n") + 1
                items.append(
                    SourceItem(
                        name=route,
                        file_path=_rel(cs_file, root),
                        line=line_num,
                    )
                )
    return items


def _check_endpoint_documentation(
    items: List[SourceItem],
    root: Path,
) -> CategoryResult:
    """Check endpoints against docs/reference/api-reference.md and CLAUDE.md.

    The match must land on a route boundary. A plain substring test credited a route for appearing
    inside a longer one, which matters most for the fragments this scan collects from route groups:
    `/complete`, `/reject`, `/{loanId}/activate` and similar are substrings of almost any documented
    path, so eight endpoints counted as documented without the doc naming them at all.

    The parameterised fallback is kept — a doc describing `/api/backfill/schedules` is taken to
    document `/api/backfill/schedules/{id}` — but it is boundary-checked too, so the base path has
    to be named rather than merely appear.
    """
    api_ref = root / "docs" / "reference" / "api-reference.md"
    claude_md = root / "CLAUDE.md"

    combined_text = ""
    for doc_path in (api_ref, claude_md):
        combined_text += _read_text_safe(doc_path) + "\n"

    # Both sides, or neither. `_scan_endpoints` normalises `{projectionRunId:guid}` to
    # `{projectionRunId}`, and seven routes in `api-reference.md` are written with the constraint
    # kept — `/api/projections/{projectionRunId:guid}/flows` at line 270 among them. Normalising
    # only the scan makes those unmatchable, and the parameter-stripped fallback then reduces the
    # route to `/api/projections/flows`, so a documented endpoint reads as a gap. The sibling
    # api-contract dashboard already normalises its corpus for this reason.
    combined_text = ROUTE_CONSTRAINT_RE.sub(r"{\1}", combined_text)

    for item in items:
        route = item.name.strip().lstrip("/")

        # A relative route of `/` carries no path of its own — its real path is the enclosing
        # `MapGroup` prefix, which this scan does not resolve. Matching what is left credits it for
        # any standalone slash in prose (the corpus has `` `Spread`/`Imbalance` ``), so it stays
        # undocumented: the scan cannot show that a doc names it. One endpoint is affected today,
        # `DirectLendingEndpoints.cs:37`.
        if not route:
            continue

        if any(_names_term(combined_text, spelling) for spelling in _route_spellings(item.name)):
            item.documented = True
        else:
            # Parameterised routes: /api/backfill/schedules/{id} -> /api/backfill/schedules
            base = re.sub(r"/\{[^}]+\}", "", item.name)
            if base and any(
                _names_term(combined_text, spelling) for spelling in _route_spellings(base)
            ):
                item.documented = True

    documented = sum(1 for i in items if i.documented)
    return CategoryResult(
        category="API Endpoints",
        total=len(items),
        documented=documented,
        items=items,
    )


# ---------------------------------------------------------------------------
# Analysis: Configuration keys
# ---------------------------------------------------------------------------

def _flatten_json_keys(obj: object, prefix: str = "") -> List[str]:
    """Recursively extract dotted key paths from a parsed JSON object."""
    keys: List[str] = []
    if isinstance(obj, dict):
        for k, v in obj.items():
            full = f"{prefix}.{k}" if prefix else k
            keys.append(full)
            keys.extend(_flatten_json_keys(v, full))
    elif isinstance(obj, list):
        for idx, v in enumerate(obj):
            keys.extend(_flatten_json_keys(v, f"{prefix}[{idx}]"))
    return keys


def _strip_json_comments(text: str) -> str:
    """Remove single-line // comments from JSON-with-comments text."""
    lines: List[str] = []
    in_string = False
    for line in text.split("\n"):
        cleaned: List[str] = []
        i = 0
        while i < len(line):
            ch = line[i]
            if ch == '"' and (i == 0 or line[i - 1] != "\\"):
                in_string = not in_string
                cleaned.append(ch)
            elif not in_string and ch == "/" and i + 1 < len(line) and line[i + 1] == "/":
                break  # rest of line is comment
            else:
                cleaned.append(ch)
            i += 1
        lines.append("".join(cleaned))
        # Reset in_string at end of each line (single-line strings only)
        in_string = False
    return "\n".join(lines)


def _scan_config_keys(root: Path) -> List[SourceItem]:
    """Extract top-level and second-level config keys from appsettings.sample.json."""
    sample = root / "config" / "appsettings.sample.json"
    if not sample.is_file():
        return []

    raw = _read_text_safe(sample)
    stripped = _strip_json_comments(raw)
    try:
        data = json.loads(stripped)
    except json.JSONDecodeError:
        return []

    # We care about top-level keys and one level of nesting.
    items: List[SourceItem] = []
    if not isinstance(data, dict):
        return items

    for key, value in data.items():
        items.append(
            SourceItem(name=key, file_path=_rel(sample, root))
        )
        if isinstance(value, dict):
            for sub_key in value:
                items.append(
                    SourceItem(
                        name=f"{key}.{sub_key}",
                        file_path=_rel(sample, root),
                    )
                )
    return items


def _check_config_documentation(
    items: List[SourceItem],
    root: Path,
) -> CategoryResult:
    """Check config keys against configuration-schema.md and CLAUDE.md.

    The key has to be named in full. Matching the last dotted segment instead — as this did — asks
    whether a doc mentions a word, not whether it documents a setting: `IB.Port` counted because
    something, somewhere, said "Port", and the same held for any key ending in `Enabled`, `Timeout`,
    or `Path`. That is the same defect as crediting `ApprovalDecision` to a doc that says
    `Decision`, and a leaf is a weaker claim still, because config leaves are ordinary English.
    """
    schema_doc = root / "docs" / "generated" / "configuration-schema.md"
    claude_md = root / "CLAUDE.md"

    combined = ""
    for doc_path in (schema_doc, claude_md):
        combined += _read_text_safe(doc_path) + "\n"

    for item in items:
        item.documented = _names_term(combined, item.name, segment_separator=".")

    documented = sum(1 for i in items if i.documented)
    return CategoryResult(
        category="Configuration Options",
        total=len(items),
        documented=documented,
        items=items,
    )


# ---------------------------------------------------------------------------
# Analysis: Provider implementations
# ---------------------------------------------------------------------------

def _scan_providers(root: Path) -> List[SourceItem]:
    """Identify provider implementation directories under Infrastructure/Providers."""
    providers_root = (
        root / "src" / "Meridian.Infrastructure" / "Providers"
    )
    if not providers_root.is_dir():
        return []

    items: List[SourceItem] = []
    # Scan subcategories: Streaming, Historical, Backfill, SymbolSearch
    for sub in sorted(providers_root.iterdir()):
        if not sub.is_dir() or sub.name.startswith("."):
            continue
        for provider_dir in sorted(sub.iterdir()):
            if not provider_dir.is_dir() or provider_dir.name.startswith("."):
                continue
            # Skip utility/framework dirs that are not actual provider names
            if provider_dir.name.lower() in {
                "core", "queue", "ratelimiting", "symbolresolution",
                "gapalysis", "gapanalysis",
            }:
                continue
            items.append(
                SourceItem(
                    name=f"{sub.name}/{provider_dir.name}",
                    file_path=_rel(provider_dir, root),
                )
            )
    return items


def _check_provider_documentation(
    items: List[SourceItem],
    root: Path,
) -> CategoryResult:
    """Check if providers are named in docs/providers/ or CLAUDE.md.

    Boundary-checked for the same reason as the checks above, though nothing moves today: the scan
    currently finds no providers, so this carried the substring defect latently rather than
    visibly. Fixed together with its siblings so the file states the rule once.
    """
    provider_docs_dir = root / "docs" / "providers"
    claude_md = root / "CLAUDE.md"

    combined = _read_text_safe(claude_md)
    if provider_docs_dir.is_dir():
        for md_file in provider_docs_dir.glob("*.md"):
            combined += "\n" + _read_text_safe(md_file)

    lowered = combined.lower()
    for item in items:
        # Short provider name, e.g. "Alpaca" from "Streaming/Alpaca". Case-insensitive because
        # provider docs use prose capitalisation, unlike the C# type names above.
        provider_name = item.name.split("/")[-1].lower()
        item.documented = _names_term(lowered, provider_name)

    documented = sum(1 for i in items if i.documented)
    return CategoryResult(
        category="Provider Implementations",
        total=len(items),
        documented=documented,
        items=items,
    )


# ---------------------------------------------------------------------------
# Analysis: ADR implementations
# ---------------------------------------------------------------------------

def _scan_adr_references(root: Path) -> List[SourceItem]:
    """Find ADR identifiers referenced in source via [ImplementsAdr]."""
    src_dir = root / "src"
    if not src_dir.is_dir():
        return []

    seen: Set[str] = set()
    items: List[SourceItem] = []

    for cs_file in _collect_files(src_dir, CS_FILE_EXTENSIONS):
        text = _read_text_safe(cs_file)
        for match in ADR_REF_RE.finditer(text):
            adr_id = match.group(1)  # e.g. "ADR-001"
            if adr_id in seen:
                continue
            seen.add(adr_id)
            line_num = text[:match.start()].count("\n") + 1
            items.append(
                SourceItem(
                    name=adr_id,
                    file_path=_rel(cs_file, root),
                    line=line_num,
                )
            )
    return items


def _check_adr_documentation(
    items: List[SourceItem],
    root: Path,
) -> CategoryResult:
    """Check that each referenced ADR has a matching file in docs/adr/ or archive/docs/adr/."""
    existing_adrs: Set[str] = set()

    for adr_dir in (root / "docs" / "adr", root / "archive" / "docs" / "adr"):
        if adr_dir.is_dir():
            for f in adr_dir.iterdir():
                m = ADR_FILE_RE.match(f.name)
                if m:
                    num = int(m.group(1))
                    existing_adrs.add(f"ADR-{num:03d}")

    for item in items:
        # Normalise to 3-digit form: ADR-1 -> ADR-001
        num_match = re.search(r"ADR-0*(\d+)", item.name)
        if num_match:
            normalised = f"ADR-{int(num_match.group(1)):03d}"
            item.documented = normalised in existing_adrs

    documented = sum(1 for i in items if i.documented)
    return CategoryResult(
        category="ADR Implementations",
        total=len(items),
        documented=documented,
        items=items,
    )


# ---------------------------------------------------------------------------
# Documentation content loader
# ---------------------------------------------------------------------------

def _load_doc_contents(root: Path) -> Dict[str, str]:
    """Load all Markdown documentation content keyed by relative path."""
    docs_dir = root / "docs"
    contents: Dict[str, str] = {}
    if docs_dir.is_dir():
        for md_file in _collect_files(docs_dir, DOC_FILE_EXTENSIONS):
            rel_path = _rel(md_file, root)
            if not rel_path.startswith(DOC_CONTENT_INCLUDE_PREFIXES):
                continue
            if rel_path.startswith(DOC_CONTENT_EXCLUDE_PREFIXES):
                continue
            contents[rel_path] = _read_text_safe(md_file)
    # CLAUDE.md and README.md are deliberately absent: both are project orientation prose, and a
    # type named in either was being counted as documented on the same footing as a reference page.
    return contents


# ---------------------------------------------------------------------------
# Report generation
# ---------------------------------------------------------------------------

def _coverage_bar(pct: float, width: int = 20) -> str:
    """Render a text-based progress bar for Markdown."""
    filled = round(pct / 100 * width)
    empty = width - filled
    return f"{'=' * filled}{'-' * empty}"


def _grade(pct: float) -> str:
    """Letter grade for a coverage percentage."""
    if pct >= 90:
        return "A"
    if pct >= 75:
        return "B"
    if pct >= 60:
        return "C"
    if pct >= 40:
        return "D"
    return "F"


def _recommendations(report: CoverageReport) -> List[str]:  # noqa: C901
    """Generate human-readable improvement recommendations."""
    recs: List[str] = []

    for cat in report.categories:
        undoc = cat.undocumented_items
        if not undoc:
            continue
        count = len(undoc)

        if cat.category == "Public Classes / Interfaces":
            if count > 50:
                recs.append(
                    f"**{cat.category}**: {count} undocumented types. "
                    "Consider generating API docs with DocFX (`docfx docfx.json`) "
                    "to cover the long tail of public types automatically."
                )
            elif count > 0:
                recs.append(
                    f"**{cat.category}**: {count} undocumented type(s). "
                    "Add entries to `docs/reference/api-reference.md` for the most "
                    "important ones."
                )

        elif cat.category == "API Endpoints":
            if count > 0:
                recs.append(
                    f"**{cat.category}**: {count} endpoint(s) missing from "
                    "`docs/reference/api-reference.md`. Run the endpoint audit "
                    "and update the API reference table."
                )

        elif cat.category == "Configuration Options":
            if count > 0:
                recs.append(
                    f"**{cat.category}**: {count} config key(s) not found in "
                    "`docs/generated/configuration-schema.md`. Re-run the "
                    "configuration schema generator to synchronise."
                )

        elif cat.category == "Provider Implementations":
            if count > 0:
                names = ", ".join(i.name.split("/")[-1] for i in undoc[:5])
                suffix = f" (and {count - 5} more)" if count > 5 else ""
                recs.append(
                    f"**{cat.category}**: {count} provider(s) lack dedicated "
                    f"documentation: {names}{suffix}. Add setup guides under "
                    "`docs/providers/`."
                )

        elif cat.category == "ADR Implementations":
            if count > 0:
                ids = ", ".join(i.name for i in undoc)
                recs.append(
                    f"**{cat.category}**: Referenced ADR(s) {ids} have no "
                    "corresponding file in `docs/adr/`. Create the missing "
                    "ADR document(s) using `docs/adr/_template.md`."
                )

    if not recs:
        recs.append("All categories are fully documented. Great job!")

    return recs


def generate_markdown(report: CoverageReport) -> str:
    """Render the full Markdown coverage report."""
    lines: List[str] = []

    lines.append("# Documentation Coverage Report")
    lines.append("")
    lines.append("> Auto-generated by `build/scripts/docs/generate-coverage.py`")
    lines.append(f"> Generated: {report.generated_at}")
    lines.append("")

    # --- Overall ---
    lines.append("## Overall Coverage")
    lines.append("")
    grade = _grade(report.overall_pct)
    lines.append(
        f"**{report.overall_documented} / {report.overall_total}** items documented "
        f"(**{report.overall_pct:.1f}%**) &mdash; Grade: **{grade}**"
    )
    lines.append("")
    lines.append("```text")
    lines.append(f"[{_coverage_bar(report.overall_pct)}] {report.overall_pct:.1f}%")
    lines.append("```")
    lines.append("")

    # --- Per-category table ---
    lines.append("## Coverage by Category")
    lines.append("")
    lines.append("| Category | Documented | Total | Coverage | Grade |")
    lines.append("| ---------- | ----------- | ------- | ---------- | ------- |")
    for cat in report.categories:
        lines.append(
            f"| {cat.category} | {cat.documented} | {cat.total} "
            f"| {cat.coverage_pct:.1f}% | {_grade(cat.coverage_pct)} |"
        )
    lines.append("")

    # --- Undocumented items ---
    has_undocumented = any(cat.undocumented_items for cat in report.categories)
    if has_undocumented:
        lines.append("## Undocumented Items")
        lines.append("")

        for cat in report.categories:
            undoc = cat.undocumented_items
            if not undoc:
                continue
            lines.append(f"### {cat.category} ({len(undoc)} undocumented)")
            lines.append("")
            lines.append("| Item | Location |")
            lines.append("| ------ | ---------- |")
            # Show up to 50 items per category to keep the report manageable
            display = undoc[:50]
            for item in display:
                loc = item.file_path
                if item.line:
                    loc += f":{item.line}"
                lines.append(f"| `{item.name}` | `{loc}` |")
            if len(undoc) > 50:
                lines.append(
                    f"| ... and {len(undoc) - 50} more | |"
                )
            lines.append("")

    # --- Recommendations ---
    lines.append("## Recommendations")
    lines.append("")
    for idx, rec in enumerate(_recommendations(report), 1):
        lines.append(f"{idx}. {rec}")
    lines.append("")

    # --- Footer ---
    lines.append("---")
    lines.append("")
    lines.append(
        "_This report was generated automatically. "
        "Do not edit manually._"
    )
    lines.append("")

    return "\n".join(lines)


def generate_summary(report: CoverageReport) -> str:
    """Generate a concise summary suitable for GITHUB_STEP_SUMMARY."""
    lines: List[str] = []
    grade = _grade(report.overall_pct)

    lines.append("### Documentation Coverage")
    lines.append("")
    lines.append(
        f"**{report.overall_pct:.1f}%** overall "
        f"({report.overall_documented}/{report.overall_total}) "
        f"&mdash; Grade: **{grade}**"
    )
    lines.append("")
    lines.append("| Category | Coverage |")
    lines.append("| ---------- | ---------- |")
    for cat in report.categories:
        lines.append(f"| {cat.category} | {cat.coverage_pct:.1f}% ({cat.documented}/{cat.total}) |")
    lines.append("")

    undoc_total = sum(len(c.undocumented_items) for c in report.categories)
    if undoc_total:
        lines.append(f"**{undoc_total}** item(s) lack documentation coverage.")
    else:
        lines.append("All items are documented.")
    lines.append("")

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Main orchestration
# ---------------------------------------------------------------------------

def build_report(root: Path) -> CoverageReport:
    """Run all analyses and assemble the coverage report."""
    report = CoverageReport(
        generated_at=STABLE_GENERATED_AT,
    )

    # Load documentation content once for type-mention scanning.
    doc_contents = _load_doc_contents(root)

    # 1. Public types
    type_items = _scan_public_types(root)
    type_result = _check_type_documentation(type_items, doc_contents)
    report.categories.append(type_result)

    # 2. API endpoints
    endpoint_items = _scan_endpoints(root)
    endpoint_result = _check_endpoint_documentation(endpoint_items, root)
    report.categories.append(endpoint_result)

    # 3. Configuration options
    config_items = _scan_config_keys(root)
    config_result = _check_config_documentation(config_items, root)
    report.categories.append(config_result)

    # 4. Provider implementations
    provider_items = _scan_providers(root)
    provider_result = _check_provider_documentation(provider_items, root)
    report.categories.append(provider_result)

    # 5. ADR implementations
    adr_items = _scan_adr_references(root)
    adr_result = _check_adr_documentation(adr_items, root)
    report.categories.append(adr_result)

    return report


def main(argv: Optional[List[str]] = None) -> int:
    """Entry point for the documentation coverage generator."""
    parser = argparse.ArgumentParser(
        description="Generate documentation coverage reports by comparing "
        "what is documented versus what exists in source code.",
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=Path("."),
        help="Repository root directory (default: current directory).",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Path to write the Markdown coverage report. "
        "Defaults to docs/generated/documentation-coverage.md.",
    )
    parser.add_argument(
        "--summary",
        action="store_true",
        default=False,
        help="Print a concise summary to stdout (suitable for GITHUB_STEP_SUMMARY).",
    )

    args = parser.parse_args(argv)

    root: Path = args.root.resolve()
    if not root.is_dir():
        print(f"Error: root directory does not exist: {root}", file=sys.stderr)
        return 1

    output: Path = (
        args.output
        if args.output is not None
        else root / "docs" / "generated" / "documentation-coverage.md"
    )

    try:
        report = build_report(root)
    except Exception as exc:
        print(f"Error: failed to build coverage report: {exc}", file=sys.stderr)
        return 1

    # Write Markdown report
    md = generate_markdown(report)
    try:
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(md, encoding="utf-8")
        print(f"Coverage report written to {output}")
    except OSError as exc:
        print(f"Error: could not write report: {exc}", file=sys.stderr)
        return 1

    # Print summary
    if args.summary:
        print("")
        print(generate_summary(report))
    else:
        # Always print a one-liner even without --summary
        grade = _grade(report.overall_pct)
        print(
            f"Documentation coverage: {report.overall_pct:.1f}% "
            f"({report.overall_documented}/{report.overall_total}) "
            f"- Grade: {grade}"
        )

    return 0


if __name__ == "__main__":
    sys.exit(main())
