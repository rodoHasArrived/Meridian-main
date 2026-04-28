#!/usr/bin/env python3
"""Focused validation for check-ai-inventory.py behavior."""

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "check-ai-inventory.py"
SPEC = importlib.util.spec_from_file_location("check_ai_inventory", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load check-ai-inventory module from {MODULE_PATH}")

check_ai_inventory = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = check_ai_inventory
SPEC.loader.exec_module(check_ai_inventory)


def write(path: Path, text: str = "placeholder\n") -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def write_required_docs(root: Path, body: str) -> None:
    for rel_path in (
        "docs/ai/README.md",
        "docs/ai/assistant-workflow-contract.md",
        "docs/ai/agents/README.md",
        "docs/ai/skills/README.md",
        "docs/ai/prompts/README.md",
        "docs/ai/instructions/README.md",
        ".codex/skills/README.md",
        ".github/prompts/README.md",
    ):
        write(root / rel_path, body)


class CheckAiInventoryTests(unittest.TestCase):
    def test_collect_inventory_discovers_supported_ai_surfaces(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root / "AGENTS.md")
            write(root / "CLAUDE.md")
            write(root / ".codex" / "config.toml")
            write(root / ".codex" / "environments" / "environment.toml")
            write(root / ".codex" / "skills" / "meridian-test" / "SKILL.md")
            write(root / ".claude" / "settings.json")
            write(root / ".claude" / "settings.local.json")
            write(root / ".claude" / "agents" / "meridian-test.md")
            write(root / ".github" / "copilot-instructions.md")
            write(root / ".github" / "prompts" / "sample.prompt.yml")
            write(root / ".github" / "instructions" / "sample.instructions.md")
            write(root / "src" / "Meridian.McpServer" / "Tools" / "SampleTools.cs")
            write(root / "docs" / "ai" / "README.md")

            inventory = check_ai_inventory.collect_inventory(root)
            pairs = {(item.kind, item.name) for item in inventory}

            self.assertIn(("entrypoint", "AGENTS.md"), pairs)
            self.assertIn(("entrypoint", "CLAUDE.md"), pairs)
            self.assertIn(("config", "config.toml"), pairs)
            self.assertIn(("environment-config", "environment.toml"), pairs)
            self.assertIn(("config", "settings.json"), pairs)
            self.assertIn(("config", "settings.local.json"), pairs)
            self.assertIn(("instruction-entrypoint", "copilot-instructions.md"), pairs)
            self.assertIn(("skill", "meridian-test"), pairs)
            self.assertIn(("agent", "meridian-test.md"), pairs)
            self.assertIn(("prompt", "sample.prompt.yml"), pairs)
            self.assertIn(("path-instruction", "sample.instructions.md"), pairs)
            self.assertIn(("mcp-tool", "SampleTools.cs"), pairs)
            self.assertIn(("ai-doc", "README.md"), pairs)

    def test_check_catalog_drift_reports_missing_agent_index_entry(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root / ".github" / "agents" / "new-agent.md")
            write_required_docs(
                root,
                "\n".join(
                    [
                        "Root assistant compatibility AGENTS.md CLAUDE.md",
                        "GitHub Copilot .github/agents .github/prompts .github/instructions",
                        "Shared AI documentation docs/ai/ .codex/skills/_shared/project-context.md",
                    ]
                ),
            )

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertTrue(any(finding.name == "new-agent.md" for finding in findings))
            self.assertTrue(any(finding.expected_doc == "docs/ai/agents/README.md" for finding in findings))

    def test_check_catalog_drift_passes_when_indexes_reference_assets(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root / "AGENTS.md")
            write(root / "CLAUDE.md")
            write(root / ".codex" / "config.toml")
            write(root / ".codex" / "skills" / "meridian-test" / "SKILL.md")
            write(root / ".claude" / "settings.json")
            write(root / ".claude" / "skills" / "meridian-test" / "SKILL.md")
            write(root / ".github" / "copilot-instructions.md")
            write(root / ".github" / "agents" / "new-agent.md")
            write(root / ".github" / "prompts" / "sample.prompt.yml")
            write(root / ".github" / "instructions" / "sample.instructions.md")
            write(root / ".github" / "workflows" / "prompt-generation.yml")
            write(root / ".github" / "workflows" / "copilot-setup-steps.yml")
            write(root / "src" / "Meridian.McpServer" / "Tools" / "SampleTools.cs")

            indexed = "\n".join(
                [
                    "Root assistant compatibility AGENTS.md CLAUDE.md",
                    "Codex .codex/config.toml .codex/environments/ .codex/skills OpenAI/Codex meridian-test",
                    "Claude / Claude Code .claude/settings.json .claude/settings.local.json .claude/agents .claude/skills meridian-test",
                    "GitHub Copilot .github/copilot-instructions.md .github/agents .github/prompts .github/instructions new-agent.md sample.prompt.yml sample.instructions.md",
                    "MCP-compatible clients src/Meridian.Mcp src/Meridian.McpServer",
                    "AI automation workflows prompt-generation.yml skill-evals.yml .github/workflows/copilot-*",
                    "Reusable prompt templates .github/prompts/ docs/ai/prompts/README.md",
                    "Shared AI documentation docs/ai/ .codex/skills/_shared/project-context.md",
                ]
            )
            write_required_docs(root, indexed)

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertEqual([], findings)

    def test_build_payload_uses_portable_repository_identity(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)

            payload = check_ai_inventory.build_payload(root, [], [])

            self.assertEqual(".", payload["repositoryRoot"])
            self.assertEqual(root.name, payload["repositoryName"])
            self.assertNotIn(str(root), check_ai_inventory.render_markdown(payload))


if __name__ == "__main__":
    unittest.main()
