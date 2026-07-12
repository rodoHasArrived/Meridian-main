from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "ci" / "dispatch-targeted-test.py"

SPEC = importlib.util.spec_from_file_location("dispatch_targeted_test", SCRIPT_PATH)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules["dispatch_targeted_test"] = MODULE
SPEC.loader.exec_module(MODULE)


class TargetedTestDispatcherTests(unittest.TestCase):
    def test_dotnet_filtered_command_includes_required_project_and_filter(self) -> None:
        args = MODULE.parse_args(
            [
                "--ref",
                "codex/test",
                "--mode",
                "dotnet-filtered",
                "--dotnet-project",
                "tests/Meridian.Tests/Meridian.Tests.csproj",
                "--dotnet-filter",
                "FullyQualifiedName~ReportPackWorkflowServiceTests",
            ]
        )

        self.assertEqual(MODULE.validate_args(args), [])
        command = MODULE.build_workflow_command(args)

        self.assertEqual(command[:5], ["gh", "workflow", "run", "targeted-test.yml", "--ref"])
        self.assertIn("mode=dotnet-filtered", command)
        self.assertIn("runner=ubuntu-latest", command)
        self.assertIn("dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj", command)
        self.assertIn("dotnet_filter=FullyQualifiedName~ReportPackWorkflowServiceTests", command)

    def test_windows_modes_default_to_windows_runner(self) -> None:
        args = MODULE.parse_args(["--ref", "codex/test", "--mode", "wpf-route"])

        self.assertEqual(MODULE.validate_args(args), [])
        self.assertIn("runner=windows-latest", MODULE.build_workflow_command(args))

    def test_rejects_broad_dotnet_filter(self) -> None:
        args = MODULE.parse_args(
            [
                "--ref",
                "codex/test",
                "--mode",
                "dotnet-filtered",
                "--dotnet-filter",
                "Category!=Integration&Category!=Performance",
            ]
        )

        self.assertIn("--dotnet-filter is too broad for Targeted Test.", MODULE.validate_args(args))

    def test_rejects_wrong_runner_for_browser_mode(self) -> None:
        args = MODULE.parse_args(["--ref", "codex/test", "--mode", "browser-workstation", "--runner", "windows-latest"])

        self.assertIn("Mode 'browser-workstation' requires runner=ubuntu-latest.", MODULE.validate_args(args))


if __name__ == "__main__":
    unittest.main()
