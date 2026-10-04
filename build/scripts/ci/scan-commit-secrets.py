#!/usr/bin/env python3
"""Scan the exact merge-group or tag commit without relying on a push commits payload."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import subprocess


def scan(binary: str, commit: str, report: Path) -> int:
    if not re.fullmatch(r'[0-9a-f]{40}', commit):
        raise ValueError('A full coordinator commit SHA is required.')
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
    if head != commit:
        raise ValueError('Secret scan checkout does not match the requested commit.')
    if subprocess.check_output(['git', 'rev-parse', '--is-shallow-repository'], text=True).strip() != 'false':
        raise ValueError('Complete secret scanning requires fetch-depth: 0; shallow history is insufficient.')
    report.parent.mkdir(parents=True, exist_ok=True)
    report.unlink(missing_ok=True)
    # -m includes merge-resolution changes; the explicit SHA limits history to this commit.
    # This also scans tag pushes whose webhook contains an empty commits array.
    result = subprocess.run([binary, 'git', '--redact', '--config', '.gitleaks.toml',
                             '--exit-code=2', '--report-format=sarif', f'--report-path={report}',
                             f'--log-opts=--full-history -m {commit}', '.'], check=False)
    if result.returncode:
        return result.returncode
    evidence = json.loads(report.read_text(encoding='utf-8'))
    if evidence.get('version') != '2.1.0' or not evidence.get('runs'):
        raise ValueError('Gitleaks did not emit a SARIF scan result.')
    if any(run.get('results') for run in evidence['runs']):
        raise ValueError('Gitleaks reported findings despite a successful exit code.')
    if summary := os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(summary, 'a', encoding='utf-8') as output:
            output.write(f'### Secret Scan\n\nScanned commit `{commit}` and its complete reachable history, including merge changes. No findings.\n')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--binary', required=True)
    parser.add_argument('--report', type=Path, default=Path('artifacts/security/commit-secrets.sarif'))
    args = parser.parse_args()
    return scan(args.binary, os.environ['GITHUB_SHA'], args.report)


if __name__ == '__main__':
    raise SystemExit(main())
