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

# A brand may restate its light identity, its dark identity, or both, in separate blocks.
# Merge a brand's blocks before checking so each variant is reported once per mode, and so
# a brand that states only a dark identity is not silently measured against the defaults.
BRAND_MODES = [("light", "--theme-"), ("dark", "--theme-dark-")]


def check_brands(root: Path) -> list[str]:
    """Check each data-brand variant's button states against its own label colour."""
    text = (root / "tokens" / "theme.css").read_text(encoding="utf-8")
    defaults = parse_tokens(text)
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


def run_checks(root: Path) -> list[str]:
    light = parse_tokens((root / "tokens" / "colors.css").read_text(encoding="utf-8"))
    dark_overrides = parse_tokens((root / "tokens" / "colors-dark.css").read_text(encoding="utf-8"))
    dark = {**light, **dark_overrides}
    return (check_mode("light", light) + check_mode("dark", dark)
            + check_brands(root) + check_workstation(root)
            + check_ws_aliases(root) + check_comment_terminators(root))


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
