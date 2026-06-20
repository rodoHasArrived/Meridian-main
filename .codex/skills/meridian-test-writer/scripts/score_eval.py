#!/usr/bin/env python3
"""Score Meridian test writer outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'scenario': 'Names the market or operator scenario being protected.', 'path': 'Exercises the full relevant code path and observable outcome.', 'project': 'Chooses the correct test project, framework, and fixture style.', 'coverage': 'Covers happy path, error path, cancellation, and cleanup where relevant.', 'validation': 'Reports the narrowest validation command and residual untested risk.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
