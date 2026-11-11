#!/usr/bin/env python3
"""Preview or synchronize unambiguous color fallbacks with their own track.

Dry-run is the default. Each literal belongs to its innermost var() role. RGB
channels may be updated in hex/rgb(a) literals and unambiguous composites while
their alpha, geometry and token names remain intact. Local overrides, dark
contexts and mismatched color shapes remain untouched and reported for review.
The explicit --canonical-alpha option converts reviewed opaque single-color
fallbacks for translucent roles to their declared RGBA wash. This does not
replace the contrast gate or update generated distributions.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import re
from collections import Counter
from pathlib import Path

_SPEC = importlib.util.spec_from_file_location("_fallback_contrast", Path(__file__).with_name("check_contrast.py"))
contrast = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(contrast)

VAR_START = re.compile(r"(?<![A-Za-z0-9_-])var\(\s*(--[A-Za-z0-9-]+)\s*,")
COLOR_LITERAL = re.compile(r"#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(")
LITERAL_START = re.compile(r"#[0-9a-fA-F]{3,8}\b|\brgba?\(", re.I)
NUMBER = re.compile(r"[+-]?(?:\d+(?:\.\d*)?|\.\d+)%?")
SIMPLE_HEX = re.compile(r"#[0-9a-fA-F]{3}(?:[0-9a-fA-F]{3})?\Z")
DARK_CONTEXT = re.compile(r"prefers-color-scheme\s*:\s*dark|(?:\\?[\"'])dark(?:\\?[\"'])", re.I)
GENERATED_NAMES = {"_ds_bundle.js", "_preview_runtime.js"}


def mask_comments(text: str) -> str:
    """Keep offsets, masking possible comments conservatively across JSX/HTML/CSS.

    Masking a quoted comment marker or URL can skip a valid reference; that is
    safer than treating JSX prose apostrophes as JS strings and editing a comment.
    The original source, including every comment, is never replaced by this mask.
    """
    potential = re.compile(r"/\*.*?(?:\*/|\Z)|<!--.*?(?:-->|\Z)|//[^\n]*", re.S)
    return potential.sub(lambda match: re.sub(r"[^\r\n]", " ", match.group()), text)


def var_end(text: str, start: int) -> int | None:
    depth, quote, i = 0, None, start
    while i < len(text):
        ch = text[i]
        if quote:
            if ch == "\\":
                i += 2
                continue
            if ch == quote:
                quote = None
        elif ch in "\"'":
            quote = ch
        elif ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return None


def dependencies(name: str, tokens: dict[str, str], seen: frozenset[str] = frozenset()) -> set[str]:
    if name in seen:
        return {name}
    names = {name}
    for ref in contrast.INNER_VAR.findall(tokens.get(name, "")):
        names |= dependencies(ref, tokens, seen | {name})
    return names


def opaque_color(value: str, tokens: dict[str, str], seen: frozenset[str] = frozenset()) -> bool:
    """Reject alpha and shorthand values before asking the checker to resolve RGB."""
    value = value.strip()
    if SIMPLE_HEX.fullmatch(value):
        return True
    alias = contrast.VAR_PATTERN.fullmatch(value)
    if alias:
        name = alias.group(1)
        return name not in seen and name in tokens and opaque_color(tokens[name], tokens, seen | {name})
    mix = contrast.MIX_PATTERN.fullmatch(value)
    if mix:
        return (0 <= float(mix.group(2)) <= 100
                and opaque_color(mix.group(1), tokens, seen)
                and opaque_color(mix.group(3), tokens, seen))
    # A concrete functional color is safe only when it has three channels and no alpha.
    functional = re.fullmatch(r"(?:rgb|hsl)\(([^()/]*)\)", value, re.I)
    return bool(functional and len(re.split(r"\s*,\s*|\s+", functional.group(1).strip())) == 3)


def literal_colors(text: str) -> list[dict] | None:
    """Return supported literal spans in lexical order, retaining alpha/format spans."""
    colors, position = [], 0
    while found := LITERAL_START.search(text, position):
        start = found.start()
        if found.group().startswith("#"):
            literal = found.group()
            if len(literal) not in {4, 5, 7, 9}:
                return None
            expanded = "".join(ch * 2 for ch in literal[1:]) if len(literal) in {4, 5} else literal[1:]
            rgb = tuple(int(expanded[i:i + 2], 16) for i in (0, 2, 4))
            alpha = int(expanded[6:], 16) / 255 if len(expanded) == 8 else 1.0
            colors.append({"start": start, "end": found.end(), "kind": "hex", "rgb": rgb,
                           "alpha": alpha, "suffix": expanded[6:]})
            position = found.end()
            continue
        end = var_end(text, start)
        if end is None:
            return None
        open_paren = text.index("(", start)
        body = text[open_paren + 1:end - 1]
        if "," in body:
            parts = body.split(",")
            if len(parts) not in {3, 4} or any(not NUMBER.fullmatch(part.strip()) for part in parts):
                return None
        else:
            parts = body.split("/")
            channels_part = parts[0].split()
            if (len(parts) > 2 or len(channels_part) != 3
                    or any(not NUMBER.fullmatch(part) for part in channels_part)
                    or (len(parts) == 2 and not NUMBER.fullmatch(parts[1].strip()))):
                return None
        channels = list(NUMBER.finditer(body))
        if len(channels) not in {3, 4} or re.sub(NUMBER, "", body).strip(" ,/\t\r\n"):
            return None
        values = [float(channel.group().rstrip("%")) for channel in channels]
        if any(not 0 <= values[i] <= (100 if channels[i].group().endswith("%") else 255) for i in range(3)):
            return None
        rgb = tuple(values[i] * (255 / 100 if channels[i].group().endswith("%") else 1) for i in range(3))
        alpha = values[3] / (100 if channels[3].group().endswith("%") else 1) if len(channels) == 4 else 1.0
        if not 0 <= alpha <= 1:
            return None
        colors.append({"start": start, "end": end, "kind": "rgb", "rgb": rgb, "alpha": alpha,
                       "channels": [(open_paren + 1 + item.start(), open_paren + 1 + item.end(), item.group().endswith("%")) for item in channels[:3]]})
        position = end
    return colors


def expand_vars(value: str, tokens: dict[str, str], seen: frozenset[str] = frozenset()) -> str | None:
    out, position = [], 0
    for found in contrast.VAR_CALL.finditer(value):
        if found.start() < position:
            continue
        end = var_end(value, found.start())
        parsed = contrast.split_var_call(value, found.start())
        if end is None or parsed is None:
            return None
        name, fallback = parsed
        if name in seen:
            return None
        target = tokens[name] if name in tokens else fallback
        if not target:
            return None
        expanded = expand_vars(target, tokens, seen | {name})
        if expanded is None:
            return None
        out.extend([value[position:found.start()], expanded])
        position = end
    return "".join(out) + value[position:]


def canonical_colors(name: str, tokens: dict[str, str]) -> tuple[list[dict], str] | None:
    expanded = expand_vars(tokens[name], tokens, frozenset({name}))
    if expanded is None:
        return None
    value = expanded.strip()
    wash = contrast.MIX_ALPHA.fullmatch(value)
    if wash and opaque_color(wash.group(1), {}):
        hue = contrast.resolve(wash.group(1), {})
        if hue is not None:
            return ([{"rgb": hue, "alpha": float(wash.group(2)) / 100}], "color")
    if opaque_color(value, {}):
        rgb = contrast.resolve(value, {})
        if rgb is not None:
            return ([{"rgb": rgb, "alpha": 1.0}], "color")
    colors = literal_colors(value)
    if not colors:
        return None
    if "color-mix(" in value or "hsl(" in value:
        return None  # Do not mistake an unresolved function's operands for separate colors.
    shape = "color" if len(colors) == 1 and colors[0]["start"] == 0 and colors[0]["end"] == len(value) else "composite"
    return colors, shape


def color_replacement(text: str, literal: dict, rgb: tuple) -> str:
    if literal["kind"] == "hex":
        return "#" + "".join(f"{round(channel):02X}" for channel in rgb) + literal["suffix"]
    result = text[literal["start"]:literal["end"]]
    for i in (2, 1, 0):
        start, end, percent = literal["channels"][i]
        value = f"{rgb[i] / 255 * 100:.6f}".rstrip("0").rstrip(".") + "%" if percent else str(round(rgb[i]))
        result = result[:start - literal["start"]] + value + result[end - literal["start"]:]
    return result


def token_maps(root: Path) -> dict[str, dict[str, str]]:
    sheets = root / "tokens"
    package = {**contrast.parse_tokens((sheets / "theme.css").read_text(encoding="utf-8")),
               **contrast.parse_tokens((sheets / "colors.css").read_text(encoding="utf-8"))}
    for sheet in sorted(sheets.glob("*.css")):
        if sheet.name not in {"theme.css", "colors.css", "colors-dark.css"}:
            for name, value in contrast.parse_tokens(sheet.read_text(encoding="utf-8")).items():
                package.setdefault(name, value)
    workstation = {}
    for path in contrast.tracks(root)["workstation"]:
        if path.exists():
            for name, value in contrast.parse_tokens(path.read_text(encoding="utf-8")).items():
                workstation.setdefault(name, value)
    return {"package": package, "workstation": workstation}


def candidates(root: Path):
    seen = set()
    for track, base in contrast.call_sites(root):
        if not base.exists():
            continue
        for path in ([base] if base.is_file() else sorted(base.rglob("*"))):
            if (not path.is_file() or path.name in GENERATED_NAMES or path.suffix not in contrast.CALL_SITE_SUFFIXES
                    or any(part in {"node_modules", "dist", "coverage", ".git"} for part in path.parts)):
                continue
            if path not in seen:
                seen.add(path)
                yield track, path


def plan_text(text: str, tokens: dict[str, str], *, path: str = "", track: str = "package", canonical_alpha: bool = False) -> dict:
    masked = mask_comments(text)
    local = set(contrast.DECL_NAME.findall(masked)) | set(contrast.JS_DECL_NAME.findall(masked))
    context = "test-source" if re.search(r"\.(?:test|spec)\.[^.]+$", path) else ""
    if Path(path).suffix == ".sh":
        context = "shell-source"  # Shell comments and emitted CSS heredocs need explicit review.
    if DARK_CONTEXT.search(masked):
        context = "explicit-dark-context"
    findings, replacements = [], []

    def visit(start: int, stop: int) -> None:
        position = start
        while found := VAR_START.search(masked, position, stop):
            end = var_end(masked, found.start())
            if end is None or end > stop:
                break
            position = end
            parsed = contrast.split_var_call(masked, found.start())
            if not parsed:
                continue
            name, fallback = parsed
            if not COLOR_LITERAL.search(fallback):
                continue
            detail = {"path": path, "track": track, "line": text.count("\n", 0, found.start()) + 1,
                      "token": name, "before": fallback, "action": "skip"}
            reason = context
            if not reason and dependencies(name, tokens) & local:
                reason = "local-override"
            if reason:
                findings.append({**detail, "reason": reason})
                continue
            if re.search(r"[\"'`$;={}]", fallback):
                findings.append({**detail, "reason": "dynamic-or-nonliteral-fallback"})
                continue
            fallback_start = found.end() + len(masked[found.end():end - 1]) - len(masked[found.end():end - 1].lstrip())
            nested = VAR_START.search(masked, fallback_start, end - 1)
            if nested:
                # The literal belongs to the inner role, even when its defaults differ
                # from the outer role. Never rewrite it using the outer role's color.
                visit(fallback_start, end - 1)
                findings.append({**detail, "reason": "nested-chain-outer-role"})
                continue
            if name not in tokens:
                findings.append({**detail, "reason": "undeclared-token"})
                continue
            canonical = canonical_colors(name, tokens)
            literals = literal_colors(fallback)
            if canonical is None or not literals:
                findings.append({**detail, "reason": "unresolved-or-unsupported-color"})
                continue
            wanted, canonical_shape = canonical
            shape = "color" if len(literals) == 1 and literals[0]["start"] == 0 and literals[0]["end"] == len(fallback) else "composite"
            if len(wanted) != len(literals) or shape != canonical_shape:
                findings.append({**detail, "reason": "ambiguous-color-shape"})
                continue
            alpha_mismatch = any(expected["alpha"] < 1 and actual["alpha"] >= 1 for expected, actual in zip(wanted, literals))
            convert_alpha = canonical_alpha and alpha_mismatch and canonical_shape == "color" and len(literals) == 1
            if alpha_mismatch and not convert_alpha:
                # An opaque pale wash may be a composited surface, not the raw hue.
                # Painting it opaque red/green would satisfy the old hue-only check
                # but destroy its meaning and contrast. Require explicit review.
                findings.append({**detail, "reason": "alpha-kind-mismatch"})
                continue
            for expected, literal in zip(wanted, literals):
                rgb = expected["rgb"]
                if not all(math.isfinite(n) and 0 <= n <= 255 for n in rgb):
                    findings.append({**detail, "reason": "unresolved-or-unsupported-color"})
                    continue
                if not convert_alpha and max(abs(rgb[i] - literal["rgb"][i]) for i in range(3)) <= 1:
                    continue
                absolute_start = fallback_start + literal["start"]
                absolute_end = fallback_start + literal["end"]
                original = text[absolute_start:absolute_end]
                if original != fallback[literal["start"]:literal["end"]]:
                    findings.append({**detail, "reason": "comment-or-escape-in-fallback"})
                    continue
                hue = "#" + "".join(f"{round(channel):02X}" for channel in rgb)
                if convert_alpha:
                    alpha = f"{expected['alpha']:.6f}".rstrip("0").rstrip(".")
                    replacement = "rgba(" + ", ".join([*(str(round(channel)) for channel in rgb), alpha]) + ")"
                else:
                    replacement = color_replacement(fallback, literal, rgb)
                replacements.append((absolute_start, absolute_end, replacement))
                findings.append({**detail, "before": original, "action": "replace", "after": replacement,
                                 "reason": "canonical-alpha-wash" if convert_alpha else "canonical-light-color",
                                 **({"hue": hue, "alpha": expected["alpha"]} if convert_alpha else {})})

    visit(0, len(masked))
    updated = text
    for start, end, replacement in sorted(replacements, reverse=True):
        updated = updated[:start] + replacement + updated[end:]
    return {"text": updated, "findings": findings, "replacements": len(replacements)}


def synchronize(root: Path, *, write: bool = False, canonical_alpha: bool = False) -> dict:
    maps = token_maps(root)
    findings, changed = [], []
    for track, path in candidates(root):
        raw = path.read_bytes()
        relative = path.relative_to(root.parent).as_posix()
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError:
            findings.append({"path": relative, "track": track, "action": "skip", "reason": "non-utf8-source"})
            continue
        plan = plan_text(text, maps[track], path=relative, track=track, canonical_alpha=canonical_alpha)
        findings.extend(plan["findings"])
        if plan["replacements"]:
            if write:
                if hashlib.sha256(path.read_bytes()).digest() != hashlib.sha256(raw).digest():
                    raise RuntimeError(f"Source changed during synchronization: {relative}")
                path.write_bytes(plan["text"].encode("utf-8"))
            changed.append(relative)
    return {"mode": "write" if write else "dry-run", "canonical_alpha": canonical_alpha,
            "replacements": sum(f["action"] == "replace" for f in findings),
            "changed_files": changed, "skipped": dict(Counter(f["reason"] for f in findings if f["action"] == "skip")),
            "findings": findings}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--write", action="store_true", help="Apply only the reported unambiguous replacements.")
    parser.add_argument("--canonical-alpha", action="store_true", help="Convert opaque single-color fallbacks for translucent roles to their declared RGBA wash.")
    parser.add_argument("--json", action="store_true", help="Print the complete machine-readable plan.")
    args = parser.parse_args()
    report = synchronize(args.root.resolve(), write=args.write, canonical_alpha=args.canonical_alpha)
    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print(f"{report['mode']}: {report['replacements']} replacements in {len(report['changed_files'])} files")
        print("Skipped for review: " + json.dumps(report["skipped"], sort_keys=True))
        for finding in [f for f in report["findings"] if f["action"] == "replace"][:20]:
            print(f"  {finding['path']}:{finding['line']} {finding['token']}: {finding['before']} -> {finding['after']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
