"""Assertions for workflow contracts that tolerate routine action pin updates."""

import re
import unittest

import yaml


def assert_pinned_action(
    test_case: unittest.TestCase, workflow: str, job_name: str, action: str
) -> dict:
    # Preserve GitHub Actions keys and scalar spellings (not YAML 1.1 booleans).
    document = yaml.load(workflow, Loader=yaml.BaseLoader)
    steps = document.get("jobs", {}).get(job_name, {}).get("steps", [])
    matches = [
        step for step in steps
        if step.get("uses", "").partition("@")[0] == action
    ]
    test_case.assertEqual(
        len(matches), 1, f"{job_name} must use exactly one {action} step"
    )
    step = matches[0]
    test_case.assertRegex(
        step["uses"], rf"\A{re.escape(action)}@[A-Fa-f0-9]{{40}}\Z",
        f"{job_name}: {action} must be pinned to a full SHA",
    )
    return step
