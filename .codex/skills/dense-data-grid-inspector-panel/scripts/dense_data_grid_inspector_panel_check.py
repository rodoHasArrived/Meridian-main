#!/usr/bin/env python3
"""Validate the Dense data grid inspector panel skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'dense-data-grid-inspector-panel', 'required_terms': ['virtualization', 'lightweight row', 'stable selection', 'inspector', 'detail tabs', 'command state', 'large dataset', 'WPF validation'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['performance-resource-review', 'desktop-test-generation', 'workstation-screen-composition', 'modular-desktop-mvvm', 'meridian-implementation-assurance'], 'scenarios': {'selection-inspector': {'description': 'dense grid guidance preserves selection and inspector state', 'required_terms': ['stable selection', 'inspector', 'detail tabs', 'command state']}, 'virtualized-scale': {'description': 'dense grid guidance covers virtualization and large dataset risk', 'required_terms': ['virtualization', 'lightweight row', 'large dataset', 'performance-resource-review']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
