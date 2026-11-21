#!/usr/bin/env python3
"""Refresh source-derived token/card metadata without changing the public census.

Only the six registered token stylesheets and existing card paths are read.
Existing token kinds and source origins are retained among equally applicable
declarations. Unconditional declarations across the registered sources take
precedence over conditional fallbacks, even when an older manifest recorded an
auto-dark origin. Conditional-only tokens remain represented.
"""
from __future__ import annotations

import argparse
import copy
import html
import json
from pathlib import Path
import re
import sys
from collections.abc import Mapping

ROOT = Path(__file__).resolve().parents[1]
TOKEN_FILES = (
    "tokens/colors-dark.css", "tokens/colors.css", "tokens/elevation.css",
    "tokens/theme.css", "tokens/typography.css", "tokens/visualization.css",
)
CARD_FIELDS = ("name", "group", "viewport", "subtitle")


def _text(sources: Mapping[str, str | bytes], path: str) -> str:
    try:
        value = sources[path]
    except KeyError as error:
        raise ValueError(f"Metadata source missing: {path}") from error
    return value.decode("utf-8") if isinstance(value, bytes) else value


def _without_comments(source: str) -> str:
    """Remove CSS comments while retaining comment-like text inside strings."""
    output = []
    quote = None
    index = 0
    while index < len(source):
        char = source[index]
        if char == "\\" and index + 1 < len(source):
            output.append(source[index:index + 2])
            index += 2
            continue
        if quote:
            output.append(char)
            if char == quote:
                quote = None
        elif char in "\"'":
            quote = char
            output.append(char)
        elif source.startswith("/*", index):
            end = source.find("*/", index + 2)
            if end < 0:
                raise ValueError("Unterminated CSS comment")
            output.append(" ")
            index = end + 2
            continue
        else:
            output.append(char)
        index += 1
    if quote:
        raise ValueError("Unterminated CSS string")
    return "".join(output)


def _split_selectors(selector: str) -> list[str]:
    result, start, depth, quote = [], 0, 0, None
    index = 0
    while index < len(selector):
        char = selector[index]
        if char == "\\":
            index += 2
            continue
        if quote:
            if char == quote:
                quote = None
        elif char in "\"'":
            quote = char
        elif char in "([":
            depth += 1
        elif char in ")]":
            depth -= 1
        elif char == "," and depth == 0:
            result.append(selector[start:index].strip())
            start = index + 1
        index += 1
    result.append(selector[start:].strip())
    return result


def _selector_key(selector: str) -> str:
    selector = re.sub(r"\s*=\s*", "=", selector.strip())
    return re.sub(r"\[([^\]]*?)'([^']*)'\]", r'[\1"\2"]', selector)


def css_declarations(source: str) -> list[dict]:
    """Parse custom declarations and their selector/conditional context.

    This is a bounded stylesheet parser, not a regex over lines: quoted braces,
    semicolons in functions/strings, comments and nested media blocks cannot
    become invented tokens or truncate a value.
    """
    source = _without_comments(source)
    result = []

    def declaration(statement: str, selectors: list[str], conditional: bool):
        match = re.fullmatch(r"\s*(--[\w-]+)\s*:\s*(.*?)\s*", statement, re.S)
        if not match:
            return
        name, value = match.groups()
        if not value:
            raise ValueError(f"Empty CSS token declaration: {name}")
        for selector in selectors:
            result.append({"selector": _selector_key(selector), "name": name,
                           "value": value, "conditional": conditional})

    def block(index: int, selectors: list[str], conditional: bool, nested: bool = False) -> int:
        start, quote, parentheses, brackets = index, None, 0, 0
        while index < len(source):
            char = source[index]
            if char == "\\":
                index += 2
                continue
            if quote:
                if char == quote:
                    quote = None
            elif char in "\"'":
                quote = char
            elif char == "(":
                parentheses += 1
            elif char == ")":
                parentheses -= 1
            elif char == "[":
                brackets += 1
            elif char == "]":
                brackets -= 1
            elif parentheses == 0 and brackets == 0:
                if char == ";":
                    declaration(source[start:index], selectors, conditional)
                    start = index + 1
                elif char == "{":
                    prelude = source[start:index].strip()
                    if prelude.startswith("@"):
                        at_name = prelude.split(None, 1)[0].lower()
                        child_selectors = selectors
                        child_conditional = conditional or at_name != "@layer"
                    else:
                        child_selectors = _split_selectors(prelude)
                        child_conditional = conditional
                    index = block(index + 1, child_selectors, child_conditional, True)
                    start = index + 1
                elif char == "}":
                    if not nested:
                        raise ValueError("Unexpected CSS closing brace")
                    declaration(source[start:index], selectors, conditional)
                    return index
            index += 1
        if nested:
            raise ValueError("Unclosed CSS block")
        if parentheses or brackets:
            raise ValueError("Unbalanced CSS value")
        return index

    block(0, [], False)
    return result


def _infer_kind(name: str, value: str) -> str:
    if re.search(r"(?:#[0-9a-fA-F]{3,8}\b|\b(?:rgb|hsl|oklch|oklab|lab|lch|color|color-mix)\()", value):
        return "color"
    color_role = r"(?:color|accent|ink|signal|surface|border|foreground|background|(?:^|-)bg(?:-|$)|(?:^|-)fg(?:-|$)|text-on|amber|orange|green|red|blue|purple|cyan|navy)"
    if re.search(color_role, name) or re.search(r"var\(\s*--(?:amber|orange|green|red|blue|purple|cyan|navy)(?:-|\))", value):
        return "color"
    if "radius" in name:
        return "radius"
    if "shadow" in name:
        return "shadow"
    if re.search(r"(?:font|type-|weight)", name):
        return "font"
    if re.fullmatch(r"-?(?:\d+(?:\.\d*)?|\.\d+)(?:px|rem|em|vh|vw|%)", value):
        return "spacing"
    return "other"


def token_metadata(manifest: dict, sources: Mapping[str, str | bytes]) -> list[dict]:
    # Registered selectors, not arbitrary local component selectors, are public.
    scopes = {}
    for scope in [theme["selector"] for theme in manifest.get("themes", [])] + [
        token["scope"] for token in manifest.get("tokens", []) if token.get("scope")
    ]:
        scopes.setdefault(_selector_key(scope), scope)
    candidates = {}
    for path in sorted(TOKEN_FILES):
        for record in css_declarations(_text(sources, path)):
            selector = record["selector"]
            if selector == ":root":
                scope = None
            elif selector in scopes:
                scope = scopes[selector]
            else:
                continue
            key = (record["name"], scope)
            candidates.setdefault(key, {}).setdefault(path, []).append(record)

    previous = {(token["name"], token.get("scope")): token for token in manifest.get("tokens", [])}
    refreshed = {}
    for key, files in candidates.items():
        old = previous.get(key)
        # Conditional ancestry is more important than a historical source origin:
        # an auto-dark :root in the alphabetically first file is not the ordinary
        # light default when another source declares an unconditional :root.
        unconditional = {path: [item for item in options if not item["conditional"]]
                         for path, options in files.items()}
        unconditional = {path: options for path, options in unconditional.items() if options}
        if unconditional:
            files = unconditional
        path = old["definedIn"] if old and old["definedIn"] in files else sorted(files)[0]
        options = files[path]
        declaration = options[0]
        name, scope = key
        token = copy.deepcopy(old) if old else {"name": name, "kind": _infer_kind(name, declaration["value"])}
        token.update({"value": declaration["value"], "definedIn": path})
        if scope is not None:
            token["scope"] = scope
        refreshed[key] = token

    # Keep existing order to minimize review churn. Newly introduced roles append
    # in stable source order; a second pass is therefore semantically identical.
    keys = [key for key in previous if key in refreshed]
    keys.extend(key for key in refreshed if key not in previous)
    return [refreshed[key] for key in keys]


def card_metadata(card: dict, source: str) -> dict:
    marker = next((match.group(1) for match in re.finditer(r"<!--\s*@dsCard\b([\s\S]*?)-->", source)), None)
    if marker is None:
        raise ValueError(f"Card marker missing: {card['path']}")
    attributes = {match.group(1): html.unescape(match.group(3)) for match in re.finditer(
        r"([\w-]+)\s*=\s*([\"'])(.*?)\2", marker, re.S
    )}
    for required in ("name", "group", "viewport"):
        if required not in attributes:
            raise ValueError(f"Card marker missing {required}: {card['path']}")
    updated = copy.deepcopy(card)
    for field in CARD_FIELDS:
        if field in attributes:
            updated[field] = attributes[field]
        else:
            updated.pop(field, None)
    return updated


def source_paths(manifest: dict) -> list[str]:
    return list(TOKEN_FILES) + [card["path"] for card in manifest.get("cards", [])]


def synchronize_manifest(manifest: dict, sources: Mapping[str, str | bytes]) -> dict:
    """Pure source-map API, also usable by distribution integrity validation."""
    updated = copy.deepcopy(manifest)
    updated["tokens"] = token_metadata(manifest, sources)
    updated["cards"] = [card_metadata(card, _text(sources, card["path"])) for card in manifest.get("cards", [])]
    return updated


def require_current_manifest(manifest: dict, sources: Mapping[str, str | bytes]) -> None:
    if synchronize_manifest(manifest, sources) != manifest:
        raise ValueError("Manifest token/card metadata is stale; run scripts/sync_manifest_metadata.py --write")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--check", action="store_true", help="Fail when token/card metadata is stale.")
    action.add_argument("--write", action="store_true", help="Refresh only token/card metadata in the existing manifest.")
    parser.add_argument("--root", type=Path, default=ROOT)
    args = parser.parse_args()
    try:
        path = args.root / "_ds_manifest.json"
        manifest = json.loads(path.read_text(encoding="utf-8"))
        sources = {name: (args.root / name).read_bytes() for name in source_paths(manifest)}
        updated = synchronize_manifest(manifest, sources)
        old_tokens = {(item["name"], item.get("scope")): item for item in manifest["tokens"]}
        new_tokens = {(item["name"], item.get("scope")): item for item in updated["tokens"]}
        added, removed = new_tokens.keys() - old_tokens.keys(), old_tokens.keys() - new_tokens.keys()
        changed = [key for key in old_tokens.keys() & new_tokens.keys() if old_tokens[key] != new_tokens[key]]
        changed_cards = [old["path"] for old, new in zip(manifest["cards"], updated["cards"]) if old != new]
        print(f"Tokens: {len(old_tokens)} -> {len(new_tokens)} ({len(added)} added, {len(removed)} removed, {len(changed)} changed); cards: {len(changed_cards)} metadata changes")
        for key in sorted(added | removed | set(changed), key=lambda item: (item[0], item[1] or "")):
            sign = "+" if key in added else "-" if key in removed else "~"
            print(f"{sign} {key[0]} [{key[1] or 'default'}]: {old_tokens.get(key)} -> {new_tokens.get(key)}")
        for card in changed_cards:
            print(f"~ card: {card}")
        if updated == manifest:
            print("Manifest token/card metadata is current")
            return 0
        if args.write:
            path.write_text(json.dumps(updated, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
            print("Refreshed _ds_manifest.json token/card metadata")
            return 0
        print("Manifest metadata is stale", file=sys.stderr)
        return 1
    except (OSError, ValueError, KeyError) as error:
        print(f"Manifest metadata error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
