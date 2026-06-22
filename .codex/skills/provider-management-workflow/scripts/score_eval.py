#!/usr/bin/env python3
"""Score Provider management workflow outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'credentials': 'Keeps credentials secure and out of user-level env writes.', 'health': 'Models provider health, degradation, validation, and fallback.', 'recovery': 'Surfaces operator recovery and disabled reasons.', 'tests': 'Names focused WPF/provider workflow tests.', 'handoff': 'Routes provider implementation work to provider-builder.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
