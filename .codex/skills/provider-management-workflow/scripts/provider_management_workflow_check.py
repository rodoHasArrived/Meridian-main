#!/usr/bin/env python3
"""Validate the Provider management workflow skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'provider-management-workflow', 'required_terms': ['credentials', 'health', 'degradation', 'calibration', 'validation', 'fallback', 'operator recovery', 'secure'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['meridian-provider-builder', 'desktop-test-generation', 'diagnostics-audit-timeline', 'performance-resource-review', 'meridian-implementation-assurance'], 'scenarios': {'credential-health': {'description': 'provider workflow guidance covers credentials, health, degradation, and fallback', 'required_terms': ['credentials', 'health', 'degradation', 'fallback']}, 'recovery-validation': {'description': 'provider workflow guidance covers validation and operator recovery', 'required_terms': ['validation', 'operator recovery', 'desktop-test-generation']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
