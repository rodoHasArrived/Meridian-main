#!/usr/bin/env python3
"""Build a token-capped AI context pack from task text and target files."""

from __future__ import annotations

import argparse
import json
import math
import re
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_NAVIGATION_JSON = Path("docs/ai/generated/repo-navigation.json")
DEFAULT_ROUTING_POLICY_JSON = Path("docs/ai/model-routing-policy.json")
MAX_MARKDOWN_LINES = 140
MAX_CODE_LINES = 180
MIN_TRUNCATED_LINES = 10


@dataclass(frozen=True)
class ClassifiedTask:
    subsystem_id: str | None
    subsystem_title: str | None
    route_id: str | None
    route_title: str | None
    task_type: str
    model_class: str
    model_route_id: str | None
    max_context_tokens: int
    mode: str
    escalation_reasons: tuple[str, ...]
    escalated_from: str | None = None


@dataclass(frozen=True)
class CandidateEntry:
    path: str
    line_start: int
    line_end: int
    token_estimate: int
    priority: int
    reason: str


def now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build a token-capped AI context pack.")
    parser.add_argument("--task", help="Task description text.")
    parser.add_argument("--task-file", type=Path, help="Path to a file containing task description text.")
    parser.add_argument("--target-file", action="append", default=[], help="Touched or target file path (repeatable).")
    parser.add_argument("--target-files-json", type=Path, help="Optional JSON file containing a list of target files.")
    parser.add_argument("--navigation-json", type=Path, default=DEFAULT_NAVIGATION_JSON, help=f"Repo navigation JSON (default: {DEFAULT_NAVIGATION_JSON}).")
    parser.add_argument("--routing-policy-json", type=Path, default=DEFAULT_ROUTING_POLICY_JSON, help=f"Model routing policy JSON (default: {DEFAULT_ROUTING_POLICY_JSON}).")
    parser.add_argument("--task-type", choices=("analysis", "implementation", "governance"), help="Optional task-type override.")
    parser.add_argument("--max-files", type=int, default=12, help="Maximum files to include in the emitted pack.")
    parser.add_argument("--json-output", type=Path, help="Optional output path. Prints to stdout when omitted.")
    parser.add_argument("--summary", action="store_true", help="Print compact summary after writing payload.")
    parser.add_argument("--escalation-log", type=Path, help="Optional JSONL path for Deep Review escalation entries.")
    parser.add_argument("--root", type=Path, default=REPO_ROOT, help="Repository root path.")
    return parser.parse_args()


def _load_json(path: Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ValueError(f"JSON file not found: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ValueError(f"Invalid JSON in {path}: {exc}") from exc


def _resolve_task_text(args: argparse.Namespace) -> str:
    if bool(args.task) == bool(args.task_file):
        raise ValueError("Provide exactly one of --task or --task-file.")
    if args.task_file:
        return args.task_file.read_text(encoding="utf-8").strip()
    return str(args.task).strip()


def _normalize_rel_path(path: str) -> str:
    return path.replace("\\", "/").lstrip("./")


def _load_target_files(args: argparse.Namespace) -> list[str]:
    targets = [_normalize_rel_path(path) for path in args.target_file]
    if args.target_files_json:
        loaded = json.loads(args.target_files_json.read_text(encoding="utf-8"))
        if not isinstance(loaded, list):
            raise ValueError("--target-files-json must contain a JSON list.")
        targets.extend(_normalize_rel_path(str(path)) for path in loaded)
    return sorted(dict.fromkeys(path for path in targets if path))


def _count_keyword_hits(text_lower: str, keywords: list[str]) -> int:
    score = 0
    for keyword in keywords:
        token = keyword.strip().lower()
        if not token:
            continue
        if token in text_lower:
            score += 1
    return score


def _find_task_type(task: str, override: str | None) -> str:
    if override:
        return override

    task_lower = task.lower()
    governance_terms = ["governance", "policy", "security", "compliance", "credential", "provider", "audit"]
    analysis_terms = ["analysis", "analyze", "investigate", "research", "plan", "explain", "document", "review"]

    if _count_keyword_hits(task_lower, governance_terms) > 0:
        return "governance"
    if _count_keyword_hits(task_lower, analysis_terms) > 0:
        return "analysis"
    return "implementation"


def _score_subsystems(navigation: dict[str, Any], task: str, target_files: list[str]) -> tuple[str | None, str | None, dict[str, int]]:
    task_lower = task.lower()
    scores: dict[str, int] = {}
    titles: dict[str, str] = {}

    for subsystem in navigation.get("subsystems", []):
        subsystem_id = subsystem.get("id")
        if not isinstance(subsystem_id, str):
            continue

        title = str(subsystem.get("title", subsystem_id))
        titles[subsystem_id] = title
        score = 0

        keywords = [str(item) for item in subsystem.get("keywords", []) if isinstance(item, str)]
        score += _count_keyword_hits(task_lower, keywords) * 3

        project_paths = [str(path) for path in subsystem.get("projectPaths", []) if isinstance(path, str)]
        contracts = [str(path) for path in subsystem.get("keyContracts", []) if isinstance(path, str)]
        entrypoints = [str(path) for path in subsystem.get("entrypoints", []) if isinstance(path, str)]
        path_prefixes = project_paths + contracts + entrypoints

        for target in target_files:
            for prefix in path_prefixes:
                normalized = _normalize_rel_path(prefix)
                if target == normalized or target.startswith(f"{normalized}/"):
                    score += 15
                    break

        if score > 0:
            scores[subsystem_id] = score

    if not scores:
        return None, None, {}

    best_id = max(scores.items(), key=lambda item: item[1])[0]
    return best_id, titles.get(best_id), scores


def _score_routes(navigation: dict[str, Any], task: str, subsystem_id: str | None) -> tuple[str | None, str | None, int]:
    task_lower = task.lower()
    best_id: str | None = None
    best_title: str | None = None
    best_score = 0

    for route in navigation.get("taskRoutes", []):
        route_id = route.get("id")
        if not isinstance(route_id, str):
            continue
        score = _count_keyword_hits(task_lower, [str(item) for item in route.get("keywords", [])]) * 4
        if subsystem_id and subsystem_id in route.get("subsystems", []):
            score += 3
        if score > best_score:
            best_id = route_id
            best_title = str(route.get("title", route_id))
            best_score = score

    return best_id, best_title, best_score


def _model_budget_for_task_type(policy: dict[str, Any], task_type: str) -> tuple[str, str | None, int]:
    class_budget = {
        str(entry.get("id")): int(entry.get("maxContextTokens", 0))
        for entry in policy.get("modelClasses", [])
        if isinstance(entry, dict) and entry.get("id")
    }

    for rule in policy.get("routingRules", []):
        if rule.get("taskType") != task_type:
            continue
        model_class = str(rule.get("modelClass", "balanced"))
        return model_class, str(rule.get("id")), class_budget.get(model_class, 0)

    default_rule_id = str(policy.get("defaultRouteId", ""))
    for rule in policy.get("routingRules", []):
        if rule.get("id") != default_rule_id:
            continue
        model_class = str(rule.get("modelClass", "balanced"))
        return model_class, default_rule_id, class_budget.get(model_class, 0)

    fallback_class = "balanced"
    return fallback_class, None, class_budget.get(fallback_class, 0)


def classify_task(
    navigation: dict[str, Any],
    policy: dict[str, Any],
    task: str,
    target_files: list[str],
    *,
    task_type_override: str | None = None,
) -> ClassifiedTask:
    task_type = _find_task_type(task, task_type_override)
    subsystem_id, subsystem_title, subsystem_scores = _score_subsystems(navigation, task, target_files)
    route_id, route_title, _ = _score_routes(navigation, task, subsystem_id)
    model_class, model_route_id, max_context_tokens = _model_budget_for_task_type(policy, task_type)

    touched_subsystems = set()
    for subsystem in navigation.get("subsystems", []):
        sid = subsystem.get("id")
        if not isinstance(sid, str):
            continue
        for target in target_files:
            for project_path in subsystem.get("projectPaths", []):
                project = _normalize_rel_path(str(project_path))
                if target == project or target.startswith(f"{project}/"):
                    touched_subsystems.add(sid)
                    break

    escalation_reasons: list[str] = []
    task_lower = task.lower()
    if len(touched_subsystems) > 1:
        escalation_reasons.append("target files span multiple subsystems")
    if any(term in task_lower for term in ("cross-cutting", "cross subsystem", "repo-wide", "broad context")):
        escalation_reasons.append("task explicitly requests broad or cross-cutting context")
    if task_type == "governance":
        escalation_reasons.append("governance task type requires Deep Review")

    mode = "Deep Review" if escalation_reasons else "Standard"
    escalated_from: str | None = None

    if mode == "Deep Review" and model_class != "research":
        escalated_from = model_class
        model_class, model_route_id, max_context_tokens = _model_budget_for_task_type(policy, "governance")

    if max_context_tokens <= 0:
        max_context_tokens = 120000

    return ClassifiedTask(
        subsystem_id=subsystem_id,
        subsystem_title=subsystem_title,
        route_id=route_id,
        route_title=route_title,
        task_type=task_type,
        model_class=model_class,
        model_route_id=model_route_id,
        max_context_tokens=max_context_tokens,
        mode=mode,
        escalation_reasons=tuple(escalation_reasons),
        escalated_from=escalated_from,
    )


def _estimate_tokens(text: str) -> int:
    if not text:
        return 0
    return max(1, math.ceil(len(text) / 4))


def _markdown_sections(lines: list[str]) -> list[tuple[int, int]]:
    starts = [index for index, line in enumerate(lines, start=1) if re.match(r"^#{1,6}\s+", line)]
    if not starts:
        return [(1, len(lines))] if lines else []

    sections: list[tuple[int, int]] = []
    for idx, start in enumerate(starts):
        end = starts[idx + 1] - 1 if idx + 1 < len(starts) else len(lines)
        sections.append((start, end))
    return sections


def _slice_markdown(lines: list[str], terms: list[str]) -> tuple[int, int]:
    if not lines:
        return 1, 0

    text_lower = "\n".join(lines).lower()
    if not terms or not any(term in text_lower for term in terms):
        return 1, min(len(lines), MAX_MARKDOWN_LINES)

    best_range = (1, min(len(lines), MAX_MARKDOWN_LINES))
    best_score = -1
    for start, end in _markdown_sections(lines):
        section_text = "\n".join(lines[start - 1:end]).lower()
        score = sum(1 for term in terms if term in section_text)
        if score > best_score:
            best_score = score
            if end - start + 1 > MAX_MARKDOWN_LINES:
                end = start + MAX_MARKDOWN_LINES - 1
            best_range = (start, end)
    return best_range


def _slice_code(lines: list[str], terms: list[str]) -> tuple[int, int]:
    if not lines:
        return 1, 0

    if not terms:
        return 1, min(len(lines), MAX_CODE_LINES)

    for idx, line in enumerate(lines, start=1):
        line_lower = line.lower()
        if any(term in line_lower for term in terms):
            start = max(1, idx - 40)
            end = min(len(lines), start + MAX_CODE_LINES - 1)
            return start, end

    return 1, min(len(lines), MAX_CODE_LINES)


def _expand_candidate_path(root: Path, relative_path: str, *, limit: int = 2) -> list[str]:
    normalized = _normalize_rel_path(relative_path)
    absolute = root / normalized
    if absolute.is_file():
        return [normalized]
    if not absolute.is_dir():
        return []

    preferred = ["README.md", "Program.cs", "App.xaml.cs", "MainWindow.xaml", "package.json"]
    collected: list[Path] = []
    for name in preferred:
        candidate = absolute / name
        if candidate.is_file():
            collected.append(candidate)

    suffixes = {".cs", ".fs", ".py", ".md", ".json", ".xaml", ".ts", ".tsx"}
    for file_path in sorted(absolute.rglob("*")):
        if not file_path.is_file() or file_path.suffix.lower() not in suffixes:
            continue
        collected.append(file_path)
        if len(collected) >= max(1, limit) + len(preferred):
            break

    deduped = []
    seen = set()
    for file_path in collected:
        rel = _normalize_rel_path(file_path.relative_to(root).as_posix())
        if rel in seen:
            continue
        seen.add(rel)
        deduped.append(rel)
        if len(deduped) >= max(1, limit):
            break
    return deduped


def _candidate_sources(navigation: dict[str, Any], classification: ClassifiedTask, target_files: list[str]) -> list[tuple[str, int, str]]:
    candidates: list[tuple[str, int, str]] = []

    for target in target_files:
        candidates.append((target, 120, "target file"))

    if classification.subsystem_id:
        subsystem = next(
            (item for item in navigation.get("subsystems", []) if item.get("id") == classification.subsystem_id),
            None,
        )
        if isinstance(subsystem, dict):
            for path in subsystem.get("keyContracts", []):
                candidates.append((str(path), 100, "subsystem key contract"))
            for path in subsystem.get("entrypoints", []):
                candidates.append((str(path), 70, "subsystem entrypoint"))
            for path in subsystem.get("relatedDocs", []):
                candidates.append((str(path), 80, "subsystem required doc"))

    if classification.route_id:
        route = next(
            (item for item in navigation.get("taskRoutes", []) if item.get("id") == classification.route_id),
            None,
        )
        if isinstance(route, dict):
            for path in route.get("authoritativeDocs", []):
                candidates.append((str(path), 95, "route authoritative doc"))
            for symbol in route.get("startSymbols", []):
                if isinstance(symbol, dict) and isinstance(symbol.get("path"), str):
                    candidates.append((str(symbol["path"]), 90, "route start symbol"))

    return candidates


def _build_candidate_entries(
    root: Path,
    task: str,
    navigation: dict[str, Any],
    classification: ClassifiedTask,
    target_files: list[str],
) -> list[CandidateEntry]:
    terms = [token.lower() for token in re.findall(r"[A-Za-z][A-Za-z0-9\-]{2,}", task)]
    terms = sorted(dict.fromkeys(terms))

    entries: list[CandidateEntry] = []
    seen_paths: set[str] = set()

    for candidate_path, priority, reason in _candidate_sources(navigation, classification, target_files):
        for expanded in _expand_candidate_path(root, candidate_path):
            if expanded in seen_paths:
                continue

            absolute = root / expanded
            try:
                content = absolute.read_text(encoding="utf-8")
            except (FileNotFoundError, IsADirectoryError, UnicodeDecodeError):
                continue

            lines = content.splitlines()
            if absolute.suffix.lower() == ".md":
                line_start, line_end = _slice_markdown(lines, terms)
            else:
                line_start, line_end = _slice_code(lines, terms)

            if line_end < line_start:
                continue

            selected_lines = lines[line_start - 1:line_end]
            token_estimate = _estimate_tokens("\n".join(selected_lines))
            entries.append(
                CandidateEntry(
                    path=expanded,
                    line_start=line_start,
                    line_end=line_end,
                    token_estimate=token_estimate,
                    priority=priority,
                    reason=reason,
                )
            )
            seen_paths.add(expanded)

    return sorted(entries, key=lambda item: (-item.priority, item.token_estimate, item.path))


def _truncate_entry(root: Path, entry: CandidateEntry, remaining_tokens: int) -> CandidateEntry | None:
    if remaining_tokens <= 0:
        return None
    if entry.token_estimate <= remaining_tokens:
        return entry

    absolute = root / entry.path
    try:
        lines = absolute.read_text(encoding="utf-8").splitlines()
    except (FileNotFoundError, UnicodeDecodeError):
        return None

    span = entry.line_end - entry.line_start + 1
    if span <= MIN_TRUNCATED_LINES:
        return None

    ratio = remaining_tokens / max(1, entry.token_estimate)
    new_span = max(MIN_TRUNCATED_LINES, int(span * ratio))
    new_span = min(new_span, span)
    new_end = min(len(lines), entry.line_start + new_span - 1)
    selected = "\n".join(lines[entry.line_start - 1:new_end])
    token_estimate = _estimate_tokens(selected)
    if token_estimate > remaining_tokens:
        return None

    return CandidateEntry(
        path=entry.path,
        line_start=entry.line_start,
        line_end=new_end,
        token_estimate=token_estimate,
        priority=entry.priority,
        reason=f"{entry.reason} (truncated to fit budget)",
    )


def generate_context_pack(
    root: Path,
    navigation: dict[str, Any],
    policy: dict[str, Any],
    task: str,
    target_files: list[str],
    *,
    task_type_override: str | None = None,
    max_files: int = 12,
) -> dict[str, Any]:
    classification = classify_task(
        navigation=navigation,
        policy=policy,
        task=task,
        target_files=target_files,
        task_type_override=task_type_override,
    )

    entries = _build_candidate_entries(root, task, navigation, classification, target_files)

    selected: list[CandidateEntry] = []
    total_tokens = 0
    for entry in entries:
        if len(selected) >= max(1, max_files):
            break
        remaining = classification.max_context_tokens - total_tokens
        if remaining <= 0:
            break
        truncated = _truncate_entry(root, entry, remaining)
        if not truncated:
            continue
        selected.append(truncated)
        total_tokens += truncated.token_estimate

    dropped = max(0, len(entries) - len(selected))
    total_candidate_tokens = sum(item.token_estimate for item in entries)

    escalation = {
        "mode": classification.mode,
        "reasons": list(classification.escalation_reasons),
        "logEntry": None,
    }
    if classification.mode == "Deep Review":
        escalation["logEntry"] = {
            "timestamp": now_iso(),
            "taskType": classification.task_type,
            "modelClass": classification.model_class,
            "maxContextTokens": classification.max_context_tokens,
            "reasons": list(classification.escalation_reasons),
            "candidateTokenEstimate": total_candidate_tokens,
        }

    payload = {
        "generatedAt": now_iso(),
        "task": task,
        "inputs": {
            "targetFiles": target_files,
        },
        "classification": {
            "subsystemId": classification.subsystem_id,
            "subsystemTitle": classification.subsystem_title,
            "routeId": classification.route_id,
            "routeTitle": classification.route_title,
            "taskType": classification.task_type,
            "modelClass": classification.model_class,
            "modelRouteId": classification.model_route_id,
            "maxContextTokens": classification.max_context_tokens,
            "mode": classification.mode,
            "escalatedFromModelClass": classification.escalated_from,
        },
        "contextPack": [
            {
                "path": entry.path,
                "lineRange": {"start": entry.line_start, "end": entry.line_end},
                "tokenEstimate": entry.token_estimate,
                "reason": entry.reason,
                "priority": entry.priority,
            }
            for entry in selected
        ],
        "totals": {
            "estimatedTokens": total_tokens,
            "candidateEstimatedTokens": total_candidate_tokens,
            "selectedFiles": len(selected),
            "droppedCandidates": dropped,
            "maxContextTokens": classification.max_context_tokens,
            "withinBudget": total_tokens <= classification.max_context_tokens,
        },
        "escalation": escalation,
    }

    if total_candidate_tokens > classification.max_context_tokens and "candidate context exceeds model-class token budget" not in escalation["reasons"]:
        escalation["reasons"].append("candidate context exceeds model-class token budget")

    return payload


def _append_escalation_log(path: Path, payload: dict[str, Any]) -> None:
    entry = payload.get("escalation", {}).get("logEntry")
    if not isinstance(entry, dict):
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(entry) + "\n")


def main() -> int:
    args = parse_args()

    try:
        task = _resolve_task_text(args)
        target_files = _load_target_files(args)
        navigation = _load_json((args.root / args.navigation_json).resolve() if not args.navigation_json.is_absolute() else args.navigation_json)
        policy = _load_json((args.root / args.routing_policy_json).resolve() if not args.routing_policy_json.is_absolute() else args.routing_policy_json)
        payload = generate_context_pack(
            root=args.root.resolve(),
            navigation=navigation,
            policy=policy,
            task=task,
            target_files=target_files,
            task_type_override=args.task_type,
            max_files=args.max_files,
        )
    except ValueError as exc:
        print(f"ERROR: {exc}")
        return 1

    if args.escalation_log:
        _append_escalation_log(args.escalation_log, payload)

    rendered = json.dumps(payload, indent=2) + "\n"
    if args.json_output:
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        args.json_output.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")

    if args.summary:
        totals = payload["totals"]
        classification = payload["classification"]
        print(
            json.dumps(
                {
                    "mode": classification["mode"],
                    "subsystem": classification["subsystemId"],
                    "route": classification["routeId"],
                    "taskType": classification["taskType"],
                    "modelClass": classification["modelClass"],
                    "selectedFiles": totals["selectedFiles"],
                    "estimatedTokens": totals["estimatedTokens"],
                    "maxContextTokens": totals["maxContextTokens"],
                },
                indent=2,
            )
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
