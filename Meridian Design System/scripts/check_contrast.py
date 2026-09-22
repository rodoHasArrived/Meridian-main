#!/usr/bin/env python3
"""WCAG contrast checker for the Meridian token system.

Computes contrast ratios directly from tokens/colors.css (light) and tokens/colors-dark.css
(dark), including one level of var() indirection and simple two-color `color-mix(in srgb, X N%, Y)`
resolution (how the --*-dim tokens are built). Fails with exit 1 when any checked pair drops
below its threshold — so token edits that break AA are caught before they ship.

This closes the "known follow-up" in guidelines/ACCESSIBILITY.md: the contrast table there was
hand-measured; this script re-derives it on every run.

Usage:  python3 scripts/check_contrast.py [--root PATH]
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

TOKEN_PATTERN = re.compile(r"(--[a-z0-9-]+)\s*:\s*([^;]+);")
HEX_PATTERN = re.compile(r"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")
VAR_PATTERN = re.compile(r"^var\(\s*(--[a-z0-9-]+)\s*\)$")
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
    ("accent text on card",        "--accent",         "--bg-light",     4.5),
    ("accent-dim text on card",    "--accent-dim",     "--bg-light",     4.5),
    # The header band is a third surface, and the accent is only 4.39:1 on it — which is why
    # DenseDataTable's sort arrow and rank, which sit there, now take the dim variant. The
    # accent keeps a non-text row on the band (borders, icons, the 3:1 threshold); text on the
    # band is --accent-dim. Checking the accent only against the card and the canvas missed a
    # pair that ships on every sorted table.
    ("accent on header band (non-text)", "--accent",   "--bg-medium",    3.0),
    ("accent-dim text on header band",   "--accent-dim", "--bg-medium",  4.5),
    ("primary button label",       "--text-on-accent", "--accent",       4.5),
    # The hover state was omitted here until 2026-09, so a hover fill that failed AA
    # shipped behind a green gate. All three button states are checked now.
    ("hover button label",         "--text-on-accent", "--accent-hover", 4.5),
    ("pressed button label",       "--text-on-accent", "--accent-dim",   4.5),
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
    # An accent wash lifts the surface toward the accent, so the accent itself loses contrast on
    # it — 4.41:1 on a card, 4.07 on canvas, 3.87 on the header band, where the steel pair
    # cleared AA. Same shape as the header-band rows above: the accent is a borders-and-icons
    # colour on its own wash, and wash *text* is --accent-dim.
    ("accent on accent wash (non-text)", "--accent",     "--accent", 0.10, "--bg-light",  3.0),
    ("accent-dim text on accent wash",   "--accent-dim", "--accent", 0.10, "--bg-light",  4.5),
    ("accent-dim on accent wash, canvas","--accent-dim", "--accent", 0.10, "--bg",        4.5),
    ("accent-dim on accent wash, band",  "--accent-dim", "--accent", 0.10, "--bg-medium", 4.5),
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
    ("state-paper chip",       "--state-paper-fg",      "--accent", 0.10, "--bg-light",  4.5),
    ("state-strategy chip",    "--state-strategy-fg",   "--purple", 0.10, "--bg-light",  4.5),
    ("state-live chip",        "--state-live-fg",       "--red",    0.12, "--bg-light",  4.5),
    ("state-pending chip",     "--state-pending-fg",    "--purple", 0.10, "--bg-light",  4.5),
]


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
    var = VAR_PATTERN.match(value)
    if var:
        target = tokens.get(var.group(1))
        return resolve(target, tokens, depth + 1) if target else None
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

# Button states a brand can restate. Every white-label variant renders these with the
# same white label, so each has to clear AA on its own — checking only the default
# palette let a brand ship an inaccessible primary action.
BRAND_PAIRS = [("base", "accent"), ("hover", "accent-hover"), ("pressed", "accent-dim")]

# Surfaces a brand's accent also drives. Checking only the button states let six brands ship
# with the default copper focus ring and the default warm-brown ghost fill, because those two
# tokens were never restated per brand and nothing here looked at them.
# (name, foreground suffix, background suffix, minimum) — 3.0 is the non-text threshold.
BRAND_SURFACES = [
    ("focus ring on card", "border-focus", "bg-light", 3.0),
    ("body text on ghost", "primary-text", "accent-ghost", 4.5),
]

# A brand may restate its light identity, its dark identity, or both, in separate blocks.
# Merge a brand's blocks before checking so each variant is reported once per mode, and so
# a brand that states only a dark identity is not silently measured against the defaults.
BRAND_MODES = [("light", "--theme-"), ("dark", "--theme-dark-")]


def check_brands(root: Path) -> list[str]:
    """Check each data-brand variant's button states against its own label colour."""
    text = (root / "tokens" / "theme.css").read_text(encoding="utf-8")
    # Brand blocks sit above the default dark block in this file, so parsing the whole text
    # would let the first brand's --theme-dark-* values stand in as "the defaults" for every
    # brand that omits one. Read the defaults from the text with the brand blocks removed.
    defaults = parse_tokens(BRAND_BLOCK.sub("", text))
    merged: dict[str, dict[str, str]] = {}
    for brand, body in BRAND_BLOCK.findall(text):
        merged.setdefault(brand, {}).update(parse_tokens(body))

    failures: list[str] = []
    for brand, declared in merged.items():
        stated = {
            mode: any(f"{prefix}{name}" in declared for _, name in BRAND_PAIRS)
            for mode, prefix in BRAND_MODES
        }
        # A brand that states a light identity but no dark one does not fall back to a
        # dark version of itself: it inherits the default accent, so the whole white-label
        # promise silently reverts to copper the moment the OS asks for dark. Six brands
        # shipped that way. Skipping the unstated mode would also hide it from every check
        # below, so name it here rather than measure the defaults twice.
        if stated["light"] and not stated["dark"]:
            print(f"[dark · brand {brand}] no dark identity: inherits the default accent FAIL")
            failures.append(
                f"[dark · brand {brand}] declares a light accent but no --theme-dark-accent*; "
                "it would render the default accent in dark mode"
            )
        for mode, prefix in BRAND_MODES:
            # Only report a mode the brand actually restates; otherwise it inherits the
            # default palette, which the light/dark passes above already cover.
            if not stated[mode]:
                continue
            tokens = {**defaults, **declared}
            label = tokens.get(f"{prefix}text-on-accent", "#FFFFFF")
            for state, name in BRAND_PAIRS:
                token = f"{prefix}{name}"
                if token not in tokens:
                    continue
                fg, bg = resolve(label, tokens), resolve(f"var({token})", tokens)
                if fg is None or bg is None:
                    continue
                value = ratio(fg, bg)
                status = "ok" if value >= 4.5 else "FAIL"
                print(f"[{mode} · brand {brand}] {state} button label: {value:.2f}:1 (min 4.5) {status}")
                if status == "FAIL":
                    failures.append(f"[{mode} · brand {brand}] {state} button label: {value:.2f}:1 < 4.5")
            for name, fg_name, bg_name, minimum in BRAND_SURFACES:
                fg = resolve(f"var({prefix}{fg_name})", tokens)
                bg = resolve(f"var({prefix}{bg_name})", tokens)
                if fg is None or bg is None:
                    continue
                value = ratio(fg, bg)
                status = "ok" if value >= minimum else "FAIL"
                print(f"[{mode} · brand {brand}] {name}: {value:.2f}:1 (min {minimum}) {status}")
                if status == "FAIL":
                    failures.append(f"[{mode} · brand {brand}] {name}: {value:.2f}:1 < {minimum}")
    return failures


# The browser workstation keeps a parallel --ws-* track in its own index.css, and its
# DesignSystemButton reads --ws-accent-hover rather than the canonical --accent-hover. That
# track shipped a 3.86:1 hover behind a green gate because nothing here looked at it.
WS_STATES = [("base", "--ws-accent"), ("hover", "--ws-accent-hover"), ("pressed", "--ws-accent-pressed")]
WS_INDEX = Path("src/Meridian.Ui/dashboard/src/styles/index.css")
# Tailwind --primary-foreground, as HSL triples: white in light, near-black ink in dark.
WS_LABEL = {"light": (255.0, 255.0, 255.0), "dark": (26.0, 21.0, 18.0)}


def check_workstation(root: Path) -> list[str]:
    """Check the browser workstation's --ws-* button states, which are not in this package."""
    index = root.parent / WS_INDEX
    if not index.exists():
        return []  # package used standalone, without the monorepo around it
    text = index.read_text(encoding="utf-8")
    # First declaration of each token is the light block; the dark blocks follow.
    blocks = {"light": {}, "dark": {}}
    for m in re.finditer(r"(--ws-accent(?:-hover|-pressed)?)\s*:\s*(#[0-9A-Fa-f]{6})\s*;", text):
        tok, val = m.group(1), m.group(2)
        blocks["light"].setdefault(tok, val)
    for m in re.finditer(r"prefers-color-scheme:\s*dark(.*)", text, re.S):
        for d in re.finditer(r"(--ws-accent(?:-hover|-pressed)?)\s*:\s*(#[0-9A-Fa-f]{6})\s*;", m.group(1)):
            blocks["dark"].setdefault(d.group(1), d.group(2))
        break

    failures: list[str] = []
    for mode, tokens in blocks.items():
        label = WS_LABEL[mode]
        for state, tok in WS_STATES:
            if tok not in tokens:
                continue
            value = ratio(label, hex_to_rgb(tokens[tok]))
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


def call_sites(root: Path) -> list[Path]:
    repo = root.parent
    return ([root / rel for rel in PACKAGE_CALL_SITES]
            + [repo / rel for rel in WORKSTATION_CALL_SITES]
            + [root / COMPILED_BUNDLE])


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


def token_colours(value: str, tokens: dict[str, str]) -> list[tuple[float, float, float]]:
    """The colours a token's declared value resolves to, in order.

    A plain colour yields one. A composite such as `2px solid var(--border-focus)` yields the
    colour of each var() inside it, so a composite fallback can be compared position by position.
    """
    direct = resolve(value, tokens)
    if direct is not None:
        return [direct]
    out = []
    for name in INNER_VAR.findall(value):
        rgb = resolve(f"var({name})", tokens)
        if rgb is not None:
            out.append(rgb)
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
    for base in call_sites(root):
        if not base.exists():
            continue
        paths = [base] if base.is_file() else sorted(base.rglob("*"))
        for path in paths:
            if not path.is_file():
                continue
            if path != base and path.suffix not in CALL_SITE_SUFFIXES:
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            if path.suffix == ".css":
                text = blank_comments(text)
            local = set(DECL_NAME.findall(text)) | set(JS_DECL_NAME.findall(text))
            tokens = (workstation_tokens if str(path).find("dashboard") >= 0
                      else package_tokens)
            for lineno, line in enumerate(text.splitlines(), 1):
                for found in VAR_CALL.finditer(line):
                    if not found.group(2):        # no comma: no fallback to check
                        continue
                    parsed = split_var_call(line, found.start())
                    if parsed is None:
                        continue
                    name, fallback = parsed
                    if not fallback or not (HEX_LITERAL.search(fallback)
                                            or RGBA_LITERAL.search(fallback)):
                        continue
                    if name not in tokens:
                        if name.startswith(LOCAL_PREFIXES) or name in local:
                            continue
                        checked += 1
                        stale = [h for h in HEX_LITERAL.findall(fallback)
                                 if len(h) == 7 and h.upper() in superseded]
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
                    hexes = HEX_LITERAL.findall(fallback)
                    rgbas = RGBA_LITERAL.findall(fallback)
                    gots: list[tuple[float, float, float] | None] = []
                    for literal in hexes:
                        gots.append(hex_to_rgb(literal))
                    for r, g, b in rgbas:
                        gots.append((float(r), float(g), float(b)))
                    # A wash token's literal carries the hue at the author's own alpha. Follow
                    # bare aliases first: --blue-a10 is declared as var(--accent-a10), and the
                    # color-mix only appears one level down.
                    mix = MIX_ALPHA.match(deref(name, tokens))
                    if mix:
                        hue = resolve(mix.group(1), tokens)
                        wants = [hue] if hue is not None else []
                    for index, got in enumerate(gots):
                        if got is None or index >= len(wants):
                            continue
                        want = wants[index]
                        checked += 1
                        if max(abs(want[i] - got[i]) for i in range(3)) > 1.0:
                            shown = (hexes + [f"rgb({r},{g},{b})" for r, g, b in rgbas])[index]
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
    """Every token-bearing CSS rule in a component source must be in the compiled bundle."""
    bundle_path = root / COMPILED_BUNDLE
    if not bundle_path.exists():
        return []
    bundle = bundle_path.read_text(encoding="utf-8", errors="replace")
    failures: list[str] = []
    checked = 0
    for path in sorted((root / "components").rglob("*.jsx")):
        for lineno, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
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
        print(f"[bundle] all {checked} token-bearing component rules are present verbatim in "
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
BRAND_FONTS = ("Space Grotesk", "IBM Plex", "JetBrains Mono", "Inter")


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
            fonts = [f for f in BRAND_FONTS if f in text]
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
ACCENT_SURFACES = ("--blue-a10", "--accent-a10", "--bg-active", "--accent-ghost")
CSS_RULE = re.compile(r"[^{}]*\{[^{}]*\}", re.S)
RULE_BG = re.compile(r"background(?:-color)?\s*:\s*([^;}]+)")
RULE_FG_ACCENT = re.compile(r"(?<![-a-z])color\s*:\s*var\(\s*--accent\s*[,)]")


def check_accent_on_wash(root: Path) -> list[str]:
    """No rule may put raw `--accent` text on an accent-tinted surface."""
    failures: list[str] = []
    scanned = 0
    for base in call_sites(root):
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
                        f"{selector[:44]} puts --accent text on an accent-tinted surface "
                        "(3.87–4.41:1) — wash text is --accent-dim")
    for f in failures[:10]:
        print(f)
    if not failures:
        print(f"[accent-wash] none of the {scanned} accent-tinted rules uses the raw accent as text")
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
