#!/usr/bin/env python3
"""Score Meridian cleanup outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'scope': 'Names one cleanup theme and smallest safe target.', 'behavior': 'Preserves observable behavior and avoids public contract drift.', 'evidence': 'Checks references, reflection, DI, generated code, or XAML bindings before removal.', 'churn': 'Avoids broad formatters and unrelated cosmetic rewrites.', 'validation': 'Reports the narrowest validation command or explicit blocker.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
