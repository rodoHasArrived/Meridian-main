#!/usr/bin/env python3
"""Detect catalog drift across Meridian AI assistant assets.

Usage:
    python3 build/scripts/docs/check-ai-inventory.py --summary
    python3 build/scripts/docs/check-ai-inventory.py --output docs/status/ai-inventory-report.md
    python3 build/scripts/docs/check-ai-inventory.py --json-output docs/status/ai-inventory-report.json
"""

from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Iterable, Sequence


REPO_ROOT = Path(__file__).resolve().parents[3]
STABLE_GENERATED_AT_UTC = "1970-01-01T00:00:00+00:00"
REPOSITORY_DISPLAY_NAME = "Meridian-main"

DOC_AI_README = "docs/ai/README.md"
AI_CONTRACT = "docs/ai/assistant-workflow-contract.md"
AGENTS_README = "docs/ai/agents/README.md"
SKILLS_README = "docs/ai/skills/README.md"
PROMPTS_README = "docs/ai/prompts/README.md"
INSTRUCTIONS_README = "docs/ai/instructions/README.md"
PROMPT_DOCS_README = "docs/prompts/README.md"
CODEX_GUIDE = "docs/ai/codex/README.md"
CODEX_SKILLS_README = ".codex/skills/README.md"
GITHUB_PROMPTS_README = ".github/prompts/README.md"
COPILOT_GUIDE = "docs/ai/copilot/instructions.md"
ROOT_ASSISTANT_GUIDES = ("CLAUDE.md", COPILOT_GUIDE)
UI_PLATFORM_POLICY_FILES = (
    AI_CONTRACT,
    "CLAUDE.md",
    "AGENTS.md",
    ".github/copilot-instructions.md",
    ".codex/skills/_shared/project-context.md",
    ".claude/skills/_shared/project-context.md",
    ".agents/skills/_shared/project-context.md",
    COPILOT_GUIDE,
)
UI_PLATFORM_POLICY_MARKERS = (
    "No mobile development lane",
    (
        "do not create mobile applications, mobile-specific product surfaces, native iOS/Android clients, "
        "MAUI clients, React Native clients, Flutter clients, or mobile-first workflows"
    ),
)
ACTIVE_OPERATOR_SURFACE_EXTRA_FILES = (
    ".codex/environments/README.md",
)
STALE_OPERATOR_SURFACE_LANGUAGE = (
    re.compile(r"\bretained\s+WPF\b", re.IGNORECASE),
    re.compile(r"\bretained\s+desktop\s+(?:support|tests?)\b", re.IGNORECASE),
    re.compile(r"`src/Meridian\.Wpf/`\s+as\s+retained\s+support", re.IGNORECASE),
)
CURRENT_REPOSITORY_URL = "https://github.com/rodoHasArrived/Meridian-main"
LEGACY_CANONICAL_LINK_PREFIXES = (
    "https://github.com/rodoHasArrived/Meridian/blob/main/",
    "https://github.com/rodoHasArrived/Meridian/tree/main/",
)

AI_WORKFLOW_FILES = (
    ".github/workflows/documentation.yml",
    ".github/workflows/prompt-generation.yml",
    ".github/workflows/reusable-ai-analysis.yml",
    ".github/workflows/skill-evals.yml",
)
AI_WORKFLOW_REFERENCE_FILES = (
    AI_CONTRACT,
    PROMPTS_README,
    GITHUB_PROMPTS_README,
    COPILOT_GUIDE,
    "docs/ai/copilot/ai-sync-workflow.md",
    "docs/prompts/automation-prompts.md",
)
WORKFLOW_REFERENCE_PATTERN = re.compile(
    r"(?:\.\./)*\.github/workflows/(?P<workflow>[A-Za-z0-9_.-]+\.ya?ml)"
)
OPTIONAL_ASSISTANT_SURFACE_PATTERNS = (
    ("cursor", (".cursorrules", ".cursor/**/*.md", ".cursor/**/*.mdc")),
    ("windsurf", (".windsurfrules", ".windsurf/**/*.md", ".windsurf/**/*.mdc")),
    (
        "continue",
        (
            ".continue/**/*.json",
            ".continue/**/*.yaml",
            ".continue/**/*.yml",
            ".continue/**/*.md",
        ),
    ),
    ("cline", (".clinerules", ".cline/**/*.md", ".cline/**/*.mdc")),
    ("roo", (".roomodes", ".roo/**/*.json", ".roo/**/*.md", ".roo/**/*.mdc")),
    (
        "gemini",
        ("GEMINI.md", ".gemini/**/*.json", ".gemini/**/*.toml", ".gemini/**/*.md"),
    ),
)

SYSTEM_CHECKS = (
    (
        "root-assistant-compatibility",
        ("AGENTS.md", "CLAUDE.md"),
        AI_CONTRACT,
        ("Root assistant compatibility", "AGENTS.md", "CLAUDE.md"),
    ),
    (
        "codex",
        (".codex/config.toml", ".codex/skills", ".codex/memory/index.yml"),
        AI_CONTRACT,
        (
            "Codex",
            ".codex/agents/",
            ".codex/AGENTS.md",
            ".codex/skills",
            ".codex/prompts/",
            ".codex/checklists/",
            "OpenAI/Codex",
        ),
    ),
    (
        "agent-skills-compatible-hosts",
        (".agents/skills",),
        AI_CONTRACT,
        ("Agent Skills-compatible hosts", ".agents/skills", "open-agent-skills-v1"),
    ),
    (
        "claude",
        (".claude/settings.json", ".claude/agents", ".claude/skills", ".claude/plugins"),
        AI_CONTRACT,
        ("Claude / Claude Code", ".claude/agents", ".claude/skills", ".claude/plugins/"),
    ),
    (
        "github-copilot",
        (".github/copilot-instructions.md", ".github/agents", ".github/prompts", ".github/instructions"),
        AI_CONTRACT,
        ("GitHub Copilot", ".github/agents", ".github/prompts", ".github/instructions"),
    ),
    (
        "mcp",
        ("src/Meridian.Mcp",),
        AI_CONTRACT,
        ("MCP-compatible clients", "src/Meridian.Mcp"),
    ),
    (
        "ai-automation-workflows",
        AI_WORKFLOW_FILES,
        AI_CONTRACT,
        ("AI automation workflows", "prompt-generation.yml", "skill-evals.yml"),
    ),
    (
        "reusable-prompts",
        (".github/prompts", "docs/prompts"),
        AI_CONTRACT,
        ("Reusable prompt templates", ".github/prompts/", "docs/prompts/", "docs/ai/prompts/README.md"),
    ),
    (
        "shared-ai-docs",
        ("docs/ai",),
        AI_CONTRACT,
        ("Shared AI documentation", "docs/ai/", ".codex/skills/_shared/project-context.md"),
    ),
    (
        "local-ai-maintenance-tools",
        ("scripts/ai", "tools/codex"),
        AI_CONTRACT,
        ("Local AI maintenance tooling", "scripts/ai/", "tools/codex/"),
    ),
)


@dataclass(frozen=True)
class InventoryItem:
    surface: str
    kind: str
    name: str
    path: str
    expected_docs: tuple[str, ...] = ()
    alternate_markers: tuple[str, ...] = ()


@dataclass(frozen=True)
class Finding:
    severity: str
    surface: str
    kind: str
    name: str
    path: str
    expected_doc: str
    message: str


def repo_relative(root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def sorted_files(root: Path, pattern: str) -> list[Path]:
    return sorted(path for path in root.glob(pattern) if path.is_file())


def collect_optional_assistant_surfaces(root: Path) -> list[InventoryItem]:
    items: list[InventoryItem] = []
    seen_paths: set[str] = set()

    for surface, patterns in OPTIONAL_ASSISTANT_SURFACE_PATTERNS:
        for pattern in patterns:
            for path in sorted_files(root, pattern):
                rel_path = repo_relative(root, path)
                if rel_path in seen_paths:
                    continue
                seen_paths.add(rel_path)
                items.append(
                    InventoryItem(
                        surface=surface,
                        kind="optional-assistant-surface",
                        name=path.name,
                        path=rel_path,
                        expected_docs=(DOC_AI_README, AI_CONTRACT),
                    )
                )

    return items


def path_exists(root: Path, rel_path: str) -> bool:
    return (root / rel_path).exists()


def markdown_contains_any(text: str, markers: Iterable[str]) -> bool:
    return any(marker and marker in text for marker in markers)


def normalize_whitespace(text: str) -> str:
    return " ".join(text.split())


def collect_inventory(root: Path) -> list[InventoryItem]:
    items: list[InventoryItem] = []

    for rel_path in ("AGENTS.md", "CLAUDE.md"):
        path = root / rel_path
        if path.is_file():
            items.append(
                InventoryItem(
                    surface="root-assistant-compatibility",
                    kind="entrypoint",
                    name=path.name,
                    path=repo_relative(root, path),
                    expected_docs=(AI_CONTRACT,),
                )
            )

    for rel_path in (".codex/config.toml",):
        path = root / rel_path
        if path.is_file():
            items.append(
                InventoryItem(
                    surface="codex",
                    kind="config",
                    name=path.name,
                    path=repo_relative(root, path),
                    expected_docs=(AI_CONTRACT,),
                )
            )

    for rel_path in (".codex/memory/index.yml",):
        path = root / rel_path
        if path.is_file():
            items.append(
                InventoryItem(
                    surface="codex",
                    kind="memory-index",
                    name=path.name,
                    path=repo_relative(root, path),
                    expected_docs=(AI_CONTRACT, CODEX_GUIDE),
                    alternate_markers=(".codex/memory/",),
                )
            )

    for path in sorted_files(root, ".codex/agents/*.toml"):
        items.append(
            InventoryItem(
                surface="codex",
                kind="agent-profile",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(AI_CONTRACT, CODEX_GUIDE, AGENTS_README),
                alternate_markers=(".codex/agents/",),
            )
        )

    for rel_path in (".codex/AGENTS.md",):
        path = root / rel_path
        if path.is_file():
            items.append(
                InventoryItem(
                    surface="codex",
                    kind="instruction-entrypoint",
                    name=path.name,
                    path=repo_relative(root, path),
                    expected_docs=(AI_CONTRACT, CODEX_GUIDE),
                )
            )

    for path in sorted_files(root, ".codex/environments/*.toml"):
        items.append(
            InventoryItem(
                surface="codex",
                kind="environment-config",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(AI_CONTRACT,),
                alternate_markers=(".codex/environments/",),
            )
        )

    for path in sorted_files(root, ".codex/prompts/*.md"):
        items.append(
            InventoryItem(
                surface="codex",
                kind="prompt-template",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(AI_CONTRACT, CODEX_GUIDE),
                alternate_markers=(".codex/prompts/",),
            )
        )

    for path in sorted_files(root, ".codex/checklists/*.md"):
        items.append(
            InventoryItem(
                surface="codex",
                kind="validation-checklist",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(AI_CONTRACT, CODEX_GUIDE),
                alternate_markers=(".codex/checklists/",),
            )
        )

    for path in sorted_files(root, "tools/codex/*.ps1"):
        items.append(
            InventoryItem(
                surface="codex",
                kind="codex-tool",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(AI_CONTRACT, CODEX_GUIDE),
                alternate_markers=("tools/codex/",),
            )
        )

    for path in sorted_files(root, ".codex/skills/*/SKILL.md"):
        name = path.parent.name
        items.append(
            InventoryItem(
                surface="codex",
                kind="skill",
                name=name,
                path=repo_relative(root, path),
                expected_docs=(SKILLS_README, CODEX_SKILLS_README),
            )
        )

    for path in sorted_files(root, ".codex/skills/*/agents/openai.yaml"):
        name = path.parents[1].name
        items.append(InventoryItem(surface="codex", kind="openai-metadata", name=name, path=repo_relative(root, path)))

    for path in sorted_files(root, ".agents/skills/*/SKILL.md"):
        name = path.parent.name
        items.append(
            InventoryItem(
                surface="agent-skills-compatible-hosts",
                kind="skill",
                name=name,
                path=repo_relative(root, path),
                expected_docs=(SKILLS_README,),
            )
        )

    for path in sorted_files(root, ".agents/skills/*/agents/openai.yaml"):
        name = path.parents[1].name
        items.append(
            InventoryItem(
                surface="agent-skills-compatible-hosts",
                kind="openai-metadata",
                name=name,
                path=repo_relative(root, path),
            )
        )

    for path in sorted_files(root, ".agents/skills/_shared/*.md"):
        items.append(
            InventoryItem(
                surface="agent-skills-compatible-hosts",
                kind="shared-context",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(AI_CONTRACT,),
            )
        )

    for rel_path in (".claude/settings.json", ".claude/settings.local.json"):
        path = root / rel_path
        if path.is_file():
            items.append(
                InventoryItem(
                    surface="claude",
                    kind="config",
                    name=path.name,
                    path=repo_relative(root, path),
                    expected_docs=(AI_CONTRACT,),
                )
            )

    for path in sorted_files(root, ".claude/plugins/*/.github/plugin/plugin.json"):
        plugin_name = path.parents[2].name
        items.append(
            InventoryItem(
                surface="claude",
                kind="plugin-manifest",
                name=plugin_name,
                path=repo_relative(root, path),
                expected_docs=(AI_CONTRACT, SKILLS_README),
                alternate_markers=(f".claude/plugins/{plugin_name}/",),
            )
        )

    for path in sorted_files(root, ".claude/plugins/*/agents/*.md"):
        plugin_name = path.parents[1].name
        items.append(
            InventoryItem(
                surface="claude",
                kind="plugin-agent",
                name=f"{plugin_name}/{path.name}",
                path=repo_relative(root, path),
                expected_docs=(AGENTS_README,),
                alternate_markers=(f".claude/plugins/{plugin_name}/",),
            )
        )

    for path in sorted_files(root, ".claude/plugins/*/skills/*/SKILL.md"):
        plugin_name = path.parents[2].name
        skill_name = path.parent.name
        items.append(
            InventoryItem(
                surface="claude",
                kind="plugin-skill",
                name=f"{plugin_name}/{skill_name}",
                path=repo_relative(root, path),
                expected_docs=(SKILLS_README,),
                alternate_markers=(f".claude/plugins/{plugin_name}/",),
            )
        )

    for path in sorted_files(root, ".claude/skills/*/SKILL.md"):
        name = path.parent.name
        items.append(
            InventoryItem(
                surface="claude",
                kind="skill",
                name=name,
                path=repo_relative(root, path),
                expected_docs=(SKILLS_README,),
            )
        )

    for path in sorted_files(root, ".claude/skills/*/agents/openai.yaml"):
        name = path.parents[1].name
        items.append(
            InventoryItem(surface="claude", kind="openai-metadata", name=name, path=repo_relative(root, path))
        )

    for path in sorted_files(root, ".github/agents/*.md"):
        items.append(
            InventoryItem(
                surface="github-copilot",
                kind="agent",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(AGENTS_README,),
            )
        )

    for rel_path in (".github/copilot-instructions.md",):
        path = root / rel_path
        if path.is_file():
            items.append(
                InventoryItem(
                    surface="github-copilot",
                    kind="instruction-entrypoint",
                    name=path.name,
                    path=repo_relative(root, path),
                    expected_docs=(AI_CONTRACT,),
                )
            )

    for path in sorted_files(root, ".claude/agents/*.md"):
        items.append(
            InventoryItem(
                surface="claude",
                kind="agent",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(AGENTS_README,),
            )
        )

    for path in sorted_files(root, ".github/prompts/*.prompt.yml"):
        items.append(
            InventoryItem(
                surface="github-copilot",
                kind="prompt",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(PROMPTS_README, GITHUB_PROMPTS_README),
            )
        )

    for path in sorted_files(root, "docs/prompts/*.md"):
        if path.name == "README.md":
            continue
        items.append(
            InventoryItem(
                surface="reusable-prompts",
                kind="prompt-documentation",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(AI_CONTRACT, PROMPTS_README, PROMPT_DOCS_README),
            )
        )

    for path in sorted_files(root, ".github/instructions/*.instructions.md"):
        items.append(
            InventoryItem(
                surface="github-copilot",
                kind="path-instruction",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(INSTRUCTIONS_README,),
            )
        )

    for path in sorted_files(root, "scripts/ai/*.sh"):
        items.append(
            InventoryItem(
                surface="local-ai-maintenance-tools",
                kind="maintenance-script",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(DOC_AI_README, AI_CONTRACT),
                alternate_markers=("scripts/ai/",),
            )
        )

    for rel_path in AI_WORKFLOW_FILES:
        path = root / rel_path
        if path.is_file():
            items.append(
                InventoryItem(
                    surface="github-actions",
                    kind="ai-workflow",
                    name=path.name,
                    path=repo_relative(root, path),
                    expected_docs=(AI_CONTRACT,),
                )
            )

    for path in sorted_files(root, ".github/workflows/copilot-*.yml"):
        items.append(
            InventoryItem(
                surface="github-actions",
                kind="copilot-workflow",
                name=path.name,
                path=repo_relative(root, path),
                expected_docs=(AI_CONTRACT,),
                alternate_markers=(".github/workflows/copilot-*",),
            )
        )

    for project in ("src/Meridian.Mcp",):
        for folder, kind in (("Prompts", "mcp-prompt"), ("Resources", "mcp-resource"), ("Tools", "mcp-tool")):
            for path in sorted_files(root, f"{project}/{folder}/*.cs"):
                items.append(InventoryItem(surface="mcp", kind=kind, name=path.name, path=repo_relative(root, path)))

    for path in sorted_files(root, "docs/ai/**/*.md"):
        items.append(InventoryItem(surface="docs-ai", kind="ai-doc", name=path.name, path=repo_relative(root, path)))

    items.extend(collect_optional_assistant_surfaces(root))

    return sorted(items, key=lambda item: (item.surface, item.kind, item.path))


def load_docs(root: Path, doc_paths: Iterable[str]) -> tuple[dict[str, str], list[Finding]]:
    docs: dict[str, str] = {}
    findings: list[Finding] = []
    for doc_path in sorted(set(doc_paths)):
        full_path = root / doc_path
        if not full_path.is_file():
            findings.append(
                Finding(
                    severity="blocking",
                    surface="docs-ai",
                    kind="missing-doc",
                    name=Path(doc_path).name,
                    path=doc_path,
                    expected_doc=doc_path,
                    message=f"Expected AI catalog file is missing: {doc_path}",
                )
            )
            continue
        docs[doc_path] = full_path.read_text(encoding="utf-8", errors="replace")
    return docs, findings


def check_catalog_drift(root: Path, inventory: Sequence[InventoryItem]) -> list[Finding]:
    docs_to_load = {doc for item in inventory for doc in item.expected_docs}
    docs_to_load.update({DOC_AI_README, AI_CONTRACT})
    docs, findings = load_docs(root, docs_to_load)
    missing_docs = {finding.expected_doc for finding in findings}

    for item in inventory:
        for doc_path in item.expected_docs:
            if doc_path in missing_docs:
                continue
            text = docs.get(doc_path, "")
            markers = (item.name, item.path, *item.alternate_markers)
            if markdown_contains_any(text, markers):
                continue
            findings.append(
                Finding(
                    severity="drift",
                    surface=item.surface,
                    kind=item.kind,
                    name=item.name,
                    path=item.path,
                    expected_doc=doc_path,
                    message=f"{item.kind} {item.name} is not listed in {doc_path}",
                )
            )

    contract_text = docs.get(AI_CONTRACT, "")
    for surface, trigger_paths, doc_path, required_markers in SYSTEM_CHECKS:
        if not any(path_exists(root, path) for path in trigger_paths):
            continue
        if doc_path in missing_docs:
            continue
        missing_markers = [marker for marker in required_markers if marker not in contract_text]
        if not missing_markers:
            continue
        findings.append(
            Finding(
                severity="drift",
                surface=surface,
                kind="system-contract",
                name=surface,
                path=doc_path,
                expected_doc=doc_path,
                message=f"System contract is missing markers: {', '.join(missing_markers)}",
            )
        )

    findings.extend(check_legacy_canonical_links(root, inventory))
    findings.extend(check_compact_assistant_guides(root))
    findings.extend(check_ui_platform_policy(root))
    findings.extend(check_stale_operator_surface_language(root, inventory))
    findings.extend(check_missing_workflow_references(root))

    return sorted(findings, key=lambda finding: (finding.severity, finding.expected_doc, finding.path))


def check_ui_platform_policy(root: Path) -> list[Finding]:
    findings: list[Finding] = []

    for doc_path in UI_PLATFORM_POLICY_FILES:
        path = root / doc_path
        if not path.is_file():
            continue

        text = normalize_whitespace(path.read_text(encoding="utf-8", errors="replace"))
        missing_markers = [marker for marker in UI_PLATFORM_POLICY_MARKERS if marker not in text]
        if not missing_markers:
            continue

        findings.append(
            Finding(
                severity="drift",
                surface="shared-ai-docs",
                kind="ui-platform-policy",
                name="No mobile development lane",
                path=doc_path,
                expected_doc=AI_CONTRACT,
                message=f"{doc_path} is missing UI platform policy markers: {', '.join(missing_markers)}",
            )
        )

    return findings


def check_stale_operator_surface_language(root: Path, inventory: Sequence[InventoryItem]) -> list[Finding]:
    findings: list[Finding] = []
    scanned_paths = {
        item.path
        for item in inventory
        if item.surface in {
            "agent-skills-compatible-hosts",
            "claude",
            "codex",
            "docs-ai",
            "github-copilot",
            "root-assistant-compatibility",
        }
    }
    scanned_paths.update(UI_PLATFORM_POLICY_FILES)
    scanned_paths.update(ACTIVE_OPERATOR_SURFACE_EXTRA_FILES)

    for rel_path in sorted(scanned_paths):
        path = root / rel_path
        if not path.is_file():
            continue

        text = path.read_text(encoding="utf-8", errors="replace")
        matched_phrase = None
        for pattern in STALE_OPERATOR_SURFACE_LANGUAGE:
            match = pattern.search(text)
            if match:
                matched_phrase = match.group(0)
                break
        if not matched_phrase:
            continue

        findings.append(
            Finding(
                severity="drift",
                surface="shared-ai-docs",
                kind="stale-operator-surface-language",
                name=Path(rel_path).name,
                path=rel_path,
                expected_doc=AI_CONTRACT,
                message=(
                    f"{rel_path} still uses stale operator-surface wording ({matched_phrase}); "
                    "describe both browser workstation and WPF desktop as active surfaces backed by shared contracts."
                ),
            )
        )

    return findings


def check_missing_workflow_references(root: Path) -> list[Finding]:
    findings: list[Finding] = []

    for doc_path in AI_WORKFLOW_REFERENCE_FILES:
        path = root / doc_path
        if not path.is_file():
            continue

        text = path.read_text(encoding="utf-8", errors="replace")
        for match in WORKFLOW_REFERENCE_PATTERN.finditer(text):
            workflow_name = match.group("workflow")
            workflow_path = f".github/workflows/{workflow_name}"
            if (root / workflow_path).is_file():
                continue

            findings.append(
                Finding(
                    severity="drift",
                    surface="ai-automation-workflows",
                    kind="missing-workflow-reference",
                    name=workflow_name,
                    path=doc_path,
                    expected_doc=doc_path,
                    message=(
                        f"{doc_path} references {workflow_path}, but that workflow is not active; "
                        "point users to an active workflow, local script, or archive note instead."
                    ),
                )
            )

    return findings


def check_legacy_canonical_links(root: Path, inventory: Sequence[InventoryItem]) -> list[Finding]:
    findings: list[Finding] = []
    scanned_paths: set[str] = set()

    for item in inventory:
        if item.path in scanned_paths:
            continue
        scanned_paths.add(item.path)

        path = root / item.path
        if not path.is_file():
            continue

        text = path.read_text(encoding="utf-8", errors="replace")
        if not any(prefix in text for prefix in LEGACY_CANONICAL_LINK_PREFIXES):
            continue

        findings.append(
            Finding(
                severity="drift",
                surface=item.surface,
                kind="legacy-repository-link",
                name=item.name,
                path=item.path,
                expected_doc=item.path,
                message=(
                    "Legacy canonical GitHub doc link points at rodoHasArrived/Meridian; "
                    f"use {CURRENT_REPOSITORY_URL} for current Meridian-main docs."
                ),
            )
        )

    return findings


def check_compact_assistant_guides(root: Path) -> list[Finding]:
    findings: list[Finding] = []
    for rel_path in ROOT_ASSISTANT_GUIDES:
        path = root / rel_path
        if not path.is_file():
            continue

        text = path.read_text(encoding="utf-8", errors="replace").replace("\r\n", "\n")
        embeds_repository_tree = "\n## Repository Structure\n" in f"\n{text}" and "```text\nMeridian-main\n" in text
        if not embeds_repository_tree:
            continue

        surface = "root-assistant-compatibility" if rel_path == "CLAUDE.md" else "github-copilot"
        findings.append(
            Finding(
                severity="drift",
                surface=surface,
                kind="duplicated-repository-tree",
                name=path.name,
                path=rel_path,
                expected_doc="docs/ai/generated/repo-navigation.md",
                message=(
                    f"{rel_path} embeds a full repository tree; link to generated navigation or "
                    "repository-structure sources instead of duplicating broad layout context."
                ),
            )
        )

    return findings


def build_payload(root: Path, inventory: Sequence[InventoryItem], findings: Sequence[Finding]) -> dict[str, object]:
    by_surface = Counter(item.surface for item in inventory)
    by_kind = Counter(item.kind for item in inventory)
    by_severity = Counter(finding.severity for finding in findings)
    return {
        "generatedAtUtc": STABLE_GENERATED_AT_UTC,
        "repositoryRoot": ".",
        "repositoryName": REPOSITORY_DISPLAY_NAME,
        "status": "pass" if not findings else "drift",
        "summary": {
            "inventoryCount": len(inventory),
            "findingCount": len(findings),
            "bySurface": dict(sorted(by_surface.items())),
            "byKind": dict(sorted(by_kind.items())),
            "bySeverity": dict(sorted(by_severity.items())),
        },
        "inventory": [asdict(item) for item in inventory],
        "findings": [asdict(finding) for finding in findings],
    }


def render_markdown(payload: dict[str, object]) -> str:
    summary = payload["summary"]
    findings = payload["findings"]
    assert isinstance(summary, dict)
    assert isinstance(findings, list)

    lines = [
        "# AI Inventory Drift Report",
        "",
        "> Auto-generated by `build/scripts/docs/check-ai-inventory.py`.",
        "",
        f"- Status: `{payload['status']}`",
        f"- Inventory items: `{summary['inventoryCount']}`",
        f"- Findings: `{summary['findingCount']}`",
        "",
        "## Inventory By Surface",
        "",
        "| Surface | Count |",
        "| --- | ---: |",
    ]

    for surface, count in summary["bySurface"].items():
        lines.append(f"| `{surface}` | {count} |")

    lines.extend(["", "## Findings", ""])
    if findings:
        lines.extend(["| Severity | Surface | Asset | Expected Doc | Message |", "| --- | --- | --- | --- | --- |"])
        for finding in findings:
            lines.append(
                "| `{severity}` | `{surface}` | `{path}` | `{expected_doc}` | {message} |".format(**finding)
            )
    else:
        lines.append("No catalog drift detected.")

    return "\n".join(lines) + "\n"


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Check AI asset inventory and docs catalog drift.")
    parser.add_argument("--root", "-r", type=Path, default=REPO_ROOT, help="Repository root to scan.")
    parser.add_argument("--output", "-o", type=Path, help="Optional Markdown report output.")
    parser.add_argument("--json-output", "-j", type=Path, help="Optional machine-readable JSON output.")
    parser.add_argument("--summary", "-s", action="store_true", help="Print a short summary to stdout.")
    parser.add_argument("--warn-only", action="store_true", help="Return 0 even when drift is found.")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    root = args.root.resolve()
    inventory = collect_inventory(root)
    findings = check_catalog_drift(root, inventory)
    payload = build_payload(root, inventory, findings)

    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(render_markdown(payload), encoding="utf-8")

    if args.json_output:
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        args.json_output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    if args.summary or (not args.output and not args.json_output):
        summary = payload["summary"]
        assert isinstance(summary, dict)
        print(
            f"AI inventory status: {payload['status']}; "
            f"{summary['inventoryCount']} item(s), {summary['findingCount']} finding(s)."
        )
        for finding in findings:
            print(f" - {finding.expected_doc}: {finding.message}")

    if findings and not args.warn_only:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
