#!/usr/bin/env python3
"""Offline governance for executable design-system components and template compositions.

Validate component companions, token use, ARIA references, manifest/eligibility,
local HTML dependencies and document/preview contracts. Follow mounted imports;
markup in unrelated sibling files cannot satisfy a template contract. Static
checks do not establish browser or assistive-technology behavior.
"""
from __future__ import annotations
import argparse
from dataclasses import dataclass
import fnmatch
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parent.parent
IMPORTANT_PRAGMA = re.compile(r"/\*\s*important:\s*\S")
COLOR_DECL = re.compile(r"(?:^|[;{\s])(?:color|background(?:-color)?|(?:border|outline)(?:-[\w-]+)?|fill|stroke|box-shadow|text-shadow)\s*:\s*([^;}\n]+)", re.M)

@dataclass(frozen=True)
class Violation:
    code: str
    path: str
    line: int
    detail: str
    def format(self):
        return f"[{self.code}] {self.path}:{self.line}: {self.detail}"

def strip_comments(src):
    """Preserve source positions and quoted URLs; strip JS/CSS/HTML comments."""
    out, i, quote = list(src), 0, None
    while i < len(src):
        if src[i] == "\\" and quote:
            i += 2
            continue
        if src[i] in "\"'`":
            if quote is None: quote = src[i]
            elif src[i] == quote: quote = None
            i += 1
            continue
        end = None
        if quote not in ("'", '"') and src.startswith("/*", i):
            finish = src.find("*/", i + 2)
            end = len(src) if finish < 0 else finish + 2
        elif quote is None and src.startswith("//", i):
            finish = src.find("\n", i + 2)
            end = len(src) if finish < 0 else finish
        elif quote is None and src.startswith("<!--", i):
            finish = src.find("-->", i + 4)
            end = len(src) if finish < 0 else finish + 3
        if end is None:
            i += 1
        else:
            for pos in range(i, end):
                if src[pos] != "\n": out[pos] = " "
            i = end
    return "".join(out)

def blank_comments(src):
    return strip_comments(src).splitlines()

class Document(HTMLParser):
    def __init__(self, text):
        super().__init__(convert_charrefs=True)
        self.tags = []
        self.feed(text)
    def handle_starttag(self, tag, attrs):
        self.tags.append((tag, dict(attrs), self.getpos()[0]))
    handle_startendtag = handle_starttag

def dynamic(value):
    return not value or any(mark in value for mark in ("{{", "${"))

class Audit:
    def __init__(self, root, baseline=None):
        self.root = Path(root).resolve()
        file = self.root / "scripts/governance-baseline.json"
        self.baseline = baseline if baseline is not None else (json.loads(file.read_text()) if file.exists() else {})
        self.failures, self.warnings, self.cache = [], [], {}
        self.manifest = self.read_json("_ds_manifest.json", required=(self.root / "release.json").exists())
        self.sources = {c["name"]: c.get("sourcePath") for c in self.manifest.get("components", [])}
        retired = self.read_json("api/retired-exports.json", required=False)
        self.retired = retired.get("exports", {} if not (self.root / "api/retired-exports.json").exists() else None) if isinstance(retired, dict) else None
        if not isinstance(self.retired, dict):
            self.add("manifest", self.root / "api/retired-exports.json", "Retired exports must be an exports object")
            self.retired = {}
    def add(self, code, path, detail, line=1, warning=False):
        item = Violation(code, Path(path).relative_to(self.root).as_posix(), line, detail)
        (self.warnings if warning else self.failures).append(item)
    def read_json(self, name, required=True):
        path = self.root / name
        if not path.exists() and not required: return {}
        try: return json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            self.add("generated", path, f"Cannot read JSON: {exc}")
            return {}
    def text(self, path):
        if path not in self.cache: self.cache[path] = path.read_text(encoding="utf-8")
        return self.cache[path]
    def allowed(self, path, key):
        return any(fnmatch.fnmatch(path.relative_to(self.root).as_posix(), pat) for pat in self.baseline.get(key, []))
    def target(self, path, value):
        if dynamic(value): return None
        url = urlsplit(value)
        if url.scheme or url.netloc or not url.path: return None
        clean = unquote(url.path).replace("\\", "/")
        return (self.root / clean.lstrip("/") if clean.startswith("/") else path.parent / clean).resolve()
    def component_checks(self):
        for jsx in sorted(self.root.glob("components/*/*.jsx")):
            if not jsx.stem[:1].isupper(): continue
            if not jsx.with_suffix(".d.ts").exists(): self.add("trio", jsx, f"Missing {jsx.stem}.d.ts")
            if not jsx.with_name(jsx.stem + ".prompt.md").exists(): self.add("prompt-coverage", jsx, f"Missing {jsx.stem}.prompt.md")
            source = self.text(jsx)
            code = strip_comments(source)
            for line, (original, content) in enumerate(zip(source.splitlines(), code.splitlines()), 1):
                if "!important" in content and not IMPORTANT_PRAGMA.search(original):
                    self.add("no-important", jsx, "Undeclared !important; document the necessary exception with /* important: reason */", line)
            for match in re.finditer(r'aria-(?:controls|labelledby|activedescendant)="([^"{]+)"', code):
                self.add("aria-refs", jsx, f"Hard-coded {match.group(0)}", code.count("\n", 0, match.start()) + 1)
            for name in re.findall(r"export (?:const|function) ([A-Z]\w+)", code):
                if self.manifest and name not in self.sources:
                    retired = self.retired.get(name)
                    if not isinstance(retired, dict) or retired.get("source") != jsx.relative_to(self.root).as_posix():
                        self.add("manifest", jsx, f"Export {name} is in neither the public manifest nor its declared retired source; publish it or declare it in api/retired-exports.json")
    def style_checks(self, path):
        # Cards/docs/tokens are illustrative references. Check executable component/template
        # declarations, not hash-shaped run identifiers or color examples in prose.
        code = strip_comments(self.text(path))
        if not self.allowed(path, "raw_hex_allowed"):
            declarations = [re.match(r"([\s\S]*)", code)] if path.suffix == ".jsx" and path.is_relative_to(self.root / "components") else COLOR_DECL.finditer(code)
            for declaration in declarations:
                value, spans = declaration.group(1), []
                for match in re.finditer(r"\bvar\(", value):
                    depth, end = 1, match.end()
                    while depth and end < len(value):
                        depth += (value[end] == "(") - (value[end] == ")")
                        end += 1
                    spans.append((match.start(), end))
                for match in re.finditer(r"#[0-9a-fA-F]{3,8}\b", value):
                    if any(a <= match.start() < b for a, b in spans) or "url(" in value: continue
                    pos = declaration.start(1) + match.start()
                    self.add("raw-hex", path, f"Use a token instead of {match.group(0)}", code.count("\n", 0, pos) + 1)
        if not self.allowed(path, "large_radius_allowed"):
            for match in re.finditer(r"border-radius\s*:\s*([^;}\n]+)", code, re.I):
                value = re.sub(r"var\([^)]*\)", "", match.group(1))
                values = re.findall(r"(?<![\w.-])(\d+(?:\.\d+)?)(px|rem)\b", value)
                if any(float(n) * (16 if unit == "rem" else 1) > 8 for n, unit in values):
                    self.add("large-radius", path, "Radius exceeds the current 8px dialog ceiling", code.count("\n", 0, match.start()) + 1)
    def html_links(self, path, document):
        for _, attrs, line in document.tags:
            for attr in ("href", "src"):
                value = attrs.get(attr)
                target = self.target(path, value)
                if target is None: continue
                rel = target.relative_to(self.root).as_posix() if target.is_relative_to(self.root) else ""
                if "uploads" in Path(rel).parts:
                    self.add("local-upload-reference", path, f"Use tracked assets instead of local upload {value}", line)
                elif not target.exists(): self.add("local-link", path, f"Missing target {value}", line)
    def dependencies(self, path):
        deps = []
        if path.suffix != ".html": return deps
        for tag, attrs, line in Document(strip_comments(self.text(path))).tags:
            target = None
            if tag == "x-import" and attrs.get("from"):
                target = self.target(path, attrs["from"])
                if target and not target.is_file(): self.add("local-link", path, f"Missing mounted source {attrs['from']}", line)
            elif tag == "dc-import" and not dynamic(attrs.get("name")):
                name = attrs["name"] + ".dc.html"
                sibling = path.parent / name
                candidates = [sibling] if sibling.exists() else sorted(self.root.glob("templates/*/" + name))
                if len(candidates) == 1: target = candidates[0]
                else: self.add("local-link", path, f"Cannot resolve mounted template {attrs['name']}", line)
            if target and target.is_file() and target.is_relative_to(self.root): deps.append(target)
        return deps
    def closure(self, path, seen=None):
        seen = set() if seen is None else seen
        if path in seen: return []
        seen.add(path)
        return [(path, strip_comments(self.text(path)))] + [item for dep in self.dependencies(path) for item in self.closure(dep, seen)]
    def template_document(self, path, document):
        if not any(tag == "meta" and attrs.get("name", "").lower() == "viewport" and re.search(r"width\s*=\s*device-width", attrs.get("content", "")) for tag, attrs, _ in document.tags):
            self.add("template-viewport", path, "Template entry needs a device-width viewport")
        content = "\n".join(text for _, text in self.closure(path))
        # WorkstationShell owns <main>. Mentioning any other component is not evidence.
        if not re.search(r'<main\b|\brole=[\"\']main[\"\']|<[^>]*\bWorkstationShell\b', content):
            self.add("template-main-landmark", path, "Entry or mounted screen must expose a main landmark")
        if not re.search(r"<h1\b", content):
            self.add("template-heading", path, "Entry or mounted screen must expose an h1")
        for match in re.finditer(r"<h1\b[^>]*>\s*(Overview|Data Operations|Data Ops|Governance)\s*</h1>", content, re.I):
            if not self.allowed(path, "legacy_workspace_mentions_allowed"):
                self.add("legacy-workspace", path, f"{match.group(1)} is not a visible root workspace name")
    def entries(self):
        """Public manifest entries plus retained standalone entries, not embedded subviews."""
        entries = {self.root / t["entryPath"] for t in self.manifest.get("templates", []) if t.get("entryPath")}
        declared_folders = {p.parent for p in entries}
        for folder in (self.root / "templates").glob("*"):
            if not folder.is_dir() or folder.name.startswith("_") or folder in declared_folders: continue
            pascal = "".join(part.capitalize() for part in folder.name.split("-"))
            candidates = [folder / (pascal + ".dc.html"), folder / "index.html"]
            preferred = next((p for p in candidates if p.is_file()), None)
            if preferred: entries.add(preferred)
            else:
                actual = list(folder.glob("*.dc.html"))
                if len(actual) == 1: entries.add(actual[0])
                elif not actual: self.add("template", folder, "No template entry exists")
                else: self.add("template", folder, "Ambiguous retained entries; declare the entry in the manifest")
        return entries
    def template_checks(self):
        for entry in sorted(self.entries()):
            if not entry.is_file(): continue
            folder = entry.parent
            source = self.text(entry)
            if "@template" not in source[:1200]: self.add("template", entry, "@template marker not near the top")
            if not (folder / "ds-base.js").exists(): self.add("template", entry, "ds-base.js missing")
            props = [attrs["data-props"] for tag, attrs, _ in Document(source).tags if tag == "script" and "data-props" in attrs]
            try: config = json.loads(props[0]) if props else {}
            except ValueError:
                config = {}
                self.add("template", entry, "data-props is not valid JSON")
            if not isinstance(config, dict):
                config = {}
                self.add("template", entry, "data-props must be a JSON object")
            if "$preview" not in config: self.add("template", entry, "$preview missing from data-props")
            for tweak in ("density", "theme", "environment", "dataState"):
                if tweak not in config: self.add("template", entry, f"Standard tweak {tweak!r} missing from data-props")
            content = "\n".join(text for _, text in self.closure(entry))
            mounted_state = re.search(
                r'<(?:[\w.]+\.)?DataStateSwitch\b|(?:component-from-global-scope|component)=["\'][^"\']*\bDataStateSwitch["\']|createElement\(\s*(?:[\w.]+\.)?DataStateSwitch\b',
                content,
            )
            if not mounted_state: self.add("template", entry, "DataStateSwitch not mounted by entry or mounted screen")
            for owner, text in self.closure(entry):
                for route in re.findall(r'["\'](\.\./[^"\'<>]+\.dc\.html)["\']', text):
                    target = self.target(owner, route)
                    if target is not None and not target.exists(): self.add("routes", owner, f"Broken route {route}")
    def package_checks(self):
        retired_path = self.root / "api/retired-exports.json"
        for name, record in sorted(self.retired.items()):
            if not isinstance(record, dict):
                self.add("manifest", retired_path, f"Retired export {name} must have a source and public replacement")
                continue
            if name in self.sources:
                self.add("manifest", retired_path, f"{name} is listed as retired but the public manifest exports it")
            source = record.get("source")
            path = (self.root / source).resolve() if isinstance(source, str) and source else None
            if (path is None or not path.is_relative_to(self.root) or not path.is_file()
                    or not re.search(rf"export (?:const|function) {re.escape(name)}\b", strip_comments(self.text(path)))):
                self.add("manifest", retired_path, f"Retired export {name}: {source!r} no longer exports it; remove the stale retirement record")
            replacement = record.get("replacement")
            if not isinstance(replacement, str) or replacement not in self.sources:
                self.add("manifest", retired_path, f"Retired export {name}: replacement {replacement!r} is not in the public manifest")
        for comp in self.manifest.get("components", []):
            path = comp.get("sourcePath")
            if path and not (self.root / path).is_file(): self.add("manifest", self.root / "_ds_manifest.json", f"Missing component source {path}")
        for entry in self.manifest.get("templates", []):
            path = entry.get("entryPath")
            if not path or not (self.root / path).is_file(): self.add("manifest", self.root / "_ds_manifest.json", f"Missing template entry {path}")
        if self.manifest:
            for name in ("_ds_bundle.js", "_ds_manifest.json", "_adherence.oxlintrc.json", "support.js"):
                if not (self.root / name).is_file(): self.add("generated", self.root / name, "Required generated file missing")
            readme = self.root / "README.md"
            if not readme.exists() or "inventory:generated:begin" not in self.text(readme): self.add("generated", readme, "Generated inventory markers missing")
            classified = set(self.read_json("api/template-eligibility.json").get("templates", {}))
            on_disk = {p.name for p in (self.root / "templates").iterdir() if p.is_dir() and not p.name.startswith("_")}
            for name in sorted(on_disk - classified): self.add("eligibility", self.root / "api/template-eligibility.json", f"Unclassified template {name}")
            for name in sorted(classified - on_disk): self.add("eligibility", self.root / "api/template-eligibility.json", f"Classified template {name} no longer exists")
    def runtime_reference_checks(self):
        """No page may read a retired component namespace or fetch its runtime from a remote host.

        Neither fault shows in source structure: a page on the previous build's namespace throws
        on load and renders blank, and a page that loads React or the JSX compiler from a CDN
        needs a network the package otherwise never uses. The standalone reconciliation page is
        a self-contained archive that embeds its own bundle on purpose; the preview regression
        names the retired namespace only to assert it is gone.
        """
        manifest = self.read_json("_ds_manifest.json", required=False) or {}
        namespace = manifest.get("namespace", "")
        exempt = {"Accounting Reconciliation (standalone).html", "tests/template-preview-regression.mjs"}
        generated = {"_ds_bundle.js", "_preview_jsx.js", "_preview_runtime.js"}
        stale = re.compile(r"MeridianDesignSystem_[0-9a-f]{6}\b")
        remote = re.compile(r"<script\b[^>]*\bsrc\s*=\s*[\"']https?://", re.I)
        for path in sorted(self.root.rglob("*")):
            rel = path.relative_to(self.root)
            if not path.is_file() or path.suffix not in (".html", ".jsx", ".js", ".mjs", ".sh"): continue
            if any(part in ("node_modules", ".git", "dist") for part in rel.parts): continue
            if rel.parts[:2] == ("docs", "reviews") or path.name in generated: continue
            text = self.text(path)
            if namespace and rel.as_posix() not in exempt:
                for name in sorted(set(stale.findall(text)) - {namespace}):
                    self.add("namespace", path, f"Reads {name}; the bundle publishes {namespace}")
            if path.suffix == ".html" and remote.search(text):
                self.add("offline", path, "Loads a script from a remote host; use _preview_runtime.js and "
                         "_preview_jsx.js (pages) or templates/_scaffold/vendor (template entries)")
    def run(self):
        self.component_checks()
        self.runtime_reference_checks()
        for folder in ("components", "templates"):
            for path in sorted((self.root / folder).rglob("*")):
                if path.is_file() and path.suffix in (".jsx", ".html") and not path.name.endswith((".card.html", ".stories.html")):
                    self.style_checks(path)
        entries = self.entries()
        for path in sorted(self.root.rglob("*.html")):
            if any(part in ("node_modules", ".git", "dist") for part in path.relative_to(self.root).parts): continue
            document = Document(self.text(path))
            self.html_links(path, document)
            if path in entries:
                self.template_document(path, document)
        self.template_checks()
        self.package_checks()
        self.failures = list(dict.fromkeys(self.failures))
        return self.failures

def run_checks(root=ROOT, baseline=None):
    """Structured, reentrant API. Baseline overrides never change the checked surface."""
    return Audit(root, baseline).run()

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    audit = Audit(parser.parse_args(argv).root)
    audit.run()
    for warning in audit.warnings: print("WARN  " + warning.format())
    for violation in audit.failures: print("FAIL  " + violation.format())
    print(f"\n{len(audit.failures)} failures · {len(audit.warnings)} warnings")
    return bool(audit.failures)

if __name__ == "__main__": sys.exit(main())
