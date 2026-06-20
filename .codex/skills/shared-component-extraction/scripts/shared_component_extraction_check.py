#!/usr/bin/env python3
"""Validate the Shared component extraction skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'shared-component-extraction', 'required_terms': ['duplicate', 'reusable', 'shared component', 'behavior drift', 'ownership', 'consumers', 'tests', 'rollback'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['safe-refactoring', 'modular-desktop-mvvm', 'workstation-screen-composition', 'performance-resource-review', 'meridian-implementation-assurance'], 'scenarios': {'reuse-ownership': {'description': 'component extraction proves reuse, owners, and consumers', 'required_terms': ['reusable', 'ownership', 'consumers', 'duplicate']}, 'behavior-drift': {'description': 'extraction guidance prevents behavior drift with tests and rollback', 'required_terms': ['behavior drift', 'tests', 'rollback']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
