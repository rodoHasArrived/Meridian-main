#!/usr/bin/env python3
"""Validate the Meridian test writer skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'meridian-test-writer', 'required_terms': ['scenario-first', 'named market scenario', 'full code path', 'business-observable outcome', 'correct test project', 'narrowest test command', 'happy path', 'cancellation path'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['meridian-code-review', 'meridian-provider-builder', 'meridian-implementation-assurance'], 'scenarios': {'market-scenario-path': {'description': 'test guidance keeps scenario, full path, outcome, and project selection visible', 'required_terms': ['named market scenario', 'full code path', 'business-observable outcome', 'correct test project']}, 'async-cleanup': {'description': 'async and cleanup scenario includes cancellation and disposal guardrails', 'required_terms': ['async Task', 'CancellationTokenSource', 'await using', 'Task.Delay']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
