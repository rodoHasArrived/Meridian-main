#!/usr/bin/env python3
"""Render source-backed HTTP declaration reference without executing the application.

This complements the hand-authored API contract sections. It records literal
minimal-API declarations, including relative declarations whose caller supplies
a group. It deliberately makes no runtime registration or authorization claim.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass, replace
import importlib.util
from pathlib import Path
import re
import sys
from urllib.parse import quote


REPOSITORY = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPOSITORY))

# Reuse Meridian's C# lexer: strings/comments are not executable declarations,
# and balanced token pairs keep nested lambda bodies inside their own mapping.
from tools.schema_control.contracts import (  # noqa: E402
    _format_tokens,
    _matching_pairs,
    _split_top_level,
    _string_literal_value,
    _tokenize,
)


def _load_coverage_helpers():
    path = Path(__file__).with_name("generate-coverage.py")
    spec = importlib.util.spec_from_file_location("endpoint_reference_route_helpers", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load route helpers: {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


routes = _load_coverage_helpers()
BEGIN = "<!-- BEGIN GENERATED HTTP DECLARATION REFERENCE -->"
END = "<!-- END GENERATED HTTP DECLARATION REFERENCE -->"
METHODS = {f"Map{verb.title()}": verb for verb in ("GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS")}


@dataclass(frozen=True)
class EndpointDeclaration:
    method: str
    route: str
    receiver: str
    relative: bool
    source: str
    line: int
    handler: str
    inputs: tuple[str, ...]
    responses: tuple[str, ...]
    metadata: tuple[str, ...]
    mounted_routes: tuple[str, ...] = ()


def _owner(tokens) -> str:
    return next((tokens[n + 1].text for n, token in enumerate(tokens[:-1]) if token.text == "class"), "")


def resolve_group_arguments(root: Path, constants: dict[str, str]) -> dict[tuple[str, str, str], set[str]]:
    """Follow local mapping-method calls, including calls across partial files.

    Only literal/known-constant MapGroup calls and named group arguments are
    followed. Dynamic expressions stay unresolved rather than guessing a URL.
    Multiple mounts (for example risk's versioned alias) are preserved.
    """
    files = []
    definitions = {}
    calls = []
    for path in routes._collect_files(root / "src", (".cs",)):
        text = path.read_text(encoding="utf-8")
        if "MapGroup" not in text and "RouteGroupBuilder" not in text:
            continue
        tokens = _tokenize(text)
        pairs = _matching_pairs(tokens)
        owner = _owner(tokens)
        local_constants = dict(constants)
        local_constants.update(dict(routes.ROUTE_CONST_RE.findall(text)))
        prefixes = routes._group_prefixes(text, local_constants)
        methods = []
        for n, token in enumerate(tokens[:-1]):
            if not token.text.startswith("Map") or tokens[n + 1].text != "(" or n + 1 not in pairs:
                continue
            close = pairs[n + 1]
            if close + 1 >= len(tokens) or tokens[close + 1].text != "{":
                continue
            parameters = _split_top_level(tokens[n + 2:close], ",")
            group_parameters = {}
            for position, parameter in enumerate(parameters):
                if any(value.text == "RouteGroupBuilder" for value in parameter):
                    group_parameters[position] = parameter[-1].text
            end = pairs.get(close + 1, close + 1)
            methods.append((token.text, token.start, tokens[end].end, group_parameters))
            if group_parameters:
                definitions[(owner, token.text)] = group_parameters
        files.append((owner, tokens, pairs, prefixes, local_constants, methods))
    for owner, tokens, pairs, prefixes, local_constants, methods in files:
        for n, token in enumerate(tokens[:-1]):
            if (owner, token.text) not in definitions or tokens[n + 1].text != "(" or n + 1 not in pairs:
                continue
            close = pairs[n + 1]
            if close + 1 < len(tokens) and tokens[close + 1].text == "{":
                continue
            if n and tokens[n - 1].text == ".":
                continue
            caller = next((method for method in methods if method[1] < token.start < method[2]), None)
            arguments = _split_top_level(tokens[n + 2:close], ",")
            calls.append((owner, token.text, caller, token.start, arguments, prefixes, local_constants))

    mounts: dict[tuple[str, str, str], set[str]] = {}
    def values(owner, caller, position, expression, prefixes, local_constants):
        if not expression:
            return set()
        if len(expression) == 1 and expression[0].kind == "identifier":
            name = expression[0].text
            local = [value for value in prefixes if value[1] == name and (caller is None or caller[1] < value[0])]
            prefix = routes._lookup_prefix(local, name, position)
            if prefix:
                return {prefix}
            return mounts.get((owner, caller[0], name), set()) if caller else set()
        for n, token in enumerate(expression[:-2]):
            if token.text != "MapGroup" or expression[n + 1].text != "(":
                continue
            value = expression[n + 2]
            own = _string_literal_value(value.text) if value.kind == "string" else local_constants.get(value.text)
            if own is None:
                return set()
            parent = values(owner, caller, position, expression[:n - 1], prefixes, local_constants) if n >= 2 else set()
            return {routes._join_route(prefix, own) for prefix in parent or {""}}
        return set()

    # Every acyclic call path has at most one edge per mapping method. Bound the
    # propagation so recursive registration helpers cannot expand forever.
    for _ in range(len(definitions) + 1):
        changed = False
        for owner, callee, caller, position, arguments, prefixes, local_constants in calls:
            for argument_index, parameter in definitions[(owner, callee)].items():
                if argument_index >= len(arguments):
                    continue
                resolved = values(owner, caller, position, arguments[argument_index], prefixes, local_constants)
                key = (owner, callee, parameter)
                previous = mounts.setdefault(key, set())
                additions = resolved - previous
                if additions:
                    previous.update(additions)
                    changed = True
        if not changed:
            break
    return mounts


def _calls(tokens, name: str) -> list[str]:
    """Exact call expressions, preserving generic arguments and nested arguments."""
    pairs = _matching_pairs(tokens)
    calls: list[str] = []
    for index, token in enumerate(tokens):
        if token.text != name:
            continue
        cursor = index + 1
        if cursor < len(tokens) and tokens[cursor].text == "<":
            depth = 1
            cursor += 1
            while cursor < len(tokens) and depth:
                depth += (tokens[cursor].text == "<") - (tokens[cursor].text == ">")
                cursor += 1
        if cursor < len(tokens) and tokens[cursor].text == "(" and cursor in pairs:
            calls.append(_format(tokens[index:pairs[cursor] + 1]))
    return list(dict.fromkeys(calls))


def _format(tokens) -> str:
    """Normalize C# spacing outside literals while preserving literal contents."""
    literals = {}
    masked = []
    for token in tokens:
        if token.kind == "string":
            marker = f"__endpoint_reference_literal_{len(literals)}__"
            literals[marker] = token.text
            masked.append(replace(token, text=marker))
        else:
            masked.append(token)
    value = _format_tokens(masked)
    value = re.sub(r"\?([A-Za-z_])", r"? \1", value)
    value = re.sub(r"([A-Za-z_][A-Za-z_0-9]*) +\(", r"\1(", value).strip().replace("async(", "async (")
    return re.sub(r"__endpoint_reference_literal_\d+__", lambda match: literals[match.group()], value)


def extract_file(root: Path, path: Path, constants: dict[str, str], mounts=None) -> list[EndpointDeclaration]:
    text = path.read_text(encoding="utf-8")
    if not any(f".{name}" in text for name in METHODS):
        return []
    local_constants = dict(constants)
    local_constants.update(dict(routes.ROUTE_CONST_RE.findall(text)))
    prefixes = routes._group_prefixes(text, local_constants)
    tokens = _tokenize(text)
    pairs = _matching_pairs(tokens)
    owner = _owner(tokens)
    methods = []
    for n, value in enumerate(tokens[:-1]):
        if not value.text.startswith("Map") or tokens[n + 1].text != "(" or n + 1 not in pairs:
            continue
        close = pairs[n + 1]
        if close + 1 < len(tokens) and tokens[close + 1].text == "{":
            methods.append((value.text, value.start, tokens[pairs.get(close + 1, close + 1)].end))
    result = []
    for index in range(1, len(tokens) - 3):
        token = tokens[index]
        if token.text not in METHODS or tokens[index - 1].text != ".":
            continue
        opening = index + 1
        if tokens[opening].text != "(" or opening not in pairs:
            continue
        route_token = tokens[opening + 1]
        if route_token.kind != "string":
            continue
        literal = _string_literal_value(route_token.text)
        if literal is None or tokens[opening + 2].text != ",":
            continue
        closing = pairs[opening]
        # A fluent chain can continue beyond MapGet(...). Walk only balanced
        # expressions until this mapping's statement terminator.
        end = closing + 1
        while end < len(tokens) and tokens[end].text not in {";", "}"}:
            if tokens[end].text in {"(", "[", "{"} and end in pairs:
                end = pairs[end] + 1
            else:
                end += 1
        statement = tokens[index:end]
        handler_tokens = tokens[opening + 3:closing]
        arrow = next((n for n, value in enumerate(handler_tokens) if value.text == "=>"), None)
        if arrow is not None:
            handler = _format(handler_tokens[:arrow]) + " => …"
        else:
            handler = _format(handler_tokens)
        receiver = tokens[index - 2].text if index >= 2 and tokens[index - 2].kind == "identifier" else "expression"
        enclosing_method = next((value for value in methods if value[1] < token.start < value[2]), None)
        local_prefixes = [value for value in prefixes if (enclosing_method is None or enclosing_method[1] < value[0]) and value[0] < token.start]
        prefix = routes._lookup_prefix(local_prefixes, receiver, token.start)
        locally_bound = any(value[1] == receiver for value in local_prefixes)
        relative = not locally_bound and receiver not in {"app", "_app", "endpoints", "routes", "builder"}
        route = routes._join_route(prefix, literal)
        method = enclosing_method[0] if enclosing_method else ""
        inherited = (mounts or {}).get((owner, method, receiver), set()) if relative else set()
        mounted_routes = tuple(sorted(routes._join_route(value, literal) for value in inherited))
        inputs = []
        for name in ("Deserialize", "DeserializeAsync", "ReadFromJsonAsync", "ReadFormAsync"):
            for call in _calls(handler_tokens, name):
                # Keep the type and operation; argument expressions can contain
                # arbitrary request-processing code and are not a wire schema.
                inputs.append(call.split("(", 1)[0].strip())
        responses = []
        for name in ("Produces", "ProducesProblem", "ProducesValidationProblem", "ProducesFile"):
            responses.extend(_calls(statement, name))
        factories = []
        for n, value in enumerate(handler_tokens[:-2]):
            if value.text in {"Results", "TypedResults"} and handler_tokens[n + 1].text == ".":
                factories.append(value.text + "." + handler_tokens[n + 2].text)
        responses.extend(dict.fromkeys(factories))
        metadata = []
        for name in ("WithName", "WithSummary", "WithDescription"):
            metadata.extend(_calls(tokens[closing + 1:end], name))
        result.append(EndpointDeclaration(
            method=METHODS[token.text], route=route or "/", receiver=receiver,
            relative=relative, source=path.relative_to(root).as_posix(),
            line=token.line, handler=handler, inputs=tuple(dict.fromkeys(inputs)),
            responses=tuple(dict.fromkeys(responses)), metadata=tuple(metadata),
            mounted_routes=mounted_routes,
        ))
    return result


def collect(root: Path) -> list[EndpointDeclaration]:
    constants = routes._load_route_constants(root)
    mounts = resolve_group_arguments(root, constants)
    result = []
    for path in routes._collect_files(root / "src", (".cs",)):
        result.extend(extract_file(root, path, constants, mounts))
    return result


def _code(value: str) -> str:
    return "`" + value.replace("`", "'").replace("|", "&#124;").replace("\n", " ") + "`"


def render(declarations: list[EndpointDeclaration]) -> str:
    lines = [
        BEGIN,
        "## HTTP declaration reference",
        "",
        "Generated by `python3 build/scripts/docs/render-endpoint-reference.py`; verify without writing with `--check`.",
        "",
        "This source reference records literal `MapGet`, `MapPost`, `MapPut`, `MapDelete`, `MapPatch`, `MapHead`, and `MapOptions` declarations. "
        "It preserves separate HTTP methods on the same path. Group prefixes declared locally are composed and route constraints are normalized "
        "(`{id:guid}` becomes `{id}`); inspect the linked source for the original constraints.",
        "",
        "**Relative declaration** means a mapping method receives its group from a caller. Its displayed path is a fragment, not a standalone URL. "
        "Where local mapping-method calls can be followed, caller-derived paths are shown as mounts; versioned aliases are preserved. "
        "The receiver and source identify the registration context; dynamic callers may add other mounts. "
        "Constant-based route declarations are outside this literal-declaration reference. This is a lexical source reference, not a runtime route inventory, "
        "authorization specification, or proof that a host registers an endpoint.",
        "",
        "Handler parameters retain C# binding types, including injected services and infrastructure parameters; they are not all request-body fields. "
        "Body-read operations list explicitly decoded types. Response entries quote declared `.Produces*` metadata and observed `Results`/`TypedResults` factories, "
        "not an exhaustive response schema or status guarantee. A dash means the declaration supplies no extractable metadata; follow the source and detailed contracts above.",
        "",
        f"**{len(declarations)} declarations** from **{len({item.source for item in declarations})} source files**.",
        "",
    ]
    previous_source = None
    for item in declarations:
        if item.source != previous_source:
            lines.extend([
                "", f"### {Path(item.source).stem}", "",
                f"Source: [{item.source}](../../{quote(item.source)})", "",
                "| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |",
                "| --- | --- | --- | --- | --- | --- |",
            ])
            previous_source = item.source
        route = _code(item.route)
        if item.relative:
            route += f" (relative declaration on {_code(item.receiver)})"
        if item.mounted_routes:
            route += "<br>Caller-derived mounts: " + "; ".join(_code(value) for value in item.mounted_routes)
        handler = _code(item.handler)
        if item.inputs:
            handler += "<br>Body reads: " + "; ".join(_code(value) for value in item.inputs)
        responses = "; ".join(_code(value) for value in item.responses) or "—"
        metadata = "; ".join(_code(value) for value in item.metadata) or "—"
        source = f"[L{item.line}](../../{quote(item.source)}#L{item.line})"
        lines.append(f"| {route} | {item.method} | {handler} | {responses} | {metadata} | {source} |")
    lines.extend(["", END])
    return "\n".join(lines)


def update_document(existing: str, generated: str) -> str:
    if existing.count(BEGIN) != existing.count(END) or existing.count(BEGIN) > 1:
        raise ValueError("HTTP declaration reference markers must form one ordered pair")
    if BEGIN in existing:
        start, end = existing.index(BEGIN), existing.index(END)
        if end < start:
            raise ValueError("HTTP declaration reference markers are reversed")
        return existing[:start] + generated + existing[end + len(END):]
    return existing.rstrip() + "\n\n" + generated + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=REPOSITORY)
    parser.add_argument("--check", action="store_true", help="Reject stale output without writing")
    args = parser.parse_args(argv)
    root = args.root.resolve()
    output = root / "docs/reference/api-reference.md"
    try:
        existing = output.read_text(encoding="utf-8")
        declarations = collect(root)
        if not declarations:
            raise ValueError("No literal HTTP declarations found")
        updated = update_document(existing, render(declarations))
        if args.check:
            if updated != existing:
                print(f"Stale HTTP declaration reference: {output}", file=sys.stderr)
                return 1
            print(f"HTTP declaration reference current ({len(declarations)} declarations)")
        else:
            output.write_text(updated, encoding="utf-8")
            print(f"Rendered {len(declarations)} HTTP declarations to {output}")
    except (OSError, ValueError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
