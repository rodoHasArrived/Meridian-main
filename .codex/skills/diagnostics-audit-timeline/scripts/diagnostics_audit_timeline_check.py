#!/usr/bin/env python3
"""Validate the Diagnostics audit timeline skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'diagnostics-audit-timeline', 'required_terms': ['diagnostics', 'audit timeline', 'evidence trail', 'replay evidence', 'operator recovery', 'stable ordering', 'provenance', 'view models'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['desktop-test-generation', 'performance-resource-review', 'workstation-screen-composition', 'meridian-implementation-assurance'], 'scenarios': {'evidence-timeline': {'description': 'diagnostics timeline guidance preserves evidence, provenance, and ordering', 'required_terms': ['evidence trail', 'provenance', 'stable ordering', 'audit timeline']}, 'recovery-surface': {'description': 'recovery guidance covers operator recovery and replay evidence', 'required_terms': ['operator recovery', 'replay evidence', 'diagnostics']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
