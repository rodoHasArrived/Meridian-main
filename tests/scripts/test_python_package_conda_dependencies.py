import unittest
from pathlib import Path

import yaml


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

    def test_canonical_script_lane_installs_screenshot_dependencies(self) -> None:
        # The retired conda workflow was replaced by Meridian CI's script lane.
        # Exercise its actual dependency-install/run chain so screenshot suites
        # cannot silently lose Pillow when the hosted environment changes.
        workflow = yaml.safe_load(
            (ROOT / ".github/workflows/meridian-ci.yml").read_text(encoding="utf-8")
        )
        steps = workflow["jobs"]["verify-workflows"]["steps"]
        commands = [step.get("run", "").strip() for step in steps]
        install_command = "python -m pip install --requirement build/scripts/ci/requirements.txt"
        lane_command = "bash scripts/ci.sh --lane verify-workflows"
        self.assertIn(install_command, commands)
        self.assertIn(lane_command, commands)
        self.assertLess(commands.index(install_command), commands.index(lane_command))

        requirements = (ROOT / "build/scripts/ci/requirements.txt").read_text(encoding="utf-8")
        self.assertRegex(requirements, r"(?mi)^pillow==\d+(?:\.\d+)+\s*$")


if __name__ == "__main__":
    unittest.main()
