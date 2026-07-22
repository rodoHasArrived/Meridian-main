"""Deterministic source-level cataloging for Meridian C# contract types.

The catalog deliberately records module-level database schema associations only.
It does not assert that a C# type, DTO, or member is structurally equivalent to a
database table or column.  Such relationships require an explicit mapping layer.
"""

from __future__ import annotations

from dataclasses import dataclass, field
import hashlib
import json
from pathlib import Path
import re
from typing import Any, Iterable, Sequence


MANIFEST_SCHEMA = "meridian.contract-manifest"
MANIFEST_VERSION = "1.0.0"
MAPPING_NOTE = (
    "Schema associations are module-level only; no DTO-to-table or "
    "member-to-column structural equivalence is claimed."
)

_TYPE_KINDS = {"class", "enum", "interface", "record", "struct"}
_TYPE_MODIFIERS = {
    "abstract",
    "file",
    "new",
    "partial",
    "readonly",
    "ref",
    "sealed",
    "static",
    "unsafe",
}
_MEMBER_MODIFIERS = {
    "abstract",
    "new",
    "override",
    "required",
    "sealed",
    "static",
    "unsafe",
    "virtual",
}
_PARAMETER_MODIFIERS = {"in", "out", "params", "ref", "scoped", "this"}
_COLLECTION_TYPES = {
    "Collection",
    "HashSet",
    "IAsyncEnumerable",
    "ICollection",
    "IEnumerable",
    "IImmutableList",
    "IList",
    "ImmutableArray",
    "IReadOnlyCollection",
    "IReadOnlyList",
    "List",
    "ObservableCollection",
    "Queue",
    "ReadOnlyCollection",
    "ReadOnlyMemory",
    "Set",
}
_MAP_TYPES = {
    "ConcurrentDictionary",
    "Dictionary",
    "IDictionary",
    "IReadOnlyDictionary",
    "ImmutableDictionary",
    "SortedDictionary",
}
_IDENTIFIER_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
_CONTRACT_SET_ID_RE = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
_REFERENCE_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*")


@dataclass(frozen=True)
class _Token:
    text: str
    start: int
    end: int
    line: int
    kind: str = "symbol"


@dataclass
class _ParsedType:
    name: str
    namespace: str
    kind: str
    source: dict[str, Any]
    sources: list[dict[str, Any]]
    partial: bool
    type_parameters: list[str]
    base_types: list[str]
    members: list[dict[str, Any]]
    enum_members: list[dict[str, Any]]
    start_token: int
    end_token: int
    body_start_token: int | None
    body_end_token: int | None
    containing_type: str | None = None
    contract_sets: list[str] = field(default_factory=list)
    mapped_schemas: list[str] = field(default_factory=list)
    diagram_contract_sets: list[str] = field(default_factory=list)

    @property
    def qualified_name(self) -> str:
        local_name = self.name
        if self.containing_type:
            local_name = f"{self.containing_type}.{local_name}"
        return f"{self.namespace}.{local_name}" if self.namespace else local_name


@dataclass(frozen=True)
class _ContractSet:
    id: str
    directories: tuple[Path, ...]
    directory_labels: tuple[str, ...]
    namespace_prefixes: tuple[str, ...]
    schemas: tuple[str, ...]
    diagram: bool


def _canonical_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True)


def _fingerprint(value: Any) -> str:
    return hashlib.sha256(_canonical_json(value).encode("utf-8")).hexdigest()


def _is_identifier(token: _Token) -> bool:
    return token.kind == "identifier"


def _consume_quoted(source: str, start: int) -> int:
    """Return the exclusive end of a C# string or character literal."""

    cursor = start
    while cursor < len(source) and source[cursor] in "$@":
        cursor += 1
    if cursor >= len(source) or source[cursor] not in {'"', "'"}:
        return start + 1

    quote = source[cursor]
    quote_count = 1
    if quote == '"':
        while (
            cursor + quote_count < len(source) and source[cursor + quote_count] == '"'
        ):
            quote_count += 1

    raw_string = quote == '"' and quote_count >= 3
    verbatim = "@" in source[start:cursor]
    # Two adjacent quotes are an empty ordinary/verbatim string, not a raw
    # delimiter. Consume only the opening quote so the normal loop sees and
    # closes on the second quote.
    cursor += quote_count if raw_string else 1

    if raw_string:
        terminator = '"' * quote_count
        close = source.find(terminator, cursor)
        return len(source) if close < 0 else close + quote_count

    while cursor < len(source):
        char = source[cursor]
        if char == "\\" and not verbatim:
            cursor += 2
            continue
        if char == quote:
            if (
                verbatim
                and quote == '"'
                and cursor + 1 < len(source)
                and source[cursor + 1] == '"'
            ):
                cursor += 2
                continue
            return cursor + 1
        cursor += 1
    return len(source)


def _tokenize(source: str) -> list[_Token]:
    tokens: list[_Token] = []
    cursor = 0
    line = 1
    length = len(source)

    while cursor < length:
        char = source[cursor]
        if char.isspace():
            if char == "\n":
                line += 1
            cursor += 1
            continue

        if source.startswith("//", cursor):
            close = source.find("\n", cursor + 2)
            if close < 0:
                break
            cursor = close
            continue

        if source.startswith("/*", cursor):
            close = source.find("*/", cursor + 2)
            close = length if close < 0 else close + 2
            line += source.count("\n", cursor, close)
            cursor = close
            continue

        quote_cursor = cursor
        while quote_cursor < length and source[quote_cursor] in "$@":
            quote_cursor += 1
        if quote_cursor < length and source[quote_cursor] in {'"', "'"}:
            end = _consume_quoted(source, cursor)
            text = source[cursor:end]
            tokens.append(_Token(text, cursor, end, line, "string"))
            line += text.count("\n")
            cursor = end
            continue

        if char.isalpha() or char == "_":
            end = cursor + 1
            while end < length and (source[end].isalnum() or source[end] == "_"):
                end += 1
            tokens.append(_Token(source[cursor:end], cursor, end, line, "identifier"))
            cursor = end
            continue

        if char.isdigit():
            end = cursor + 1
            while end < length and (source[end].isalnum() or source[end] in "._"):
                end += 1
            tokens.append(_Token(source[cursor:end], cursor, end, line, "number"))
            cursor = end
            continue

        operator = next(
            (
                candidate
                for candidate in ("=>", "::", "??", "?.", "==", "!=", "<=", ">=")
                if source.startswith(candidate, cursor)
            ),
            None,
        )
        if operator:
            tokens.append(_Token(operator, cursor, cursor + len(operator), line))
            cursor += len(operator)
            continue

        tokens.append(_Token(char, cursor, cursor + 1, line))
        cursor += 1

    return tokens


def _matching_pairs(tokens: Sequence[_Token]) -> dict[int, int]:
    opening = {"(": ")", "[": "]", "{": "}"}
    stacks: dict[str, list[int]] = {key: [] for key in opening}
    result: dict[int, int] = {}
    reverse = {value: key for key, value in opening.items()}
    for index, token in enumerate(tokens):
        if token.text in opening:
            stacks[token.text].append(index)
        elif token.text in reverse:
            opener = reverse[token.text]
            if stacks[opener]:
                start = stacks[opener].pop()
                result[start] = index
                result[index] = start
    return result


def _format_tokens(tokens: Sequence[_Token]) -> str:
    if not tokens:
        return ""
    value = " ".join(token.text for token in tokens).strip()
    value = re.sub(r"\s*::\s*", "::", value)
    value = re.sub(r"\s*\.\s*", ".", value)
    value = re.sub(r"\s*([<>\[\]?])\s*", r"\1", value)
    value = re.sub(r"\s*,\s*", ", ", value)
    value = re.sub(r"\(\s+", "(", value)
    value = re.sub(r"\s+\)", ")", value)
    value = re.sub(r"\s+", " ", value)
    return value.strip()


def _split_top_level(tokens: Sequence[_Token], delimiter: str) -> list[list[_Token]]:
    groups: list[list[_Token]] = []
    current: list[_Token] = []
    depths = {"(": 0, "[": 0, "{": 0, "<": 0}
    close_to_open = {")": "(", "]": "[", "}": "{", ">": "<"}

    for token in tokens:
        if token.text == delimiter and all(depth == 0 for depth in depths.values()):
            groups.append(current)
            current = []
            continue
        if token.text in depths:
            depths[token.text] += 1
        elif token.text in close_to_open:
            opener = close_to_open[token.text]
            depths[opener] = max(0, depths[opener] - 1)
        current.append(token)
    groups.append(current)
    return groups


def _split_enum_members(tokens: Sequence[_Token]) -> list[list[_Token]]:
    """Split enum members without treating bit-shift operators as generics."""

    groups: list[list[_Token]] = []
    current: list[_Token] = []
    depths = {"(": 0, "[": 0, "{": 0}
    close_to_open = {")": "(", "]": "[", "}": "{"}
    for token in tokens:
        if token.text == "," and all(depth == 0 for depth in depths.values()):
            groups.append(current)
            current = []
            continue
        if token.text in depths:
            depths[token.text] += 1
        elif token.text in close_to_open:
            opener = close_to_open[token.text]
            depths[opener] = max(0, depths[opener] - 1)
        current.append(token)
    groups.append(current)
    return groups


def _string_literal_value(raw: str) -> str | None:
    prefix_length = 0
    while prefix_length < len(raw) and raw[prefix_length] in "$@":
        prefix_length += 1
    quoted = raw[prefix_length:]
    if len(quoted) < 2 or not quoted.startswith('"'):
        return None
    if quoted.startswith('"""'):
        quote_count = len(quoted) - len(quoted.lstrip('"'))
        return quoted[quote_count:-quote_count]
    value = quoted[1:-1]
    if "@" in raw[:prefix_length]:
        return value.replace('""', '"')
    try:
        return json.loads(quoted)
    except (json.JSONDecodeError, TypeError):
        return value.replace('\\"', '"').replace("\\\\", "\\")


def _attribute_metadata(tokens: Sequence[_Token]) -> tuple[str | None, bool]:
    json_name: str | None = None
    ignored = False
    for index, token in enumerate(tokens):
        short_name = token.text.rsplit(".", 1)[-1]
        if short_name in {"JsonIgnore", "JsonIgnoreAttribute"}:
            ignored = True
        if short_name not in {"JsonPropertyName", "JsonPropertyNameAttribute"}:
            continue
        for candidate in tokens[index + 1 :]:
            if candidate.kind == "string":
                json_name = _string_literal_value(candidate.text)
                break
            if candidate.text == "]":
                break
    return json_name, ignored


def _strip_attributes(tokens: Sequence[_Token]) -> tuple[list[_Token], list[_Token]]:
    """Remove declaration-leading attributes without consuming array brackets."""

    attributes: list[_Token] = []
    cursor = 0
    while cursor < len(tokens) and tokens[cursor].text == "[":
        depth = 0
        end = cursor
        while end < len(tokens):
            if tokens[end].text == "[":
                depth += 1
            elif tokens[end].text == "]":
                depth -= 1
                if depth == 0:
                    end += 1
                    break
            end += 1
        if depth != 0:
            break
        attributes.extend(tokens[cursor:end])
        cursor = end
    return list(tokens[cursor:]), attributes


def _type_shape(type_tokens: Sequence[_Token]) -> dict[str, Any]:
    raw_type = _format_tokens(type_tokens)
    significant = list(type_tokens)
    nullable = bool(significant and significant[-1].text == "?")
    without_nullable = significant[:-1] if nullable else significant

    collection = False
    collection_kind: str | None = None
    element_type: str | None = None
    key_type: str | None = None

    if (
        len(without_nullable) >= 2
        and without_nullable[-2].text == "["
        and without_nullable[-1].text == "]"
    ):
        collection = True
        collection_kind = "array"
        element_type = _format_tokens(without_nullable[:-2])
    else:
        angle_index = next(
            (i for i, token in enumerate(without_nullable) if token.text == "<"), None
        )
        if angle_index is not None:
            outer_identifiers = [
                token.text
                for token in without_nullable[:angle_index]
                if _is_identifier(token)
            ]
            outer_name = outer_identifiers[-1] if outer_identifiers else ""
            if without_nullable[-1].text == ">":
                arguments = _split_top_level(
                    without_nullable[angle_index + 1 : -1], ","
                )
                if outer_name in _MAP_TYPES and len(arguments) >= 2:
                    collection = True
                    collection_kind = "map"
                    key_type = _format_tokens(arguments[0])
                    element_type = _format_tokens(arguments[1])
                elif outer_name in _COLLECTION_TYPES and arguments:
                    collection = True
                    collection_kind = outer_name
                    element_type = _format_tokens(arguments[0])

    return {
        "type": raw_type,
        "raw_type": raw_type,
        "nullable": nullable,
        "collection": collection,
        "collection_kind": collection_kind,
        "element_type": element_type,
        "key_type": key_type,
    }


def _namespace_ranges(
    tokens: Sequence[_Token], pairs: dict[int, int]
) -> list[tuple[int, int, str]]:
    declarations: list[tuple[int, int, str, int]] = []
    for index, token in enumerate(tokens):
        if token.text != "namespace":
            continue
        name_tokens: list[_Token] = []
        cursor = index + 1
        while cursor < len(tokens) and tokens[cursor].text not in {";", "{"}:
            name_tokens.append(tokens[cursor])
            cursor += 1
        if cursor >= len(tokens):
            continue
        name = _format_tokens(name_tokens).replace(" ", "")
        if tokens[cursor].text == ";":
            declarations.append((cursor + 1, len(tokens), name, index))
        else:
            close = pairs.get(cursor)
            if close is not None:
                declarations.append((cursor + 1, close, name, index))

    ranges: list[tuple[int, int, str]] = []
    for start, end, name, declaration_index in declarations:
        parents = [
            item
            for item in declarations
            if item[0] <= declaration_index < item[1] and item[3] != declaration_index
        ]
        if parents:
            parent = min(parents, key=lambda item: item[1] - item[0])
            name = f"{parent[2]}.{name}"
        ranges.append((start, end, name))
    return ranges


def _namespace_at(index: int, ranges: Sequence[tuple[int, int, str]]) -> str:
    candidates = [item for item in ranges if item[0] <= index < item[1]]
    if not candidates:
        return ""
    return min(candidates, key=lambda item: item[1] - item[0])[2]


def _find_type_declaration(
    tokens: Sequence[_Token],
    public_index: int,
    pairs: dict[int, int],
) -> dict[str, Any] | None:
    cursor = public_index + 1
    modifiers: list[str] = []
    while cursor < len(tokens) and tokens[cursor].text in _TYPE_MODIFIERS:
        modifiers.append(tokens[cursor].text)
        cursor += 1
    if cursor >= len(tokens) or tokens[cursor].text not in _TYPE_KINDS:
        return None

    base_kind = tokens[cursor].text
    cursor += 1
    kind = base_kind
    if (
        base_kind == "record"
        and cursor < len(tokens)
        and tokens[cursor].text in {"class", "struct"}
    ):
        kind = f"record_{tokens[cursor].text}"
        cursor += 1
    if cursor >= len(tokens) or not _is_identifier(tokens[cursor]):
        return None

    name_token = tokens[cursor]
    cursor += 1
    type_parameters: list[str] = []
    if cursor < len(tokens) and tokens[cursor].text == "<":
        depth = 0
        start = cursor + 1
        while cursor < len(tokens):
            if tokens[cursor].text == "<":
                depth += 1
            elif tokens[cursor].text == ">":
                depth -= 1
                if depth == 0:
                    type_parameters = [
                        _format_tokens(group)
                        for group in _split_top_level(tokens[start:cursor], ",")
                        if group
                    ]
                    cursor += 1
                    break
            cursor += 1

    positional_start: int | None = None
    positional_end: int | None = None
    if base_kind == "record" and cursor < len(tokens) and tokens[cursor].text == "(":
        positional_start = cursor
        positional_end = pairs.get(cursor)
        if positional_end is None:
            return None
        cursor = positional_end + 1

    header_start = cursor
    body_start: int | None = None
    terminator: int | None = None
    paren_depth = 0
    bracket_depth = 0
    angle_depth = 0
    while cursor < len(tokens):
        text = tokens[cursor].text
        if text == "(":
            paren_depth += 1
        elif text == ")":
            paren_depth = max(0, paren_depth - 1)
        elif text == "[":
            bracket_depth += 1
        elif text == "]":
            bracket_depth = max(0, bracket_depth - 1)
        elif text == "<":
            angle_depth += 1
        elif text == ">":
            angle_depth = max(0, angle_depth - 1)
        elif paren_depth == bracket_depth == angle_depth == 0 and text in {"{", ";"}:
            terminator = cursor
            if text == "{":
                body_start = cursor
            break
        cursor += 1
    if terminator is None:
        return None

    body_end = pairs.get(body_start) if body_start is not None else terminator
    if body_start is not None and body_end is None:
        return None

    header_tokens = list(tokens[header_start:terminator])
    where_index = next(
        (i for i, token in enumerate(header_tokens) if token.text == "where"),
        len(header_tokens),
    )
    relevant_header = header_tokens[:where_index]
    colon_index = next(
        (i for i, token in enumerate(relevant_header) if token.text == ":"), None
    )
    base_types: list[str] = []
    if colon_index is not None:
        base_types = [
            _format_tokens(group)
            for group in _split_top_level(relevant_header[colon_index + 1 :], ",")
            if _format_tokens(group)
        ]

    return {
        "name": name_token.text,
        "name_line": name_token.line,
        "kind": kind,
        "partial": "partial" in modifiers,
        "type_parameters": type_parameters,
        "base_types": base_types,
        "positional_start": positional_start,
        "positional_end": positional_end,
        "body_start": body_start,
        "body_end": body_end,
        "end": body_end,
    }


def _positional_members(
    tokens: Sequence[_Token], start: int | None, end: int | None
) -> list[dict[str, Any]]:
    if start is None or end is None:
        return []
    result: list[dict[str, Any]] = []
    for segment in _split_top_level(tokens[start + 1 : end], ","):
        clean, attributes = _strip_attributes(segment)
        equals_index = next(
            (i for i, token in enumerate(clean) if token.text == "="), len(clean)
        )
        declaration = clean[:equals_index]
        default_tokens = clean[equals_index + 1 :] if equals_index < len(clean) else []
        while declaration and declaration[0].text in _PARAMETER_MODIFIERS:
            declaration.pop(0)
        name_index = next(
            (
                i
                for i in range(len(declaration) - 1, -1, -1)
                if _is_identifier(declaration[i])
            ),
            None,
        )
        if name_index is None or name_index == 0:
            continue
        name_token = declaration[name_index]
        type_tokens = declaration[:name_index]
        json_name, json_ignored = _attribute_metadata(attributes)
        member = {
            "name": name_token.text,
            **_type_shape(type_tokens),
            "json_name": json_name,
            "json_ignored": json_ignored,
            "origin": "positional_parameter",
            "source_line": name_token.line,
        }
        if default_tokens:
            member["default"] = _format_tokens(default_tokens)
        result.append(member)
    return result


def _leading_attributes(
    tokens: Sequence[_Token], public_index: int, lower_bound: int
) -> list[_Token]:
    attributes: list[_Token] = []
    cursor = public_index - 1
    while cursor > lower_bound and tokens[cursor].text == "]":
        depth = 1
        end = cursor
        cursor -= 1
        while cursor > lower_bound:
            if tokens[cursor].text == "]":
                depth += 1
            elif tokens[cursor].text == "[":
                depth -= 1
                if depth == 0:
                    attributes[0:0] = list(tokens[cursor : end + 1])
                    cursor -= 1
                    break
            cursor -= 1
    return attributes


def _property_members(
    tokens: Sequence[_Token],
    body_start: int | None,
    body_end: int | None,
    pairs: dict[int, int],
) -> list[dict[str, Any]]:
    if body_start is None or body_end is None:
        return []
    result: list[dict[str, Any]] = []
    depth = 0
    cursor = body_start + 1
    while cursor < body_end:
        token = tokens[cursor]
        if token.text == "{":
            depth += 1
            cursor += 1
            continue
        if token.text == "}":
            depth = max(0, depth - 1)
            cursor += 1
            continue
        if depth != 0 or token.text != "public":
            cursor += 1
            continue

        type_start = cursor + 1
        while type_start < body_end and tokens[type_start].text in _MEMBER_MODIFIERS:
            type_start += 1
        if type_start < body_end and tokens[type_start].text in _TYPE_KINDS:
            cursor += 1
            continue
        scan = type_start
        paren_depth = 0
        bracket_depth = 0
        angle_depth = 0
        accessor_start: int | None = None
        disqualified = False
        while scan < body_end:
            text = tokens[scan].text
            if text == "(" and paren_depth == bracket_depth == angle_depth == 0:
                if scan != type_start:
                    disqualified = True
                    break
                paren_depth += 1
                scan += 1
                continue
            if text in {";", "=>"} and paren_depth == bracket_depth == angle_depth == 0:
                disqualified = True
                break
            if text == "(":
                paren_depth += 1
            elif text == ")":
                paren_depth = max(0, paren_depth - 1)
            elif text == "[":
                bracket_depth += 1
            elif text == "]":
                bracket_depth = max(0, bracket_depth - 1)
            elif text == "<":
                angle_depth += 1
            elif text == ">":
                angle_depth = max(0, angle_depth - 1)
            elif text == "{" and paren_depth == bracket_depth == angle_depth == 0:
                accessor_start = scan
                break
            scan += 1
        if disqualified or accessor_start is None or accessor_start <= type_start:
            cursor += 1
            continue

        accessor_end = pairs.get(accessor_start)
        if accessor_end is None or accessor_end > body_end:
            cursor += 1
            continue
        accessor_words = {
            candidate.text
            for candidate in tokens[accessor_start + 1 : accessor_end]
            if _is_identifier(candidate)
        }
        if not accessor_words.intersection({"get", "init", "set"}):
            cursor = accessor_end + 1
            continue

        name_token = tokens[accessor_start - 1]
        if not _is_identifier(name_token) or name_token.text == "this":
            cursor = accessor_end + 1
            continue
        type_tokens = tokens[type_start : accessor_start - 1]
        if not type_tokens:
            cursor = accessor_end + 1
            continue
        attributes = _leading_attributes(tokens, cursor, body_start)
        json_name, json_ignored = _attribute_metadata(attributes)
        result.append(
            {
                "name": name_token.text,
                **_type_shape(type_tokens),
                "json_name": json_name,
                "json_ignored": json_ignored,
                "origin": "property",
                "source_line": name_token.line,
            }
        )
        cursor = accessor_end + 1
    return result


def _enum_members(
    tokens: Sequence[_Token],
    body_start: int | None,
    body_end: int | None,
) -> list[dict[str, Any]]:
    if body_start is None or body_end is None:
        return []
    result: list[dict[str, Any]] = []
    for segment in _split_enum_members(tokens[body_start + 1 : body_end]):
        clean, _ = _strip_attributes(segment)
        name_index = next(
            (i for i, token in enumerate(clean) if _is_identifier(token)), None
        )
        if name_index is None:
            continue
        name_token = clean[name_index]
        equals_index = next(
            (
                i
                for i, token in enumerate(clean[name_index + 1 :], name_index + 1)
                if token.text == "="
            ),
            None,
        )
        value = (
            _format_tokens(clean[equals_index + 1 :])
            if equals_index is not None
            else None
        )
        result.append(
            {
                "name": name_token.text,
                "value": value or None,
                "explicit_value": equals_index is not None,
                "source_line": name_token.line,
            }
        )
    return result


def _relative_path(root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return Path(re.sub(r"\\", "/", str(path.resolve()))).as_posix()


def _parse_file(root: Path, path: Path) -> list[_ParsedType]:
    source = path.read_text(encoding="utf-8-sig")
    tokens = _tokenize(source)
    pairs = _matching_pairs(tokens)
    namespaces = _namespace_ranges(tokens, pairs)
    relative_path = _relative_path(root, path)
    parsed: list[_ParsedType] = []

    for index, token in enumerate(tokens):
        if token.text != "public":
            continue
        declaration = _find_type_declaration(tokens, index, pairs)
        if declaration is None:
            continue
        source_location = {"path": relative_path, "line": declaration["name_line"]}
        members = _positional_members(
            tokens, declaration["positional_start"], declaration["positional_end"]
        )
        property_members = _property_members(
            tokens, declaration["body_start"], declaration["body_end"], pairs
        )
        members_by_name = {member["name"]: member for member in members}
        for member in property_members:
            members_by_name[member["name"]] = member
        enum_members = (
            _enum_members(tokens, declaration["body_start"], declaration["body_end"])
            if declaration["kind"] == "enum"
            else []
        )
        parsed.append(
            _ParsedType(
                name=declaration["name"],
                namespace=_namespace_at(index, namespaces),
                kind=declaration["kind"],
                source=source_location,
                sources=[source_location],
                partial=declaration["partial"],
                type_parameters=declaration["type_parameters"],
                base_types=declaration["base_types"],
                members=sorted(
                    members_by_name.values(),
                    key=lambda member: (member["name"].casefold(), member["name"]),
                ),
                enum_members=sorted(
                    enum_members,
                    key=lambda member: (member["source_line"], member["name"]),
                ),
                start_token=index,
                end_token=declaration["end"],
                body_start_token=declaration["body_start"],
                body_end_token=declaration["body_end"],
            )
        )

    for item in parsed:
        parents = [
            candidate
            for candidate in parsed
            if candidate is not item
            and candidate.body_start_token is not None
            and candidate.body_end_token is not None
            and candidate.body_start_token < item.start_token < candidate.body_end_token
        ]
        if parents:
            parent = min(
                parents,
                key=lambda candidate: candidate.body_end_token - candidate.body_start_token,  # type: ignore[operator]
            )
            parent_name = parent.name
            if parent.containing_type:
                parent_name = f"{parent.containing_type}.{parent_name}"
            item.containing_type = parent_name
    return parsed


def _classification(name: str, kind: str) -> str:
    if kind == "enum":
        return "enum"
    if kind == "interface":
        return "service_contract"
    suffixes = (
        ("Dto", "dto"),
        ("Request", "request"),
        ("Command", "request"),
        ("Response", "response"),
        ("Result", "result"),
        ("Payload", "payload"),
        ("Event", "event"),
        ("Options", "configuration"),
        ("Config", "configuration"),
        ("Catalog", "catalog"),
        ("Registry", "catalog"),
    )
    for suffix, classification in suffixes:
        if name.endswith(suffix):
            return classification
    return kind


def _normalize_string_list(
    value: Any, field_name: str, *, required: bool = False
) -> tuple[str, ...]:
    if value is None:
        if required:
            raise ValueError(f"{field_name} is required")
        return ()
    if not isinstance(value, list) or any(
        not isinstance(item, str) or not item.strip() for item in value
    ):
        raise ValueError(f"{field_name} must be a list of non-empty strings")
    return tuple(
        sorted(
            {item.strip() for item in value}, key=lambda item: (item.casefold(), item)
        )
    )


def _load_contract_sets(root: Path, config: dict[str, Any]) -> list[_ContractSet]:
    entries = config.get("contract_sets")
    if not isinstance(entries, list):
        raise ValueError("config.contract_sets must be a list")
    result: list[_ContractSet] = []
    seen: set[str] = set()
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError("each contract_sets entry must be an object")
        contract_id = entry.get("id")
        if not isinstance(contract_id, str) or not contract_id.strip():
            raise ValueError("contract_sets[].id must be a non-empty string")
        contract_id = contract_id.strip()
        if _CONTRACT_SET_ID_RE.fullmatch(contract_id) is None:
            raise ValueError(
                "contract_sets[].id must be a lowercase slug containing only letters, "
                "digits, and single hyphens"
            )
        if contract_id in seen:
            raise ValueError(f"duplicate contract set id: {contract_id}")
        seen.add(contract_id)
        directory_labels = _normalize_string_list(
            entry.get("directories"),
            f"contract set {contract_id}.directories",
            required=True,
        )
        if not directory_labels:
            raise ValueError(
                f"contract set {contract_id}.directories must not be empty"
            )
        directories: list[Path] = []
        normalized_labels: list[str] = []
        for label in directory_labels:
            directory = Path(label)
            directory = directory if directory.is_absolute() else root / directory
            directory = directory.resolve()
            if not directory.is_dir():
                raise FileNotFoundError(
                    f"contract set {contract_id} directory does not exist: {label}"
                )
            directories.append(directory)
            normalized_labels.append(_relative_path(root, directory))
        result.append(
            _ContractSet(
                id=contract_id,
                directories=tuple(directories),
                directory_labels=tuple(normalized_labels),
                namespace_prefixes=_normalize_string_list(
                    entry.get("namespace_prefixes"),
                    f"contract set {contract_id}.namespace_prefixes",
                ),
                schemas=_normalize_string_list(
                    entry.get("schemas", []), f"contract set {contract_id}.schemas"
                ),
                diagram=bool(entry.get("diagram", False)),
            )
        )
    return sorted(result, key=lambda item: (item.id.casefold(), item.id))


def _path_is_within(path: Path, directory: Path) -> bool:
    # Both values are normalized to absolute paths while loading/scanning. Avoid
    # resolving them again for every type/set comparison: on Windows that turns
    # a linear source scan into tens of thousands of filesystem round trips.
    try:
        path.relative_to(directory)
        return True
    except ValueError:
        return False


def _namespace_matches(namespace: str, prefixes: Sequence[str]) -> bool:
    if not prefixes:
        return True
    return any(
        namespace == prefix or namespace.startswith(f"{prefix}.") for prefix in prefixes
    )


def _merge_types(items: Iterable[_ParsedType]) -> list[_ParsedType]:
    merged: dict[str, _ParsedType] = {}
    for item in sorted(
        items,
        key=lambda value: (
            value.qualified_name.casefold(),
            value.qualified_name,
            value.source["path"],
            value.source["line"],
        ),
    ):
        key = item.qualified_name
        existing = merged.get(key)
        if existing is None:
            merged[key] = item
            continue
        if existing.kind != item.kind:
            raise ValueError(
                f"conflicting public type declarations for {key}: {existing.kind} and {item.kind}"
            )
        existing.partial = existing.partial or item.partial
        existing.sources.extend(item.sources)
        unique_sources = sorted(
            {(_source["path"], _source["line"]) for _source in existing.sources},
            key=lambda value: (value[0].casefold(), value[0], value[1]),
        )
        existing.sources = [
            {"path": path, "line": line} for path, line in unique_sources
        ]
        existing.source = existing.sources[0]
        existing.type_parameters = sorted(
            set(existing.type_parameters), key=lambda value: (value.casefold(), value)
        )
        existing.base_types = sorted(
            set(existing.base_types + item.base_types),
            key=lambda value: (value.casefold(), value),
        )
        member_map = {member["name"]: member for member in existing.members}
        member_map.update({member["name"]: member for member in item.members})
        existing.members = sorted(
            member_map.values(),
            key=lambda member: (member["name"].casefold(), member["name"]),
        )
        enum_map = {member["name"]: member for member in existing.enum_members}
        enum_map.update({member["name"]: member for member in item.enum_members})
        existing.enum_members = sorted(
            enum_map.values(),
            key=lambda member: (member["source_line"], member["name"]),
        )
    return list(merged.values())


def _base_reference_type(raw: str) -> str:
    depth = 0
    for index, char in enumerate(raw):
        if char == "<":
            depth += 1
        elif char == ">":
            depth = max(0, depth - 1)
        elif char == "(" and depth == 0:
            return raw[:index].strip()
    return raw


def _resolve_references(objects: list[dict[str, Any]]) -> None:
    by_id = {item["id"]: item["id"] for item in objects}
    by_simple: dict[str, list[str]] = {}
    for object_id in by_id:
        simple = object_id.rsplit(".", 1)[-1]
        by_simple.setdefault(simple, []).append(object_id)

    for item in objects:
        raw_types = [_base_reference_type(value) for value in item["base_types"]]
        raw_types.extend(member["raw_type"] for member in item["members"])
        references: set[str] = set()
        for raw_type in raw_types:
            for candidate in _REFERENCE_RE.findall(raw_type):
                candidate = candidate.removeprefix("global::")
                resolved: str | None = None
                if candidate in by_id:
                    resolved = candidate
                else:
                    local = (
                        f"{item['namespace']}.{candidate}"
                        if item["namespace"]
                        else candidate
                    )
                    if local in by_id:
                        resolved = local
                    else:
                        simple = candidate.rsplit(".", 1)[-1]
                        matches = by_simple.get(simple, [])
                        if len(matches) == 1:
                            resolved = matches[0]
                if resolved and resolved != item["id"]:
                    references.add(resolved)
        item["references"] = sorted(
            references, key=lambda value: (value.casefold(), value)
        )


def _object_payload(item: _ParsedType) -> dict[str, Any]:
    return {
        "id": item.qualified_name,
        "full_name": item.qualified_name,
        "qualified_name": item.qualified_name,
        "name": item.name,
        "namespace": item.namespace,
        "containing_type": item.containing_type,
        "kind": item.kind,
        "classification": _classification(item.name, item.kind),
        "partial": item.partial,
        "type_parameters": item.type_parameters,
        "base_types": item.base_types,
        "members": item.members,
        "enum_members": item.enum_members,
        "source": item.source,
        "sources": item.sources,
        "contract_sets": item.contract_sets,
        "mapped_schemas": item.mapped_schemas,
        "diagram": bool(item.diagram_contract_sets),
        "diagram_contract_sets": item.diagram_contract_sets,
        "references": [],
    }


def build_contract_manifest(root: Path, config: dict) -> dict:
    """Build a deterministic manifest of configured public C# contract types.

    Overlapping contract sets never duplicate objects.  Instead, each global object
    records every matching contract-set ID and the union of those sets' mapped
    schemas.  Schema mapping remains module-level metadata only.
    """

    root = Path(root).resolve()
    if not root.is_dir():
        raise FileNotFoundError(f"repository root does not exist: {root}")
    if not isinstance(config, dict):
        raise ValueError("config must be an object")
    contract_sets = _load_contract_sets(root, config)

    source_paths: set[Path] = set()
    for contract_set in contract_sets:
        for directory in contract_set.directories:
            for path in directory.rglob("*.cs"):
                if any(part.casefold() in {"bin", "obj"} for part in path.parts):
                    continue
                source_paths.add(path.resolve())

    parsed_types: list[_ParsedType] = []
    for path in sorted(
        source_paths,
        key=lambda item: (
            _relative_path(root, item).casefold(),
            _relative_path(root, item),
        ),
    ):
        parsed_types.extend(_parse_file(root, path))
    parsed_types = _merge_types(parsed_types)

    objects_by_set: dict[str, list[str]] = {
        contract_set.id: [] for contract_set in contract_sets
    }
    included: list[_ParsedType] = []
    for item in parsed_types:
        source_paths_for_type = [root / source["path"] for source in item.sources]
        matching_sets = [
            contract_set
            for contract_set in contract_sets
            if any(
                _path_is_within(source_path, directory)
                for source_path in source_paths_for_type
                for directory in contract_set.directories
            )
            and _namespace_matches(item.namespace, contract_set.namespace_prefixes)
        ]
        if not matching_sets:
            continue
        item.contract_sets = [contract_set.id for contract_set in matching_sets]
        item.mapped_schemas = sorted(
            {
                schema
                for contract_set in matching_sets
                for schema in contract_set.schemas
            },
            key=lambda value: (value.casefold(), value),
        )
        item.diagram_contract_sets = [
            contract_set.id for contract_set in matching_sets if contract_set.diagram
        ]
        for contract_set in matching_sets:
            objects_by_set[contract_set.id].append(item.qualified_name)
        included.append(item)

    objects = [_object_payload(item) for item in included]
    objects.sort(key=lambda item: (item["id"].casefold(), item["id"]))
    _resolve_references(objects)
    for item in objects:
        structural = {
            key: value
            for key, value in item.items()
            if key
            not in {
                "contract_sets",
                "mapped_schemas",
                "diagram",
                "diagram_contract_sets",
                "fingerprint",
            }
        }
        item["fingerprint"] = _fingerprint(structural)

    set_entries: list[dict[str, Any]] = []
    sets_by_id = {contract_set.id: contract_set for contract_set in contract_sets}
    for contract_id in sorted(
        objects_by_set, key=lambda value: (value.casefold(), value)
    ):
        contract_set = sets_by_id[contract_id]
        object_ids = sorted(
            set(objects_by_set[contract_id]),
            key=lambda value: (value.casefold(), value),
        )
        entry = {
            "id": contract_set.id,
            "directories": list(contract_set.directory_labels),
            "namespace_prefixes": list(contract_set.namespace_prefixes),
            "schemas": list(contract_set.schemas),
            "diagram": contract_set.diagram,
            "object_ids": object_ids,
        }
        entry["fingerprint"] = _fingerprint(entry)
        set_entries.append(entry)

    partitions: list[dict[str, Any]] = []
    for namespace in sorted(
        {item["namespace"] for item in objects},
        key=lambda value: (value.casefold(), value),
    ):
        partition_objects = [item for item in objects if item["namespace"] == namespace]
        partition = {
            "namespace": namespace,
            "object_ids": [item["id"] for item in partition_objects],
            "contract_sets": sorted(
                {
                    contract_id
                    for item in partition_objects
                    for contract_id in item["contract_sets"]
                },
                key=lambda value: (value.casefold(), value),
            ),
            "mapped_schemas": sorted(
                {
                    schema
                    for item in partition_objects
                    for schema in item["mapped_schemas"]
                },
                key=lambda value: (value.casefold(), value),
            ),
        }
        partition["fingerprint"] = _fingerprint(
            {
                **partition,
                "object_fingerprints": [
                    item["fingerprint"] for item in partition_objects
                ],
            }
        )
        partitions.append(partition)

    manifest = {
        "schema": MANIFEST_SCHEMA,
        "version": MANIFEST_VERSION,
        "mapping_policy": {
            "level": "module",
            "structural_equivalence_claimed": False,
            "note": MAPPING_NOTE,
        },
        "contract_sets": set_entries,
        "namespace_partitions": partitions,
        "objects": objects,
    }
    manifest["fingerprint"] = _fingerprint(manifest)
    return manifest


__all__ = ["build_contract_manifest"]
