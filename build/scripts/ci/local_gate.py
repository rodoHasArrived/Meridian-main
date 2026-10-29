#!/usr/bin/env python3
"""Run the complete local gate with bounded, isolated .NET/browser overlap.

Only the first two lanes overlap. Generation and workflow tests run afterwards,
because they read/write repository files. Dependency caches are never deleted.
"""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import threading
import time
import uuid


FULL_LANES = ("verify-dotnet", "verify-browser", "verify-docs", "verify-workflows")
ROOT = Path(__file__).resolve().parents[3]


class ValidationLockBusy(Exception):
    pass


@contextmanager
def dotnet_writer_lock(root: Path, run_id: str):
    # Use the same exclusive-create protocol and owner-checked release as
    # buildctl, including buildctl --no-isolation writers in this checkout.
    sys.path.insert(0, str(ROOT / "build/python/cli"))
    import buildctl
    acquired = buildctl._acquire_validation_lock(
        root, run_id=run_id, command="local-gate .NET/browser pair", queue=False, timeout_seconds=0)
    if not acquired:
        raise ValidationLockBusy("Another buildctl writer owns the checkout's validation lock.")
    try:
        yield
    finally:
        buildctl._release_validation_lock(root, run_id)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def available_memory() -> int | None:
    """Available physical memory, including reclaimable inactive file cache."""
    memory = None
    if sys.platform == "win32":
        import ctypes

        class MemoryStatus(ctypes.Structure):
            _fields_ = [("length", ctypes.c_uint32), ("load", ctypes.c_uint32),
                        *((name, ctypes.c_uint64) for name in
                          ("totalPhysical", "availablePhysical", "totalPageFile", "availablePageFile",
                           "totalVirtual", "availableVirtual", "availableExtendedVirtual"))]

        try:
            query = ctypes.WinDLL("kernel32", use_last_error=True).GlobalMemoryStatusEx
            query.argtypes = [ctypes.POINTER(MemoryStatus)]
            query.restype = ctypes.c_int
            status = MemoryStatus(length=ctypes.sizeof(MemoryStatus))
            return int(status.availablePhysical) if query(ctypes.byref(status)) else None
        except (AttributeError, OSError):
            return None
    if sys.platform.startswith("linux"):
        try:
            for line in Path("/proc/meminfo").read_text().splitlines():
                if line.startswith("MemAvailable:"):
                    memory = max(0, int(line.split()[1])) * 1024
                    break
        except (OSError, ValueError, IndexError):
            pass
    try:
        if memory is None:
            memory = os.sysconf("SC_AVPHYS_PAGES") * os.sysconf("SC_PAGE_SIZE")
    except (AttributeError, OSError, ValueError):
        pass
    try:
        limit = Path("/sys/fs/cgroup/memory.max").read_text().strip()
        if limit != "max":
            limit_bytes = int(limit)
            used = int(Path("/sys/fs/cgroup/memory.current").read_text().strip())
            reclaimable = 0
            try:
                stats = dict(line.split() for line in Path("/sys/fs/cgroup/memory.stat").read_text().splitlines())
                reclaimable = min(max(0, int(stats.get("inactive_file", "0"))), max(0, used))
            except (OSError, ValueError):
                pass
            available = max(0, min(limit_bytes, limit_bytes - used + reclaimable))
            memory = min(memory, available) if memory is not None else available
    except (OSError, ValueError):
        pass
    return memory


def automatic_jobs() -> int:
    """Conservative local budget, including Linux container CPU/memory limits."""
    cpus = os.cpu_count() or 1
    try:
        quota, period = Path("/sys/fs/cgroup/cpu.max").read_text().split()
        if quota != "max":
            cpus = min(cpus, max(1, int(quota) // int(period)))
    except (OSError, ValueError, ZeroDivisionError):
        pass
    memory = available_memory()
    # Unknown memory capacity chooses sequential execution; --jobs 2 is explicit.
    return 2 if cpus >= 4 and memory is not None and memory >= 8 * 1024**3 else 1


@contextmanager
def gate_lock(root: Path):
    """Prevent aggregate runs from racing on tracked bundles/generated docs."""
    path = root / ".ai/locks/local-gate.lock"
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a+b") as handle:
        if os.name == "nt":
            import msvcrt
            handle.seek(0)
            if not handle.read(1):
                handle.write(b"\0")
                handle.flush()
            handle.seek(0)
            msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            yield
        finally:
            if os.name == "nt":
                handle.seek(0)
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(handle.fileno(), fcntl.LOCK_UN)


def write_json(path: Path, data: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


class Children:
    def __init__(self):
        self.processes: list[subprocess.Popen] = []
        self.lock = threading.RLock()
        self.stopped = False

    def run(self, command: list[str], root: Path, environment: dict, log: Path) -> int:
        log.parent.mkdir(parents=True, exist_ok=True)
        with log.open("w", encoding="utf-8") as output:
            with self.lock:
                if self.stopped:
                    raise InterruptedError("Local gate interrupted before child launch")
                process = subprocess.Popen(
                    command, cwd=root, env=environment, stdout=output, stderr=subprocess.STDOUT,
                    start_new_session=os.name != "nt",
                    creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0,
                )
                self.processes.append(process)
            return process.wait()

    def stop(self) -> None:
        with self.lock:
            self.stopped = True
            processes = list(self.processes)
        for process in processes:
            try:
                if os.name == "nt":
                    if process.poll() is None:
                        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
                else:
                    # The leader may already have exited while a descendant survives.
                    os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
        deadline = time.monotonic() + 2
        for process in processes:
            try:
                process.wait(timeout=max(0, deadline - time.monotonic()))
            except subprocess.TimeoutExpired:
                pass
        if os.name != "nt":
            for process in processes:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
        for process in processes:
            process.wait()


def run_gate(root: Path, *, lane: str = "quality-gate", jobs: int | None = None,
             cold: bool = False, commands: dict[str, list[str]] | None = None,
             prerequisite: list[str] | None = None) -> int:
    """Run fixed production commands; command injection is for isolated unit tests."""
    root = root.resolve()
    if lane not in ("quality-gate", "verify-fast") or jobs not in (None, 1, 2):
        raise ValueError("unsupported aggregate lane or concurrency")
    lanes = FULL_LANES if lane == "quality-gate" else FULL_LANES[:2]
    command_map = commands if commands is not None else {
        name: ["bash", "scripts/ci.sh", "--lane", name] for name in lanes}
    preflight = prerequisite if prerequisite is not None else [
        os.environ.get("MERIDIAN_CI_PYTHON", sys.executable), "build/python/prerequisites.py", "--lane", lane]
    run_id = "local-gate-" + uuid.uuid4().hex
    output_root = root / "artifacts/local-gates" / run_id
    output_root.mkdir(parents=True, exist_ok=False)
    receipt = root / ".ai/validation-runs" / (run_id + ".json")
    workers = jobs if jobs is not None else automatic_jobs()
    payload = {
        "schemaVersion": 2, "runId": run_id, "command": lane, "lane": lane,
        "project": "Meridian.sln", "configuration": "Release", "startedAt": utc_now(),
        "status": "started", "cacheState": "unspecified", "freshBuildOutputs": False,
        "jobs": workers, "completedLanes": [], "steps": [], "outputDirectory": str(output_root),
        "dependencyCaches": "retained",
    }
    started = time.monotonic()
    execution_started = None
    children = Children()
    state_lock = threading.Lock()
    interrupted = False
    previous_handlers = {}

    def interrupt(signum, _frame):
        nonlocal interrupted
        interrupted = True
        children.stop()
        raise KeyboardInterrupt

    def persist() -> None:
        write_json(receipt, payload)
        write_json(output_root / "summary.json", payload)

    def run_lane(name: str) -> dict:
        if interrupted:
            return {"name": name, "exitCode": 130, "durationSeconds": 0}
        lane_root = output_root / name
        environment = dict(os.environ)
        environment["MERIDIAN_CI_ARTIFACT_ROOT"] = str(lane_root)
        environment["MERIDIAN_BROWSER_RESULTS_DIR"] = str(lane_root / "test-results/browser")
        # A direct shell leaf does not invoke this aggregate scheduler again.
        if cold and name == "verify-dotnet":
            environment["MERIDIAN_CI_ISOLATION_KEY"] = run_id
        if name == "verify-browser":
            # The .NET host copies tracked assets during its build. Let browser
            # tests overlap, then wait before Vite replaces those same assets.
            environment["MERIDIAN_CI_BROWSER_BUILD_BARRIER"] = str(output_root / "dotnet-finished")
        log = lane_root / "lane.log"
        step_started = time.monotonic()
        print(f"[{name}] started; log: {log}", flush=True)
        try:
            code = children.run(command_map[name], root, environment, log)
        except OSError as exc:
            log.parent.mkdir(parents=True, exist_ok=True)
            log.write_text(str(exc) + "\n", encoding="utf-8")
            code = 127
        finally:
            if name == "verify-dotnet":
                (output_root / "dotnet-finished").touch()
        result = {"name": name, "exitCode": code,
                  "durationSeconds": round(time.monotonic() - step_started, 3), "log": str(log)}
        with state_lock:
            payload["steps"].append(result)
            payload["completedLanes"].append(name)
            persist()
        print(f"[{name}] finished: exit {code}, {result['durationSeconds']}s", flush=True)
        if code:
            print("\n".join(log.read_text(encoding="utf-8", errors="replace").splitlines()[-30:]), flush=True)
        return result

    persist()
    if threading.current_thread() is threading.main_thread():
        for sig in (signal.SIGINT, signal.SIGTERM):
            previous_handlers[sig] = signal.signal(sig, interrupt)
    exit_code = 1
    try:
        with gate_lock(root):
            execution_started = time.monotonic()
            payload["queueSeconds"] = round(execution_started - started, 3)
            print(f"Local {lane}: {workers} lane worker(s); evidence: {output_root}", flush=True)
            prerequisite_code = children.run(preflight, root, dict(os.environ), output_root / "prerequisites.log")
            if prerequisite_code:
                payload.update(status="blocked", failurePhase="prerequisites", exitCode=prerequisite_code)
                print((output_root / "prerequisites.log").read_text(encoding="utf-8", errors="replace"), flush=True)
                exit_code = prerequisite_code
            else:
                if cold:
                    build_directories = [root / "artifacts" / name / run_id for name in ("bin", "obj")]
                    for directory in build_directories:
                        directory.mkdir(parents=True, exist_ok=False)
                    payload.update(cacheState="cold-isolated", freshBuildOutputs=True,
                                   isolationKey=run_id, buildOutputDirectories=[str(path) for path in build_directories])
                # Retain the writer lock until the browser bundle completes too:
                # it replaces the tracked asset tree read by .NET host builds.
                with dotnet_writer_lock(root, run_id):
                    with ThreadPoolExecutor(max_workers=workers) as pool:
                        first = [pool.submit(run_lane, name) for name in lanes[:2]]
                        results = [future.result() for future in first]
                # Generation is a writer; workflow tests inspect that state. Join
                # both parallel lanes before either sequential lane starts.
                for name in lanes[2:]:
                    results.append(run_lane(name))
                exit_code = next((result["exitCode"] for result in results if result["exitCode"]), 0)
                payload.update(status="passed" if exit_code == 0 else "failed", exitCode=exit_code)
                if cold and lane == "quality-gate":
                    payload["coldFullGateSeconds"] = round(time.monotonic() - started, 3)
    except KeyboardInterrupt:
        interrupted = True
        children.stop()
        exit_code = 130
        payload.update(status="interrupted", exitCode=exit_code)
    except ValidationLockBusy as exc:
        exit_code = 3
        payload.update(status="blocked", exitCode=exit_code, failurePhase="validation-lock")
        print(str(exc), file=sys.stderr)
    except BlockingIOError:
        exit_code = 3
        payload.update(status="blocked", exitCode=exit_code, failurePhase="local-gate-lock")
        print("Another local aggregate gate owns the checkout. Wait for it to finish.", file=sys.stderr)
    except OSError as exc:
        exit_code = 1
        payload.update(status="failed", exitCode=exit_code, failurePhase="orchestration", error=str(exc))
        children.stop()
    finally:
        for sig, handler in previous_handlers.items():
            signal.signal(sig, handler)
        payload.update(finishedAt=utc_now(), feedbackSeconds=round(time.monotonic() - started, 3),
                       executionSeconds=round(time.monotonic() - execution_started, 3)
                       if execution_started is not None else None)
        if interrupted or set(payload["completedLanes"]) != set(FULL_LANES):
            payload.pop("coldFullGateSeconds", None)
        persist()
        markdown = [f"# Local {lane}", "", f"Result: {payload['status']}; {payload['feedbackSeconds']}s; workers: {workers}",
                    "", "Dependency caches retained. Cold mode isolates .NET compiled outputs only.", "",
                    "| Lane | Exit | Seconds |", "| --- | ---: | ---: |",
                    *[f"| {step['name']} | {step['exitCode']} | {step['durationSeconds']} |" for step in payload["steps"]], ""]
        (output_root / "summary.md").write_text("\n".join(markdown), encoding="utf-8")
        print(f"Local gate {payload['status']}; receipt: {receipt}", flush=True)
    return exit_code if exit_code >= 0 else 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--lane", choices=("quality-gate", "verify-fast"), default="quality-gate")
    parser.add_argument("--jobs", choices=("auto", "1", "2"), default="auto")
    parser.add_argument("--cold", action="store_true", help="Fresh .NET build outputs; retain dependency caches.")
    parser.add_argument("--wait-for-dotnet", type=Path, help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.wait_for_dotnet is not None:
        return wait_for_dotnet(args.wait_for_dotnet)
    return run_gate(ROOT, lane=args.lane, jobs=None if args.jobs == "auto" else int(args.jobs), cold=args.cold)


def wait_for_dotnet(marker: Path, timeout: float = 7200) -> int:
    deadline = time.monotonic() + timeout
    while not marker.is_file():
        if time.monotonic() >= deadline:
            print("Timed out waiting for the local .NET lane before replacing browser assets.", file=sys.stderr)
            return 1
        time.sleep(0.1)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
