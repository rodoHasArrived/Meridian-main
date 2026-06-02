from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "context_budget.py"
SPEC = importlib.util.spec_from_file_location("context_budget", MODULE_PATH)
context_budget = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = context_budget
SPEC.loader.exec_module(context_budget)


class ContextBudgetTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.root = Path(self.temp_dir.name)

        (self.root / "docs/ai/generated").mkdir(parents=True, exist_ok=True)
        (self.root / "docs/ai").mkdir(parents=True, exist_ok=True)
        (self.root / "src/Meridian.Wpf/ViewModels").mkdir(parents=True, exist_ok=True)
        (self.root / "src/Meridian.Storage/Archival").mkdir(parents=True, exist_ok=True)

        (self.root / "docs/ai/navigation.md").write_text("# Navigation\n\n## Desktop\nWPF desktop workflow context.\n", encoding="utf-8")
        (self.root / "src/Meridian.Wpf/ViewModels/MainVm.cs").write_text(
            "\n".join(["public class MainVm { }" for _ in range(80)]),
            encoding="utf-8",
        )
        (self.root / "src/Meridian.Storage/Archival/WriteAheadLog.cs").write_text(
            "\n".join(["public class WriteAheadLog { }" for _ in range(80)]),
            encoding="utf-8",
        )

        self.navigation = {
            "subsystems": [
                {
                    "id": "workstation-ui",
                    "title": "Desktop and UI Workflows",
                    "projectPaths": ["src/Meridian.Wpf"],
                    "keywords": ["wpf", "desktop", "viewmodel"],
                    "keyContracts": ["src/Meridian.Wpf/ViewModels/MainVm.cs"],
                    "entrypoints": ["src/Meridian.Wpf"],
                    "relatedDocs": ["docs/ai/navigation.md"],
                },
                {
                    "id": "providers-data",
                    "title": "Providers and Storage",
                    "projectPaths": ["src/Meridian.Storage"],
                    "keywords": ["storage", "wal"],
                    "keyContracts": ["src/Meridian.Storage/Archival/WriteAheadLog.cs"],
                    "entrypoints": ["src/Meridian.Storage"],
                    "relatedDocs": [],
                },
            ],
            "taskRoutes": [
                {
                    "id": "wpf-workflow",
                    "title": "WPF route",
                    "keywords": ["wpf", "desktop"],
                    "subsystems": ["workstation-ui"],
                    "authoritativeDocs": ["docs/ai/navigation.md"],
                    "startSymbols": [{"path": "src/Meridian.Wpf/ViewModels/MainVm.cs"}],
                }
            ],
        }

        self.policy = {
            "defaultRouteId": "analysis-economy",
            "modelClasses": [
                {"id": "economy", "maxContextTokens": 40},
                {"id": "balanced", "maxContextTokens": 70},
                {"id": "research", "maxContextTokens": 120},
            ],
            "routingRules": [
                {"id": "analysis-economy", "taskType": "analysis", "modelClass": "economy"},
                {"id": "implementation-balanced", "taskType": "implementation", "modelClass": "balanced"},
                {"id": "governance-research", "taskType": "governance", "modelClass": "research"},
            ],
        }

    def test_classification_prefers_target_subsystem(self) -> None:
        classified = context_budget.classify_task(
            navigation=self.navigation,
            policy=self.policy,
            task="Fix WPF viewmodel startup bug",
            target_files=["src/Meridian.Wpf/ViewModels/MainVm.cs"],
        )
        self.assertEqual("workstation-ui", classified.subsystem_id)
        self.assertEqual("implementation", classified.task_type)

    def test_generated_pack_stays_within_budget(self) -> None:
        payload = context_budget.generate_context_pack(
            root=self.root,
            navigation=self.navigation,
            policy=self.policy,
            task="Analyze WPF desktop issue",
            target_files=["src/Meridian.Wpf/ViewModels/MainVm.cs"],
            task_type_override="analysis",
            max_files=5,
        )

        self.assertTrue(payload["totals"]["withinBudget"])
        self.assertLessEqual(payload["totals"]["estimatedTokens"], payload["totals"]["maxContextTokens"])
        self.assertGreaterEqual(len(payload["contextPack"]), 1)
        self.assertIn("lineRange", payload["contextPack"][0])

    def test_multi_subsystem_targets_trigger_deep_review(self) -> None:
        payload = context_budget.generate_context_pack(
            root=self.root,
            navigation=self.navigation,
            policy=self.policy,
            task="Cross-cutting update across desktop and WAL handling",
            target_files=[
                "src/Meridian.Wpf/ViewModels/MainVm.cs",
                "src/Meridian.Storage/Archival/WriteAheadLog.cs",
            ],
            max_files=6,
        )

        self.assertEqual("Deep Review", payload["classification"]["mode"])
        self.assertTrue(payload["escalation"]["reasons"])
        self.assertIsNotNone(payload["escalation"]["logEntry"])

    def test_main_writes_json_payload(self) -> None:
        navigation_path = self.root / "docs/ai/generated/repo-navigation.json"
        policy_path = self.root / "docs/ai/model-routing-policy.json"
        output_path = self.root / "context-pack.json"

        navigation_path.write_text(json.dumps(self.navigation), encoding="utf-8")
        policy_path.write_text(json.dumps(self.policy), encoding="utf-8")

        original_argv = sys.argv[:]
        try:
            sys.argv = [
                "context_budget.py",
                "--root",
                str(self.root),
                "--task",
                "Fix WPF desktop shell issue",
                "--target-file",
                "src/Meridian.Wpf/ViewModels/MainVm.cs",
                "--json-output",
                str(output_path),
            ]
            self.assertEqual(0, context_budget.main())
        finally:
            sys.argv = original_argv

        payload = json.loads(output_path.read_text(encoding="utf-8"))
        self.assertIn("contextPack", payload)
        self.assertIn("classification", payload)


if __name__ == "__main__":
    unittest.main()
