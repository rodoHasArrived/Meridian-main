#!/usr/bin/env python3
"""Score Meridian docs outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'evidence': 'Names current repo evidence instead of inventing commands or behavior.', 'scope': 'Updates the nearest canonical doc or index without broad rewrites.', 'generated': 'Protects generated docs and updates source inputs when needed.', 'uncertainty': 'Marks TODOs or uncertainty clearly when facts cannot be verified.', 'validation': 'Runs the narrowest docs, AI inventory, or diff hygiene check.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
