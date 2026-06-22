#!/usr/bin/env python3
"""Validate the Workstation screen composition skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'workstation-screen-composition', 'required_terms': ['shared shell', 'navigation', 'command', 'status', 'table', 'inspector', 'diagnostics', 'WPF'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['modular-desktop-mvvm', 'shared-component-extraction', 'dense-data-grid-inspector-panel', 'diagnostics-audit-timeline', 'meridian-implementation-assurance'], 'scenarios': {'shared-primitives': {'description': 'screen composition uses shared workstation primitives', 'required_terms': ['shared shell', 'navigation', 'command', 'status', 'inspector']}, 'specialist-handoff': {'description': 'composition guidance hands dense grids and diagnostics to specialist lanes', 'required_terms': ['dense-data-grid-inspector-panel', 'diagnostics-audit-timeline', 'shared-component-extraction']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
