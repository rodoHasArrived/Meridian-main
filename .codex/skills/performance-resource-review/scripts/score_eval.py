#!/usr/bin/env python3
"""Score Performance resource review outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'resources': 'Covers memory, CPU, I/O, rendering, and lifecycle risk.', 'concurrency': 'Checks concurrency and cancellation behavior.', 'hotpath': 'Identifies hot-path and large-data risks.', 'evidence': 'Names evidence and validation gaps.', 'handoff': 'Routes implementation or tests to the right lane.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
