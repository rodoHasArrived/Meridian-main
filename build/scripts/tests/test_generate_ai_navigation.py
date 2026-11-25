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
        self.assertIn("browser-workstation", route_ids)
        self.assertIn("storage-investigation", route_ids)
        self.assertIn("mcp-surface", route_ids)

        self.assertIn("Meridian.Ui.Dashboard", project_names)
        self.assertIn("Meridian.ProviderSdk", project_names)
        self.assertIn("Meridian.Storage", project_names)
        self.assertIn("Meridian.Mcp", project_names)

    def test_generated_dataset_references_existing_paths(self) -> None:
        dataset = nav.build_dataset(nav.REPO_ROOT)

        for subsystem in dataset["subsystems"]:
            for path in subsystem["entrypoints"] + subsystem["keyContracts"] + subsystem["relatedDocs"]:
                self.assertTrue((nav.REPO_ROOT / path).exists(), path)

        for symbol in dataset["symbols"]:
            self.assertTrue((nav.REPO_ROOT / symbol["path"]).exists(), symbol["path"])

        for document in dataset["documents"]:
            self.assertTrue((nav.REPO_ROOT / document["path"]).exists(), document["path"])

    def test_project_index_lists_every_source_and_test_project(self) -> None:
        dataset = nav.build_dataset(nav.REPO_ROOT)
        index = {item["name"]: item for item in dataset["projectIndex"]}

        for area in ("src", "tests"):
            for project_file in list(nav.REPO_ROOT.glob(f"{area}/*/*.csproj")) + list(nav.REPO_ROOT.glob(f"{area}/*/*.fsproj")):
                self.assertIn(project_file.stem, index, project_file)
                self.assertEqual(index[project_file.stem]["path"], f"{area}/{project_file.parent.name}")

        for item in dataset["projectIndex"]:
            self.assertTrue((nav.REPO_ROOT / item["path"]).exists(), item["path"])

        self.assertTrue(index["Meridian.Wpf"]["catalogued"])
        self.assertEqual(index["Meridian.Wpf"]["subsystem"], "workstation-ui")
        self.assertIn("Meridian.Wpf", index["Meridian.Wpf.Tests"]["covers"])

        seeded = {item["name"] for item in dataset["projects"]}
        for item in dataset["projectIndex"]:
            if item["area"] == "source":
                self.assertEqual(item["catalogued"], item["name"] in seeded, item["name"])

    def test_project_index_keeps_unseeded_projects(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            for relative in ("src/Meridian.Brand.New/Meridian.Brand.New.csproj", "src/Meridian.Wpf/Meridian.Wpf.csproj"):
                (root / relative).parent.mkdir(parents=True)
                (root / relative).write_text("<Project />", encoding="utf-8")
            test_project = root / "tests/Meridian.Brand.New.Tests/Meridian.Brand.New.Tests.csproj"
            test_project.parent.mkdir(parents=True)
            test_project.write_text(
                '<Project><ItemGroup><ProjectReference Include="..\\..\\src\\Meridian.Brand.New\\Meridian.Brand.New.csproj" /></ItemGroup></Project>',
                encoding="utf-8",
            )
            ignored = root / "src/Meridian.Brand.New/obj/Ignored/Ignored.csproj"
            ignored.parent.mkdir(parents=True)
            ignored.write_text("<Project />", encoding="utf-8")

            index = {item["name"]: item for item in nav.discover_project_index(root)}

            self.assertNotIn("Meridian.Brand.New", {item["name"] for item in nav.discover_projects(root)})
            self.assertFalse(index["Meridian.Brand.New"]["catalogued"])
            self.assertIsNone(index["Meridian.Brand.New"]["subsystem"])
            self.assertTrue(index["Meridian.Wpf"]["catalogued"])
            self.assertEqual(index["Meridian.Brand.New.Tests"]["covers"], ["Meridian.Brand.New"])
            self.assertNotIn("Ignored", index)

            markdown = "\n".join(nav.render_project_index(list(index.values())))
            self.assertIn("| `Meridian.Brand.New` | `src/Meridian.Brand.New` | _uncatalogued_ |", markdown)
            self.assertIn("| `Meridian.Brand.New.Tests` | `tests/Meridian.Brand.New.Tests` | `Meridian.Brand.New` |", markdown)
            self.assertIn("1 source project(s) are uncatalogued", markdown)

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
            self.assertIn("recentChanges", written)
            self.assertIn("# Meridian AI Repo Navigation", markdown)


if __name__ == "__main__":
    unittest.main()
