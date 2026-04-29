import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def _environment_dependency_names() -> set[str]:
    dependencies: set[str] = set()
    in_dependencies = False

    for raw_line in (ROOT / "environment.yml").read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if line == "dependencies:":
            in_dependencies = True
            continue
        if not in_dependencies:
            continue
        if not line.startswith("- "):
            break

        dependency = line[2:].split("=", maxsplit=1)[0].strip().lower()
        if dependency:
            dependencies.add(dependency)

    return dependencies


class PythonPackageCondaDependencyTests(unittest.TestCase):
    def test_environment_includes_pillow_for_screenshot_diff_fixtures(self) -> None:
        dependencies = _environment_dependency_names()

        self.assertIn("pillow", dependencies)

    def test_python_package_workflow_installs_environment_file(self) -> None:
        workflow = (ROOT / ".github/workflows/python-package-conda.yml").read_text(
            encoding="utf-8"
        )

        self.assertIn("conda env update --file environment.yml --name base", workflow)


if __name__ == "__main__":
    unittest.main()
