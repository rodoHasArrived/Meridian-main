#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, sys
from pathlib import Path


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument('--canonical', required=True, type=Path)
    p.add_argument('--mirror', action='append', default=[], type=Path)
    args = p.parse_args()
    expected = digest(args.canonical)
    failures = []
    for m in args.mirror:
        if not m.exists():
            failures.append(f"missing mirror: {m}")
            continue
        if digest(m) != expected:
            failures.append(f"drift detected: {m}")
    if failures:
        print('AI contract drift check failed:')
        for f in failures:
            print(f' - {f}')
        return 1
    print('AI contract drift check passed.')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
