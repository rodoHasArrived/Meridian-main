#!/usr/bin/env python3
"""Score Meridian code review outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'findings_first': 'Leads with findings before summary and orders them by severity.', 'evidence': 'Includes concrete file and line references where available.', 'risk_focus': 'Prioritizes correctness, regression, contract, architecture, and missing-test risk.', 'validation': 'Names validation performed or explicit validation gaps.', 'handoff': 'Routes tests, cleanup, or implementation proof to the right Meridian skill.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
