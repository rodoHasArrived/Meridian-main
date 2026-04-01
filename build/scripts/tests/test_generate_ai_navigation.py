from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "docs" / "generate-ai-navigation.py"
SPEC = importlib.util.spec_from_file_location("generate_ai_navigation", SCRIPT_PATH)
assert SPEC and SPEC.loader
nav = importlib.util.module_from_spec(SPEC)
sys.modules["generate_ai_navigation"] = nav
SPEC.loader.exec_module(nav)


class GenerateAiNavigationTests(unittest.TestCase):
    def test_build_dataset_contains_expected_subsystems_and_routes(self) -> None:
        dataset = nav.build_dataset(nav.REPO_ROOT)

        subsystem_ids = {item["id"] for item in dataset["subsystems"]}
        route_ids = {item["id"] for item in dataset["taskRoutes"]}
        project_names = {item["name"] for item in dataset["projects"]}

        self.assertIn("providers-data", subsystem_ids)
        self.assertIn("workstation-ui", subsystem_ids)
        self.assertIn("mcp-integration", subsystem_ids)

        self.assertIn("provider-work", route_ids)
        self.assertIn("wpf-workflow", route_ids)
        self.assertIn("storage-investigation", route_ids)
        self.assertIn("mcp-surface", route_ids)

        self.assertIn("Meridian.ProviderSdk", project_names)
        self.assertIn("Meridian.Storage", project_names)
        self.assertIn("Meridian.McpServer", project_names)

    def test_generated_dataset_references_existing_paths(self) -> None:
        dataset = nav.build_dataset(nav.REPO_ROOT)

        for subsystem in dataset["subsystems"]:
            for path in subsystem["entrypoints"] + subsystem["keyContracts"] + subsystem["relatedDocs"]:
                self.assertTrue((nav.REPO_ROOT / path).exists(), path)

        for symbol in dataset["symbols"]:
            self.assertTrue((nav.REPO_ROOT / symbol["path"]).exists(), symbol["path"])

        for document in dataset["documents"]:
            self.assertTrue((nav.REPO_ROOT / document["path"]).exists(), document["path"])

    def test_main_writes_json_and_markdown_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            json_output = Path(temp_dir) / "repo-navigation.json"
            markdown_output = Path(temp_dir) / "repo-navigation.md"

            original_argv = sys.argv[:]
            try:
                sys.argv = [
                    "generate-ai-navigation.py",
                    "--json-output",
                    str(json_output),
                    "--markdown-output",
                    str(markdown_output),
                ]
                self.assertEqual(nav.main(), 0)
            finally:
                sys.argv = original_argv

            written = json.loads(json_output.read_text(encoding="utf-8"))
            markdown = markdown_output.read_text(encoding="utf-8")

            self.assertIn("subsystems", written)
            self.assertIn("taskRoutes", written)
            self.assertIn("# Meridian AI Repo Navigation", markdown)


if __name__ == "__main__":
    unittest.main()
