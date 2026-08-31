#!/usr/bin/env python3
"""Generate the UI route wiring report.

Answers one question mechanically: which backend HTTP routes does the browser
workstation never call?

Both sides are resolved symbolically rather than grepped, because neither side
writes routes as plain literals:

* Backend routes are assembled from ``UiApiRoutes`` constants, ``MapGroup``
  prefixes (including prefixes that travel into helper methods through a
  ``RouteGroupBuilder`` parameter), and ``*Subroute`` helpers that strip a
  prefix the group already carries.
* Dashboard call sites reference registry symbols (``WORKSTATION_API_ENDPOINTS
  .portfolio``, ``riskEscalationApproveEndpoint(id)``) that resolve to a path
  only after the registry modules are evaluated.

Comparing the two sides after resolution gives three states per route:

``wired``          a non-registry dashboard module resolves to this path.
``registry-only``  the endpoint registry declares the path, nothing calls it.
``unwired``        no dashboard module references the path at all.

Usage:
    python3 build/scripts/docs/generate-ui-route-wiring-report.py --summary
    python3 build/scripts/docs/generate-ui-route-wiring-report.py \
        --output docs/status/ui-route-wiring-report.md \
        --json-output docs/status/ui-route-wiring-report.json
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from pathlib import Path
from typing import Iterable, Optional, Sequence

REPO_ROOT = Path(__file__).resolve().parents[3]
SRC_ROOT = REPO_ROOT / "src"
DASHBOARD_ROOT = REPO_ROOT / "src/Meridian.Ui/dashboard/src"
WPF_ROOT = REPO_ROOT / "src/Meridian.Wpf"

# Registry modules declare paths for other modules to call. A reference inside one
# is not a call site, so they are scored separately from ordinary dashboard code.
REGISTRY_MODULES = {
    "workstation-endpoints.ts",
    "reporting-governance-routes.ts",
}

# Generated mirrors of the C# route contract. Every route appears in them by
# construction, so they can never be evidence that a route is called.
GENERATED_MODULES = {
    "ui-api-routes.generated.ts",
    "workspace-catalog.generated.ts",
}

HTTP_VERBS = ("Get", "Post", "Put", "Patch", "Delete", "Methods")

# Routes the browser workstation is not expected to call. Each entry is a reason,
# not a silencer: the report still lists them, under "excluded by design".
NON_UI_ROUTE_PATTERNS: tuple[tuple[str, str], ...] = (
    (r"^/(health|healthz|ready|readyz|live|livez|startup|startupz|metrics)$",
     "Container/orchestrator probe, not an operator surface."),
    (r"^/api/health", "Probe alias consumed by infrastructure, not the workstation."),
    (r"^/hooks/", "Inbound provider callback authenticated by signature, never called by a browser."),
    (r"^/api/dev/", "Development-only seeding seam."),
    (r"^/api/demo/", "Demo-mode seam driven by the demo host."),
    (r"^/api/v1/", "Versioned alias of an /api route the workstation already calls."),
    (r"^/api/auth/desktop-launch/", "Desktop (WPF) launch handoff."),
    (r"/callback$", "OAuth redirect target the browser navigates to, not a fetch."),
    (r"^/api/fund-structure/(report-packs|report-pack-preview|workspace-view|structured-exports)",
     "410 Gone tombstone for the retired legacy reporting lifecycle."),
    (r"^/api/fund-structure/reporting/packs", "410 Gone tombstone for the retired legacy reporting lifecycle."),
    (r"^/workstation(/|$)", "Static workstation asset route served by the host."),
    (r"^/login$|^/logout$", "Host-rendered authentication page."),
    (r"^/api/fund-structure/reporting/distribution/deliveries/delivery_",
     "External recipient download link, opened outside the workstation."),
)


# ---------------------------------------------------------------------------
# Shared helpers
# ---------------------------------------------------------------------------

def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


UNKNOWN_SEGMENT = "\x01"


def normalize(path: str) -> str:
    """Collapse route parameters and query strings so both sides compare equal.

    A declared route parameter becomes ``{}``. A call-site interpolation the
    analyzer could not resolve becomes ``*``: it may stand for a literal segment
    (``/api/replay/{id}/${action}``) as well as for a parameter, so it has to
    match more loosely than a declared parameter does.
    """
    path = path.split("?", 1)[0]
    path = re.sub(r"\{[^{}]*\}", "{}", path)
    path = path.replace(UNKNOWN_SEGMENT, "*")
    path = re.sub(r"/+", "/", path)
    return path.rstrip("/") or "/"


def route_is_called(route: str, called: set[str], called_index: dict[int, list[list[str]]]) -> bool:
    """True when a call site resolves to this route.

    Exact match first; otherwise compare segment by segment so an unresolved
    call-site segment (``*``) can stand in for a literal or a parameter.
    """
    if route in called:
        return True
    segments = route.split("/")
    for candidate in called_index.get(len(segments), ()):
        if all(actual == expected or actual == "*" or (actual == "{}" and expected == "{}")
               for expected, actual in zip(segments, candidate)):
            return True
    return False


def index_called(called: set[str]) -> dict[int, list[list[str]]]:
    index: dict[int, list[list[str]]] = defaultdict(list)
    for path in called:
        if "*" not in path:
            continue
        segments = path.split("/")
        index[len(segments)].append(segments)
    return index


def excluded_reason(path: str) -> Optional[str]:
    for pattern, reason in NON_UI_ROUTE_PATTERNS:
        if re.search(pattern, path):
            return reason
    return None


# ---------------------------------------------------------------------------
# Backend: C# route inventory
# ---------------------------------------------------------------------------

CONST_DECL_RE = re.compile(
    r"\b(?:public|internal|private|protected)?\s*const\s+string\s+(\w+)\s*=\s*([^;]+);")
CLASS_RE = re.compile(r"(?:static\s+)?class\s+(\w+)")
MAP_GROUP_RE = re.compile(r"MapGroup\(\s*([^()]*?(?:\([^()]*\))?[^()]*?)\s*\)")
GROUP_VAR_RE = re.compile(r"var\s+(\w+)\s*=\s*((?:[^;]|\n)*?);")
SUBROUTE_RE = re.compile(r"^\w*Subroute\(\s*(.*?)\s*\)$", re.S)
METHOD_DECL_RE = re.compile(
    r"(?:public|private|internal|protected)\s+(?:static\s+)?[\w<>,\[\]?\s]+?\s(\w+)\s*\(([^)]*)\)\s*\{",
    re.S)
GROUP_PARAM_RE = re.compile(
    r"\b(?:RouteGroupBuilder|IEndpointRouteBuilder|IEndpointConventionBuilder)\s+(\w+)")
MAP_CALL_RE = re.compile(
    r"(?:(\w+)\s*\.\s*)?Map(" + "|".join(HTTP_VERBS) + r")\s*\(\s*(?:\[[^\]]*\]\s*)?"
    r"((?:[^,()]|\([^()]*\))*)")
ROOT_BUILDER_NAMES = {"app", "builder", "endpoints", "routes"}
OBSOLETE_PRAGMA_RE = re.compile(
    r"#pragma\s+warning\s+(disable|restore)\s+CS0618[^\S\n]*(?://\s*(?P<note>.*))?")


def obsolete_spans(source: str) -> list[tuple[int, int, str]]:
    """Find regions where a file suppresses CS0618 (\"member is obsolete\").

    A route mapped inside one is deliberately serving a superseded contract:
    the canonical route is mapped outside the region over the same service, and
    the alias only exists so retained links stay recoverable. Such a route is
    not a gap in the UI, so it is reported with the suppression's own note
    rather than counted as work to do.
    """
    spans: list[tuple[int, int, str]] = []
    open_at: Optional[tuple[int, str]] = None
    for match in OBSOLETE_PRAGMA_RE.finditer(source):
        if match.group(1) == "disable":
            if open_at is None:
                open_at = (match.start(), (match.group("note") or "").strip())
        elif open_at is not None:
            spans.append((open_at[0], match.end(), open_at[1]))
            open_at = None
    if open_at is not None:
        spans.append((open_at[0], len(source), open_at[1]))
    return spans


ABSOLUTE_MARKER = "\0ABS"


def load_route_constants() -> dict[str, str]:
    """Map ``Member`` and ``Class.Member`` names to their literal route values."""
    constants: dict[str, str] = {}
    for file in sorted(SRC_ROOT.rglob("*.cs")):
        source = read_text(file)
        if "const string" not in source:
            continue
        class_match = CLASS_RE.search(source)
        class_name = class_match.group(1) if class_match else None
        declarations = [(m.group(1), m.group(2).strip()) for m in CONST_DECL_RE.finditer(source)]
        local: dict[str, str] = {}
        for _ in range(4):
            for name, expression in declarations:
                if name in local:
                    continue
                value = _fold_constant(expression, local, constants)
                if value is not None:
                    local[name] = value
        for name, value in local.items():
            if not value.startswith("/") and value != "":
                continue
            constants.setdefault(name, value)
            if class_name:
                constants[f"{class_name}.{name}"] = value
    return constants


def _fold_constant(expression: str, local: dict[str, str], constants: dict[str, str]) -> Optional[str]:
    tokens = re.findall(r'"([^"]*)"|([A-Za-z_][\w.]*)', expression)
    if not tokens:
        return None
    folded = ""
    for literal, identifier in tokens:
        if literal:
            folded += literal
            continue
        key = identifier.split(".")[-1]
        if key in local:
            folded += local[key]
        elif key in constants:
            folded += constants[key]
        else:
            return None
    return folded


def resolve_route_expression(expression: str, constants: dict[str, str]) -> Optional[str]:
    """Resolve a C# route argument to a literal path.

    ``*Subroute(...)`` helpers strip the group prefix from an absolute constant,
    so the resolved value is returned pre-marked as absolute: prefixing it again
    would double the prefix.
    """
    expression = expression.strip().replace("global::Meridian.Contracts.Api.", "")
    expression = re.sub(r"\.Replace\([^()]*\)", "", expression)
    subroute = SUBROUTE_RE.match(expression)
    if subroute:
        inner = resolve_route_expression(subroute.group(1), constants)
        return None if inner is None else ABSOLUTE_MARKER + inner.removeprefix(ABSOLUTE_MARKER)
    if expression in ("string.Empty", '""'):
        return ""
    literal = re.fullmatch(r'"([^"]*)"', expression)
    if literal:
        return literal.group(1)
    return _fold_constant(expression, {}, constants)


def _group_prefix(expression: str, groups: dict[str, str], constants: dict[str, str]) -> Optional[str]:
    base = ""
    head = re.match(r"\s*(\w+)\s*(?:\.|$)", expression)
    if head:
        name = head.group(1)
        if name in groups:
            base = groups[name]
        elif name not in ROOT_BUILDER_NAMES:
            # An unknown identifier carries an unknown prefix. Treating it as the
            # root would mint routes that are missing their group prefix.
            return None
    segments = []
    for match in MAP_GROUP_RE.finditer(expression):
        resolved = resolve_route_expression(match.group(1), constants)
        if resolved is None:
            return None
        segments.append(resolved.removeprefix(ABSOLUTE_MARKER))
    return base + "".join(segments)


def _method_regions(source: str) -> list[dict]:
    """Method bodies, so a group variable reused across methods stays scoped.

    ``var group = app.MapGroup(...)`` appears once per mapping method in most
    endpoint files; without scoping, the last declaration would silently rewrite
    the prefix of every earlier one.
    """
    regions = []
    for match in METHOD_DECL_RE.finditer(source):
        declared = [p.strip() for p in match.group(2).split(",")]
        group_parameters = {
            GROUP_PARAM_RE.search(parameter).group(1): index
            for index, parameter in enumerate(declared) if GROUP_PARAM_RE.search(parameter)
        }
        opening = source.find("{", match.end() - 1)
        if opening < 0:
            continue
        depth, index = 0, opening
        while index < len(source):
            if source[index] == "{":
                depth += 1
            elif source[index] == "}":
                depth -= 1
                if depth == 0:
                    break
            index += 1
        regions.append({
            "name": match.group(1),
            "start": opening,
            "end": index,
            "body": source[opening:index],
            "group_parameters": group_parameters,
        })
    return regions


def _resolve_groups(body: str, inherited: dict[str, str], constants: dict[str, str]) -> dict[str, str]:
    groups = dict(inherited)
    for _ in range(3):
        for match in GROUP_VAR_RE.finditer(body):
            variable, expression = match.group(1), match.group(2)
            if "MapGroup" not in expression:
                continue
            prefix = _group_prefix(expression, groups, constants)
            if prefix is not None:
                groups[variable] = prefix
    return groups


def collect_backend_routes(constants: dict[str, str]) -> tuple[list[dict], list[dict]]:
    """Resolve every mapped route to a full path.

    Endpoint classes are partial and split across files, so a group declared in
    one file is routinely handed to a mapping helper in another. Group variables
    are resolved per method body, then shared across the declaring type so a
    helper's caller can supply its prefix.
    """
    files: list[dict] = []
    for file in sorted(SRC_ROOT.rglob("*.cs")):
        source = read_text(file)
        if ".Map" not in source:
            continue
        class_match = CLASS_RE.search(source)
        files.append({
            "path": file.relative_to(REPO_ROOT).as_posix(),
            "source": source,
            "class": class_match.group(1) if class_match else file.stem,
            "regions": _method_regions(source),
        })

    for entry in files:
        for region in entry["regions"]:
            region["groups"] = _resolve_groups(region["body"], {}, constants)

    # A helper receives an already-prefixed group; bind the prefixes its call
    # sites pass to that specific helper. Keying by helper rather than by
    # parameter name keeps two helpers that both name their parameter
    # ``reportingGroup`` from inheriting each other's prefix.
    declarations: dict[str, dict[str, dict[str, int]]] = defaultdict(dict)
    for entry in files:
        for region in entry["regions"]:
            if region["group_parameters"]:
                declarations[entry["class"]][region["name"]] = region["group_parameters"]

    call_prefixes: dict[tuple[str, str, str], set[str]] = defaultdict(set)
    for entry in files:
        for name, group_parameters in declarations[entry["class"]].items():
            # One statement's argument list: balanced parentheses, no statement
            # separator, and a call terminator. A method declaration's own
            # signature cannot match, because its `)` is followed by a body.
            call_re = re.compile(
                re.escape(name) + r"\s*\(([^;()]*(?:\([^()]*\)[^;()]*)*)\)\s*(?:;|,)")
            for call in call_re.finditer(entry["source"]):
                arguments = _split_arguments(call.group(1))
                enclosing = next((r for r in entry["regions"] if r["start"] <= call.start() <= r["end"]), None)
                if enclosing and enclosing["name"] == name:
                    continue  # recursive/self reference
                scope = enclosing["groups"] if enclosing else {}
                for parameter_name, position in group_parameters.items():
                    if position >= len(arguments):
                        continue
                    prefix = _group_prefix(arguments[position], scope, constants)
                    if prefix is not None:
                        call_prefixes[(entry["class"], name, parameter_name)].add(prefix)

    routes: dict[tuple[str, str], dict] = {}
    unresolved: list[dict] = []
    for entry in files:
        suppressions = obsolete_spans(entry["source"])
        for match in MAP_CALL_RE.finditer(entry["source"]):
            variable, verb, route_expression = match.groups()
            resolved = resolve_route_expression(route_expression, constants)
            if resolved is None:
                unresolved.append({"file": entry["path"], "method": verb.upper(),
                                   "expression": route_expression.strip()[:120]})
                continue
            absolute = resolved.startswith(ABSOLUTE_MARKER)
            resolved = resolved.removeprefix(ABSOLUTE_MARKER)

            region = next((r for r in entry["regions"] if r["start"] <= match.start() <= r["end"]), None)
            groups = region["groups"] if region else {}

            if absolute or variable in ROOT_BUILDER_NAMES:
                prefixes = {""}
            elif variable in groups:
                prefixes = {groups[variable]}
            elif region and variable in region["group_parameters"]:
                prefixes = set(call_prefixes.get((entry["class"], region["name"], variable), ())) or {""}
            else:
                prefixes = {""}

            constant = re.search(r"UiApiRoutes\.(\w+)", route_expression)
            for prefix in prefixes:
                full = resolved if (prefix and resolved.startswith(prefix)) else prefix + resolved
                if not full.startswith("/"):
                    continue
                route = routes.setdefault((full, verb.upper()), {
                    "path": full,
                    "method": verb.upper(),
                    "files": set(),
                    "constant": constant.group(1) if constant else None,
                    "obsolete_reason": None,
                })
                route["files"].add(entry["path"])
                note = next((n for start, end, n in suppressions if start <= match.start() <= end), None)
                if note is not None and route["obsolete_reason"] is None:
                    route["obsolete_reason"] = (
                        f"Superseded contract retained behind a CS0618 suppression: {note}"
                        if note else "Superseded contract retained behind a CS0618 suppression.")

    inventory = [
        {"path": route["path"], "method": route["method"],
         "constant": route["constant"], "files": sorted(route["files"]),
         "obsolete_reason": route["obsolete_reason"]}
        for route in routes.values()
    ]
    inventory.sort(key=lambda item: (item["path"], item["method"]))
    return inventory, unresolved


# ---------------------------------------------------------------------------
# Dashboard: TypeScript call-site inventory
# ---------------------------------------------------------------------------

OBJECT_DECL_RE = re.compile(r"export const (\w+)\s*=\s*\{(.*?)\n\}", re.S)
OBJECT_ENTRY_RE = re.compile(r"^\s*(\w+)\s*:\s*(.+?),?\s*$", re.M)
FUNCTION_DECL_RE = re.compile(r"(?:export )?function (\w+)\s*\([^)]*\)\s*:\s*string\s*\{(.*?)\n\}", re.S)
RETURN_RE = re.compile(r"return\s+(.+?);", re.S)
TEMPLATE_LITERAL_RE = re.compile(r"`([^`]*\$\{[^`]*)`")
LITERAL_PATH_RE = re.compile(
    r"""["'`](/(?:api|health|healthz|ready|readyz|live|livez|startup|startupz|metrics|hubs|ws"""
    r"""|workstation|portal|setup|login|logout|hooks)[^"'`]*)""")


def strip_comments(source: str) -> str:
    """Blank out line and block comments, preserving offsets and string contents.

    Route paths appear constantly in doc comments (``/** GET /api/... */``).
    Scanning them would count a mention as a call site, so comments are removed
    before any call-site pattern runs.
    """
    out = []
    state = "code"
    index = 0
    length = len(source)
    while index < length:
        char = source[index]
        nxt = source[index + 1] if index + 1 < length else ""
        if state == "code":
            if char == "/" and nxt == "/":
                state = "line"
                out.append("  ")
                index += 2
                continue
            if char == "/" and nxt == "*":
                state = "block"
                out.append("  ")
                index += 2
                continue
            if char in "\"'`":
                state = {'"': "double", "'": "single", "`": "template"}[char]
            out.append(char)
        elif state == "line":
            if char == "\n":
                state = "code"
                out.append(char)
            else:
                out.append(" ")
        elif state == "block":
            if char == "*" and nxt == "/":
                state = "code"
                out.append("  ")
                index += 2
                continue
            out.append("\n" if char == "\n" else " ")
        else:
            out.append(char)
            if char == "\\":
                if index + 1 < length:
                    out.append(source[index + 1])
                    index += 2
                    continue
            elif (state == "double" and char == '"') or (state == "single" and char == "'") \
                    or (state == "template" and char == "`"):
                # The opening quote was appended in "code"; this closes it.
                if len(out) >= 2:
                    state = "code"
        index += 1
    return "".join(out)


def load_generated_routes() -> dict[str, str]:
    source = read_text(DASHBOARD_ROOT / "lib/ui-api-routes.generated.ts")
    return {m.group(1): m.group(2) for m in re.finditer(r'(\w+):\s*"([^"]+)"', source)}



QUERY_BUILDERS = {"queryString", "buildQueryString", "toQueryString", "withQuery"}
SEGMENT_BUILDERS = {"pathSegment", "encodeURIComponent", "encodeURI", "segment"}


def _split_top_level(expression: str, separator: str) -> list[str]:
    """Split on a separator that is not nested inside brackets or a template literal."""
    parts, depth, current = [], 0, ""
    in_template = False
    for char in expression:
        if char == "`":
            in_template = not in_template
        elif not in_template and char in "({[":
            depth += 1
        elif not in_template and char in ")}]":
            depth -= 1
        if char == separator and depth == 0 and not in_template:
            parts.append(current)
            current = ""
            continue
        current += char
    parts.append(current)
    return parts


def _split_arguments(arguments: str) -> list[str]:
    return _split_top_level(arguments, ",")


def _template_interpolations(body: str) -> list[tuple[int, int, str]]:
    """Spans and contents of `${...}` in a template literal.

    Brace-counted rather than regex-matched: an interpolation routinely contains
    an object literal (`${queryString({ limit })}`), which a `[^{}]*` pattern
    silently skips, leaving the raw source text embedded in the resolved path.
    """
    spans = []
    index = 0
    while True:
        start = body.find("${", index)
        if start < 0:
            return spans
        depth = 0
        cursor = start + 1
        while cursor < len(body):
            if body[cursor] == "{":
                depth += 1
            elif body[cursor] == "}":
                depth -= 1
                if depth == 0:
                    break
            cursor += 1
        if cursor >= len(body):
            return spans
        spans.append((start, cursor + 1, body[start + 2:cursor].strip()))
        index = cursor + 1


def _resolve_ts_expression(expression: str, generated: dict[str, str],
                           symbols: dict[str, set[str]]) -> Optional[set[str]]:
    """Resolve a TypeScript endpoint expression to the path patterns it can produce.

    A helper often produces more than one path (``presetId ? base/{id} : base``),
    so resolution is set-valued.
    """
    expression = expression.strip().rstrip(",").strip()
    if not expression:
        return None

    plain = re.fullmatch(r"""["']([^"']*)["']""", expression)
    if plain:
        return {plain.group(1)}

    template = re.fullmatch(r"`([^`]*)`", expression)
    if template:
        body = template.group(1)
        results = {""}
        index = 0
        for start, end, inner_expression in _template_interpolations(body):
            literal = body[index:start]
            inner = _resolve_ts_expression(inner_expression, generated, symbols) or {UNKNOWN_SEGMENT}
            results = {prefix + literal + value for prefix in results for value in inner}
            index = end
        tail = body[index:]
        return {value + tail for value in results}

    member = re.fullmatch(r"UI_API_ROUTES\.(\w+)", expression)
    if member:
        route = generated.get(member.group(1))
        return {route} if route else None

    registry = re.fullmatch(r"(\w+)\.(\w+)", expression)
    if registry:
        return symbols.get(f"{registry.group(1)}.{registry.group(2)}")

    bare = re.fullmatch(r"\w+", expression)
    if bare:
        return symbols.get(expression)

    ternary = _split_top_level(expression, "?")
    if len(ternary) == 2:
        branches = _split_top_level(ternary[1], ":")
        if len(branches) >= 2:
            resolved: set[str] = set()
            for branch in (branches[0], ":".join(branches[1:])):
                values = _resolve_ts_expression(branch, generated, symbols)
                if values:
                    resolved |= values
            return resolved or None

    call = re.fullmatch(r"(\w+)\((.*)\)", expression, re.S)
    if call:
        name, arguments = call.group(1), call.group(2)
        if name in QUERY_BUILDERS:
            return {""}
        if name in SEGMENT_BUILDERS:
            return {UNKNOWN_SEGMENT}
        if name in symbols:
            return symbols[name]
        for argument in _split_arguments(arguments):
            values = _resolve_ts_expression(argument, generated, symbols)
            if values and any(value.startswith("/") for value in values):
                return values
        return None

    concatenation = [part for part in _split_top_level(expression, "+") if part.strip()]
    if len(concatenation) > 1:
        results = {""}
        for part in concatenation:
            values = _resolve_ts_expression(part, generated, symbols)
            if not values:
                return None
            results = {prefix + value for prefix in results for value in values}
        return results

    return None


def build_registry_symbols(generated: dict[str, str]) -> dict[str, set[str]]:
    """Map registry symbols (``OBJ.key`` and helper function names) to paths."""
    symbols: dict[str, set[str]] = {}
    sources = {}
    for name in REGISTRY_MODULES:
        path = DASHBOARD_ROOT / "lib" / name
        if path.exists():
            sources[name] = strip_comments(read_text(path))

    for _ in range(5):
        for source in sources.values():
            for object_match in OBJECT_DECL_RE.finditer(source):
                object_name = object_match.group(1)
                for entry in OBJECT_ENTRY_RE.finditer(object_match.group(2)):
                    key = f"{object_name}.{entry.group(1)}"
                    if key in symbols:
                        continue
                    resolved = _resolve_ts_expression(entry.group(2), generated, symbols)
                    routes = {r for r in resolved or () if r.startswith("/")}
                    if routes:
                        symbols[key] = routes
            for function_match in FUNCTION_DECL_RE.finditer(source):
                name = function_match.group(1)
                if name in symbols:
                    continue
                routes: set[str] = set()
                for return_match in RETURN_RE.finditer(function_match.group(2)):
                    resolved = _resolve_ts_expression(return_match.group(1), generated, symbols)
                    routes |= {r for r in resolved or () if r.startswith("/")}
                if routes:
                    symbols[name] = routes
    return symbols


def unresolved_registry_helpers(symbols: dict[str, set[str]]) -> list[str]:
    """Registry helpers whose return value the analyzer could not fold to a path.

    Each one is a blind spot: a route it builds can be reported as unwired even
    though a screen calls it. Listing them keeps that gap visible.
    """
    unresolved = []
    for name in REGISTRY_MODULES:
        path = DASHBOARD_ROOT / "lib" / name
        if not path.exists():
            continue
        for match in re.finditer(r"export function (\w+)\s*\([^)]*\)\s*:\s*string", strip_comments(read_text(path))):
            if match.group(1) not in symbols:
                unresolved.append(f"{name}::{match.group(1)}")
    return sorted(unresolved)


def dashboard_files() -> list[tuple[Path, str]]:
    files = []
    for file in sorted(DASHBOARD_ROOT.rglob("*")):
        if not file.is_file() or file.suffix not in {".ts", ".tsx"}:
            continue
        if file.name in GENERATED_MODULES:
            continue
        files.append((file, strip_comments(read_text(file))))
    return files


def is_test_module(file: Path) -> bool:
    name = file.name
    return (".test." in name or ".spec." in name
            or "/test/" in file.as_posix() or "dev-fixtures" in file.as_posix())


def collect_called_paths(files: Iterable[tuple[Path, str]], generated: dict[str, str],
                         symbols: dict[str, set[str]]) -> set[str]:
    """Normalized paths reachable from the given modules."""
    called: set[str] = set()
    symbol_names = sorted(symbols, key=len, reverse=True)
    member_symbols = [s for s in symbol_names if "." in s]
    function_symbols = [s for s in symbol_names if "." not in s]

    for _, source in files:
        for match in re.finditer(r"UI_API_ROUTES\.(\w+)", source):
            route = generated.get(match.group(1))
            if route:
                called.add(normalize(route))
        for match in LITERAL_PATH_RE.finditer(source):
            raw = match.group(1)
            called.add(normalize(raw))
            # Template literals are captured up to the first interpolation; the
            # static head still identifies a parameterized route.
            head = raw.split("${", 1)[0]
            if head != raw and head.strip("/"):
                called.add(normalize(head.rstrip("/") + "/{}"))
                called.add(normalize(head))
        for symbol in member_symbols:
            if symbol in source:
                called.update(normalize(path) for path in symbols[symbol])
        for symbol in function_symbols:
            if re.search(r"\b" + re.escape(symbol) + r"\s*\(", source):
                called.update(normalize(path) for path in symbols[symbol])
        # Call sites routinely compose a registry symbol with extra segments
        # (`${WORKSTATION_API_ENDPOINTS.data}/replacement-cost`). Resolving the
        # template is the only way to see the composed path.
        for match in TEMPLATE_LITERAL_RE.finditer(source):
            for resolved in _resolve_ts_expression("`" + match.group(1) + "`", generated, symbols) or ():
                if resolved.startswith("/"):
                    called.add(normalize(resolved))
    return called


def collect_wpf_paths(generated: dict[str, str]) -> set[str]:
    called: set[str] = set()
    if not WPF_ROOT.exists():
        return called
    for file in WPF_ROOT.rglob("*"):
        if not file.is_file() or file.suffix not in {".cs", ".xaml"}:
            continue
        source = read_text(file)
        for match in re.finditer(r"UiApiRoutes\.(\w+)", source):
            route = generated.get(match.group(1))
            if route:
                called.add(normalize(route))
        for match in re.finditer(r'"(/(?:api|health|hooks|workstation)[^"\s]*)"', source):
            called.add(normalize(match.group(1)))
    return called


# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------

def classify(inventory: Sequence[dict], called_by_app: set[str], called_by_registry: set[str],
             called_by_tests: set[str], called_by_wpf: set[str]) -> list[dict]:
    app_index = index_called(called_by_app)
    registry_index = index_called(called_by_registry)
    test_index = index_called(called_by_tests)
    wpf_index = index_called(called_by_wpf)

    classified = []
    for route in inventory:
        key = normalize(route["path"])
        if route_is_called(key, called_by_app, app_index):
            state = "wired"
        elif route_is_called(key, called_by_registry, registry_index):
            state = "registry-only"
        else:
            state = "unwired"
        classified.append({
            **route,
            "state": state,
            "excluded_reason": excluded_reason(route["path"]) or route.get("obsolete_reason"),
            "test_only_reference": state != "wired" and route_is_called(key, called_by_tests, test_index),
            "called_by_wpf": route_is_called(key, called_by_wpf, wpf_index),
        })
    return classified


def render_markdown(classified: Sequence[dict], unresolved: Sequence[dict],
                    unresolved_helpers: Sequence[str]) -> str:
    total = len(classified)
    counts = defaultdict(int)
    for route in classified:
        counts[route["state"]] += 1

    actionable = [r for r in classified if r["state"] != "wired" and not r["excluded_reason"]]
    excluded = [r for r in classified if r["state"] != "wired" and r["excluded_reason"]]

    lines = [
        "# UI Route Wiring Report",
        "",
        "<!-- Generated by build/scripts/docs/generate-ui-route-wiring-report.py. Do not edit by hand. -->",
        "",
        "Backend HTTP routes compared against browser-workstation call sites. Both sides are",
        "resolved symbolically: backend routes through `UiApiRoutes` constants and `MapGroup`",
        "prefixes, dashboard call sites through the endpoint registry modules.",
        "",
        "**How to read this.** `wired` means a non-registry dashboard module resolves to the",
        "path — a reference, not proof that a rendered screen reaches it, so a client wrapper",
        "with no caller still counts as wired. `registry-only` means the endpoint registry",
        "declares the path and nothing else mentions it. `unwired` means no dashboard module",
        "references it at all. Comments are stripped before scanning, so a route named in a doc",
        "comment is not mistaken for a call.",
        "",
        "A route is *excluded by design* when it is not the browser's to call: probes and",
        "webhooks, `410 Gone` tombstones, desktop handoffs, and superseded contracts a file",
        "deliberately keeps mapped behind a `#pragma warning disable CS0618` suppression, whose",
        "canonical replacement is mapped over the same service. Each exclusion is listed with",
        "its reason rather than dropped.",
        "",
        "## Summary",
        "",
        "| Metric | Count |",
        "| --- | ---: |",
        f"| Mapped backend routes (path + verb) | {total} |",
        f"| Wired — called by a dashboard module | {counts['wired']} |",
        f"| Registry-only — declared in the endpoint registry, no caller | {counts['registry-only']} |",
        f"| Unwired — no dashboard reference at all | {counts['unwired']} |",
        f"| Actionable (unwired or registry-only, not excluded by design) | {len(actionable)} |",
        f"| Excluded by design (probes, webhooks, tombstones, desktop-only) | {len(excluded)} |",
        "",
    ]

    lines += ["## Actionable routes", ""]
    if not actionable:
        lines += ["Every operator-facing backend route has a dashboard call site.", ""]
    else:
        by_file: dict[str, list[dict]] = defaultdict(list)
        for route in actionable:
            by_file[route["files"][0]].append(route)
        for file in sorted(by_file, key=lambda f: (-len(by_file[f]), f)):
            lines += [f"### `{file}` ({len(by_file[file])})", "",
                      "| Method | Route | State | Notes |", "| --- | --- | --- | --- |"]
            for route in sorted(by_file[file], key=lambda r: (r["path"], r["method"])):
                notes = []
                if route["called_by_wpf"]:
                    notes.append("called by WPF")
                if route["test_only_reference"]:
                    notes.append("dashboard tests only")
                lines.append(
                    f"| {route['method']} | `{route['path']}` | {route['state']} | {', '.join(notes) or '—'} |")
            lines.append("")

    lines += ["## Excluded by design", "",
              "| Method | Route | Reason |", "| --- | --- | --- |"]
    for route in sorted(excluded, key=lambda r: (r["path"], r["method"])):
        lines.append(f"| {route['method']} | `{route['path']}` | {route['excluded_reason']} |")
    lines.append("")

    if unresolved_helpers:
        lines += ["## Unresolved endpoint-registry helpers", "",
                  "Helpers whose return value could not be folded to a path. Routes they build",
                  "may be listed as actionable even though a screen calls them.", ""]
        lines += [f"- `{helper}`" for helper in unresolved_helpers]
        lines.append("")

    if unresolved:
        lines += ["## Unresolved route expressions", "",
                  "Map calls whose route argument the analyzer could not fold to a literal.",
                  "", "| Source | Method | Expression |", "| --- | --- | --- |"]
        for item in sorted(unresolved, key=lambda i: (i["file"], i["expression"])):
            lines.append(f"| `{item['file']}` | {item['method']} | `{item['expression']}` |")
        lines.append("")

    return "\n".join(lines)


def _display_path(path: Path) -> str:
    """Repo-relative when possible; an output directory outside the repo is valid."""
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output", type=Path, default=REPO_ROOT / "docs/status/ui-route-wiring-report.md")
    parser.add_argument("--json-output", type=Path,
                        default=REPO_ROOT / "docs/status/ui-route-wiring-report.json")
    parser.add_argument("--summary", action="store_true",
                        help="Print the summary counts without writing report files.")
    parser.add_argument("--fail-on-unresolved", action="store_true",
                        help="Exit non-zero when a map call's route argument cannot be resolved.")
    args = parser.parse_args(argv)

    constants = load_route_constants()
    inventory, unresolved = collect_backend_routes(constants)

    generated = load_generated_routes()
    symbols = build_registry_symbols(generated)
    unresolved_helpers = unresolved_registry_helpers(symbols)

    files = dashboard_files()
    registry_files = [(f, s) for f, s in files if f.name in REGISTRY_MODULES]
    app_files = [(f, s) for f, s in files if f.name not in REGISTRY_MODULES and not is_test_module(f)]
    test_files = [(f, s) for f, s in files if is_test_module(f)]

    called_by_app = collect_called_paths(app_files, generated, symbols)
    called_by_registry = collect_called_paths(registry_files, generated, symbols)
    called_by_tests = collect_called_paths(test_files, generated, symbols)
    called_by_wpf = collect_wpf_paths(generated)

    classified = classify(inventory, called_by_app, called_by_registry, called_by_tests, called_by_wpf)

    counts = defaultdict(int)
    for route in classified:
        counts[route["state"]] += 1
    actionable = sum(1 for r in classified if r["state"] != "wired" and not r["excluded_reason"])

    if args.summary:
        print(f"routes={len(classified)} wired={counts['wired']} "
              f"registry-only={counts['registry-only']} unwired={counts['unwired']} "
              f"actionable={actionable} unresolved={len(unresolved)} "
              f"unresolved-helpers={len(unresolved_helpers)}")
    else:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(render_markdown(classified, unresolved, unresolved_helpers), encoding="utf-8")
        args.json_output.write_text(
            json.dumps({"routes": classified, "unresolved": unresolved,
                        "unresolved_registry_helpers": unresolved_helpers}, indent=2) + "\n",
            encoding="utf-8")
        print(f"Wrote {_display_path(args.output)} and "
              f"{_display_path(args.json_output)} ({actionable} actionable routes).")

    if args.fail_on_unresolved and unresolved:
        print(f"error: {len(unresolved)} unresolved route expression(s).", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
