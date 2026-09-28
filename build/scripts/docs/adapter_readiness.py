#!/usr/bin/env python3
"""Source-backed contracts for the adapter readiness registry.

This deliberately small C# reader does not execute application code. It reads balanced
catalog initializers, ignores comments/literals when finding declarations, and checks
family-level interface coverage, including inherited interfaces. Unsupported catalog
syntax is an error rather than a reason to skip validation. Runtime reflection tests
remain the authority for compiled/conditional behavior.
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import Any

try:
    import yaml
except ModuleNotFoundError as exc:
    raise SystemExit("PyYAML is required; run python -m pip install -r build/scripts/docs/requirements.txt") from exc

REGISTRY_PATH = "docs/source/data/adapter-readiness.yml"
CATALOG_PATH = "src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs"
IDENTITY_PATH = "src/Meridian.ProviderSdk/ProviderIdentity.cs"
ADAPTER_ROOT = "src/Meridian.Infrastructure/Adapters"
CAPABILITIES = {
    "streaming": ("Streaming", "IMarketDataClient"),
    "historical": ("Historical", "IHistoricalDataProvider"),
    "symbol_search": ("Search", "ISymbolSearchProvider"),
    "corporate_actions": ("CorporateActions", "ICorporateActionProvider"),
    "options": ("Options", "IOptionsChainProvider"),
    "brokerage": ("Brokerage", "IBrokerageGateway"),
}
OTHER_TYPES = {
    "symbol_resolver": ("SymbolResolver", "ISymbolResolver"),
    "compatibility_data_source": ("CompatibilityDataSource", "IDataSource"),
}
TYPE_SLOTS = dict(CAPABILITIES, **OTHER_TYPES)
STATES = {"complete", "partial", "experimental", "template-only"}
IDENTIFIER = r"[A-Za-z_][A-Za-z_0-9]*"


class SourceError(ValueError):
    """The source contract is missing, inconsistent, or uses unsupported syntax."""


class _UniqueKeyLoader(yaml.SafeLoader):
    pass


def _unique_mapping(loader: _UniqueKeyLoader, node: yaml.MappingNode, deep: bool = False) -> dict:
    result = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node, deep=deep)
        if not isinstance(key, str):
            raise SourceError("registry YAML mapping keys must be strings")
        if key in result:
            raise SourceError(f"duplicate YAML key: {key}")
        result[key] = loader.construct_object(value_node, deep=deep)
    return result


_UniqueKeyLoader.add_constructor(yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, _unique_mapping)


def load_registry(root: Path) -> Any:
    return yaml.load((root / REGISTRY_PATH).read_text(encoding="utf-8"), Loader=_UniqueKeyLoader)


def _lex(source: str):
    """Yield (token, start, end); strings remain opaque and comments disappear."""
    pattern = re.compile(
        r'//[^\n]*|/\*[\s\S]*?\*/|(?:\$+)?(?P<raw>"{3,})[\s\S]*?(?P=raw)'
        r'|(?:\$@|@\$|@)"(?:""|[^"])*"|\$?"(?:\\.|[^"\\])*"'
        r"|'(?:\\.|[^'\\])*'|[A-Za-z_][A-Za-z_0-9]*|\d+|\S"
    )
    for match in pattern.finditer(source):
        token = match.group()
        if not token.startswith(("//", "/*")):
            yield token, match.start(), match.end()


def code_only(source: str) -> str:
    """Preserve positions/newlines while masking comments and C# string literals."""
    result = ["\n" if ch == "\n" else " " for ch in source]
    for token, start, end in _lex(source):
        if not token.startswith(('"', "'", '@"', '$"', '$@"', '@$"')) and not re.match(r'\$+"', token):
            result[start:end] = token
    return re.sub(r"(?m)^\s*#[^\n]*", "", "".join(result))


def _tokens(source: str) -> list[str]:
    return [token for token, _, _ in _lex(source)]


def _balanced(tokens: list[str], start: int) -> tuple[list[str], int]:
    pairs = {"(": ")", "[": "]", "{": "}"}
    if tokens[start] not in pairs:
        raise SourceError("expected a balanced source initializer")
    stack = [pairs[tokens[start]]]
    for index in range(start + 1, len(tokens)):
        token = tokens[index]
        if token in pairs:
            stack.append(pairs[token])
        elif token in pairs.values():
            if token != stack.pop():
                raise SourceError("unbalanced source initializer")
            if not stack:
                return tokens[start + 1:index], index + 1
    raise SourceError("unterminated source initializer")


def _initializer(tokens: list[str], name: str, opening: str) -> list[str]:
    positions = [i for i, token in enumerate(tokens) if token == name]
    if not positions:
        raise SourceError(f"source contract {name} is missing")
    for start in positions:
        index = start + 1
        while index < len(tokens) and tokens[index] not in ("=", ";"):
            if tokens[index] in ("(", "[", "{"):
                _, index = _balanced(tokens, index)
            else:
                index += 1
        if index == len(tokens) or tokens[index] != "=":
            continue
        index += 1
        while index < len(tokens) and tokens[index] not in (opening, ";"):
            index += 1
        if index < len(tokens) and tokens[index] == opening:
            return _balanced(tokens, index)[0]
    raise SourceError(f"unsupported initializer for {name}")


def _arguments(tokens: list[str]) -> list[list[str]]:
    """Split only outer commas, including generic method arguments in lambdas."""
    result: list[list[str]] = []
    current: list[str] = []
    index = 0
    generic_depth = 0
    while index < len(tokens):
        token = tokens[index]
        if token in ("(", "[", "{"):
            _, end = _balanced(tokens, index)
            current.extend(tokens[index:end])
            index = end
            continue
        if token == "<":
            generic_depth += 1
        elif token == ">" and generic_depth:
            generic_depth -= 1
        elif token == "," and not generic_depth:
            result.append(current)
            current = []
            index += 1
            continue
        current.append(token)
        index += 1
    if generic_depth:
        raise SourceError("unbalanced generic arguments in catalog")
    if current:
        result.append(current)
    return result


def _constructors(tokens: list[str]) -> list[list[list[str]]]:
    rows = []
    index = 0
    while index < len(tokens):
        if tokens[index] == ",":
            index += 1
            continue
        if tokens[index:index + 2] != ["new", "("]:
            raise SourceError(f"unsupported catalog row near {' '.join(tokens[index:index + 8])}")
        inner, index = _balanced(tokens, index + 1)
        rows.append(_arguments(inner))
    if not rows:
        raise SourceError("empty source inventory")
    return rows


def _string(tokens: list[str]) -> str:
    if len(tokens) != 1 or not re.fullmatch(r'"[^"\\]*"', tokens[0]):
        raise SourceError("expected a plain string in source identity inventory")
    return json.loads(tokens[0])


def read_catalog(root: Path) -> tuple[dict[str, dict[str, str | None]], set[str]]:
    tokens = _tokens((root / CATALOG_PATH).read_text(encoding="utf-8"))
    parameters = _arguments(_balanced(tokens, tokens.index("(", tokens.index("ProviderCapabilityDescriptor", tokens.index("record"))))[0])
    names = []
    for parameter in parameters:
        before_default = parameter[:parameter.index("=")] if "=" in parameter else parameter
        names.append(before_default[-1])
    if names[:9] != ["ProviderId", *[v[0] for v in TYPE_SLOTS.values()]]:
        raise SourceError("ProviderCapabilityDescriptor type slot signature changed")
    descriptors = {}
    for arguments in _constructors(_initializer(tokens, "Descriptors", "[")):
        values = {}
        for index, argument in enumerate(arguments):
            if len(argument) > 1 and argument[1] == ":":
                name, expression = argument[0], argument[2:]
            elif index < len(names):
                name, expression = names[index], argument
            else:
                raise SourceError("too many positional descriptor arguments")
            if name not in names or name in values:
                raise SourceError(f"unknown or duplicate descriptor argument: {name}")
            values[name] = expression
        provider_id = _string(values.get("ProviderId", []))
        if provider_id in descriptors:
            raise SourceError(f"duplicate catalog provider ID: {provider_id}")
        slots = {}
        for key, (slot, _) in TYPE_SLOTS.items():
            expression = values.get(slot, ["null"])
            if expression == ["null"]:
                slots[key] = None
            elif len(expression) == 4 and expression[:2] == ["typeof", "("] and expression[-1] == ")" and re.fullmatch(IDENTIFIER, expression[2]):
                slots[key] = expression[2]
            else:
                raise SourceError(f"unsupported {provider_id}.{slot} type expression")
        if not any(slots.values()):
            raise SourceError(f"catalog provider {provider_id} has no known adapter types")
        descriptors[provider_id] = slots
    excluded = set()
    for arguments in _constructors(_initializer(tokens, "ExcludedAdapterFamilies", "[")):
        if len(arguments) != 2:
            raise SourceError("unsupported adapter family exclusion")
        folder, reason = map(_string, arguments)
        if not reason.strip() or folder in excluded:
            raise SourceError(f"invalid or duplicate adapter exclusion: {folder}")
        excluded.add(folder)
    return descriptors, excluded


def read_identities(root: Path) -> tuple[set[str], dict[str, set[str]]]:
    tokens = _tokens((root / IDENTITY_PATH).read_text(encoding="utf-8"))
    ids = [_string(argument) for argument in _arguments(_initializer(tokens, "CanonicalFamilyIds", "{"))]
    if not ids or len(set(ids)) != len(ids):
        raise SourceError("empty or duplicate canonical provider identities")
    aliases: dict[str, set[str]] = {}
    seen = set()
    for row in _arguments(_initializer(tokens, "Aliases", "{")):
        if len(row) != 5 or row[0] != "[" or row[2:4] != ["]", "="]:
            raise SourceError("unsupported ProviderIdentity alias syntax")
        alias, canonical = _string(row[1:2]), _string(row[4:])
        if alias in seen:
            raise SourceError(f"duplicate source alias {alias}")
        seen.add(alias)
        aliases.setdefault(canonical, set()).add(alias)
    return set(ids), aliases


@dataclass
class AdapterType:
    name: str
    folder: str | None
    bases: set[str]
    concrete_public: bool


def _base_names(tail: str) -> set[str]:
    # Generic arguments and `where T : IFoo` constraints are not implemented bases.
    tokens = _tokens(tail)
    if "where" in tokens:
        tokens = tokens[:tokens.index("where")]
    if ":" not in tokens:
        return set()
    names = set()
    for base in _arguments(tokens[tokens.index(":") + 1:]):
        index = 0
        if base[:3] == ["global", ":", ":"]:
            index = 3
        if index >= len(base) or not re.fullmatch(IDENTIFIER, base[index]):
            raise SourceError(f"unsupported adapter base declaration: {' '.join(base)}")
        name = base[index]
        index += 1
        while base[index:index + 1] == ["."]:
            if index + 1 >= len(base) or not re.fullmatch(IDENTIFIER, base[index + 1]):
                raise SourceError("unsupported qualified adapter base declaration")
            name = base[index + 1]
            index += 2
        if index < len(base) and base[index] not in {"<", "("}:
            raise SourceError(f"unsupported adapter base declaration: {' '.join(base)}")
        names.add(name)
    return names


def read_adapter_types(root: Path) -> dict[str, AdapterType]:
    types: dict[str, AdapterType] = {}
    roots = [root / ADAPTER_ROOT, root / "src/Meridian.ProviderSdk", root / "src/Meridian.Infrastructure/DataSources"]
    declaration = re.compile(r"\b(?P<modifiers>(?:(?:public|internal|private|protected|abstract|sealed|static|partial|readonly)\s+)*)"
                             r"(?P<kind>class|interface)\s+(?P<name>" + IDENTIFIER + r")(?P<tail>[^;{}]*)\{")
    for source_root in roots:
        for path in sorted(source_root.rglob("*.cs")):
            if any(part in {"obj", "bin"} for part in path.relative_to(source_root).parts):
                continue
            text = code_only(path.read_text(encoding="utf-8"))
            folder = path.relative_to(root / ADAPTER_ROOT).parts[0] if path.is_relative_to(root / ADAPTER_ROOT) else None
            for match in declaration.finditer(text):
                if match["kind"] == "class" and "public" not in match["modifiers"].split():
                    continue
                name = match["name"]
                tail = match["tail"]
                bases = _base_names(tail)
                concrete = match["kind"] == "class" and "public" in match["modifiers"].split() and "abstract" not in match["modifiers"].split() and "static" not in match["modifiers"].split()
                if name in types:
                    if types[name].folder != folder:
                        raise SourceError(f"ambiguous source adapter type {name}")
                    types[name].bases.update(bases)  # partial declarations and #if SDK branches
                    types[name].concrete_public |= concrete
                else:
                    types[name] = AdapterType(name, folder, bases, concrete)
    return types


def implements(types: dict[str, AdapterType], name: str, contract: str, seen: frozenset[str] = frozenset()) -> bool:
    if name == contract:
        return True
    if name in seen or name not in types:
        return False
    return any(implements(types, base, contract, seen | {name}) for base in types[name].bases)


def _safe_path(root: Path, value: Any) -> Path:
    if not isinstance(value, str) or not value or "\\" in value or PureWindowsPath(value).drive or PurePosixPath(value).is_absolute() or any(part in {"..", "."} for part in value.split("/")):
        raise SourceError(f"invalid repository-relative path: {value!r}")
    path = (root / value).resolve()
    if not path.is_relative_to(root.resolve()):
        raise SourceError(f"path escapes repository root: {value}")
    if not path.is_file():
        raise SourceError(f"referenced file is missing: {value}")
    return path


def _has_symbol(text: str, symbol: str) -> bool:
    code = code_only(text)
    name = re.escape(symbol)
    type_declaration = rf"\b(?:class|interface|enum|struct|record(?:\s+(?:class|struct))?)\s+{name}\b"
    # Methods need an access modifier and return type; invocations/comments cannot satisfy evidence.
    method_declaration = rf"\b(?:public|internal|private|protected)\s+(?:(?:static|async|virtual|override|sealed|new|partial)\s+)*(?:[A-Za-z_][\w.<>,?\[\] ]*\s+)?{name}\s*(?:<[^;{{}}]*>)?\s*\("
    return bool(re.search(type_declaration, code) or re.search(method_declaration, code))


def validate_registry(root: Path, data: Any) -> list[str]:
    errors: list[str] = []
    if not isinstance(data, dict) or data.get("schema") != {"id": "meridian.adapter-readiness", "version": "1.0.0"}:
        return ["expected schema meridian.adapter-readiness version 1.0.0"]
    rows = data.get("adapters")
    if not isinstance(rows, list) or not rows:
        return ["adapters must be a non-empty list"]
    try:
        catalog, excluded = read_catalog(root)
        canonical, aliases = read_identities(root)
        types = read_adapter_types(root)
    except (OSError, ValueError, IndexError) as exc:
        return [f"cannot read source capability contracts: {exc}"]
    folders = {path.name for path in (root / ADAPTER_ROOT).iterdir() if path.is_dir()}
    catalog_folders: dict[str, str] = {}
    for provider_id, slots in catalog.items():
        if provider_id not in canonical:
            errors.append(f"catalog has unknown provider ID: {provider_id}")
        type_folders = set()
        for key, name in slots.items():
            if name is None:
                continue
            if name not in types or not types[name].concrete_public or types[name].folder is None:
                errors.append(f"catalog {provider_id}.{key}: unknown concrete adapter type {name}")
                continue
            type_folders.add(types[name].folder)
            if not implements(types, name, TYPE_SLOTS[key][1]):
                errors.append(f"catalog {provider_id}.{key}: {name} does not implement {TYPE_SLOTS[key][1]}")
        if len(type_folders) != 1:
            errors.append(f"catalog {provider_id}: types must belong to exactly one adapter folder")
            continue
        folder = next(iter(type_folders))
        if folder in catalog_folders or folder in excluded:
            errors.append(f"catalog folder {folder} is duplicated or excluded")
        catalog_folders[folder] = provider_id
        for key, (_, contract) in TYPE_SLOTS.items():
            known = [t.name for t in types.values() if t.folder == folder and t.concrete_public and implements(types, t.name, contract)]
            if known and not slots[key]:
                errors.append(f"catalog {provider_id}.{key}: missing known adapter capability implemented by {', '.join(sorted(known))}")
    source_folders = set(catalog_folders) | excluded
    if source_folders != folders:
        errors.append(f"source catalog folder coverage mismatch: missing={sorted(folders - source_folders)}, nonexistent={sorted(source_folders - folders)}")
    expected_ids = {folder: catalog_folders.get(folder, folder.lower() if folder.lower() in canonical else None) for folder in folders}
    if canonical != {value for value in expected_ids.values() if value is not None}:
        errors.append("canonical provider identities do not match catalog and excluded adapter families")
    seen = set()
    row_keys = {"folder", "provider_id", "aliases", "state", "capabilities", "adapter_types", "other_types", "requirements", "risks", "degradation", "registration", "evidence", "owner", "next_action"}
    reference_cache: dict[Path, str] = {}
    for index, row in enumerate(rows):
        label = f"adapter[{index}]"
        if not isinstance(row, dict):
            errors.append(f"{label}: must be a mapping")
            continue
        if set(row) != row_keys:
            errors.append(f"{label}: fields mismatch: missing={sorted(row_keys - set(row))}, unknown={sorted(set(row) - row_keys)}")
        folder = row.get("folder")
        if not isinstance(folder, str) or folder not in folders:
            errors.append(f"{label}: unknown or missing adapter folder {folder!r}")
            continue
        label = folder
        if folder in seen:
            errors.append(f"{label}: duplicate adapter folder")
        seen.add(folder)
        provider_id = row.get("provider_id")
        expected_id = expected_ids[folder]
        if provider_id != expected_id:
            errors.append(f"{label}: unknown or incorrect provider ID {provider_id!r}; expected {expected_id!r}")
        expected_aliases = aliases.get(expected_id, set())
        actual_aliases = row.get("aliases")
        if not isinstance(actual_aliases, list) or any(not isinstance(a, str) for a in actual_aliases) or len(actual_aliases) != len(set(actual_aliases)) or set(actual_aliases) != expected_aliases:
            errors.append(f"{label}: aliases must match ProviderIdentity: {sorted(expected_aliases)}")
        if not isinstance(row.get("state"), str) or row["state"] not in STATES:
            errors.append(f"{label}: invalid readiness state {row.get('state')!r}")
        slots = catalog.get(expected_id, dict.fromkeys(TYPE_SLOTS))
        for field, keys in (("capabilities", CAPABILITIES), ("adapter_types", CAPABILITIES), ("other_types", OTHER_TYPES)):
            values = row.get(field)
            if not isinstance(values, dict) or set(values) != set(keys):
                errors.append(f"{label}: {field} must contain exactly {', '.join(keys)}")
                continue
            for key in keys:
                expected = slots[key] is not None if field == "capabilities" else slots[key]
                if values[key] != expected or (field == "capabilities" and type(values[key]) is not bool):
                    errors.append(f"{label}.{field}.{key}: expected {expected!r} from source catalog, got {values[key]!r}")
        for field in ("owner", "next_action", "degradation"):
            if not isinstance(row.get(field), str) or not row[field].strip():
                errors.append(f"{label}: {field} must be non-empty text")
        requirements = row.get("requirements")
        if not isinstance(requirements, dict) or set(requirements) != {"credentials", "optional_sdk"} or any(not isinstance(value, str) or not value.strip() for value in requirements.values()):
            errors.append(f"{label}: requirements must contain non-empty credentials and optional_sdk text")
        risks = row.get("risks")
        if not isinstance(risks, list) or not risks or any(not isinstance(risk, str) or not risk.strip() for risk in risks):
            errors.append(f"{label}: risks must be a non-empty list of text")
        for field in ("registration", "evidence"):
            refs = row.get(field)
            if not isinstance(refs, list) or not refs:
                errors.append(f"{label}: {field} must be a non-empty reference list")
                continue
            has_test = False
            for ref in refs:
                expected_keys = {"path", "symbol", "kind"} if field == "evidence" else {"path", "symbol"}
                if not isinstance(ref, dict) or set(ref) != expected_keys:
                    errors.append(f"{label}: malformed {field} reference {ref!r}")
                    continue
                symbol = ref.get("symbol")
                if not isinstance(symbol, str) or not re.fullmatch(IDENTIFIER, symbol):
                    errors.append(f"{label}: reference symbol must be a declaration identifier")
                    continue
                try:
                    path = _safe_path(root, ref["path"])
                    if path.suffix != ".cs":
                        raise SourceError(f"reference must target C# source: {ref['path']}")
                    if field == "evidence":
                        if not isinstance(ref["kind"], str) or ref["kind"] not in {"test", "source"}:
                            raise SourceError(f"invalid evidence kind: {ref['kind']}")
                        if ref["kind"] == "test":
                            if not ref["path"].startswith("tests/"):
                                raise SourceError("test evidence must be under tests/")
                            has_test = True
                    reference_cache.setdefault(path, path.read_text(encoding="utf-8"))
                    if not _has_symbol(reference_cache[path], symbol):
                        raise SourceError(f"stale reference symbol {symbol} in {ref['path']}")
                except (OSError, ValueError) as exc:
                    errors.append(f"{label}.{field}: {exc}")
            if field == "evidence" and not has_test:
                errors.append(f"{label}: evidence must include a targeted test reference")
    if seen != folders:
        errors.append(f"registry missing adapter folders: {', '.join(sorted(folders - seen))}")
    return errors


if __name__ == "__main__":
    import argparse

    argparse.ArgumentParser(description=__doc__).parse_args()
