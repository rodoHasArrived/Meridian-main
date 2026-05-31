#!/usr/bin/env python3
"""Focused validation for check-ai-handoff.py."""

from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "check-ai-handoff.py"
SPEC = importlib.util.spec_from_file_location("check_ai_handoff", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load check-ai-handoff module from {MODULE_PATH}")

check_ai_handoff = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = check_ai_handoff
SPEC.loader.exec_module(check_ai_handoff)


def write_text(root: Path, rel_path: str, content: str) -> None:
    path = root / rel_path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


REQUIRED_GUIDANCE_TEXT = "\n".join(
    [
        "Reference: docs/ai/agent-handoff-checklist.md",
        "Reference: docs/ai/parallel-task-manifest-template.md",
        "Reference: docs/ai/work-modes.md",
        "",
    ]
)


def setup_host_root(root: Path, checklist: str) -> None:
    write_text(root, "docs/ai/agent-handoff-checklist.md", checklist)
    for rel_path in (
        "CLAUDE.md",
        ".codex/AGENTS.md",
        ".github/copilot-instructions.md",
        "docs/ai/README.md",
        "docs/ai/assistant-workflow-contract.md",
        "docs/ai/codex/README.md",
        "docs/ai/copilot/instructions.md",
        "docs/ai/skills/README.md",
        "docs/ai/agent-handoff-host-target.md",
    ):
        write_text(root, rel_path, REQUIRED_GUIDANCE_TEXT)


def write_host_config(root: Path, targets: list[object]) -> Path:
    path = root / "build" / "scripts" / "docs" / "ai-handoff-host-targets.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({"targets": targets}, indent=2) + "\n", encoding="utf-8")
    return path


class CheckAiHandoffTests(unittest.TestCase):
    def test_run_passes_with_required_references(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            setup_host_root(root, "## 3) Required Handoff Packet\n\nThis is required.")
            result = check_ai_handoff.run(root)
            self.assertEqual(0, result)

    def test_run_falls_back_to_default_required_targets_when_config_missing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            setup_host_root(root, "## 3) Required Handoff Packet\n\nThis is required.")
            result = check_ai_handoff.run(root, host_targets=Path("build/scripts/docs/ai-handoff-host-targets.json"))
            self.assertEqual(0, result)

    def test_run_uses_custom_host_targets_config(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            setup_host_root(root, "## 3) Required Handoff Packet\n\nThis is required.")
            write_host_config(
                root,
                [
                    "CLAUDE.md",
                    "docs/ai/agent-handoff-host-target.md",
                    "docs/ai/assistant-workflow-contract.md",
                ],
            )
            result = check_ai_handoff.run(
                root,
                host_targets=Path("build/scripts/docs/ai-handoff-host-targets.json"),
            )
            self.assertEqual(0, result)

    def test_run_reports_invalid_host_targets_config(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            setup_host_root(root, "## 3) Required Handoff Packet\n\nThis is required.")
            config_path = root / "build" / "scripts" / "docs" / "ai-handoff-host-targets.json"
            config_path.parent.mkdir(parents=True, exist_ok=True)
            config_path.write_text("{ invalid json }", encoding="utf-8")

            result = check_ai_handoff.run(
                root,
                host_targets=Path("build/scripts/docs/ai-handoff-host-targets.json"),
            )
            self.assertEqual(1, result)

    def test_run_reports_missing_checklist_file(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for rel_path in (
                "CLAUDE.md",
                ".codex/AGENTS.md",
                ".github/copilot-instructions.md",
                "docs/ai/README.md",
                "docs/ai/assistant-workflow-contract.md",
                "docs/ai/codex/README.md",
                "docs/ai/copilot/instructions.md",
                "docs/ai/skills/README.md",
            ):
                write_text(root, rel_path, REQUIRED_GUIDANCE_TEXT)
            result = check_ai_handoff.run(root)
            self.assertEqual(1, result)

    def test_run_reports_missing_required_section(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            setup_host_root(root, "No required packet section here.")
            result = check_ai_handoff.run(root)
            self.assertEqual(1, result)

    def test_run_strict_mode_rejects_missing_required_fields(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            setup_host_root(root, "## 3) Required Handoff Packet\n\n- [ ] Some section\n")
            result = check_ai_handoff.run(root, strict=True)
            self.assertEqual(1, result)

    def test_run_reports_missing_reference_from_one_host_file(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            setup_host_root(root, "## 3) Required Handoff Packet\n\nThis is required.")
            write_text(root, "docs/ai/README.md", "No reference here.\n")
            result = check_ai_handoff.run(root)
            self.assertEqual(1, result)

    def test_run_supports_root_required_references_override(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            setup_host_root(root, "## 3) Required Handoff Packet\n\nThis is required.")
            write_text(root, "docs/ai/agent-handoff-host-target.md", "Reference: docs/ai/agent-handoff-checklist.md\n")
            config_path = root / "build" / "scripts" / "docs" / "ai-handoff-host-targets.json"
            config_path.parent.mkdir(parents=True, exist_ok=True)
            config_path.write_text(
                json.dumps(
                    {
                        "requiredReferences": ["agent-handoff-checklist.md"],
                        "targets": ["docs/ai/agent-handoff-host-target.md"],
                    },
                    indent=2,
                )
                + "\n",
                encoding="utf-8",
            )

            result = check_ai_handoff.run(
                root,
                host_targets=Path("build/scripts/docs/ai-handoff-host-targets.json"),
            )
            self.assertEqual(0, result)

    def test_run_supports_per_target_required_references_override(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            setup_host_root(root, "## 3) Required Handoff Packet\n\nThis is required.")
            write_text(root, "docs/ai/agent-handoff-host-target.md", "Reference: docs/ai/agent-handoff-checklist.md\n")
            config_path = root / "build" / "scripts" / "docs" / "ai-handoff-host-targets.json"
            config_path.parent.mkdir(parents=True, exist_ok=True)
            config_path.write_text(
                json.dumps(
                    {
                        "targets": [
                            {
                                "path": "docs/ai/agent-handoff-host-target.md",
                                "requiredReferences": ["agent-handoff-checklist.md"],
                            }
                        ]
                    },
                    indent=2,
                )
                + "\n",
                encoding="utf-8",
            )

            result = check_ai_handoff.run(
                root,
                host_targets=Path("build/scripts/docs/ai-handoff-host-targets.json"),
            )
            self.assertEqual(0, result)

    def test_run_reports_invalid_root_required_references(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            setup_host_root(root, "## 3) Required Handoff Packet\n\nThis is required.")
            config_path = root / "build" / "scripts" / "docs" / "ai-handoff-host-targets.json"
            config_path.parent.mkdir(parents=True, exist_ok=True)
            config_path.write_text(
                json.dumps(
                    {
                        "requiredReferences": "agent-handoff-checklist.md",
                        "targets": ["CLAUDE.md"],
                    },
                    indent=2,
                )
                + "\n",
                encoding="utf-8",
            )
            result = check_ai_handoff.run(
                root,
                host_targets=Path("build/scripts/docs/ai-handoff-host-targets.json"),
            )
            self.assertEqual(1, result)

    def test_run_strict_mode_accepts_complete_fields(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            checklist = (
                "## 3) Required Handoff Packet\n\n"
                "- `Scope`: what was requested and excluded.\n"
                "- `Inputs loaded`: files and rationale.\n"
                "- `Changes made`: files edited and reasons.\n"
                "- `Validation`: commands and outcomes.\n"
                "- `Open risks`: residual risks.\n"
                "- `Next lane`: requested next action.\n"
                "- `Required context`: files to read first.\n"
                "- `Optional context`: helpful background.\n"
            )
            setup_host_root(root, checklist)
            result = check_ai_handoff.run(root, strict=True)
            self.assertEqual(0, result)

    def test_json_output_is_machine_readable(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            setup_host_root(root, "## 3) Required Handoff Packet\n\nThis is required.")
            json_path = root / "docs" / "status" / "ai-handoff-checklist-report.json"
            result = check_ai_handoff.run(root, json_path=json_path)
            self.assertEqual(0, result)
            payload = json.loads(json_path.read_text(encoding="utf-8"))
            self.assertEqual("pass", payload["status"])
            self.assertGreater(payload["checksTotal"], 0)
            self.assertEqual(payload["checksTotal"], payload["checksPassed"] + payload["checksFailed"])


if __name__ == "__main__":
    unittest.main()
