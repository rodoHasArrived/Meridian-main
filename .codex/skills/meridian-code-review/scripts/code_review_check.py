#!/usr/bin/env python3
"""Validate the Meridian code review skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'meridian-code-review', 'required_terms': ['findings-first', 'severity order', 'file and line references', 'residual risks', 'validation gaps', 'missing tests', 'architecture drift', 'regressions'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['meridian-test-writer', 'meridian-cleanup', 'meridian-implementation-assurance'], 'scenarios': {'findings-first-output': {'description': 'review output keeps findings before summary with severity and file evidence', 'required_terms': ['findings', 'severity order', 'file and line references', 'residual risks']}, 'validation-risk': {'description': 'review output reports static-only or skipped validation risk', 'required_terms': ['validation gaps', 'static-only', 'narrow tests']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
