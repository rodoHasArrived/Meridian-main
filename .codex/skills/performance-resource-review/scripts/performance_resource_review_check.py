#!/usr/bin/env python3
"""Validate the Performance resource review skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'performance-resource-review', 'required_terms': ['memory', 'CPU', 'I/O', 'rendering', 'concurrency', 'lifecycle', 'hot path', 'resource risk'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['meridian-code-review', 'desktop-test-generation', 'meridian-implementation-assurance', 'safe-refactoring'], 'scenarios': {'resource-risk': {'description': 'resource review covers memory, CPU, I/O, rendering, and lifecycle', 'required_terms': ['memory', 'CPU', 'I/O', 'rendering', 'lifecycle']}, 'concurrency-hotpath': {'description': 'resource review covers concurrency and hot path risk', 'required_terms': ['concurrency', 'hot path', 'resource risk']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
