from __future__ import annotations

import json
import os
import signal
import subprocess
import sys
import tempfile
import textwrap
import time
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.parse import parse_qs, urlsplit


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


# The Docker and SQL boundaries are synthetic; the wrapper, child processes,
# signals, filesystem evidence, and concurrent runs exercise real code.
DOCKER_SOURCE = r"""
import json
import os
import sys
import time
import uuid
from pathlib import Path

backend = Path(os.environ['FAKE_BACKEND'])
args = sys.argv[1:]
command = args[0]
event = {'kind': 'docker', 'args': args, 'time': time.monotonic()}
with (backend / 'events.jsonl').open('a', encoding='utf-8') as stream:
    stream.write(json.dumps(event) + '\n')

def find_resource(identity):
    for path in backend.glob('resource-*.json'):
        item = json.loads(path.read_text())
        if identity in (item['Id'], item['Name']):
            return path, item
    print('No such container', file=sys.stderr)
    raise SystemExit(1)

if command == os.environ.get('FAKE_FAIL_COMMAND'):
    print('synthetic Docker failure', file=sys.stderr)
    raise SystemExit(1)
if command == 'image':
    print('synthetic-image-id')
elif command == 'pull':
    print('synthetic image pulled')
elif command in ('create', 'run'):
    if os.environ.get('FAKE_DAEMON_DELAY'):
        time.sleep(float(os.environ['FAKE_DAEMON_DELAY']))
    name = args[args.index('--name') + 1]
    labels = {}
    for index, value in enumerate(args):
        if value == '--label':
            key, label = args[index + 1].split('=', 1)
            labels[key] = label
    identifier = uuid.uuid4().hex
    lock = backend / 'port-allocation.lock'
    while True:
        try:
            lock.mkdir()
            break
        except FileExistsError:
            time.sleep(0.001)
    try:
        counter = backend / 'next-port'
        port = int(counter.read_text()) if counter.exists() else 20000
        counter.write_text(str(port + 1))
    finally:
        lock.rmdir()
    item = {
        'Id': identifier,
        'Name': name,
        'Config': {'Labels': labels},
        'NetworkSettings': {'Ports': {'5432/tcp': [{
            'HostIp': '127.0.0.1', 'HostPort': str(port)
        }]}},
        'State': {'Running': command == 'run'},
    }
    (backend / ('resource-' + identifier + '.json')).write_text(json.dumps(item))
    print(identifier)
elif command == 'start':
    path, item = find_resource(args[-1])
    item['State']['Running'] = True
    path.write_text(json.dumps(item))
    print(item['Id'])
elif command == 'inspect':
    path, item = find_resource(args[-1])
    if '--format' in args:
        for value in (item['Id'], item['Config']['Labels'],
                      item['NetworkSettings']['Ports'], item['State']['Running']):
            print(json.dumps(value))
    else:
        print(json.dumps([item]))
elif command == 'logs':
    find_resource(args[-1])
    print('synthetic PostgreSQL diagnostic log')
    if os.environ.get('FAKE_LOG_PASSWORD'):
        print(os.environ['POSTGRES_PASSWORD'])
elif command == 'rm':
    path, item = find_resource(args[-1])
    path.unlink()
    print(item['Id'])
elif command == 'stop':
    path, item = find_resource(args[-1])
    item['State']['Running'] = False
    path.write_text(json.dumps(item))
else:
    raise SystemExit('Unhandled fake Docker command: ' + command)
"""


VERIFIER_SOURCE = r"""
import json
import os
import signal
import sys
import time
from pathlib import Path

backend = Path(os.environ['FAKE_BACKEND'])
args = sys.argv[1:]
candidate = Path(args[args.index('--candidate-root') + 1])
if not candidate.is_absolute():
    candidate = Path(args[args.index('--root') + 1]) / candidate
candidate.mkdir(parents=True, exist_ok=True)
(candidate / 'reports').mkdir(exist_ok=True)
(candidate / 'reports' / 'summary.md').write_text('retained verifier evidence\n')
event = {'kind': 'verifier', 'args': args, 'pid': os.getpid(),
         'candidate': str(candidate), 'time': time.monotonic(),
         'password_supplied': bool(os.environ.get('PGPASSWORD')),
         'pg_environment': {
             key: '[ephemeral]' if key == 'PGPASSWORD' else value
             for key, value in os.environ.items() if key.startswith('PG')
         }}
def cancel(signum, frame):
    (candidate / 'terminated.json').write_text(json.dumps({
        'signal': signum, 'time': time.monotonic()
    }))
    print('synthetic verifier terminated', flush=True)
    raise SystemExit(128 + signum)

signal.signal(signal.SIGINT, cancel)
signal.signal(signal.SIGTERM, cancel)
with (backend / 'events.jsonl').open('a', encoding='utf-8') as stream:
    stream.write(json.dumps(event) + '\n')
(candidate / 'started.json').write_text(json.dumps(event))
print('synthetic verification output', flush=True)
if os.environ.get('FAKE_LOG_PASSWORD'):
    (backend / 'ephemeral-password').write_text(os.environ['PGPASSWORD'])
    print(os.environ['PGPASSWORD'], flush=True)
if os.environ.get('FAKE_VERIFIER_WAIT'):
    while True:
        time.sleep(0.01)
if os.environ.get('FAKE_TAMPER_OWNERSHIP'):
    for path in backend.glob('resource-*.json'):
        item = json.loads(path.read_text())
        for key in item['Config']['Labels']:
            item['Config']['Labels'][key] = 'someone-else'
        path.write_text(json.dumps(item))
raise SystemExit(int(os.environ.get('FAKE_VERIFIER_STATUS', '0')))
"""


LAUNCHER_SOURCE = r"""
import json
import os
import signal
import subprocess
import sys
import threading
import time
from pathlib import Path

sys.path.insert(0, os.environ['FAKE_REPOSITORY'])
from tools.schema_control import disposable

original_run = subprocess.run
original_popen = subprocess.Popen
backend = Path(os.environ['FAKE_BACKEND'])

class VerifierProcess(original_popen):
    def wait(self, *args, **kwargs):
        status = super().wait(*args, **kwargs)
        with (backend / 'events.jsonl').open('a', encoding='utf-8') as stream:
            stream.write(json.dumps({'kind': 'reaped', 'pid': self.pid,
                                     'time': time.monotonic()}) + '\n')
        return status

def docker(args, *, timeout=30.0, env=None):
    actual_env = os.environ.copy()
    if env:
        actual_env.update(env)
    if args[0] == 'create' and os.environ.get('FAKE_LATE_CREATE_TIMED_OUT'):
        actual_env['FAKE_DAEMON_DELAY'] = '0.3'
        daemon_operation = original_popen(
            [sys.executable, os.environ['FAKE_DOCKER'], *args],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=actual_env,
        )
        threading.Thread(target=daemon_operation.wait, daemon=True).start()
        raise subprocess.TimeoutExpired(['docker', *args], timeout)
    result = original_run(
        [sys.executable, os.environ['FAKE_DOCKER'], *args],
        capture_output=True, text=True, timeout=timeout, env=actual_env,
    )
    if args[0] == 'create' and os.environ.get('FAKE_CREATE_TIMED_OUT'):
        raise subprocess.TimeoutExpired(['docker', *args], timeout)
    return result

def popen(args, **kwargs):
    if any(str(value).endswith('schema-control.py') for value in args):
        entrypoint = next(index for index, value in enumerate(args)
                          if str(value).endswith('schema-control.py'))
        args = [sys.executable, os.environ['FAKE_VERIFIER'], *args[entrypoint + 1:]]
        return VerifierProcess(args, **kwargs)
    return original_popen(args, **kwargs)

def bridge_windows_cancellation():
    # Windows TerminateProcess cannot deliver SIGTERM to a Python handler.
    # Invoke the installed callback there; POSIX tests deliver real OS signals.
    request = backend / ('cancel-' + str(os.getpid()) + '.json')
    while True:
        if request.exists():
            signum = json.loads(request.read_text())['signal']
            handler = signal.getsignal(signum)
            if callable(handler):
                handler(signum, None)
                return
        time.sleep(0.01)

if os.name == 'nt' or os.environ.get('FAKE_CALLBACK_CANCELLATION'):
    threading.Thread(target=bridge_windows_cancellation, daemon=True).start()

attempts = 0
def probe(database_url, *, password, connect_timeout=2):
    global attempts
    attempts += 1
    with (backend / 'events.jsonl').open('a', encoding='utf-8') as stream:
        stream.write(json.dumps({'kind': 'connect', 'attempt': attempts,
                                 'database_url': database_url,
                                 'time': time.monotonic()}) + '\n')
    if attempts < int(os.environ.get('FAKE_READY_AFTER', '1')):
        raise OSError('synthetic PostgreSQL is not ready')

disposable._docker = docker
disposable._probe_postgres = probe
disposable.resolve_git_commit = lambda root, ref: 'a' * 40
disposable.subprocess.Popen = popen
raise SystemExit(disposable.run_disposable(
    root=Path(sys.argv[1]), config={'manifest': {'postgres_image': 'postgres:16'}},
    mode=sys.argv[2], base_ref='reviewed-baseline',
    config_path='database/schema-control.json',
    policies_path='database/policies/schema-control.json',
    waivers_path='database/policies/migration-waivers.json',
    readiness_timeout=float(sys.argv[3]),
))
"""


class SchemaControlDisposableTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.root = self.directory / 'worktree'
        self.root.mkdir()
        (self.root / 'build/scripts').mkdir(parents=True)
        (self.root / 'build/scripts/schema-control.py').touch()
        self.backend = self.directory / 'backend'
        self.backend.mkdir()
        self.environment = os.environ.copy()
        self.environment.update(
            FAKE_REPOSITORY=str(REPOSITORY_ROOT),
            FAKE_BACKEND=str(self.backend),
        )
        for name, source in (
            ('docker', DOCKER_SOURCE), ('verifier', VERIFIER_SOURCE),
            ('launcher', LAUNCHER_SOURCE),
        ):
            path = self.directory / (name + '.py')
            path.write_text(textwrap.dedent(source), encoding='utf-8')
            self.environment['FAKE_' + name.upper()] = str(path)

    def launch(self, *, root: Path | None = None, mode: str = 'verify',
               readiness_timeout: float = 3.0, **environment: str) -> subprocess.Popen:
        env = {**self.environment, **environment}
        process = subprocess.Popen(
            [sys.executable, self.environment['FAKE_LAUNCHER'], str(root or self.root),
             mode, str(readiness_timeout)],
            cwd=REPOSITORY_ROOT, env=env, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, text=True,
        )
        self.addCleanup(self.stop_process, process)
        return process

    def stop_process(self, process: subprocess.Popen) -> None:
        if process.poll() is None:
            self.cancel(process, signal.SIGTERM)
        try:
            process.communicate(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.communicate(timeout=5)

    def finish(self, process: subprocess.Popen, status: int = 0) -> str:
        output, _ = process.communicate(timeout=15)
        self.assertEqual(status, process.returncode, output)
        return output

    def cancel(self, process: subprocess.Popen, signum: int, *, callback: bool = False) -> None:
        if os.name == 'nt' or callback:
            request = self.backend / f'cancel-{process.pid}.json'
            temporary = request.with_suffix('.tmp')
            temporary.write_text(json.dumps({'signal': signum}))
            temporary.replace(request)
        else:
            process.send_signal(signum)

    def events(self) -> list[dict]:
        path = self.backend / 'events.jsonl'
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def verifier_events(self) -> list[dict]:
        return [event for event in self.events() if event['kind'] == 'verifier']

    def wait_for_verifiers(self, count: int) -> list[dict]:
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            events = self.verifier_events()
            if len(events) >= count:
                return events
            time.sleep(0.01)
        self.fail(f'verifiers did not start: {self.events()}')

    def wait_for_connection(self) -> None:
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            if any(event['kind'] == 'connect' for event in self.events()):
                return
            time.sleep(0.01)
        self.fail(f'readiness did not start: {self.events()}')

    def run_roots(self, root: Path | None = None) -> list[Path]:
        return sorted((root or self.root).glob('build/schema-control/runs/*'))

    def assert_diagnostics(self, run_root: Path, *, verified: bool = True) -> None:
        self.assertTrue((run_root / 'run.json').is_file())
        self.assertIn('synthetic PostgreSQL diagnostic log',
                      (run_root / 'postgres.log').read_text())
        if verified:
            self.assertIn('synthetic verification output',
                          (run_root / 'verification.log').read_text())
            self.assertTrue((run_root / 'candidate/reports/summary.md').is_file())

    def assert_logs_precede_removal(self) -> None:
        events = self.events()
        removals = [(index, event) for index, event in enumerate(events)
                    if event['kind'] == 'docker' and event['args'][0] == 'rm']
        self.assertTrue(removals, events)
        for index, removal in removals:
            identity = removal['args'][-1]
            self.assertTrue(any(event['kind'] == 'docker'
                                and event['args'][0] == 'logs'
                                and event['args'][-1] == identity
                                for event in events[:index]), events)

    def test_consecutive_runs_keep_independent_evidence_and_remove_owned_resources(self) -> None:
        self.finish(self.launch())
        first_root = self.run_roots()[0]
        first_evidence = (first_root / 'candidate/reports/summary.md').read_bytes()
        self.finish(self.launch())

        run_roots = self.run_roots()
        self.assertEqual(2, len(run_roots))
        self.assertEqual(first_evidence, (first_root / 'candidate/reports/summary.md').read_bytes())
        creates = [event['args'] for event in self.events()
                   if event['kind'] == 'docker' and event['args'][0] == 'create']
        self.assertEqual(2, len(creates))
        names = [args[args.index('--name') + 1] for args in creates]
        self.assertEqual(2, len(set(names)))
        for args in creates:
            self.assertEqual('127.0.0.1::5432', args[args.index('--publish') + 1])
        self.assertFalse(list(self.backend.glob('resource-*.json')))
        for run_root in run_roots:
            self.assert_diagnostics(run_root)
        self.assert_logs_precede_removal()

    def test_concurrent_worktrees_and_same_worktree_runs_are_isolated(self) -> None:
        other_root = self.directory / 'other-worktree'
        other_root.mkdir()
        processes = [self.launch(root=root, FAKE_VERIFIER_WAIT='1')
                     for root in (self.root, self.root, other_root)]
        verifiers = self.wait_for_verifiers(3)
        self.assertEqual(3, len({event['candidate'] for event in verifiers}))
        self.assertEqual(3, len(list(self.backend.glob('resource-*.json'))))
        urls = [event['args'][event['args'].index('--database-url') + 1]
                for event in verifiers]
        self.assertEqual(3, len(set(urls)))
        for process in processes:
            self.cancel(process, signal.SIGTERM)
        for process in processes:
            self.finish(process, 128 + signal.SIGTERM)
        self.assertFalse(list(self.backend.glob('resource-*.json')))
        self.assertEqual(2, len(self.run_roots()))
        self.assertEqual(1, len(self.run_roots(other_root)))
        self.assert_logs_precede_removal()

    def test_readiness_retries_before_starting_verification(self) -> None:
        self.finish(self.launch(FAKE_READY_AFTER='3'))
        events = self.events()
        attempts = [event for event in events if event['kind'] == 'connect']
        self.assertEqual(3, len(attempts))
        verifier = self.verifier_events()[0]
        self.assertGreater(verifier['time'], attempts[-1]['time'])
        self.assertTrue(verifier['password_supplied'])
        self.assertEqual('a' * 40,
                         verifier['args'][verifier['args'].index('--base-ref') + 1])

    def test_readiness_timeout_retains_diagnostics_and_cleans_up_without_verifier(self) -> None:
        self.finish(self.launch(readiness_timeout=0.15, FAKE_READY_AFTER='9999'), 2)
        self.assertFalse(self.verifier_events())
        self.assertFalse(list(self.backend.glob('resource-*.json')))
        self.assert_diagnostics(self.run_roots()[0], verified=False)
        self.assert_logs_precede_removal()

    def test_verifier_failure_status_and_evidence_survive_cleanup(self) -> None:
        self.finish(self.launch(FAKE_VERIFIER_STATUS='7'), 7)
        self.assertFalse(list(self.backend.glob('resource-*.json')))
        self.assert_diagnostics(self.run_roots()[0])
        self.assert_logs_precede_removal()

    def test_failure_starting_container_removes_allocated_resource(self) -> None:
        self.finish(self.launch(FAKE_FAIL_COMMAND='start'), 2)
        self.assertFalse(self.verifier_events())
        self.assertFalse(list(self.backend.glob('resource-*.json')))
        self.assert_diagnostics(self.run_roots()[0], verified=False)
        self.assert_logs_precede_removal()

    def test_create_client_timeout_still_finds_and_cleans_owned_resource(self) -> None:
        self.finish(self.launch(FAKE_CREATE_TIMED_OUT='1'), 2)
        self.assertFalse(self.verifier_events())
        self.assertFalse(list(self.backend.glob('resource-*.json')))
        self.assert_diagnostics(self.run_roots()[0], verified=False)
        self.assert_logs_precede_removal()

    def test_delayed_daemon_allocation_after_client_timeout_is_reconciled(self) -> None:
        self.finish(self.launch(FAKE_LATE_CREATE_TIMED_OUT='1'), 2)
        self.assertFalse(self.verifier_events())
        self.assertFalse(list(self.backend.glob('resource-*.json')))
        self.assert_diagnostics(self.run_roots()[0], verified=False)
        events = self.events()
        removal = next(event for event in events
                       if event['kind'] == 'docker' and event['args'][0] == 'rm')
        inspections = [event for event in events
                       if event['kind'] == 'docker' and event['args'][0] == 'inspect'
                       and event['time'] < removal['time']]
        self.assertGreater(len(inspections), 1)
        self.assert_logs_precede_removal()

    def test_cancellation_while_waiting_for_readiness_cleans_up(self) -> None:
        process = self.launch(readiness_timeout=30.0, FAKE_READY_AFTER='9999')
        self.wait_for_connection()
        self.cancel(process, signal.SIGTERM)
        self.finish(process, 128 + signal.SIGTERM)
        self.assertFalse(self.verifier_events())
        self.assertFalse(list(self.backend.glob('resource-*.json')))
        self.assert_diagnostics(self.run_roots()[0], verified=False)
        self.assert_logs_precede_removal()

    def test_cancellation_callback_bridge_reaps_verifier_before_cleanup(self) -> None:
        process = self.launch(FAKE_VERIFIER_WAIT='1', FAKE_CALLBACK_CANCELLATION='1')
        verifier = self.wait_for_verifiers(1)[0]
        self.cancel(process, signal.SIGTERM, callback=True)
        self.finish(process, 128 + signal.SIGTERM)
        reaped = next(event for event in self.events()
                      if event['kind'] == 'reaped' and event['pid'] == verifier['pid'])
        removal = next(event for event in self.events()
                       if event['kind'] == 'docker' and event['args'][0] == 'rm')
        self.assertLess(reaped['time'], removal['time'])
        self.assertFalse(list(self.backend.glob('resource-*.json')))
        self.assert_diagnostics(Path(verifier['candidate']).parent)

    def test_ephemeral_password_is_redacted_from_retained_diagnostics(self) -> None:
        self.finish(self.launch(FAKE_LOG_PASSWORD='1'))
        password = (self.backend / 'ephemeral-password').read_text()
        run_root = self.run_roots()[0]
        for path in run_root.rglob('*'):
            if path.is_file():
                self.assertNotIn(password, path.read_text(), str(path))
        self.assertIn('[redacted]', (run_root / 'verification.log').read_text())
        self.assertIn('[redacted]', (run_root / 'postgres.log').read_text())

    def test_ambient_libpq_settings_cannot_redirect_owned_verification(self) -> None:
        self.finish(self.launch(
            PGHOSTADDR='192.0.2.42', PGSERVICE='foreign-database',
            PGSSLMODE='require', PGOPTIONS='-c search_path=foreign',
            PGPASSWORD='inherited-password',
        ))
        verifier = self.verifier_events()[0]
        self.assertEqual({'PGPASSWORD': '[ephemeral]'}, verifier['pg_environment'])
        url = verifier['args'][verifier['args'].index('--database-url') + 1]
        query = parse_qs(urlsplit(url).query)
        self.assertEqual(['127.0.0.1'], query['hostaddr'])
        self.assertEqual(['disable'], query['sslmode'])
        attempts = [event for event in self.events() if event['kind'] == 'connect']
        self.assertTrue(attempts)
        self.assertEqual(url, attempts[0]['database_url'])

    def test_readiness_probe_filters_libpq_settings_without_changing_parent_environment(self) -> None:
        from tools.schema_control import disposable

        ambient = {
            'PGHOSTADDR': '192.0.2.42', 'PGSERVICE': 'foreign-database',
            'PGSSLMODE': 'require', 'PGOPTIONS': '-c search_path=foreign',
            'PGPASSWORD': 'inherited-password',
        }
        for fail in (False, True):
            with self.subTest(connection_failure=fail):
                captured: dict = {}
                def run(argv: list[str], **kwargs):
                    captured['argv'] = argv
                    captured['kwargs'] = kwargs
                    return subprocess.CompletedProcess(
                        argv, 1 if fail else 0, stdout='',
                        stderr='synthetic connection failure' if fail else '',
                    )

                with patch.dict(os.environ, ambient):
                    before = dict(os.environ)
                    with patch('tools.schema_control.disposable.subprocess.run', side_effect=run):
                        if fail:
                            with self.assertRaises(ConnectionError):
                                disposable._probe_postgres(
                                    'postgresql://owned', password='owned-password'
                                )
                        else:
                            self.assertIsNone(disposable._probe_postgres(
                                'postgresql://owned', password='owned-password'
                            ))
                    self.assertEqual(before, dict(os.environ))
                pg_environment = {
                    key: value for key, value in captured['kwargs']['env'].items()
                    if key.startswith('PG')
                }
                self.assertEqual({'PGPASSWORD': 'owned-password'}, pg_environment)
                self.assertNotIn('owned-password', captured['argv'])

    def test_snapshot_never_promotes_generated_outputs(self) -> None:
        manifest = self.root / 'database/manifest/existing.json'
        manifest.parent.mkdir(parents=True)
        manifest.write_text('{"reviewed": true}\n')
        self.finish(self.launch(mode='snapshot'))
        verifier = self.verifier_events()[0]
        self.assertIn('snapshot', verifier['args'])
        self.assertNotIn('promote', verifier['args'])
        self.assertEqual('{"reviewed": true}\n', manifest.read_text())
        self.assert_diagnostics(self.run_roots()[0])

    def test_signals_terminate_and_reap_verifier_before_resource_cleanup(self) -> None:
        for signum in (signal.SIGINT, signal.SIGTERM):
            with self.subTest(signal=signum):
                started = len(self.verifier_events())
                process = self.launch(FAKE_VERIFIER_WAIT='1')
                verifier = self.wait_for_verifiers(started + 1)[-1]
                self.cancel(process, signum)
                self.finish(process, 128 + signum)
                candidate = Path(verifier['candidate'])
                removal = next(event for event in self.events()
                               if event['kind'] == 'docker'
                               and event['args'][0] == 'rm'
                               and event['time'] > verifier['time'])
                reaped = next(event for event in self.events()
                              if event['kind'] == 'reaped'
                              and event['pid'] == verifier['pid'])
                self.assertLess(reaped['time'], removal['time'])
                if os.name != 'nt':
                    self.assertTrue((candidate / 'terminated.json').is_file())
                    termination = json.loads((candidate / 'terminated.json').read_text())
                    self.assertLess(termination['time'], removal['time'])
                    with self.assertRaises(ProcessLookupError):
                        os.kill(verifier['pid'], 0)
                self.assert_diagnostics(candidate.parent)
                self.assertFalse(list(self.backend.glob('resource-*.json')))
        self.assert_logs_precede_removal()

    def test_cleanup_refuses_a_resource_with_changed_ownership_labels(self) -> None:
        output = self.finish(self.launch(FAKE_TAMPER_OWNERSHIP='1'), 2)
        self.assertIn('ownership', output.lower())
        self.assertEqual(1, len(list(self.backend.glob('resource-*.json'))))
        self.assertFalse([event for event in self.events()
                          if event['kind'] == 'docker' and event['args'][0] == 'rm'])
        run_root = self.run_roots()[0]
        report = json.loads((run_root / 'run.json').read_text())
        self.assertEqual('refused foreign container', report['cleanup'])
        self.assertIn('synthetic verification output',
                      (run_root / 'verification.log').read_text())
        self.assertTrue((run_root / 'docker.log').is_file())


if __name__ == '__main__':
    unittest.main()
