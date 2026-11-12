#!/usr/bin/env python3
"""Merge behavior of the tracked documentation health dashboard.

The tracked dashboard used to record every Markdown file's line count and the
repository total, so any two PRs that edited Markdown conflicted on it. These
tests merge real Git branches in a scratch repository to show that:

* independent Markdown edits merge cleanly, and the merged dashboard is current;
* concurrent changes to tracked signals still conflict, on ``signal_digest``
  only, instead of merging cleanly into a wrong ``total_files``.
"""

from __future__ import annotations

import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "build" / "scripts" / "docs"
GENERATOR = SCRIPTS / "generate-health-dashboard.py"
DASHBOARD_JSON = "docs/status/doc-health-dashboard.json"
DASHBOARD_MD = "docs/status/doc-health-dashboard.md"
VOLATILE_JSON = "artifacts/doc-health-line-metrics.json"

if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))
spec = importlib.util.spec_from_file_location("generate_health_dashboard_merge_under_test", GENERATOR)
assert spec is not None and spec.loader is not None
dashboard = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = dashboard
spec.loader.exec_module(dashboard)


class DocHealthDashboardMergeTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.git("init", "--initial-branch=main")
        for key, value in (
            ("user.name", "Doc health merge test"),
            ("user.email", "doc-health-test@example.invalid"),
            ("core.autocrlf", "false"),
            ("commit.gpgsign", "false"),
            ("merge.conflictStyle", "merge"),
        ):
            self.git("config", key, value)
        (self.repo / ".gitignore").write_text("artifacts/\n", encoding="utf-8")
        # Like the real repository (about 230 lines per file, few TODO markers), the
        # fixtures average well over the score's 20-line size threshold and hold no
        # TODO markers. There, line edits cannot move the health score; below the
        # threshold the size term, a tracked signal, would follow line counts.
        filler = "".join(f"Line {number}.\n" for number in range(30))
        self.write("README.md", "# Repo\n\n[A](docs/a.md) [M](docs/m.md) [Z](docs/z.md)\n" + filler)
        for name in ("a", "m", "z"):
            self.write(f"docs/{name}.md", f"# {name.upper()}\n\nBody of {name}.\n" + filler)
        self.generate()
        self.commit("base")

    def git(self, *args: str, check: bool = True) -> subprocess.CompletedProcess:
        return subprocess.run(
            ["git", *args], cwd=self.repo, check=check, capture_output=True, text=True,
        )

    def write(self, path: str, content: str) -> None:
        target = self.repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")

    def append(self, path: str, content: str) -> None:
        with (self.repo / path).open("a", encoding="utf-8") as stream:
            stream.write(content)

    def generate(self) -> None:
        environment = {key: value for key, value in os.environ.items() if key != "MERIDIAN_DOC_HEALTH_USE_GIT"}
        subprocess.run(
            [
                sys.executable, str(GENERATOR), "--root", str(self.repo),
                "--output", str(self.repo / DASHBOARD_MD),
                "--json-output", str(self.repo / DASHBOARD_JSON),
                "--volatile-json-output", str(self.repo / VOLATILE_JSON),
            ],
            check=True, capture_output=True, text=True, env=environment,
        )

    def commit(self, message: str) -> None:
        self.git("add", "--all")
        self.git("commit", "-m", message)

    def branch(self, name: str, change) -> dict:
        """Create ``name`` from main, apply ``change``, regenerate and commit."""
        self.git("switch", "-c", name, "main")
        change()
        self.generate()
        self.commit(name)
        volatile = json.loads((self.repo / VOLATILE_JSON).read_text(encoding="utf-8"))
        self.git("switch", "main")
        return volatile

    def tracked(self) -> dict:
        return json.loads((self.repo / DASHBOARD_JSON).read_text(encoding="utf-8"))

    def test_tracked_outputs_omit_line_counts_and_volatile_output_keeps_them(self) -> None:
        tracked = self.tracked()
        self.assertNotIn("total_lines", tracked)
        self.assertNotIn("average_lines", tracked)
        self.assertTrue(tracked["all_files"])
        self.assertTrue(all("line_count" not in item for item in tracked["all_files"]))
        self.assertRegex(tracked["signal_digest"], r"^sha256:[0-9a-f]{64}$")
        self.assertEqual(4, tracked["total_files"])

        markdown = (self.repo / DASHBOARD_MD).read_text(encoding="utf-8")
        self.assertNotIn("| Total lines |", markdown)
        self.assertNotIn("| Average file size (lines) |", markdown)
        self.assertIn("| Total documentation files | 4 |", markdown)

        volatile = json.loads((self.repo / VOLATILE_JSON).read_text(encoding="utf-8"))
        self.assertGreater(volatile["total_lines"], 0)
        self.assertTrue(all("line_count" in item for item in volatile["all_files"]))

    def test_digest_tracks_signals_only(self) -> None:
        metrics = dashboard.analyse(self.repo)
        baseline = metrics.to_tracked_dict()
        self.assertEqual(baseline, metrics.to_tracked_dict())

        metrics.total_lines += 1000
        metrics.all_files[0].line_count += 1000
        self.assertEqual(baseline, metrics.to_tracked_dict())

        metrics.all_files[0].has_heading = not metrics.all_files[0].has_heading
        self.assertNotEqual(baseline["signal_digest"], metrics.to_tracked_dict()["signal_digest"])

    def test_independent_markdown_edits_merge_cleanly_and_stay_current(self) -> None:
        base_volatile = json.loads((self.repo / VOLATILE_JSON).read_text(encoding="utf-8"))
        first = self.branch("edit-a", lambda: self.append("docs/a.md", "\nMore about a.\n" * 20))
        second = self.branch("edit-z", lambda: self.append("docs/z.md", "\nMore about z.\n" * 7))

        # Both branches changed line counts, which the old tracked format recorded.
        self.assertNotEqual(base_volatile["total_lines"], first["total_lines"])
        self.assertNotEqual(base_volatile["total_lines"], second["total_lines"])
        for branch in ("edit-a", "edit-z"):
            self.assertEqual("", self.git("diff", "main", branch, "--", "docs/status").stdout)

        self.git("merge", "--no-ff", "--no-edit", "edit-a")
        merged = self.git("merge", "--no-ff", "--no-edit", "edit-z", check=False)
        self.assertEqual(0, merged.returncode, merged.stdout + merged.stderr)

        self.generate()
        self.assertEqual("", self.git("status", "--porcelain").stdout)

    def test_concurrent_file_additions_conflict_on_digest_instead_of_miscounting(self) -> None:
        def add_b() -> None:
            self.write("docs/b-new.md", "# B new\n")
            self.append("docs/a.md", "\nSee [B new](b-new.md).\n")

        def add_y() -> None:
            self.write("docs/y-new.md", "# Y new\n")
            self.append("docs/z.md", "\nSee [Y new](y-new.md).\n")

        self.branch("add-b", add_b)
        self.branch("add-y", add_y)
        self.git("merge", "--no-ff", "--no-edit", "add-b")
        merged = self.git("merge", "--no-ff", "--no-edit", "add-y", check=False)

        self.assertNotEqual(0, merged.returncode)
        conflicted = self.git("diff", "--name-only", "--diff-filter=U").stdout.split()
        self.assertEqual([DASHBOARD_JSON], conflicted)

        # Both sides wrote the same total_files (4 -> 5), which Git would merge
        # silently. The digest is the only conflicting line.
        conflict_lines: list[str] = []
        inside = False
        for line in (self.repo / DASHBOARD_JSON).read_text(encoding="utf-8").splitlines():
            if line.startswith("<<<<<<< "):
                inside = True
            elif line.startswith(">>>>>>> "):
                inside = False
            elif inside and line != "=======":
                conflict_lines.append(line.strip())
        self.assertEqual(2, len(conflict_lines))
        self.assertTrue(all(line.startswith('"signal_digest": ') for line in conflict_lines))

        # Regeneration resolves the conflict to the correct combined total.
        self.generate()
        self.assertEqual(6, self.tracked()["total_files"])


if __name__ == "__main__":
    unittest.main()
