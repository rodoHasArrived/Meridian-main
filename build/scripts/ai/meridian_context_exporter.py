#!/usr/bin/env python3
"""Export a compact Meridian context snapshot for AI coding sessions."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import xml.etree.ElementTree as ET
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_MARKDOWN_OUTPUT = Path("docs/ai/exports/LLM_CONTEXT.md")
DEFAULT_JSON_OUTPUT = Path("docs/ai/exports/context.json")
EXCLUDED_PARTS = {".git", ".vs", "bin", "obj", "node_modules", "TestResults", "artifacts", "__pycache__"}


@dataclass(frozen=True)
class ProjectInfo:
    name: str
    path: str
    target_frameworks: tuple[str, ...]
    project_references: tuple[str, ...]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Export Meridian AI context as Markdown and JSON.")
    parser.add_argument("--root", type=Path, default=REPO_ROOT, help="Repository root.")
    parser.add_argument("--markdown-output", type=Path, default=DEFAULT_MARKDOWN_OUTPUT, help="Markdown output path.")
    parser.add_argument("--json-output", type=Path, default=DEFAULT_JSON_OUTPUT, help="JSON output path.")
    parser.add_argument("--summary", action="store_true", help="Print a compact summary.")
    parser.add_argument("--check", action="store_true", help="Validate that the existing JSON export is current without writing files.")
    return parser.parse_args()


def rel(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def should_skip(path: Path) -> bool:
    return any(part in EXCLUDED_PARTS for part in path.parts)


def read_first_heading(path: Path) -> str | None:
    try:
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.startswith("# "):
                return line[2:].strip()
    except UnicodeDecodeError:
        return None
    return None


def discover_projects(root: Path) -> list[ProjectInfo]:
    projects: list[ProjectInfo] = []
    for project_path in sorted(root.glob("src/**/*.csproj")) + sorted(root.glob("tests/**/*.csproj")):
        if should_skip(project_path):
            continue
        try:
            xml_root = ET.parse(project_path).getroot()
        except ET.ParseError:
            continue
        frameworks: list[str] = []
        for tag in ("TargetFramework", "TargetFrameworks"):
            for node in xml_root.findall(f".//{tag}"):
                if node.text:
                    frameworks.extend(item.strip() for item in node.text.split(";") if item.strip())
        references = tuple(
            sorted(
                rel((project_path.parent / node.attrib["Include"]).resolve(), root)
                for node in xml_root.findall(".//ProjectReference")
                if "Include" in node.attrib
            )
        )
        projects.append(ProjectInfo(project_path.stem, rel(project_path, root), tuple(sorted(set(frameworks))), references))
    return projects


def collect_docs(root: Path, pattern: str) -> list[dict[str, str]]:
    docs: list[dict[str, str]] = []
    for path in sorted(root.glob(pattern)):
        if path.is_file() and not should_skip(path):
            docs.append({"path": rel(path, root), "title": read_first_heading(path) or path.stem})
    return docs


def source_files(root: Path) -> list[Path]:
    patterns = (
        "docs/architecture/meridian-*.md",
        "docs/domain/*.md",
        "docs/ai/context/*.md",
        "docs/adr/*.md",
        "src/**/*.csproj",
        "tests/**/*.csproj",
    )
    files: list[Path] = []
    for pattern in patterns:
        for path in root.glob(pattern):
            if path.is_file() and not should_skip(path):
                files.append(path)
    return sorted(set(files))


def digest_sources(root: Path, files: list[Path]) -> str:
    digest = hashlib.sha256()
    for path in files:
        relative = rel(path, root)
        digest.update(relative.encode("utf-8"))
        digest.update(b"\0")
        digest.update(path.read_bytes())
        digest.update(b"\0")
    return digest.hexdigest()


def build_payload(root: Path) -> dict[str, Any]:
    projects = discover_projects(root)
    sources = source_files(root)
    return {
        "generatedAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "repository": root.name,
        "sourceDigest": digest_sources(root, sources),
        "sourceFiles": [rel(path, root) for path in sources],
        "mdif": {
            "constitution": collect_docs(root, "docs/architecture/meridian-*.md"),
            "domainDictionary": collect_docs(root, "docs/domain/*.md"),
            "contextPacks": collect_docs(root, "docs/ai/context/*.md"),
            "decisionRecords": collect_docs(root, "docs/adr/*.md"),
        },
        "projects": [asdict(project) for project in projects],
        "counts": {
            "projects": len(projects),
            "constitutionDocs": len(collect_docs(root, "docs/architecture/meridian-*.md")),
            "domainDocs": len(collect_docs(root, "docs/domain/*.md")),
            "contextPacks": len(collect_docs(root, "docs/ai/context/*.md")),
        },
    }


def render_markdown(payload: dict[str, Any]) -> str:
    lines = [
        "# Meridian LLM Context",
        "",
        "**Status:** generated",
        "**Owner:** core-team",
        f"**Reviewed:** {payload['generatedAtUtc'][:10]}",
        "",
        f"Generated at: `{payload['generatedAtUtc']}`",
        f"Source digest: `{payload['sourceDigest']}`",
        "",
        "## MDIF Sources",
        "",
    ]
    for label, key in (
        ("Project Constitution", "constitution"),
        ("Domain Dictionary", "domainDictionary"),
        ("AI Context Packs", "contextPacks"),
        ("Decision Records", "decisionRecords"),
    ):
        lines.extend([f"### {label}", ""])
        for doc in payload["mdif"][key]:
            lines.append(f"- [{doc['title']}]({doc['path']})")
        lines.append("")

    lines.extend(["## Projects", ""])
    for project in payload["projects"]:
        frameworks = ", ".join(project["target_frameworks"]) or "unspecified"
        lines.append(f"- `{project['name']}` — `{project['path']}` ({frameworks})")
    lines.append("")
    return "\n".join(lines)


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def _load_existing_json(path: Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ValueError(f"Export not found: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ValueError(f"Export is not valid JSON: {path}: {exc}") from exc


def _print_summary(payload: dict[str, Any], verb: str) -> None:
    counts = payload["counts"]
    print(
        f"{verb} "
        f"{counts['projects']} projects, "
        f"{counts['constitutionDocs']} constitution docs, "
        f"{counts['domainDocs']} domain docs, "
        f"{counts['contextPacks']} context packs, "
        f"digest {payload['sourceDigest'][:12]}"
    )


def main() -> int:
    args = parse_args()
    root = args.root.resolve()
    payload = build_payload(root)
    json_output = root / args.json_output
    markdown_output = root / args.markdown_output

    if args.check:
        try:
            existing = _load_existing_json(json_output)
        except ValueError as exc:
            print(str(exc), file=sys.stderr)
            return 1
        if existing.get("sourceDigest") != payload["sourceDigest"]:
            print(
                "MDIF context export is stale: "
                f"expected digest {payload['sourceDigest']} but found {existing.get('sourceDigest', '<missing>')}. "
                "Run build/scripts/ai/meridian_context_exporter.py --summary.",
                file=sys.stderr,
            )
            return 1
        if args.summary:
            _print_summary(payload, "current")
        return 0

    write_text(json_output, json.dumps(payload, indent=2) + "\n")
    write_text(markdown_output, render_markdown(payload))
    if args.summary:
        _print_summary(payload, "exported")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
