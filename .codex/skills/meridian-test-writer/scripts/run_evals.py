#!/usr/bin/env python3
"""Run deterministic eval cases for the Meridian test writer skill."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_eval_runner.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_eval_runner", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(Path(__file__).resolve().parents[1], __doc__))
