#!/usr/bin/env python3
"""Validate the Desktop test generation skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'desktop-test-generation', 'required_terms': ['WPF', 'view models', 'commands', 'services', 'bindings', 'shell routes', 'Meridian.Wpf.Tests', 'EnableFullWpfBuild'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['modular-desktop-mvvm', 'dense-data-grid-inspector-panel', 'provider-management-workflow', 'diagnostics-audit-timeline', 'meridian-implementation-assurance'], 'scenarios': {'wpf-target': {'description': 'desktop test guidance chooses view-model, command, service, binding, or shell route targets', 'required_terms': ['view models', 'commands', 'services', 'bindings', 'shell routes']}, 'wpf-validation': {'description': 'desktop test guidance includes Meridian.Wpf.Tests validation', 'required_terms': ['Meridian.Wpf.Tests', 'EnableWindowsTargeting', 'EnableFullWpfBuild']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
