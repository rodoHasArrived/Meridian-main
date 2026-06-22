#!/usr/bin/env python3
"""Score Workstation screen composition outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'composition': 'Uses shared shell, navigation, command, status, table, inspector, and diagnostics primitives.', 'taxonomy': 'Preserves workspace/navigation taxonomy.', 'state': 'Keeps command and status state testable.', 'handoff': 'Routes specialized grid, diagnostics, or component work to the right lane.', 'validation': 'Names focused WPF validation.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
