#!/usr/bin/env python3
"""Validate the Meridian provider builder skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'meridian-provider-builder', 'required_terms': ['ProviderSdk', 'IOptionsMonitor', 'rate limiting', 'reconnect', 'CancellationToken', 'source-generated JSON', 'DI/module', 'ProviderSdk-compliant'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['meridian-blueprint', 'meridian-test-writer', 'meridian-implementation-assurance'], 'scenarios': {'resilience-config': {'description': 'provider guidance keeps configuration, cancellation, rate limiting, and reconnect controls visible', 'required_terms': ['IOptionsMonitor', 'rate limiting', 'reconnect', 'CancellationToken']}, 'wire-format': {'description': 'wire-format scenario uses official docs, recorded fixtures, and source-generated JSON', 'required_terms': ['official docs', 'recorded fixtures', 'source-generated JSON']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
