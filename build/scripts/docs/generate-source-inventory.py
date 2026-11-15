#!/usr/bin/env python3
"""Emit actual source hashes as CI evidence without accepting a review baseline."""
from __future__ import annotations

import importlib.util
import json
import subprocess
from pathlib import Path


def main() -> int:
    root = Path(__file__).resolve().parents[3]
    spec = importlib.util.spec_from_file_location('source_inventory_hashes', Path(__file__).with_name('validate-doc-hashes.py'))
    hashes = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(hashes)
    actual = {
        'schema_version': 1, 'kind': 'actual-source-inventory', 'review_evidence': False,
        'revision': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip(),
        'modules': hashes.build_manifest(root)['modules'],
    }
    output = root / 'artifacts/docs/source-hashes.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(actual, indent=2) + '\n', encoding='utf-8')
    print('Actual source inventory: artifacts/docs/source-hashes.json (not review evidence)')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
