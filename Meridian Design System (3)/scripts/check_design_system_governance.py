#!/usr/bin/env python3
"""Governance checks for the standalone Meridian design-system package."""

from __future__ import annotations

import argparse
import fnmatch
import json
import re
import sys
from dataclasses import dataclass
from html.parser import HTMLParser
from pathlib import Path
from typing import Iterable


TEXT_SUFFIXES = {".css", ".html", ".md", ".jsx"}
FORBIDDEN_VISIBLE_WORKSPACE_NAMES = ("Overview", "Data Operations", "Data Ops", "Governance")
HEX_PATTERN = re.compile(r"#[0-9a-fA-F]{3,8}\b")
RADIUS_PATTERN = re.compile(r"border-radius\s*:\s*([0-9.]+)(px|rem)", re.IGNORECASE)
GRADIENT_PATTERN = re.compile(r"\b(?:linear|radial)-gradient\(", re.IGNORECASE)
TAG_ATTR_PATTERN = re.compile(r"(?:href|src)=\"([^\"]+)\"", re.IGNORECASE)
TD_PATTERN = re.compile(r"<td(?P<attrs>[^>]*)>(?P<body>.*?)</td>", re.IGNORECASE | re.DOTALL)
NUMERIC_TEXT_PATTERN = re.compile(r"(?:[$−-]?\d[\d,.]*%?|[A-Z]{1,6}\d{2,})")


@dataclass(frozen=True)
class Violation:
    code: str
    path: str
    line: int
    detail: str

    def format(self) -> str:
        return f"{self.code}: {self.path}:{self.line}: {self.detail}"


class TextExtractor(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.parts: list[str] = []

    def handle_data(self, data: str) -> None:
        self.parts.append(data)

    @property
    def text(self) -> str:
        return " ".join(self.parts)


def load_baseline(root: Path) -> dict[str, list[str]]:
    baseline_path = root / "governance-baseline.json"
    if not baseline_path.exists():
        return {}
    with baseline_path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    return {key: list(value) for key, value in data.items()}


def rel_path(root: Path, path: Path) -> str:
    return path.relative_to(root).as_posix()


def is_allowed(rel: str, baseline: dict[str, list[str]], key: str) -> bool:
    return any(fnmatch.fnmatch(rel, pattern) for pattern in baseline.get(key, []))


def iter_text_files(root: Path) -> Iterable[Path]:
    for path in root.rglob("*"):
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
    violations: list[Violation] = []
    for match in TAG_ATTR_PATTERN.finditer(text):
        link = match.group(1)
        if is_external_or_dynamic(link):
            continue
        clean = link.split("#", 1)[0].split("?", 1)[0]
        if not clean:
            continue
        target = (path.parent / Path(*clean.split("/"))).resolve()
        if not target.exists():
            violations.append(
                Violation("local-link", rel_path(root, path), line_number(text, match.start()), f"Missing target `{link}`")
            )
    return violations


def check_raw_hex(root: Path, path: Path, text: str, baseline: dict[str, list[str]]) -> list[Violation]:
    rel = rel_path(root, path)
    if is_allowed(rel, baseline, "raw_hex_allowed"):
        return []
    return [
        Violation("raw-hex", rel, line_number(text, match.start()), f"Use a token instead of `{match.group(0)}`")
        for match in HEX_PATTERN.finditer(text)
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
        if px > 10 and "50%" not in line and "999" not in line:
            violations.append(Violation("large-radius", rel, line_number(text, match.start()), f"Radius {match.group(0)} exceeds 10px"))
    return violations


def check_gradients(root: Path, path: Path, text: str, baseline: dict[str, list[str]]) -> list[Violation]:
    rel = rel_path(root, path)
    if is_allowed(rel, baseline, "gradient_allowed"):
        return []
    return [
        Violation("decorative-gradient", rel, line_number(text, match.start()), "Gradient outside approved token, brand, or chart contexts")
        for match in GRADIENT_PATTERN.finditer(text)
    ]


def normalized_visible_label(value: str) -> str:
    return re.sub(r"\s+", " ", value).strip().strip("`*_#-[]()")


def check_workspace_names(root: Path, path: Path, text: str, baseline: dict[str, list[str]]) -> list[Violation]:
    rel = rel_path(root, path)
    if is_allowed(rel, baseline, "legacy_workspace_mentions_allowed"):
        return []
    violations: list[Violation] = []
    if path.suffix.lower() == ".html":
        parser = TextExtractor()
        parser.feed(text)
        labels = [(normalized_visible_label(part), text.find(part)) for part in parser.parts]
    else:
        labels = [
            (normalized_visible_label(line), text.find(line))
            for line in text.splitlines()
        ]
    for label, index in labels:
        if label in FORBIDDEN_VISIBLE_WORKSPACE_NAMES:
            violations.append(
                Violation("legacy-workspace", rel, line_number(text, max(index, 0)), f"`{label}` is not a visible root workspace")
            )
    return violations


def strip_tags(value: str) -> str:
    return re.sub(r"<[^>]+>", "", value).strip()


def check_numeric_table_cells(root: Path, path: Path, text: str, baseline: dict[str, list[str]]) -> list[Violation]:
    rel = rel_path(root, path)
    if is_allowed(rel, baseline, "numeric_table_allowed"):
        return []
    violations: list[Violation] = []
    for match in TD_PATTERN.finditer(text):
        attrs = match.group("attrs")
        body = strip_tags(match.group("body"))
        if not body or not NUMERIC_TEXT_PATTERN.search(body):
            continue
        class_match = re.search(r"class=\"([^\"]+)\"", attrs)
        classes = class_match.group(1).split() if class_match else []
        if not ({"r", "mono"} & set(classes)):
            violations.append(
                Violation("numeric-table-cell", rel, line_number(text, match.start()), f"Numeric table cell `{body}` needs `r` or `mono` class")
            )
    return violations


def run_checks(root: Path, baseline: dict[str, list[str]] | None = None) -> list[Violation]:
    baseline = baseline or load_baseline(root)
    violations: list[Violation] = []
    for path in iter_text_files(root):
        text = path.read_text(encoding="utf-8")
        if path.suffix.lower() == ".html":
            violations.extend(check_local_links(root, path, text))
        violations.extend(check_raw_hex(root, path, text, baseline))
        violations.extend(check_large_radius(root, path, text, baseline))
        violations.extend(check_gradients(root, path, text, baseline))
        violations.extend(check_workspace_names(root, path, text, baseline))
        if path.suffix.lower() == ".html":
            violations.extend(check_numeric_table_cells(root, path, text, baseline))
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
