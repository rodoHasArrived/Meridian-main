#!/usr/bin/env python3
"""Governance for the active Meridian design-system package.

Python checks document composition, links and inventory. Rendered visual values
are checked by the shared TypeScript/PostCSS policy, also used on the real browser
workstation. Documentation color examples, identifiers and comments are not CSS.
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from html import unescape
from pathlib import Path
from typing import Iterable
from urllib.parse import unquote


TEXT_SUFFIXES = {".css", ".html", ".md", ".jsx", ".js", ".ts", ".tsx"}
FORBIDDEN_VISIBLE_WORKSPACE_NAMES = ("Overview", "Data Operations", "Data Ops", "Governance")
TAG_ATTR_PATTERN = re.compile(r"""(?:href|src)\s*=\s*(["'])(.*?)\1""", re.IGNORECASE)
CSS_IMPORT_PATTERN = re.compile(r"""@import\s+(?:url\(\s*)?(["'])(.*?)\1""", re.IGNORECASE)
CSS_URL_PATTERN = re.compile(r"""url\(\s*(["']?)([^'")]+?)\1\s*\)""", re.IGNORECASE)
LOCAL_UPLOAD_PREFIX = "uploads/"
VIEWPORT_PATTERN = re.compile(
    r'<meta\s+name="viewport"\s+content="width=device-width,\s*initial-scale=1"\s*/?>',
    re.IGNORECASE,
)
MAIN_PATTERN = re.compile(r"<main\b", re.IGNORECASE)
H1_PATTERN = re.compile(r"<h1\b", re.IGNORECASE)
X_IMPORT_FROM_PATTERN = re.compile(r'<x-import\b[^>]*\bfrom="([^"]+)"', re.IGNORECASE)
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
    skip_dirs = {"node_modules", ".git", "__pycache__", "__tests__"}
    for path in root.rglob("*"):
        if any(part in skip_dirs for part in path.parts):
            continue
        if path.is_file() and path.suffix.lower() in TEXT_SUFFIXES and not re.search(r"\.(?:test|spec)\.[cm]?[jt]sx?$", path.name):
            yield path


def check_rendered_styles(root: Path, paths: list[Path]) -> list[Violation]:
    """Use the same AST/PostCSS policy for package examples and native adapters.

    Documentation prose and custom-property declarations are not component color
    usages. Conversely a .card.html extension must not exempt its rendered CSS.
    Node tooling is an explicit development prerequisite, not an optional check:
    failures to invoke/parse it fail closed rather than silently skip sources.
    """
    script = Path(__file__).resolve().parents[2] / "src/Meridian.Ui/dashboard/scripts/design-system-policy.mjs"
    files = [str(path) for path in paths
             if path.suffix.lower() in {".css", ".html", ".jsx", ".js", ".ts", ".tsx"}
             and not path.name.endswith(".d.ts")]
    try:
        result = subprocess.run(
            ["node", str(script), "--stdin", "--json"],
            input=json.dumps({"files": files, "base": str(root)}),
            capture_output=True, text=True, check=False,
        )
        if result.returncode not in {0, 1}:
            raise ValueError(result.stderr.strip() or f"policy process exited {result.returncode}")
        findings = json.loads(result.stdout)
        if not isinstance(findings, list):
            raise ValueError("policy output is not a diagnostics array")
        return [Violation(**item) for item in findings]
    except (OSError, ValueError, TypeError) as error:
        return [Violation("style-policy-unavailable", "scripts/check_design_system_governance.py", 1, str(error))]


def line_number(text: str, index: int) -> int:
    return text.count("\n", 0, index) + 1


def is_external_or_dynamic(link: str) -> bool:
    return (
        bool(re.match(r"^[a-z][a-z0-9+.-]*:", link, re.IGNORECASE))
        or link.startswith(("//", "#"))
        or "${" in link
        or "{{" in link
        or "var(" in link
        or not link.strip()
    )


def check_local_references(root: Path, path: Path, text: str, references: list[tuple[str, int]]) -> list[Violation]:
    """Resolve portable asset paths against their document/stylesheet, not cwd."""
    violations: list[Violation] = []
    seen: set[tuple[str, int]] = set()
    for link, index in references:
        link = link.strip()
        line = line_number(text, index)
        if (link, line) in seen:
            continue
        seen.add((link, line))
        if is_external_or_dynamic(link):
            continue
        clean = unquote(link.split("#", 1)[0].split("?", 1)[0])
        if not clean:
            continue
        normalized = clean.replace("\\", "/")
        if normalized.lower().startswith(LOCAL_UPLOAD_PREFIX):
            violations.append(Violation("local-upload-reference", rel_path(root, path), line,
                                        f"Use a tracked assets/ path instead of local-only `{link}`"))
            continue
        target = ((root / clean.lstrip("/")) if clean.startswith("/") else (path.parent / Path(*clean.split("/")))).resolve()
        if not target.exists():
            violations.append(Violation("local-link", rel_path(root, path), line, f"Missing target `{link}`"))
    return violations


def css_references(css: str, offset: int = 0) -> list[tuple[str, int]]:
    # Blank comments rather than remove them so line positions stay correct.
    css = re.sub(r"/\*[\s\S]*?\*/", lambda match: "".join("\n" if char == "\n" else " " for char in match.group()), css)
    return [(match.group(2), offset + match.start())
            for pattern in (CSS_IMPORT_PATTERN, CSS_URL_PATTERN) for match in pattern.finditer(css)]


def check_local_links(root: Path, path: Path, text: str) -> list[Violation]:
    """HTML attributes and embedded CSS both resolve against the HTML location."""
    rendered = re.sub(r"<!--[\s\S]*?-->", lambda match: "".join("\n" if char == "\n" else " " for char in match.group()), text)
    references = [(match.group(2), match.start()) for match in TAG_ATTR_PATTERN.finditer(rendered)]
    for match in re.finditer(r"<style\b[^>]*>([\s\S]*?)</style\s*>", rendered, re.IGNORECASE):
        references.extend(css_references(match.group(1), match.start(1)))
    for match in re.finditer(r"""\bstyle\s*=\s*(["'])(.*?)\1""", rendered, re.IGNORECASE):
        references.extend(css_references(unescape(match.group(2)), match.start(2)))
    return check_local_references(root, path, text, references)


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

    Design Component wrappers can own their landmarks or delegate them to one
    or more x-import children. Inspect the composition while retaining wrapper
    content. Browser acceptance checks the actual rendered document, including
    conditional branches that a static composition cannot fully evaluate.
    The viewport meta remains the wrapper's responsibility.
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
    # The wrapper remains part of the rendered composition. Imported children
    # supplement it; replacing it loses wrapper-owned landmarks/headings.
    def composed(current: Path, content: str, visited: set[Path]) -> str:
        if current in visited:
            return ""
        visited.add(current)
        parts = [re.sub(r"<!--[\s\S]*?-->|/\*[\s\S]*?\*/", "", content)]
        for match in X_IMPORT_FROM_PATTERN.finditer(content):
            sibling = (current.parent / match.group(1)).resolve()
            if sibling.is_file():
                parts.append(composed(sibling, sibling.read_text(encoding="utf-8"), visited))
        return "\n".join(parts)

    content_text = composed(path.resolve(), text, set())
    if len(MAIN_PATTERN.findall(content_text)) != 1:
        violations.append(Violation("template-main-landmark", rel, 1, "Template entry (or the screen its x-import mounts) must expose a main landmark"))
    if len(H1_PATTERN.findall(content_text)) != 1:
        violations.append(Violation("template-heading", rel, 1, "Template entry (or the screen its x-import mounts) must expose exactly one h1 heading"))
    return violations


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
    paths = sorted(iter_text_files(root))
    for path in paths:
        text = path.read_text(encoding="utf-8")
        if path.suffix.lower() == ".html":
            violations.extend(check_local_links(root, path, text))
            violations.extend(check_template_entry_metadata(root, path, text, baseline))
        elif path.suffix.lower() == ".css":
            violations.extend(check_local_references(root, path, text, css_references(text)))
        violations.extend(check_workspace_names(root, path, text, baseline))
    violations.extend(check_rendered_styles(root, paths))
    violations.extend(check_gallery_coverage(root, baseline))
    violations.extend(check_prompt_coverage(root, baseline))
    return violations


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Check Meridian design-system governance rules.")
    parser.add_argument("--root", default=Path(__file__).resolve().parents[1], type=Path)
    parser.add_argument("--json", action="store_true", help="Emit machine-readable diagnostics")
    args = parser.parse_args(argv)
    root = args.root.resolve()
    violations = run_checks(root)
    if args.json:
        print(json.dumps([{"code": v.code, "path": v.path, "line": v.line, "detail": v.detail} for v in violations]))
        return 1 if violations else 0
    if violations:
        print("Design-system governance: FAIL")
        for violation in violations:
            print(violation.format())
        return 1
    print("Design-system governance: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
