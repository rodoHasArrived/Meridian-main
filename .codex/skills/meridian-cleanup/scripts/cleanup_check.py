#!/usr/bin/env python3
"""Validate the Meridian cleanup skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'meridian-cleanup', 'required_terms': ['observable behavior', 'dead code', 'stale docs', 'broad formatters', 'narrowest relevant validation', 'reflection', 'DI', 'XAML bindings'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['meridian-code-review', 'meridian-archive-organizer', 'meridian-implementation-assurance'], 'scenarios': {'dead-code-safe': {'description': 'dead-code cleanup checks references, reflection, and XAML binding risk', 'required_terms': ['dead code', 'references', 'reflection', 'XAML bindings']}, 'cosmetic-churn': {'description': 'cleanup avoids broad formatters and cosmetic-only rewrites outside target', 'required_terms': ['broad formatters', 'cosmetic-only', 'cleanup target']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
