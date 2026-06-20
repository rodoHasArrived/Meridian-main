#!/usr/bin/env python3
"""Score Desktop test generation outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'target': 'Chooses the right WPF view model, command, service, binding, or shell route target.', 'fixtures': 'Uses existing WPF test patterns and mocks.', 'coverage': 'Covers command state, bindings, and resource-sensitive transitions.', 'validation': 'Runs Meridian.Wpf.Tests with Windows targeting and full WPF build when needed.', 'handoff': 'Routes implementation gaps to the owning desktop skill.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
