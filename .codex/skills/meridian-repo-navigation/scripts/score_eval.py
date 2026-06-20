#!/usr/bin/env python3
"""Score Meridian repo navigation outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'orientation': 'Names the likely subsystem, owner docs, and first files.', 'map': 'Uses generated navigation before broad search.', 'handoff': 'Recommends one narrow specialist lane and handoff point.', 'scope': 'Stops at orientation unless implementation is requested.', 'validation': 'Names any stale navigation or validation follow-up.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
