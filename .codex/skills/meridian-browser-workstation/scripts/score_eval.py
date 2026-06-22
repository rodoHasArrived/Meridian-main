#!/usr/bin/env python3
"""Score Meridian browser workstation outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'scope': 'Keeps work inside src/Meridian.Ui/dashboard or required shared contracts.', 'shared_seam': 'Uses Meridian.Ui.Services or Meridian.Ui.Shared for common behavior.', 'accessibility': 'Preserves accessible names, keyboard behavior, live regions, and route/deep-link state.', 'validation': 'Runs dashboard-local Vitest/build or narrower equivalent validation.', 'no_mobile': 'Avoids mobile-specific product or native-client scope.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
