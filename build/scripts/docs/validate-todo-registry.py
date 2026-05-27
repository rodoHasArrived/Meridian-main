#!/usr/bin/env python3
"""Validate TODO scan output against a registry-backed governance contract."""
from __future__ import annotations
import argparse, json, re, sys
from pathlib import Path

ID_RE = re.compile(r"\bTODO-ID\s*:\s*([A-Z0-9\-]+)")
OWNER_RE = re.compile(r"\bOWNER\s*:\s*([A-Za-z0-9_.\-/@]+)")


def load_registry(path: Path) -> set[str]:
    payload = json.loads(path.read_text(encoding='utf-8'))
    return {e['id'] for e in payload.get('entries', []) if isinstance(e, dict) and 'id' in e}


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument('--scan-json', required=True, type=Path)
    p.add_argument('--registry', required=True, type=Path)
    p.add_argument('--enforce-prefix', action='append', default=[])
    args = p.parse_args()
    scan = json.loads(args.scan_json.read_text(encoding='utf-8'))
    registered = load_registry(args.registry)

    failures: list[str] = []
    for item in scan.get('todos', []):
        file_path = str(item.get('file',''))
        if args.enforce_prefix and not any(file_path.startswith(prefix) for prefix in args.enforce_prefix):
            continue
        text = item.get('text', '')
        loc = f"{item.get('file')}:{item.get('line')}"
        id_match = ID_RE.search(text)
        owner_match = OWNER_RE.search(text)
        if not id_match:
            failures.append(f"{loc} missing TODO-ID metadata")
            continue
        todo_id = id_match.group(1)
        if todo_id not in registered:
            failures.append(f"{loc} uses unregistered TODO-ID '{todo_id}'")
        if not owner_match:
            failures.append(f"{loc} missing OWNER metadata")

    if failures:
        print('TODO registry validation failed:')
        for f in failures:
            print(f" - {f}")
        return 1
    print('TODO registry validation passed.')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
