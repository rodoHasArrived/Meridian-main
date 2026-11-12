"""Keep the demo build small without weakening its clean-environment CLI checks."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import textwrap
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]
PROJECT = "tests/Meridian.Demo.Tests/Meridian.Demo.Tests.csproj"


class DemoSmokeWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = yaml.load(
            (ROOT / ".github/workflows/demo-smoke.yml").read_text(encoding="utf-8"),
            Loader=yaml.BaseLoader,
        )
        cls.steps = cls.workflow["jobs"]["demo-smoke"]["steps"]

    def test_restore_and_test_only_the_dedicated_project_without_filtering_cases(self):
        commands = [step["run"] for step in self.steps if "run" in step]
        restore = next(command for command in commands if command.startswith("dotnet restore"))
        test = next(command for command in commands if "dotnet test " in command)
        self.assertIn(f"dotnet restore {PROJECT}", restore)
        self.assertIn(f"dotnet test {PROJECT} -c Release --no-restore", test)
        self.assertNotIn("--filter", test)
        self.assertNotIn("--no-build", test)
        self.assertIn("--logger \"trx;LogFilePrefix=demo-smoke\"", test)
        self.assertIn("--results-directory artifacts/test-results/demo-smoke", test)
        self.assertNotIn("tests/Meridian.Tests/Meridian.Tests.csproj", "\n".join(commands))

    def test_both_events_cover_the_project_and_its_linked_helpers(self):
        paths = self.workflow["on"]["pull_request"]["paths"]
        self.assertEqual(paths, self.workflow["on"]["push"]["paths"])
        for dependency in (
            "tests/Meridian.Demo.Tests/**",
            "tests/Meridian.Tests/Testing/TestArtifactDirectory.cs",
            "tests/Meridian.Tests/TestSupport/ProviderCatalogTestLease.cs",
            "tests/Meridian.Tests/TestHelpers/SecurityMasterTestCapabilityFloor.cs",
            "tests/Meridian.Tests/TestCollections.cs",
            "tests/Meridian.TestSupport/**",
            "tests/Directory.Build.props",
            "tests/xunit.runner.json",
        ):
            with self.subTest(dependency=dependency):
                self.assertIn(dependency, paths)

    def test_cli_keeps_normal_seed_build_and_reuses_release_for_serve_and_reset(self):
        script = next(step["run"] for step in self.steps if "demo_run()" in step.get("run", ""))
        with tempfile.TemporaryDirectory(prefix="demo-smoke-workflow-") as temporary:
            directory = Path(temporary)
            command_log = directory / "commands.jsonl"
            dotnet = directory / "dotnet"
            dotnet.write_text(textwrap.dedent("""\
                #!/usr/bin/env python3
                import json, os, sys, time
                with open(os.environ["DEMO_SMOKE_COMMAND_LOG"], "a") as output:
                    output.write(json.dumps({
                        "args": sys.argv[1:],
                        "environment": {name: os.environ.get(name) for name in (
                            "DOTNET_ENVIRONMENT", "MDC_AUTH_MODE", "MERIDIAN_USE_INMEMORY_GOVERNANCE")},
                    }) + "\\n")
                if "--demo" in sys.argv:
                    time.sleep(2)
                """), encoding="utf-8")
            curl = directory / "curl"
            curl.write_text(textwrap.dedent("""\
                #!/usr/bin/env python3
                import json, os, pathlib, sys, time
                assert sys.argv[-1] == "http://127.0.0.1:8123/workstation/"
                for _ in range(200):
                    log = pathlib.Path(os.environ["DEMO_SMOKE_COMMAND_LOG"])
                    if log.exists() and any("--demo" in json.loads(line)["args"] for line in log.read_text().splitlines()):
                        sys.exit(0)
                    time.sleep(0.01)
                sys.exit(1)
                """), encoding="utf-8")
            dotnet.chmod(0o755)
            curl.chmod(0o755)
            environment = dict(os.environ, PATH=str(directory) + os.pathsep + os.environ["PATH"],
                               DEMO_SMOKE_COMMAND_LOG=str(command_log), DOTNET_ENVIRONMENT="Development",
                               MDC_AUTH_MODE="required", MERIDIAN_USE_INMEMORY_GOVERNANCE="false")
            completed = subprocess.run(["bash", "-e", "-c", script], cwd=directory,
                                       env=environment, capture_output=True, text=True, timeout=15)
            self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
            commands = [json.loads(line) for line in command_log.read_text().splitlines()]
            self.assertEqual(len(commands), 3)
            base = ["run", "--project", "src/Meridian/Meridian.csproj", "-c", "Release"]
            self.assertEqual(commands[0]["args"], base + ["--", "--seed-demo", "--seed-only"])
            self.assertEqual(commands[1]["args"], base + ["--no-build", "--", "--demo", "--http-port", "8123"])
            self.assertEqual(commands[2]["args"], base + ["--no-build", "--", "--reset-demo"])
            for command in commands:
                self.assertTrue(all(value is None for value in command["environment"].values()))


if __name__ == "__main__":
    unittest.main()
