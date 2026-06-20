#!/usr/bin/env python3
"""Score Dense data grid inspector panel outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'virtualization': 'Preserves virtualization and lightweight row view models.', 'selection': 'Keeps selection and inspector state stable.', 'commands': 'Handles command state and disabled reasons.', 'resources': 'Accounts for large dataset and lifecycle risk.', 'validation': 'Names focused WPF validation.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
