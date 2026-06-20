#!/usr/bin/env python3
"""Validate the Meridian docs skill package."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CONFIG = {'skill': 'meridian-docs', 'required_terms': ['authoritative source', 'repo evidence', 'generated docs', 'TODO', 'uncertainty', 'exact paths', 'validation commands', 'canonical docs'], 'required_docs': ['../_shared/project-context.md', '../_shared/codex-execution-contract.md'], 'required_handoffs': ['meridian-implementation-assurance', 'meridian-archive-organizer', 'meridian-roadmap-strategist', 'meridian-repo-navigation'], 'scenarios': {'source-evidence': {'description': 'docs guidance names authoritative source, repo evidence, exact paths, and commands', 'required_terms': ['authoritative source', 'repo evidence', 'exact paths', 'commands']}, 'generated-safety': {'description': 'docs guidance protects generated docs and marks uncertainty with TODOs', 'required_terms': ['generated docs', 'Do not touch generated', 'TODO', 'uncertainty']}}}

HELPER = Path(__file__).resolve().parents[2] / "_shared" / "scripts" / "text_package_check.py"
SPEC = importlib.util.spec_from_file_location("text_package_check", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


if __name__ == "__main__":
    raise SystemExit(MODULE.main(CONFIG))
