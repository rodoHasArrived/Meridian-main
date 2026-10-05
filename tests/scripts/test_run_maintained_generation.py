"""Exercise maintained generation with real Git snapshots and controlled producers."""

from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "build/scripts/run-maintained-generation.py"
SPEC = importlib.util.spec_from_file_location("maintained_generation_under_test", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
generation = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = generation
SPEC.loader.exec_module(generation)

STRUCTURE = "docs/generated/repository-structure.md"
OVERVIEW = "docs/generated/workflows-overview.md"
BASELINE = "docs/source/generated/source-hash-manifest.json"
HELP = "docs/HELP.md"
COVERAGE = "docs/status/coverage-report.md"
COVERAGE_MARKER = "<!-- auto-sync:coverage -->"
ASSET_ROOT = "src/Meridian.Ui/wwwroot/workstation/"
OLD_ASSET = ASSET_ROOT + "assets/app-old12345.js"
NEW_ASSET = ASSET_ROOT + "assets/app-new67890.js"
BEGIN = "<!-- BEGIN AUTO-GENERATED: WORKFLOW-MANIFEST-HELP -->"
END = "<!-- END AUTO-GENERATED: WORKFLOW-MANIFEST-HELP -->"
EMPTY_CHANGES = {"added": [], "modified": [], "deleted": []}


class MaintainedGenerationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.git("init", "--initial-branch=main")
        self.git("config", "core.autocrlf", "false")
        self.policy = {
            "whole_files": [STRUCTURE, OVERVIEW],
            "whole_trees": [ASSET_ROOT],
            "hybrid_files": {HELP: [[BEGIN, END]]},
        }
        self.write(HELP, self.hybrid("Reviewed operator guidance.", "old commands"))
        self.git("add", "--all")

    def git(self, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run(
            ["git", "--literal-pathspecs", *args], cwd=self.repo,
            capture_output=True, check=True,
        )

    def write(self, path: str, text: str) -> None:
        target = self.repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8", newline="\n")

    @staticmethod
    def hybrid(prose: str, generated: str) -> str:
        return f"# Help\n\n{prose}\n\n{BEGIN}\n{generated}\n{END}\n\nReviewed footer.\n"

    @staticmethod
    def step(name: str, *, optional: bool = False):
        return generation.Step(name, ("fixture-command", name), optional)

    def test_snapshot_captures_dirty_tracked_and_nonignored_untracked_files(self) -> None:
        self.write(".gitignore", "ignored/\n")
        self.write("tracked.txt", "indexed value\n")
        self.git("add", "--all")
        self.write("tracked.txt", "local user edit\n")
        self.write("new file.txt", "untracked value\n")
        self.write("ignored/scratch.txt", "ignore this\n")

        state = generation.snapshot(self.repo)

        self.assertEqual(b"local user edit\n", state["tracked.txt"])
        self.assertEqual(b"untracked value\n", state["new file.txt"])
        self.assertNotIn("ignored/scratch.txt", state)
        self.assertFalse(any(path.startswith(".git/") for path in state))

    def test_changes_report_additions_modifications_and_tracked_deletions(self) -> None:
        self.write(OLD_ASSET, "old bundle\n")
        self.write(STRUCTURE, "old structure\n")
        self.git("add", "--all")
        before = generation.snapshot(self.repo)
        (self.repo / OLD_ASSET).unlink()
        self.write(NEW_ASSET, "new bundle\n")
        self.write(STRUCTURE, "new structure\n")

        self.assertEqual(
            {"added": [NEW_ASSET], "modified": [STRUCTURE], "deleted": [OLD_ASSET]},
            generation.changes(before, generation.snapshot(self.repo)),
        )

    def test_discovery_covers_workflow_generators_in_order_and_skips_checks_and_installs(self) -> None:
        self.write(
            ".github/workflows/documentation.yml",
            """jobs:
  regenerate-docs:
    steps:
      - uses: actions/checkout@fixture
      - name: Install docs script dependencies
        run: python -m pip install --requirement build/scripts/docs/requirements.txt
      - name: Install diagram dependencies
        run: npm ci --no-fund --no-audit
      - name: Run docs automation profile
        run: |
          python3 build/scripts/docs/run-docs-automation.py \\
            --profile core \\
            --summary-output docs/status/docs-automation-summary.md \\
            --json-output docs/status/docs-automation-summary.json
      - name: Render Mermaid diagrams
        run: |
          python3 build/scripts/docs/render-roadmap-diagrams.py --summary
          python3 build/scripts/docs/render-source-diagrams.py --summary
      - name: Render WPF UI diagrams
        run: npm run generate-diagrams
      - name: Render future maintained report
        run: python3 build/scripts/docs/new-maintained-report.py
      - name: Render UML diagrams
        continue-on-error: true
        run: |
          docker run --rm plantuml/plantuml -tpng /data/*.puml
          docker run --rm plantuml/plantuml -tsvg /data/*.puml
      - name: Refresh workflow overview
        run: python3 build/scripts/docs/generate-structure-docs.py --workflows-only
      - name: Refresh workflow manifest outputs
        run: python3 build/scripts/docs/generate-workflow-manifest.py
      - name: Compare dashboard readiness deltas vs previous commit
        shell: python
        run: print('comparison only')
      - name: Check generated docs are current
        run: git diff --exit-code -- .
      - name: Check whitespace
        run: git diff --check
""",
        )

        steps = generation.discover_steps(self.repo)
        commands = [" ".join(step.command) for step in steps]
        flattened = "\n".join(commands)
        expected_order = [
            "generate-ui-api-routes-ts.py",
            "generate-workspace-catalog-ts.py",
            "npm --prefix src/Meridian.Ui/dashboard run build",
            "render-adapter-readiness.py",
            "render-roadmap-docs.py",
            "render-source-docs.py",
            "run-docs-automation.py",
            "render-roadmap-diagrams.py",
            "render-source-diagrams.py",
            "npm run generate-diagrams",
            "new-maintained-report.py",
            "-tpng",
            "-tsvg",
            "generate-structure-docs.py",
            "generate-workflow-manifest.py",
        ]
        locations = [flattened.index(command) for command in expected_order]
        self.assertEqual(sorted(locations), locations)
        self.assertNotIn("pip install", flattened)
        self.assertNotIn("npm ci", flattened)
        self.assertNotIn("git diff", flattened)
        self.assertNotIn("comparison only", flattened)
        uml_steps = [step for step in steps if "plantuml/plantuml" in " ".join(step.command)]
        self.assertEqual(1, len(uml_steps))
        self.assertTrue(all(step.optional for step in uml_steps))

    def test_discovery_follows_an_additional_generator_in_the_actual_workflow(self) -> None:
        actual_root = SCRIPT.parents[2]
        workflow = generation.yaml.safe_load((actual_root / generation.WORKFLOW).read_text(encoding="utf-8"))
        original = generation.discover_steps(actual_root)
        workflow["jobs"]["regenerate-docs"]["steps"].append({
            "name": "Render newly maintained summary",
            "run": "python3 build/scripts/docs/render-future-summary.py --summary",
        })
        self.write(generation.WORKFLOW, generation.yaml.safe_dump(workflow))

        discovered = generation.discover_steps(self.repo)

        self.assertEqual(original, discovered[:-1])
        self.assertIn("build/scripts/docs/render-future-summary.py", discovered[-1].command)
        self.assertFalse(discovered[-1].optional)

    def test_discovery_rejects_an_unrecognized_generation_command(self) -> None:
        actual_root = SCRIPT.parents[2]
        workflow = generation.yaml.safe_load((actual_root / generation.WORKFLOW).read_text(encoding="utf-8"))
        workflow["jobs"]["regenerate-docs"]["steps"].append({
            "name": "Unrecognized new generator",
            "run": "make generate-new-output",
        })
        self.write(generation.WORKFLOW, generation.yaml.safe_dump(workflow))

        with self.assertRaisesRegex(ValueError, "Unsupported generation command"):
            generation.discover_steps(self.repo)

    def test_late_producer_reaches_fixed_point_and_second_invocation_is_empty(self) -> None:
        self.write(STRUCTURE, "old registry\n")
        self.write(OVERVIEW, "old overview\n")
        self.git("add", "--all")
        steps = [self.step("consumer"), self.step("late-producer")]

        def runner(root: Path, step) -> int:
            if step.name == "consumer":
                self.write(OVERVIEW, (root / STRUCTURE).read_text(encoding="utf-8"))
            else:
                self.write(STRUCTURE, "current registry\n")
            return 0

        report = generation.run_generation(self.repo, steps, self.policy, runner=runner)

        self.assertTrue(report["successful"], report)
        self.assertTrue(report["converged"], report)
        self.assertEqual(3, len(report["passes"]))
        self.assertEqual([STRUCTURE, OVERVIEW], report["changed_outputs"]["modified"])
        self.assertEqual(EMPTY_CHANGES, report["passes"][-1]["changed_outputs"])
        self.assertEqual(b"current registry\n", (self.repo / OVERVIEW).read_bytes())
        settled = generation.snapshot(self.repo)

        second = generation.run_generation(self.repo, steps, self.policy, runner=runner)

        self.assertTrue(second["successful"], second)
        self.assertEqual(1, len(second["passes"]))
        self.assertEqual(EMPTY_CHANGES, second["changed_outputs"])
        self.assertEqual(settled, generation.snapshot(self.repo))

    def test_nonconverging_generator_fails_at_the_iteration_cap(self) -> None:
        self.write(STRUCTURE, "a\n")

        def runner(root: Path, step) -> int:
            old = (root / STRUCTURE).read_text(encoding="utf-8")
            self.write(STRUCTURE, "b\n" if old == "a\n" else "a\n")
            return 0

        report = generation.run_generation(
            self.repo, [self.step("oscillating")], self.policy,
            max_passes=2, runner=runner,
        )

        self.assertFalse(report["successful"], report)
        self.assertFalse(report["converged"], report)
        self.assertEqual(2, len(report["passes"]))

    def test_successful_build_reports_obsolete_hash_removal_and_is_repeatable(self) -> None:
        self.write(OLD_ASSET, "stale bytes\n")
        self.git("add", "--all")

        def runner(root: Path, step) -> int:
            for asset in (root / ASSET_ROOT / "assets").glob("*.js"):
                asset.unlink()
            self.write(NEW_ASSET, "current bytes\n")
            return 0

        steps = [self.step("browser-build")]
        report = generation.run_generation(self.repo, steps, self.policy, runner=runner)

        self.assertTrue(report["successful"], report)
        self.assertEqual([OLD_ASSET], report["changed_outputs"]["deleted"])
        self.assertEqual([NEW_ASSET], report["changed_outputs"]["added"])
        self.assertFalse((self.repo / OLD_ASSET).exists())
        second = generation.run_generation(self.repo, steps, self.policy, runner=runner)
        self.assertTrue(second["successful"], second)
        self.assertEqual(EMPTY_CHANGES, second["changed_outputs"])

    def test_mandatory_failure_is_reported_even_when_files_do_not_change(self) -> None:
        report = generation.run_generation(
            self.repo, [self.step("broken-generator")], self.policy,
            runner=lambda root, step: 23,
        )

        self.assertFalse(report["successful"], report)
        self.assertTrue(any(
            failure["name"] == "broken-generator" and failure["return_code"] == 23
            and not failure["optional"] for failure in report["failed_steps"]
        ), report)

    def test_advisory_failure_is_reported_without_masking_successful_required_steps(self) -> None:
        called = []

        def runner(root: Path, step) -> int:
            called.append(step.name)
            return 9 if step.optional else 0

        report = generation.run_generation(
            self.repo, [self.step("uml", optional=True), self.step("required")],
            self.policy, runner=runner,
        )

        self.assertTrue(report["successful"], report)
        self.assertIn("required", called)
        self.assertTrue(any(
            failure["name"] == "uml" and failure["optional"]
            and failure["return_code"] == 9 for failure in report["failed_steps"]
        ), report)

    def test_missing_executable_becomes_a_reported_failure(self) -> None:
        def runner(root: Path, step) -> int:
            raise FileNotFoundError("fixture executable is not installed")

        report = generation.run_generation(
            self.repo, [self.step("missing-command")], self.policy, runner=runner,
        )

        self.assertFalse(report["successful"], report)
        self.assertTrue(any(
            failure["name"] == "missing-command" and failure["return_code"] != 0
            for failure in report["failed_steps"]
        ), report)

    def test_hybrid_generated_region_can_change_while_existing_user_prose_is_preserved(self) -> None:
        # The index deliberately differs: protection is relative to invocation start.
        self.write(HELP, self.hybrid("User's uncommitted prose edit.", "old commands"))
        before = generation.snapshot(self.repo)

        def runner(root: Path, step) -> int:
            self.write(HELP, self.hybrid("User's uncommitted prose edit.", "current commands"))
            return 0

        report = generation.run_generation(
            self.repo, [self.step("workflow-manifest")], self.policy, runner=runner,
        )

        self.assertTrue(report["successful"], report)
        self.assertEqual([HELP], report["changed_outputs"]["modified"])
        self.assertEqual([], generation.validate_changes(before, generation.snapshot(self.repo), self.policy))
        self.assertIn(b"User's uncommitted prose edit.", (self.repo / HELP).read_bytes())

    def test_protection_rejects_baseline_unknown_generated_file_and_manual_prose(self) -> None:
        self.write(BASELINE, '{"reviewed": true}\n')
        self.write("docs/generated/README.md", "Handwritten ownership rules.\n")
        before = generation.snapshot(self.repo)
        self.write(BASELINE, '{"reviewed": false}\n')
        self.write("docs/generated/README.md", "Unreviewed ownership rules.\n")
        self.write(HELP, self.hybrid("Rewritten manual prose.", "new commands"))

        violations = generation.validate_changes(before, generation.snapshot(self.repo), self.policy)

        for path in (BASELINE, "docs/generated/README.md", HELP):
            self.assertTrue(any(path in violation for violation in violations), violations)

    def test_protection_violation_restores_invocation_start_baseline_and_prose(self) -> None:
        self.write(BASELINE, '{"reviewed": "user-local-baseline"}\n')
        self.write(HELP, self.hybrid("User's existing prose.", "old commands"))
        before = generation.snapshot(self.repo)

        def runner(root: Path, step) -> int:
            self.write(BASELINE, '{"reviewed": "silently-accepted-new-hashes"}\n')
            self.write(HELP, self.hybrid("Destroyed prose.", "new commands"))
            return 0

        report = generation.run_generation(
            self.repo, [self.step("unsafe-generator")], self.policy, runner=runner,
        )

        self.assertFalse(report["successful"], report)
        self.assertTrue(report["protection_violations"], report)
        self.assertEqual(before[BASELINE], (self.repo / BASELINE).read_bytes())
        self.assertEqual(before[HELP], (self.repo / HELP).read_bytes())

    def test_generator_created_symlink_and_normal_prose_mutation_are_both_restored(self) -> None:
        self.write(BASELINE, '{"reviewed": "preserve-local-baseline"}\n')
        self.git("add", "--all")
        before = generation.snapshot(self.repo)
        external = tempfile.TemporaryDirectory()
        self.addCleanup(external.cleanup)
        external_file = Path(external.name) / "external-baseline.json"
        external_file.write_bytes(b"external content must not be overwritten\n")

        def runner(root: Path, step) -> int:
            (root / BASELINE).unlink()
            try:
                (root / BASELINE).symlink_to(external_file)
            except (OSError, NotImplementedError) as exc:
                self.skipTest(f"Symlinks unavailable: {exc}")
            self.write(HELP, self.hybrid("Unauthorized prose mutation.", "new commands"))
            return 0

        report = generation.run_generation(
            self.repo, [self.step("unsafe-link-generator")], self.policy, runner=runner,
        )

        self.assertFalse(report["successful"], report)
        self.assertEqual(sorted([BASELINE, HELP]), report["protection_violations"])
        self.assertFalse((self.repo / BASELINE).is_symlink())
        self.assertEqual(before, generation.snapshot(self.repo))
        self.assertEqual(b"external content must not be overwritten\n", external_file.read_bytes())

    def test_preexisting_symlink_fails_before_any_generator_runs(self) -> None:
        self.write("source.txt", "Existing user-owned target.\n")
        link = self.repo / "existing-link.txt"
        try:
            link.symlink_to("source.txt")
        except (OSError, NotImplementedError) as exc:
            self.skipTest(f"Symlinks unavailable: {exc}")
        called = []

        def runner(root: Path, step) -> int:
            called.append(step.name)
            return 0

        with self.assertRaisesRegex(ValueError, "Symlinks are not supported"):
            generation.run_generation(
                self.repo, [self.step("should-not-run")], self.policy, runner=runner,
            )

        self.assertFalse(called)
        self.assertTrue(link.is_symlink())
        self.assertEqual(b"Existing user-owned target.\n", link.read_bytes())

    def test_coverage_prefix_refresh_preserves_the_entire_handwritten_suffix(self) -> None:
        self.policy["whole_files"].append(COVERAGE)
        self.policy["protected_suffixes"] = {COVERAGE: COVERAGE_MARKER}
        suffix = f"{COVERAGE_MARKER}\n\nReviewed coverage explanation.\n\nManual follow-up.\n"
        self.write(COVERAGE, "# Coverage\nOld generated counters.\n" + suffix)
        self.git("add", "--all")

        def runner(root: Path, step) -> int:
            self.write(COVERAGE, "# Coverage\nCurrent generated counters.\n" + suffix)
            return 0

        report = generation.run_generation(
            self.repo, [self.step("coverage")], self.policy, runner=runner,
        )

        self.assertTrue(report["successful"], report)
        self.assertEqual([COVERAGE], report["changed_outputs"]["modified"])
        self.assertEqual(
            suffix.encode(),
            generation.protected_content(COVERAGE, (self.repo / COVERAGE).read_bytes(), self.policy),
        )

    def test_coverage_suffix_mutation_removal_or_file_deletion_is_rejected(self) -> None:
        self.policy["whole_files"].append(COVERAGE)
        self.policy["protected_suffixes"] = {COVERAGE: COVERAGE_MARKER}
        original = f"# Generated coverage\n{COVERAGE_MARKER}\nReviewed prose.\n".encode()
        before = {COVERAGE: original}
        for after in (
            {COVERAGE: f"# New coverage\n{COVERAGE_MARKER}\nUnauthorized prose.\n".encode()},
            {COVERAGE: b"# Coverage without its handwritten suffix\n"},
            {},
        ):
            with self.subTest(after=after):
                self.assertEqual([COVERAGE], generation.validate_changes(before, after, self.policy))

    def test_coverage_without_a_handwritten_suffix_can_be_created_and_refreshed(self) -> None:
        self.policy["whole_files"].append(COVERAGE)
        self.policy["protected_suffixes"] = {COVERAGE: COVERAGE_MARKER}

        def runner(root: Path, step) -> int:
            self.write(COVERAGE, "# Generated coverage\nCurrent counters.\n")
            return 0

        report = generation.run_generation(
            self.repo, [self.step("coverage")], self.policy, runner=runner,
        )

        self.assertTrue(report["successful"], report)
        self.assertEqual([COVERAGE], report["changed_outputs"]["added"])
        self.assertEqual(
            [], generation.validate_changes(
                {COVERAGE: b"# Old generated coverage\n"},
                {COVERAGE: b"# Current generated coverage\n"}, self.policy,
            ),
        )

    def test_hybrid_protection_rejects_missing_duplicate_and_reversed_markers(self) -> None:
        for malformed in (
            b"# Help\nNo markers.\n",
            f"{BEGIN}\n{BEGIN}\nbody\n{END}\n".encode(),
            f"{BEGIN}\nbody\n{END}\n{END}\n".encode(),
            f"{END}\nbody\n{BEGIN}\n".encode(),
        ):
            with self.subTest(content=malformed), self.assertRaises(ValueError):
                generation.protected_content(HELP, malformed, self.policy)

    def test_malformed_hybrid_fails_before_running_generators(self) -> None:
        self.write(HELP, "# User prose without generated markers\n")
        called = []

        def runner(root: Path, step) -> int:
            called.append(step.name)
            return 0

        with self.assertRaisesRegex(ValueError, "generated markers"):
            generation.run_generation(
                self.repo, [self.step("should-not-run")], self.policy, runner=runner,
            )

        self.assertFalse(called)
        self.assertEqual(b"# User prose without generated markers\n", (self.repo / HELP).read_bytes())


if __name__ == "__main__":
    unittest.main()
