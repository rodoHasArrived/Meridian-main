import json
import subprocess
from pathlib import Path

from .utils import ensure_directory


class DependencyGraph:
    def __init__(self, root: Path, project: str) -> None:
        self.root = root
        self.project = project

    def generate(self, output_dir: Path) -> dict:
        ensure_directory(output_dir)
        data = self._dotnet_list()
        json_path = output_dir / "dependency-graph.json"
        dot_path = output_dir / "dependency-graph.dot"
        json_path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
        dot_path.write_text(self._to_dot(data), encoding="utf-8")
        return data

    def _dotnet_list(self) -> dict:
        result = subprocess.run(
            [
                "dotnet",
                "list",
                self.project,
                "package",
                "--include-transitive",
                "--format",
                "json",
            ],
            cwd=str(self.root),
            capture_output=True,
            text=True,
            check=False,
        )
        if result.returncode != 0:
            return {
                "error": "dotnet list package failed",
                "details": result.stdout + result.stderr,
            }
        return json.loads(result.stdout)

    def _to_dot(self, data: dict) -> str:
        lines = ["digraph dependencies {", "  rankdir=LR;"]
        for project in data.get("projects", []):
            project_name = project.get("name", "project")
            lines.append(f'  "{project_name}" [shape=box];')
            frameworks = project.get("frameworks", [])
            for framework in frameworks:
                for dependency in framework.get("dependencies", []):
                    dep_name = dependency.get("name")
                    dep_version = dependency.get("resolvedVersion") or dependency.get("version")
                    if dep_name:
                        label = dep_name
                        if dep_version:
                            label = f"{dep_name} {dep_version}"
                        lines.append(f'  "{project_name}" -> "{label}";')
        lines.append("}")
        return "\n".join(lines) + "\n"
