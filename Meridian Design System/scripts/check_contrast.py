#!/usr/bin/env python3
"""WCAG contrast checker for the Meridian token system.

Computes contrast from the current token roles and component source across light/dark
appearances, including recursive var() resolution, HSL channels and two-color srgb mixes.
Transparent chip washes are composited over their actual material surfaces. Fails with
exit 1 when a checked text/graphic pair drops below its AA threshold or retained source
conformance checks fail.

This closes the "known follow-up" in guidelines/ACCESSIBILITY.md: the contrast table there was
hand-measured; this script re-derives it on every run.

Usage:  python3 scripts/check_contrast.py [--root PATH]
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
    ("signal ink on card",         "--brand-signal-ink", "--bg-light",    4.5),
    ("signal ink on canvas",       "--brand-signal-ink", "--bg",          4.5),
    # The incoming integration also publishes this text role; measure both names.
    ("signal text on card",        "--brand-signal-text", "--bg-light",   4.5),
    ("signal text on header band", "--brand-signal-text", "--bg-medium",  4.5),
    # Graphic signal marks retain their 3:1 threshold; normal text uses the
    # dedicated ink role, including the retained table's sort/rank labels.
    ("accent on card (non-text)",  "--accent",         "--bg-light",     3.0),
    ("accent on header band (non-text)", "--accent",   "--bg-medium",    3.0),
    ("signal ink on header band",  "--brand-signal-ink", "--bg-medium", 4.5),
    ("signal ink on selected row", "--brand-signal-ink", "--bg-active", 4.5),
    ("signal ink on ghost fill",   "--brand-signal-ink", "--accent-ghost", 4.5),
    ("signal fill label",          "--text-on-accent", "--accent",       4.5),
    # The hover state was omitted here until 2026-09, so a hover fill that failed AA
    # shipped behind a green gate. All three button states are checked now.
    ("hover signal label",         "--text-on-accent", "--accent-hover", 4.5),
    ("pressed signal label",       "--text-on-accent", "--accent-dim",   4.5),
    ("focus ring on card",         "--border-focus",   "--bg-light",     3.0),
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


def resolve(value: str, tokens: dict[str, str], depth: int = 0) -> tuple[float, float, float] | None:
    if depth > 8:
        return None
    value = value.strip()
    rgb = hex_to_rgb(value)
    if rgb:
        return rgb
    var = CSS_VAR_PATTERN.match(value)
    if var:
        target = tokens.get(var.group(1), var.group(2))
        return resolve(target, tokens, depth + 1) if target else None
    hsl = HSL_PATTERN.match(value)
    if hsl:
        channels = hsl.group(1).strip()
        source = CSS_VAR_PATTERN.match(channels)
        if source:
            channels = tokens.get(source.group(1), source.group(2) or "")
        components = re.fullmatch(r"([\d.]+)\s+([\d.]+)%\s+([\d.]+)%", channels)
        if components:
            hue, saturation, lightness = (float(component) for component in components.groups())
            if 0 <= saturation <= 100 and 0 <= lightness <= 100:
                return tuple(channel * 255 for channel in colorsys.hls_to_rgb(
                    hue / 360, lightness / 100, saturation / 100))
        return None
    mix = MIX_PATTERN.match(value)
    if mix:
        a = resolve(mix.group(1), tokens, depth + 1)
        pct = float(mix.group(2)) / 100.0
        b_raw = mix.group(3).strip()
        b = (0.0, 0.0, 0.0) if b_raw in ("transparent",) else resolve(b_raw, tokens, depth + 1)
        if a is None or b is None:
            return None
        return tuple(a[i] * pct + b[i] * (1 - pct) for i in range(3))  # type: ignore[return-value]
    if value in ("#000", "black"):
        return (0.0, 0.0, 0.0)
    if value in ("#FFF", "#FFFFFF", "white"):
        return (255.0, 255.0, 255.0)
    return None


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
        "auto dark": r"@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root\s*\{([^{}]+)\}",
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
    """Measure effective brand roles in every supported appearance, including inheritance.

    Brands customize the signal, not the independent blue focus family or structural
    primary action. An omitted dark identity uses the declared default dark palette;
    it still has to pass the same text thresholds rather than disappearing from QA.
    """
    text = (root / "tokens/theme.css").read_text(encoding="utf-8")
    base = {**parse_tokens((root / "tokens/colors.css").read_text(encoding="utf-8")),
            **parse_tokens(BRAND_BLOCK.sub("", text))}
    dark_css = (root / "tokens/colors-dark.css").read_text(encoding="utf-8")
    merged: dict[str, dict[str, str]] = {}
    for brand, body in BRAND_BLOCK.findall(text):
        merged.setdefault(brand, {}).update(parse_tokens(body))
    failures = []
    for brand, declared in merged.items():
        for mode, tokens in appearance_scopes({**base, **declared}, dark_css).items():
            if mode == "auto dark":
                # html[data-brand] is more specific than the media block's :root.
                # Keep direct brand outputs here so a white-label ink override cannot
                # be incorrectly hidden by a lower-specificity dark declaration.
                tokens = {**tokens, **declared}
            scope = f"{mode} · brand {brand}"
            label = resolve("var(--text-on-accent)", tokens)
            for state, suffix in BRAND_PAIRS:
                fill = resolve(f"var(--{suffix})", tokens)
                if label is None or fill is None:
                    failures.append(f"[{scope}] {state} signal label: cannot resolve actual roles")
                elif ratio(label, fill) < 4.5:
                    failures.append(f"[{scope}] {state} signal label: {ratio(label, fill):.2f}:1 < 4.5:1")
            for name, foreground, background, minimum in (
                ("focus ring on card", "--border-focus", "--bg-light", 3.0),
                ("body text on signal ghost", "--text-primary", "--accent-ghost", 4.5),
            ):
                fg, bg = resolve(f"var({foreground})", tokens), resolve(f"var({background})", tokens)
                if fg is None or bg is None:
                    failures.append(f"[{scope}] {name}: cannot resolve actual roles")
                elif ratio(fg, bg) < minimum:
                    failures.append(f"[{scope}] {name}: {ratio(fg, bg):.2f}:1 < {minimum}:1")
            failures += check_primary_buttons(root, scope, tokens)
            failures += check_semantic_chips(scope, tokens)
            failures += check_signal_ink(scope, tokens)
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


def check_workstation(root: Path) -> list[str]:
    """Check the native adapter's real primary label and state fills in each appearance."""
    index = root.parent / WS_INDEX
    if not index.exists():
        return []  # package used standalone, without the monorepo around it
    text = index.read_text(encoding="utf-8")
    button = root.parent / WS_BUTTON
    if not button.exists():
        return ["[workstation] native primary-button source is missing"]
    declaration = re.search(r'default:\s*"([^"]+)"', button.read_text(encoding="utf-8"))
    classes = declaration.group(1).split() if declaration else []
    label = button_colour_utility(classes, "text")
    base = button_colour_utility(classes, "bg")
    if label is None or base is None:
        return ["[workstation] cannot identify the native primary fill and label roles"]
    states = {"base": (base, label)}
    for state, prefix in (("hover", "hover"), ("pressed", "active")):
        fill = button_colour_utility(classes, "bg", prefix)
        state_label = (button_colour_utility(classes, "text", prefix)
                       if any(value.startswith(f"{prefix}:text-") for value in classes) else label)
        if fill is None or state_label is None:
            return [f"[workstation] cannot identify the native primary {state} role"]
        states[state] = (fill, state_label)

    light = parse_tokens(text)
    blocks = {"light": light}
    scopes = {
        "auto dark": r"@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root\s*\{([^{}]+)\}",
        "manual dark": r':root\[data-theme="dark"\]\s*\{([^{}]+)\}',
        "forced light": r':root\[data-theme="light"\]\s*\{([^{}]+)\}',
    }
    for name, pattern in scopes.items():
        match = re.search(pattern, strip_comments(text), re.S)
        if match:
            blocks[name] = {**light, **parse_tokens(match.group(1))}

    failures: list[str] = []
    for mode, tokens in blocks.items():
        for state, (fill_expression, label_expression) in states.items():
            fill = resolve(fill_expression, tokens)
            label = resolve(label_expression, tokens)
            if label is None or fill is None:
                failures.append(f"[{mode} · workstation] {state} button: cannot resolve actual roles")
                continue
            value = ratio(label, fill)
            status = "ok" if value >= 4.5 else "FAIL"
            print(f"[{mode} \u00b7 workstation] {state} button label: {value:.2f}:1 (min 4.5) {status}")
            if status == "FAIL":
                failures.append(f"[{mode} \u00b7 workstation] {state} button label: {value:.2f}:1 < 4.5")
    return failures


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
        for path in ([base] if base.is_file() else sorted(base.rglob("*"))):
            if path != base and path.suffix not in CALL_SITE_SUFFIXES:
                continue
            if is_test_source(path):
                continue
            text = blank_source_comments(path.read_text(encoding="utf-8", errors="replace"), path.suffix)
            declared = workstation_tokens if track == "workstation" else package_tokens
            local = set(DECL_NAME.findall(text)) | set(JS_DECL_NAME.findall(text))
            for lineno, line in enumerate(text.splitlines(), 1):
                for ref in BARE_REF.findall(line):
                    checked += 1
                    if ref in declared or ref in local or ref in KNOWN_UNDECLARED:
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
    for stale_entry in sorted(KNOWN_UNDECLARED & tokens.keys()):
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


def check_brand_assets(root: Path) -> list[str]:
    """No brand asset may carry a retired colour or font, and the copies must agree."""
    repo = root.parent
    failures: list[str] = []
    checked = 0
    canonical = repo / BRAND_DIRS[0]
    for rel in BRAND_DIRS:
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
                used = svg_font_families(text)
            except ET.ParseError as error:
                failures.append(f"[brand] {_rel(path, root)}: invalid SVG: {error}")
                continue
            fonts = [family for family in BRAND_FONTS if family in used]
            if fonts:
                failures.append(f"[brand] {_rel(path, root)}: names {', '.join(fonts)}, a face the "
                                "package no longer loads")
            source = canonical / path.name
            if base != canonical and source.exists() and \
                    source.read_text(encoding="utf-8").rstrip("\n") != text.rstrip("\n"):
                failures.append(f"[brand] {_rel(path, root)}: differs from the package copy — "
                                "the tracks would drift apart again")
    for f in failures[:10]:
        print(f)
    if not failures:
        print(f"[brand] all {checked} brand asset(s) across {len(BRAND_DIRS)} track(s) carry the "
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


def run_checks(root: Path) -> list[str]:
    light = parse_tokens((root / "tokens" / "colors.css").read_text(encoding="utf-8"))
    dark_overrides = parse_tokens((root / "tokens" / "colors-dark.css").read_text(encoding="utf-8"))
    dark = {**light, **dark_overrides}
    return (check_mode("light", light) + check_mode("dark", dark)
            + check_semantic_chips("light", light) + check_semantic_chips("dark", dark)
            + check_signal_ink("light", light) + check_signal_ink("dark", dark)
            + check_primary_buttons(root, "light", light) + check_primary_buttons(root, "dark", dark)
            + check_brands(root) + check_workstation(root)
            + check_ws_aliases(root) + check_comment_terminators(root)
            + check_var_chains(root) + check_fallbacks(root)
            + check_card_metadata(root) + check_doc_palette(root)
            + check_elevation_guidance(root) + check_bundle_parity(root)
            + check_tailwind_literals(root) + check_catalog_palette(root)
            + check_runtime_palette(root)
            + check_accent_on_wash(root)
            + check_brand_assets(root))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args()
    failures = run_checks(args.root)
    if failures:
        print("\nContrast failures:", file=sys.stderr)
        for f in failures:
            print("  " + f, file=sys.stderr)
        return 1
    print("\nAll token contrast pairs pass.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
