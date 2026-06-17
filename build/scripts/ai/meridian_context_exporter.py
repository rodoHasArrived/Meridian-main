#!/usr/bin/env python3
"""Export a compact Meridian context snapshot for AI coding sessions."""

from __future__ import annotations

import argparse
import json
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
    return parser.parse_args()


def rel(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def should_skip(path: Path) -> bool:
    return any(part in EXCLUDED_PARTS for part in path.parts)


def iter_files(start: Path, suffix: str) -> list[Path]:
    if not start.exists():
        return []

    results: list[Path] = []
    stack = [start]
    while stack:
        current = stack.pop()
        if should_skip(current):
            continue
        for child in current.iterdir():
            if child.is_dir():
                if not should_skip(child):
                    stack.append(child)
            elif child.name.endswith(suffix):
                results.append(child)
    return sorted(results)


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
    for project_path in iter_files(root / "src", ".csproj") + iter_files(root / "tests", ".csproj"):
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


def collect_docs(root: Path, pattern: str, excluded_names: set[str] | None = None) -> list[dict[str, str]]:
    excluded_names = excluded_names or set()
    docs: list[dict[str, str]] = []
    for path in sorted(root.glob(pattern)):
        if path.is_file() and path.name not in excluded_names and not should_skip(path):
            docs.append({"path": rel(path, root), "title": read_first_heading(path) or path.stem})
    return docs


def markdown_link_path(repo_relative_path: str) -> str:
    if repo_relative_path.startswith("docs/"):
        return "../../" + repo_relative_path.removeprefix("docs/")
    return "../../../" + repo_relative_path


def build_payload(root: Path) -> dict[str, Any]:
    projects = discover_projects(root)
    constitution = collect_docs(root, "docs/architecture/meridian-*.md")
    domain_dictionary = collect_docs(root, "docs/domain/*.md", excluded_names={"README.md"})
    context_packs = collect_docs(root, "docs/ai/context/*.md", excluded_names={"README.md"})
    decision_records = collect_docs(root, "docs/adr/*.md")
    return {
        "generatedAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "repository": root.name,
        "activeScope": {
            "productThesis": "Meridian proves operational records from source evidence to governed output.",
            "proofChain": [
                "source evidence",
                "normalized record",
                "validation",
                "reconciliation",
                "exception resolution",
                "journal / ledger impact",
                "capital account impact",
                "close package",
                "report line",
                "delivery record",
                "audit evidence",
            ],
            "workspaceNavigation": ["Trading", "Portfolio", "Accounting", "Reporting", "Strategy", "Data", "Settings"],
            "activeSurfaces": [
                "src/Meridian.Wpf/",
                "src/Meridian.Ui/dashboard/",
                "src/Meridian.Ui/wwwroot/workstation/",
            ],
            "sharedSeams": [
                "src/Meridian.Ui.Services/",
                "src/Meridian.Ui.Shared/",
                "src/Meridian.Contracts/",
            ],
            "deferredByDefault": [
                "mobile applications",
                "full live trading",
                "full payment execution",
                "broad client portal",
                "no-code workflow builder",
                "forecasting or enterprise-risk surfaces detached from operational records",
            ],
        },
        "recommendedLoadOrder": [
            "docs/ai/navigation/README.md",
            "docs/ai/generated/repo-navigation.md",
            "docs/product/meridian-design-document.md",
            "docs/architecture/meridian-development-intelligence-framework.md",
            "docs/architecture/meridian-vision.md",
            "docs/architecture/meridian-domain-model.md",
            "docs/ai/context/*.md relevant to the task",
            "docs/domain/*.md relevant to the task",
        ],
        "mdif": {
            "constitution": constitution,
            "domainDictionary": domain_dictionary,
            "contextPacks": context_packs,
            "decisionRecords": decision_records,
        },
        "projects": [asdict(project) for project in projects],
        "counts": {
            "projects": len(projects),
            "constitutionDocs": len(constitution),
            "domainDocs": len(domain_dictionary),
            "contextPacks": len(context_packs),
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
        "",
        "## Active Scope",
        "",
        payload["activeScope"]["productThesis"],
        "",
        "### Proof Chain",
        "",
    ]
    for step in payload["activeScope"]["proofChain"]:
        lines.append(f"- {step}")
    lines.extend(
        [
            "",
            "### Active Operator Workspaces",
            "",
            ", ".join(f"`{workspace}`" for workspace in payload["activeScope"]["workspaceNavigation"]),
            "",
            "### Shared Seams",
            "",
        ]
    )
    for seam in payload["activeScope"]["sharedSeams"]:
        lines.append(f"- `{seam}`")
    lines.extend(
        [
            "",
            "### Deferred By Default",
            "",
        ]
    )
    for item in payload["activeScope"]["deferredByDefault"]:
        lines.append(f"- {item}")
    lines.extend(
        [
            "",
            "## Recommended Load Order",
            "",
        ]
    )
    for source in payload["recommendedLoadOrder"]:
        lines.append(f"- `{source}`")
    lines.extend(
        [
            "",
            "## MDIF Sources",
            "",
        ]
    )
    for label, key in (
        ("Project Constitution", "constitution"),
        ("Domain Dictionary", "domainDictionary"),
        ("AI Context Packs", "contextPacks"),
        ("Decision Records", "decisionRecords"),
    ):
        lines.extend([f"### {label}", ""])
        for doc in payload["mdif"][key]:
            lines.append(f"- [{doc['title']}]({markdown_link_path(doc['path'])})")
        lines.append("")

    lines.extend(["## Projects", ""])
    for project in payload["projects"]:
        frameworks = ", ".join(project["target_frameworks"]) or "unspecified"
        lines.append(f"- `{project['name']}` - `{project['path']}` ({frameworks})")
    lines.append("")
    return "\n".join(lines)


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def main() -> int:
    args = parse_args()
    root = args.root.resolve()
    payload = build_payload(root)
    write_text(root / args.json_output, json.dumps(payload, indent=2) + "\n")
    write_text(root / args.markdown_output, render_markdown(payload))
    if args.summary:
        counts = payload["counts"]
        print(
            "exported "
            f"{counts['projects']} projects, "
            f"{counts['constitutionDocs']} constitution docs, "
            f"{counts['domainDocs']} domain docs, "
            f"{counts['contextPacks']} context packs"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
