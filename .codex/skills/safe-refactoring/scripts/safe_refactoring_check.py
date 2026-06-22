#!/usr/bin/env python3
"""Validate the Safe refactoring skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'safe-refactoring', 'required_terms': ['behavior-preserving', 'characterization tests', 'small reversible steps', 'narrow validation', 'no unrelated churn', 'rollback', 'public contracts', 'module boundaries'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['meridian-code-review', 'meridian-test-writer', 'meridian-code-architecture', 'meridian-implementation-assurance'], 'scenarios': {'characterization': {'description': 'safe refactor guidance uses characterization tests and narrow validation', 'required_terms': ['characterization tests', 'narrow validation', 'behavior-preserving']}, 'boundary-churn': {'description': 'safe refactor guidance protects contracts, boundaries, and unrelated churn', 'required_terms': ['public contracts', 'module boundaries', 'no unrelated churn', 'rollback']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
