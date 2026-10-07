"""Own a disposable PostgreSQL instance around the existing schema verifier."""

from __future__ import annotations

import hashlib
import json
import math
import os
import signal
import subprocess
import sys
import tempfile
import threading
import time
import uuid
from collections.abc import Mapping, Sequence
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterator

from .migrations import resolve_git_commit


RUN_LABEL = "meridian.schema-control.run-id"
WORKTREE_LABEL = "meridian.schema-control.worktree"
INSPECT_FORMAT = (
    "{{json .Id}}\n"
    '{"meridian.schema-control.run-id":'
    '{{json (index .Config.Labels "meridian.schema-control.run-id")}},'
    '"meridian.schema-control.worktree":'
    '{{json (index .Config.Labels "meridian.schema-control.worktree")}}}\n'
    "{{json .NetworkSettings.Ports}}\n{{json .State.Running}}"
)
_READINESS_SCRIPT = """
import sys
try:
    import psycopg
except ImportError:
    print('Install tools/schema_control/requirements.txt before running local schema control.', file=sys.stderr)
    raise SystemExit(3)
try:
    with psycopg.connect(sys.argv[1], connect_timeout=int(sys.argv[2])) as connection:
        with connection.cursor() as cursor:
            cursor.execute('select 1')
            cursor.fetchone()
except Exception as exc:
    print(str(exc), file=sys.stderr)
    raise SystemExit(1)
"""


def _docker(
    args: Sequence[str],
    *,
    timeout: float | None = 30.0,
    env: Mapping[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["docker", *args],
        capture_output=True,
        text=True,
        check=False,
        timeout=timeout,
        env=dict(env) if env is not None else None,
        start_new_session=os.name != "nt",
        creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0,
    )


def _postgres_env(password: str) -> dict[str, str]:
    return {
        **{key: value for key, value in os.environ.items() if not key.startswith("PG")},
        "PGPASSWORD": password,
        "PYTHONUNBUFFERED": "1",
    }


def _probe_postgres(database_url: str, *, password: str, connect_timeout: int = 2) -> None:
    # libpq always reads defaults such as PGSERVICE from the environment. A
    # separate, bounded process avoids mutating its caller's environment while
    # making the readiness connection use exactly the same defaults as verify.
    result = subprocess.run(
        [sys.executable, "-c", _READINESS_SCRIPT, database_url, str(connect_timeout)],
        env=_postgres_env(password),
        capture_output=True,
        text=True,
        check=False,
        timeout=connect_timeout + 2,
        start_new_session=os.name != "nt",
        creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0,
    )
    if result.returncode == 3:
        raise RuntimeError(result.stderr.strip())
    if result.returncode:
        raise ConnectionError(result.stderr.strip() or "PostgreSQL readiness connection failed.")


def _worktree_id(root: Path) -> str:
    return hashlib.sha256(str(root.resolve()).encode("utf-8")).hexdigest()[:12]


def _new_run_root(root: Path) -> Path:
    runs = root / "build" / "schema-control" / "runs"
    runs.mkdir(parents=True, exist_ok=True)
    return Path(tempfile.mkdtemp(prefix=f"{_worktree_id(root)}-", dir=runs))


class _Cancelled(Exception):
    pass


class _Cancellation:
    def __init__(self) -> None:
        self.signum: int | None = None
        self.event = threading.Event()

    def request(self, signum: int, _frame: Any) -> None:
        if self.signum is None:
            self.signum = signum
        self.event.set()

    def check(self) -> None:
        if self.event.is_set():
            raise _Cancelled()


@contextmanager
def _handle_signals() -> Iterator[_Cancellation]:
    cancellation = _Cancellation()
    previous: dict[int, Any] = {}
    # Separate processes are the normal concurrency boundary. Embedders may also
    # use threads, where Python does not permit installing signal handlers.
    if threading.current_thread() is threading.main_thread():
        for signum in (signal.SIGINT, signal.SIGTERM):
            previous[signum] = signal.signal(signum, cancellation.request)
    try:
        yield cancellation
    finally:
        for signum, handler in previous.items():
            signal.signal(signum, handler)


def _timestamp() -> str:
    return datetime.now(timezone.utc).isoformat()


def _write_run(path: Path, details: Mapping[str, Any]) -> None:
    staging = path.with_suffix(".json.tmp")
    staging.write_text(json.dumps(details, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    staging.replace(path)


def _stop_verifier(child: subprocess.Popen[Any]) -> None:
    if child.poll() is not None:
        child.wait()
        return

    def send(signum: int) -> None:
        try:
            if os.name != "nt":
                os.killpg(child.pid, signum)
            elif signum == signal.SIGTERM:
                child.terminate()
            else:
                child.kill()
        except ProcessLookupError:
            # The child can exit between poll and signal delivery.
            pass

    send(signal.SIGTERM)
    try:
        child.wait(timeout=5)
    except subprocess.TimeoutExpired:
        send(signal.SIGKILL if os.name != "nt" else signal.SIGTERM)
        child.wait(timeout=5)


def run_disposable(
    *,
    root: Path,
    config: Mapping[str, Any],
    mode: str,
    base_ref: str | None,
    config_path: str,
    policies_path: str,
    waivers_path: str,
    image: str | None = None,
    readiness_timeout: float = 60.0,
) -> int:
    """Run verification or snapshot generation with owned resources and evidence.

    This entrypoint never promotes snapshots. Each run keeps its own candidate
    and diagnostic directory, including failed and cancelled runs.
    """
    if mode not in {"verify", "snapshot"}:
        raise ValueError("Disposable schema control mode must be verify or snapshot.")
    if not math.isfinite(readiness_timeout) or readiness_timeout <= 0:
        raise ValueError("readiness_timeout must be a positive finite number.")
    root = root.resolve()
    manifest_config = config.get("manifest", {})
    if not isinstance(manifest_config, Mapping):
        raise ValueError("schema-control manifest configuration must be a JSON object.")
    selected_image = image or str(manifest_config.get("postgres_image") or "")
    if not selected_image.strip():
        raise ValueError("A PostgreSQL image must be configured or supplied with --image.")

    with _handle_signals() as cancellation:
        # A moving Git ref must not change the comparison while PostgreSQL starts.
        baseline_sha = None
        candidate_sha = None
        try:
            baseline_sha = resolve_git_commit(root, base_ref) if base_ref is not None else None
            try:
                candidate_sha = resolve_git_commit(root, "HEAD")
            except ValueError:
                pass
        except (OSError, RuntimeError, ValueError):
            if not cancellation.event.is_set():
                raise
        run_root = _new_run_root(root)
        candidate_root = run_root / "candidate"
        candidate_root.mkdir()
        run_id = run_root.name
        container_name = f"meridian-schema-control-{uuid.uuid4().hex}"
        labels = {RUN_LABEL: run_id, WORKTREE_LABEL: _worktree_id(root)}
        password = uuid.uuid4().hex
        docker_env = {**os.environ, "POSTGRES_PASSWORD": password}
        verifier_env = _postgres_env(password)
        run_path = run_root / "run.json"
        verification_log = run_root / "verification.log"
        postgres_log = run_root / "postgres.log"
        docker_log = run_root / "docker.log"
        for log in (verification_log, postgres_log, docker_log):
            log.touch()
        details: dict[str, Any] = {
            "format": "meridian.schema-control-disposable-run.v1",
            "run_id": run_id,
            "worktree": str(root),
            "worktree_id": labels[WORKTREE_LABEL],
            "mode": mode,
            "image": selected_image,
            "baseline_sha": baseline_sha,
            "candidate_sha": candidate_sha,
            "candidate_root": str(candidate_root),
            "container_name": container_name,
            "container_id": None,
            "port": None,
            "started_at": _timestamp(),
            "status": "allocating",
            "cleanup": "pending",
            "exit_code": None,
        }
        _write_run(run_path, details)
        child: subprocess.Popen[Any] | None = None
        allocation_attempted = False
        allocation_uncertain = False
        exit_code = 2

        def redact(value: str) -> str:
            return value.replace(password, "[redacted]")

        def command(
            args: Sequence[str], *, timeout: float | None = 30.0, checked: bool = True
        ) -> subprocess.CompletedProcess[str]:
            with docker_log.open("a", encoding="utf-8") as log:
                log.write(f"docker {json.dumps(list(args))}\n")
                try:
                    result = _docker(args, timeout=timeout, env=docker_env)
                except Exception as exc:
                    log.write(redact(f"{type(exc).__name__}: {exc}\n"))
                    raise
                log.write(redact(result.stdout or ""))
                log.write(redact(result.stderr or ""))
            if checked and result.returncode:
                raise RuntimeError(
                    redact(f"docker {args[0]} failed: {(result.stderr or result.stdout).strip()}")
                )
            return result

        def inspect(resource: str) -> dict[str, Any] | None:
            result = command(["inspect", "--format", INSPECT_FORMAT, resource], checked=False)
            if result.returncode:
                message = (result.stderr or "").lower()
                if "no such" in message or "not found" in message:
                    return None
                raise RuntimeError(redact(f"Cannot inspect owned container: {result.stderr.strip()}"))
            fields = result.stdout.splitlines()
            if len(fields) != 4:
                raise RuntimeError("Docker returned invalid container ownership information.")
            identifier, actual_labels, ports, running = (
                json.loads(field) for field in fields
            )
            return {
                "id": identifier, "labels": actual_labels,
                "ports": ports, "running": running,
            }

        def owned(info: Mapping[str, Any]) -> bool:
            actual_labels = info.get("labels") or {}
            return all(actual_labels.get(key) == value for key, value in labels.items())

        try:
            print(f"Schema-control run artifacts: {run_root}", flush=True)
            cancellation.check()
            # Pull separately so a slow image download cannot leave a late
            # container allocation after the create client has timed out.
            available = command(
                ["image", "inspect", "--format", "{{.Id}}", selected_image], checked=False
            )
            if available.returncode:
                command(["pull", selected_image], timeout=120.0)
            cancellation.check()
            # Set this before the command: Docker may create a container even if
            # its client loses the response before returning the ID.
            allocation_attempted = True
            try:
                command(
                    [
                        "create", "--pull=never", "--name", container_name,
                        "--label", f"{RUN_LABEL}={labels[RUN_LABEL]}",
                        "--label", f"{WORKTREE_LABEL}={labels[WORKTREE_LABEL]}",
                        "--publish", "127.0.0.1::5432",
                        "--tmpfs", "/var/lib/postgresql/data",
                        "--env", "POSTGRES_USER=meridian",
                        "--env", "POSTGRES_PASSWORD",
                        "--env", "POSTGRES_DB=meridian_schema_control",
                        selected_image,
                    ],
                    # Keep this mutation's client until the daemon settles its
                    # response. Killing it on a timer could leave a container
                    # that appears only after cleanup has already finished.
                    # Cancellation is remembered and checked immediately after
                    # creation returns, before any database work starts.
                    timeout=None,
                )
            except Exception:
                allocation_uncertain = True
                raise
            cancellation.check()
            info = inspect(container_name)
            if info is None or not owned(info):
                raise RuntimeError("Docker did not allocate the expected owned container.")
            details["container_id"] = info["id"]
            details["status"] = "starting"
            _write_run(run_path, details)
            command(["start", info["id"]])
            cancellation.check()
            info = inspect(info["id"])
            if info is None or not owned(info):
                raise RuntimeError("Owned PostgreSQL container disappeared during startup.")
            bindings = (info["ports"] or {}).get("5432/tcp") or []
            binding = next(
                (item for item in bindings if item.get("HostIp") == "127.0.0.1"), None
            )
            if binding is None:
                raise RuntimeError("Docker did not publish an isolated loopback PostgreSQL port.")
            port = int(binding["HostPort"])
            if not 1 <= port <= 65535:
                raise RuntimeError("Docker returned an invalid PostgreSQL port.")
            details["port"] = port
            details["status"] = "waiting for PostgreSQL"
            _write_run(run_path, details)
            database_url = (
                f"postgresql://meridian@127.0.0.1:{port}/meridian_schema_control"
                "?hostaddr=127.0.0.1&sslmode=disable"
            )
            deadline = time.monotonic() + readiness_timeout
            last_error = "PostgreSQL has not accepted a connection."
            while True:
                cancellation.check()
                if time.monotonic() >= deadline:
                    raise RuntimeError(f"PostgreSQL readiness timed out: {last_error}")
                try:
                    _probe_postgres(database_url, password=password, connect_timeout=2)
                    break
                except RuntimeError:
                    # A missing dependency cannot become ready by retrying.
                    raise
                except Exception as exc:
                    last_error = redact(str(exc))
                    cancellation.event.wait(
                        min(0.25, max(0.0, deadline - time.monotonic()))
                    )
            cancellation.check()
            details["status"] = "verifying" if mode == "verify" else "generating snapshot"
            _write_run(run_path, details)
            argv = [
                sys.executable, str(root / "build" / "scripts" / "schema-control.py"),
                "--root", str(root), "--config", config_path,
                "--policies", policies_path, "--waivers", waivers_path,
                mode, "--database-url", database_url,
                "--candidate-root", str(candidate_root),
            ]
            if baseline_sha is not None:
                argv.extend(["--base-ref", baseline_sha])
            with verification_log.open("w", encoding="utf-8") as log:
                child = subprocess.Popen(
                    argv, cwd=root, env=verifier_env, stdout=log,
                    stderr=subprocess.STDOUT, start_new_session=os.name != "nt",
                    creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0,
                )
                while child.poll() is None:
                    cancellation.check()
                    cancellation.event.wait(0.1)
                exit_code = child.wait()
            cancellation.check()
            if exit_code < 0:
                exit_code = 128 - exit_code
            details["status"] = "passed" if exit_code == 0 else f"failed {mode}"
        except _Cancelled:
            details["status"] = "cancelled"
            exit_code = 128 + (cancellation.signum or signal.SIGINT)
        except Exception as exc:
            details["status"] = "failed"
            details["error"] = redact(f"{type(exc).__name__}: {exc}")
            print(f"schema-control: {details['error']}", file=sys.stderr)
        finally:
            # Never destroy a database underneath a verifier that still runs.
            try:
                if child is not None:
                    try:
                        _stop_verifier(child)
                    except Exception as exc:
                        details["verifier_stop_error"] = redact(f"{type(exc).__name__}: {exc}")
                        # Retry a direct kill/reap before tearing down its DB.
                        if child.poll() is None:
                            child.kill()
                            child.wait(timeout=5)
                if allocation_attempted:
                    info = inspect(container_name)
                    # Reconcile delayed daemon responses after a client timeout.
                    # Pulling is already complete; only container creation can
                    # still be in flight, and this lookup never touches peers.
                    reconcile_deadline = time.monotonic() + 5.0
                    while (
                        info is None and allocation_uncertain
                        and time.monotonic() < reconcile_deadline
                    ):
                        time.sleep(0.1)
                        info = inspect(container_name)
                    if info is None:
                        details["cleanup"] = (
                            "allocation unconfirmed" if allocation_uncertain
                            else "container absent"
                        )
                    elif not owned(info):
                        details["cleanup"] = "refused foreign container"
                        raise RuntimeError("Refusing to remove a container without this run's ownership labels.")
                    else:
                        details["container_id"] = info["id"]
                        try:
                            logs = command(["logs", info["id"]], checked=False)
                            postgres_log.write_text(
                                redact((logs.stdout or "") + (logs.stderr or "")),
                                encoding="utf-8",
                            )
                        finally:
                            # No shared network or named volume is allocated.
                            command(["rm", "--force", "--volumes", info["id"]])
                        details["cleanup"] = "removed"
                else:
                    details["cleanup"] = "not allocated"
            except Exception as exc:
                details["cleanup_error"] = redact(f"{type(exc).__name__}: {exc}")
                if details["cleanup"] == "pending":
                    details["cleanup"] = "failed"
                print(f"schema-control cleanup: {details['cleanup_error']}", file=sys.stderr)
                if exit_code == 0:
                    exit_code = 2
                    details["status"] = "failed cleanup"
            if cancellation.event.is_set():
                details["status"] = "cancelled"
                details["signal"] = cancellation.signum
                exit_code = 128 + (cancellation.signum or signal.SIGINT)
            # Child output is diagnostic evidence; redact the ephemeral password
            # even if an unexpected dependency printed its environment or DSN.
            verification_log.write_text(
                redact(verification_log.read_text(encoding="utf-8", errors="replace")),
                encoding="utf-8",
            )
            details["exit_code"] = exit_code
            details["finished_at"] = _timestamp()
            _write_run(run_path, details)
            print(f"Schema-control {mode}: {details['status']} (exit {exit_code}).", flush=True)
            print(f"Schema-control candidate: {candidate_root}", flush=True)
            print(f"Schema-control diagnostics: {run_path}", flush=True)
        return exit_code
