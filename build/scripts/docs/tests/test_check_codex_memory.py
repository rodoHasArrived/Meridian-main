#!/usr/bin/env python3
"""Focused validation for check-codex-memory.py behavior."""

from __future__ import annotations

import contextlib
import importlib.util
import io
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

    def test_unknown_tier_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_valid_memory_tree(root)
            text = valid_index().replace("tier: repo", "tier: global", 1)
            write(root / ".codex" / "memory" / "index.yml", text)

            findings, _ = check_codex_memory.collect_findings(root)

            self.assertTrue(any("Disabled tier" in finding.message for finding in findings))

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
