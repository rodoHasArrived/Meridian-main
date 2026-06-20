#!/usr/bin/env python3
"""Validate and route Meridian repo-local Codex memory entries."""

from __future__ import annotations

import argparse
import fnmatch
import json
import sys
from dataclasses import asdict, dataclass
from datetime import date, datetime, timedelta
from pathlib import Path
from typing import Any, Sequence

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from common import _parse_yaml_subset, load_data, repo_path, write_text_if_changed  # noqa: E402


REPO_ROOT = Path(__file__).resolve().parents[3]
MEMORY_ROOT_REL = ".codex/memory"
INDEX_REL = ".codex/memory/index.yml"
REQUIRED_FIELDS = {
    "id",
    "tier",
    "scope",
    "file",
    "tags",
    "load_when",
    "confidence",
    "freshness",
    "source_refs",
    "review_after",
    "invalidates_when",
}
LOAD_WHEN_LIST_FIELDS = {"skills", "paths", "intents", "branches", "tags"}
LOAD_WHEN_TASK_FIELDS = {"ids", "work_modes", "intents", "paths"}
EXCLUDE_WHEN_FIELDS = {"skills", "paths", "intents", "branches", "tags", "task_ids"}
TASK_DESCRIPTOR_REQUIRED_FIELDS = {
    "version",
    "task_id",
    "intent",
    "selected_skill",
    "work_mode",
    "branch",
    "planned_paths",
    "memory_tags",
    "success_criteria",
}
GOAL_INVENTORY_REQUIRED_FIELDS = {
    "version",
    "goal_id",
    "objective",
    "status",
    "started_at",
    "updated_at",
    "active_task_descriptor",
    "progress_inventory",
    "next_actions",
}
GOAL_PROGRESS_REQUIRED_FIELDS = {"id", "status", "summary", "evidence_refs", "updated_at"}
ACTIVE_TIERS = {"session", "branch", "task", "repo", "archive"}
DISABLED_TIERS = {"user", "global"}
ALLOWED_CONFIDENCE = {"low", "medium", "high"}
ALLOWED_FRESHNESS = {"fresh", "review-soon", "stale", "unknown"}
ALLOWED_GOAL_STATUS = {"active", "blocked", "complete", "abandoned"}
ALLOWED_PROGRESS_STATUS = {"pending", "in_progress", "completed", "blocked", "deferred"}
TIER_PRECEDENCE = {"task": 1, "branch": 2, "repo": 3, "session": 4, "archive": 5}


@dataclass(frozen=True)
class Finding:
    severity: str
    path: str
    message: str


@dataclass(frozen=True)
class RoutingContext:
    paths: tuple[str, ...] = ()
    explicit_tags: tuple[str, ...] = ()
    descriptor_tags: tuple[str, ...] = ()
    skills: tuple[str, ...] = ()
    intents: tuple[str, ...] = ()
    branches: tuple[str, ...] = ()
    task_id: str | None = None
    work_mode: str | None = None


@dataclass(frozen=True)
class RoutingDecision:
    id: str
    tier: str
    scope: str
    file: str
    selected: bool
    reasons: tuple[str, ...]
    skipped_reasons: tuple[str, ...]
    warnings: tuple[str, ...]
    precedence: int | None
    task_scope_conflict: bool = False


def normalize_path(value: str | Path) -> str:
    return str(value).replace("\\", "/")


def rel(root: Path, path: Path | str) -> str:
    return repo_path(path, root)


def parse_yaml_text(text: str) -> Any:
    try:
        import yaml  # type: ignore

        return yaml.safe_load(text) or {}
    except Exception:
        return _parse_yaml_subset(text)


def dump_yaml(data: Any, indent: int = 0) -> str:
    try:
        import yaml  # type: ignore

        return yaml.safe_dump(data, sort_keys=False, allow_unicode=False)
    except Exception:
        lines: list[str] = []
        prefix = " " * indent
        if isinstance(data, dict):
            for key, value in data.items():
                if isinstance(value, (dict, list)):
                    lines.append(f"{prefix}{key}:")
                    lines.append(dump_yaml(value, indent + 2).rstrip())
                else:
                    lines.append(f"{prefix}{key}: {format_scalar(value)}")
        elif isinstance(data, list):
            for item in data:
                if isinstance(item, (dict, list)):
                    lines.append(f"{prefix}-")
                    lines.append(dump_yaml(item, indent + 2).rstrip())
                else:
                    lines.append(f"{prefix}- {format_scalar(item)}")
        else:
            lines.append(f"{prefix}{format_scalar(data)}")
        return "\n".join(lines) + "\n"


def format_scalar(value: Any) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "true" if value else "false"
    text = str(value)
    if not text or any(char in text for char in ":#[]{}*,&!|>'\"%@`"):
        return json.dumps(text)
    return text


def parse_front_matter(path: Path) -> tuple[dict[str, Any], list[Finding]]:
    text = path.read_text(encoding="utf-8", errors="replace")
    display_path = rel(REPO_ROOT, path)
    if not text.startswith("---\n"):
        return {}, [Finding("error", display_path, "Memory entry is missing YAML front matter.")]

    end = text.find("\n---", 4)
    if end == -1:
        return {}, [Finding("error", display_path, "Memory entry front matter is missing a closing delimiter.")]

    front_matter_text = text[4:end]
    parsed = parse_yaml_text(front_matter_text)
    if not isinstance(parsed, dict):
        return {}, [Finding("error", display_path, "Memory entry front matter must be a mapping.")]
    return parsed, []


def safe_repo_relative_path(root: Path, raw_path: str, finding_path: str) -> tuple[Path | None, list[Finding]]:
    findings: list[Finding] = []
    normalized = normalize_path(raw_path)
    if not normalized or normalized.startswith("/") or Path(normalized).is_absolute():
        findings.append(Finding("error", finding_path, f"Path must be repo-relative: {raw_path}"))
        return None, findings
    if any(part == ".." for part in Path(normalized).parts):
        findings.append(Finding("error", finding_path, f"Path must not contain '..': {raw_path}"))
        return None, findings
    resolved = (root / normalized).resolve()
    try:
        resolved.relative_to(root.resolve())
    except ValueError:
        findings.append(Finding("error", finding_path, f"Path escapes repository root: {raw_path}"))
        return None, findings
    return resolved, findings


def safe_memory_path(root: Path, raw_path: str, finding_path: str) -> tuple[Path | None, list[Finding]]:
    path, findings = safe_repo_relative_path(root, raw_path, finding_path)
    if path is None:
        return None, findings
    memory_root = (root / MEMORY_ROOT_REL).resolve()
    try:
        path.relative_to(memory_root)
    except ValueError:
        findings.append(Finding("error", finding_path, f"Memory file must live under {MEMORY_ROOT_REL}: {raw_path}"))
        return None, findings
    return path, findings


def validate_string_list(value: Any, field: str, path: str) -> list[Finding]:
    if not isinstance(value, list):
        return [Finding("error", path, f"{field} must be a list.")]
    if any(not isinstance(item, str) or not item.strip() for item in value):
        return [Finding("error", path, f"{field} must contain non-empty strings.")]
    return []


def validate_optional_string_list(value: Any, field: str, path: str) -> list[Finding]:
    if value is None:
        return []
    return validate_string_list(value, field, path)


def validate_scope(entry: dict[str, Any], path: str) -> list[Finding]:
    tier = entry.get("tier")
    scope = entry.get("scope")
    if not isinstance(scope, str) or not scope:
        return [Finding("error", path, "scope must be a non-empty string.")]
    if tier == "repo" and scope != "repo":
        return [Finding("error", path, "repo-tier memory must use scope 'repo'.")]
    if tier == "branch" and not scope.startswith("branch:"):
        return [Finding("error", path, "branch-tier memory scope must start with 'branch:'.")]
    if tier == "task" and not scope.startswith("task:"):
        return [Finding("error", path, "task-tier memory scope must start with 'task:'.")]
    if tier == "session" and not scope.startswith("session:"):
        return [Finding("error", path, "session-tier memory scope must start with 'session:'.")]
    if tier == "archive" and scope not in {"archive"} and not scope.startswith("archive:"):
        return [Finding("error", path, "archive-tier memory scope must be 'archive' or start with 'archive:'.")]
    return []


def validate_review_after(value: Any, path: str) -> list[Finding]:
    if isinstance(value, datetime):
        review_date = value.date()
        display_value = review_date.isoformat()
    elif isinstance(value, date):
        review_date = value
        display_value = value.isoformat()
    elif isinstance(value, str):
        try:
            review_date = date.fromisoformat(value)
        except ValueError:
            return [Finding("error", path, f"review_after is not a valid ISO date: {value}")]
        display_value = value
    else:
        return [Finding("error", path, "review_after must be an ISO date string.")]
    if review_date < date.today():
        return [Finding("warning", path, f"Memory review date has passed: {display_value}")]
    return []


def validate_iso_timestamp(value: Any, field: str, path: str) -> list[Finding]:
    if isinstance(value, datetime):
        return []
    if isinstance(value, date):
        return []
    if not isinstance(value, str) or not value.strip():
        return [Finding("error", path, f"{field} must be an ISO date or datetime string.")]
    try:
        datetime.fromisoformat(value.replace("Z", "+00:00"))
        return []
    except ValueError:
        try:
            date.fromisoformat(value)
            return []
        except ValueError:
            return [Finding("error", path, f"{field} is not a valid ISO date or datetime: {value}")]


def validate_source_refs(root: Path, entry: dict[str, Any], path: str) -> list[Finding]:
    findings: list[Finding] = []
    source_refs = entry.get("source_refs")
    findings.extend(validate_string_list(source_refs, "source_refs", path))
    if findings:
        return findings
    assert isinstance(source_refs, list)

    for source_ref in source_refs:
        if "://" in source_ref or source_ref.startswith("#"):
            continue
        source_path, path_findings = safe_repo_relative_path(root, source_ref, path)
        findings.extend(path_findings)
        if source_path is not None and not source_path.exists():
            findings.append(Finding("error", path, f"source_ref does not exist: {source_ref}"))
    return findings


def validate_load_when(entry: dict[str, Any], path: str) -> list[Finding]:
    load_when = entry.get("load_when")
    if not isinstance(load_when, dict):
        return [Finding("error", path, "load_when must be a mapping.")]
    findings: list[Finding] = []
    allowed_fields = LOAD_WHEN_LIST_FIELDS | {"task"}
    for field in sorted(set(load_when) - allowed_fields):
        findings.append(Finding("error", path, f"load_when has unknown selector: {field}"))
    for field in LOAD_WHEN_LIST_FIELDS:
        if field not in load_when:
            findings.append(Finding("error", path, f"load_when is missing {field}."))
            continue
        findings.extend(validate_string_list(load_when.get(field), f"load_when.{field}", path))
    task_selectors = load_when.get("task")
    if not isinstance(task_selectors, dict):
        findings.append(Finding("error", path, "load_when.task must be a mapping."))
    else:
        for field in sorted(set(task_selectors) - LOAD_WHEN_TASK_FIELDS):
            findings.append(Finding("error", path, f"load_when.task has unknown selector: {field}"))
        for field in LOAD_WHEN_TASK_FIELDS:
            if field not in task_selectors:
                findings.append(Finding("error", path, f"load_when.task is missing {field}."))
                continue
            findings.extend(validate_string_list(task_selectors.get(field), f"load_when.task.{field}", path))
    if entry.get("tier") == "task" and isinstance(task_selectors, dict):
        task_ids = task_selectors.get("ids")
        scope = entry.get("scope", "")
        scope_id = scope.split(":", 1)[1] if isinstance(scope, str) and scope.startswith("task:") else ""
        if isinstance(task_ids, list) and task_ids and scope_id not in task_ids:
            findings.append(Finding("error", path, "task-tier load_when.task.ids must include the task scope id."))
    if entry.get("tier") == "branch":
        branch_ids = load_when.get("branches")
        scope = entry.get("scope", "")
        scope_id = scope.split(":", 1)[1] if isinstance(scope, str) and scope.startswith("branch:") else ""
        if isinstance(branch_ids, list):
            if not branch_ids:
                findings.append(Finding("error", path, "branch-tier load_when.branches must name its branch scope."))
            elif scope_id not in branch_ids:
                findings.append(Finding("error", path, "branch-tier load_when.branches must include the branch scope id."))
    return findings


def validate_exclude_when(entry: dict[str, Any], path: str) -> list[Finding]:
    exclude_when = entry.get("exclude_when")
    if exclude_when is None:
        return []
    if not isinstance(exclude_when, dict):
        return [Finding("error", path, "exclude_when must be a mapping.")]
    findings: list[Finding] = []
    for field in sorted(set(exclude_when) - EXCLUDE_WHEN_FIELDS):
        findings.append(Finding("error", path, f"exclude_when has unknown selector: {field}"))
    for field in EXCLUDE_WHEN_FIELDS:
        findings.extend(validate_optional_string_list(exclude_when.get(field), f"exclude_when.{field}", path))
    return findings


def validate_entry_shape(root: Path, entry: Any, index_path: Path, seen_ids: set[str]) -> tuple[dict[str, Any] | None, list[Finding]]:
    path = rel(root, index_path)
    findings: list[Finding] = []
    if not isinstance(entry, dict):
        return None, [Finding("error", path, "Each memory index entry must be a mapping.")]

    entry_id = entry.get("id", "<missing-id>")
    finding_path = f"{path}#{entry_id}"

    missing = sorted(REQUIRED_FIELDS - set(entry))
    for field in missing:
        findings.append(Finding("error", finding_path, f"Memory index entry is missing {field}."))
    if missing:
        return entry, findings

    if not isinstance(entry.get("id"), str) or not entry["id"].strip():
        findings.append(Finding("error", finding_path, "id must be a non-empty string."))
    elif entry["id"] in seen_ids:
        findings.append(Finding("error", finding_path, f"Duplicate memory id: {entry['id']}"))
    else:
        seen_ids.add(entry["id"])

    tier = entry.get("tier")
    if tier in DISABLED_TIERS:
        findings.append(Finding("error", finding_path, f"Disabled tier is not enabled for repo-local memory: {tier}"))
    elif tier not in ACTIVE_TIERS:
        findings.append(Finding("error", finding_path, f"Unknown memory tier: {tier}"))

    findings.extend(validate_scope(entry, finding_path))
    findings.extend(validate_string_list(entry.get("tags"), "tags", finding_path))
    findings.extend(validate_string_list(entry.get("invalidates_when"), "invalidates_when", finding_path))
    findings.extend(validate_load_when(entry, finding_path))
    findings.extend(validate_exclude_when(entry, finding_path))
    findings.extend(validate_source_refs(root, entry, finding_path))
    findings.extend(validate_review_after(entry.get("review_after"), finding_path))

    if entry.get("confidence") not in ALLOWED_CONFIDENCE:
        findings.append(Finding("error", finding_path, f"Unknown confidence: {entry.get('confidence')}"))
    if entry.get("freshness") not in ALLOWED_FRESHNESS:
        findings.append(Finding("error", finding_path, f"Unknown freshness: {entry.get('freshness')}"))
    if entry.get("freshness") in {"stale", "unknown"}:
        findings.append(Finding("warning", finding_path, f"Memory freshness is {entry.get('freshness')}."))

    raw_file = entry.get("file")
    if not isinstance(raw_file, str):
        findings.append(Finding("error", finding_path, "file must be a string."))
        return entry, findings

    memory_file, file_findings = safe_memory_path(root, raw_file, finding_path)
    findings.extend(file_findings)
    if memory_file is None:
        return entry, findings
    if memory_file.name.lower() == "readme.md":
        findings.append(Finding("error", finding_path, "Folder README files are guidance and must not be indexed."))
    if not memory_file.exists():
        findings.append(Finding("error", finding_path, f"Memory file does not exist: {raw_file}"))
        return entry, findings

    front_matter, front_findings = parse_front_matter(memory_file)
    findings.extend(front_findings)
    if front_findings:
        return entry, findings

    for field in REQUIRED_FIELDS:
        if field not in front_matter:
            findings.append(Finding("error", rel(root, memory_file), f"Memory front matter is missing {field}."))
    for field in ("id", "tier", "scope", "file", "confidence", "freshness", "review_after"):
        front_value = normalize_metadata_value(front_matter.get(field))
        entry_value = normalize_metadata_value(entry.get(field))
        if field in front_matter and front_value != entry_value:
            findings.append(
                Finding(
                    "error",
                    rel(root, memory_file),
                    f"Front matter {field} does not match index value {entry_value!r}.",
                )
            )
    return entry, findings


def normalize_metadata_value(value: Any) -> Any:
    if isinstance(value, datetime):
        return value.date().isoformat()
    if isinstance(value, date):
        return value.isoformat()
    return value


def load_index(root: Path) -> tuple[dict[str, Any] | None, Path, list[Finding]]:
    index_path = root / INDEX_REL
    if not index_path.is_file():
        return None, index_path, [Finding("error", INDEX_REL, "Codex memory index is missing.")]
    try:
        data = load_data(index_path)
    except Exception as exc:
        return None, index_path, [Finding("error", INDEX_REL, f"Unable to parse memory index: {exc}")]
    if not isinstance(data, dict):
        return None, index_path, [Finding("error", INDEX_REL, "Codex memory index must be a YAML mapping.")]
    return data, index_path, []


def validate_task_descriptor(descriptor: Any, display_path: str) -> tuple[dict[str, Any] | None, list[Finding]]:
    findings: list[Finding] = []
    if not isinstance(descriptor, dict):
        return None, [Finding("error", display_path, "Task descriptor must be a YAML mapping.")]

    missing = sorted(TASK_DESCRIPTOR_REQUIRED_FIELDS - set(descriptor))
    for field in missing:
        findings.append(Finding("error", display_path, f"Task descriptor is missing {field}."))
    if missing:
        return descriptor, findings

    version = descriptor.get("version")
    if version not in {1, "1"}:
        findings.append(Finding("error", display_path, f"Task descriptor version must be 1: {version}"))
    for field in ("task_id", "intent", "selected_skill", "work_mode", "branch"):
        if not isinstance(descriptor.get(field), str) or not descriptor[field].strip():
            findings.append(Finding("error", display_path, f"{field} must be a non-empty string."))
    for field in ("planned_paths", "memory_tags", "success_criteria"):
        findings.extend(validate_string_list(descriptor.get(field), field, display_path))

    promotion_candidates = descriptor.get("promotion_candidates", [])
    if not isinstance(promotion_candidates, list):
        findings.append(Finding("error", display_path, "promotion_candidates must be a list when present."))
    elif any(not isinstance(item, (dict, str)) for item in promotion_candidates):
        findings.append(Finding("error", display_path, "promotion_candidates must contain mappings or strings."))
    return descriptor, findings


def load_task_descriptor(root: Path, raw_path: str) -> tuple[dict[str, Any] | None, list[Finding]]:
    descriptor_path, findings = safe_memory_path(root, raw_path, raw_path)
    if descriptor_path is None:
        return None, findings
    tasks_root = (root / MEMORY_ROOT_REL / "tasks").resolve()
    try:
        descriptor_path.relative_to(tasks_root)
    except ValueError:
        findings.append(Finding("error", raw_path, f"Task descriptor must live under {MEMORY_ROOT_REL}/tasks."))
        return None, findings
    if descriptor_path.suffix.lower() not in {".yml", ".yaml"}:
        findings.append(Finding("error", rel(root, descriptor_path), "Task descriptor must be a .yml or .yaml file."))
        return None, findings
    if not descriptor_path.is_file():
        findings.append(Finding("error", rel(root, descriptor_path), "Task descriptor does not exist."))
        return None, findings
    try:
        descriptor = load_data(descriptor_path)
    except Exception as exc:
        findings.append(Finding("error", rel(root, descriptor_path), f"Unable to parse task descriptor: {exc}"))
        return None, findings
    return validate_task_descriptor(descriptor, rel(root, descriptor_path))


def validate_goal_progress_item(root: Path, item: Any, display_path: str, index: int) -> list[Finding]:
    path = f"{display_path}#progress_inventory[{index}]"
    findings: list[Finding] = []
    if not isinstance(item, dict):
        return [Finding("error", path, "Progress inventory item must be a mapping.")]
    missing = sorted(GOAL_PROGRESS_REQUIRED_FIELDS - set(item))
    for field in missing:
        findings.append(Finding("error", path, f"Progress inventory item is missing {field}."))
    if missing:
        return findings
    for field in ("id", "summary"):
        if not isinstance(item.get(field), str) or not item[field].strip():
            findings.append(Finding("error", path, f"{field} must be a non-empty string."))
    if item.get("status") not in ALLOWED_PROGRESS_STATUS:
        findings.append(Finding("error", path, f"Unknown progress status: {item.get('status')}"))
    findings.extend(validate_string_list(item.get("evidence_refs"), "evidence_refs", path))
    if isinstance(item.get("evidence_refs"), list):
        findings.extend(validate_source_refs(root, {"source_refs": item.get("evidence_refs")}, path))
    findings.extend(validate_iso_timestamp(item.get("updated_at"), "updated_at", path))
    return findings


def validate_goal_inventory(root: Path, goal: Any, display_path: str) -> tuple[dict[str, Any] | None, list[Finding]]:
    findings: list[Finding] = []
    if not isinstance(goal, dict):
        return None, [Finding("error", display_path, "Goal inventory must be a YAML mapping.")]

    missing = sorted(GOAL_INVENTORY_REQUIRED_FIELDS - set(goal))
    for field in missing:
        findings.append(Finding("error", display_path, f"Goal inventory is missing {field}."))
    if missing:
        return goal, findings

    version = goal.get("version")
    if version not in {1, "1"}:
        findings.append(Finding("error", display_path, f"Goal inventory version must be 1: {version}"))
    for field in ("goal_id", "objective", "active_task_descriptor"):
        if not isinstance(goal.get(field), str) or not goal[field].strip():
            findings.append(Finding("error", display_path, f"{field} must be a non-empty string."))
    if goal.get("status") not in ALLOWED_GOAL_STATUS:
        findings.append(Finding("error", display_path, f"Unknown goal status: {goal.get('status')}"))
    findings.extend(validate_iso_timestamp(goal.get("started_at"), "started_at", display_path))
    findings.extend(validate_iso_timestamp(goal.get("updated_at"), "updated_at", display_path))
    findings.extend(validate_string_list(goal.get("next_actions"), "next_actions", display_path))
    findings.extend(validate_optional_string_list(goal.get("open_questions"), "open_questions", display_path))

    active_task = goal.get("active_task_descriptor")
    if isinstance(active_task, str):
        task_path, task_findings = safe_memory_path(root, active_task, display_path)
        findings.extend(task_findings)
        tasks_root = (root / MEMORY_ROOT_REL / "tasks").resolve()
        if task_path is not None:
            try:
                task_path.relative_to(tasks_root)
            except ValueError:
                findings.append(Finding("error", display_path, "active_task_descriptor must live under .codex/memory/tasks."))
            if task_path.suffix.lower() not in {".yml", ".yaml"}:
                findings.append(Finding("error", display_path, "active_task_descriptor must point to a task descriptor YAML file."))
            if not task_path.is_file():
                findings.append(Finding("error", display_path, f"active_task_descriptor does not exist: {active_task}"))

    progress_inventory = goal.get("progress_inventory")
    if not isinstance(progress_inventory, list):
        findings.append(Finding("error", display_path, "progress_inventory must be a list."))
    else:
        for index, item in enumerate(progress_inventory):
            findings.extend(validate_goal_progress_item(root, item, display_path, index))

    promotion_candidates = goal.get("promotion_candidates", [])
    if not isinstance(promotion_candidates, list):
        findings.append(Finding("error", display_path, "promotion_candidates must be a list when present."))
    elif any(not isinstance(item, (dict, str)) for item in promotion_candidates):
        findings.append(Finding("error", display_path, "promotion_candidates must contain mappings or strings."))
    return goal, findings


def load_goal_inventory(root: Path, raw_path: str) -> tuple[dict[str, Any] | None, list[Finding]]:
    goal_path, findings = safe_memory_path(root, raw_path, raw_path)
    if goal_path is None:
        return None, findings
    goals_root = (root / MEMORY_ROOT_REL / "goals").resolve()
    try:
        goal_path.relative_to(goals_root)
    except ValueError:
        findings.append(Finding("error", raw_path, f"Goal inventory must live under {MEMORY_ROOT_REL}/goals."))
        return None, findings
    if goal_path.suffix.lower() not in {".yml", ".yaml"}:
        findings.append(Finding("error", rel(root, goal_path), "Goal inventory must be a .yml or .yaml file."))
        return None, findings
    if not goal_path.is_file():
        findings.append(Finding("error", rel(root, goal_path), "Goal inventory does not exist."))
        return None, findings
    try:
        goal = load_data(goal_path)
    except Exception as exc:
        findings.append(Finding("error", rel(root, goal_path), f"Unable to parse goal inventory: {exc}"))
        return None, findings
    return validate_goal_inventory(root, goal, rel(root, goal_path))


def collect_unindexed_files(root: Path, entries: Sequence[dict[str, Any]]) -> list[Finding]:
    memory_root = root / MEMORY_ROOT_REL
    if not memory_root.exists():
        return []
    indexed = {normalize_path(entry["file"]) for entry in entries if isinstance(entry.get("file"), str)}
    findings: list[Finding] = []
    for path in sorted(memory_root.rglob("*")):
        if not path.is_file():
            continue
        rel_path = rel(root, path)
        name = path.name.lower()
        if rel_path == INDEX_REL or name == "readme.md":
            continue
        if path.parent.resolve() == (memory_root / "tasks").resolve() and path.suffix.lower() in {".yml", ".yaml"}:
            continue
        if path.parent.resolve() == (memory_root / "goals").resolve() and path.suffix.lower() in {".yml", ".yaml"}:
            continue
        if normalize_path(rel_path) not in indexed:
            findings.append(Finding("error", rel_path, "Memory file is not listed in .codex/memory/index.yml."))
    return findings


def collect_findings(root: Path) -> tuple[list[Finding], list[dict[str, Any]]]:
    root = root.resolve()
    data, index_path, findings = load_index(root)
    if data is None:
        return findings, []

    if data.get("memory_root") != MEMORY_ROOT_REL:
        findings.append(Finding("error", INDEX_REL, f"memory_root must be {MEMORY_ROOT_REL}."))

    entries_raw = data.get("entries")
    if not isinstance(entries_raw, list):
        findings.append(Finding("error", INDEX_REL, "entries must be a list."))
        return findings, []

    entries: list[dict[str, Any]] = []
    seen_ids: set[str] = set()
    for raw_entry in entries_raw:
        entry, entry_findings = validate_entry_shape(root, raw_entry, index_path, seen_ids)
        findings.extend(entry_findings)
        if entry is not None:
            entries.append(entry)

    findings.extend(collect_unindexed_files(root, entries))
    return sorted(findings, key=lambda finding: (finding.severity, finding.path, finding.message)), entries


def entry_is_stale(entry: dict[str, Any]) -> bool:
    if entry.get("freshness") in {"stale", "unknown"}:
        return True
    review_after = entry.get("review_after")
    try:
        if isinstance(review_after, datetime):
            return review_after.date() < date.today()
        if isinstance(review_after, date):
            return review_after < date.today()
        if not isinstance(review_after, str):
            return False
        return date.fromisoformat(review_after) < date.today()
    except ValueError:
        return False


def string_values(value: Any) -> list[str]:
    if not isinstance(value, list):
        return []
    return [item for item in value if isinstance(item, str) and item.strip()]


def unique_strings(values: Sequence[str]) -> tuple[str, ...]:
    seen: set[str] = set()
    result: list[str] = []
    for value in values:
        if value in seen:
            continue
        seen.add(value)
        result.append(value)
    return tuple(result)


def load_when_list(entry: dict[str, Any], field: str) -> list[str]:
    load_when = entry.get("load_when")
    if not isinstance(load_when, dict):
        return []
    return string_values(load_when.get(field))


def load_when_task_list(entry: dict[str, Any], field: str) -> list[str]:
    load_when = entry.get("load_when")
    if not isinstance(load_when, dict):
        return []
    task_selectors = load_when.get("task")
    if not isinstance(task_selectors, dict):
        return []
    return string_values(task_selectors.get(field))


def matching_values(candidates: Sequence[str], requested: Sequence[str]) -> tuple[str, ...]:
    requested_set = set(requested)
    return tuple(value for value in candidates if value in requested_set)


def matching_path_reasons(patterns: Sequence[str], paths: Sequence[str]) -> tuple[str, ...]:
    normalized_paths = [normalize_path(path) for path in paths]
    reasons: list[str] = []
    for path in normalized_paths:
        for pattern in patterns:
            if fnmatch.fnmatchcase(path, pattern):
                reasons.append(f"{path} matches {pattern}")
    return tuple(reasons)


def matches_paths(entry: dict[str, Any], paths: Sequence[str]) -> bool:
    return bool(matching_path_reasons(load_when_list(entry, "paths"), paths))


def matches_tags(entry: dict[str, Any], tags: Sequence[str]) -> bool:
    if not tags:
        return False
    requested = set(tags)
    entry_tags = set(entry.get("tags") if isinstance(entry.get("tags"), list) else [])
    load_tags = set(load_when_list(entry, "tags"))
    return bool(requested & (entry_tags | load_tags))


def build_routing_context(
    paths: Sequence[str] | None = None,
    tags: Sequence[str] | None = None,
    task_descriptor: dict[str, Any] | None = None,
    skills: Sequence[str] | None = None,
    intents: Sequence[str] | None = None,
    branches: Sequence[str] | None = None,
) -> RoutingContext:
    descriptor_paths: list[str] = []
    descriptor_tags: list[str] = []
    descriptor_skills: list[str] = []
    descriptor_intents: list[str] = []
    descriptor_branches: list[str] = []
    task_id: str | None = None
    work_mode: str | None = None

    if task_descriptor:
        descriptor_paths = string_values(task_descriptor.get("planned_paths"))
        descriptor_tags = string_values(task_descriptor.get("memory_tags"))
        selected_skill = task_descriptor.get("selected_skill")
        if isinstance(selected_skill, str) and selected_skill.strip():
            descriptor_skills = [selected_skill]
        intent = task_descriptor.get("intent")
        if isinstance(intent, str) and intent.strip():
            descriptor_intents = [intent]
        branch = task_descriptor.get("branch")
        if isinstance(branch, str) and branch.strip():
            descriptor_branches = [branch]
        raw_task_id = task_descriptor.get("task_id")
        if isinstance(raw_task_id, str) and raw_task_id.strip():
            task_id = raw_task_id
        raw_work_mode = task_descriptor.get("work_mode")
        if isinstance(raw_work_mode, str) and raw_work_mode.strip():
            work_mode = raw_work_mode

    return RoutingContext(
        paths=unique_strings([*list_arg(paths), *descriptor_paths]),
        explicit_tags=unique_strings(list_arg(tags)),
        descriptor_tags=unique_strings(descriptor_tags),
        skills=unique_strings([*list_arg(skills), *descriptor_skills]),
        intents=unique_strings([*list_arg(intents), *descriptor_intents]),
        branches=unique_strings([*list_arg(branches), *descriptor_branches]),
        task_id=task_id,
        work_mode=work_mode,
    )


def context_has_routing_filters(context: RoutingContext) -> bool:
    return bool(
        context.paths
        or context.explicit_tags
        or context.descriptor_tags
        or context.skills
        or context.intents
        or context.branches
        or context.task_id
        or context.work_mode
    )


def task_scope_id(entry: dict[str, Any]) -> str | None:
    scope = entry.get("scope")
    if isinstance(scope, str) and scope.startswith("task:"):
        return scope.split(":", 1)[1]
    return None


def branch_scope_id(entry: dict[str, Any]) -> str | None:
    scope = entry.get("scope")
    if isinstance(scope, str) and scope.startswith("branch:"):
        return scope.split(":", 1)[1]
    return None


def exclusion_reasons(entry: dict[str, Any], context: RoutingContext) -> tuple[str, ...]:
    exclude_when = entry.get("exclude_when")
    if not isinstance(exclude_when, dict):
        return ()
    reasons: list[str] = []
    for value in matching_values(string_values(exclude_when.get("skills")), context.skills):
        reasons.append(f"excluded by skill {value}")
    for value in matching_values(string_values(exclude_when.get("intents")), context.intents):
        reasons.append(f"excluded by intent {value}")
    for value in matching_values(string_values(exclude_when.get("branches")), context.branches):
        reasons.append(f"excluded by branch {value}")
    all_tags = (*context.explicit_tags, *context.descriptor_tags)
    for value in matching_values(string_values(exclude_when.get("tags")), all_tags):
        reasons.append(f"excluded by tag {value}")
    for reason in matching_path_reasons(string_values(exclude_when.get("paths")), context.paths):
        reasons.append(f"excluded by path {reason}")
    if context.task_id:
        for value in matching_values(string_values(exclude_when.get("task_ids")), [context.task_id]):
            reasons.append(f"excluded by task {value}")
    return tuple(reasons)


def task_match_reasons(entry: dict[str, Any], context: RoutingContext) -> tuple[str, ...]:
    reasons: list[str] = []
    if context.task_id:
        scope_id = task_scope_id(entry)
        if scope_id == context.task_id:
            reasons.append(f"task scope matches {context.task_id}")
        for value in matching_values(load_when_task_list(entry, "ids"), [context.task_id]):
            reasons.append(f"task id matches {value}")
    if context.work_mode:
        for value in matching_values(load_when_task_list(entry, "work_modes"), [context.work_mode]):
            reasons.append(f"task work mode matches {value}")
    for value in matching_values(load_when_task_list(entry, "intents"), context.intents):
        reasons.append(f"task intent matches {value}")
    for reason in matching_path_reasons(load_when_task_list(entry, "paths"), context.paths):
        reasons.append(f"task planned path {reason}")
    return tuple(reasons)


def positive_match_reasons(entry: dict[str, Any], context: RoutingContext) -> tuple[tuple[str, int], ...]:
    matches: list[tuple[str, int]] = []
    explicit_tags = context.explicit_tags
    if explicit_tags and matches_tags(entry, explicit_tags):
        matched_tags = sorted(set(explicit_tags) & (set(string_values(entry.get("tags"))) | set(load_when_list(entry, "tags"))))
        matches.extend((f"explicit tag matches {tag}", 0) for tag in matched_tags)
    task_reasons = task_match_reasons(entry, context)
    matches.extend((reason, 1) for reason in task_reasons)
    for value in matching_values(load_when_list(entry, "branches"), context.branches):
        matches.append((f"branch matches {value}", 2))
    for value in matching_values(load_when_list(entry, "skills"), context.skills):
        matches.append((f"skill matches {value}", 3))
    for value in matching_values(load_when_list(entry, "intents"), context.intents):
        matches.append((f"intent matches {value}", 3))
    for reason in matching_path_reasons(load_when_list(entry, "paths"), context.paths):
        matches.append((f"path {reason}", 3))
    for value in matching_values(load_when_list(entry, "tags"), context.descriptor_tags):
        matches.append((f"task memory tag matches {value}", 3))
    return tuple(matches)


def decide_entry(entry: dict[str, Any], context: RoutingContext, stale_only: bool) -> RoutingDecision:
    entry_id = str(entry.get("id", "<missing-id>"))
    tier = str(entry.get("tier", "<missing-tier>"))
    scope = str(entry.get("scope", "<missing-scope>"))
    file = str(entry.get("file", "<missing-file>"))
    warnings: list[str] = []
    skipped: list[str] = []
    task_conflict = False

    if entry_is_stale(entry):
        warnings.append("entry is stale or past review_after")
    elif stale_only:
        skipped.append("entry is not stale")

    if tier == "archive" and context_has_routing_filters(context):
        skipped.append("archive entries are audit-only")

    scope_id = task_scope_id(entry)
    task_ids = load_when_task_list(entry, "ids")
    if tier == "task":
        if not context.task_id:
            skipped.append("task-tier entry requires --task descriptor")
            task_conflict = True
        elif scope_id != context.task_id and context.task_id not in task_ids:
            skipped.append(f"task scope mismatch: entry is {scope}, descriptor is task:{context.task_id}")
            task_conflict = True
    if tier == "branch":
        branch_id = branch_scope_id(entry)
        branch_selectors = set(load_when_list(entry, "branches"))
        active_branches = set(context.branches)
        if not active_branches:
            skipped.append("branch-tier entry requires an active branch selector")
        elif branch_id not in active_branches and not (branch_selectors & active_branches):
            skipped.append(f"branch scope mismatch: entry is {scope}, active branches are {', '.join(context.branches)}")

    skipped.extend(exclusion_reasons(entry, context))

    positive_matches = positive_match_reasons(entry, context)
    if not context_has_routing_filters(context):
        positive_matches = (("audit mode: no routing filters supplied", TIER_PRECEDENCE.get(tier, 9)),)
    if not positive_matches:
        skipped.append("no routing selector matched")

    selected = not skipped and bool(positive_matches)
    precedence = min((precedence for _, precedence in positive_matches), default=None)
    return RoutingDecision(
        id=entry_id,
        tier=tier,
        scope=scope,
        file=file,
        selected=selected,
        reasons=tuple(reason for reason, _ in positive_matches) if selected else (),
        skipped_reasons=tuple(skipped),
        warnings=tuple(warnings),
        precedence=precedence,
        task_scope_conflict=task_conflict,
    )


def route_entries(
    entries: Sequence[dict[str, Any]],
    context: RoutingContext,
    stale_only: bool = False,
) -> tuple[list[dict[str, Any]], list[RoutingDecision]]:
    decisions = [decide_entry(entry, context, stale_only) for entry in entries]
    selected_by_id = {decision.id for decision in decisions if decision.selected}
    selected = [entry for entry in entries if entry.get("id") in selected_by_id]
    selected.sort(
        key=lambda entry: (
            next((decision.precedence for decision in decisions if decision.id == entry.get("id")), None) or 99,
            TIER_PRECEDENCE.get(str(entry.get("tier")), 99),
            str(entry.get("id")),
        )
    )
    return selected, decisions


def select_entries(
    entries: Sequence[dict[str, Any]],
    paths: Sequence[str],
    tags: Sequence[str],
    stale_only: bool,
    task_descriptor: dict[str, Any] | None = None,
    skills: Sequence[str] | None = None,
    intents: Sequence[str] | None = None,
    branches: Sequence[str] | None = None,
) -> list[dict[str, Any]]:
    context = build_routing_context(paths, tags, task_descriptor, skills, intents, branches)
    selected, _ = route_entries(entries, context, stale_only)
    return selected
    for entry in entries:
        if stale_only and not entry_is_stale(entry):
            continue
        if has_filters and not (matches_paths(entry, paths) or matches_tags(entry, tags)):
            continue
        selected.append(entry)
    return selected


def task_descriptor_summary(task_descriptor: dict[str, Any] | None) -> dict[str, Any] | None:
    if not task_descriptor:
        return None
    return {
        "task_id": task_descriptor.get("task_id"),
        "intent": task_descriptor.get("intent"),
        "selected_skill": task_descriptor.get("selected_skill"),
        "work_mode": task_descriptor.get("work_mode"),
        "branch": task_descriptor.get("branch"),
        "planned_paths": string_values(task_descriptor.get("planned_paths")),
        "memory_tags": string_values(task_descriptor.get("memory_tags")),
        "promotion_candidate_count": len(task_descriptor.get("promotion_candidates", []))
        if isinstance(task_descriptor.get("promotion_candidates", []), list)
        else 0,
    }


def goal_inventory_summary(goal_inventory: dict[str, Any] | None) -> dict[str, Any] | None:
    if not goal_inventory:
        return None
    progress = goal_inventory.get("progress_inventory", [])
    progress_items = progress if isinstance(progress, list) else []
    completed = sum(1 for item in progress_items if isinstance(item, dict) and item.get("status") == "completed")
    blocked = sum(1 for item in progress_items if isinstance(item, dict) and item.get("status") == "blocked")
    return {
        "goal_id": goal_inventory.get("goal_id"),
        "objective": goal_inventory.get("objective"),
        "status": goal_inventory.get("status"),
        "started_at": normalize_metadata_value(goal_inventory.get("started_at")),
        "updated_at": normalize_metadata_value(goal_inventory.get("updated_at")),
        "active_task_descriptor": goal_inventory.get("active_task_descriptor"),
        "progress_count": len(progress_items),
        "completed_count": completed,
        "blocked_count": blocked,
        "next_action_count": len(goal_inventory.get("next_actions", []))
        if isinstance(goal_inventory.get("next_actions"), list)
        else 0,
    }


def build_payload(
    root: Path,
    findings: Sequence[Finding],
    entries: Sequence[dict[str, Any]],
    selected: Sequence[dict[str, Any]],
    decisions: Sequence[RoutingDecision] | None = None,
    task_descriptor: dict[str, Any] | None = None,
    goal_inventory: dict[str, Any] | None = None,
) -> dict[str, Any]:
    errors = [finding for finding in findings if finding.severity == "error"]
    warnings = [finding for finding in findings if finding.severity == "warning"]
    return {
        "status": "pass" if not errors else "fail",
        "summary": {
            "entry_count": len(entries),
            "selected_count": len(selected),
            "finding_count": len(findings),
            "error_count": len(errors),
            "warning_count": len(warnings),
        },
        "selected_entries": [
            {
                "id": entry.get("id"),
                "tier": entry.get("tier"),
                "scope": entry.get("scope"),
                "file": entry.get("file"),
                "freshness": entry.get("freshness"),
                "review_after": normalize_metadata_value(entry.get("review_after")),
            }
            for entry in selected
        ],
        "routing_decisions": [asdict(decision) for decision in decisions or []],
        "routing_conflicts": [
            asdict(decision) for decision in decisions or [] if decision.task_scope_conflict
        ],
        "task_descriptor": task_descriptor_summary(task_descriptor),
        "goal_inventory": goal_inventory_summary(goal_inventory),
        "findings": [asdict(finding) for finding in findings],
        "repositoryRoot": ".",
        "repositoryName": root.resolve().name,
    }


def print_summary(payload: dict[str, Any], explain: bool = False) -> None:
    summary = payload["summary"]
    print(
        "Codex memory status: "
        f"{payload['status']}; {summary['entry_count']} entrie(s), "
        f"{summary['selected_count']} selected, "
        f"{summary['error_count']} error(s), {summary['warning_count']} warning(s)."
    )
    goal = payload.get("goal_inventory")
    if goal:
        print(
            "goal: "
            f"{goal['goal_id']} ({goal['status']}); "
            f"{goal['completed_count']}/{goal['progress_count']} progress item(s) completed; "
            f"active task {goal['active_task_descriptor']}"
        )
    for entry in payload["selected_entries"]:
        print(f"selected: {entry['id']} -> {entry['file']}")
    if explain:
        for decision in payload.get("routing_decisions", []):
            outcome = "selected" if decision["selected"] else "skipped"
            reasons = decision["reasons"] if decision["selected"] else decision["skipped_reasons"]
            suffix = "; ".join(reasons or ["no reason recorded"])
            warnings = "; ".join(decision.get("warnings", []))
            if warnings:
                suffix = f"{suffix}; warning: {warnings}"
            print(f"route: {outcome}: {decision['id']}: {suffix}")
    for finding in payload["findings"]:
        print(f"{finding['severity']}: {finding['path']}: {finding['message']}")


def list_arg(values: Sequence[str] | None) -> list[str]:
    return list(values or [])


def default_review_after(tier: str) -> str:
    days = 30 if tier in {"session", "branch"} else 90
    return (date.today() + timedelta(days=days)).isoformat()


def default_scope_for(tier: str, entry_id: str) -> str:
    slug = entry_id.split(":", 1)[-1]
    return "repo" if tier == "repo" else f"{tier}:{slug}"


def require_index_for_write(root: Path) -> tuple[dict[str, Any] | None, Path, int]:
    data, index_path, findings = load_index(root)
    if findings:
        for finding in findings:
            print(f"{finding.severity}: {finding.path}: {finding.message}", file=sys.stderr)
        return None, index_path, 1
    assert data is not None
    if not isinstance(data.get("entries"), list):
        print(f"error: {INDEX_REL}: entries must be a list.", file=sys.stderr)
        return None, index_path, 1
    return data, index_path, 0


def build_stub_entry(args: argparse.Namespace) -> dict[str, Any]:
    tier = args.stub_tier
    scope = args.stub_scope or default_scope_for(tier, args.write_stub)
    task_ids = list_arg(args.stub_task_id)
    if tier == "task" and not task_ids and scope.startswith("task:"):
        task_ids = [scope.split(":", 1)[1]]
    return {
        "id": args.write_stub,
        "tier": tier,
        "scope": scope,
        "file": normalize_path(args.stub_file),
        "tags": list_arg(args.stub_tags),
        "load_when": {
            "skills": list_arg(args.stub_skill),
            "paths": list_arg(args.stub_path),
            "intents": list_arg(args.stub_intent),
            "branches": list_arg(args.stub_branch),
            "tags": list_arg(args.stub_load_tag),
            "task": {
                "ids": task_ids,
                "work_modes": list_arg(args.stub_work_mode),
                "intents": list_arg(args.stub_task_intent),
                "paths": list_arg(args.stub_task_path),
            },
        },
        "confidence": args.stub_confidence,
        "freshness": args.stub_freshness,
        "source_refs": list_arg(args.stub_source_ref),
        "review_after": args.stub_review_after or default_review_after(tier),
        "invalidates_when": list_arg(args.stub_invalidates_when),
    }


def validate_stub_request(root: Path, data: dict[str, Any], entry: dict[str, Any]) -> list[Finding]:
    findings: list[Finding] = []
    existing_ids = {item.get("id") for item in data.get("entries", []) if isinstance(item, dict)}
    if entry["id"] in existing_ids:
        findings.append(Finding("error", INDEX_REL, f"Memory id already exists: {entry['id']}"))
    if entry["tier"] == "repo" and not entry["source_refs"]:
        findings.append(Finding("error", INDEX_REL, "Repo-tier stubs require at least one --stub-source-ref."))
    if entry["tier"] not in ACTIVE_TIERS:
        findings.append(Finding("error", INDEX_REL, f"Unknown active tier: {entry['tier']}"))
    memory_file, path_findings = safe_memory_path(root, entry["file"], INDEX_REL)
    findings.extend(path_findings)
    if memory_file is not None and memory_file.exists():
        findings.append(Finding("error", entry["file"], "Refusing to overwrite an existing memory file."))
    temp_seen: set[str] = set()
    _, shape_findings = validate_entry_shape_for_stub(root, entry, temp_seen)
    findings.extend(shape_findings)
    return findings


def validate_entry_shape_for_stub(root: Path, entry: dict[str, Any], seen_ids: set[str]) -> tuple[dict[str, Any], list[Finding]]:
    path = f"{INDEX_REL}#{entry.get('id', '<missing-id>')}"
    findings: list[Finding] = []
    missing = sorted(REQUIRED_FIELDS - set(entry))
    for field in missing:
        findings.append(Finding("error", path, f"Stub entry is missing {field}."))
    if missing:
        return entry, findings
    if entry["tier"] in DISABLED_TIERS or entry["tier"] not in ACTIVE_TIERS:
        findings.append(Finding("error", path, f"Invalid stub tier: {entry['tier']}"))
    findings.extend(validate_scope(entry, path))
    findings.extend(validate_string_list(entry.get("tags"), "tags", path))
    findings.extend(validate_string_list(entry.get("invalidates_when"), "invalidates_when", path))
    findings.extend(validate_load_when(entry, path))
    findings.extend(validate_exclude_when(entry, path))
    findings.extend(validate_source_refs(root, entry, path))
    findings.extend(validate_review_after(entry.get("review_after"), path))
    return entry, findings


def write_stub(root: Path, args: argparse.Namespace) -> int:
    data, index_path, status = require_index_for_write(root)
    if status:
        return status
    assert data is not None
    entry = build_stub_entry(args)
    findings = validate_stub_request(root, data, entry)
    if findings:
        for finding in findings:
            print(f"{finding.severity}: {finding.path}: {finding.message}", file=sys.stderr)
        return 1

    memory_file = (root / entry["file"]).resolve()
    front_matter = dump_yaml(entry).rstrip()
    title = entry["id"].split(":", 1)[-1].replace("-", " ").replace("_", " ").title()
    body = (
        f"---\n{front_matter}\n---\n\n"
        f"# {title}\n\n"
        "## Summary\n\n"
        "TODO: Add the sourced memory summary.\n\n"
        "## Usage\n\n"
        "TODO: Describe when agents should load this memory.\n"
    )
    data["entries"].append(entry)
    write_text_if_changed(index_path, dump_yaml(data))
    write_text_if_changed(memory_file, body)
    print(f"Created memory stub {entry['id']} at {entry['file']}")
    return 0


def promote_session(root: Path, args: argparse.Namespace) -> int:
    data, index_path, status = require_index_for_write(root)
    if status:
        return status
    assert data is not None
    source_path, source_findings = safe_memory_path(root, args.promote_session, INDEX_REL)
    if source_findings:
        for finding in source_findings:
            print(f"{finding.severity}: {finding.path}: {finding.message}", file=sys.stderr)
        return 1
    if source_path is None or not source_path.is_file():
        print(f"error: {args.promote_session}: session memory file does not exist.", file=sys.stderr)
        return 1

    promote_tier = args.promote_tier
    source_refs = list_arg(args.promote_source_ref)
    if promote_tier == "repo" and not source_refs:
        print("error: repo promotion requires --promote-source-ref.", file=sys.stderr)
        return 1
    promote_scope = args.promote_scope or default_scope_for(promote_tier, args.promote_id)
    promote_task_ids = list_arg(args.promote_task_id)
    if promote_tier == "task" and not promote_task_ids and promote_scope.startswith("task:"):
        promote_task_ids = [promote_scope.split(":", 1)[1]]

    entry = {
        "id": args.promote_id,
        "tier": promote_tier,
        "scope": promote_scope,
        "file": normalize_path(args.promote_file),
        "tags": list_arg(args.promote_tag),
        "load_when": {
            "skills": list_arg(args.promote_skill),
            "paths": list_arg(args.promote_path),
            "intents": list_arg(args.promote_intent),
            "branches": list_arg(args.promote_branch),
            "tags": list_arg(args.promote_load_tag),
            "task": {
                "ids": promote_task_ids,
                "work_modes": list_arg(args.promote_work_mode),
                "intents": list_arg(args.promote_task_intent),
                "paths": list_arg(args.promote_task_path),
            },
        },
        "confidence": args.promote_confidence,
        "freshness": "fresh",
        "source_refs": source_refs,
        "review_after": args.promote_review_after or default_review_after(promote_tier),
        "invalidates_when": list_arg(args.promote_invalidates_when),
    }

    findings = validate_stub_request(root, data, entry)
    if findings:
        for finding in findings:
            print(f"{finding.severity}: {finding.path}: {finding.message}", file=sys.stderr)
        return 1

    if not args.apply:
        print(json.dumps({"status": "dry-run", "candidate": entry}, indent=2))
        return 0

    target_path = (root / entry["file"]).resolve()
    text = source_path.read_text(encoding="utf-8")
    body = markdown_body(text)
    write_text_if_changed(target_path, f"---\n{dump_yaml(entry).rstrip()}\n---\n\n{body.rstrip()}\n")
    data["entries"].append(entry)
    if args.archive_source:
        archive_entry = archive_entry_for(root, data, source_path, entry)
        archive_path = root / archive_entry["file"]
        write_text_if_changed(archive_path, f"---\n{dump_yaml(archive_entry).rstrip()}\n---\n\n{body.rstrip()}\n")
        data["entries"].append(archive_entry)
        source_path.unlink()
    write_text_if_changed(index_path, dump_yaml(data))
    print(f"Promoted {args.promote_session} to {entry['id']} at {entry['file']}")
    return 0


def markdown_body(text: str) -> str:
    if not text.startswith("---\n"):
        return text
    end = text.find("\n---", 4)
    if end == -1:
        return text
    return text[end + 4 :].lstrip()


def archive_entry_for(root: Path, data: dict[str, Any], source_path: Path, promoted_entry: dict[str, Any]) -> dict[str, Any]:
    existing_ids = {item.get("id") for item in data.get("entries", []) if isinstance(item, dict)}
    source_stem = source_path.stem
    base_id = f"archive:{source_stem}"
    archive_id = base_id
    suffix = 1
    while archive_id in existing_ids:
        suffix += 1
        archive_id = f"{base_id}-{suffix}"

    archive_file = root / MEMORY_ROOT_REL / "archive" / f"{source_stem}.md"
    suffix = 1
    while archive_file.exists():
        suffix += 1
        archive_file = root / MEMORY_ROOT_REL / "archive" / f"{source_stem}-{suffix}.md"

    return {
        "id": archive_id,
        "tier": "archive",
        "scope": "archive",
        "file": rel(root, archive_file),
        "tags": sorted(set(["archive", *list_arg(promoted_entry.get("tags"))])),
        "load_when": {
            "skills": [],
            "paths": [],
            "intents": [],
            "branches": [],
            "tags": [],
            "task": {"ids": [], "work_modes": [], "intents": [], "paths": []},
        },
        "confidence": promoted_entry.get("confidence", "medium"),
        "freshness": "fresh",
        "source_refs": list_arg(promoted_entry.get("source_refs")),
        "review_after": default_review_after("archive"),
        "invalidates_when": [
            "Archived source no longer has audit value.",
            "Replacement guidance changes materially.",
        ],
    }


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=REPO_ROOT, help="Repository root.")
    parser.add_argument("--summary", action="store_true", help="Print compact summary output.")
    parser.add_argument("--json-output", type=Path, help="Write machine-readable JSON output.")
    parser.add_argument("--stale-only", action="store_true", help="Select only stale or expired entries.")
    parser.add_argument("--task", help="Repo-relative .codex/memory/tasks/*.yml task descriptor used for routing.")
    parser.add_argument("--goal", help="Repo-relative .codex/memory/goals/*.yml long-goal inventory used for progress and routing.")
    parser.add_argument("--explain", action="store_true", help="Print selected and skipped routing reasons.")
    parser.add_argument("--paths", nargs="*", default=[], help="Repo-relative paths used for routing selection.")
    parser.add_argument("--tags", nargs="*", default=[], help="Explicit tags used for routing selection.")
    parser.add_argument("--skills", nargs="*", default=[], help="Skill names used for routing selection.")
    parser.add_argument("--intents", nargs="*", default=[], help="Intent labels used for routing selection.")
    parser.add_argument("--branches", nargs="*", default=[], help="Branch names used for routing selection.")

    parser.add_argument("--write-stub", metavar="ID", help="Create a new indexed memory Markdown stub.")
    parser.add_argument("--stub-tier", default="session", choices=sorted(ACTIVE_TIERS))
    parser.add_argument("--stub-scope")
    parser.add_argument("--stub-file")
    parser.add_argument("--stub-tags", nargs="*", default=[])
    parser.add_argument("--stub-skill", action="append", default=[])
    parser.add_argument("--stub-path", action="append", default=[])
    parser.add_argument("--stub-intent", action="append", default=[])
    parser.add_argument("--stub-branch", action="append", default=[])
    parser.add_argument("--stub-load-tag", action="append", default=[])
    parser.add_argument("--stub-task-id", action="append", default=[])
    parser.add_argument("--stub-work-mode", action="append", default=[])
    parser.add_argument("--stub-task-intent", action="append", default=[])
    parser.add_argument("--stub-task-path", action="append", default=[])
    parser.add_argument("--stub-source-ref", action="append", default=[])
    parser.add_argument("--stub-invalidates-when", action="append", default=[])
    parser.add_argument("--stub-confidence", default="medium", choices=sorted(ALLOWED_CONFIDENCE))
    parser.add_argument("--stub-freshness", default="fresh", choices=sorted(ALLOWED_FRESHNESS))
    parser.add_argument("--stub-review-after")

    parser.add_argument("--promote-session", metavar="FILE", help="Promote a session memory file.")
    parser.add_argument("--promote-tier", choices=sorted(ACTIVE_TIERS - {"session", "archive"}), default="task")
    parser.add_argument("--promote-id")
    parser.add_argument("--promote-scope")
    parser.add_argument("--promote-file")
    parser.add_argument("--promote-tag", action="append", default=[])
    parser.add_argument("--promote-skill", action="append", default=[])
    parser.add_argument("--promote-path", action="append", default=[])
    parser.add_argument("--promote-intent", action="append", default=[])
    parser.add_argument("--promote-branch", action="append", default=[])
    parser.add_argument("--promote-load-tag", action="append", default=[])
    parser.add_argument("--promote-task-id", action="append", default=[])
    parser.add_argument("--promote-work-mode", action="append", default=[])
    parser.add_argument("--promote-task-intent", action="append", default=[])
    parser.add_argument("--promote-task-path", action="append", default=[])
    parser.add_argument("--promote-source-ref", action="append", default=[])
    parser.add_argument("--promote-invalidates-when", action="append", default=[])
    parser.add_argument("--promote-confidence", default="medium", choices=sorted(ALLOWED_CONFIDENCE))
    parser.add_argument("--promote-review-after")
    parser.add_argument("--apply", action="store_true", help="Apply --promote-session. Without this, promotion is dry-run.")
    parser.add_argument("--archive-source", action="store_true", help="Archive the source session file after applied promotion.")
    return parser.parse_args(argv)


def validate_write_args(args: argparse.Namespace) -> int:
    if args.write_stub:
        if not args.stub_file:
            print("error: --write-stub requires --stub-file.", file=sys.stderr)
            return 1
        if not args.stub_tags:
            print("error: --write-stub requires --stub-tags.", file=sys.stderr)
            return 1
        if not args.stub_invalidates_when:
            print("error: --write-stub requires --stub-invalidates-when.", file=sys.stderr)
            return 1
    if args.promote_session:
        for required_name in ("promote_id", "promote_file"):
            if not getattr(args, required_name):
                print(f"error: --promote-session requires --{required_name.replace('_', '-')}.", file=sys.stderr)
                return 1
        if not args.promote_tag:
            print("error: --promote-session requires --promote-tag.", file=sys.stderr)
            return 1
        if not args.promote_invalidates_when:
            print("error: --promote-session requires --promote-invalidates-when.", file=sys.stderr)
            return 1
    if args.write_stub and args.promote_session:
        print("error: choose either --write-stub or --promote-session, not both.", file=sys.stderr)
        return 1
    return 0


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    root = args.root.resolve()

    write_arg_status = validate_write_args(args)
    if write_arg_status:
        return write_arg_status
    if args.write_stub:
        return write_stub(root, args)
    if args.promote_session:
        return promote_session(root, args)

    goal_inventory: dict[str, Any] | None = None
    goal_findings: list[Finding] = []
    if args.goal:
        goal_inventory, goal_findings = load_goal_inventory(root, args.goal)

    task_descriptor: dict[str, Any] | None = None
    task_findings: list[Finding] = []
    task_path = args.task
    if not task_path and goal_inventory and isinstance(goal_inventory.get("active_task_descriptor"), str):
        task_path = goal_inventory["active_task_descriptor"]
    if task_path:
        task_descriptor, task_findings = load_task_descriptor(root, task_path)

    findings, entries = collect_findings(root)
    findings.extend(goal_findings)
    findings.extend(task_findings)
    context = build_routing_context(
        paths=args.paths,
        tags=args.tags,
        task_descriptor=task_descriptor,
        skills=args.skills,
        intents=args.intents,
        branches=args.branches,
    )
    selected, decisions = route_entries(entries, context, args.stale_only)
    payload = build_payload(root, findings, entries, selected, decisions, task_descriptor, goal_inventory)

    if args.json_output:
        output_path = args.json_output
        if not output_path.is_absolute():
            output_path = root / output_path
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    if args.summary or not args.json_output:
        print_summary(payload, args.explain)

    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
