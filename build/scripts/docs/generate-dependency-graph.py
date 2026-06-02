#!/usr/bin/env python3
"""
Project Dependency Graph Generator

Analyzes .NET project files (.csproj/.fsproj) to extract dependencies and generates
a visual dependency graph in multiple formats (DOT, Mermaid, JSON).

Features:
- Extracts project references
- Extracts NuGet package dependencies
- Detects circular dependencies
- Generates visual dependency graphs
- Identifies unused projects
- Calculates project complexity metrics

Usage:
    python3 generate-dependency-graph.py --output deps.md
    python3 generate-dependency-graph.py --format dot --output deps.dot
    python3 generate-dependency-graph.py --format json --output deps.json
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import xml.etree.ElementTree as ET
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path, PureWindowsPath
from typing import Optional


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

EXCLUDE_DIRS: frozenset[str] = frozenset({
    '.git', 'archive', 'node_modules', 'bin', 'obj', '__pycache__', '.vs', 'packages', 'TestResults'
})
PROJECT_EXTENSIONS: frozenset[str] = frozenset({'.csproj', '.fsproj'})
IGNORED_PROJECT_NAME_MARKERS: tuple[str, ...] = ('_wpftmp',)


# ---------------------------------------------------------------------------
# Data Models
# ---------------------------------------------------------------------------

@dataclass
class ProjectDependency:
    """Represents a project reference."""
    name: str
    path: str


@dataclass
class PackageDependency:
    """Represents a NuGet package dependency."""
    name: str
    version: str


@dataclass
class ProjectNode:
    """Represents a project in the dependency graph."""
    name: str
    path: str
    project_refs: list[ProjectDependency] = field(default_factory=list)
    package_refs: list[PackageDependency] = field(default_factory=list)
    referenced_by: list[str] = field(default_factory=list)

    @property
    def is_leaf(self) -> bool:
        """Check if project has no dependencies."""
        return len(self.project_refs) == 0

    @property
    def is_root(self) -> bool:
        """Check if project is not referenced by others."""
        return len(self.referenced_by) == 0

    @property
    def complexity(self) -> int:
        """Calculate project complexity as total dependency count."""
        return len(self.project_refs) + len(self.package_refs)


@dataclass
class DependencyGraph:
    """Complete dependency graph."""
    projects: dict[str, ProjectNode] = field(default_factory=dict)
    circular_deps: list[list[str]] = field(default_factory=list)
    generated_at: str = ""

    def to_dict(self) -> dict:
        """Convert to dictionary for JSON serialization."""
        return {
            'projects': {name: asdict(proj) for name, proj in self.projects.items()},
            'circular_deps': self.circular_deps,
            'generated_at': self.generated_at,
            'statistics': {
                'total_projects': len(self.projects),
                'leaf_projects': sum(1 for p in self.projects.values() if p.is_leaf),
                'root_projects': sum(1 for p in self.projects.values() if p.is_root),
                'circular_dependencies': len(self.circular_deps),
            }
        }


# ---------------------------------------------------------------------------
# Parsing Functions
# ---------------------------------------------------------------------------

def _should_skip(path: Path) -> bool:
    """Check if path should be skipped."""
    return any(part in EXCLUDE_DIRS for part in path.parts)


def _should_skip_project(project_path: Path) -> bool:
    """Check if a project file should be excluded from the graph."""
    return any(marker in project_path.stem for marker in IGNORED_PROJECT_NAME_MARKERS)


def _project_stem(include: str) -> str:
    """Resolve a project name from a ProjectReference path across slash styles."""
    return PureWindowsPath(include.replace('/', '\\')).stem


def _parse_project(project_path: Path, root: Path) -> Optional[ProjectNode]:
    """Parse a project file and extract dependencies."""
    try:
        tree = ET.parse(project_path)
        xml_root = tree.getroot()
    except Exception as e:
        print(f"Warning: Could not parse {project_path}: {e}", file=sys.stderr)
        return None

    project_name = project_path.stem
    try:
        rel_path = str(project_path.relative_to(root))
    except ValueError:
        rel_path = str(project_path)

    node = ProjectNode(name=project_name, path=rel_path)

    # Extract ProjectReference elements
    for ref in xml_root.findall('.//ProjectReference'):
        include = ref.get('Include')
        if include and '$(' not in include:
            # Extract project name from path
            ref_project = _project_stem(include)
            if any(marker in ref_project for marker in IGNORED_PROJECT_NAME_MARKERS):
                continue
            node.project_refs.append(ProjectDependency(
                name=ref_project,
                path=include
            ))

    # Extract PackageReference elements
    for pkg in xml_root.findall('.//PackageReference'):
        name = pkg.get('Include')
        version = pkg.get('Version', '')
        if name:
            node.package_refs.append(PackageDependency(
                name=name,
                version=version
            ))

    return node


def _detect_circular_deps(graph: dict[str, ProjectNode]) -> list[list[str]]:
    """Detect circular dependencies using DFS."""
    circular = []
    visited = set()
    rec_stack = set()
    path = []

    def dfs(node_name: str):
        if node_name in rec_stack:
            # Found a cycle
            cycle_start = path.index(node_name)
            cycle = path[cycle_start:] + [node_name]
            circular.append(cycle)
            return

        if node_name in visited:
            return

        visited.add(node_name)
        rec_stack.add(node_name)
        path.append(node_name)

        node = graph.get(node_name)
        if node:
            for dep in node.project_refs:
                dfs(dep.name)

        path.pop()
        rec_stack.remove(node_name)

    for project_name in graph:
        if project_name not in visited:
            dfs(project_name)

    return circular


def build_dependency_graph(root: Path) -> DependencyGraph:
    """Build dependency graph from all supported project files in repository."""
    graph = DependencyGraph(
        generated_at=datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
    )

    # Find all supported project files while pruning ignored directories before traversal.
    for current_root, dirnames, filenames in os.walk(root):
        dirnames[:] = [name for name in dirnames if name not in EXCLUDE_DIRS]
        current_path = Path(current_root)
        if _should_skip(current_path):
            continue

        for filename in filenames:
            project_path = current_path / filename
            if project_path.suffix not in PROJECT_EXTENSIONS or _should_skip_project(project_path):
                continue

            node = _parse_project(project_path, root)
            if node:
                graph.projects[node.name] = node

    # Build reverse reference map
    for node in graph.projects.values():
        for dep in node.project_refs:
            if dep.name in graph.projects:
                graph.projects[dep.name].referenced_by.append(node.name)

    # Detect circular dependencies
    graph.circular_deps = _detect_circular_deps(graph.projects)

    return graph


def categorize_project(node: ProjectNode) -> str:
    """Classify a project into a high-level repo family."""
    path = Path(node.path)
    top_level = path.parts[0] if path.parts else ""

    if top_level == "tests":
        return "Tests"
    if top_level == "benchmarks":
        return "Benchmarks"
    if top_level == "build":
        return "Build Tools"

    if node.name in {"Meridian", "Meridian.Mcp", "Meridian.Wpf"}:
        return "Hosts"
    if node.name.startswith("Meridian.Ui."):
        return "UI Shared"
    if node.name in {
        "Meridian.Contracts",
        "Meridian.Core",
        "Meridian.Domain",
        "Meridian.Execution.Sdk",
        "Meridian.IbApi.SmokeStub",
        "Meridian.Ledger",
        "Meridian.ProviderSdk",
    } or node.name.startswith("Meridian.FSharp"):
        return "Foundation"

    return "Runtime"


# ---------------------------------------------------------------------------
# Output Generation
# ---------------------------------------------------------------------------

def generate_dot(graph: DependencyGraph) -> str:
    """Generate DOT format for Graphviz."""
    lines = []
    lines.append('digraph Dependencies {')
    lines.append('    rankdir=LR;')
    lines.append('    node [shape=box, style=rounded];')
    lines.append('')

    # Add nodes
    for name, node in sorted(graph.projects.items()):
        color = 'lightblue' if node.is_root else ('lightgreen' if node.is_leaf else 'white')
        lines.append(f'    "{name}" [fillcolor={color}, style=filled];')

    lines.append('')

    # Add edges
    for name, node in sorted(graph.projects.items()):
        for dep in node.project_refs:
            lines.append(f'    "{name}" -> "{dep.name}";')

    lines.append('}')
    return '\n'.join(lines)


def _category_matches(node: ProjectNode, categories: Optional[set[str]]) -> bool:
    if categories is None:
        return True
    return categorize_project(node) in categories


def generate_mermaid(graph: DependencyGraph, categories: Optional[set[str]] = None) -> str:
    """Generate Mermaid diagram syntax."""
    lines = []
    lines.append('```mermaid')
    lines.append('graph LR')

    nodes = [
        node for name, node in sorted(graph.projects.items())
        if _category_matches(node, categories)
    ]
    included_names = {node.name for node in nodes}

    for node in nodes:
        safe_name = node.name.replace('-', '_').replace('.', '_')
        lines.append(f'    {safe_name}[{node.name}]')

    for name, node in sorted(graph.projects.items()):
        if name not in included_names:
            continue
        safe_name = name.replace('-', '_').replace('.', '_')
        for dep in node.project_refs:
            if dep.name not in included_names:
                continue
            safe_dep = dep.name.replace('-', '_').replace('.', '_')
            lines.append(f'    {safe_name}[{name}] --> {safe_dep}[{dep.name}]')

    lines.append('```')
    return '\n'.join(lines)


def generate_family_overview(graph: DependencyGraph) -> str:
    """Generate a compact family-level overview diagram."""
    grouped: dict[str, list[str]] = {}
    for node in sorted(graph.projects.values(), key=lambda item: (categorize_project(item), item.name)):
        grouped.setdefault(categorize_project(node), []).append(node.name)

    def label(category: str) -> str:
        names = grouped.get(category, [])
        detail = "<br/>".join(names) if names else "None"
        return f'{category}<br/><br/>{detail}'

    lines = [
        '```mermaid',
        'flowchart LR',
        f'    BuildTools["{label("Build Tools")}"]',
        f'    Foundation["{label("Foundation")}"]',
        f'    Runtime["{label("Runtime")}"]',
        f'    UIShared["{label("UI Shared")}"]',
        f'    Hosts["{label("Hosts")}"]',
        f'    Benchmarks["{label("Benchmarks")}"]',
        f'    Tests["{label("Tests")}"]',
        '    BuildTools --> Foundation',
        '    Foundation --> Runtime',
        '    Runtime --> UIShared',
        '    UIShared --> Hosts',
        '    Foundation --> Hosts',
        '    Runtime --> Hosts',
        '    Foundation --> Tests',
        '    Runtime --> Tests',
        '    UIShared --> Tests',
        '    Hosts --> Tests',
        '    Runtime --> Benchmarks',
        '    Hosts --> Benchmarks',
        '```',
    ]
    return '\n'.join(lines)


def generate_markdown(graph: DependencyGraph) -> str:  # noqa: C901
    """Generate Markdown dependency report."""
    lines = []

    lines.append('# Project Dependency Graph')
    lines.append('')
    lines.append(f'> Generated: {graph.generated_at}')
    lines.append('')

    # Summary
    stats = graph.to_dict()['statistics']
    category_counts: dict[str, int] = {}
    for node in graph.projects.values():
        category = categorize_project(node)
        category_counts[category] = category_counts.get(category, 0) + 1
    lines.append('## Summary')
    lines.append('')
    lines.append('| Metric | Value |')
    lines.append('|--------|-------|')
    lines.append(f"| Total Projects | {stats['total_projects']} |")
    lines.append(f"| Foundation Projects | {category_counts.get('Foundation', 0)} |")
    lines.append(f"| Runtime Projects | {category_counts.get('Runtime', 0)} |")
    lines.append(f"| UI/Host Projects | {category_counts.get('UI Shared', 0) + category_counts.get('Hosts', 0)} |")
    lines.append(f"| Test Projects | {category_counts.get('Tests', 0)} |")
    lines.append(f"| Build/Benchmark Projects | {category_counts.get('Build Tools', 0) + category_counts.get('Benchmarks', 0)} |")
    lines.append(f"| Root Projects | {stats['root_projects']} |")
    lines.append(f"| Leaf Projects | {stats['leaf_projects']} |")
    lines.append(f"| Circular Dependencies | {stats['circular_dependencies']} |")
    lines.append('')

    lines.append('> This file is auto-generated. Do not edit manually.')
    lines.append('')

    lines.append('## Project Family Overview')
    lines.append('')
    lines.append(generate_family_overview(graph))
    lines.append('')

    lines.append('## Runtime Project Graph')
    lines.append('')
    lines.append('Shows the current source/runtime dependency shape without tests, benchmarks, or build tooling noise.')
    lines.append('')
    lines.append(generate_mermaid(graph, categories={'Foundation', 'Runtime', 'UI Shared', 'Hosts'}))
    lines.append('')

    # Circular dependencies
    if graph.circular_deps:
        lines.append('## ⚠️ Circular Dependencies')
        lines.append('')
        lines.append('The following circular dependencies were detected:')
        lines.append('')
        for cycle in graph.circular_deps:
            cycle_str = ' → '.join(cycle)
            lines.append(f'- {cycle_str}')
        lines.append('')

    # Root projects (entry points)
    root_projects = [p for p in graph.projects.values() if p.is_root]
    if root_projects:
        lines.append('## Entry Point Projects')
        lines.append('')
        lines.append('These projects are not referenced by other projects:')
        lines.append('')
        for proj in sorted(root_projects, key=lambda p: p.name):
            lines.append(f'- **{proj.name}**')
            if proj.project_refs:
                lines.append(f'  - Dependencies: {len(proj.project_refs)}')
            if proj.package_refs:
                lines.append(f'  - NuGet Packages: {len(proj.package_refs)}')
        lines.append('')

    # Complexity ranking
    complex_projects = sorted(graph.projects.values(), key=lambda p: -p.complexity)[:10]
    if complex_projects:
        lines.append('## Most Complex Projects')
        lines.append('')
        lines.append('Projects with the most dependencies:')
        lines.append('')
        lines.append('| Project | Project Deps | Package Deps | Total |')
        lines.append('|---------|--------------|--------------|-------|')
        for proj in complex_projects:
            lines.append(
                f'| {proj.name} | {len(proj.project_refs)} | '
                f'{len(proj.package_refs)} | {proj.complexity} |'
            )
        lines.append('')

    # Dependency graph (Mermaid)
    lines.append('## Full Dependency Graph')
    lines.append('')
    lines.append(generate_mermaid(graph))
    lines.append('')

    # Project details
    lines.append('## Project Details')
    lines.append('')
    for name, proj in sorted(graph.projects.items()):
        lines.append(f'### {name}')
        lines.append('')
        lines.append(f'**Path:** `{proj.path}`')
        lines.append('')

        if proj.project_refs:
            lines.append('**Project References:**')
            for dep in sorted(proj.project_refs, key=lambda d: d.name):
                lines.append(f'- {dep.name}')
            lines.append('')

        if proj.referenced_by:
            lines.append('**Referenced By:**')
            for ref in sorted(proj.referenced_by):
                lines.append(f'- {ref}')
            lines.append('')

        if proj.package_refs:
            lines.append(f'**NuGet Packages ({len(proj.package_refs)}):**')
            for pkg in sorted(proj.package_refs, key=lambda p: p.name)[:10]:
                version_str = f' ({pkg.version})' if pkg.version else ''
                lines.append(f'- {pkg.name}{version_str}')
            if len(proj.package_refs) > 10:
                lines.append(f'- ... and {len(proj.package_refs) - 10} more')
            lines.append('')

    lines.append('---')
    lines.append('')
    lines.append('*This report is auto-generated. Run `python3 build/scripts/docs/generate-dependency-graph.py` to regenerate.*')  # noqa: E501
    lines.append('')

    return '\n'.join(lines)


def generate_summary(graph: DependencyGraph) -> str:
    """Generate concise summary."""
    stats = graph.to_dict()['statistics']
    circular_warning = f" ⚠️ {stats['circular_dependencies']} circular!" if stats['circular_dependencies'] > 0 else ""

    return (
        f"### Dependency Graph\n\n"
        f"- **Projects**: {stats['total_projects']}\n"
        f"- **Root Projects**: {stats['root_projects']}\n"
        f"- **Leaf Projects**: {stats['leaf_projects']}\n"
        f"- **Circular Dependencies**: {stats['circular_dependencies']}{circular_warning}\n"
    )


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main(argv: Optional[list[str]] = None) -> int:  # noqa: C901
    """Entry point."""
    parser = argparse.ArgumentParser(
        description='Generate project dependency graph'
    )
    parser.add_argument(
        '--root', '-r',
        type=Path,
        default=Path('.'),
        help='Repository root directory (default: current directory)'
    )
    parser.add_argument(
        '--output', '-o',
        type=Path,
        help='Output file'
    )
    parser.add_argument(
        '--format', '-f',
        choices=['markdown', 'dot', 'mermaid', 'json'],
        default='markdown',
        help='Output format (default: markdown)'
    )
    parser.add_argument(
        '--summary', '-s',
        action='store_true',
        help='Print summary to stdout'
    )

    args = parser.parse_args(argv)

    root = args.root.resolve()
    if not root.is_dir():
        print(f"Error: root directory does not exist: {root}", file=sys.stderr)
        return 1

    try:
        print("Building dependency graph...", file=sys.stderr)
        graph = build_dependency_graph(root)
        print(f"Found {len(graph.projects)} projects", file=sys.stderr)

    except Exception as exc:
        print(f"Error building graph: {exc}", file=sys.stderr)
        return 1

    # Generate output
    if args.format == 'markdown':
        content = generate_markdown(graph)
    elif args.format == 'dot':
        content = generate_dot(graph)
    elif args.format == 'mermaid':
        content = generate_mermaid(graph)
    elif args.format == 'json':
        content = json.dumps(graph.to_dict(), indent=2)

    # Write output
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(content, encoding='utf-8')
        print(f"Dependency graph written to {args.output}")
    else:
        print(content)

    # Print summary
    if args.summary:
        print(generate_summary(graph))

    # Warn about circular dependencies
    if graph.circular_deps:
        print(f"\n⚠️  Warning: {len(graph.circular_deps)} circular dependency chains detected!", file=sys.stderr)
        return 1

    return 0


if __name__ == '__main__':
    sys.exit(main())
