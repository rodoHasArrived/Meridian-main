#!/usr/bin/env python3
"""Governance checks for the Meridian design-system package.

Rewritten for the current repo layout (tokens/, guidelines/, docs/, components/, templates/).
The previous version of this script (kept in git history, not in this tree) checked a pre-reorg
layout — `ui_kits/`, `preview/`, `colors_and_type.css`, `INSPIRATION_BRIEF.md` — none of which
exist anymore. That drift meant the "governance" suite was silently checking nothing real; the
lesson carried into this rewrite is in the docstring of `run_checks` below.
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


TEXT_SUFFIXES = {".css", ".html", ".md", ".jsx"}
FORBIDDEN_VISIBLE_WORKSPACE_NAMES = ("Overview", "Data Operations", "Data Ops", "Governance")
HEX_PATTERN = re.compile(r"#[0-9a-fA-F]{3,8}\b")
VAR_FALLBACK_PATTERN = re.compile(r"var\([^)]*\)")
RADIUS_PATTERN = re.compile(r"border-radius\s*:\s*([0-9.]+)(px|rem)", re.IGNORECASE)
GRADIENT_PATTERN = re.compile(r"\b(?:linear|radial)-gradient\(", re.IGNORECASE)
TAG_ATTR_PATTERN = re.compile(r"(?:href|src)=\"([^\"]+)\"", re.IGNORECASE)
LOCAL_UPLOAD_PREFIX = "uploads/"
VIEWPORT_PATTERN = re.compile(
    r'<meta\s+name="viewport"\s+content="width=device-width,\s*initial-scale=1"\s*/?>',
    re.IGNORECASE,
)
MAIN_PATTERN = re.compile(r"<main\b", re.IGNORECASE)
H1_PATTERN = re.compile(r"<h1\b", re.IGNORECASE)
X_IMPORT_FROM_PATTERN = re.compile(r'<x-import\b[^>]*\bfrom="([^"]+)"', re.IGNORECASE)
WHITE_ON_FILL_PATTERN = re.compile(r"color\s*:\s*(?:white|#fff(?:fff)?)\b", re.IGNORECASE)
GALLERY_CARD_PATTERN = re.compile(r'"([^"]+\.card\.html)"')


@dataclass(frozen=True)
class Violation:
    code: str
    path: str
    line: int
    detail: str

    def format(self) -> str:
        return f"{self.code}: {self.path}:{self.line}: {self.detail}"


def load_baseline(root: Path) -> dict[str, list[str]]:
    baseline_path = root / "scripts" / "governance-baseline.json"
    if not baseline_path.exists():
        return {}
    with baseline_path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    return {key: list(value) for key, value in data.items()}


def rel_path(root: Path, path: Path) -> str:
    return path.relative_to(root).as_posix()


def is_allowed(rel: str, baseline: dict[str, list[str]], key: str) -> bool:
    # fnmatch has no path-aware "**" — a bare "*" already matches across "/", so patterns like
    # "*.card.html" or "tokens/*.css" are intentionally broad. Keep patterns as narrow as the
    # exception actually needs.
    return any(fnmatch.fnmatch(rel, pattern) for pattern in baseline.get(key, []))


def iter_text_files(root: Path) -> Iterable[Path]:
    skip_dirs = {"node_modules", ".git"}
    for path in root.rglob("*"):
        if any(part in skip_dirs for part in path.parts):
            continue
        if path.is_file() and path.suffix.lower() in TEXT_SUFFIXES:
            yield path


def line_number(text: str, index: int) -> int:
    return text.count("\n", 0, index) + 1


def is_external_or_dynamic(link: str) -> bool:
    return (
        link.startswith(("http:", "https:", "mailto:", "data:", "#", "javascript:"))
        or "${" in link
        or not link.strip()
    )


def check_local_links(root: Path, path: Path, text: str) -> list[Violation]:
    """Every relative href/src in an .html file should resolve to a real file in the tree."""
    violations: list[Violation] = []
    for match in TAG_ATTR_PATTERN.finditer(text):
        link = match.group(1)
        if is_external_or_dynamic(link):
            continue
        clean = link.split("#", 1)[0].split("?", 1)[0]
        if not clean:
            continue
        normalized = clean.replace("\\", "/")
        if normalized.lower().startswith(LOCAL_UPLOAD_PREFIX):
            violations.append(
                Violation(
                    "local-upload-reference",
                    rel_path(root, path),
                    line_number(text, match.start()),
                    f"Use a tracked assets/ path instead of local-only `{link}`",
                )
            )
            continue
        target = (path.parent / Path(*clean.split("/"))).resolve()
        if not target.exists():
            violations.append(
                Violation("local-link", rel_path(root, path), line_number(text, match.start()), f"Missing target `{link}`")
            )
    return violations


def check_raw_hex(root: Path, path: Path, text: str, baseline: dict[str, list[str]]) -> list[Violation]:
    """Flag hardcoded hex outside the token/doc layer.

    A hex code that appears only as the fallback in `var(--token, #hex)` is the sanctioned
    pattern (guidelines/TOKEN_REFERENCE.md: "use the token with a hex fallback only") and is
    exempt. A bare `color: #FFFFFF` (no `var()` at all) is not — that's exactly the shape of the
    bug this check exists to catch (see BlankWorkstation's old hardcoded Live-pill text color).
    """
    rel = rel_path(root, path)
    if is_allowed(rel, baseline, "raw_hex_allowed"):
        return []
    exempt_spans = [m.span() for m in VAR_FALLBACK_PATTERN.finditer(text)]

    def in_exempt_span(pos: int) -> bool:
        return any(start <= pos < end for start, end in exempt_spans)

    return [
        Violation("raw-hex", rel, line_number(text, match.start()), f"Use a token instead of `{match.group(0)}`")
        for match in HEX_PATTERN.finditer(text)
        if not in_exempt_span(match.start())
    ]


def check_large_radius(root: Path, path: Path, text: str, baseline: dict[str, list[str]]) -> list[Violation]:
    rel = rel_path(root, path)
    if is_allowed(rel, baseline, "large_radius_allowed"):
        return []
    violations: list[Violation] = []
    for match in RADIUS_PATTERN.finditer(text):
        value = float(match.group(1))
        px = value * 16 if match.group(2).lower() == "rem" else value
        line_start = text.rfind("\n", 0, match.start()) + 1
        line_end = text.find("\n", match.end())
        line = text[line_start : len(text) if line_end == -1 else line_end].lower()
        if px > 6 and "50%" not in line and "999" not in line:
            violations.append(
                Violation("large-radius", rel, line_number(text, match.start()), f"Radius {match.group(0)} exceeds Concrete's 6px ceiling")
            )
    return violations


def check_gradients(root: Path, path: Path, text: str, baseline: dict[str, list[str]]) -> list[Violation]:
    rel = rel_path(root, path)
    if is_allowed(rel, baseline, "gradient_allowed"):
        return []
    return [
        Violation("decorative-gradient", rel, line_number(text, match.start()), "Gradient outside an approved context (Concrete surfaces are flat) \u2014 allowlist it in governance-baseline.json only if it's a motion sweep or chart fill, not decoration")
        for match in GRADIENT_PATTERN.finditer(text)
    ]


def normalized_visible_label(value: str) -> str:
    return re.sub(r"\s+", " ", value).strip().strip("`*_#-[]()")


def check_workspace_names(root: Path, path: Path, text: str, baseline: dict[str, list[str]]) -> list[Violation]:
    """Guard against regressing to the old generic top-level labels the app moved away from."""
    rel = rel_path(root, path)
    if is_allowed(rel, baseline, "legacy_workspace_mentions_allowed"):
        return []
    violations: list[Violation] = []
    lines = re.split(r"<[^>]+>|\n", text) if path.suffix.lower() == ".html" else text.splitlines()
    for line in lines:
        label = normalized_visible_label(line)
        if label in FORBIDDEN_VISIBLE_WORKSPACE_NAMES:
            violations.append(
                Violation("legacy-workspace", rel, line_number(text, text.find(line)), f"`{label}` is not a visible root workspace name")
            )
    return violations


def check_template_entry_metadata(root: Path, path: Path, text: str, baseline: dict[str, list[str]]) -> list[Violation]:
    """Template entry points are the first thing a consumer opens \u2014 hold them to the document
    contract WORKSTATION_BLUEPRINT.md and ACCESSIBILITY.md's own checklist expect: a viewport
    meta tag, a `<main>` landmark, and one `<h1>`. (Adapted from a check that used to apply to a
    now-removed `preview/` folder and a `colors_and_type.css` + `preview-common.css` stylesheet
    pair that no longer exist.)

    Every template today is a thin Design Component wrapper: <x-dc> + <helmet> + a single
    <x-import from="./screen.jsx"> that mounts the real screen. The wrapper's own source never
    contains a literal <main>/<h1> - those live in the imported screen file, so the landmark/
    heading check follows the x-import to that sibling file when there is one. The viewport meta
    is still checked on the wrapper itself, since it owns the static <head>.
    """
    rel = rel_path(root, path)
    is_template_entry = rel.startswith("templates/") and (rel.endswith("/index.html") or rel.endswith(".dc.html"))
    if not is_template_entry or is_allowed(rel, baseline, "template_entry_allowed"):
        return []
    violations: list[Violation] = []
    if not VIEWPORT_PATTERN.search(text):
        violations.append(
            Violation("template-viewport", rel, 1, 'Template entry must include `<meta name="viewport" content="width=device-width, initial-scale=1">`')
        )
    content_text = text
    import_match = X_IMPORT_FROM_PATTERN.search(text)
    if import_match:
        sibling = (path.parent / import_match.group(1)).resolve()
        if sibling.exists():
            content_text = sibling.read_text(encoding="utf-8")
    if not MAIN_PATTERN.search(content_text):
        violations.append(Violation("template-main-landmark", rel, 1, "Template entry (or the screen its x-import mounts) must expose a main landmark"))
    if not H1_PATTERN.search(content_text):
        violations.append(Violation("template-heading", rel, 1, "Template entry (or the screen its x-import mounts) must expose exactly one h1 heading"))
    return violations


def check_white_on_fill(root: Path, path: Path, text: str, baseline: dict[str, list[str]]) -> list[Violation]:
    """The July 2026 contrast sweep's bug class: `color: white` (or bare #fff) on a component.
    White ink on a solid fill fails AA the moment dark mode lightens the fill — route it through
    `var(--text-on-accent)` (accent fills) or `var(--text-on-fill)` (semantic fills) instead.
    Only component sources are checked; a hex inside a `var(--x, #fff)` fallback is sanctioned.
    """
    rel = rel_path(root, path)
    if not rel.startswith("components/") or path.suffix.lower() != ".jsx":
        return []
    if is_allowed(rel, baseline, "white_on_fill_allowed"):
        return []
    exempt_spans = [m.span() for m in VAR_FALLBACK_PATTERN.finditer(text)]

    def in_exempt_span(pos: int) -> bool:
        return any(start <= pos < end for start, end in exempt_spans)

    return [
        Violation("white-on-fill", rel, line_number(text, match.start()), "Use var(--text-on-accent) / var(--text-on-fill) instead of hardcoded white on a fill")
        for match in WHITE_ON_FILL_PATTERN.finditer(text)
        if not in_exempt_span(match.start())
    ]


def check_gallery_coverage(root: Path, baseline: dict[str, list[str]]) -> list[Violation]:
    """tests/gallery.html promises to render every @dsCard demo — hold it to that. Diffs the
    CARDS list in the gallery against `*.card.html` on disk, both directions."""
    gallery = root / "tests" / "gallery.html"
    if not gallery.exists():
        return [Violation("gallery-missing", "tests/gallery.html", 1, "Card gallery page is missing")]
    text = gallery.read_text(encoding="utf-8")
    listed = set(GALLERY_CARD_PATTERN.findall(text))
    on_disk = {
        rel_path(root, p)
        for p in root.rglob("*.card.html")
        if not any(part in {"node_modules", ".git", "uploads"} for part in p.parts)
    }
    violations: list[Violation] = []
    for missing in sorted(on_disk - listed):
        if is_allowed(missing, baseline, "gallery_coverage_allowed"):
            continue
        violations.append(
            Violation("gallery-coverage", "tests/gallery.html", 1, f"Card `{missing}` exists on disk but is not in the gallery's CARDS list")
        )
    for stale in sorted(listed - on_disk):
        violations.append(
            Violation("gallery-stale-entry", "tests/gallery.html", 1, f"Gallery lists `{stale}` which does not exist on disk")
        )
    return violations


def check_prompt_coverage(root: Path, baseline: dict[str, list[str]]) -> list[Violation]:
    """Every exported component ships a per-component `X.prompt.md` so AI consumers get usage
    guidance beyond the raw type signature. Any PascalCase `X.jsx` with a sibling `X.d.ts` under
    components/ must have a sibling `X.prompt.md`. Derived from the tree \u2014 don't hardcode a
    component list (that drift is exactly how the old suite silently stopped checking anything);
    allowlist a genuine exception in `prompt_coverage_allowed`, keyed on the `.jsx` path.
    """
    components_root = root / "components"
    if not components_root.exists():
        return []
    violations: list[Violation] = []
    for dts in sorted(components_root.rglob("*.d.ts")):
        base = dts.name[:-5]  # strip ".d.ts"
        if not base[:1].isupper():
            continue
        jsx = dts.with_name(base + ".jsx")
        if not jsx.exists():
            continue
        if dts.with_name(base + ".prompt.md").exists():
            continue
        if is_allowed(rel_path(root, jsx), baseline, "prompt_coverage_allowed"):
            continue
        violations.append(
            Violation(
                "prompt-coverage",
                rel_path(root, jsx),
                1,
                f"Component `{base}` has {base}.jsx + {base}.d.ts but no {base}.prompt.md (per-component usage guidance)",
            )
        )
    return violations


def run_checks(root: Path, baseline: dict[str, list[str]] | None = None) -> list[Violation]:
    """Re-derive `baseline` from the CURRENT tree if you're extending this: don't copy an
    allowlist forward from memory the way the old baseline was, or it silently stops matching
    real paths the moment anything gets renamed or moved (exactly what happened before this
    rewrite). Prefer fixing the violation over allowlisting it; the baseline exists for
    grandfathered exceptions, not as a way to make a rule permanently quiet.
    """
    baseline = baseline if baseline is not None else load_baseline(root)
    violations: list[Violation] = []
    for path in iter_text_files(root):
        text = path.read_text(encoding="utf-8")
        if path.suffix.lower() == ".html":
            violations.extend(check_local_links(root, path, text))
            violations.extend(check_template_entry_metadata(root, path, text, baseline))
        violations.extend(check_raw_hex(root, path, text, baseline))
        violations.extend(check_white_on_fill(root, path, text, baseline))
        violations.extend(check_large_radius(root, path, text, baseline))
        violations.extend(check_gradients(root, path, text, baseline))
        violations.extend(check_workspace_names(root, path, text, baseline))
    violations.extend(check_gallery_coverage(root, baseline))
    violations.extend(check_prompt_coverage(root, baseline))
    return violations


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Check Meridian design-system governance rules.")
    parser.add_argument("--root", default=Path(__file__).resolve().parents[1], type=Path)
    args = parser.parse_args(argv)
    root = args.root.resolve()
    violations = run_checks(root)
    if violations:
        print("Design-system governance: FAIL")
        for violation in violations:
            print(violation.format())
        return 1
    print("Design-system governance: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
