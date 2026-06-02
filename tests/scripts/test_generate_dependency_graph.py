import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "build" / "scripts" / "docs" / "generate-dependency-graph.py"

spec = importlib.util.spec_from_file_location("generate_dependency_graph", MODULE_PATH)
module = importlib.util.module_from_spec(spec)
assert spec.loader is not None
sys.modules[spec.name] = module
spec.loader.exec_module(module)


def write_project(path: Path, body: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(body, encoding="utf-8")


class GenerateDependencyGraphTests(unittest.TestCase):
    def test_build_dependency_graph_includes_fsproj_and_skips_temp_projects(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            write_project(
                root / "src" / "Meridian.App" / "Meridian.App.csproj",
                """
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\\Meridian.FSharp\\Meridian.FSharp.fsproj" />
    <ProjectReference Include="$(GeneratedProject)" />
  </ItemGroup>
</Project>
""".strip(),
            )
            write_project(
                root / "src" / "Meridian.FSharp" / "Meridian.FSharp.fsproj",
                """
<Project Sdk="Microsoft.NET.Sdk">
</Project>
""".strip(),
            )
            write_project(
                root / "src" / "Meridian.Wpf" / "Meridian.Wpf_abc_wpftmp.csproj",
                """
<Project Sdk="Microsoft.NET.Sdk">
</Project>
""".strip(),
            )

            graph = module.build_dependency_graph(root)

            self.assertIn("Meridian.App", graph.projects)
            self.assertIn("Meridian.FSharp", graph.projects)
            self.assertNotIn("Meridian.Wpf_abc_wpftmp", graph.projects)
            self.assertEqual(
                [dependency.name for dependency in graph.projects["Meridian.App"].project_refs],
                ["Meridian.FSharp"],
            )
            self.assertEqual(graph.projects["Meridian.FSharp"].referenced_by, ["Meridian.App"])

    def test_generate_markdown_adds_summary_diagrams(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            write_project(
                root / "src" / "Meridian.Contracts" / "Meridian.Contracts.csproj",
                """
<Project Sdk="Microsoft.NET.Sdk">
</Project>
""".strip(),
            )
            write_project(
                root / "src" / "Meridian.Application" / "Meridian.Application.csproj",
                """
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\\Meridian.Contracts\\Meridian.Contracts.csproj" />
  </ItemGroup>
</Project>
""".strip(),
            )
            write_project(
                root / "src" / "Meridian.Ui.Shared" / "Meridian.Ui.Shared.csproj",
                """
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\\Meridian.Application\\Meridian.Application.csproj" />
  </ItemGroup>
</Project>
""".strip(),
            )

            graph = module.build_dependency_graph(root)
            markdown = module.generate_markdown(graph)

            self.assertIn("> This file is auto-generated. Do not edit manually.", markdown)
            self.assertIn("## Project Family Overview", markdown)
            self.assertIn("## Runtime Project Graph", markdown)
            self.assertIn("## Full Dependency Graph", markdown)


if __name__ == "__main__":
    unittest.main()
