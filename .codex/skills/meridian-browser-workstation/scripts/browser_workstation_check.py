#!/usr/bin/env python3
"""Validate the Meridian browser workstation skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'meridian-browser-workstation', 'required_terms': ['src/Meridian.Ui/dashboard', 'shared contracts', 'accessibility', 'route/deep-link', 'no mobile', 'dashboard-local validation', 'Codex Browser plugin', 'unauthenticated local route'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['meridian-repo-navigation', 'meridian-test-writer', 'meridian-code-review', 'meridian-implementation-assurance'], 'scenarios': {'accessibility-routing': {'description': 'browser changes preserve accessibility and route/deep-link behavior', 'required_terms': ['accessible names', 'keyboard', 'route/deep-link', 'live-region']}, 'shared-validation': {'description': 'browser changes use shared contracts and dashboard-local validation', 'required_terms': ['shared contracts', 'npm --prefix src/Meridian.Ui/dashboard run test', 'npm --prefix src/Meridian.Ui/dashboard run build']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
