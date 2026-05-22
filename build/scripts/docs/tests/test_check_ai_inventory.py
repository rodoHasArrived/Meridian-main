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
    body_with_policy = "\n".join(
        [
            body,
            "No mobile development lane",
            (
                "do not create mobile applications, mobile-specific product surfaces, native iOS/Android clients, "
                "MAUI clients, React Native clients, Flutter clients, or mobile-first workflows"
            ),
        ]
    )
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
        write(root / rel_path, body_with_policy)


def write_ui_platform_policy_docs(root: Path, body: str | None = None) -> None:
    policy = body or (
        "No mobile development lane\n"
        "do not create mobile applications, mobile-specific product surfaces, native iOS/Android clients, "
        "MAUI clients, React Native clients, Flutter clients, or mobile-first workflows\n"
    )
    for rel_path in check_ai_inventory.UI_PLATFORM_POLICY_FILES:
        write(root / rel_path, policy)


class CheckAiInventoryTests(unittest.TestCase):
    def test_collect_inventory_discovers_supported_ai_surfaces(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_ui_platform_policy_docs(root)
            write(root / ".codex" / "config.toml")
            write(root / ".codex" / "environments" / "environment.toml")
            write(root / ".codex" / "skills" / "meridian-test" / "SKILL.md")
            write(root / ".agents" / "skills" / "meridian-portable" / "SKILL.md")
            write(root / ".agents" / "skills" / "meridian-portable" / "agents" / "openai.yaml")
            write(root / ".agents" / "skills" / "_shared" / "project-context.md")
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
            surface_pairs = {(item.surface, item.kind, item.name) for item in inventory}

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
            self.assertIn(("agent-skills-compatible-hosts", "skill", "meridian-portable"), surface_pairs)
            self.assertIn(("agent-skills-compatible-hosts", "openai-metadata", "meridian-portable"), surface_pairs)
            self.assertIn(("agent-skills-compatible-hosts", "shared-context", "project-context.md"), surface_pairs)

    def test_collect_inventory_discovers_optional_assistant_surfaces(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root / ".cursor" / "rules" / "meridian.mdc")
            write(root / ".windsurfrules")
            write(root / ".continue" / "config.yaml")
            write(root / ".clinerules")
            write(root / ".roo" / "rules" / "meridian.md")
            write(root / "GEMINI.md")

            inventory = check_ai_inventory.collect_inventory(root)
            paths = {
                item.path
                for item in inventory
                if item.kind == "optional-assistant-surface"
            }

            self.assertIn(".cursor/rules/meridian.mdc", paths)
            self.assertIn(".windsurfrules", paths)
            self.assertIn(".continue/config.yaml", paths)
            self.assertIn(".clinerules", paths)
            self.assertIn(".roo/rules/meridian.md", paths)
            self.assertIn("GEMINI.md", paths)

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

    def test_check_catalog_drift_reports_missing_agent_skills_index_entry(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root / ".agents" / "skills" / "new-skill" / "SKILL.md")
            write_required_docs(
                root,
                "\n".join(
                    [
                        "Root assistant compatibility AGENTS.md CLAUDE.md",
                        "Codex .codex/config.toml .codex/environments/ .codex/skills OpenAI/Codex",
                        "Agent Skills-compatible hosts .agents/skills open-agent-skills-v1",
                        "GitHub Copilot .github/agents .github/prompts .github/instructions",
                        "Reusable prompt templates .github/prompts/ docs/ai/prompts/README.md",
                        "Shared AI documentation docs/ai/ .codex/skills/_shared/project-context.md",
                    ]
                ),
            )

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertTrue(any(finding.name == "new-skill" for finding in findings))
            self.assertTrue(any(finding.expected_doc == "docs/ai/skills/README.md" for finding in findings))

    def test_check_catalog_drift_reports_undocumented_optional_assistant_surface(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root / ".cursor" / "rules" / "meridian.mdc")
            write_required_docs(root, "Shared AI documentation docs/ai/")

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertTrue(
                any(
                    finding.kind == "optional-assistant-surface"
                    and finding.path == ".cursor/rules/meridian.mdc"
                    and finding.expected_doc == "docs/ai/assistant-workflow-contract.md"
                    for finding in findings
                )
            )

    def test_check_catalog_drift_reports_missing_workflow_reference(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_required_docs(root, "Shared AI documentation docs/ai/ .codex/skills/_shared/project-context.md")
            write(
                root / "docs" / "prompts" / "automation-prompts.md",
                "Use `.github/workflows/prompt-generation.yml` to regenerate prompts.\n",
            )

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertTrue(
                any(
                    finding.kind == "missing-workflow-reference"
                    and finding.name == "prompt-generation.yml"
                    and finding.path == "docs/prompts/automation-prompts.md"
                    for finding in findings
                )
            )

    def test_check_catalog_drift_allows_existing_workflow_reference(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_required_docs(root, "Shared AI documentation docs/ai/ .codex/skills/_shared/project-context.md")
            write(root / ".github" / "workflows" / "ci.yml")
            write(
                root / "docs" / "prompts" / "automation-prompts.md",
                "Use `.github/workflows/ci.yml` for the active CI workflow.\n",
            )

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertFalse(any(finding.kind == "missing-workflow-reference" for finding in findings))

    def test_check_catalog_drift_passes_when_optional_assistant_surface_is_documented(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root / ".cursor" / "rules" / "meridian.mdc")
            indexed = "\n".join(
                [
                    "Root assistant compatibility AGENTS.md CLAUDE.md",
                    "Codex .codex/config.toml .codex/environments/ .codex/skills OpenAI/Codex",
                    "Claude / Claude Code .claude/settings.json .claude/settings.local.json .claude/agents .claude/skills",
                    "GitHub Copilot .github/agents .github/prompts .github/instructions",
                    "Reusable prompt templates .github/prompts/ docs/ai/prompts/README.md",
                    "Shared AI documentation docs/ai/ .codex/skills/_shared/project-context.md",
                    ".cursor/rules/meridian.mdc",
                ]
            )
            write_required_docs(root, indexed)

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertEqual([], findings)

    def test_check_catalog_drift_passes_when_indexes_reference_assets(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            policy = (
                "No mobile development lane\n"
                "do not create mobile applications, mobile-specific product surfaces, native iOS/Android clients, "
                "MAUI clients, React Native clients, Flutter clients, or mobile-first workflows\n"
            )
            write(root / "AGENTS.md", policy)
            write(root / "CLAUDE.md", policy)
            write(root / ".codex" / "config.toml")
            write(root / ".codex" / "skills" / "meridian-test" / "SKILL.md")
            write(root / ".claude" / "settings.json")
            write(root / ".claude" / "skills" / "meridian-test" / "SKILL.md")
            write(
                root / ".github" / "copilot-instructions.md",
                policy,
            )
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

    def test_check_catalog_drift_reports_legacy_canonical_repository_links(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_required_docs(root, "Shared AI documentation docs/ai/")
            write(
                root / "docs" / "ai" / "README.md",
                "https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md\n",
            )

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertTrue(any(finding.kind == "legacy-repository-link" for finding in findings))
            self.assertTrue(any(finding.path == "docs/ai/README.md" for finding in findings))

    def test_check_catalog_drift_allows_historical_workflow_evidence_links(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_required_docs(root, "Shared AI documentation docs/ai/")
            write(
                root / "docs" / "ai" / "ai-known-errors.md",
                "https://github.com/rodoHasArrived/Meridian/actions/runs/123456\n",
            )

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertFalse(any(finding.kind == "legacy-repository-link" for finding in findings))

    def test_check_catalog_drift_reports_copilot_repository_tree_duplication(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_required_docs(root, "Shared AI documentation docs/ai/")
            write_ui_platform_policy_docs(root)
            write(
                root / "docs" / "ai" / "copilot" / "instructions.md",
                "\n".join(
                    [
                        "# Meridian Copilot Guide",
                        "",
                        "## Repository Structure",
                        "",
                        "```text",
                        "Meridian-main",
                        "src/",
                        "```",
                    ]
                ),
            )

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertTrue(any(finding.kind == "duplicated-repository-tree" for finding in findings))

    def test_check_catalog_drift_reports_claude_repository_tree_duplication(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_required_docs(root, "Shared AI documentation docs/ai/")
            write_ui_platform_policy_docs(root)
            write(
                root / "CLAUDE.md",
                "\n".join(
                    [
                        "# Claude Guide",
                        "",
                        "## Repository Structure",
                        "",
                        "```text",
                        "Meridian-main",
                        "src/",
                        "```",
                    ]
                ),
            )

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertTrue(
                any(
                    finding.kind == "duplicated-repository-tree" and finding.path == "CLAUDE.md"
                    for finding in findings
                )
            )

    def test_check_catalog_drift_allows_compact_copilot_navigation_guide(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_required_docs(root, "Shared AI documentation docs/ai/")
            write_ui_platform_policy_docs(root)
            write(
                root / "docs" / "ai" / "copilot" / "instructions.md",
                "## Repository Navigation\nUse docs/ai/generated/repo-navigation.md for routing.\n",
            )

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertFalse(any(finding.kind == "duplicated-repository-tree" for finding in findings))

    def test_check_catalog_drift_reports_missing_ui_platform_policy(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_required_docs(root, "Shared AI documentation docs/ai/")
            write_ui_platform_policy_docs(root)
            write(root / "CLAUDE.md", "Browser workstation only.\n")

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertTrue(
                any(
                    finding.kind == "ui-platform-policy" and finding.path == "CLAUDE.md"
                    for finding in findings
                )
            )

    def test_check_catalog_drift_reports_missing_agent_skills_shared_policy(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_required_docs(root, "Shared AI documentation docs/ai/")
            write_ui_platform_policy_docs(root)
            write(
                root / ".agents" / "skills" / "_shared" / "project-context.md",
                "Portable skills context.\n",
            )

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertTrue(
                any(
                    finding.kind == "ui-platform-policy"
                    and finding.path == ".agents/skills/_shared/project-context.md"
                    for finding in findings
                )
            )

    def test_check_catalog_drift_passes_when_ui_platform_policy_is_mirrored(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_required_docs(root, "Shared AI documentation docs/ai/")
            write_ui_platform_policy_docs(root)

            inventory = check_ai_inventory.collect_inventory(root)
            findings = check_ai_inventory.check_catalog_drift(root, inventory)

            self.assertFalse(any(finding.kind == "ui-platform-policy" for finding in findings))

    def test_build_payload_uses_portable_repository_identity(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)

            payload = check_ai_inventory.build_payload(root, [], [])

            self.assertEqual(".", payload["repositoryRoot"])
            self.assertEqual(root.name, payload["repositoryName"])
            self.assertNotIn(str(root), check_ai_inventory.render_markdown(payload))


if __name__ == "__main__":
    unittest.main()
