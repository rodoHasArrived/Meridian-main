#!/usr/bin/env python3
"""Focused validation for check-codex-memory.py behavior."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "check-codex-memory.py"
SPEC = importlib.util.spec_from_file_location("check_codex_memory", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load check-codex-memory module from {MODULE_PATH}")

check_codex_memory = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = check_codex_memory
SPEC.loader.exec_module(check_codex_memory)


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def valid_entry(review_after: str = "2999-01-01") -> str:
    return f"""id: repo:validation
tier: repo
scope: repo
file: .codex/memory/repo/validation.md
tags:
  - validation
load_when:
  skills:
    - meridian-docs
  paths:
    - docs/**
  intents:
    - validation
  branches: []
  tags:
    - validation
  task:
    ids: []
    work_modes: []
    intents: []
    paths: []
confidence: high
freshness: fresh
source_refs:
  - docs/ai/tooling/README.md
review_after: {review_after}
invalidates_when:
  - Validation commands change.
"""


def valid_index(review_after: str = "2999-01-01") -> str:
    return f"""version: 1
memory_root: .codex/memory
entries:
  - {valid_entry(review_after).replace(chr(10), chr(10) + "    ").rstrip()}
"""


def index_entry(text: str) -> str:
    return "  - " + text.replace("\n", "\n    ").rstrip() + "\n"


def valid_memory_file(review_after: str = "2999-01-01") -> str:
    return f"""---
{valid_entry(review_after).rstrip()}
---

# Validation Memory

Stable validation guidance.
"""


def write_valid_memory_tree(root: Path, review_after: str = "2999-01-01") -> None:
    write(root / "docs" / "ai" / "tooling" / "README.md", "# Tooling\n")
    write(root / ".codex" / "memory" / "index.yml", valid_index(review_after))
    write(root / ".codex" / "memory" / "README.md", "# Memory\n")
    write(root / ".codex" / "memory" / "repo" / "validation.md", valid_memory_file(review_after))


def task_descriptor(task_id: str = "task-a", memory_tags: list[str] | None = None) -> str:
    tags = memory_tags or ["validation"]
    tag_lines = "\n".join(f"  - {tag}" for tag in tags)
    return f"""version: 1
task_id: {task_id}
intent: validation
selected_skill: meridian-docs
work_mode: implementation
branch: feature/task-a
planned_paths:
  - docs/ai/codex/quickstart.md
memory_tags:
{tag_lines}
success_criteria:
  - Route task memory narrowly.
promotion_candidates: []
"""


def goal_inventory(progress_status: str = "in_progress") -> str:
    return f"""version: 1
goal_id: goal-a
objective: Keep Codex memory goal-aware.
status: active
started_at: 2026-06-20T00:00:00Z
updated_at: 2026-06-20T00:10:00Z
active_task_descriptor: .codex/memory/tasks/task-a.yml
progress_inventory:
  - id: task-routing
    status: {progress_status}
    summary: Add task descriptor routing.
    evidence_refs:
      - docs/ai/tooling/README.md
    updated_at: 2026-06-20T00:10:00Z
next_actions:
  - Run focused memory validation.
open_questions: []
promotion_candidates: []
"""


def task_memory_entry(task_id: str) -> str:
    return f"""id: task:{task_id}
tier: task
scope: task:{task_id}
file: .codex/memory/tasks/{task_id}.md
tags:
  - validation
load_when:
  skills: []
  paths: []
  intents: []
  branches: []
  tags: []
  task:
    ids:
      - {task_id}
    work_modes:
      - implementation
    intents:
      - validation
    paths:
      - docs/ai/**
confidence: high
freshness: fresh
source_refs:
  - docs/ai/tooling/README.md
review_after: 2999-01-01
invalidates_when:
  - Task closes.
"""


def generic_tag_repo_entry() -> str:
    return """id: repo:generic-tag
tier: repo
scope: repo
file: .codex/memory/repo/generic-tag.md
tags:
  - generic
load_when:
  skills: []
  paths: []
  intents: []
  branches: []
  tags: []
  task:
    ids: []
    work_modes: []
    intents: []
    paths: []
confidence: high
freshness: fresh
source_refs:
  - docs/ai/tooling/README.md
review_after: 2999-01-01
invalidates_when:
  - Generic tag guidance changes.
"""


def archive_memory_entry(with_active_selectors: bool = False) -> str:
    entry = valid_entry().replace("id: repo:validation", "id: archive:validation", 1)
    entry = entry.replace("tier: repo", "tier: archive", 1)
    entry = entry.replace("scope: repo", "scope: archive", 1)
    entry = entry.replace(
        "file: .codex/memory/repo/validation.md",
        "file: .codex/memory/archive/validation.md",
        1,
    )
    if not with_active_selectors:
        entry = entry.replace(
            """load_when:
  skills:
    - meridian-docs
  paths:
    - docs/**
  intents:
    - validation
  branches: []
  tags:
    - validation
  task:
    ids: []
    work_modes: []
    intents: []
    paths: []""",
            """load_when:
  skills: []
  paths: []
  intents: []
  branches: []
  tags: []
  task:
    ids: []
    work_modes: []
    intents: []
    paths: []""",
            1,
        )
    return entry.rstrip() + """
retired_because: Superseded by current validation memory.
replaced_by:
  - .codex/memory/repo/validation.md
"""

def memory_file_from_entry(entry: str, title: str = "Memory") -> str:
    return f"---\n{entry.rstrip()}\n---\n\n# {title}\n"


def finding_messages(findings: list[object]) -> list[str]:
    return [getattr(finding, "message") for finding in findings]


class CheckCodexMemoryTests(unittest.TestCase):
    def test_valid_index_and_seed_file_pass(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)

            findings, entries = check_codex_memory.collect_findings(root)

            self.assertEqual([], findings)
            self.assertEqual(["repo:validation"], [entry["id"] for entry in entries])

    def test_missing_index_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            findings, _ = check_codex_memory.collect_findings(Path(tmp))

            self.assertTrue(any("index is missing" in finding.message for finding in findings))

    def test_duplicate_ids_fail(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            index = valid_index().rstrip() + "\n  - " + valid_entry().replace("\n", "\n    ").rstrip() + "\n"
            write(root / ".codex" / "memory" / "index.yml", index)

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(any("Duplicate memory id" in finding.message for finding in findings))

    def test_linked_file_missing_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            (root / ".codex" / "memory" / "repo" / "validation.md").unlink()

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(any("Memory file does not exist" in finding.message for finding in findings))

    def test_missing_front_matter_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            write(root / ".codex" / "memory" / "repo" / "validation.md", "# No metadata\n")

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(any("missing YAML front matter" in finding.message for finding in findings))

    def test_front_matter_missing_required_metadata_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            entry = valid_entry().replace("source_refs:\n  - docs/ai/tooling/README.md\n", "")
            write(root / ".codex" / "memory" / "repo" / "validation.md", memory_file_from_entry(entry))

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(
                any("Memory front matter is missing source_refs" in finding.message for finding in findings)
            )

    def test_front_matter_mismatched_id_and_file_fail(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            entry = valid_entry().replace("id: repo:validation", "id: repo:other", 1).replace(
                "file: .codex/memory/repo/validation.md",
                "file: .codex/memory/repo/other.md",
                1,
            )
            write(root / ".codex" / "memory" / "repo" / "validation.md", memory_file_from_entry(entry))

            findings, _ = check_codex_memory.collect_findings(root)
            messages = finding_messages(findings)

            self.assertTrue(
                any("Front matter id does not match index value 'repo:validation'" in message for message in messages)
            )
            self.assertTrue(
                any(
                    "Front matter file does not match index value '.codex/memory/repo/validation.md'" in message
                    for message in messages
                )
            )

    def test_front_matter_mismatched_list_metadata_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            write(root / "docs" / "ai" / "other.md", "# Other source\n")
            entry = valid_entry().replace("  - validation", "  - other", 1).replace(
                "  - docs/ai/tooling/README.md",
                "  - docs/ai/other.md",
                1,
            ).replace("  - Validation commands change.", "  - Other commands change.", 1)
            write(root / ".codex" / "memory" / "repo" / "validation.md", memory_file_from_entry(entry))

            findings, _ = check_codex_memory.collect_findings(root)
            messages = finding_messages(findings)

            self.assertTrue(any("Front matter tags does not match index value" in message for message in messages))
            self.assertTrue(
                any("Front matter source_refs does not match index value" in message for message in messages)
            )
            self.assertTrue(
                any("Front matter invalidates_when does not match index value" in message for message in messages)
            )

    def test_empty_source_refs_fail_for_index_and_front_matter(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            index = valid_index().replace(
                "source_refs:\n      - docs/ai/tooling/README.md", "source_refs: []"
            )
            entry = valid_entry().replace("source_refs:\n  - docs/ai/tooling/README.md", "source_refs: []")
            write(root / ".codex" / "memory" / "index.yml", index)
            write(root / ".codex" / "memory" / "repo" / "validation.md", memory_file_from_entry(entry))

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(
                any("source_refs must contain at least one entry" in finding.message for finding in findings)
            )

    def test_front_matter_invalid_confidence_freshness_and_review_date_fail(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            entry = valid_entry().replace("confidence: high", "confidence: certain", 1).replace(
                "freshness: fresh", "freshness: ancient", 1
            ).replace("review_after: 2999-01-01", "review_after: soon", 1)
            write(root / ".codex" / "memory" / "repo" / "validation.md", memory_file_from_entry(entry))

            findings, _ = check_codex_memory.collect_findings(root)
            messages = finding_messages(findings)

            self.assertTrue(any("Unknown front matter confidence: certain" in message for message in messages))
            self.assertTrue(any("Unknown front matter freshness: ancient" in message for message in messages))
            self.assertTrue(any("review_after is not a valid ISO date: soon" in message for message in messages))

    def test_unknown_tier_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            text = valid_index().replace("tier: repo", "tier: global", 1)
            write(root / ".codex" / "memory" / "index.yml", text)

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(any("Disabled tier" in finding.message for finding in findings))

    def test_invalid_review_after_date_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root, review_after="not-a-date")

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(any("review_after is not a valid ISO date" in finding.message for finding in findings))

    def test_missing_source_ref_file_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            (root / "docs" / "ai" / "tooling" / "README.md").unlink()

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(any("source_ref does not exist" in finding.message for finding in findings))

    def test_expired_review_after_is_warning(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root, review_after="2000-01-01")

            findings, entries = check_codex_memory.collect_findings(root)
            selected = check_codex_memory.select_entries(entries, [], [], stale_only=True)

            self.assertTrue(any(finding.severity == "warning" for finding in findings))
            self.assertEqual(["repo:validation"], [entry["id"] for entry in selected])

    def test_unindexed_memory_file_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            write(root / ".codex" / "memory" / "repo" / "extra.md", "---\nid: extra\n---\n")

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(any("not listed" in finding.message for finding in findings))

    def test_paths_and_tags_select_routed_entries(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            findings, entries = check_codex_memory.collect_findings(root)

            by_path = check_codex_memory.select_entries(entries, ["docs/ai/codex/quickstart.md"], [], False)
            by_tag = check_codex_memory.select_entries(entries, [], ["validation"], False)

            self.assertEqual([], findings)
            self.assertEqual(["repo:validation"], [entry["id"] for entry in by_path])
            self.assertEqual(["repo:validation"], [entry["id"] for entry in by_tag])

    def test_task_descriptor_selects_matching_task_and_repo_entries(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            task_a = task_memory_entry("task-a")
            task_b = task_memory_entry("task-b")
            index = valid_index() + index_entry(task_a) + index_entry(task_b)
            write(root / ".codex" / "memory" / "index.yml", index)
            write(root / ".codex" / "memory" / "tasks" / "task-a.md", memory_file_from_entry(task_a, "Task A"))
            write(root / ".codex" / "memory" / "tasks" / "task-b.md", memory_file_from_entry(task_b, "Task B"))
            write(root / ".codex" / "memory" / "tasks" / "task-a.yml", task_descriptor("task-a"))

            descriptor, task_findings = check_codex_memory.load_task_descriptor(
                root, ".codex/memory/tasks/task-a.yml"
            )
            findings, entries = check_codex_memory.collect_findings(root)
            context = check_codex_memory.build_routing_context(task_descriptor=descriptor)
            selected, decisions = check_codex_memory.route_entries(entries, context)

            self.assertEqual([], task_findings)
            self.assertEqual([], findings)
            self.assertEqual(["task:task-a", "repo:validation"], [entry["id"] for entry in selected])
            task_b_decision = next(decision for decision in decisions if decision.id == "task:task-b")
            self.assertFalse(task_b_decision.selected)
            self.assertTrue(task_b_decision.task_scope_conflict)

    def test_exclude_when_prevents_otherwise_valid_selection(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            excluded_entry = valid_entry().rstrip() + """
exclude_when:
  intents:
    - validation
"""
            index = f"""version: 1
memory_root: .codex/memory
entries:
{index_entry(excluded_entry)}"""
            write(root / ".codex" / "memory" / "index.yml", index)
            write(root / ".codex" / "memory" / "tasks" / "task-a.yml", task_descriptor("task-a"))

            descriptor, _ = check_codex_memory.load_task_descriptor(root, ".codex/memory/tasks/task-a.yml")
            _, entries = check_codex_memory.collect_findings(root)
            context = check_codex_memory.build_routing_context(task_descriptor=descriptor)
            selected, decisions = check_codex_memory.route_entries(entries, context)

            self.assertEqual([], selected)
            decision = decisions[0]
            self.assertFalse(decision.selected)
            self.assertIn("excluded by intent validation", decision.skipped_reasons)

    def test_explain_and_json_output_include_routing_decisions(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            write(root / ".codex" / "memory" / "tasks" / "task-a.yml", task_descriptor("task-a"))
            json_path = root / "routing.json"
            stdout = io.StringIO()

            with contextlib.redirect_stdout(stdout):
                status = check_codex_memory.main(
                    [
                        "--root",
                        str(root),
                        "--task",
                        ".codex/memory/tasks/task-a.yml",
                        "--explain",
                        "--summary",
                        "--json-output",
                        str(json_path),
                    ]
                )

            self.assertEqual(0, status)
            self.assertIn("route: selected: repo:validation", stdout.getvalue())
            payload = json.loads(json_path.read_text(encoding="utf-8"))
            self.assertIn("routing_decisions", payload)
            self.assertEqual("task-a", payload["task_descriptor"]["task_id"])

    def test_generic_repo_tag_requires_explicit_tag_request(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            generic = generic_tag_repo_entry()
            write(root / ".codex" / "memory" / "index.yml", valid_index() + index_entry(generic))
            write(root / ".codex" / "memory" / "repo" / "generic-tag.md", memory_file_from_entry(generic))
            write(root / ".codex" / "memory" / "tasks" / "task-a.yml", task_descriptor("task-a", ["generic"]))

            descriptor, _ = check_codex_memory.load_task_descriptor(root, ".codex/memory/tasks/task-a.yml")
            findings, entries = check_codex_memory.collect_findings(root)
            task_context = check_codex_memory.build_routing_context(task_descriptor=descriptor)
            by_task, _ = check_codex_memory.route_entries(entries, task_context)
            by_explicit_tag = check_codex_memory.select_entries(entries, [], ["generic"], False)

            self.assertEqual([], findings)
            self.assertNotIn("repo:generic-tag", [entry["id"] for entry in by_task])
            self.assertIn("repo:generic-tag", [entry["id"] for entry in by_explicit_tag])

    def test_goal_inventory_loads_active_task_descriptor(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            write(root / ".codex" / "memory" / "tasks" / "task-a.yml", task_descriptor("task-a"))
            write(root / ".codex" / "memory" / "goals" / "goal-a.yml", goal_inventory())
            json_path = root / "goal-routing.json"
            stdout = io.StringIO()

            with contextlib.redirect_stdout(stdout):
                status = check_codex_memory.main(
                    [
                        "--root",
                        str(root),
                        "--goal",
                        ".codex/memory/goals/goal-a.yml",
                        "--explain",
                        "--summary",
                        "--json-output",
                        str(json_path),
                    ]
                )

            self.assertEqual(0, status)
            self.assertIn("goal: goal-a (active)", stdout.getvalue())
            payload = json.loads(json_path.read_text(encoding="utf-8"))
            self.assertEqual("goal-a", payload["goal_inventory"]["goal_id"])
            self.assertEqual("task-a", payload["task_descriptor"]["task_id"])
            self.assertEqual(["repo:validation"], [entry["id"] for entry in payload["selected_entries"]])

    def test_receipt_reports_selected_and_skipped_entries_with_selectors(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            generic = generic_tag_repo_entry()
            write(root / ".codex" / "memory" / "index.yml", valid_index() + index_entry(generic))
            write(root / ".codex" / "memory" / "repo" / "generic-tag.md", memory_file_from_entry(generic))
            write(root / ".codex" / "memory" / "tasks" / "task-a.yml", task_descriptor("task-a"))
            json_path = root / "receipt.json"
            stdout = io.StringIO()

            with contextlib.redirect_stdout(stdout):
                status = check_codex_memory.main(
                    [
                        "--root",
                        str(root),
                        "--task",
                        ".codex/memory/tasks/task-a.yml",
                        "--receipt",
                        "--summary",
                        "--json-output",
                        str(json_path),
                    ]
                )

            self.assertEqual(0, status)
            output = stdout.getvalue()
            self.assertIn("task_descriptor_path: .codex/memory/tasks/task-a.yml", output)
            self.assertIn("selectors: branches=['feature/task-a']", output)
            self.assertIn("selected: repo:validation", output)
            self.assertIn("skipped: repo:generic-tag", output)
            payload = json.loads(json_path.read_text(encoding="utf-8"))
            receipt = payload["memory_receipt"]
            self.assertEqual(".codex/memory/tasks/task-a.yml", receipt["task_descriptor_path"])
            self.assertEqual(["feature/task-a"], receipt["selectors"]["branches"])
            self.assertEqual(1, receipt["selected_count"])
            self.assertEqual(1, receipt["skipped_count"])
            self.assertEqual(["repo:validation"], [entry["id"] for entry in receipt["selected_memory"]])
            self.assertEqual(["repo:generic-tag"], [entry["id"] for entry in receipt["skipped_memory"]])
            self.assertIn("match_reasons", receipt["selected_memory"][0])
            self.assertIn("skip_reasons", receipt["skipped_memory"][0])

    def test_goal_inventory_rejects_unknown_progress_status(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            write(root / ".codex" / "memory" / "tasks" / "task-a.yml", task_descriptor("task-a"))
            write(root / ".codex" / "memory" / "goals" / "goal-a.yml", goal_inventory("almost_done"))

            _, findings = check_codex_memory.load_goal_inventory(root, ".codex/memory/goals/goal-a.yml")

            self.assertTrue(any("Unknown progress status" in finding.message for finding in findings))


    def test_collect_findings_validates_task_descriptors_under_memory_tasks(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            invalid_task = task_descriptor("task-a").replace("selected_skill: meridian-docs", "selected_skill: ")
            write(root / ".codex" / "memory" / "tasks" / "task-a.yml", invalid_task)

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(any("selected_skill must be a non-empty string" in finding.message for finding in findings))

    def test_collect_findings_validates_goal_inventories_under_memory_goals(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            write(root / ".codex" / "memory" / "tasks" / "task-a.yml", task_descriptor("task-a"))
            invalid_goal = goal_inventory().replace("status: active", "status: paused")
            write(root / ".codex" / "memory" / "goals" / "goal-a.yml", invalid_goal)

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(any("Unknown goal status" in finding.message for finding in findings))

    def test_archive_entries_are_not_selected_as_active_guidance(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            archive_entry = archive_memory_entry()
            write(root / ".codex" / "memory" / "index.yml", valid_index() + index_entry(archive_entry))
            write(root / ".codex" / "memory" / "archive" / "validation.md", memory_file_from_entry(archive_entry, "Archived"))

            findings, entries = check_codex_memory.collect_findings(root)
            selected = check_codex_memory.select_entries(entries, ["docs/ai/codex/quickstart.md"], [], False)

            self.assertEqual([], findings)
            self.assertNotIn("archive:validation", [entry["id"] for entry in selected])

    def test_archive_entries_with_active_selectors_are_reported(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            archive_entry = archive_memory_entry(with_active_selectors=True)
            write(root / ".codex" / "memory" / "index.yml", valid_index() + index_entry(archive_entry))
            write(root / ".codex" / "memory" / "archive" / "validation.md", memory_file_from_entry(archive_entry, "Archived"))

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(
                any(
                    "archive-tier memory must not load as active guidance" in finding.message
                    for finding in findings
                )
            )

    def test_goal_inventory_yaml_is_not_reported_as_unindexed_memory(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            write(root / ".codex" / "memory" / "tasks" / "task-a.yml", task_descriptor("task-a"))
            write(root / ".codex" / "memory" / "goals" / "goal-a.yml", goal_inventory())

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertFalse(any("not listed" in finding.message for finding in findings))

    def test_record_goal_progress_creates_inventory_item(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            write(root / ".codex" / "memory" / "tasks" / "task-a.yml", task_descriptor("task-a"))
            write(root / ".codex" / "memory" / "goals" / "goal-a.yml", goal_inventory())
            stdout = io.StringIO()

            with contextlib.redirect_stdout(stdout):
                status = check_codex_memory.main(
                    [
                        "--root",
                        str(root),
                        "--goal",
                        ".codex/memory/goals/goal-a.yml",
                        "--record-goal-progress",
                        "dynamic-memory-use",
                        "--progress-status",
                        "completed",
                        "--progress-summary",
                        "Recorded memory receipt and progress update behavior.",
                        "--progress-evidence-ref",
                        "docs/ai/tooling/README.md",
                        "--progress-updated-at",
                        "2026-06-20T00:20:00Z",
                        "--next-action",
                        "Keep receipts visible during long goals.",
                    ]
                )

            self.assertEqual(0, status)
            self.assertIn("Memory update: goal progress recorded", stdout.getvalue())
            goal, findings = check_codex_memory.load_goal_inventory(root, ".codex/memory/goals/goal-a.yml")
            self.assertEqual([], [finding for finding in findings if finding.severity == "error"])
            assert goal is not None
            progress = {
                item["id"]: item
                for item in goal["progress_inventory"]
                if isinstance(item, dict)
            }
            self.assertEqual("completed", progress["dynamic-memory-use"]["status"])
            self.assertEqual(
                ["docs/ai/tooling/README.md"],
                progress["dynamic-memory-use"]["evidence_refs"],
            )
            self.assertEqual("2026-06-20T00:20:00Z", progress["dynamic-memory-use"]["updated_at"])
            self.assertIn("Keep receipts visible during long goals.", goal["next_actions"])
            goal_text = (root / ".codex" / "memory" / "goals" / "goal-a.yml").read_text(encoding="utf-8")
            self.assertIn("progress_inventory:\n  -", goal_text)
            self.assertNotIn("00:00:00+00:00", goal_text)

    def test_record_goal_progress_requires_goal(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            stderr = io.StringIO()

            with contextlib.redirect_stderr(stderr):
                status = check_codex_memory.main(
                    [
                        "--root",
                        str(root),
                        "--record-goal-progress",
                        "dynamic-memory-use",
                        "--progress-status",
                        "completed",
                        "--progress-summary",
                        "Recorded memory receipt and progress update behavior.",
                        "--progress-evidence-ref",
                        "docs/ai/tooling/README.md",
                    ]
                )

            self.assertEqual(1, status)
            self.assertIn("requires --goal", stderr.getvalue())

    def test_write_stub_refuses_existing_file(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            stderr = io.StringIO()

            with contextlib.redirect_stderr(stderr):
                status = check_codex_memory.main(
                    [
                        "--root",
                        str(root),
                        "--write-stub",
                        "repo:validation-copy",
                        "--stub-tier",
                        "repo",
                        "--stub-file",
                        ".codex/memory/repo/validation.md",
                        "--stub-tags",
                        "validation",
                        "--stub-source-ref",
                        "docs/ai/tooling/README.md",
                        "--stub-invalidates-when",
                        "Validation changes.",
                    ]
                )

            self.assertEqual(1, status)
            self.assertIn("Refusing to overwrite", stderr.getvalue())

    def test_write_stub_refuses_out_of_root_path(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            stderr = io.StringIO()

            with contextlib.redirect_stderr(stderr):
                status = check_codex_memory.main(
                    [
                        "--root",
                        str(root),
                        "--write-stub",
                        "repo:bad",
                        "--stub-tier",
                        "repo",
                        "--stub-file",
                        "../bad.md",
                        "--stub-tags",
                        "validation",
                        "--stub-source-ref",
                        "docs/ai/tooling/README.md",
                        "--stub-invalidates-when",
                        "Validation changes.",
                    ]
                )

            self.assertEqual(1, status)
            self.assertIn("must not contain '..'", stderr.getvalue())


if __name__ == "__main__":
    unittest.main()
