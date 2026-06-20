#!/usr/bin/env python3
"""Score Meridian provider builder outputs with a compact rubric."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

RUBRIC = {'contract': 'Identifies provider type and ProviderSdk or adapter contract implemented.', 'configuration': 'Uses options, DI, secrets, and registration patterns correctly.', 'resilience': 'Covers cancellation, rate limiting, reconnect, and serialization posture.', 'evidence': 'Uses official docs, recorded fixtures, or neighboring provider patterns.', 'validation': 'Names focused provider tests and implementation-assurance handoff.'}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "simple_skill_score.py"
SPEC = importlib.util.spec_from_file_location("simple_skill_score", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(RUBRIC, __doc__))
