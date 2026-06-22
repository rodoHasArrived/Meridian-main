#!/usr/bin/env python3
"""Validate the Meridian repo navigation skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'meridian-repo-navigation', 'required_terms': ['repo-navigation', 'generated repo map', 'owner files', 'first docs', 'handoff', 'narrowest lane', 'large-repo', 'route'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['meridian-blueprint', 'meridian-code-architecture', 'meridian-contract-governance', 'meridian-provider-builder', 'meridian-implementation-assurance'], 'scenarios': {'owner-routing': {'description': 'navigation output identifies owner files, first docs, and route recommendation', 'required_terms': ['owner files', 'first docs', 'recommended skill', 'handoff']}, 'generated-map': {'description': 'navigation guidance uses generated repo map before broad search', 'required_terms': ['generated repo map', 'broad recursive search', 'docs/ai/navigation']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
