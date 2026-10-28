#!/usr/bin/env python3
"""Assemble small, source-bound task packets; never replace repository instructions."""
from __future__ import annotations

import argparse
from dataclasses import asdict
import hashlib
import importlib.util
import json
from pathlib import Path, PurePosixPath
import re
import shlex
import subprocess
import sys
from typing import Any

import yaml

from context_budget import MAX_MARKDOWN_LINES, classify_task, _candidate_sources, _slice_code, _slice_markdown
from task_brief import validate_brief

REPO_ROOT = Path(__file__).resolve().parents[3]
NAVIGATION = "docs/ai/generated/repo-navigation.json"
POLICY = "docs/ai/model-routing-policy.json"
RULES = "docs/ai/codex/prompt-route-rules.json"
REGISTRY = "docs/source/data/source-modules.yml"
MAX_INPUT_BYTES = 8 * 1024 * 1024
AI_GUIDANCE_PREFIXES = (
    "build/scripts/ai/", "build/scripts/docs/", "docs/ai/", ".codex/", ".claude/",
    ".agents/", ".github/agents/",
)
AI_GUIDANCE_FILES = {"AGENTS.md", "CLAUDE.md", ".github/copilot-instructions.md"}


def is_ai_guidance(name: str) -> bool:
    return name in AI_GUIDANCE_FILES or name.startswith(AI_GUIDANCE_PREFIXES)


def local_path(root: Path, name: str) -> Path:
    normalized = name.replace("\\", "/")
    parts = PurePosixPath(normalized)
    if not normalized or parts.is_absolute() or ".." in parts.parts or ":" in normalized:
        raise ValueError(f"Expected repository-relative path: {name}")
    candidate = root / normalized
    if not candidate.resolve().is_relative_to(root):
        raise ValueError(f"Path escapes repository: {name}")
    return candidate


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def revision(root: Path) -> str | None:
    result = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"], capture_output=True, text=True)
    return result.stdout.strip() if result.returncode == 0 else None


class Sources:
    def __init__(self, root: Path):
        self.root = root
        self.contents: dict[str, str] = {}
        self.hashes: dict[str, str] = {}
        self.missing: set[str] = set()

    def read(self, name: str, *, required: bool = False) -> str | None:
        path = local_path(self.root, name)
        name = path.relative_to(self.root).as_posix()
        if name in self.contents:
            return self.contents[name]
        if not path.is_file():
            if required:
                raise ValueError(f"Required file is missing: {name}")
            self.missing.add(name)
            return None
        if path.stat().st_size > MAX_INPUT_BYTES:
            raise ValueError(f"Input exceeds {MAX_INPUT_BYTES} bytes: {name}")
        data = path.read_bytes()
        try:
            text = data.decode("utf-8")
        except UnicodeDecodeError as exc:
            raise ValueError(f"Input must be UTF-8: {name}") from exc
        self.contents[name] = text
        self.hashes[name] = digest(data)
        return text

    def json(self, name: str) -> dict[str, Any]:
        text = self.read(name)
        if text is None:
            return {}
        result = json.loads(text)
        if not isinstance(result, dict):
            raise ValueError(f"Expected JSON object: {name}")
        return result


def context_directory(root: Path, target: str) -> PurePosixPath:
    """Include an owned directory's own guidance as well as its ancestors."""
    path = PurePosixPath(target)
    return path if local_path(root, target).is_dir() else path.parent


def instruction_paths(root: Path, targets: list[str]) -> list[str]:
    names = {"AGENTS.md", "AGENTS.override.md", "CLAUDE.md", ".codex/skills/_shared/project-context.md",
             ".codex/skills/_shared/codex-execution-contract.md"}
    for target in targets:
        parent = context_directory(root, target)
        while str(parent) != ".":
            for filename in ("AGENTS.md", "AGENTS.override.md"):
                name = (parent / filename).as_posix()
                local_path(root, name)  # Check even absent instruction paths for symlink escapes.
                names.add(name)
            parent = parent.parent
    return sorted(names)


def nearest_readme(root: Path, target: str) -> str | None:
    parent = context_directory(root, target)
    while str(parent) != ".":
        name = (parent / "README.md").as_posix()
        if local_path(root, name).is_file():
            return name
        parent = parent.parent
    return "README.md" if local_path(root, "README.md").is_file() else None



def task_markdown_slice(lines: list[str], task: str, terms: list[str]) -> tuple[int, int]:
    """Prefer a section explicitly named in the objective over generic keyword hits."""
    normalize = lambda text: " ".join(re.findall(r"[a-z0-9]+", text.lower()))
    normalized_task = " " + normalize(task) + " "
    headings = []
    for number, line in enumerate(lines, start=1):
        match = re.match(r"^(#{1,6})\s+(.+?)\s*#*\s*$", line)
        if match:
            headings.append((number, len(match[1]), normalize(match[2])))
    marked_phrases = [normalize(value) for value in re.findall(r"#{1,6}\s+([^\n]+)", task)]
    matches = [heading for heading in headings
               if heading[2] and " " + heading[2] + " " in normalized_task]
    if not matches:
        return _slice_markdown(lines, terms)
    # A longer heading phrase wins over an incidental shorter heading such as
    # "Commands". Earlier source order breaks equal-specificity ties.
    def specificity(heading: tuple[int, int, str]) -> tuple:
        title = heading[2]
        marked = any(phrase == title or phrase.startswith(title + " ") for phrase in marked_phrases)
        return marked, len(title.split()), len(title), heading[1], -heading[0]

    start, level, _ = max(matches, key=specificity)
    end = min(len(lines), start + MAX_MARKDOWN_LINES - 1)
    for number, next_level, _ in headings:
        if number > start and next_level <= level:
            end = min(end, number - 1)
            break
    return start, end


def build_packet(root: Path, task: str = "", targets: list[str] | None = None, *, max_files: int = 12,
                 max_chars: int = 24000, brief: dict[str, Any] | None = None,
                 brief_path: str | None = None) -> dict[str, Any]:
    root = root.resolve()
    sources = Sources(root)
    targets = list(targets or [])
    if brief_path:
        brief_path = local_path(root, brief_path).relative_to(root).as_posix()
        loaded = validate_brief(root, json.loads(sources.read(brief_path, required=True)))
        if brief is not None and validate_brief(root, brief) != loaded:
            raise ValueError("Inline task brief differs from its source file")
        brief = loaded
    elif brief is not None:
        brief = validate_brief(root, brief)
    if brief is not None:
        task = brief["desiredBehavior"]
        if not targets:
            targets = [name for name in brief["ownedPaths"] if local_path(root, name).is_file()]
        if not targets:
            raise ValueError("Brief ownedPaths contains no existing file anchor; provide --target-file with an existing source or README")
    if not task.strip() or not targets:
        raise ValueError("Provide a non-empty task and at least one target file")
    if not 1 <= max_files <= 40 or not 256 <= max_chars <= 100000:
        raise ValueError("max-files must be 1..40 and max-chars must be 256..100000")
    if len(task) > 12000 or len(targets) > 40:
        raise ValueError("Task or target list exceeds packet input limits")
    targets = sorted({local_path(root, name).relative_to(root).as_posix() for name in targets})
    planning_paths = sorted(set(targets) | set(brief["ownedPaths"] if brief else []))
    for name in targets:
        sources.read(name, required=True)
    navigation = sources.json(NAVIGATION)
    policy = sources.json(POLICY)
    classification = classify_task(navigation, policy, task, planning_paths)
    route: dict[str, Any] = json.loads(json.dumps(asdict(classification)))
    rules = sources.json(RULES)
    if rules:
        # Reuse the canonical router without invoking its artifact-writing CLI.
        module_path = Path(__file__).resolve().parents[1] / "docs/prompt-route-linter.py"
        spec = importlib.util.spec_from_file_location("meridian_task_packet_router", module_path)
        module = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = module
        spec.loader.exec_module(module)
        module.validate_ruleset(rules)
        route["promptRoute"] = asdict(module.match_route(task, rules))

    registry_text = sources.read(REGISTRY)
    registry = yaml.safe_load(registry_text) if registry_text else {}
    if registry is not None and not isinstance(registry, dict):
        raise ValueError("Module registry must be a mapping")
    registered = (registry or {}).get("modules", [])
    if not isinstance(registered, list) or any(not isinstance(item, dict) for item in registered):
        raise ValueError("Module registry modules must be a list of mappings")
    modules = []
    for item in registered:
        prefix = str(item.get("path", "")).rstrip("/")
        if prefix and any(name == prefix or name.startswith(prefix + "/") for name in planning_paths):
            modules.append({key: item.get(key) for key in ("id", "path", "owner_lane", "readme", "validation")})
    modules.sort(key=lambda item: str(item["id"]))
    validation = sorted({str(command) for item in modules for command in item.get("validation") or []})
    validation = list(dict.fromkeys(validation + route.get("promptRoute", {}).get("validation_floor", [])))
    consumers = []
    if any(name.startswith(("src/Meridian.Contracts/", "src/Meridian.Ui.Shared/", "src/Meridian.Ui.Services/")) for name in planning_paths):
        consumers = ["src/Meridian.Ui/dashboard", "src/Meridian.Wpf"]

    planner_path = Path(__file__).resolve().parents[2] / "python/cli/validation_plan.py"
    spec = importlib.util.spec_from_file_location("meridian_task_packet_validation_plan", planner_path)
    planner = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = planner
    spec.loader.exec_module(planner)
    validation_plan = planner.recommend_validation(root, planning_paths)
    for source in validation_plan["sources"]:
        sources.read(source["path"], required=True)
        if sources.hashes[source["path"]] != source["sha256"]:
            raise ValueError(f"Validation metadata changed during packet generation: {source['path']}")
    for name in ("build/python/cli/validation_plan.py", "build/python/cli/buildctl.py"):
        sources.read(name)
    validation = list(dict.fromkeys(validation + (brief["validationCommands"] if brief else []) +
                                   [shlex.join(check["argv"]) for check in validation_plan["checks"]]))
    instructions = []
    for name in instruction_paths(root, planning_paths):
        if local_path(root, name).is_file():
            sources.read(name)
            instructions.append(name)
    candidates = [(name, 200, "target file") for name in targets]
    if any(is_ai_guidance(name) for name in planning_paths):
        candidates.append(("docs/ai/tooling/README.md", 180, "AI tooling command and validation index"))
    for name in planning_paths:
        readme = nearest_readme(root, name)
        if readme:
            candidates.append((readme, 20 if readme == "README.md" else 130, "nearest module README"))
    for item in modules:
        if item["readme"]:
            candidates.append((item["readme"], 130, "registered module README"))
    for consumer in consumers:
        candidates.append((consumer + "/README.md", 125, "shared-contract consumer README"))
    ai_only = all(is_ai_guidance(name) for name in planning_paths)
    navigation_candidates = _candidate_sources(navigation, classification, targets)
    # Keyword routing is advisory; e.g. "guidance" may match a UI keyword. Keep an
    # explicit tooling-only task from loading unrelated product implementation.
    unrelated = []
    for candidate in navigation_candidates:
        if ai_only and candidate[0].startswith("src/"):
            unrelated.append({"path": candidate[0], "reason": "outside explicit AI-tooling scope"})
        else:
            candidates.append(candidate)
    selected = []
    omitted = unrelated
    seen = set()
    used = 0
    terms = sorted(set(re.findall(r"[a-z][a-z0-9-]{2,}", task.lower())))
    # Resolve directory references to README only: never scan a whole subtree.
    for name, _, reason in sorted(candidates, key=lambda item: (-item[1], item[0])):
        path = local_path(root, name)
        if path.is_dir():
            name = (PurePosixPath(name) / "README.md").as_posix()
        if name in seen:
            continue
        seen.add(name)
        text = sources.read(name)
        if text is None:
            omitted.append({"path": name, "reason": "source missing"})
            continue
        if len(selected) >= max_files or used >= max_chars:
            omitted.append({"path": name, "reason": "packet file or character budget exhausted"})
            continue
        lines = text.splitlines()
        start, end = task_markdown_slice(lines, task, terms) if name.endswith(".md") else _slice_code(lines, terms)
        excerpt = "\n".join(lines[start - 1:end])
        clipped = excerpt[:min(3000, max_chars - used)]
        if not clipped:
            omitted.append({"path": name, "reason": "source has no excerptable content"})
            continue
        end = start + clipped.count("\n")
        selected.append({"path": name, "reason": reason, "lineStart": start, "lineEnd": end,
                         "excerpt": clipped, "sha256": sources.hashes[name],
                         "truncated": clipped != text})
        used += len(clipped)
    warnings = list(validation_plan["coverageWarnings"])
    if brief is not None and brief_path is None:
        warnings.append("Inline task brief has no external freshness source; use --brief-file for source-bound checks.")
    if not modules:
        warnings.append("No matching source-module owner; inspect the nearest README for ownership and proof.")
    if not validation:
        warnings.append("No validation command selected; choose focused proof from current source and instructions.")
    if consumers:
        warnings.append("Shared contract changes require checking both browser and WPF consumers; impact is not proof of test coverage.")
    warnings.append("Excerpts are partial context. Read applicable instructions in full; packets never override them.")
    return {
        "schemaVersion": 1, "objective": task.strip(),
        "scope": {"targets": targets, "ownedPaths": brief["ownedPaths"] if brief else targets, "affectedConsumers": consumers},
        "repository": {"revision": revision(root)}, "route": route,
        "moduleOwners": modules, "validation": validation, "validationPlan": validation_plan,
        "brief": brief, "briefSource": brief_path,
        "instructions": instructions, "context": selected, "omitted": omitted,
        "sources": [{"path": name, "sha256": value} for name, value in sorted(sources.hashes.items())],
        "missingSources": sorted(sources.missing), "warnings": warnings,
        "budget": {"maxFiles": max_files, "maxChars": max_chars, "selectedFiles": len(selected), "excerptChars": used},
    }


def check_packet(root: Path, packet: dict[str, Any]) -> None:
    if packet.get("schemaVersion") != 1:
        raise ValueError("Unsupported task packet schema")
    budget = packet["budget"]
    current = build_packet(root, packet["objective"], packet["scope"]["targets"],
                           max_files=budget["maxFiles"], max_chars=budget["maxChars"],
                           brief=None if packet.get("briefSource") else packet.get("brief"),
                           brief_path=packet.get("briefSource"))
    if current != packet:
        raise ValueError("Task packet is stale or altered; regenerate before use")


def markdown(packet: dict[str, Any]) -> str:
    brief_lines = []
    if packet.get("brief"):
        brief_lines = ["## Observable task brief", "", "````json",
                       json.dumps(packet["brief"], indent=2, ensure_ascii=False), "````", ""]
    plan = packet["validationPlan"]
    plan_lines = ["## Focused validation plan (not executed)", "", plan["policy"], ""]
    plan_lines += [f"- `{shlex.join(check['argv'])}`: {check['reason']}" for check in plan["checks"]]
    plan_lines += ["", "Required final checks remain: `bash scripts/ci.sh`, `Meridian CI / quality-gate`, and `Meridian CI / integration-gate`.", ""]
    lines = ["# Task context packet", "", packet["objective"], "", *brief_lines, *plan_lines,
             "Packets provide partial context and never override applicable instructions.", "",
             "## Scope", "", *[f"- `{name}`" for name in packet["scope"]["targets"]], "",
             "## Instructions to read in full", "", *[f"- `{name}`" for name in packet["instructions"]], "",
             "## Validation candidates (not executed)", "", *[f"- `{command}`" for command in packet["validation"]], "",
             "## Warnings", "", *[f"- {warning}" for warning in packet["warnings"]], "",
             "## Omitted context", "", *[f"- `{item['path']}`: {item['reason']}" for item in packet["omitted"]], "",
             "## Selected source excerpts", ""]
    for item in packet["context"]:
        lines += [f"### {item['path']}:{item['lineStart']}", "", item["reason"], "", "````",
                  item["excerpt"], "````", ""]
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=REPO_ROOT)
    objective = parser.add_mutually_exclusive_group()
    objective.add_argument("--task")
    objective.add_argument("--brief-file", help="Repository-relative observable task brief JSON")
    parser.add_argument("--target-file", action="append", default=[])
    parser.add_argument("--output", default="artifacts/ai/task-context.json")
    parser.add_argument("--markdown-output")
    parser.add_argument("--max-files", type=int, default=12)
    parser.add_argument("--max-chars", type=int, default=24000)
    parser.add_argument("--check", metavar="PACKET_JSON")
    args = parser.parse_args()
    root = args.root.resolve()
    try:
        if args.check:
            packet_path = local_path(root, args.check)
            check_packet(root, json.loads(packet_path.read_text(encoding="utf-8")))
            print("Task context packet is current")
            return 0
        packet = build_packet(root, args.task or "", args.target_file, max_files=args.max_files, max_chars=args.max_chars,
                              brief_path=args.brief_file)
        destinations = [local_path(root, args.output)]
        if args.markdown_output:
            destinations.append(local_path(root, args.markdown_output))
        protected = {local_path(root, name).resolve() for name in
                     [item["path"] for item in packet["sources"]] + packet["missingSources"] + packet["scope"]["ownedPaths"]}
        if len({path.resolve() for path in destinations}) != len(destinations) or any(path.resolve() in protected for path in destinations):
            raise ValueError("Packet outputs collide with each other or a source file")
        rendered = [json.dumps(packet, indent=2, sort_keys=True) + "\n", markdown(packet)]
        for path, content in zip(destinations, rendered):
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        print(f"Task packet: {packet['budget']['selectedFiles']} excerpts, {packet['budget']['excerptChars']} chars")
        return 0
    except (ValueError, OSError, KeyError, TypeError, yaml.YAMLError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1
