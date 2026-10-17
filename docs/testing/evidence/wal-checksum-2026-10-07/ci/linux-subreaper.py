#!/usr/bin/env python3
"""Artifact-only Linux init wrapper: adopt/reap orphans; preserve command status.

Usage: python3 linux-subreaper.py --receipt receipt.json --log command.log -- COMMAND ...
No test, command, environment, or repository behavior is changed. The wrapper supplies
the child reaping normally performed by init, which this container's PID 1 omits.
"""

from __future__ import annotations

import argparse
import ctypes
import json
import os
from pathlib import Path
import signal
import sys
from datetime import datetime, timezone


def now() -> str:
    return datetime.now(timezone.utc).isoformat()


def process_snapshot() -> list[dict]:
    rows = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            raw = (entry / 'stat').read_text()
            end = raw.rfind(')')
            fields = raw[end + 2:].split()
            rows.append({
                'pid': int(entry.name), 'comm': raw[raw.index('(') + 1:end],
                'state': fields[0], 'ppid': int(fields[1]),
                'processGroup': int(fields[2]), 'session': int(fields[3]),
                'exitStatusRaw': int(fields[49]),
            })
        except (OSError, ValueError, IndexError):
            pass  # A racing process exit does not invalidate the snapshot.
    return sorted(rows, key=lambda row: row['pid'])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--receipt', type=Path)
    parser.add_argument('--log', type=Path)
    parser.add_argument('--cwd', type=Path)
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not command:
        parser.error('a command is required after --')
    if not sys.platform.startswith('linux'):
        parser.error('Linux is required for child-subreaper semantics')

    receipt_path = args.receipt.resolve() if args.receipt else None
    log_path = args.log.resolve() if args.log else None
    working_directory = args.cwd.resolve() if args.cwd else Path.cwd()
    before = process_snapshot()
    baseline_zombies = {row['pid'] for row in before if row['state'] == 'Z'}
    receipt = {
        'schemaVersion': 1, 'command': command, 'workingDirectory': str(working_directory),
        'startedAt': now(), 'wrapperPid': os.getpid(), 'beforeProcesses': before,
        'environment': {name: os.environ.get(name) for name in ('PATH', 'DOTNET_ROOT')},
        'reaped': [], 'forwardedSignals': [],
    }

    def write_receipt() -> None:
        if receipt_path:
            receipt_path.parent.mkdir(parents=True, exist_ok=True)
            receipt_path.write_text(json.dumps(receipt, indent=2) + '\n')

    libc = ctypes.CDLL(None, use_errno=True)
    # PR_SET_CHILD_SUBREAPER=36 and PR_GET_CHILD_SUBREAPER=37 from linux/prctl.h.
    if libc.prctl(36, 1, 0, 0, 0) != 0:
        raise OSError(ctypes.get_errno(), 'PR_SET_CHILD_SUBREAPER failed')
    enabled = ctypes.c_int()
    if libc.prctl(37, ctypes.byref(enabled), 0, 0, 0) != 0 or enabled.value != 1:
        raise RuntimeError('Child-subreaper enablement could not be verified')
    receipt['childSubreaperEnabled'] = True
    write_receipt()

    log_fd = None
    if log_path:
        log_path.parent.mkdir(parents=True, exist_ok=True)
        log_fd = os.open(log_path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o644)
    child = os.fork()
    if child == 0:
        try:
            os.setsid()  # Forward signals to the wrapped command's group, never our own.
            os.chdir(working_directory)
            if log_fd is not None:
                os.dup2(log_fd, 1)
                os.dup2(log_fd, 2)
                if log_fd > 2:
                    os.close(log_fd)
            os.execvp(command[0], command)
        except BaseException as error:
            print(f'subreaper: cannot execute command: {error}', file=sys.stderr, flush=True)
            os._exit(127)

    if log_fd is not None:
        os.close(log_fd)
    receipt['commandPid'] = child
    write_receipt()

    def forward(signum: int, _frame) -> None:
        receipt['forwardedSignals'].append({'signal': signum, 'at': now()})
        try:
            os.killpg(child, signum)
        except ProcessLookupError:
            try:
                os.kill(child, signum)
            except ProcessLookupError:
                pass

    for signum in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP, signal.SIGQUIT):
        signal.signal(signum, forward)

    root_status = None
    while root_status is None:
        try:
            pid, status = os.waitpid(-1, 0)
        except InterruptedError:
            continue
        receipt['reaped'].append({
            'pid': pid, 'statusRaw': status, 'exitCode': os.waitstatus_to_exitcode(status),
            'commandRoot': pid == child, 'at': now(),
        })
        if pid == child:
            root_status = status

    # Drain children already dead at command completion. Like an init wrapper, we return
    # the root command's result instead of waiting forever for background build servers.
    while True:
        try:
            pid, status = os.waitpid(-1, os.WNOHANG)
        except ChildProcessError:
            break
        if pid == 0:
            break
        receipt['reaped'].append({
            'pid': pid, 'statusRaw': status, 'exitCode': os.waitstatus_to_exitcode(status),
            'commandRoot': False, 'at': now(),
        })

    after = process_snapshot()
    exit_code = os.waitstatus_to_exitcode(root_status)
    receipt.update({
        'completedAt': now(), 'exitCode': exit_code,
        'status': 'passed' if exit_code == 0 else 'failed', 'afterProcesses': after,
        'baselineZombiePids': sorted(baseline_zombies),
        'addedZombiePids': [row['pid'] for row in after
                            if row['state'] == 'Z' and row['pid'] not in baseline_zombies],
        'remainingAdoptedProcesses': [row for row in after if row['ppid'] == os.getpid()],
    })
    write_receipt()
    print(f'subreaper: command exit={exit_code}; adopted descendants reaped='
          f'{sum(not row["commandRoot"] for row in receipt["reaped"])}; '
          f'new zombies={len(receipt["addedZombiePids"])}', file=sys.stderr, flush=True)

    if exit_code < 0:
        # Preserve termination by signal, not a fabricated ordinary success/exit status.
        signum = -exit_code
        if signum not in (signal.SIGKILL, signal.SIGSTOP):
            signal.signal(signum, signal.SIG_DFL)
        os.kill(os.getpid(), signum)
        return 128 + signum  # Only reached if the signal could not terminate us.
    return exit_code


if __name__ == '__main__':
    raise SystemExit(main())
