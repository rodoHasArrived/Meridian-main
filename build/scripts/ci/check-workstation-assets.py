#!/usr/bin/env python3
"""Validate generated workstation entry points and their local asset references."""
from __future__ import annotations

import argparse
from html.parser import HTMLParser
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit


ROOT = Path(__file__).resolve().parents[3]
ASSETS = "src/Meridian.Ui/wwwroot/workstation"


class EntryParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.references: list[str] = []
        self.scripts = 0

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag == "script" and values.get("src"):
            self.references.append(values["src"])
            self.scripts += 1
        if tag == "link" and values.get("href"):
            self.references.append(values["href"])


def validate(root: Path) -> list[str]:
    root = root.resolve()
    index = root / "index.html"
    if not index.is_file() or index.is_symlink():
        return ["Missing generated workstation/index.html; build the dashboard first"]
    parser = EntryParser()
    parser.feed(index.read_text(encoding="utf-8"))
    errors = []
    if not parser.scripts:
        errors.append("Workstation entry point contains no script")

    def check_reference(source: Path, reference: str):
        url = urlsplit(reference)
        if url.scheme or url.netloc or not url.path:
            return
        path = unquote(url.path)
        if path.startswith("/workstation/"):
            destination = root / path.removeprefix("/workstation/")
        elif path.startswith("/"):
            errors.append(f"Unsupported absolute asset reference: {reference}")
            return
        else:
            destination = source.parent / path
        if not destination.resolve().is_relative_to(root):
            errors.append(f"Asset reference escapes workstation tree: {reference}")
        elif not destination.is_file() or destination.is_symlink():
            errors.append(f"Missing asset referenced by {source.relative_to(root)}: {reference}")

    for reference in parser.references:
        check_reference(index, reference)
    # Vite emits lazy chunk paths as relative imports and root-relative entries
    # in its dependency map. Validate them as well as the initial HTML shell.
    for source in sorted(root.rglob("*")):
        if source.is_symlink():
            errors.append(f"Asset tree contains a symlink: {source.relative_to(root)}")
            continue
        if source.suffix not in {".js", ".css"} or not source.is_file():
            continue
        text = source.read_text(encoding="utf-8")
        if not text.strip():
            errors.append(f"Empty asset: {source.relative_to(root)}")
        if source.suffix == ".js":
            for match in re.finditer(r'''["']((?:\./|/workstation/|assets/)[^"'\s]+\.(?:js|css))["']''', text):
                reference = match.group(1)
                check_reference(index if reference.startswith("assets/") else source, reference)
        else:
            for match in re.finditer(r'''url\(\s*["']?([^\s"')]+)["']?\s*\)''', text):
                check_reference(source, match.group(1))
    return sorted(set(errors))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, default=ROOT / ASSETS)
    args = parser.parse_args()
    try:
        errors = validate(args.assets)
        for error in errors:
            print(error)
        if not errors:
            print("Generated workstation entry point and asset references are valid")
        return 1 if errors else 0
    except (OSError, UnicodeError, ValueError) as exc:
        print(f"Workstation validation failed: {exc}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
