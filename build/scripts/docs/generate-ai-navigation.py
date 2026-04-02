#!/usr/bin/env python3
"""
Generate a compact AI navigation dataset for large-repository orientation.

This script produces two synchronized artifacts under docs/ai/generated/:
1. A machine-readable JSON dataset consumed by MCP tools/resources.
2. A human-readable markdown digest for assistants and contributors.

Usage:
    python3 build/scripts/docs/generate-ai-navigation.py \
        --json-output docs/ai/generated/repo-navigation.json \
        --markdown-output docs/ai/generated/repo-navigation.md
"""

from __future__ import annotations

import argparse
import json
import re
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[3]
GENERATOR_VERSION = "1.0"
EXCLUDED_PARTS = {"bin", "obj", "node_modules", ".git", ".vs", "__pycache__", "TestResults", "artifacts"}


@dataclass(frozen=True)
class ProjectSeed:
    project: str
    subsystem: str
    kind: str
    summary: str
    common_tasks: tuple[str, ...]
    keywords: tuple[str, ...]
    key_contracts: tuple[str, ...]
    preferred_entrypoints: tuple[str, ...]


@dataclass(frozen=True)
class SubsystemSeed:
    subsystem: str
    title: str
    summary: str
    common_tasks: tuple[str, ...]
    keywords: tuple[str, ...]


PROJECT_SEEDS: dict[str, ProjectSeed] = {
    "Meridian": ProjectSeed(
        "Meridian",
        "host-shell",
        "host",
        "Application host, CLI surface, and top-level composition entrypoint.",
        ("host startup", "composition wiring", "config bootstrapping", "CLI entrypoints"),
        ("host", "startup", "composition", "cli", "appsettings"),
        ("src/Meridian/Program.cs", "src/Meridian.Application/Composition/ServiceCollectionExtensions.cs"),
        ("Program.cs",),
    ),
    "Meridian.Application": ProjectSeed(
        "Meridian.Application",
        "host-shell",
        "application",
        "Application services, orchestration, pipeline coordination, and runtime workflows.",
        ("service orchestration", "pipeline flow", "application services", "composition"),
        ("application", "services", "orchestration", "pipeline", "composition"),
        ("src/Meridian.Application/Pipeline/EventPipeline.cs",),
        ("Composition", "Pipeline"),
    ),
    "Meridian.Contracts": ProjectSeed(
        "Meridian.Contracts",
        "host-shell",
        "contracts",
        "Cross-project DTOs and shared contracts consumed across layers.",
        ("DTO changes", "contract routing", "cross-layer payloads"),
        ("contracts", "dto", "shared", "api"),
        ("src/Meridian.Contracts",),
        ("",),
    ),
    "Meridian.Core": ProjectSeed(
        "Meridian.Core",
        "host-shell",
        "core",
        "Cross-cutting configuration, exceptions, logging, and serialization infrastructure.",
        ("config", "exceptions", "logging", "serialization"),
        ("core", "config", "logging", "serialization", "exceptions"),
        ("src/Meridian.Core/Serialization/MarketDataJsonContext.cs",),
        ("Serialization",),
    ),
    "Meridian.ProviderSdk": ProjectSeed(
        "Meridian.ProviderSdk",
        "providers-data",
        "sdk",
        "Provider-facing contracts and abstractions for streaming and historical integrations.",
        ("new provider", "provider contract lookup", "adapter routing"),
        ("provider", "sdk", "market data", "streaming", "historical"),
        ("src/Meridian.ProviderSdk/IMarketDataClient.cs",),
        ("IMarketDataClient.cs",),
    ),
    "Meridian.Infrastructure": ProjectSeed(
        "Meridian.Infrastructure",
        "providers-data",
        "infrastructure",
        "Provider adapters, HTTP integration, resilience helpers, and adapter templates.",
        ("provider implementation", "HTTP integration", "adapter templates", "symbol search"),
        ("infrastructure", "providers", "adapters", "http", "reconnect"),
        ("src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs",),
        ("Adapters",),
    ),
    "Meridian.Storage": ProjectSeed(
        "Meridian.Storage",
        "providers-data",
        "storage",
        "Tiered persistence, WAL handling, archival packaging, and storage catalog services.",
        ("storage bug", "WAL integrity", "catalog queries", "archival"),
        ("storage", "wal", "archival", "catalog", "sink"),
        ("src/Meridian.Storage/Interfaces/IStorageSink.cs", "src/Meridian.Storage/Archival/WriteAheadLog.cs"),
        ("Archival", "Interfaces"),
    ),
    "Meridian.Wpf": ProjectSeed(
        "Meridian.Wpf",
        "workstation-ui",
        "desktop-ui",
        "WPF desktop shell and workstation-facing desktop workflows.",
        ("WPF bug", "shell navigation", "desktop MVVM", "workspace flow"),
        ("wpf", "desktop", "shell", "viewmodel", "workspace"),
        ("src/Meridian.Wpf/App.xaml.cs", "src/Meridian.Wpf/MainWindow.xaml"),
        ("App.xaml.cs", "MainWindow.xaml", "ViewModels"),
    ),
    "Meridian.Ui": ProjectSeed(
        "Meridian.Ui",
        "workstation-ui",
        "web-ui",
        "Web UI surface and browser-facing application experiences.",
        ("web ui", "frontend route", "page issue"),
        ("ui", "web", "frontend", "page"),
        ("src/Meridian.Ui",),
        ("Program.cs", "Pages"),
    ),
    "Meridian.Ui.Services": ProjectSeed(
        "Meridian.Ui.Services",
        "workstation-ui",
        "ui-services",
        "Shared services that support UI workflows and state transitions.",
        ("ui services", "shared view state", "workflow services"),
        ("ui services", "viewmodel services", "shared state"),
        ("src/Meridian.Ui.Services",),
        ("",),
    ),
    "Meridian.Ui.Shared": ProjectSeed(
        "Meridian.Ui.Shared",
        "workstation-ui",
        "shared-ui",
        "Shared UI components and contracts reused across interfaces.",
        ("shared component", "ui contract", "cross-ui reuse"),
        ("shared ui", "components", "contracts"),
        ("src/Meridian.Ui.Shared",),
        ("",),
    ),
    "Meridian.Backtesting": ProjectSeed(
        "Meridian.Backtesting",
        "backtesting-research",
        "backtesting",
        "Replay engine and backtesting runtime for historical simulations.",
        ("backtesting engine", "replay flow", "historical simulation"),
        ("backtesting", "replay", "simulation", "research"),
        ("src/Meridian.Backtesting",),
        ("Program.cs",),
    ),
    "Meridian.Backtesting.Sdk": ProjectSeed(
        "Meridian.Backtesting.Sdk",
        "backtesting-research",
        "sdk",
        "Strategy SDK surface for backtesting and simulation integrations.",
        ("sdk integration", "strategy hooks", "backtesting api"),
        ("backtesting sdk", "strategy sdk", "interfaces"),
        ("src/Meridian.Backtesting.Sdk",),
        ("",),
    ),
    "Meridian.QuantScript": ProjectSeed(
        "Meridian.QuantScript",
        "backtesting-research",
        "research",
        "Research and scripting layer for strategy and analytics workflows.",
        ("quantscript", "research workflow", "scripting"),
        ("research", "script", "quant", "analytics"),
        ("src/Meridian.QuantScript",),
        ("Program.cs",),
    ),
    "Meridian.Execution": ProjectSeed(
        "Meridian.Execution",
        "execution-risk",
        "execution",
        "Execution engine and order-routing implementation layer.",
        ("order routing", "gateway issue", "execution flow"),
        ("execution", "orders", "gateway", "routing"),
        ("src/Meridian.Execution/Interfaces/IOrderGateway.cs",),
        ("Interfaces",),
    ),
    "Meridian.Execution.Sdk": ProjectSeed(
        "Meridian.Execution.Sdk",
        "execution-risk",
        "sdk",
        "Execution-facing abstractions shared by gateways and brokers.",
        ("execution sdk", "gateway contracts"),
        ("execution sdk", "gateway", "contracts"),
        ("src/Meridian.Execution.Sdk",),
        ("",),
    ),
    "Meridian.Risk": ProjectSeed(
        "Meridian.Risk",
        "execution-risk",
        "risk",
        "Pre-trade validation rules and risk orchestration layer.",
        ("risk rule", "pre-trade validation", "limit enforcement"),
        ("risk", "pre-trade", "validation", "rules"),
        ("src/Meridian.Risk/IRiskRule.cs",),
        ("IRiskRule.cs",),
    ),
    "Meridian.Strategies": ProjectSeed(
        "Meridian.Strategies",
        "execution-risk",
        "strategies",
        "Strategy lifecycle, run orchestration, and strategy persistence.",
        ("strategy run", "lifecycle issue", "strategy storage"),
        ("strategies", "lifecycle", "runs"),
        ("src/Meridian.Strategies/Interfaces/IStrategyLifecycle.cs",),
        ("Interfaces",),
    ),
    "Meridian.Domain": ProjectSeed(
        "Meridian.Domain",
        "domain-ledger-fsharp",
        "domain",
        "Core trading domain logic, collectors, and event-level behavior.",
        ("domain rules", "event modeling", "collector behavior"),
        ("domain", "collectors", "events"),
        ("src/Meridian.Domain",),
        ("Collectors",),
    ),
    "Meridian.FSharp": ProjectSeed(
        "Meridian.FSharp",
        "domain-ledger-fsharp",
        "fsharp-domain",
        "F# domain library and shared functional model implementations.",
        ("fsharp interop", "domain calculations", "functional models"),
        ("fsharp", "interop", "domain"),
        ("src/Meridian.FSharp",),
        ("Domain",),
    ),
    "Meridian.FSharp.Trading": ProjectSeed(
        "Meridian.FSharp.Trading",
        "domain-ledger-fsharp",
        "fsharp-domain",
        "Trading-focused F# domain models and calculations.",
        ("trading models", "fsharp trading logic"),
        ("fsharp", "trading", "domain"),
        ("src/Meridian.FSharp.Trading",),
        ("",),
    ),
    "Meridian.FSharp.Ledger": ProjectSeed(
        "Meridian.FSharp.Ledger",
        "domain-ledger-fsharp",
        "fsharp-domain",
        "Ledger-focused F# models and interop helpers.",
        ("ledger models", "fsharp ledger logic"),
        ("fsharp", "ledger", "interop"),
        ("src/Meridian.FSharp.Ledger",),
        ("",),
    ),
    "Meridian.FSharp.DirectLending.Aggregates": ProjectSeed(
        "Meridian.FSharp.DirectLending.Aggregates",
        "domain-ledger-fsharp",
        "fsharp-domain",
        "Direct lending aggregates and specialized financial domain behavior.",
        ("direct lending", "aggregate rules", "financial calculations"),
        ("direct lending", "fsharp", "aggregates"),
        ("src/Meridian.FSharp.DirectLending.Aggregates",),
        ("",),
    ),
    "Meridian.Ledger": ProjectSeed(
        "Meridian.Ledger",
        "domain-ledger-fsharp",
        "ledger",
        "Double-entry ledger implementation and accounting workflow support.",
        ("ledger issue", "posting flow", "accounting state"),
        ("ledger", "accounting", "posting"),
        ("src/Meridian.Ledger",),
        ("",),
    ),
    "Meridian.Mcp": ProjectSeed(
        "Meridian.Mcp",
        "mcp-integration",
        "mcp-client",
        "MCP client/host surface for interacting with Meridian capabilities.",
        ("mcp client", "tooling surface", "prompt routing"),
        ("mcp", "tools", "client"),
        ("src/Meridian.Mcp/Program.cs",),
        ("Program.cs",),
    ),
    "Meridian.McpServer": ProjectSeed(
        "Meridian.McpServer",
        "mcp-integration",
        "mcp-server",
        "Attribute-driven MCP server exposing tools, prompts, and resources over stdio.",
        ("mcp server", "new tool", "resource surface", "prompt integration"),
        ("mcp", "server", "tools", "resources", "prompts"),
        ("src/Meridian.McpServer/Program.cs",),
        ("Program.cs", "Tools", "Resources", "Prompts"),
    ),
}

SUBSYSTEM_SEEDS: dict[str, SubsystemSeed] = {
    "host-shell": SubsystemSeed(
        "host-shell",
        "Host and Composition",
        "Runtime startup, application composition, shared contracts, and cross-cutting infrastructure.",
        ("startup debugging", "service composition", "configuration", "shared contracts"),
        ("startup", "composition", "host", "contracts", "core"),
    ),
    "providers-data": SubsystemSeed(
        "providers-data",
        "Providers and Storage",
        "Provider contracts, adapter implementations, storage catalog, WAL, and archival behavior.",
        ("add provider", "provider bug", "storage regression", "catalog query"),
        ("provider", "adapter", "storage", "wal", "backfill"),
    ),
    "workstation-ui": SubsystemSeed(
        "workstation-ui",
        "Desktop and UI Workflows",
        "WPF desktop shell, shared UI services, and browser-facing UI surfaces.",
        ("wpf issue", "viewmodel routing", "workspace flow", "ui polish"),
        ("wpf", "ui", "viewmodel", "workspace", "desktop"),
    ),
    "backtesting-research": SubsystemSeed(
        "backtesting-research",
        "Backtesting and Research",
        "Replay engine, backtesting SDK, and quant research workflows.",
        ("backtesting bug", "simulation", "research scripting"),
        ("backtesting", "simulation", "research", "quantscript"),
    ),
    "execution-risk": SubsystemSeed(
        "execution-risk",
        "Execution, Risk, and Strategies",
        "Order routing, gateways, risk rules, and strategy lifecycle management.",
        ("execution issue", "risk validation", "strategy lifecycle"),
        ("execution", "risk", "strategies", "gateway", "orders"),
    ),
    "domain-ledger-fsharp": SubsystemSeed(
        "domain-ledger-fsharp",
        "Domain, Ledger, and F#",
        "Core domain rules, F# interop, ledger logic, and direct lending aggregates.",
        ("fsharp interop", "domain rule", "ledger behavior", "direct lending"),
        ("domain", "fsharp", "ledger", "interop", "direct lending"),
    ),
    "mcp-integration": SubsystemSeed(
        "mcp-integration",
        "MCP Integration",
        "MCP hosts, tools, prompts, and resources that expose Meridian capabilities to LLMs.",
        ("mcp work", "new mcp tool", "resource routing"),
        ("mcp", "tool", "resource", "prompt", "server"),
    ),
}

DOC_CATALOG: list[dict[str, Any]] = [
    {
        "path": "CLAUDE.md",
        "title": "Root project context",
        "area": "root-context",
        "whenToConsult": "Before any substantial change for commands, architecture, and repo-wide conventions.",
        "keywords": ["overview", "commands", "architecture", "conventions"],
    },
    {
        "path": "docs/ai/README.md",
        "title": "AI resource index",
        "area": "ai-navigation",
        "whenToConsult": "When deciding which AI guide, agent, or prompt to read next.",
        "keywords": ["ai", "index", "navigation", "guidance"],
    },
    {
        "path": "docs/ai/ai-known-errors.md",
        "title": "Known AI errors",
        "area": "guardrails",
        "whenToConsult": "Before changes in areas where AI mistakes recur and after fixing a new AI-caused issue.",
        "keywords": ["errors", "guardrails", "pitfalls", "prevention"],
    },
    {
        "path": "docs/ai/navigation/README.md",
        "title": "AI navigation workflow",
        "area": "ai-navigation",
        "whenToConsult": "When orienting an assistant to a task or deciding how to route work across subsystems.",
        "keywords": ["navigation", "orientation", "routing", "repo map"],
    },
    {
        "path": "docs/ai/claude/CLAUDE.providers.md",
        "title": "Provider implementation guide",
        "area": "providers",
        "whenToConsult": "When working on market data providers, adapter contracts, or provider-facing tests.",
        "keywords": ["providers", "adapter", "backfill", "market data"],
    },
    {
        "path": "docs/ai/claude/CLAUDE.storage.md",
        "title": "Storage guide",
        "area": "storage",
        "whenToConsult": "When touching WAL, archival, cataloging, or persistent sink behavior.",
        "keywords": ["storage", "wal", "archival", "sink"],
    },
    {
        "path": "docs/ai/claude/CLAUDE.testing.md",
        "title": "Testing guide",
        "area": "testing",
        "whenToConsult": "When adding or repairing tests, especially across C#, F#, UI, or provider layers.",
        "keywords": ["tests", "xunit", "coverage", "qa"],
    },
    {
        "path": "docs/ai/claude/CLAUDE.fsharp.md",
        "title": "F# interop guide",
        "area": "fsharp",
        "whenToConsult": "When tracing domain behavior into F# projects or extending interop boundaries.",
        "keywords": ["fsharp", "interop", "domain"],
    },
    {
        "path": "docs/plans/trading-workstation-migration-blueprint.md",
        "title": "Trading workstation migration blueprint",
        "area": "ui",
        "whenToConsult": "When a task affects workflow-centric desktop or workspace experiences.",
        "keywords": ["workstation", "wpf", "workspace", "migration"],
    },
    {
        "path": "docs/development/provider-implementation.md",
        "title": "Provider implementation developer guide",
        "area": "providers",
        "whenToConsult": "When scaffolding or reviewing provider work from outside the AI guides.",
        "keywords": ["provider", "implementation", "adapter"],
    },
]

SYMBOL_CATALOG: list[dict[str, Any]] = [
    {
        "name": "IMarketDataClient",
        "kind": "interface",
        "path": "src/Meridian.ProviderSdk/IMarketDataClient.cs",
        "project": "Meridian.ProviderSdk",
        "reason": "Primary entrypoint for streaming provider work.",
        "keywords": ["provider", "streaming", "market data"],
    },
    {
        "name": "IHistoricalDataProvider",
        "kind": "interface",
        "path": "src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs",
        "project": "Meridian.Infrastructure",
        "reason": "Primary contract for historical/backfill providers.",
        "keywords": ["provider", "historical", "backfill"],
    },
    {
        "name": "MarketDataJsonContext",
        "kind": "json-context",
        "path": "src/Meridian.Core/Serialization/MarketDataJsonContext.cs",
        "project": "Meridian.Core",
        "reason": "Source-generated JSON context used in hot-path and provider serialization.",
        "keywords": ["json", "serialization", "adr-014"],
    },
    {
        "name": "EventPipeline",
        "kind": "pipeline",
        "path": "src/Meridian.Application/Pipeline/EventPipeline.cs",
        "project": "Meridian.Application",
        "reason": "High-signal coordination point for runtime event flow.",
        "keywords": ["pipeline", "events", "runtime"],
    },
    {
        "name": "WriteAheadLog",
        "kind": "storage",
        "path": "src/Meridian.Storage/Archival/WriteAheadLog.cs",
        "project": "Meridian.Storage",
        "reason": "Authoritative WAL implementation for durability and storage integrity work.",
        "keywords": ["storage", "wal", "durability"],
    },
    {
        "name": "AtomicFileWriter",
        "kind": "storage",
        "path": "src/Meridian.Storage/Archival/AtomicFileWriter.cs",
        "project": "Meridian.Storage",
        "reason": "Crash-safe file write boundary used by storage-sensitive changes.",
        "keywords": ["storage", "file io", "durability"],
    },
    {
        "name": "IOrderGateway",
        "kind": "interface",
        "path": "src/Meridian.Execution/Interfaces/IOrderGateway.cs",
        "project": "Meridian.Execution",
        "reason": "Primary execution abstraction for routing order-flow investigations.",
        "keywords": ["execution", "orders", "gateway"],
    },
    {
        "name": "IRiskRule",
        "kind": "interface",
        "path": "src/Meridian.Risk/IRiskRule.cs",
        "project": "Meridian.Risk",
        "reason": "Key contract for pre-trade risk validation work.",
        "keywords": ["risk", "validation", "rules"],
    },
    {
        "name": "IStrategyLifecycle",
        "kind": "interface",
        "path": "src/Meridian.Strategies/Interfaces/IStrategyLifecycle.cs",
        "project": "Meridian.Strategies",
        "reason": "Primary lifecycle abstraction for strategy run work.",
        "keywords": ["strategy", "lifecycle", "runs"],
    },
    {
        "name": "MainWindow",
        "kind": "wpf-shell",
        "path": "src/Meridian.Wpf/MainWindow.xaml",
        "project": "Meridian.Wpf",
        "reason": "Desktop shell entrypoint for WPF workflow and navigation issues.",
        "keywords": ["wpf", "shell", "desktop"],
    },
    {
        "name": "Program",
        "kind": "mcp-entrypoint",
        "path": "src/Meridian.McpServer/Program.cs",
        "project": "Meridian.McpServer",
        "reason": "Registration point for MCP tools, resources, and prompts.",
        "keywords": ["mcp", "server", "registration"],
    },
]

TASK_ROUTE_SEEDS: list[dict[str, Any]] = [
    {
        "id": "provider-work",
        "title": "Provider implementation and provider bugs",
        "description": "Use when adding, debugging, or reviewing streaming, historical, or symbol-search providers.",
        "keywords": ["provider", "adapter", "backfill", "market data", "symbol search"],
        "subsystems": ["providers-data"],
        "startProjects": ["Meridian.ProviderSdk", "Meridian.Infrastructure", "Meridian.Storage"],
        "startSymbols": ["IMarketDataClient", "IHistoricalDataProvider", "MarketDataJsonContext"],
        "docs": [
            "docs/ai/claude/CLAUDE.providers.md",
            "docs/development/provider-implementation.md",
            "docs/ai/ai-known-errors.md",
        ],
        "recommendedSkill": "meridian-provider-builder",
        "recommendedAgent": "provider-builder-agent",
    },
    {
        "id": "wpf-workflow",
        "title": "WPF and workstation workflow issues",
        "description": "Use when the task involves shell navigation, workspace composition, desktop view models, or desktop UX flows.",
        "keywords": ["wpf", "desktop", "workspace", "viewmodel", "shell"],
        "subsystems": ["workstation-ui"],
        "startProjects": ["Meridian.Wpf", "Meridian.Ui.Services", "Meridian.Ui.Shared"],
        "startSymbols": ["MainWindow"],
        "docs": [
            "docs/plans/trading-workstation-migration-blueprint.md",
            "docs/ai/ai-known-errors.md",
        ],
        "recommendedSkill": "meridian-blueprint",
        "recommendedAgent": "code-review-agent",
    },
    {
        "id": "storage-investigation",
        "title": "Storage and WAL investigations",
        "description": "Use when tracing archival bugs, sink registration, WAL sequencing, or storage catalog behavior.",
        "keywords": ["storage", "wal", "archival", "catalog", "sink"],
        "subsystems": ["providers-data"],
        "startProjects": ["Meridian.Storage", "Meridian.Application"],
        "startSymbols": ["WriteAheadLog", "AtomicFileWriter", "EventPipeline"],
        "docs": [
            "docs/ai/claude/CLAUDE.storage.md",
            "docs/ai/ai-known-errors.md",
        ],
        "recommendedSkill": "meridian-code-review",
        "recommendedAgent": "code-review-agent",
    },
    {
        "id": "mcp-surface",
        "title": "MCP tools, prompts, and resources",
        "description": "Use when adding or debugging MCP server behavior, prompt registration, or stdio tool/resource exposure.",
        "keywords": ["mcp", "tool", "resource", "prompt", "server"],
        "subsystems": ["mcp-integration"],
        "startProjects": ["Meridian.McpServer", "Meridian.Mcp"],
        "startSymbols": ["Program"],
        "docs": [
            "docs/ai/navigation/README.md",
            "docs/ai/README.md",
        ],
        "recommendedSkill": "meridian-repo-navigation",
        "recommendedAgent": "repo-navigation-agent",
    },
]


def now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate Meridian AI navigation artifacts.")
    parser.add_argument("--root", type=Path, default=REPO_ROOT, help="Repository root.")
    parser.add_argument("--json-output", type=Path, required=True, help="Path to JSON output.")
    parser.add_argument("--markdown-output", type=Path, required=True, help="Path to markdown output.")
    parser.add_argument("--summary", action="store_true", help="Print a short summary to stdout.")
    return parser.parse_args()


def repo_relative(root: Path, path: Path) -> str:
    return path.resolve().relative_to(root.resolve()).as_posix()


def parse_project_references(project_file: Path) -> list[str]:
    try:
        tree = ET.parse(project_file)
    except ET.ParseError:
        return []

    refs: list[str] = []
    for item in tree.findall(".//ProjectReference"):
        include = item.attrib.get("Include")
        if not include:
            continue
        target = (project_file.parent / include).resolve()
        refs.append(target.stem)
    return sorted(set(refs))


def find_entrypoints(root: Path, project_dir: Path, preferred_names: tuple[str, ...]) -> list[str]:
    found: list[str] = []
    for preferred in preferred_names:
        if not preferred:
            continue
        candidate = project_dir / preferred
        if candidate.exists():
            found.append(repo_relative(root, candidate))
            continue
        matches = sorted(project_dir.rglob(preferred))
        if matches:
            found.append(repo_relative(root, matches[0]))

    if found:
        return found

    fallbacks = [
        *sorted(project_dir.rglob("Program.cs")),
        *sorted(project_dir.rglob("App.xaml.cs")),
        *sorted(project_dir.rglob("MainWindow.xaml")),
    ]
    if fallbacks:
        return [repo_relative(root, path) for path in fallbacks[:3]]

    return [repo_relative(root, project_dir)]


def discover_projects(root: Path) -> list[dict[str, Any]]:
    projects: list[dict[str, Any]] = []
    project_files = sorted(root.glob("src/*/*.csproj")) + sorted(root.glob("src/*/*.fsproj"))
    for project_file in project_files:
        project_name = project_file.stem
        seed = PROJECT_SEEDS.get(project_name)
        if seed is None:
            continue

        project_dir = project_file.parent
        entrypoints = find_entrypoints(root, project_dir, seed.preferred_entrypoints)
        existing_contracts = [path for path in seed.key_contracts if (root / path).exists()]
        related_docs = [
            doc["path"]
            for doc in DOC_CATALOG
            if any(keyword in " ".join(doc["keywords"]).lower() for keyword in seed.keywords[:3])
        ]

        projects.append(
            {
                "name": project_name,
                "path": repo_relative(root, project_dir),
                "kind": seed.kind,
                "subsystem": seed.subsystem,
                "summary": seed.summary,
                "commonTasks": list(seed.common_tasks),
                "keywords": sorted(set(seed.keywords)),
                "entrypoints": entrypoints,
                "keyContracts": existing_contracts,
                "projectReferences": parse_project_references(project_file),
                "relatedDocs": related_docs,
            }
        )

    return projects


def discover_documents(root: Path) -> list[dict[str, Any]]:
    docs: list[dict[str, Any]] = []
    for item in DOC_CATALOG:
        path = root / item["path"]
        if not path.exists():
            continue

        docs.append(
            {
                "path": item["path"],
                "title": item["title"],
                "area": item["area"],
                "whenToConsult": item["whenToConsult"],
                "keywords": item["keywords"],
            }
        )
    return docs


def discover_symbols(root: Path) -> list[dict[str, Any]]:
    symbols: list[dict[str, Any]] = []
    for item in SYMBOL_CATALOG:
        if not (root / item["path"]).exists():
            continue

        symbols.append(dict(item))
    return symbols


def build_subsystems(projects: list[dict[str, Any]], docs: list[dict[str, Any]]) -> list[dict[str, Any]]:
    docs_by_keyword = " ".join(doc["path"] + " " + " ".join(doc["keywords"]) for doc in docs).lower()
    result: list[dict[str, Any]] = []

    for subsystem_id, seed in SUBSYSTEM_SEEDS.items():
        subsystem_projects = [project for project in projects if project["subsystem"] == subsystem_id]
        if not subsystem_projects:
            continue

        contracts: list[str] = []
        entrypoints: list[str] = []
        keywords = set(seed.keywords)
        for project in subsystem_projects:
            contracts.extend(project["keyContracts"])
            entrypoints.extend(project["entrypoints"])
            keywords.update(project["keywords"])

        related_docs = [
            doc["path"]
            for doc in docs
            if any(keyword in " ".join(doc["keywords"]).lower() for keyword in seed.keywords)
            or doc["area"] in {"ai-navigation", "guardrails"}
        ]

        result.append(
            {
                "id": subsystem_id,
                "title": seed.title,
                "summary": seed.summary,
                "projects": [project["name"] for project in subsystem_projects],
                "projectPaths": [project["path"] for project in subsystem_projects],
                "commonTasks": list(seed.common_tasks),
                "keywords": sorted(keywords),
                "keyContracts": sorted(dict.fromkeys(contracts)),
                "entrypoints": sorted(dict.fromkeys(entrypoints)),
                "relatedDocs": sorted(dict.fromkeys(related_docs)),
                "recommendedStart": subsystem_projects[0]["name"],
                "docCoverageHint": "high" if any(keyword in docs_by_keyword for keyword in seed.keywords) else "medium",
            }
        )

    return result


def build_dependencies(projects: list[dict[str, Any]]) -> list[dict[str, Any]]:
    known = {project["name"] for project in projects}
    dependencies: list[dict[str, Any]] = []
    for project in projects:
        for reference in project["projectReferences"]:
            if reference not in known:
                continue
            dependencies.append(
                {
                    "from": project["name"],
                    "to": reference,
                    "reason": f"{project['name']} references {reference} directly via project reference.",
                }
            )
    return dependencies


def build_routes(subsystems: list[dict[str, Any]], symbols: list[dict[str, Any]]) -> list[dict[str, Any]]:
    subsystem_lookup = {subsystem["id"]: subsystem for subsystem in subsystems}
    symbol_lookup = {symbol["name"]: symbol for symbol in symbols}
    routes: list[dict[str, Any]] = []

    for item in TASK_ROUTE_SEEDS:
        matched_subsystems = [subsystem_lookup[subsystem_id]["title"] for subsystem_id in item["subsystems"] if subsystem_id in subsystem_lookup]
        matched_symbols = [
            {
                "name": symbol_lookup[name]["name"],
                "path": symbol_lookup[name]["path"],
                "reason": symbol_lookup[name]["reason"],
            }
            for name in item["startSymbols"]
            if name in symbol_lookup
        ]
        routes.append(
            {
                "id": item["id"],
                "title": item["title"],
                "description": item["description"],
                "keywords": item["keywords"],
                "subsystems": item["subsystems"],
                "subsystemTitles": matched_subsystems,
                "startProjects": item["startProjects"],
                "startSymbols": matched_symbols,
                "authoritativeDocs": item["docs"],
                "recommendedSkill": item["recommendedSkill"],
                "recommendedAgent": item["recommendedAgent"],
            }
        )

    return routes


def build_dataset(root: Path) -> dict[str, Any]:
    projects = discover_projects(root)
    docs = discover_documents(root)
    symbols = discover_symbols(root)
    subsystems = build_subsystems(projects, docs)
    dependencies = build_dependencies(projects)
    routes = build_routes(subsystems, symbols)

    return {
        "generatedAt": now_iso(),
        "generatorVersion": GENERATOR_VERSION,
        "repositoryRoot": root.resolve().as_posix(),
        "subsystems": subsystems,
        "projects": projects,
        "documents": docs,
        "symbols": symbols,
        "taskRoutes": routes,
        "dependencies": dependencies,
        "notes": {
            "purpose": "Orientation-first navigation dataset for assistants working inside Meridian.",
            "scope": "High-signal subsystems, contracts, docs, and task routes rather than exhaustive symbol search.",
        },
    }


def render_markdown(dataset: dict[str, Any]) -> str:
    lines = [
        "# Meridian AI Repo Navigation",
        "",
        f"> Auto-generated on {dataset['generatedAt']} by `build/scripts/docs/generate-ai-navigation.py`. Do not edit manually.",
        "",
        "## Quick Start",
        "",
        "Use this file when an assistant needs fast orientation before reading subsystem-specific guidance.",
        "",
        "| Task shape | Start here | Authoritative docs |",
        "|---|---|---|",
    ]

    for route in dataset["taskRoutes"]:
        docs = ", ".join(f"`{path}`" for path in route["authoritativeDocs"][:3])
        starts = ", ".join(f"`{project}`" for project in route["startProjects"])
        lines.append(f"| {route['title']} | {starts} | {docs} |")

    lines.extend([
        "",
        "## Subsystems",
        "",
    ])

    for subsystem in dataset["subsystems"]:
        lines.append(f"### {subsystem['title']}")
        lines.append("")
        lines.append(subsystem["summary"])
        lines.append("")
        lines.append(f"- Projects: {', '.join(f'`{name}`' for name in subsystem['projects'])}")
        lines.append(f"- Entrypoints: {', '.join(f'`{path}`' for path in subsystem['entrypoints'][:4])}")
        lines.append(f"- Key contracts: {', '.join(f'`{path}`' for path in subsystem['keyContracts'][:4])}")
        lines.append(f"- Common tasks: {', '.join(subsystem['commonTasks'])}")
        lines.append(f"- Related docs: {', '.join(f'`{path}`' for path in subsystem['relatedDocs'][:4])}")
        lines.append("")

    lines.extend([
        "## High-Signal Symbols",
        "",
        "| Symbol | Kind | Project | Why it matters |",
        "|---|---|---|---|",
    ])

    for symbol in dataset["symbols"]:
        lines.append(
            f"| `{symbol['name']}` | {symbol['kind']} | `{symbol['project']}` | {symbol['reason']} |"
        )

    lines.extend([
        "",
        "## Dependency Highlights",
        "",
        "| From | To | Why it matters |",
        "|---|---|---|",
    ])

    for dependency in dataset["dependencies"][:20]:
        lines.append(
            f"| `{dependency['from']}` | `{dependency['to']}` | {dependency['reason']} |"
        )

    lines.append("")
    return "\n".join(lines)


def main() -> int:
    args = parse_args()
    root = args.root.resolve()
    dataset = build_dataset(root)

    args.json_output.parent.mkdir(parents=True, exist_ok=True)
    args.markdown_output.parent.mkdir(parents=True, exist_ok=True)

    args.json_output.write_text(json.dumps(dataset, indent=2) + "\n", encoding="utf-8")
    args.markdown_output.write_text(render_markdown(dataset) + "\n", encoding="utf-8")

    if args.summary:
        print(
            json.dumps(
                {
                    "generatedAt": dataset["generatedAt"],
                    "subsystems": len(dataset["subsystems"]),
                    "projects": len(dataset["projects"]),
                    "documents": len(dataset["documents"]),
                    "symbols": len(dataset["symbols"]),
                    "routes": len(dataset["taskRoutes"]),
                    "dependencies": len(dataset["dependencies"]),
                },
                indent=2,
            )
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
