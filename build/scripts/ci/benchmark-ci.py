#!/usr/bin/env python3
"""Collect paired test evidence; never change production concurrency defaults."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--subject', choices=('dotnet', 'browser'), required=True)
    parser.add_argument('--pair', type=int, choices=range(1, 6), required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    variants = [('baseline', 2 if args.subject == 'dotnet' else 8), ('candidate', 4 if args.subject == 'dotnet' else 16)]
    # Alternate execution order so the candidate does not always benefit from warm caches.
    if args.pair % 2 == 0:
        variants.reverse()
    pair = {'pairId': f'{args.subject}-{os.environ["GITHUB_RUN_ID"]}-{args.pair}'}
    for name, setting in variants:
        directory = (args.output.parent / name).resolve()
        directory.mkdir(parents=True, exist_ok=True)
        env = os.environ.copy()
        if args.subject == 'dotnet':
            command = [sys.executable, 'build/scripts/ci/run-dotnet-ci-tests.py', '--max-parallel', str(setting),
                       '--results-dir', str(directory), '--summary-output', str(directory / 'summary.md'),
                       '--json-output', str(directory / 'summary.json')]
        else:
            env.update(DASHBOARD_VITEST_BATCH_SIZE=str(setting), MERIDIAN_BROWSER_RESULTS_DIR=str(directory))
            command = ['npm', '--prefix', 'src/Meridian.Ui/dashboard', 'test']
        code = subprocess.run(command, env=env, check=False).returncode
        try:
            report = json.loads((directory / 'summary.json').read_text(encoding='utf-8'))
        except (OSError, ValueError):
            report = {}
        pair[name] = dict(report.get('counts', {}), commitSha=os.environ['GITHUB_SHA'],
                          testIdentityDigest=report.get('testIdentityDigest'), testSeconds=report.get('testSeconds') or 0,
                          runnerLabel='ubuntu-latest', conclusion='success' if code == 0 else 'failure',
                          resourceFailure=code != 0, runId=os.environ['GITHUB_RUN_ID'],
                          runAttempt=os.environ['GITHUB_RUN_ATTEMPT'], setting=setting)
    args.output.write_text(json.dumps(pair, indent=2) + '\n', encoding='utf-8')
    return int(any(pair[name]['conclusion'] != 'success' for name in ('baseline', 'candidate')))


if __name__ == '__main__':
    raise SystemExit(main())
