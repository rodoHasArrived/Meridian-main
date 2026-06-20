#!/usr/bin/env python3
"""Score Diagnostics audit timeline outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'evidence': 'Preserves evidence trails, provenance, and replay evidence.', 'ordering': 'Keeps timeline ordering stable and testable.', 'recovery': 'Separates operator recovery action from presentation.', 'resources': 'Accounts for lifecycle and resource cleanup.', 'validation': 'Names focused diagnostics validation.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
