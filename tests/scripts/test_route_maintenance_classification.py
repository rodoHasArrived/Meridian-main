"""Functional checks for scripts/ai/route-maintenance.sh change classification.

The router inspects the diff between a base and head ref and writes
`.ai/maintenance-route.json` with a routing mode plus granular change-category
flags that downstream maintenance jobs use to target validation. These tests run
the script in a throwaway git repository with crafted changes and assert the
emitted classification.
"""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
ROUTE_SCRIPT = REPO_ROOT / "scripts" / "ai" / "route-maintenance.sh"


def _run_git(cwd: Path, *args: str) -> None:
    subprocess.run(
        ["git", *args],
        cwd=cwd,
        check=True,
        capture_output=True,
        text=True,
    )


def _classify(changed_files: list[str]) -> dict:
    """Run the router over a temp repo where `changed_files` are added on HEAD."""
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        _run_git(root, "init", "-q")
        _run_git(root, "config", "user.email", "test@example.com")
        _run_git(root, "config", "user.name", "Test")

        # Base commit with a single unrelated file so a base ref exists.
        (root / "README.md").write_text("base\n", encoding="utf-8")
        _run_git(root, "add", "README.md")
        _run_git(root, "commit", "-q", "-m", "base")
        base_sha = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=root,
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()

        # Head commit introducing the changed files.
        for rel in changed_files:
            path = root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("change\n", encoding="utf-8")
        _run_git(root, "add", "-A")
        _run_git(root, "commit", "-q", "-m", "head")

        env = dict(os.environ)
        subprocess.run(
            [
                "bash",
                str(ROUTE_SCRIPT),
                "--classify-only",
                "--base",
                base_sha,
                "--head",
                "HEAD",
            ],
            cwd=root,
            check=True,
            capture_output=True,
            text=True,
            env=env,
        )
        return json.loads((root / ".ai" / "maintenance-route.json").read_text(encoding="utf-8"))


class RouteMaintenanceClassificationTests(unittest.TestCase):
    def test_docs_only_change_routes_light(self) -> None:
        route = _classify(["docs/ai/README.md"])
        self.assertEqual("light", route["mode"])
        self.assertTrue(route["docs_only"])
        self.assertTrue(route["ai_tooling_changes"])

    def test_source_change_routes_full(self) -> None:
        route = _classify(["src/Meridian.Core/Foo.cs"])
        self.assertEqual("full", route["mode"])
        self.assertFalse(route["tests_only"])

    def test_tests_only_flag_true_when_all_under_tests(self) -> None:
        route = _classify(["tests/Meridian.Tests/FooTests.cs"])
        self.assertTrue(route["tests_only"])

    def test_tests_only_flag_false_when_mixed_with_source(self) -> None:
        route = _classify(["tests/Meridian.Tests/FooTests.cs", "src/Meridian.Core/Foo.cs"])
        self.assertFalse(route["tests_only"])

    def test_provider_adapter_change_sets_provider_flag(self) -> None:
        route = _classify(["src/Meridian.Infrastructure/Adapters/Acme/AcmeClient.cs"])
        self.assertTrue(route["provider_changes"])

    def test_ai_tooling_change_sets_ai_tooling_flag(self) -> None:
        route = _classify(["scripts/ai/setup.sh"])
        self.assertTrue(route["ai_tooling_changes"])

    def test_config_change_sets_config_flag(self) -> None:
        route = _classify(["Directory.Packages.props"])
        self.assertTrue(route["config_changes"])

    def test_route_json_exposes_all_expected_keys(self) -> None:
        route = _classify(["docs/ai/README.md"])
        for key in (
            "mode",
            "docs_only",
            "workflow_changes",
            "ui_changes",
            "ledger_changes",
            "provider_changes",
            "ai_tooling_changes",
            "config_changes",
            "tests_only",
            "base_ref",
            "head_ref",
            "changed_files_count",
        ):
            self.assertIn(key, route)


if __name__ == "__main__":
    unittest.main()
