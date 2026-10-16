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

    def test_workflow_lane_installs_screenshot_dependencies_before_validation(self) -> None:
        # The standalone Conda workflow was retired in the May 2026 consolidation.
        # Screenshot fixtures now run in the canonical workflow lane.
        workflow = yaml.load(
            (ROOT / ".github/workflows/meridian-ci.yml").read_text(encoding="utf-8"),
            Loader=yaml.BaseLoader,
        )
        steps = workflow["jobs"]["verify-workflows"]["steps"]
        dependency_command = "python -m pip install --requirement build/scripts/ci/requirements.txt"
        validation_command = "bash scripts/ci.sh --lane verify-workflows"
        dependency_steps = [step for step in steps if step.get("run", "").strip() == dependency_command]
        validation_steps = [step for step in steps if step.get("run", "").strip() == validation_command]

        self.assertEqual(len(dependency_steps), 1)
        self.assertEqual(len(validation_steps), 1)
        self.assertLess(steps.index(dependency_steps[0]), steps.index(validation_steps[0]))
        for step in (*dependency_steps, *validation_steps):
            self.assertNotIn("if", step)
            self.assertNotIn("continue-on-error", step)

        requirements = (ROOT / "build/scripts/ci/requirements.txt").read_text(encoding="utf-8")
        self.assertRegex(requirements, r"(?mi)^Pillow==\d+\.\d+\.\d+\s*$")

    def test_workflow_lane_executes_script_tests(self) -> None:
        ci_script = (ROOT / "scripts/ci.sh").read_text(encoding="utf-8")
        workflow_lane = ci_script.split("verify_workflows()", 1)[1].split("\n}", 1)[0]

        self.assertIn("build/scripts/ci/run-script-tests.py", workflow_lane)


if __name__ == "__main__":
    unittest.main()
