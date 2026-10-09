#!/usr/bin/env python3
"""Supply missing container-init child reaping without changing the child command."""
import ctypes
import os
import signal
import sys
import time

if len(sys.argv) < 2:
    raise SystemExit("Usage: run-with-child-reaper.py COMMAND [ARG ...]")
libc = ctypes.CDLL(None, use_errno=True)
if libc.prctl(36, 1, 0, 0, 0) != 0:  # PR_SET_CHILD_SUBREAPER
    raise OSError(ctypes.get_errno(), "Cannot enable child subreaper")

command_pid = os.fork()
if command_pid == 0:
    os.setsid()
    os.execvp(sys.argv[1], sys.argv[1:])

def forward(signum, _frame):
    try:
        os.killpg(command_pid, signum)
    except ProcessLookupError:
        pass

signal.signal(signal.SIGTERM, forward)
signal.signal(signal.SIGINT, forward)
while True:
    try:
        child_pid, status = os.waitpid(-1, 0)
    except InterruptedError:
        continue
    if child_pid == command_pid:
        command_status = os.waitstatus_to_exitcode(status)
        break

# Persistent build servers may outlive a successful command. Do not wait for them.
drain_deadline = time.monotonic() + 0.5
while time.monotonic() < drain_deadline:
    try:
        child_pid, _ = os.waitpid(-1, os.WNOHANG)
    except ChildProcessError:
        break
    if child_pid == 0:
        time.sleep(0.01)
raise SystemExit(command_status if command_status >= 0 else 128 - command_status)
