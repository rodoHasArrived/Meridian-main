#!/usr/bin/env python3
"""Collect paired test evidence; never change production concurrency defaults."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import tempfile
import time
import uuid


def provenance(local):
    if local:
        commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
        dirty = bool(subprocess.check_output(['git', 'status', '--porcelain'], text=True).strip())
        return dict(commitSha=commit, runnerLabel=f'local-{platform.system()}-{platform.machine()}',
                    executionEnvironment='local', worktreeDirty=dirty,
                    runId=f'local-{uuid.uuid4().hex}', runAttempt=1)
    required = ('GITHUB_SHA', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT')
    if any(not os.environ.get(key) for key in required):
        raise ValueError('Actions run metadata is unavailable; use --local for explicitly local evidence.')
    return dict(commitSha=os.environ['GITHUB_SHA'], runnerLabel='ubuntu-latest',
                executionEnvironment='github-actions', runId=os.environ['GITHUB_RUN_ID'],
                runAttempt=os.environ['GITHUB_RUN_ATTEMPT'])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--subject', choices=('dotnet', 'browser'), required=True)
    parser.add_argument('--pair', type=int, choices=range(1, 6), required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--local', action='store_true',
                        help='Record local provenance; local measurements cannot promote hosted defaults.')
    args = parser.parse_args(argv)
    try:
        identity = provenance(args.local)
    except (OSError, subprocess.CalledProcessError, ValueError) as exc:
        parser.error(str(exc))
    variants = [('baseline', 2 if args.subject == 'dotnet' else 8), ('candidate', 4 if args.subject == 'dotnet' else 16)]
    # Alternate execution order so the candidate does not always benefit from warm caches.
    if args.pair % 2 == 0:
        variants.reverse()
    pair = {'pairId': f'{args.subject}-{identity["runId"]}-{args.pair}',
            'subject': args.subject, 'executionOrder': [name for name, _ in variants]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    for name, setting in variants:
        # A failed launch must never consume a summary left by an earlier invocation.
        directory = Path(tempfile.mkdtemp(prefix=f'{name}-', dir=args.output.parent)).resolve()
        env = os.environ.copy()
        if args.subject == 'dotnet':
            command = [sys.executable, 'build/scripts/ci/run-dotnet-ci-tests.py', '--max-parallel', str(setting),
                       '--results-dir', str(directory), '--summary-output', str(directory / 'summary.md'),
                       '--json-output', str(directory / 'summary.json')]
        else:
            env.update(DASHBOARD_VITEST_BATCH_SIZE=str(setting), MERIDIAN_BROWSER_RESULTS_DIR=str(directory))
            command = ['npm', '--prefix', 'src/Meridian.Ui/dashboard', 'test']
        started_at = datetime.now(timezone.utc).isoformat()
        started = time.monotonic()
        launch_error = None
        with (directory / 'command.log').open('w', encoding='utf-8') as log:
            try:
                code = subprocess.run(command, env=env, check=False,
                                      stdout=log, stderr=subprocess.STDOUT).returncode
            except OSError as exc:
                code, launch_error = 127, str(exc)
                log.write(f'Unable to launch benchmark: {exc}\n')
        elapsed = time.monotonic() - started
        completed_at = datetime.now(timezone.utc).isoformat()
        evidence_error = None
        try:
            report = json.loads((directory / 'summary.json').read_text(encoding='utf-8'))
            if not isinstance(report, dict) or not isinstance(report.get('counts'), dict):
                raise ValueError('Test summary must contain an object with counts.')
        except (OSError, ValueError) as exc:
            report = {}
            evidence_error = str(exc)
        successful = code == 0 and evidence_error is None
        pair[name] = dict(report.get('counts', {}), **identity,
                          testIdentityDigest=report.get('testIdentityDigest'), testSeconds=report.get('testSeconds'),
                          conclusion='success' if successful else 'failure',
                          resourceFailure=not successful, exitCode=code, setting=setting,
                          startedAt=started_at, completedAt=completed_at, commandSeconds=elapsed,
                          command=command, evidenceDirectory=str(directory),
                          launchError=launch_error, evidenceError=evidence_error)
        # Persist each variant immediately so an interrupted pair retains completed evidence.
        args.output.write_text(json.dumps(pair, indent=2) + '\n', encoding='utf-8')
        print(f'{args.subject} pair {args.pair}: {name} {pair[name]["conclusion"]}; log: {directory / "command.log"}', flush=True)
    args.output.write_text(json.dumps(pair, indent=2) + '\n', encoding='utf-8')
    return int(any(pair[name]['conclusion'] != 'success' for name in ('baseline', 'candidate')))


if __name__ == '__main__':
    raise SystemExit(main())
