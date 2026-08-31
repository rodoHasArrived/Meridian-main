"""Pins the pilot-readiness opt-in in run-docs-automation.

The pilot-readiness dashboard is generated from
``artifacts/pilot-acceptance/latest/pilot-readiness.json``, which
``PilotAcceptanceHarnessTests`` writes and ``.gitignore`` keeps untracked. When the docs
profiles regenerated it unconditionally, whether that artifact happened to be on disk decided
whether the committed dashboard came out as a placeholder or carried one local test run's
ephemeral identifiers, so running the test suite and then any docs regeneration in the same
workspace silently turned local test output into a committed document. These tests keep the
task opt-in.
"""

import argparse
import importlib.util
import sys
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).resolve().parents[1] / "run-docs-automation.py"

_spec = importlib.util.spec_from_file_location("run_docs_automation", MODULE_PATH)
run_docs_automation = importlib.util.module_from_spec(_spec)
assert _spec.loader is not None
# Register before executing: the module defines dataclasses, and dataclass field resolution
# looks the owning module up in sys.modules.
sys.modules["run_docs_automation"] = run_docs_automation
_spec.loader.exec_module(run_docs_automation)

PILOT_TASK = run_docs_automation.PILOT_READINESS_TASK


def make_args(**overrides) -> argparse.Namespace:
    defaults = {
        "profile": "core",
        "scripts": None,
        "include_pilot_readiness": False,
        "auto_create_todos": False,
    }
    defaults.update(overrides)
    return argparse.Namespace(**defaults)


class PilotReadinessOptInTests(unittest.TestCase):
    def test_profiles_still_declare_the_task(self):
        # The opt-in must be enforced at selection time rather than by quietly deleting the
        # task from the profiles, so the profile still documents that the dashboard exists.
        declaring = [
            profile
            for profile, tasks in run_docs_automation.PROFILE_CONFIG.items()
            if PILOT_TASK in tasks
        ]
        self.assertTrue(declaring, "expected at least one profile to declare the pilot task")

    def test_task_is_skipped_by_default(self):
        for profile in run_docs_automation.PROFILE_CONFIG:
            with self.subTest(profile=profile):
                selected = run_docs_automation.resolve_selected_scripts(make_args(profile=profile))
                self.assertNotIn(
                    PILOT_TASK,
                    selected,
                    "the pilot dashboard must not regenerate without an explicit opt-in",
                )

    def test_task_runs_when_explicitly_requested(self):
        declaring = next(
            profile
            for profile, tasks in run_docs_automation.PROFILE_CONFIG.items()
            if PILOT_TASK in tasks
        )

        selected = run_docs_automation.resolve_selected_scripts(
            make_args(profile=declaring, include_pilot_readiness=True)
        )

        self.assertIn(PILOT_TASK, selected)

    def test_explicit_script_selection_also_requires_the_flag(self):
        # --scripts is the other way in; leaving it unguarded would reopen the same hole.
        selected = run_docs_automation.resolve_selected_scripts(make_args(scripts=PILOT_TASK))
        self.assertNotIn(PILOT_TASK, selected)

        opted_in = run_docs_automation.resolve_selected_scripts(
            make_args(scripts=PILOT_TASK, include_pilot_readiness=True)
        )
        self.assertIn(PILOT_TASK, opted_in)

    def test_other_dashboard_tasks_are_unaffected(self):
        selected = run_docs_automation.resolve_selected_scripts(make_args(profile="core"))

        for task in run_docs_automation.PROFILE_CONFIG["core"]:
            if task == PILOT_TASK:
                continue
            self.assertIn(task, selected)


if __name__ == "__main__":
    unittest.main()
