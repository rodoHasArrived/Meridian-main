#!/usr/bin/env python3
"""Validate the Research data acquisition skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'research-data-acquisition', 'required_terms': ['provider-backed import', 'preview', 'validation', 'lineage', 'catalog handoff', 'backfill', 'dirty rows', 'rejected rows'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['provider-management-workflow', 'meridian-provider-builder', 'desktop-test-generation', 'diagnostics-audit-timeline', 'meridian-implementation-assurance'], 'scenarios': {'lineage-catalog': {'description': 'research acquisition guidance preserves lineage and catalog handoff', 'required_terms': ['lineage', 'catalog handoff', 'preview', 'validation']}, 'dirty-rows': {'description': 'research import guidance handles dirty and rejected rows', 'required_terms': ['dirty rows', 'rejected rows', 'provider-backed import']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
