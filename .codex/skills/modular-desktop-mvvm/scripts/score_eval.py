#!/usr/bin/env python3
"""Score Modular desktop MVVM outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'mvvm': 'Keeps view behavior in bindings and commands.', 'shared': 'Uses shared services and reusable view models where appropriate.', 'tests': 'Names focused WPF tests for touched view models or commands.', 'resources': 'Accounts for resource and lifecycle risk.', 'docs': 'Updates source README or docs when behavior changes.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
