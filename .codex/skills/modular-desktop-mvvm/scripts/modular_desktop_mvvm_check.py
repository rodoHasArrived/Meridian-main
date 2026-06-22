#!/usr/bin/env python3
"""Validate the Modular desktop MVVM skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'modular-desktop-mvvm', 'required_terms': ['MVVM', 'shared services', 'view models', 'commands', 'XAML', 'WPF validation', 'resource', 'docs sync'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['workstation-screen-composition', 'shared-component-extraction', 'desktop-test-generation', 'performance-resource-review', 'meridian-implementation-assurance'], 'scenarios': {'mvvm-boundary': {'description': 'desktop MVVM guidance preserves bindings, commands, and view-model boundaries', 'required_terms': ['bindings', 'commands', 'view models', 'code-behind']}, 'wpf-validation': {'description': 'desktop implementation guidance includes focused WPF validation', 'required_terms': ['Meridian.Wpf.Tests', 'EnableWindowsTargeting', 'EnableFullWpfBuild']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
