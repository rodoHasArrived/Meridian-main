"""Tests for the buildctl impact command and change-impact planner."""
from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

# ---------------------------------------------------------------------------
# Loader helpers
# ---------------------------------------------------------------------------

REPO_ROOT = Path(__file__).resolve().parents[2]
BUILDCTL_PATH = REPO_ROOT / "build" / "python" / "cli" / "buildctl.py"
IMPACT_PKG = REPO_ROOT / "build" / "python"


def _load_buildctl():
    spec = importlib.util.spec_from_file_location("buildctl_under_test", BUILDCTL_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load buildctl.py from {BUILDCTL_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _load_planner():
    if str(IMPACT_PKG) not in sys.path:
        sys.path.insert(0, str(IMPACT_PKG))
    from impact.planner import (  # type: ignore[import]
        ImpactPlanner,
        emit_shell_script,
        emit_github_outputs,
        print_plan_summary,
    )
    return ImpactPlanner, emit_shell_script, emit_github_outputs, print_plan_summary


# Load planner at module level so helpers are accessible as plain functions.
_ImpactPlanner, _emit_shell_script, _emit_github_outputs, _print_plan_summary = (
    _load_planner()
)


# ---------------------------------------------------------------------------
# Planner unit tests
# ---------------------------------------------------------------------------


class ImpactPlannerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        pass  # module-level helpers used directly

    def _plan(self, changed_files: list[str], **kwargs):
        planner = _ImpactPlanner()
        return planner.plan(changed_files, **kwargs)

    # ------------------------------------------------------------------
    # Safety gates always present
    # ------------------------------------------------------------------

    def test_safety_gates_always_present_for_docs_only_change(self) -> None:
        plan = self._plan(["docs/status/README.md"])
        gate_names = {g.name for g in plan.safety_gates}
        expected = {"lint", "workflow-hygiene", "known-lanes", "ai-contract-drift", "warning-suppressions"}
        self.assertEqual(gate_names, expected, "All five safety gates must always be present")

    def test_safety_gates_always_present_with_no_changed_files(self) -> None:
        plan = self._plan([])
        self.assertGreater(len(plan.safety_gates), 0)

    # ------------------------------------------------------------------
    # Docs-only: code lanes must be skipped
    # ------------------------------------------------------------------

    def test_docs_only_skips_code_lanes(self) -> None:
        plan = self._plan(["docs/developer/build-test-run.md", "docs/status/README.md"])
        activated_names = {al.name for al in plan.activated_lanes}
        # Docs lane should be activated
        self.assertIn("verify-docs", activated_names)
        # Code-only lanes must NOT be activated
        for lane in ("verify-core", "verify-backtesting", "verify-fsharp",
                     "verify-directlending", "verify-fundstructure", "verify-mcpserver",
                     "verify-quantscript"):
            self.assertNotIn(lane, activated_names, f"{lane} must not fire for docs-only change")

    # ------------------------------------------------------------------
    # Core source changes: core lane activated
    # ------------------------------------------------------------------

    def test_core_source_activates_verify_core(self) -> None:
        plan = self._plan(["src/Meridian.Core/Foo.cs"])
        activated_names = {al.name for al in plan.activated_lanes}
        self.assertIn("verify-core", activated_names)

    def test_host_source_activates_verify_core(self) -> None:
        plan = self._plan(["src/Meridian/GlobalUsings.cs"])
        activated_names = {al.name for al in plan.activated_lanes}
        self.assertIn("verify-core", activated_names)

    def test_core_source_skips_unrelated_lanes(self) -> None:
        plan = self._plan(["src/Meridian.Core/Foo.cs"])
        activated_names = {al.name for al in plan.activated_lanes}
        self.assertNotIn("verify-backtesting", activated_names)
        self.assertNotIn("verify-docs", activated_names)

    # ------------------------------------------------------------------
    # Test-file changes activate their lane
    # ------------------------------------------------------------------

    def test_backtesting_test_file_activates_lane(self) -> None:
        plan = self._plan(["tests/Meridian.Backtesting.Tests/SomeTest.cs"])
        activated_names = {al.name for al in plan.activated_lanes}
        self.assertIn("verify-backtesting", activated_names)

    def test_fsharp_test_file_activates_lane(self) -> None:
        plan = self._plan(["tests/Meridian.FSharp.Tests/SomeTest.fs"])
        activated_names = {al.name for al in plan.activated_lanes}
        self.assertIn("verify-fsharp", activated_names)

    def test_ui_test_file_activates_lane(self) -> None:
        plan = self._plan(["tests/Meridian.Ui.Tests/FooTest.cs"])
        activated_names = {al.name for al in plan.activated_lanes}
        self.assertIn("verify-ui", activated_names)

    # ------------------------------------------------------------------
    # Shared project triggers multiple lanes
    # ------------------------------------------------------------------

    def test_packages_props_triggers_build_config(self) -> None:
        plan = self._plan(["Directory.Packages.props"])
        activated_names = {al.name for al in plan.activated_lanes}
        self.assertIn("verify-build-config", activated_names)

    # ------------------------------------------------------------------
    # All-lanes mode
    # ------------------------------------------------------------------

    def test_all_lanes_mode_activates_everything(self) -> None:
        plan = self._plan([], all_lanes=True)
        activated_names = {al.name for al in plan.activated_lanes}
        for expected in (
            "verify-core",
            "verify-fsharp",
            "verify-backtesting",
            "verify-directlending",
            "verify-ui",
            "verify-docs",
        ):
            self.assertIn(expected, activated_names, f"{expected} must be active in all-lanes mode")
        self.assertEqual(len(plan.skipped_lanes), 0)
        self.assertTrue(plan.all_lanes_mode)

    # ------------------------------------------------------------------
    # Reason strings
    # ------------------------------------------------------------------

    def test_reason_contains_triggering_file(self) -> None:
        plan = self._plan(["src/Meridian.Backtesting/Engine.cs"])
        lane = next(al for al in plan.activated_lanes if al.name == "verify-backtesting")
        self.assertIn("src/Meridian.Backtesting/Engine.cs", lane.reason)

    def test_all_lanes_reason_string(self) -> None:
        plan = self._plan([], all_lanes=True)
        for al in plan.activated_lanes:
            self.assertIn("all-lanes", al.reason)

    # ------------------------------------------------------------------
    # Plan dict round-trip
    # ------------------------------------------------------------------

    def test_plan_to_dict_is_valid_json(self) -> None:
        plan = self._plan(["src/Meridian.Core/Foo.cs"])
        d = plan.to_dict()
        serialised = json.dumps(d)
        decoded = json.loads(serialised)
        self.assertEqual(decoded["_schema"], "meridian-impact-plan/1.0")
        self.assertIn("safety_gates", decoded)
        self.assertIn("activated_lanes", decoded)
        self.assertIn("skipped_lanes", decoded)

    # ------------------------------------------------------------------
    # Shell script emission
    # ------------------------------------------------------------------

    def test_shell_script_contains_safety_gates(self) -> None:
        plan = self._plan(["docs/README.md"])
        script = _emit_shell_script(plan)
        self.assertIn("#!/usr/bin/env bash", script)
        self.assertIn("lint", script)
        self.assertIn("known-lanes", script)

    def test_shell_script_contains_activated_lane_steps(self) -> None:
        plan = self._plan(["src/Meridian.Core/Foo.cs"])
        script = _emit_shell_script(plan)
        self.assertIn("verify-core/test-core", script)
        self.assertIn("bash -o pipefail -c", script)

    def test_shell_script_has_telemetry_section(self) -> None:
        plan = self._plan([])
        script = _emit_shell_script(plan)
        self.assertIn("_impact_start", script)
        self.assertIn("_impact_elapsed", script)
        self.assertIn("_TEOF", script)

    def test_shell_script_does_not_contain_skipped_lane_commands(self) -> None:
        plan = self._plan(["docs/README.md"])
        script = _emit_shell_script(plan)
        # backtesting is not triggered by a docs change
        self.assertNotIn("Meridian.Backtesting.Tests", script)

    # ------------------------------------------------------------------
    # GitHub Actions output emission
    # ------------------------------------------------------------------

    def test_github_output_writes_lane_flags(self) -> None:
        plan = self._plan(["src/Meridian.Core/Foo.cs"])
        with tempfile.NamedTemporaryFile(mode="r", suffix=".txt", delete=False) as tmp:
            tmp_path = tmp.name
        self.addCleanup(lambda: Path(tmp_path).unlink(missing_ok=True))
        _emit_github_outputs(plan, tmp_path)
        content = Path(tmp_path).read_text(encoding="utf-8")
        lines = dict(line.split("=", 1) for line in content.splitlines() if "=" in line)
        self.assertEqual(lines.get("verify_core"), "true")
        self.assertEqual(lines.get("verify_backtesting"), "false")

    def test_github_output_all_lanes_all_true(self) -> None:
        plan = self._plan([], all_lanes=True)
        with tempfile.NamedTemporaryFile(mode="r", suffix=".txt", delete=False) as tmp:
            tmp_path = tmp.name
        self.addCleanup(lambda: Path(tmp_path).unlink(missing_ok=True))
        _emit_github_outputs(plan, tmp_path)
        content = Path(tmp_path).read_text(encoding="utf-8")
        lines = dict(line.split("=", 1) for line in content.splitlines() if "=" in line)
        # In all-lanes mode every lane should be true
        for key, value in lines.items():
            self.assertEqual(value, "true", f"{key} should be true in all-lanes mode")


# ---------------------------------------------------------------------------
# buildctl CLI integration tests
# ---------------------------------------------------------------------------


class BuildctlImpactCLITests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.buildctl = _load_buildctl()

    def _run_impact(self, argv: list[str]) -> int:
        parser = self.buildctl.build_parser()
        args = parser.parse_args(["impact"] + argv)
        return self.buildctl.cmd_impact(args)

    def test_impact_with_changed_files_exits_zero(self) -> None:
        rc = self._run_impact(["--changed-files", "docs/README.md"])
        self.assertEqual(rc, 0)

    def test_impact_json_format_is_valid(self) -> None:
        import io
        import contextlib

        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            rc = self._run_impact(
                ["--changed-files", "src/Meridian.Core/Foo.cs", "--format", "json"]
            )
        self.assertEqual(rc, 0)
        output = buf.getvalue()
        # Find the JSON block (may have extra lines before it)
        for i, line in enumerate(output.splitlines()):
            if line.startswith("{"):
                data = json.loads("\n".join(output.splitlines()[i:]))
                break
        else:
            self.fail("No JSON output found")
        self.assertEqual(data["_schema"], "meridian-impact-plan/1.0")

    def test_impact_writes_plan_file(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            plan_path = Path(tmp) / "plan.json"
            rc = self._run_impact(
                ["--changed-files", "src/Meridian.Core/Foo.cs", "--output", str(plan_path)]
            )
            self.assertEqual(rc, 0)
            self.assertTrue(plan_path.exists())
            data = json.loads(plan_path.read_text(encoding="utf-8"))
            self.assertEqual(data["_schema"], "meridian-impact-plan/1.0")

    def test_impact_writes_script_file(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            script_path = Path(tmp) / "plan.sh"
            rc = self._run_impact(
                [
                    "--changed-files",
                    "src/Meridian.Core/Foo.cs",
                    "--emit-script",
                    str(script_path),
                ]
            )
            self.assertEqual(rc, 0)
            self.assertTrue(script_path.exists())
            content = script_path.read_text(encoding="utf-8")
            self.assertIn("#!/usr/bin/env bash", content)

    def test_impact_all_lanes_flag(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            plan_path = Path(tmp) / "plan.json"
            rc = self._run_impact(
                ["--changed-files", "docs/README.md", "--all-lanes", "--output", str(plan_path)]
            )
            self.assertEqual(rc, 0)
            data = json.loads(plan_path.read_text(encoding="utf-8"))
            self.assertTrue(data["all_lanes_mode"])
            self.assertEqual(data["skipped_lanes"], [])

    def test_impact_legacy_file_flag(self) -> None:
        """--file flag still works for backward compatibility."""
        rc = self._run_impact(["--file", "src/Meridian.Core/Foo.cs"])
        self.assertEqual(rc, 0)


if __name__ == "__main__":
    unittest.main()
