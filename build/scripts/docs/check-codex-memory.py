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
ACTIVE_TIERS = {"session", "branch", "task", "repo", "archive"}
DISABLED_TIERS = {"user", "global"}
ALLOWED_CONFIDENCE = {"low", "medium", "high"}
ALLOWED_FRESHNESS = {"fresh", "review-soon", "stale", "unknown"}
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


def matches_paths(entry: dict[str, Any], paths: Sequence[str]) -> bool:
    load_when = entry.get("load_when")
    if not paths or not isinstance(load_when, dict):
        return False
    patterns = load_when.get("paths")
    if not isinstance(patterns, list):
        return False
    normalized_paths = [normalize_path(path) for path in paths]
    return any(fnmatch.fnmatchcase(path, pattern) for path in normalized_paths for pattern in patterns)


def matches_tags(entry: dict[str, Any], tags: Sequence[str]) -> bool:
    if not tags:
        return False
    requested = set(tags)
    entry_tags = set(entry.get("tags") if isinstance(entry.get("tags"), list) else [])
    load_when = entry.get("load_when")
    load_tags: set[str] = set()
    if isinstance(load_when, dict) and isinstance(load_when.get("tags"), list):
        load_tags = set(load_when["tags"])
    return bool(requested & (entry_tags | load_tags))


def select_entries(entries: Sequence[dict[str, Any]], paths: Sequence[str], tags: Sequence[str], stale_only: bool) -> list[dict[str, Any]]:
    selected: list[dict[str, Any]] = []
    has_filters = bool(paths or tags)
    for entry in entries:
        if stale_only and not entry_is_stale(entry):
            continue
        if has_filters and not (matches_paths(entry, paths) or matches_tags(entry, tags)):
            continue
        selected.append(entry)
    return selected


def build_payload(root: Path, findings: Sequence[Finding], entries: Sequence[dict[str, Any]], selected: Sequence[dict[str, Any]]) -> dict[str, Any]:
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
        "findings": [asdict(finding) for finding in findings],
        "repositoryRoot": ".",
        "repositoryName": root.resolve().name,
    }


def print_summary(payload: dict[str, Any]) -> None:
    summary = payload["summary"]
    print(
        "Codex memory status: "
        f"{payload['status']}; {summary['entry_count']} entrie(s), "
        f"{summary['selected_count']} selected, "
        f"{summary['error_count']} error(s), {summary['warning_count']} warning(s)."
    )
    for entry in payload["selected_entries"]:
        print(f"selected: {entry['id']} -> {entry['file']}")
    for finding in payload["findings"]:
        print(f"{finding['severity']}: {finding['path']}: {finding['message']}")


def list_arg(values: Sequence[str] | None) -> list[str]:
    return list(values or [])


def default_review_after(tier: str) -> str:
    days = 30 if tier in {"session", "branch"} else 90
    return (date.today() + timedelta(days=days)).isoformat()


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
    return {
        "id": args.write_stub,
        "tier": tier,
        "scope": args.stub_scope or ("repo" if tier == "repo" else f"{tier}:{args.write_stub}"),
        "file": normalize_path(args.stub_file),
        "tags": list_arg(args.stub_tags),
        "load_when": {
            "skills": list_arg(args.stub_skill),
            "paths": list_arg(args.stub_path),
            "intents": list_arg(args.stub_intent),
            "branches": list_arg(args.stub_branch),
            "tags": list_arg(args.stub_load_tag),
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

    entry = {
        "id": args.promote_id,
        "tier": promote_tier,
        "scope": args.promote_scope or ("repo" if promote_tier == "repo" else f"{promote_tier}:{args.promote_id}"),
        "file": normalize_path(args.promote_file),
        "tags": list_arg(args.promote_tag),
        "load_when": {
            "skills": list_arg(args.promote_skill),
            "paths": list_arg(args.promote_path),
            "intents": list_arg(args.promote_intent),
            "branches": list_arg(args.promote_branch),
            "tags": list_arg(args.promote_load_tag),
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
        "load_when": {"skills": [], "paths": [], "intents": [], "branches": [], "tags": []},
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
    parser.add_argument("--paths", nargs="*", default=[], help="Repo-relative paths used for routing selection.")
    parser.add_argument("--tags", nargs="*", default=[], help="Explicit tags used for routing selection.")

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

    findings, entries = collect_findings(root)
    selected = select_entries(entries, args.paths, args.tags, args.stale_only)
    payload = build_payload(root, findings, entries, selected)

    if args.json_output:
        output_path = args.json_output
        if not output_path.is_absolute():
            output_path = root / output_path
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    if args.summary or not args.json_output:
        print_summary(payload)

    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
