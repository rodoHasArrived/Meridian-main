#!/usr/bin/env python3
"""WCAG contrast checker for the Meridian token system.

Computes contrast from the current token roles and component source across light/dark
appearances, including recursive var() resolution, HSL channels and two-color srgb mixes.
Transparent chip washes are composited over their actual material surfaces. Fails with
exit 1 when a checked text/graphic pair drops below its AA threshold or retained source
conformance checks fail.

This closes the "known follow-up" in guidelines/ACCESSIBILITY.md: the contrast table there was
hand-measured; this script re-derives it on every run.

Usage:  python3 scripts/check_contrast.py [--root PATH] [--scope all|browser] [--actions-only]
"""

from __future__ import annotations

import argparse
import colorsys
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

TOKEN_PATTERN = re.compile(r"(--[a-z0-9-]+)\s*:\s*([^;]+);")
HEX_PATTERN = re.compile(r"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")
VAR_PATTERN = re.compile(r"^var\(\s*(--[a-z0-9-]+)\s*\)$")
CSS_VAR_PATTERN = re.compile(r"^var\(\s*(--[a-z0-9-]+)\s*(?:,\s*(.+))?\)$")
HSL_PATTERN = re.compile(r"^hsl\(\s*(.+)\s*\)$")
MIX_PATTERN = re.compile(
    r"^color-mix\(in srgb,\s*(.+?)\s+(\d+(?:\.\d+)?)%\s*,\s*(.+?)\)$"
)

# (name, fg token, bg token, minimum ratio)
# 4.5 = AA normal text · 3.0 = AA large text / graphical objects (dots, checkmark glyphs).
PAIRS = [
    ("body text on card",          "--text-primary",   "--bg-light",     4.5),
    ("body text on canvas",        "--text-primary",   "--bg",           4.5),
    ("body text on header band",   "--text-primary",   "--bg-medium",    4.5),
    ("secondary text on card",     "--text-secondary", "--bg-light",     4.5),
    ("muted text on card",         "--text-muted",     "--bg-light",     4.5),
    ("muted text on header band",  "--text-muted",     "--bg-medium",    4.5),
    ("muted text on hover row",    "--text-muted",     "--bg-hover",     4.5),
    # Signal is an identity/plot role, not body text or a primary action. Measure the
    # published action and interaction roles rather than imposing the old copper UI.
    ("signal on card (non-text)", "--accent", "--bg-light", 3.0),
    ("signal on header band (non-text)", "--accent", "--bg-medium", 3.0),
    ("brand-signal-text text on card", "--brand-signal-text", "--bg-light", 4.5),
    ("brand-signal-text text on header band", "--brand-signal-text", "--bg-medium", 4.5),
    ("primary button label",       "--ink-inverse", "--brand-axis", 4.5),
    ("hover button label",         "--ink-inverse", "--check-primary-hover", 4.5),
    ("pressed button label",       "--ink-inverse", "--check-primary-pressed", 4.5),
    ("signal-fill label",          "--text-on-accent", "--accent", 4.5),
    ("hover signal-fill label",    "--text-on-accent", "--accent-hover", 4.5),
    ("pressed signal-fill label",  "--text-on-accent", "--accent-dim", 4.5),
    ("link text on card",          "--interaction-link", "--bg-light", 4.5),
    ("link text on header band",   "--interaction-link", "--bg-medium", 4.5),
    ("hover link text on card",    "--interaction-link-hover", "--bg-light", 4.5),
    ("selected row text",          "--text-primary", "--selected-surface", 4.5),
    ("body text on signal ghost",  "--text-primary", "--accent-ghost", 4.5),
    ("selection edge on card",     "--interaction-selected", "--bg-light", 3.0),
    ("focus ring on card",         "--interaction-focus", "--bg-light", 3.0),
    ("LIVE badge label",           "--text-on-fill",   "--mode-live",    4.5),
    ("PAPER badge label",          "--text-on-fill",   "--mode-paper",   4.5),
    ("FIXTURE badge label",        "--text-on-fill",   "--mode-fixture", 4.5),
    # Green fill carries only the stepper checkmark glyph → graphical-object threshold.
    ("checkmark on green fill",    "--text-on-fill",   "--green",        3.0),
    ("green-dim text on card",     "--green-dim",      "--bg-light",     4.5),
    ("red-dim text on card",       "--red-dim",        "--bg-light",     4.5),
    ("orange-dim text on card",    "--orange-dim",     "--bg-light",     4.5),
    ("topbar text",                "--topbar-text",       "--topbar-bg", 4.5),
    ("topbar muted text",          "--topbar-text-muted", "--topbar-bg", 4.5),
    ("topbar faint text",          "--topbar-text-faint", "--topbar-bg", 4.5),
    ("statusbar text",             "--statusbar-text", "--statusbar-bg", 4.5),
    ("chrome ok dot",              "--chrome-ok",      "--topbar-bg",    3.0),
    ("chrome warn dot",            "--chrome-warn",    "--topbar-bg",    3.0),
    ("chrome err dot",             "--chrome-err",     "--topbar-bg",    3.0),
]

# Tinted-chip pairs: `-dim` text sitting ON an alpha wash. The wash is translucent, so the
# effective background is the wash composited over the surface it sits on:
# (name, fg token, hue token, wash fraction, surface token, minimum)
WASH_PAIRS = [
    ("green-dim on green-a10 wash",   "--green-dim",  "--green",  0.10, "--bg-light", 4.5),
    ("red-dim on red-a10 wash",       "--red-dim",    "--red",    0.10, "--bg-light", 4.5),
    ("orange-dim on orange-a10 wash", "--orange-dim", "--orange", 0.10, "--bg-light", 4.5),
    ("purple-dim on purple-a10 wash", "--purple-dim", "--purple", 0.10, "--bg-light", 4.5),
    ("green-dim on green-a20 wash",   "--green-dim",  "--green",  0.20, "--bg-light", 4.5),
    # A signal wash lifts the surface toward its hue. Graphic marks still need
    # 3:1, while the migrated normal text uses the separate AA ink role.
    ("accent on accent wash (non-text)", "--accent",     "--accent", 0.10, "--bg-light",  3.0),
    ("signal ink on signal wash",       "--brand-signal-ink", "--accent", 0.10, "--bg-light",  4.5),
    ("signal ink on wash, canvas",      "--brand-signal-ink", "--accent", 0.10, "--bg",        4.5),
    ("signal ink on wash, band",        "--brand-signal-ink", "--accent", 0.10, "--bg-medium", 4.5),
    ("signal text on signal wash",      "--brand-signal-text", "--accent", 0.10, "--bg-light", 4.5),
    ("signal text on wash, canvas",     "--brand-signal-text", "--accent", 0.10, "--bg",       4.5),
    ("signal text on wash, band",       "--brand-signal-text", "--accent", 0.10, "--bg-medium", 4.5),
    ("red-dim on red-a20 wash",       "--red-dim",    "--red",    0.20, "--bg-light", 4.5),
    # The actual status chips, foreground token against its own wash, on each surface a chip
    # sits on. Checking only the -dim tokens in the abstract missed that the *-fg tokens were
    # wired to the raw hue: four of them measured under 4.5:1 as shipped.
    ("severity-review chip",   "--severity-review-fg",  "--accent", 0.10, "--bg-light",  4.5),
    ("severity-review on band","--severity-review-fg",  "--accent", 0.10, "--bg-medium", 4.5),
    ("severity-ready chip",    "--severity-ready-fg",   "--green",  0.10, "--bg-light",  4.5),
    ("severity-blocked chip",  "--severity-blocked-fg", "--red",    0.10, "--bg-light",  4.5),
    ("severity-action chip",   "--severity-action-fg",  "--orange", 0.11, "--bg-light",  4.5),
    ("state-healthy chip",     "--state-healthy-fg",    "--green",  0.10, "--bg-light",  4.5),
    ("state-danger chip",      "--state-danger-fg",     "--red",    0.10, "--bg-light",  4.5),
    # Paper and pending retain their Field-slate and warm-taupe meanings.
    ("state-paper chip",       "--state-paper-fg",      "--brand-field", 0.10, "--bg-light",  4.5),
    ("state-strategy chip",    "--state-strategy-fg",   "--purple", 0.10, "--bg-light",  4.5),
    ("state-live chip",        "--state-live-fg",       "--red",    0.12, "--bg-light",  4.5),
    ("state-pending chip",     "--state-pending-fg",    "--pending", 0.10, "--bg-light",  4.5),
]

CHIP_ROLES = (
    "severity-blocked", "severity-action", "severity-review", "severity-ready", "severity-info",
    "state-healthy", "state-warn", "state-danger", "state-paper", "state-strategy",
    "state-live", "state-muted", "state-pending",
)
CHIP_SURFACES = ("--bg", "--bg-light", "--bg-medium", "--card-surface-raised")


def blank_comments(text: str) -> str:
    """Like strip_comments, but keeps every newline so line numbers stay true."""
    return re.sub(r"/\*.*?\*/", lambda m: re.sub(r"[^\n]", " ", m.group(0)), text, flags=re.DOTALL)


def strip_comments(text: str) -> str:
    """Remove /* ... */ the way a CSS parser does, so this script sees what a browser sees.

    Matching tokens against raw text hides a whole class of bug: prose containing `*/` (for
    example "--theme-*/base") closes its comment early, the browser then discards the rule
    that follows, and a regex-based checker still reports the tokens as present. That happened
    to the --ws-* compatibility block, which shipped entirely undefined behind a green gate.
    """
    return re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)


def blank_source_comments(text: str, suffix: str) -> str:
    """Mask source comments without hiding CSS held in JavaScript string literals."""
    if suffix == ".css":
        return blank_comments(text)
    result = list(text)
    index, quote = 0, None
    while index < len(text):
        character = text[index]
        if quote:
            if character == "\\":
                index += 2
                continue
            if character == quote:
                quote = None
            index += 1
            continue
        if character in "\"'`":
            quote = character
            index += 1
            continue
        close = None
        if character == "/" and not text.startswith(("//", "/*"), index):
            previous = index - 1
            while previous >= 0 and text[previous].isspace():
                previous -= 1
            prefix = text[max(0, previous - 16):previous + 1]
            starts_regex = (previous < 0 or text[previous] in "=(:,[!&|?;{}"
                            or re.search(r"(?<![.\w])(?:return|case|throw|yield)$", prefix))
            if starts_regex:
                cursor, character_class = index + 1, False
                while cursor < len(text) and text[cursor] not in "\r\n":
                    token = text[cursor]
                    if token == "\\":
                        cursor += 2
                        continue
                    if token == "[":
                        character_class = True
                    elif token == "]":
                        character_class = False
                    elif token == "/" and not character_class:
                        for offset in range(index, cursor + 1):
                            result[offset] = " "
                        index = cursor + 1
                        break
                    cursor += 1
                else:
                    cursor = None
                if cursor is not None:
                    continue
        if text.startswith("/*", index):
            stop = text.find("*/", index + 2)
            close = len(text) if stop < 0 else stop + 2
        elif text.startswith("//", index) and (index == 0 or text[index - 1] != "\\"):
            stop = text.find("\n", index + 2)
            close = len(text) if stop < 0 else stop
        elif text.startswith("<!--", index):
            stop = text.find("-->", index + 4)
            close = len(text) if stop < 0 else stop + 3
        if close is not None:
            for offset in range(index, close):
                if result[offset] not in "\r\n":
                    result[offset] = " "
            index = close
        else:
            index += 1
    return "".join(result)


def is_test_source(path: Path) -> bool:
    return bool(re.search(r"\.(?:test|spec)\.[cm]?[jt]sx?$", path.name))


def parse_tokens(text: str) -> dict[str, str]:
    """First occurrence wins — colors-dark.css declares the dark media block first."""
    tokens: dict[str, str] = {}
    for name, value in TOKEN_PATTERN.findall(strip_comments(text)):
        if name not in tokens:
            tokens[name] = value.strip()
    return tokens


def hex_to_rgb(value: str) -> tuple[float, float, float] | None:
    m = HEX_PATTERN.match(value)
    if not m:
        return None
    h = m.group(1)
    if len(h) == 3:
        h = "".join(c * 2 for c in h)
    return tuple(int(h[i : i + 2], 16) for i in (0, 2, 4))  # type: ignore[return-value]


def split_arguments(value: str, delimiter: str = ",") -> list[str]:
    """Split CSS function arguments without splitting nested var()/color-mix()."""
    parts, start, depth = [], 0, 0
    for index, char in enumerate(value):
        if char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
        elif char == delimiter and depth == 0:
            parts.append(value[start:index].strip())
            start = index + 1
    parts.append(value[start:].strip())
    return parts


def expand_variables(value: str, tokens: dict[str, str], seen: tuple[str, ...] = ()) -> str | None:
    """Resolve custom-property aliases, including fallbacks; cycles remain invalid."""
    if len(seen) > 32:
        return None
    while "var(" in value:
        start = value.index("var(")
        cursor, depth = start + 4, 1
        while cursor < len(value) and depth:
            depth += (value[cursor] == "(") - (value[cursor] == ")")
            cursor += 1
        if depth:
            return None
        arguments = split_arguments(value[start + 4:cursor - 1])
        name = arguments[0]
        if not re.fullmatch(r"--[a-z0-9-]+", name):
            return None
        if name in seen:
            return None
        replacement = None
        if name in tokens:
            replacement = expand_variables(tokens[name], tokens, (*seen, name))
        if replacement is None and len(arguments) > 1:
            replacement = expand_variables(",".join(arguments[1:]), tokens, seen)
        if replacement is None:
            return None
        value = value[:start] + replacement + value[cursor:]
    return value.strip()


def resolve_color(value: str, tokens: dict[str, str]) -> tuple[float, float, float, float] | None:
    """Resolve the CSS colors used by primary actions, retaining alpha for compositing.

    HSL channel properties, rgb()/hsl(), var() fallbacks and premultiplied sRGB
    color-mix() are supported. Unsupported expressions fail instead of becoming
    a guessed color or an omitted required pair.
    """
    value = expand_variables(value.strip(), tokens)
    if value is None:
        return None
    value = value.strip()
    if value.lower() in {"black", "white", "transparent"}:
        return {"black": (0., 0., 0., 1.), "white": (255., 255., 255., 1.),
                "transparent": (0., 0., 0., 0.)}[value.lower()]
    match = re.fullmatch(r"#([a-f0-9]{3,4}|[a-f0-9]{6}|[a-f0-9]{8})", value, re.I)
    if match:
        digits = match.group(1)
        if len(digits) in (3, 4):
            digits = "".join(char * 2 for char in digits)
        rgb = tuple(float(int(digits[index:index + 2], 16)) for index in (0, 2, 4))
        return (*rgb, int(digits[6:8], 16) / 255 if len(digits) == 8 else 1.)
    mix = re.fullmatch(r"color-mix\(\s*in\s+srgb\s*,(.*)\)", value, re.I | re.S)
    if mix:
        arguments = split_arguments(mix.group(1))
        if len(arguments) != 2:
            return None
        colors, weights = [], []
        for argument in arguments:
            weight = re.search(r"\s+([0-9]+(?:\.[0-9]+)?)%$", argument)
            colors.append(resolve_color(argument[:weight.start()] if weight else argument, {}))
            weights.append(float(weight.group(1)) / 100 if weight else None)
        if any(color is None for color in colors):
            return None
        if weights == [None, None]:
            weights = [.5, .5]
        elif weights[0] is None:
            weights[0] = 1 - weights[1]
        elif weights[1] is None:
            weights[1] = 1 - weights[0]
        if any(weight < 0 or weight > 1 for weight in weights):
            return None
        total = sum(weights)
        if total == 0:
            return None
        weights = [weight / total for weight in weights]
        alpha = sum(color[3] * weight for color, weight in zip(colors, weights))
        channels = tuple(sum(color[index] * color[3] * weight for color, weight in zip(colors, weights)) / alpha
                         if alpha else 0. for index in range(3))
        return (*channels, alpha * min(total, 1.))
    function = re.fullmatch(r"(rgba?|hsla?)\((.*)\)", value, re.I | re.S)
    # Some consumers store HSL channels directly; Tailwind wraps them in hsl().
    if function:
        kind, channels = function.group(1).lower(), function.group(2)
    elif re.fullmatch(r"[-+0-9.]+(?:deg|turn|rad)?\s+[-+0-9.]+%\s+[-+0-9.]+%", value):
        kind, channels = "hsl", value
    else:
        return None
    pieces = re.split(r"\s*(?:,|/)\s*|\s+", channels.strip())
    if len(pieces) not in (3, 4):
        return None
    try:
        alpha = float(pieces[3].rstrip("%")) / (100 if pieces[3].endswith("%") else 1) if len(pieces) == 4 else 1.
        if not 0 <= alpha <= 1:
            return None
        if kind.startswith("rgb"):
            rgb = tuple(float(part.rstrip("%")) / (100 if part.endswith("%") else 255) * 255 for part in pieces[:3])
            return (*tuple(min(255., max(0., channel)) for channel in rgb), alpha)
        hue = re.fullmatch(r"([-+0-9.]+)(deg|turn|rad)?", pieces[0])
        if hue is None or not pieces[1].endswith("%") or not pieces[2].endswith("%"):
            return None
        angle = float(hue.group(1)) * {None: 1, "deg": 1, "turn": 360, "rad": 180 / 3.141592653589793}[hue.group(2)]
        saturation, lightness = (min(1., max(0., float(part[:-1]) / 100)) for part in pieces[1:3])
        return (*tuple(channel * 255 for channel in colorsys.hls_to_rgb((angle % 360) / 360, lightness, saturation)), alpha)
    except ValueError:
        return None


def resolve(value: str, tokens: dict[str, str], depth: int = 0) -> tuple[float, float, float] | None:
    """Compatibility API for checks requiring an opaque color."""
    color = resolve_color(value, tokens)
    return color[:3] if color is not None and color[3] == 1 else None



def luminance(rgb: tuple[float, float, float]) -> float:
    def channel(c: float) -> float:
        c /= 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

    r, g, b = (channel(c) for c in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def ratio(fg: tuple[float, float, float], bg: tuple[float, float, float]) -> float:
    l1, l2 = sorted((luminance(fg), luminance(bg)), reverse=True)
    return (l1 + 0.05) / (l2 + 0.05)


def composite(value: str, tokens: dict[str, str], surface: tuple[float, float, float],
              depth: int = 0) -> tuple[float, float, float] | None:
    """Resolve a chip fill over its actual surface, retaining transparent wash alpha."""
    if depth > 8:
        return None
    value = value.strip()
    alias = VAR_PATTERN.match(value)
    if alias:
        target = tokens.get(alias.group(1))
        return composite(target, tokens, surface, depth + 1) if target else None
    mix = MIX_PATTERN.match(value)
    if mix and mix.group(3).strip() == "transparent":
        hue = resolve(mix.group(1), tokens)
        if hue is None:
            return None
        fraction = float(mix.group(2)) / 100.0
        return tuple(hue[i] * fraction + surface[i] * (1 - fraction) for i in range(3))
    return resolve(value, tokens)


def check_semantic_chips(mode: str, tokens: dict[str, str]) -> list[str]:
    """Measure exported chip roles against their own fill, rather than an assumed hue."""
    failures = []
    for role in CHIP_ROLES:
        foreground = resolve(f"var(--{role}-fg)", tokens)
        for surface_name in CHIP_SURFACES:
            surface = resolve(f"var({surface_name})", tokens)
            background = composite(f"var(--{role}-bg)", tokens, surface) if surface else None
            if foreground is None or background is None:
                failures.append(f"[{mode}] {role} on {surface_name}: cannot resolve chip colours")
                continue
            value = ratio(foreground, background)
            if value < 4.5:
                failures.append(f"[{mode}] {role} on {surface_name}: {value:.2f}:1 < 4.5:1")
    if not failures:
        print(f"[{mode}] all {len(CHIP_ROLES)} semantic chip roles pass AA on {len(CHIP_SURFACES)} surfaces")
    return failures


def check_signal_ink(mode: str, tokens: dict[str, str]) -> list[str]:
    """Measure both published signal text roles on materials, selection and washes."""
    failures = []
    for name, role in (("signal ink", "--brand-signal-ink"), ("signal text", "--brand-signal-text")):
        foreground = resolve(f"var({role})", tokens)
        for background_name in (*CHIP_SURFACES, "--bg-active", "--accent-ghost", "--blue-a10"):
            backdrops = CHIP_SURFACES if background_name == "--blue-a10" else ("--bg-light",)
            for backdrop_name in backdrops:
                backdrop = resolve(f"var({backdrop_name})", tokens)
                background = composite(f"var({background_name})", tokens, backdrop) if backdrop else None
                if foreground is None or background is None:
                    failures.append(f"[{mode}] {name} on {background_name}: cannot resolve actual roles")
                elif ratio(foreground, background) < 4.5:
                    failures.append(f"[{mode}] {name} on {background_name}: {ratio(foreground, background):.2f}:1 < 4.5:1")
    return failures


def check_mode(mode: str, tokens: dict[str, str]) -> list[str]:
    tokens = action_tokens(tokens)
    failures = []
    for name, fg_tok, bg_tok, minimum in PAIRS:
        fg = resolve(f"var({fg_tok})", tokens)
        bg = resolve(f"var({bg_tok})", tokens)
        if fg is None or bg is None:
            missing = fg_tok if fg is None else bg_tok
            failures.append(f"[{mode}] {name}: cannot resolve {missing}")
            continue
        r = ratio(fg, bg)
        status = "ok" if r >= minimum else "FAIL"
        print(f"[{mode}] {name}: {r:.2f}:1 (min {minimum}) {status}")
        if r < minimum:
            failures.append(f"[{mode}] {name}: {r:.2f}:1 < {minimum}:1 ({fg_tok} on {bg_tok})")
    for name, fg_tok, hue_tok, frac, surface_tok, minimum in WASH_PAIRS:
        fg = resolve(f"var({fg_tok})", tokens)
        hue = resolve(f"var({hue_tok})", tokens)
        surface = resolve(f"var({surface_tok})", tokens)
        if fg is None or hue is None or surface is None:
            failures.append(f"[{mode}] {name}: cannot resolve a token")
            continue
        bg = tuple(hue[i] * frac + surface[i] * (1 - frac) for i in range(3))
        r = ratio(fg, bg)
        status = "ok" if r >= minimum else "FAIL"
        print(f"[{mode}] {name}: {r:.2f}:1 (min {minimum}) {status}")
        if r < minimum:
            failures.append(f"[{mode}] {name}: {r:.2f}:1 < {minimum}:1 ({fg_tok} on {hue_tok} wash)")
    return failures


BRAND_BLOCK = re.compile(r'html\[data-brand="(\w+)"\]\s*\{(.*?)\n\}', re.S)

# Filled signal states a brand can restate. Each uses its declared signal label;
# structural primary actions are measured separately from the Button source.
BRAND_PAIRS = [("base", "accent"), ("hover", "accent-hover"), ("pressed", "accent-dim")]


def appearance_scopes(light: dict[str, str], dark_css: str) -> dict[str, dict[str, str]]:
    """Resolve the current source's default, OS-dark, manual-dark and forced-light scopes."""
    modes = {"light": light}
    patterns = {
        "auto dark": r'@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root(?::not\(\[data-theme="light"\]\))?\s*\{([^{}]+)\}',
        "manual dark": r':root\[data-theme="dark"\]\s*\{([^{}]+)\}',
        "forced light": r':root\[data-theme="light"\]\s*\{([^{}]+)\}',
    }
    for name, pattern in patterns.items():
        match = re.search(pattern, strip_comments(dark_css), re.S)
        if match:
            modes[name] = {**light, **parse_tokens(match.group(1))}
    return modes


def check_primary_buttons(root: Path, mode: str, tokens: dict[str, str]) -> list[str]:
    """Measure the published Button's structural action rules, including hover and press."""
    source = root / "components/core/Button.jsx"
    if not source.exists():
        return [f"[{mode}] primary button source is missing"]
    rules = {
        state or "base": body
        for state, body in re.findall(r"\.mds-btn--primary(:hover|:active)?\s*\{([^{}]+)\}",
                                     source.read_text(encoding="utf-8"))
    }
    failures = []
    base = rules.get("base", "")
    label_match = re.search(r"(?:^|;)\s*color\s*:\s*([^;]+)", base)
    label = resolve(label_match.group(1), tokens) if label_match else None
    for state in ("base", ":hover", ":active"):
        fill_match = re.search(r"(?:^|;)\s*background\s*:\s*([^;]+)", rules.get(state, ""))
        fill = resolve(fill_match.group(1), tokens) if fill_match else None
        if label is None or fill is None:
            failures.append(f"[{mode}] primary button {state}: cannot resolve actual label/fill")
        elif ratio(label, fill) < 4.5:
            failures.append(f"[{mode}] primary button {state}: {ratio(label, fill):.2f}:1 < 4.5:1")
    return failures


def check_brands(root: Path) -> list[str]:
    """Check computed semantic roles, including inherited roles, in every brand/mode."""
    text = (root / "tokens/theme.css").read_text(encoding="utf-8")
    brands: dict[str, dict[str, str]] = {}
    for brand, body in BRAND_BLOCK.findall(text):
        brands.setdefault(brand, {}).update(parse_tokens(body))
    failures = []
    for brand, declared in brands.items():
        defaults = parse_tokens(BRAND_BLOCK.sub("", text))
        if "--theme-accent" in declared:
            required = {name for name in defaults if name.startswith("--theme-accent") or name.startswith("--theme-dark-accent")}
            required.add("--theme-text-on-accent")
            for missing in sorted(required - set(declared)):
                failures.append(f"[brand {brand}] missing declared identity role {missing}")
        for mode, tokens in package_modes(root, declared, brand).items():
            scope = f"{mode} · brand {brand}"
            failures.extend(check_mode(scope, tokens))
            for state, suffix in BRAND_PAIRS:
                label = resolve("var(--text-on-accent)", tokens)
                fill = resolve(f"var(--{suffix})", tokens)
                if label is None or fill is None or ratio(label, fill) < 4.5:
                    failures.append(f"[{mode.replace("-", " ")} · brand {brand}] {state} signal label: below 4.5:1 or unresolved")
            failures.extend(check_semantic_chips(scope, tokens))
            failures.extend(check_signal_ink(scope, tokens))
            if (root / "components/core/Button.jsx").exists():
                failures.extend(check_primary_buttons(root, scope, tokens))
            expected_role = "--theme-accent" if mode in ("light", "forced-light") else "--theme-dark-accent"
            if expected_role in declared:
                expected = resolve(declared[expected_role], tokens)
                actual = resolve("var(--accent)", tokens)
                if actual is None or expected is None or actual != expected:
                    failures.append(f"[{mode} · brand {brand}] declared signal identity is overridden by the default palette")
    return failures


# The browser adapter now uses structural primary roles. Read its actual state classes;
# brand-signal fills and invented white labels are not the adapter's contrast contract.
WS_INDEX = Path("src/Meridian.Ui/dashboard/src/styles/index.css")
WS_BUTTON = Path("src/Meridian.Ui/dashboard/src/design-system/button.tsx")


def button_colour_utility(classes: list[str], kind: str, state: str = "") -> str | None:
    """Read one actual Tailwind colour expression; ambiguous/unknown utilities fail closed."""
    prefix = f"{state}:" if state else ""
    values = [value[len(prefix + kind + "-"):] for value in classes
              if value.startswith(prefix + kind + "-")]
    if len(values) != 1:
        return None
    value = values[0]
    if value.startswith("[") and value.endswith("]"):
        # Tailwind arbitrary values encode CSS whitespace with underscores.
        return value[1:-1].replace("_", " ")
    return {("bg", "primary"): "hsl(var(--primary))",
            ("text", "primary-foreground"): "hsl(var(--primary-foreground))"}.get((kind, value))


def css_leaf_rules(text: str):
    """Yield simple CSS rules with enclosing at-rules, preserving source order.

    This is deliberately scoped to the flat declaration blocks authored in the
    package token sheets. It handles quotes/braces and nested @layer/@media,
    rather than letting an auto-dark regex consume all later manual/light rules.
    """
    text = strip_comments(text)
    stack, segment, quote, escaped = [], 0, None, False
    for index, char in enumerate(text):
        if quote:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = None
            continue
        if char in ("'", '"'):
            quote = char
        elif char == ";":
            segment = index + 1
        elif char == "{":
            prelude = text[segment:index].strip()
            if stack:
                stack[-1]["children"] = True
            stack.append({"selector": prelude, "start": index + 1, "children": False})
            segment = index + 1
        elif char == "}" and stack:
            block = stack.pop()
            if not block["children"]:
                yield block["selector"], text[block["start"]:index], tuple(item["selector"] for item in stack)
            segment = index + 1


def root_selector_specificity(selector: str, brand: str | None, appearance: str | None):
    """Match the authored root/brand/theme selectors and retain their cascade priority."""
    root = re.match(r"(:root|html)", selector)
    if root is None:
        return None
    rest = selector[root.end():]
    classes, elements = (1, 0) if root.group() == ":root" else (0, 1)
    attributes = {"brand": brand, "theme": appearance}
    while rest:
        negative = rest.startswith(":not(")
        candidate = rest[5:] if negative else rest
        attribute = re.match(r'''\[data-(brand|theme)(?:=["']?([a-z0-9-]+)["']?)?\]''', candidate)
        if attribute is None:
            return None
        rest = candidate[attribute.end():]
        if negative:
            if not rest.startswith(")"):
                return None
            rest = rest[1:]
        actual, expected = attributes[attribute.group(1)], attribute.group(2)
        matches = actual is not None if expected is None else actual == expected
        if matches == negative:
            return None
        classes += 1  # :not() contributes its attribute argument's specificity.
    return classes, elements


def mode_tokens(text: str, brand: str | None = None) -> dict[str, dict[str, str]]:
    """Resolve actual root/brand/theme rules by specificity and source order."""
    modes = {"light": (False, None), "auto-dark": (True, None),
             "manual-dark": (False, "dark"), "forced-light": (True, "light")}
    contexts = {name: {} for name in modes}
    priorities = {name: {} for name in modes}
    for selector_list, body, ancestors in css_leaf_rules(text):
        media = [ancestor for ancestor in ancestors if ancestor.startswith("@media")]
        # Print/high-contrast modes remain separate user choices.
        if any(not re.search(r"prefers-color-scheme\s*:\s*dark", ancestor) for ancestor in media):
            continue
        values = {name: value.strip() for name, value in TOKEN_PATTERN.findall(body)}
        for mode, (os_dark, appearance) in modes.items():
            if media and not os_dark:
                continue
            for selector in split_arguments(selector_list):
                specificity = root_selector_specificity(selector, brand, appearance)
                if specificity is None:
                    continue
                for name, value in values.items():
                    if specificity >= priorities[mode].get(name, (-1, -1)):
                        contexts[mode][name] = value
                        priorities[mode][name] = specificity
    return contexts


def declarations(body: str) -> dict[str, str]:
    # Background longhand participates in the same cascade as the color part of
    # the shorthand; otherwise a safe base fill could hide an unsafe hover fill.
    return {"background" if name == "background-color" else name: value.strip()
            for name, value in re.findall(r"(?:^|;)\s*([a-z-]+)\s*:\s*([^;]+)", body)}


def primary_css_states(text: str) -> dict[str, tuple[str, str]]:
    """Read the package Button's actual background/color cascade."""
    selectors = {".mds-btn--primary": "base", ".mds-btn--primary:hover": "hover", ".mds-btn--primary:active": "pressed"}
    rules = {}
    for selector_list, body, _ in css_leaf_rules(text):
        for selector in split_arguments(selector_list):
            if selector in selectors:
                rules.setdefault(selectors[selector], {}).update(declarations(body))
    base = rules.get("base", {})
    # Missing required declarations are deliberately not supplied guessed colors.
    states = {}
    for state in ("base", "hover", "pressed"):
        values = {**base, **rules.get(state, {})}
        background = values.get("background", "")
        if values.get("background-image", "none") != "none":
            background = "unsupported layered primary background: " + values["background-image"]
        states[state] = (values.get("color", ""), background)
    return states


def composite_color(color, surface):
    return tuple(color[index] * color[3] + surface[index] * (1 - color[3]) for index in range(3))


def check_primary_states(track: str, contexts: dict[str, dict[str, str]], states: dict[str, tuple[str, str]]) -> list[str]:
    failures = []
    for mode, tokens in contexts.items():
        for state, (foreground, background) in states.items():
            fg, bg = resolve_color(foreground, tokens), resolve_color(background, tokens)
            if fg is None or bg is None:
                role = "foreground" if fg is None else "background"
                expression = foreground if fg is None else background
                failures.append(f"[{mode} · {track}] structural primary {state}: cannot resolve required {role} {expression!r}")
                continue
            if bg[3] < 1:
                surface = resolve_color("var(--bg-light)", tokens)
                if surface is None or surface[3] != 1:
                    failures.append(f"[{mode} · {track}] structural primary {state}: cannot resolve required opaque supporting surface --bg-light")
                    continue
                effective_background = composite_color(bg, surface)
            else:
                effective_background = bg[:3]
            value = ratio(composite_color(fg, effective_background), effective_background)
            status = "ok" if value >= 4.5 else "FAIL"
            print(f"[{mode} · {track}] structural primary {state} label: {value:.2f}:1 (min 4.5) {status}")
            if value < 4.5:
                failures.append(f"[{mode} · {track}] structural primary {state} label: {value:.2f}:1 < 4.5")
    return failures


def check_package_primary(root: Path) -> list[str]:
    component = root / "components/core/Button.jsx"
    sheets = [root / "tokens" / name for name in ("colors.css", "theme.css", "colors-dark.css")]
    if not component.exists() or any(not sheet.exists() for sheet in sheets):
        return ["[package] structural primary: required Button source or token stylesheet missing"]
    source = component.read_text(encoding="utf-8")
    css = re.search(r"\bconst\s+css\s*=\s*`(.*?)`", source, re.S)
    if css is None:
        return ["[package] structural primary: cannot read required Button CSS contract"]
    contexts = mode_tokens("\n".join(sheet.read_text(encoding="utf-8") for sheet in sheets))
    return check_primary_states("package", contexts, primary_css_states(css.group(1)))


def utility_color(classes: str, property_name: str, prefix: str = "") -> str:
    """Use the same fail-closed parser for named legacy and arbitrary color classes."""
    return button_colour_utility(classes.split(), property_name, prefix.rstrip(":")) or ""


def check_workstation(root: Path) -> list[str]:
    """Read the consuming adapter's primary utilities and its scoped CSS tokens."""
    index, adapter = root.parent / WS_INDEX, root.parent / WS_BUTTON
    if not index.exists() and not adapter.parent.exists() and not (root.parent / "Meridian.sln").exists():
        print("[workstation] outside verification scope: standalone package has no consuming workstation")
        return []
    if not index.exists() or not adapter.exists():
        return ["[workstation] structural primary: required stylesheet or button adapter missing"]
    source = adapter.read_text(encoding="utf-8")
    variants = re.search(r"\bconst\s+buttonVariantClasses\b[^=]*=\s*\{(.*?)\};", source, re.S)
    default = re.search(r'\bdefault:\s*"([^"]+)"', variants.group(1)) if variants else None
    if default is None:
        return ["[workstation] structural primary: cannot read required default button utility contract"]
    classes = default.group(1)
    semantic_failures = []
    if re.search(r"(?:^|\s)(?:hover:|active:)?bg-\[var\(--(?:ws-)?accent", classes):
        semantic_failures.append("[workstation] the primary button paints an accent fill; signal fills are not structural primary actions")
    label = utility_color(classes, "text")
    base = utility_color(classes, "bg")
    states = {"base": (label, base),
              "hover": (utility_color(classes, "text", "hover:") if "hover:text-" in classes else label,
                        utility_color(classes, "bg", "hover:") if "hover:bg-" in classes else base),
              "pressed": (utility_color(classes, "text", "active:") if "active:text-" in classes else label,
                          utility_color(classes, "bg", "active:") if "active:bg-" in classes else base)}
    return semantic_failures + check_primary_states("workstation", mode_tokens(index.read_text(encoding="utf-8")), states)



# The package publishes a --ws-* compatibility layer beside the canonical tokens. The two
# tracks are meant to carry the same colours; when only one is updated, a consumer using the
# documented paste path gets a palette that disagrees with the canonical contract. This has
# now drifted three times, so it is checked rather than remembered.
WS_ALIASES = [
    ("--ws-accent", "--accent"), ("--ws-accent-hover", "--accent-hover"),
    ("--ws-accent-pressed", "--accent-dim"), ("--ws-page-bg", "--bg"),
    ("--ws-surface", "--bg-light"), ("--ws-surface-subtle", "--bg-medium"),
    ("--ws-border", "--border"), ("--ws-border-strong", "--border-strong"),
    ("--ws-text", "--text-primary"), ("--ws-text-muted", "--text-muted"),
]


def check_ws_aliases(root: Path) -> list[str]:
    """The --ws-* compatibility layer must agree with the canonical tokens it mirrors."""
    tokens = parse_tokens((root / "tokens" / "colors.css").read_text(encoding="utf-8"))
    failures: list[str] = []
    for ws, canon in WS_ALIASES:
        if ws not in tokens or canon not in tokens:
            failures.append(f"[ws-alias] {ws} or {canon} is not declared — "
                            "check the block was not discarded by an early comment terminator")
            print(f"[ws-alias] {ws}/{canon}: MISSING")
            continue
        a, b = resolve(f"var({ws})", tokens), resolve(f"var({canon})", tokens)
        if a is None or b is None:
            failures.append(f"[ws-alias] cannot resolve required alias pair {ws}/{canon}")
            continue
        if a != b:
            failures.append(f"[ws-alias] {ws} does not mirror {canon}")
            print(f"[ws-alias] {ws} vs {canon}: MISMATCH")
    if not failures:
        print(f"[ws-alias] all {len(WS_ALIASES)} compatibility aliases mirror their canonical token")
    return failures


# A `*/` inside comment prose closes the comment early; the browser then treats the rest of
# the sentence plus the following rule as an invalid qualified rule and discards it. A
# regex-based token parser still sees the declarations, so this is invisible to every other
# check here. It happened to the --ws-* block, which shipped entirely undefined.
CSS_AFTER_COMMENT = re.compile(r"^\s*([{}]|/\*|[.#@:*\[]|[\w-]+\s*[:{,]|$)")


def check_comment_terminators(root: Path) -> list[str]:
    """Flag a `*/` followed by prose rather than CSS — an early comment terminator."""
    failures: list[str] = []
    files = sorted((root / "tokens").glob("*.css")) + sorted(root.glob("*.css"))
    for path in files:
        for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            for m in re.finditer(r"\*/", line):
                after = line[m.end():]
                if after.strip() and not CSS_AFTER_COMMENT.match(after):
                    rel = path.relative_to(root)
                    failures.append(f"[comment] {rel}:{lineno}: '*/' closes the comment early, "
                                    f"leaving prose: {after.strip()[:48]!r}")
                    print(f"[comment] {rel}:{lineno}: early terminator")
    if not failures:
        print(f"[comment] no early comment terminators in {len(files)} stylesheet(s)")
    return failures


# Two checks that model what the browser does with a reference, rather than whether the text
# of a declaration is present. Everything above matches tokens by name, and twice now that has
# reported a token as healthy while the browser rendered nothing: once when a stray `*/` made
# the parser throw the whole --ws-* block away, and once when the workstation's --*-fg tokens
# pointed at --green-dim and friends that only the package declared.

def _rel(path: Path, root: Path) -> str:
    """Path relative to the repository, so a finding can be opened directly."""
    repo = root.parent
    try:
        return str(path.relative_to(repo))
    except ValueError:
        return str(path)


DECL_NAME = re.compile(r"(--[a-zA-Z0-9-]+)\s*:")
JS_DECL_NAME = re.compile(r"[\"'](--[a-zA-Z0-9-]+)[\"']\s*:")
BARE_REF = re.compile(r"var\(\s*(--[a-zA-Z0-9-]+)\s*\)")

# Each track is self-contained: the browser workstation does not import the package's
# stylesheets, so a token the package declares is not available to it. Paths are resolved
# from an explicit base, because the package also carries a governance snapshot at
# `<package>/src/Meridian.Ui/...` — a frozen pre-restyle copy owned by
# governance-baseline.json, which must not be read as the live workstation track.
PACKAGE_TRACK = ["tokens/colors.css", "tokens/colors-dark.css", "tokens/theme.css"]
WORKSTATION_TRACK = ["src/Meridian.Ui/dashboard/src/styles/index.css"]


def tracks(root: Path) -> dict[str, list[Path]]:
    repo = root.parent
    return {
        "package": [root / rel for rel in PACKAGE_TRACK],
        "workstation": [repo / rel for rel in WORKSTATION_TRACK],
    }


def check_var_chains(root: Path) -> list[str]:
    """Fail when a bare var() names a token its own track never declares.

    Such a declaration is invalid at computed-value time: the custom property becomes the
    guaranteed-invalid value, so consumers fall through to their fallback (rendering the
    literal rather than the token) or, with no fallback, inherit. Nothing else here sees it,
    because the declaration is present in the text either way.
    """
    failures: list[str] = []
    checked = 0
    for track, paths in tracks(root).items():
        files = [f for f in paths if f.exists()]
        if not files:
            continue
        declared: set[str] = set()
        for f in files:
            declared |= set(DECL_NAME.findall(blank_comments(f.read_text(encoding="utf-8"))))
        if track == "package":
            variants = package_modes(root)
        else:
            index_text = files[0].read_text(encoding="utf-8")
            variants = {mode: scoped_tokens(index_text, f':root[data-theme="{mode}"]') for mode in ("light", "dark")}
        for mode, effective in variants.items():
            graph = {name: set(re.findall(r"var\(\s*(--[a-zA-Z0-9-]+)", value)) for name, value in effective.items()}
            complete, cycles = set(), set()
            def visit(name, pending):
                if name in pending:
                    cycles.add(tuple(sorted(pending[pending.index(name):])))
                    return
                if name in complete:
                    return
                for ref in graph.get(name, set()):
                    if ref in graph:
                        visit(ref, [*pending, name])
                complete.add(name)
            for name in graph:
                visit(name, [])
            for cycle in sorted(cycles):
                failures.append(f"[var-chain] {track} {mode}: cyclic custom-property dependency: {', '.join(cycle)}")
        for f in files:
            text = blank_comments(f.read_text(encoding="utf-8"))
            for lineno, line in enumerate(text.splitlines(), 1):
                for ref in BARE_REF.findall(line):
                    checked += 1
                    if ref not in declared:
                        failures.append(f"[var-chain] {track} {_rel(f, root)}:{lineno}: var({ref}) — "
                                        f"this track never declares it, so the declaration is "
                                        f"invalid at computed-value time")
                        print(f"[var-chain] {track} {_rel(f, root)}:{lineno}: var({ref}) UNRESOLVED")
    # The loop above reads the token sheets, which is where a token's *definition* can name a
    # missing sibling. A component can do the same thing with no fallback at all, and that was
    # invisible to every pass here: check_fallbacks only sees a call that HAS a literal, and
    # this one only saw the stylesheets. Round twelve walked straight into it by pointing the
    # package's CandleChart at a --chart-ma20 only the workstation declared.
    # Every sheet the package ships, not just the colour ones: --font-body lives in
    # typography.css and --focus-ring in elevation.css, and a call site may name either.
    package_tokens: set[str] = set()
    for sheet in sorted((root / "tokens").glob("*.css")):
        package_tokens |= set(DECL_NAME.findall(blank_comments(sheet.read_text(encoding="utf-8"))))
    workstation_tokens: set[str] = set()
    for f in tracks(root)["workstation"]:
        if f.exists():
            workstation_tokens |= set(DECL_NAME.findall(blank_comments(f.read_text(encoding="utf-8"))))
    for track, base in call_sites(root):
        if not base.exists():
            continue
        for path in production_paths(base):
            text = blank_source_comments(path.read_text(encoding="utf-8", errors="replace"), path.suffix)
            declared = workstation_tokens if track == "workstation" else package_tokens
            local = set(DECL_NAME.findall(text)) | set(JS_DECL_NAME.findall(text))
            for lineno, line in style_lines(path, text):
                for ref in BARE_REF.findall(line):
                    checked += 1
                    if ref in declared or ref in local or ref in KNOWN_UNDECLARED or ref.startswith(LOCAL_PREFIXES):
                        continue
                    failures.append(f"[var-chain] {_rel(path, root)}:{lineno}: bare var({ref}) with "
                                    "no fallback names a token this track never declares — it "
                                    "renders as nothing")
                    print(f"[var-chain] {_rel(path, root)}:{lineno}: bare var({ref}) UNDECLARED")
    if not failures:
        print(f"[var-chain] all {checked} bare var() references resolve within their own track")
    return failures


DOC_HEX = re.compile(r"#[0-9A-Fa-f]{6}\b")
# The steel brand block IS the superseded identity, kept deliberately as `data-brand="steel"`.
STEEL_BLOCK = re.compile(r'html\[data-brand="steel"\]\s*\{(.*?)\n\}', re.S)
EXTRA_SUPERSEDED = {
    "#2AB2D4", "#08101A", "#06F3FF", "#C06B4A",
    # The pre-restyle chart overlay palette. The steel block holds the superseded
    # *UI* colours only, so these were invisible to the scan that is meant to find
    # exactly this: a live surface still rendering the old identity.
    "#7C5CFF", "#F5A524", "#14B8A6", "#38BDF8", "#EC4899",
}


def superseded_values(root: Path) -> set[str]:
    """Every colour the steel identity holds and the current one does not.

    Subtracting the live palette matters: a brand replaces the *accent*, so it keeps plenty of
    values the default keeps too — `#FFFFFF` for one. Without this, every card that puts white
    ink on a fill reads as "a superseded value stated as current", which is both wrong and the
    kind of noise that gets a check switched off.
    """
    theme = (root / "tokens" / "theme.css").read_text(encoding="utf-8")
    steel = {h.upper() for block in STEEL_BLOCK.findall(theme)
             for h in DOC_HEX.findall(block)} | EXTRA_SUPERSEDED
    current = {h.upper()
               for sheet in ("colors.css", "colors-dark.css")
               for h in DOC_HEX.findall((root / "tokens" / sheet).read_text(encoding="utf-8"))}
    return steel - current


DSCARD = re.compile(r"@dsCard\s+([^>]*?)-->")
DSCARD_ATTR = re.compile(r'(\w+)="([^"]*)"')
CARD_FIELDS = ("group", "viewport", "name", "subtitle")


MIX_ALPHA = re.compile(r"^color-mix\(in srgb,\s*(.+?)\s+(\d+(?:\.\d+)?)%\s*,\s*transparent\s*\)$")
VAR_CALL = re.compile(r"var\(\s*(--[a-zA-Z0-9-]+)\s*(,)?")
HEX_LITERAL = re.compile(r"#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{3}\b")
RGBA_LITERAL = re.compile(r"rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)")
COLOR_LITERAL = re.compile(r"#[0-9A-Fa-f]{8}\b|#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{4}\b|#[0-9A-Fa-f]{3}\b|rgba?\([^()]*\)", re.I)


def literal_colours(text: str) -> list[dict]:
    """Read literal channels and alpha in lexical order, including modern RGB syntax."""
    colours = []
    for match in COLOR_LITERAL.finditer(text):
        value = match.group()
        if value.startswith("#"):
            expanded = "".join(character * 2 for character in value[1:]) if len(value) in (4, 5) else value[1:]
            rgb = tuple(float(int(expanded[offset:offset + 2], 16)) for offset in (0, 2, 4))
            alpha = int(expanded[6:], 16) / 255 if len(expanded) == 8 else 1.0
        else:
            components = re.split(r"\s*[,/]\s*|\s+", value[value.index("(") + 1:-1].strip())
            if len(components) not in (3, 4):
                continue
            try:
                rgb = tuple(float(component.rstrip("%")) * (255 / 100 if component.endswith("%") else 1)
                            for component in components[:3])
                alpha = float(components[3].rstrip("%")) / (100 if components[3].endswith("%") else 1) if len(components) == 4 else 1.0
            except ValueError:
                continue
        if all(0 <= channel <= 255 for channel in rgb) and 0 <= alpha <= 1:
            colours.append({"literal": value, "rgb": rgb, "alpha": alpha, "start": match.start(), "end": match.end()})
    return colours


def var_call_end(text: str, start: int) -> int | None:
    depth, quote, index = 0, None, start
    while index < len(text):
        character = text[index]
        if quote:
            if character == "\\":
                index += 2
                continue
            if character == quote:
                quote = None
        elif character in "\"'":
            quote = character
        elif character == "(":
            depth += 1
        elif character == ")":
            depth -= 1
            if depth == 0:
                return index + 1
        index += 1
    return None


def without_nested_vars(value: str) -> str:
    result, position = list(value), 0
    for match in VAR_CALL.finditer(value):
        if match.start() < position:
            continue
        end = var_call_end(value, match.start())
        if end is None:
            continue
        result[match.start():end] = " " * (end - match.start())
        position = end
    return "".join(result)
INNER_VAR = re.compile(r"var\(\s*(--[a-zA-Z0-9-]+)")
# Directories whose sources carry `var(--token, literal)` call sites. The package's own
# `src/` governance snapshot is deliberately absent: it is a frozen pre-restyle copy.
# `tokens` and `scripts` are call sites too, and both were missing. The token cards paint with
# var(--token, literal) like any component, and scripts/create-workstation.sh *emits* a stylesheet
# — what it writes is the first thing a freshly scaffolded workstation renders, so a stale literal
# there ships to every new consumer while nothing here ever read the file.
PACKAGE_CALL_SITES = ["components", "templates", "guidelines", "tokens", "scripts", "docs"]
WORKSTATION_CALL_SITES = ["src/Meridian.Ui/dashboard/src"]
CALL_SITE_SUFFIXES = (".jsx", ".tsx", ".ts", ".css", ".html", ".sh")


# The compiled bundle is a call site too, and it is the one that actually ships to a
# consumer. Round four found it carrying pre-restyle fallbacks its own sources no longer
# had; checking both means neither side can drift alone again.
COMPILED_BUNDLE = "_ds_bundle.js"

# Tokens a component may reference without the package declaring them: ones it sets on itself
# through an inline style (the `--mds-*` convention) and Tailwind's runtime internals.
LOCAL_PREFIXES = ("--mds-", "--tw-")


def call_sites(root: Path) -> list[tuple[str, Path]]:
    """Each call-site base with the track that owns it.

    The track cannot be inferred from the path text: `templates/dashboard-workstation/` is a
    package template whose name contains "dashboard", and resolving it against the
    workstation's tokens reported forty perfectly good references as undeclared.
    """
    repo = root.parent
    return ([("package", root / rel) for rel in PACKAGE_CALL_SITES]
            + [("workstation", repo / rel) for rel in WORKSTATION_CALL_SITES]
            + [("package", root / COMPILED_BUNDLE)])


def split_var_call(text: str, start: int) -> tuple[str, str] | None:
    """Return (token, fallback) for the `var(` beginning at `start`, honouring nesting.

    A regex cannot do this: a fallback is an arbitrary CSS value and may itself contain
    parentheses (`rgba(…)`) or a whole shorthand (`2px solid #2F6F8F`). Matching only the
    shape `var(--x, #hex)` is what let nineteen composite focus-ring fallbacks stay blue.
    """
    depth, i = 0, start
    while i < len(text):
        if text[i] == "(":
            depth += 1
        elif text[i] == ")":
            depth -= 1
            if depth == 0:
                break
        i += 1
    else:
        return None
    if depth != 0:
        return None
    inner = text[start + len("var(") : i]
    depth = 0
    for j, ch in enumerate(inner):
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        elif ch == "," and depth == 0:
            return inner[:j].strip(), inner[j + 1 :].strip()
    return inner.strip(), ""


def deref(name: str, tokens: dict[str, str], depth: int = 0) -> str:
    """A token's declared value with bare `var(--x)` aliases followed to their source."""
    value = tokens.get(name, "").strip()
    alias = VAR_PATTERN.match(value)
    if alias and depth < 8:
        return deref(alias.group(1), tokens, depth + 1)
    return value


def token_colours(value: str, tokens: dict[str, str], depth: int = 0) -> list[tuple[float, float, float]]:
    """The colours a token's declared value resolves to, in order.

    A plain colour yields one. A composite such as `2px solid var(--border-focus)` yields the
    colour of each var() inside it, so a composite fallback can be compared position by position.
    """
    direct = resolve(value, tokens)
    if direct is not None:
        return [direct]
    if depth > 8:
        return []
    out, position = [], 0
    for match in re.finditer(r"var\(|" + COLOR_LITERAL.pattern, value, re.I):
        if match.start() < position:
            continue
        if match.group().startswith("var("):
            end = var_call_end(value, match.start())
            parsed = split_var_call(value, match.start())
            if end is None or parsed is None:
                continue
            name, fallback = parsed
            out.extend(token_colours(tokens.get(name, fallback), tokens, depth + 1))
            position = end
        else:
            colours = literal_colours(match.group())
            out.extend(colour["rgb"] for colour in colours)
    return out


# Token names that call sites reference and the package has never declared — on `origin/main`
# as well as here. Each one means the literal beside it is what renders, always; none of them
# carries a superseded colour any more, so none is this restyle's defect. They are listed
# rather than skipped so that a *new* undeclared reference fails, and so the remaining gap is
# a reviewable list instead of a silent branch. Declaring or re-pointing them is a package
# design decision, not a restyle.
KNOWN_UNDECLARED = frozenset({
    "--accent-pressed", "--amber-a10", "--bg-panel", "--bg-subtle", "--panel", "--shadow-raised",
    "--state-danger", "--state-positive", "--state-warning",
})


def check_fallbacks(root: Path) -> list[str]:
    """Every `var(--token, literal)` literal must be the value that token resolves to.

    The literal is what renders wherever the stylesheet is absent — a component pasted into a
    consuming app, a static export, a card rendered on its own. A literal left on the previous
    palette is invisible in the app and wrong everywhere else, which is exactly how the
    superseded colours survived three rounds of review. Only the RGB channels are compared:
    the alpha on a wash fallback is the author's opacity choice, not a palette value.

    A token the package never declares is a failure rather than a skip. The fallback is then
    not a fallback at all — it is what renders always, stylesheet or no stylesheet — and
    skipping it reported success over a live defect (`--accent-a10`, never declared, kept the
    case-queue selection wash on the superseded steel).
    """
    # A fallback renders in place of the LIGHT default, so colors.css and theme.css are
    # authoritative here; colors-dark.css must never win. The remaining sheets only widen the
    # set of names the package declares (elevation, typography, motion), which is what lets a
    # composite like `var(--focus-ring, 2px solid #…)` be checked at all.
    package_tokens = {**parse_tokens((root / "tokens" / "theme.css").read_text(encoding="utf-8")),
                      **parse_tokens((root / "tokens" / "colors.css").read_text(encoding="utf-8"))}
    for sheet in sorted((root / "tokens").glob("*.css")):
        if sheet.name in ("colors.css", "colors-dark.css", "theme.css"):
            continue
        for name, value in parse_tokens(sheet.read_text(encoding="utf-8")).items():
            package_tokens.setdefault(name, value)
    # The workstation is its own track and declares tokens the package never will (the chart
    # overlay channels, for one). Resolving its call sites against the package's table reported
    # every one of those as undeclared, which is how seven live chart channels reached a
    # "known gap" list instead of being fixed.
    workstation = root.parent / WORKSTATION_TRACK[0]
    workstation_tokens = dict(package_tokens)
    if workstation.exists():
        workstation_tokens.update(parse_tokens(workstation.read_text(encoding="utf-8")))
    superseded = superseded_values(root)
    failures: list[str] = []
    notes: list[str] = []
    checked = 0
    for track, base in call_sites(root):
        if not base.exists():
            continue
        paths = [base] if base.is_file() else sorted(base.rglob("*"))
        for path in paths:
            if not path.is_file():
                continue
            if path != base and path.suffix not in CALL_SITE_SUFFIXES:
                continue
            if is_test_source(path):
                continue
            text = blank_source_comments(path.read_text(encoding="utf-8", errors="replace"), path.suffix)
            text = re.sub(r"\.replace\(/[^\n]*?/[gimsuy]*,\s*[\"\'][^\"\']*[\"\']\)", "", text)
            local = set(DECL_NAME.findall(text)) | set(JS_DECL_NAME.findall(text))
            tokens = workstation_tokens if track == "workstation" else package_tokens
            for lineno, line in enumerate(text.splitlines(), 1):
                for found in VAR_CALL.finditer(line):
                    if not found.group(2):        # no comma: no fallback to check
                        continue
                    parsed = split_var_call(line, found.start())
                    if parsed is None:
                        continue
                    name, fallback = parsed
                    if not fallback:
                        continue
                    nested = VAR_CALL.search(fallback)
                    owned = literal_colours(without_nested_vars(fallback))
                    if nested:
                        # Each nested literal is checked against its own innermost role.
                        # An outer role may legitimately have a different default.
                        if owned:
                            failures.append(f"[fallback] {_rel(path, root)}:{lineno}: var({name}, …) "
                                            "mixes nested roles with owned literals; scoped review required")
                        continue
                    if not owned:
                        continue
                    if name not in tokens:
                        if name.startswith(LOCAL_PREFIXES) or name in local:
                            continue
                        checked += 1
                        stale = ["#" + "".join(f"{round(channel):02X}" for channel in colour["rgb"])
                                 for colour in owned if colour["literal"].startswith("#")]
                        stale = [colour for colour in stale if colour in superseded]
                        where = f"{_rel(path, root)}:{lineno}: var({name}, …)"
                        if name in KNOWN_UNDECLARED and not stale:
                            notes.append(f"[fallback-note] {where} — undeclared (known gap)")
                            continue
                        detail = (f"so {', '.join(stale)} is what renders, always"
                                  if stale else "so the literal is what renders, always")
                        failures.append(f"[fallback] {where} — the package declares no such "
                                        f"token, {detail}")
                        continue
                    wants = token_colours(tokens[name], tokens)
                    if not wants:
                        continue
                    # A wash token's literal carries the hue at the author's own alpha. Follow
                    # bare aliases first: --blue-a10 is declared as var(--accent-a10), and the
                    # color-mix only appears one level down.
                    mix = MIX_ALPHA.match(deref(name, tokens))
                    declared_literals = literal_colours(deref(name, tokens))
                    declared_alpha = None
                    if mix:
                        hue = resolve(mix.group(1), tokens)
                        wants = [hue] if hue is not None else []
                        declared_alpha = float(mix.group(2)) / 100
                    elif len(declared_literals) == 1 and declared_literals[0]["literal"] == deref(name, tokens):
                        declared_alpha = declared_literals[0]["alpha"]
                    if declared_alpha is not None and declared_alpha < 1 and any(colour["alpha"] >= 1 for colour in owned):
                        checked += 1
                        failures.append(f"[fallback] {_rel(path, root)}:{lineno}: var({name}, …) "
                                        "has an opaque fallback for a translucent role; retain the declared wash alpha")
                        continue
                    if len(owned) != len(wants):
                        checked += 1
                        failures.append(f"[fallback] {_rel(path, root)}:{lineno}: var({name}, …) "
                                        "has a different colour shape from its declared role")
                        continue
                    for index, colour in enumerate(owned):
                        got = colour["rgb"]
                        if got is None or index >= len(wants):
                            continue
                        want = wants[index]
                        checked += 1
                        if max(abs(want[i] - got[i]) for i in range(3)) > 1.0:
                            shown = colour["literal"]
                            failures.append(
                                f"[fallback] {_rel(path, root)}:{lineno}: var({name}, …{shown}…) — "
                                f"the token resolves to "
                                f"#{round(want[0]):02X}{round(want[1]):02X}{round(want[2]):02X}")
    for stale_entry in sorted(KNOWN_UNDECLARED & package_tokens.keys()):
        failures.append(f"[fallback] {stale_entry} is declared now — drop it from "
                        "KNOWN_UNDECLARED so the exemption cannot outlive the gap")
    for f in failures[:10]:
        print(f)
    if notes:
        print(f"[fallback-note] {len(notes)} call site(s) name one of the "
              f"{len(KNOWN_UNDECLARED)} known-undeclared tokens")
    if failures:
        print(f"[fallback] {len(failures)} of {checked} literals disagree with the token they guard")
    else:
        print(f"[fallback] all {checked} literals mirror the token they guard")
    return failures


# A component's CSS lives as a one-line rule inside its source, and the bundle is built from
# those strings verbatim. Comparing them catches the drift the fallback check cannot see: a
# rule that changes which *token* it reads (--accent to --accent-dim, say) leaves both sides'
# literals correctly mirroring their own tokens while the shipped component keeps the old one.
SOURCE_RULE = re.compile(r"^[.#&\[][^{]*\{[^{}]*var\(--[a-z0-9-]+[^{}]*\}$")


def check_bundle_parity(root: Path) -> list[str]:
    """Every bundled input's CSS must match; retained non-public files are not exports."""
    bundle_path = root / COMPILED_BUNDLE
    if not bundle_path.exists():
        return [f"[bundle] {COMPILED_BUNDLE} is missing; rebuild the package"]
    try:
        provenance = json.loads((root / "release-build.json").read_text(encoding="utf-8"))
        manifest = json.loads((root / "_ds_manifest.json").read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        return [f"[bundle] cannot read build input inventory: {error}"]
    build = provenance.get("build", {})
    bundled = build.get("bundledSources")
    retained = build.get("sourceHashes", {})
    if not isinstance(bundled, list) or not bundled or not all(isinstance(path, str) for path in bundled):
        return ["[bundle] bundled input inventory is missing; run npm run build"]
    if not isinstance(retained, dict):
        return ["[bundle] retained source-hash inventory is missing; run npm run build"]
    public = {component["sourcePath"] for component in manifest.get("components", [])
              if isinstance(component.get("sourcePath"), str)}
    failures = [f"[bundle] public source {path} is absent from the compiler input inventory"
                for path in sorted(public - set(bundled))]
    bundle = bundle_path.read_text(encoding="utf-8", errors="replace")
    checked = 0
    for relative in sorted(set(bundled)):
        path = root / relative
        if Path(relative).is_absolute() or ".." in Path(relative).parts or relative not in retained:
            failures.append(f"[bundle] invalid compiler input path: {relative}")
            continue
        if not path.is_file():
            failures.append(f"[bundle] compiler input {relative} is missing from the package")
            continue
        if path.suffix not in (".jsx", ".js", ".tsx", ".ts"):
            continue
        text = blank_source_comments(path.read_text(encoding="utf-8", errors="replace"), path.suffix)
        for lineno, line in enumerate(text.splitlines(), 1):
            rule = line.strip()
            if not SOURCE_RULE.match(rule):
                continue
            checked += 1
            if rule not in bundle:
                failures.append(f"[bundle] {_rel(path, root)}:{lineno}: this rule is not in "
                                f"{COMPILED_BUNDLE} — the shipped component differs from its "
                                f"source: {rule[:72]}")
    for f in failures[:10]:
        print(f)
    if not failures:
        print(f"[bundle] all {checked} token-bearing bundled-source rules are present verbatim in "
              f"{COMPILED_BUNDLE}")
    return failures


# A Tailwind arbitrary value — `bg-[#F3F6F9]` in a className string — is outside the token
# layer entirely: no `var()`, no stylesheet, so neither the restyle nor a `data-brand` can
# reach it and no fallback check can see it. Forty-six of them survived nine review rounds
# rendering the superseded steel surfaces in live primitives, because every sweep before this
# read `var(--token, literal)` fallbacks or bare literals in stylesheets and never a className.
TAILWIND_HEX = re.compile(r"\[(#[0-9A-Fa-f]{3,8})\]")
TAILWIND_SUFFIXES = (".tsx", ".ts", ".jsx", ".css")


def check_tailwind_literals(root: Path) -> list[str]:
    """No colour may be hard-coded as a Tailwind arbitrary value; it must come from a token."""
    base = root.parent / WORKSTATION_CALL_SITES[0]
    if not base.exists():
        return []
    failures: list[str] = []
    scanned = 0
    for path in sorted(base.rglob("*")):
        if not path.is_file() or path.suffix not in TAILWIND_SUFFIXES:
            continue
        scanned += 1
        for lineno, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            for found in TAILWIND_HEX.finditer(line):
                failures.append(f"[tailwind] {_rel(path, root)}:{lineno}: {found.group(0)} is a "
                                "hard-coded colour outside the token layer — use "
                                "[var(--token)] so the restyle and data-brand can reach it")
    for f in failures[:10]:
        print(f)
    if not failures:
        print(f"[tailwind] no hard-coded arbitrary colours in {scanned} workstation source files")
    return failures


def check_card_metadata(root: Path) -> list[str]:
    """The manifest's card records must match each card's own @dsCard declaration.

    A catalog UI reads the manifest, not the card, so a card can be corrected at source and
    still present its superseded description to everyone who browses it.
    """
    manifest = root / "_ds_manifest.json"
    if not manifest.exists():
        return []
    import json
    records = json.loads(manifest.read_text(encoding="utf-8")).get("cards", [])
    failures: list[str] = []
    checked = 0
    for record in records:
        card = root / record.get("path", "")
        if not card.is_file():
            continue
        found = DSCARD.search(card.read_text(encoding="utf-8", errors="replace")[:600])
        if not found:
            continue
        declared = dict(DSCARD_ATTR.findall(found.group(1)))
        for field in CARD_FIELDS:
            if field not in declared:
                continue
            checked += 1
            if record.get(field) != declared[field]:
                failures.append(f"[card-meta] {record['path']}: manifest {field}="
                                f"{record.get(field)!r} but the card declares {declared[field]!r}")
    for f in failures[:10]:
        print(f)
    if not failures:
        print(f"[card-meta] all {checked} card metadata fields match their @dsCard declaration")
    return failures


# The steel brand block IS the superseded identity, kept deliberately as `data-brand="steel"`.
# Any document quoting one of its values while describing the current system is describing the
# old one. Plus the superseded brand cyan/navy and the pre-AA-fix hover.
# Whole documents whose job is to record history rather than describe the current system.
HISTORICAL_DOCS = {"CHANGELOG.md", "INSPIRATION_BRIEF.md", "docs/UPGRADING.md"}
HISTORICAL_DIRS = ("docs/changelog/", "src/")
# A line that names the value as superseded is a legitimate historical reference.
HISTORICAL_LINE = re.compile(r"steel|superseded|previous|was\b|former|legacy|until this pass|→",
                             re.I)


def check_doc_palette(root: Path) -> list[str]:
    """Flag a superseded-identity colour quoted in documentation as if it were current."""
    superseded = superseded_values(root)
    failures: list[str] = []
    checked = 0
    # `.card.html` swatch tables document the palette in prose the same way a guide does — and
    # round ten found four rows of one still publishing the steel values as the implementation
    # figures. Scanning only `*.md` left every catalogue card out of the one check that exists
    # to catch exactly that.
    docs = sorted(root.rglob("*.md")) + sorted(root.rglob("*.card.html"))
    for path in docs:
        rel = path.relative_to(root).as_posix()
        if rel in HISTORICAL_DOCS or rel.startswith(HISTORICAL_DIRS):
            continue
        for lineno, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            hits = [h for h in DOC_HEX.findall(line) if h.upper() in superseded]
            if not hits:
                continue
            checked += len(hits)
            if HISTORICAL_LINE.search(line):
                continue
            failures.append(f"[doc-palette] {rel}:{lineno}: {', '.join(hits)} — a superseded-identity "
                            "value stated as current")
    for f in failures[:10]:
        print(f)
    if not failures:
        print(f"[doc-palette] all {checked} superseded-value mentions in docs are labelled as historical")
    return failures


# The brand marks are rendered identity, not artwork in a folder: WorkstationTopbar defaults to
# meridian-mark-light.svg, 41 files reference the set, and the dashboard and WPF both carry their
# own copy. Nothing had ever read an .svg, so the neon cyan sat in every masthead while the token
# and catalogue checks passed. The hero's own cool ramp is listed because it appears nowhere else.
BRAND_DIRS = [
    "Meridian Design System/assets/brand",
    "src/Meridian.Ui/dashboard/src/assets/brand",
    "src/Meridian.Wpf/Assets/Brand",
]
BRAND_RETIRED = frozenset({
    "#06F3FF", "#00D9FF", "#2AB2D4", "#DEE6EF", "#F5F7FA", "#D8DFE8", "#8A96A8", "#7C8A9B",
    "#08101A", "#1F344C",
    # the hero's blue/cyan/mint ramp
    "#07101C", "#0F1B28", "#111C28", "#152838", "#1B334B", "#21405D", "#29425C",
    "#1A6BB5", "#0EA5E9", "#16A34A", "#3B82F6", "#60A5FA", "#67E8F9", "#86EFAC", "#84F3B6",
})
# IBM Plex is loaded by tokens/fonts.css; metadata about outlined fonts does not render.
BRAND_FONTS = ("Space Grotesk", "JetBrains Mono", "Inter")
GENERIC_FONTS = {"serif", "sans-serif", "monospace", "system-ui", "ui-sans-serif", "ui-monospace"}


def svg_font_families(text: str) -> set[str]:
    """Read rendered font declarations; path provenance and comments do not render fonts."""
    tree = ET.fromstring(text)
    families = set()
    for element in tree.iter():
        declarations = [element.attrib.get("font-family", "")]
        style = element.attrib.get("style", "")
        if element.tag.rsplit("}", 1)[-1] == "style":
            style += element.text or ""
        declarations += re.findall(r"font-family\s*:\s*([^;}]+)", strip_comments(style))
        for declaration in declarations:
            families.update(value.strip(" \t\n\"'") for value in declaration.split(",") if value.strip())
    return families


def check_brand_assets(root: Path, scope: str = "all") -> list[str]:
    """No brand asset may carry a retired colour or font, and the copies must agree."""
    repo = root.parent
    failures: list[str] = []
    checked = 0
    canonical = repo / BRAND_DIRS[0]
    fonts = root / "tokens/fonts.css"
    loaded = {primary_font(value) for value in re.findall(r"font-family\s*:\s*([^;]+)", blank_comments(fonts.read_text(encoding="utf-8")))} if fonts.exists() else set()
    directories = [rel for rel in BRAND_DIRS if scope == "all" or "Meridian.Wpf" not in str(rel)]
    for rel in directories:
        base = repo / rel
        if not base.exists():
            continue
        for path in sorted(base.glob("*.svg")):
            checked += 1
            text = path.read_text(encoding="utf-8", errors="replace")
            hits = sorted({h.upper() for h in DOC_HEX.findall(text)} & BRAND_RETIRED)
            if hits:
                failures.append(f"[brand] {_rel(path, root)}: {', '.join(hits)} — a retired-identity "
                                "colour in an asset the workstation chrome renders")
            try:
                unavailable = sorted(svg_fonts(text) - loaded - GENERIC_FONTS)
                if unavailable:
                    failures.append(f"[brand] {_rel(path, root)}: primary face {', '.join(unavailable)} is not loaded by the package")
            except ET.ParseError as error:
                failures.append(f"[brand] {_rel(path, root)}: invalid SVG: {error}")
            source = canonical / path.name
            if base != canonical and source.exists() and \
                    source.read_text(encoding="utf-8").rstrip("\n") != text.rstrip("\n"):
                failures.append(f"[brand] {_rel(path, root)}: differs from the package copy — "
                                "the tracks would drift apart again")
    for f in failures[:10]:
        print(f)
    if not failures:
        print(f"[brand] all {checked} brand asset(s) across {len(directories)} selected track(s) carry the "
              "current identity and agree")
    return failures


# Round eight moved DenseDataTable's sort rank off the raw accent and stopped there. The same
# shape survived in five more components and two templates, because a table row pins the *token*
# rule and nothing was reading the call sites. An accent-tinted surface lifts the background
# toward the accent, so the accent on it is 3.87–4.41:1 — fine for a border or an icon, under AA
# for a label.
ACCENT_SURFACES = ("--blue-a10", "--accent-a10", "--bg-active", "--accent-ghost", "--brand-signal-ghost", "--brand-signal-a10")
CSS_RULE = re.compile(r"[^{}]*\{[^{}]*\}", re.S)
RULE_BG = re.compile(r"background(?:-color)?\s*:\s*([^;}]+)")
RULE_FG_ACCENT = re.compile(r"(?<![-a-z])color\s*:\s*var\(\s*--(?:accent(?:-dim|-hover)?|brand-signal(?:-dim|-hover)?)\s*[,)]")


def check_accent_on_wash(root: Path) -> list[str]:
    """No rule may use a signal fill variant as text on a tinted surface."""
    failures: list[str] = []
    scanned = 0
    for track, base in call_sites(root):
        if not base.exists():
            continue
        for path in ([base] if base.is_file() else sorted(base.rglob("*"))):
            if path != base and path.suffix not in CALL_SITE_SUFFIXES:
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            for m in CSS_RULE.finditer(text):
                body = m.group(0)
                bg = RULE_BG.search(body)
                if not bg or not any(s in bg.group(1) for s in ACCENT_SURFACES):
                    continue
                scanned += 1
                if RULE_FG_ACCENT.search(body):
                    selector = body.split("{", 1)[0].strip().splitlines()[-1].strip()
                    failures.append(
                        f"[accent-wash] {_rel(path, root)}:{text.count(chr(10), 0, m.start()) + 1}: "
                        f"{selector[:44]} uses a signal fill as text on a tinted surface "
                        "— normal text requires the AA --brand-signal-ink role")
    for f in failures[:10]:
        print(f)
    if not failures:
        print(f"[accent-wash] none of the {scanned} tinted rules uses signal fills as text")
    return failures


# The desktop lane has a second palette that no stylesheet check could reach: Charting,
# QuantScript and the ScottPlot surfaces render from ColorPalette.cs, not from ThemeTokens.xaml,
# so restyling the XAML left those surfaces on the previous identity. Each field there now names
# the XAML key it mirrors in its doc comment; this reads the claim back and verifies it.
RUNTIME_PALETTE = "src/Meridian.Ui.Services/Services/ColorPalette.cs"
WPF_TOKENS = "src/Meridian.Wpf/Styles/ThemeTokens.xaml"
XAML_COLOUR = re.compile(r'<Color x:Key="([A-Za-z0-9_]+)">#([0-9A-Fa-f]{6,8})</Color>')
# `/// <summary>… — SomeXamlKey #RRGGBB.</summary>` then `… Name = new(255, r, g, b);`
PALETTE_FIELD = re.compile(
    r"—\s*(?P<key>[A-Za-z0-9_]+)\s*#(?P<hex>[0-9A-Fa-f]{6})\b"
    r"(?:(?!</summary>).)*</summary>\s*\n"
    r"\s*public static readonly ArgbColor (?P<name>\w+) = "
    r"new\(255,\s*(?P<r>\d+),\s*(?P<g>\d+),\s*(?P<b>\d+)\);",
    re.S)


def check_runtime_palette(root: Path) -> list[str]:
    """Every annotated ColorPalette.cs colour must equal the XAML token it says it mirrors."""
    repo = root.parent
    palette_path, tokens_path = repo / RUNTIME_PALETTE, repo / WPF_TOKENS
    if not palette_path.exists() or not tokens_path.exists():
        return []
    xaml = {k: "#" + v.upper()[-6:]
            for k, v in XAML_COLOUR.findall(tokens_path.read_text(encoding="utf-8"))}
    failures: list[str] = []
    checked = 0
    for m in PALETTE_FIELD.finditer(palette_path.read_text(encoding="utf-8")):
        checked += 1
        name, key = m.group("name"), m.group("key")
        stated = "#" + m.group("hex").upper()
        literal = "#{:02X}{:02X}{:02X}".format(*(int(m.group(c)) for c in "rgb"))
        if key not in xaml:
            failures.append(f"[runtime-palette] {RUNTIME_PALETTE}: {name} says it mirrors {key}, "
                            f"which {WPF_TOKENS} does not declare")
        elif xaml[key] != literal:
            failures.append(f"[runtime-palette] {RUNTIME_PALETTE}: {name} is {literal}, but the "
                            f"{key} it mirrors is {xaml[key]} — the desktop charts would render "
                            "a different identity from the XAML around them")
        elif stated != literal:
            failures.append(f"[runtime-palette] {RUNTIME_PALETTE}: {name}'s comment says {stated} "
                            f"and the value is {literal}")
    for f in failures:
        print(f)
    if not failures:
        print(f"[runtime-palette] all {checked} annotated ColorPalette.cs colours match the "
              "ThemeTokens.xaml key they mirror")
    return failures


# The catalogue — colors_and_type.css, index.html, preview/ and ui_kits/ — is the package's
# primary visual entrypoint and was the last surface still rendering the pre-restyle identity.
# It survived nine rounds because every check here pointed at tokens/, guidelines/, the cards
# and the dashboard, and nothing read the catalogue's own 1,459 literals. These are the values
# it carried: the navy/cyan ladder, its accents and its semantics.
CATALOG_SUPERSEDED = frozenset({
    # surfaces
    # (#171A1F is deliberately absent: it is the steel brand's chrome in tokens/theme.css
    #  and components/shell/*, a live value, not a retired one.)
    "#050B12", "#0B0E16", "#05101B", "#08101A", "#081423", "#1A1407", "#08131F",
    "#0B1520", "#0D1722", "#101C2B", "#13253A", "#1A1F2E", "#142036", "#162334", "#18283C",
    "#1A2940", "#1B2A3C", "#1A2D44", "#1F2E42", "#2A3142", "#26364B", "#2D3E54", "#2E4766",
    # lines and text
    "#1F344C", "#4A5060", "#477089", "#5A6878", "#5B7B9E", "#7C8A9B", "#80A2C8", "#93B4DA",
    "#A8B5C4", "#BEC8D4", "#D4DCE6", "#DEE6EF",
    # cyan accent
    "#2AB2D4", "#2D9CDB", "#20D3F7", "#8CC5DE",
    # semantics
    "#26BF86", "#34D399", "#84F3B6", "#DE5878", "#E84545", "#F06B6B", "#D69E38", "#E6A93C",
    "#F2C94C", "#A78BFA", "#60A5FA",
})

CATALOG_ROOTS = ("colors_and_type.css", "index.html", "preview/", "ui_kits/")
CATALOG_SUFFIXES = {".css", ".html", ".jsx", ".js"}
CATALOG_RGB = re.compile(r"rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*[,)]")


def check_catalog_palette(root: Path) -> list[str]:
    """No superseded-identity colour may render from the catalogue.

    Scans literals, not tokens: the swatch sheets paint inline, so a token sweep never saw
    them. `src/Meridian.Ui/...` under the package is out of scope on purpose — it is the
    frozen pre-restyle snapshot governance-baseline.json owns, not a surface that renders.
    """
    failures: list[str] = []
    scanned = files = 0
    for path in sorted(root.rglob("*")):
        if any(part in {"node_modules", ".git", "__pycache__"} for part in path.parts):
            continue
        if not path.is_file() or path.suffix.lower() not in CATALOG_SUFFIXES:
            continue
        rel = path.relative_to(root).as_posix()
        if not rel.startswith(CATALOG_ROOTS):
            continue
        files += 1
        for lineno, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            hits = {h.upper() for h in DOC_HEX.findall(line) if h.upper() in CATALOG_SUPERSEDED}
            for m in CATALOG_RGB.finditer(line):
                value = "#{:02X}{:02X}{:02X}".format(*(int(g) for g in m.groups()))
                if value in CATALOG_SUPERSEDED:
                    hits.add(value)
            scanned += len(DOC_HEX.findall(line))
            if hits:
                failures.append(f"[catalog-palette] {_rel(path, root)}:{lineno}: "
                                f"{', '.join(sorted(hits))} — the catalogue still renders a "
                                "superseded-identity colour")
    for f in failures[:10]:
        print(f)
    if failures and len(failures) > 10:
        print(f"[catalog-palette] ... and {len(failures) - 10} more")
    if not failures:
        print(f"[catalog-palette] {files} catalogue file(s), {scanned} literal(s), none on the "
              "superseded identity")
    return failures


# The guides state the radius and shadow contract in prose. Round four synced the two copies of
# VISUAL_FOUNDATIONS.md onto the stale wording, so both described 4/6/8px radii and card shadows
# while tokens/elevation.css declares a unified 2px corner and --shadow-card: none.
GUIDES = ["VISUAL_FOUNDATIONS.md", "guidelines/VISUAL_FOUNDATIONS.md"]
RADII_LINE = re.compile(r"^- Radii:(.*)$", re.M)
SHADOW_LINE = re.compile(r"^- Shadows:(.*(?:\n  .*)*)$", re.M)


def check_elevation_guidance(root: Path) -> list[str]:
    """The guides' stated radii and card shadow must match tokens/elevation.css."""
    elevation = root / "tokens" / "elevation.css"
    if not elevation.exists():
        return []
    tokens = parse_tokens(elevation.read_text(encoding="utf-8"))
    card_radius = tokens.get("--radius-card", "").strip()
    card_shadow = tokens.get("--shadow-card", "").strip()
    failures: list[str] = []
    for rel in GUIDES:
        path = root / rel
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8")
        radii = RADII_LINE.search(text)
        if radii and card_radius and card_radius not in radii.group(1):
            failures.append(f"[elevation] {rel}: the radii line does not state the token contract "
                            f"(--radius-card is {card_radius}): {radii.group(1).strip()[:60]!r}")
        shadows = SHADOW_LINE.search(text)
        if shadows and card_shadow == "none" and "none" not in shadows.group(1):
            failures.append(f"[elevation] {rel}: the shadows line describes a card shadow, but "
                            "--shadow-card is none")
    for f in failures:
        print(f)
    if not failures:
        print(f"[elevation] both guide copies state the token contract "
              f"(--radius-card {card_radius}, --shadow-card {card_shadow})")
    return failures


def run_checks(root: Path, scope: str = "all") -> list[str]:
    modes = package_modes(root)
    failures = [failure for mode, tokens in modes.items() for failure in check_mode(mode, tokens)]
    return (failures
            + [failure for mode, tokens in modes.items() for failure in check_semantic_chips(mode, tokens)]
            + [failure for mode, tokens in modes.items() for failure in check_signal_ink(mode, tokens)]
            + check_brands(root) + check_package_primary(root) + check_workstation(root)
            + check_ws_aliases(root) + check_comment_terminators(root)
            + check_var_chains(root) + check_runtime_fallbacks(root) + check_fallbacks(root)
            + check_card_metadata(root) + check_doc_palette(root)
            + check_elevation_guidance(root) + check_bundle_parity(root)
            + check_tailwind_literals(root) + check_catalog_palette(root)
            + (check_runtime_palette(root) if scope == "all" else [])
            + check_accent_on_wash(root)
            + check_brand_assets(root, scope))


def action_tokens(tokens: dict[str, str]) -> dict[str, str]:
    """The exact source Button expressions, with no extra published token names."""
    return {**tokens,
            "--check-primary-hover": "color-mix(in srgb, var(--brand-axis) 88%, var(--bg-light))",
            "--check-primary-pressed": "color-mix(in srgb, var(--brand-axis) 78%, var(--bg-light))"}


def package_modes(root: Path, brand: dict[str, str] | None = None, brand_name: str | None = None) -> dict[str, dict[str, str]]:
    """Apply actual root selector/source-order cascades across the published sheets."""
    css = "\n".join((root / "tokens" / name).read_text(encoding="utf-8") for name in ("colors.css", "theme.css", "colors-dark.css"))
    contexts = mode_tokens(css, brand_name)
    return {"light": contexts["light"], "forced-light": contexts["forced-light"],
            "auto-dark": contexts["auto-dark"], "dark": contexts["manual-dark"]}


def scoped_tokens(text: str, selector: str) -> dict[str, str]:
    """Read declarations from exact leaf selectors, using CSS's last declaration wins."""
    out = {}
    for match in re.finditer(r"([^{}]+)\{([^{}]*)\}", blank_comments(text)):
        if selector not in [part.strip() for part in match.group(1).split(",")]:
            continue
        out.update(dict(TOKEN_PATTERN.findall(match.group(2))))
    return {name: value.strip() for name, value in out.items()}


def root_selector(selector: str, attributes: dict[str, str]):
    """Match the simple compound root selectors authored in token sheets.

    Attributes and :not(attribute) contribute their argument specificity; :not has
    no extra specificity of its own. Unsupported/descendant selectors never match
    the root. This keeps brand and forced-light decisions separate from source order.
    """
    prefix = re.match(r"^(html|:root)", selector)
    if prefix is None:
        return None
    tail = selector[prefix.end():]
    score = 0 if prefix.group(1) == "html" else 1
    attribute = re.compile(r"(?P<not>:not\()?\[(?P<name>[a-zA-Z0-9_-]+)(?:\s*=\s*(?:\"(?P<double>[^\"]*)\"|'(?P<single>[^']*)'|(?P<plain>[^\]\s]+)))?\](?(not)\))")
    cursor = 0
    while cursor < len(tail):
        match = attribute.match(tail, cursor)
        if match is None:
            return None
        name = match.group("name")
        value = next((match.group(part) for part in ("double", "single", "plain") if match.group(part) is not None), None)
        matches = name in attributes and (value is None or attributes[name] == value)
        if bool(match.group("not")) == matches:
            return None
        score += 1
        cursor = match.end()
    return (0, score, 1 if prefix.group(1) == "html" else 0)


def production_paths(base: Path):
    """Production sources and shipped artifacts; tests are evidence, not rendered CSS."""
    for path in ([base] if base.is_file() else sorted(base.rglob("*"))):
        if not path.is_file() or (path != base and path.suffix not in CALL_SITE_SUFFIXES):
            continue
        if re.search(r"\.(?:test|spec)\.[^.]+$", path.name) or path.name.endswith(".d.ts"):
            continue
        yield path


STYLE_VALUE = re.compile(r"(?:[\w-]+[\"']?\s*:\s*[^;{}]*|\[[^\]]*)var\(")


def style_lines(path: Path, text: str):
    """A var() is CSS only in a declaration/value or an arbitrary-style class.

    Scanning all JavaScript strings incorrectly treated GovernanceLints' replacement
    string 'var(--x)' and test assertions as styles shipped to the browser.
    """
    text = blank_comments(text)
    for lineno, line in enumerate(text.splitlines(), 1):
        if path.suffix == ".css" or STYLE_VALUE.search(line):
            yield lineno, line


def primary_font(stack: str) -> str:
    return stack.split(",", 1)[0].strip().strip("\"'")


def svg_fonts(text: str) -> set[str]:
    tree = ET.fromstring(text)
    stacks = []
    for node in tree.iter():
        if node.get("font-family"):
            stacks.append(node.get("font-family"))
        stacks.extend(re.findall(r"font-family\s*:\s*([^;}]+)", node.get("style", "")))
        if node.tag.rsplit("}", 1)[-1] == "style":
            stacks.extend(re.findall(r"font-family\s*:\s*([^;}]+)", node.text or ""))
    return {primary_font(stack) for stack in stacks}


def check_runtime_fallbacks(root: Path) -> list[str]:
    """Validate fallback reachability, not equality with a value that prevents it rendering.

    CSS var(--defined, fallback) uses the defined custom property. Its fallback need not
    equal that value (and cannot equal both light and dark values). Missing dependencies,
    cycles and undeclared names are still failures: those make the fallback load-bearing.
    Token contrast checks measure the values actually rendered; bundle parity still proves
    that the shipped rules match their sources. A fallback is not an allowance for a missing
    public token, even if its literal looks acceptable in one theme.
    """
    token_sets = {}
    for track, paths in tracks(root).items():
        tokens = {}
        sheets = sorted((root / "tokens").glob("*.css")) if track == "package" else paths
        for sheet in sheets:
            if sheet.exists():
                for name, value in parse_tokens(sheet.read_text(encoding="utf-8")).items():
                    tokens.setdefault(name, value)
        token_sets[track] = tokens
    failures, checked, guarded = [], 0, 0
    superseded = superseded_values(root)
    for track, base in call_sites(root):
        if not base.exists():
            continue
        tokens = token_sets[track]
        for path in production_paths(base):
            text = path.read_text(encoding="utf-8", errors="replace")
            local = set(DECL_NAME.findall(text)) | set(JS_DECL_NAME.findall(text))
            for lineno, line in style_lines(path, text):
                for found in VAR_CALL.finditer(line):
                    if not found.group(2):
                        continue
                    parsed = split_var_call(line, found.start())
                    if parsed is None:
                        continue
                    name, fallback = parsed
                    if not fallback or not (HEX_LITERAL.search(fallback) or RGBA_LITERAL.search(fallback)):
                        continue
                    checked += 1
                    if name in tokens or name in local or name.startswith(LOCAL_PREFIXES):
                        guarded += 1
                        continue
                    # Existing known-gap debt remains visible as a note, never grows here.
                    if name in KNOWN_UNDECLARED:
                        stale = sorted({value.upper() for value in HEX_LITERAL.findall(fallback) if len(value) == 7} & superseded)
                        if stale:
                            failures.append(f"[fallback] {_rel(path, root)}:{lineno}: existing undeclared {name} renders retired palette {', '.join(stale)}")
                        else:
                            print(f"[fallback-note] {_rel(path, root)}:{lineno}: existing undeclared {name}")
                        continue
                    failures.append(f"[fallback] {_rel(path, root)}:{lineno}: var({name}, …) — this track declares no such token; the fallback is always active")
    # This debt list belongs to package consumers, not similarly named workstation aliases.
    for name in sorted(KNOWN_UNDECLARED & set(token_sets["package"])):
        failures.append(f"[fallback] {name} is declared now — drop it from KNOWN_UNDECLARED so the exemption cannot outlive the gap")
    for failure in failures[:10]:
        print(failure)
    print(f"[fallback] {checked} production fallback expressions, {guarded} guarded by declared/local tokens; {len(failures)} undeclared dependencies")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--scope", choices=("all", "browser"), default="all",
                        help="all (default) preserves native WPF palette/asset parity guards; browser explicitly excludes native assets/palette without certifying them")
    parser.add_argument("--actions-only", action="store_true",
                        help="Check actual package/workstation structural primary actions in every color-scheme mode; other guards are not selected")
    args = parser.parse_args()
    root = args.root.resolve()
    if args.scope == "browser":
        print("[scope] browser only: native WPF palette and asset parity checks were not selected; they are not passed by this command")
    failures = (check_package_primary(root) + check_workstation(root)
                if args.actions_only else run_checks(root, args.scope))
    if failures:
        print("\nContrast failures:", file=sys.stderr)
        for f in failures:
            print("  " + f, file=sys.stderr)
        return 1
    print("\nAll primary action contrast pairs pass." if args.actions_only else "\nAll token contrast pairs pass.")
    return 0



if __name__ == "__main__":
    sys.exit(main())
