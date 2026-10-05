import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

import { assertPortAvailable, parseOptions, waitForHttp } from './web-dev.mjs';
import { startOwnedProcess } from './owned-process.mjs';

const repoRoot = fileURLToPath(new URL('../../', import.meta.url));
const processFixture = path.join(repoRoot, 'tests/scripts/fixtures/web-dev/process-tree.mjs');
const launcherFixture = path.join(repoRoot, 'tests/scripts/fixtures/web-dev/launcher.mjs');

async function temporaryDirectory(t) {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'meridian-web-dev-test-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  return directory;
}

async function eventually(read, description, timeoutMs = 5_000) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      return await read();
    } catch (error) {
      lastError = error;
      await delay(25);
    }
  }
  throw new Error(`Timed out waiting for ${description}`, { cause: lastError });
}

async function readState(stateFile) {
  return eventually(async () => JSON.parse(await readFile(stateFile, 'utf8')), stateFile, 10_000);
}

async function listen(t, handler, host = '127.0.0.1') {
  const server = createServer(handler);
  t.after(async () => {
    server.closeAllConnections();
    await new Promise((resolve) => server.close(resolve));
  });
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, host, resolve);
  });
  return server;
}

async function assertStopped(state) {
  await eventually(async () => {
    await assertPortAvailable(state.port);
    if (state.descendant) await assertPortAvailable(state.descendant.port);
  }, 'owned process ports to close');
}

async function reservePorts(t) {
  const first = await listen(t, (_request, response) => response.end());
  const second = await listen(t, (_request, response) => response.end());
  const ports = [first.address().port, second.address().port];
  await Promise.all([first, second].map((server) => new Promise((resolve) => server.close(resolve))));
  return ports;
}

async function launchFixture(t, mode, scenario = 'running', ports) {
  const directory = await temporaryDirectory(t);
  const viteDirectory = path.join(directory, 'src/Meridian.Ui/dashboard/node_modules/vite/bin');
  await mkdir(viteDirectory, { recursive: true });
  await writeFile(path.join(viteDirectory, 'vite.js'), '// dependency presence fixture\n');
  const [frontendPort, backendPort] = ports ?? await reservePorts(t);
  const launcher = spawn(process.execPath, [
    launcherFixture, directory, mode, String(frontendPort), String(backendPort), scenario,
  ], { cwd: repoRoot, stdio: ['ignore', 'pipe', 'pipe', 'ipc'] });
  let output = '';
  launcher.stdout.on('data', (chunk) => { output += chunk; });
  launcher.stderr.on('data', (chunk) => { output += chunk; });
  const result = new Promise((resolve, reject) => {
    launcher.once('error', reject);
    launcher.once('exit', (code, signal) => resolve({ code, signal }));
  });
  const interrupt = () => {
    if (process.platform === 'win32') launcher.send({ type: 'interrupt' });
    else launcher.kill('SIGINT');
  };
  t.after(async () => {
    if (launcher.exitCode !== null || launcher.signalCode !== null) return;
    interrupt();
    await result;
  });
  return {
    directory, frontendPort, backendPort, launcher, result, interrupt,
    output: () => output,
    ready: () => eventually(async () => {
      assert.match(output, /\[dev\] Ready \(/);
    }, 'launcher ready output', 10_000),
  };
}

test('mode selection is explicit and validates conflicting ports and invalid options', () => {
  assert.throws(() => parseOptions([]), /mode/i);
  assert.throws(() => parseOptions(['--mode', 'auto']), /mode/i);
  assert.throws(() => parseOptions(['--mode', 'backend-connected', '--port', '8080']), /different ports/i);
  for (const value of ['0', '-1', '65536', '5173.5', 'abc']) {
    assert.throws(() => parseOptions(['--mode', 'fixture-only', '--port', value]), /port/i);
  }
  assert.throws(() => parseOptions(['--mode', 'fixture-only', '--unknown', 'x']), /option/i);
  assert.throws(() => parseOptions(['--mode', 'fixture-only', '--mode', 'backend-connected']), /repeated/i);
  assert.equal(parseOptions(['--mode=fixture-only', '--port=6001']).port, 6001);
  assert.equal(parseOptions(['--mode', 'backend-connected']).mode, 'backend-connected');
  assert.equal(parseOptions(['--mode', 'backend-connected']).timeoutMs, 600_000);
});

test('an occupied IPv4 port is rejected without stopping its listener', async (t) => {
  const server = await listen(t, (_request, response) => response.end('unrelated listener'));
  const port = server.address().port;

  await assert.rejects(assertPortAvailable(port, { label: 'Vite' }), /port|use|occupied/i);
  assert.equal(await (await fetch(`http://127.0.0.1:${port}`)).text(), 'unrelated listener');
});

test('an occupied IPv6 loopback port is rejected without stopping its listener', async (t) => {
  let server;
  try {
    server = await listen(t, (_request, response) => response.end('unrelated IPv6 listener'), '::1');
  } catch (error) {
    if (['EADDRNOTAVAIL', 'EAFNOSUPPORT', 'ENETUNREACH'].includes(error.code)) {
      t.skip('IPv6 loopback is unavailable on this host');
      return;
    }
    throw error;
  }
  const port = server.address().port;

  await assert.rejects(assertPortAvailable(port, { label: 'backend' }), /port|use|occupied/i);
  assert.equal(await (await fetch(`http://[::1]:${port}`)).text(), 'unrelated IPv6 listener');
});

test('readiness retries HTTP failures until the endpoint succeeds', async (t) => {
  let requests = 0;
  const server = await listen(t, (_request, response) => {
    response.writeHead(++requests < 3 ? 503 : 200);
    response.end();
  });

  await waitForHttp(`http://127.0.0.1:${server.address().port}/ready`, { timeoutMs: 3_000 });
  assert.ok(requests >= 3, 'a listening socket or HTTP failure must not count as readiness');
});

test('readiness rejects a persistent HTTP error', async (t) => {
  let requests = 0;
  const server = await listen(t, (_request, response) => {
    requests += 1;
    response.writeHead(404);
    response.end();
  });

  await assert.rejects(waitForHttp(`http://127.0.0.1:${server.address().port}/missing`, { timeoutMs: 350 }));
  assert.ok(requests > 0);
});

test('readiness rejects an unrelated HTTP 200 response with the wrong session marker', async (t) => {
  const server = await listen(t, (_request, response) => {
    response.setHeader('X-Meridian-Dev-Session', 'different-launcher');
    response.end('ready');
  });

  await assert.rejects(waitForHttp(`http://127.0.0.1:${server.address().port}/ready`, {
    timeoutMs: 300,
    expectedHeader: { name: 'X-Meridian-Dev-Session', value: 'this-launcher' },
  }));
});

test('readiness waits for the expected session marker before accepting HTTP 200', async (t) => {
  let requests = 0;
  const server = await listen(t, (_request, response) => {
    response.setHeader('X-Meridian-Dev-Session', ++requests < 3 ? 'previous-launcher' : 'this-launcher');
    response.end('ready');
  });

  await waitForHttp(`http://127.0.0.1:${server.address().port}/ready`, {
    timeoutMs: 3_000,
    expectedHeader: { name: 'X-Meridian-Dev-Session', value: 'this-launcher' },
  });
  assert.ok(requests >= 3);
});

test('readiness bounds a request whose server never responds', async (t) => {
  const server = await listen(t, () => {});
  const started = Date.now();

  await assert.rejects(waitForHttp(`http://127.0.0.1:${server.address().port}/hang`, { timeoutMs: 250 }));
  assert.ok(Date.now() - started < 2_000, 'the overall timeout must also cancel an in-flight request');
});

test('readiness aborts an in-flight request promptly', async (t) => {
  const server = await listen(t, () => {});
  const controller = new AbortController();
  const pending = waitForHttp(`http://127.0.0.1:${server.address().port}/hang`, {
    timeoutMs: 10_000,
    signal: controller.signal,
  });
  const started = Date.now();
  setTimeout(() => controller.abort(), 50);

  await assert.rejects(pending);
  assert.ok(Date.now() - started < 2_000, 'shutdown must not wait for the readiness timeout');
});

test('readiness respects a signal that was already aborted', async () => {
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(waitForHttp('http://127.0.0.1:1/ready', {
    timeoutMs: 10_000,
    signal: controller.signal,
  }));
});

test('readiness preserves the shutdown reason when aborted between HTTP retries', async (t) => {
  let requested;
  const requestReceived = new Promise((resolve) => { requested = resolve; });
  const server = await listen(t, (_request, response) => {
    response.writeHead(503);
    response.end();
    requested();
  });
  const controller = new AbortController();
  const reason = new Error('Backend watch exited unexpectedly');
  const pending = waitForHttp(`http://127.0.0.1:${server.address().port}/ready`, {
    timeoutMs: 10_000,
    signal: controller.signal,
  });
  const rejected = assert.rejects(pending, (error) => error === reason);
  await requestReceived;
  await delay(30);
  controller.abort(reason);
  await rejected;
});

test('owned process shutdown removes its child tree and preserves an unrelated process', { timeout: 20_000 }, async (t) => {
  const directory = await temporaryDirectory(t);
  const sentinelFile = path.join(directory, 'sentinel.json');
  const sentinel = spawn(process.execPath, [processFixture, 'server', sentinelFile], {
    cwd: repoRoot,
    stdio: 'ignore',
  });
  t.after(async () => {
    if (sentinel.exitCode !== null || sentinel.signalCode !== null) return;
    const exited = new Promise((resolve) => sentinel.once('exit', resolve));
    sentinel.kill('SIGKILL');
    await exited;
  });
  const sentinelState = await readState(sentinelFile);

  const stateFile = path.join(directory, 'owned.json');
  const owned = startOwnedProcess({
    label: 'test tree',
    command: process.execPath,
    args: [processFixture, 'tree', stateFile],
    cwd: repoRoot,
  });
  t.after(() => owned.stop());
  const state = await readState(stateFile);
  await waitForHttp(`http://127.0.0.1:${state.descendant.port}`, { timeoutMs: 2_000 });

  await owned.stop();
  await owned.result;
  await assertStopped(state);
  assert.equal(sentinel.exitCode, null);
  assert.equal(sentinel.signalCode, null);
  assert.equal((await (await fetch(`http://127.0.0.1:${sentinelState.port}`)).json()).pid, sentinel.pid);
  if (process.platform !== 'win32') {
    assert.equal(await readFile(`${stateFile}.interrupted`, 'utf8'), 'SIGINT');
    assert.equal(await readFile(`${stateFile}.child.interrupted`, 'utf8'), 'SIGINT');
  }
  await owned.stop();
});

test('a command spawn failure is reported and remains safe to stop', { timeout: 10_000 }, async (t) => {
  const directory = await temporaryDirectory(t);
  const owned = startOwnedProcess({
    label: 'missing command',
    command: path.join(directory, 'missing-executable'),
    cwd: directory,
  });
  t.after(() => owned.stop());

  const result = await owned.result;
  assert.ok(result.error || result.code !== 0, 'a missing executable must be reported as a failure');
  await owned.stop();
});

test('an exited command retains an owned anchor for cleaning its surviving descendant', { timeout: 15_000 }, async (t) => {
  const directory = await temporaryDirectory(t);
  const stateFile = path.join(directory, 'orphan.json');
  const owned = startOwnedProcess({
    label: 'early exit',
    command: process.execPath,
    args: [processFixture, 'orphan', stateFile],
    cwd: repoRoot,
  });
  t.after(() => owned.stop());
  const state = await readState(stateFile);

  assert.equal((await owned.result).code, 23);
  assert.doesNotThrow(() => process.kill(owned.pid, 0), 'the owned anchor must survive command exit');
  await waitForHttp(`http://127.0.0.1:${state.descendant.port}`, { timeoutMs: 2_000 });
  await owned.stop();
  await assertStopped(state);
});

test('shutdown has a bounded force-kill fallback for a child tree that ignores SIGINT', { timeout: 15_000 }, async (t) => {
  const directory = await temporaryDirectory(t);
  const stateFile = path.join(directory, 'stubborn.json');
  const owned = startOwnedProcess({
    label: 'stubborn tree',
    command: process.execPath,
    args: [processFixture, 'tree', stateFile],
    cwd: repoRoot,
    env: { ...process.env, TEST_IGNORE_INTERRUPT: '1' },
  });
  t.after(() => owned.stop());
  const state = await readState(stateFile);
  const started = Date.now();

  await owned.stop();
  await assertStopped(state);
  assert.ok(Date.now() - started < 10_000, 'unresponsive owned children must not block shutdown indefinitely');
});

test('a surviving descendant receives time to flush after its command has already exited', {
  timeout: 15_000,
  skip: process.platform === 'win32' ? 'POSIX signal grace period' : false,
}, async (t) => {
  const directory = await temporaryDirectory(t);
  const stateFile = path.join(directory, 'flushing.json');
  const owned = startOwnedProcess({
    label: 'flushing descendant',
    command: process.execPath,
    args: [processFixture, 'orphan', stateFile],
    cwd: repoRoot,
    env: { ...process.env, TEST_INTERRUPT_DELAY_MS: '800' },
  });
  t.after(() => owned.stop());
  const state = await readState(stateFile);
  assert.equal((await owned.result).code, 23);

  await owned.stop();
  assert.equal(await readFile(`${stateFile}.child.flushed`, 'utf8'), 'shutdown complete');
  await assertStopped(state);
});

test('cleanup still stops its process group when the ownership supervisor is killed', {
  timeout: 15_000,
  skip: process.platform === 'win32' ? 'POSIX process groups survive their leader' : false,
}, async (t) => {
  const directory = await temporaryDirectory(t);
  const stateFile = path.join(directory, 'supervisor-killed.json');
  const owned = startOwnedProcess({
    label: 'killed supervisor',
    command: process.execPath,
    args: [processFixture, 'tree', stateFile],
    cwd: repoRoot,
  });
  t.after(() => owned.stop());
  const state = await readState(stateFile);
  process.kill(owned.pid, 'SIGKILL');
  await owned.result;

  await owned.stop();
  await assertStopped(state);
});

test('fixture-only launches just Vite and Ctrl+C releases its complete process tree', { timeout: 20_000 }, async (t) => {
  const fixture = await launchFixture(t, 'fixture-only');
  await fixture.ready();
  const state = await readState(path.join(fixture.directory, 'vite.json'));
  const events = (await readFile(path.join(fixture.directory, 'events.ndjson'), 'utf8')).trim().split('\n').map(JSON.parse);
  assert.deepEqual(events.map(({ label }) => label), ['vite']);
  assert.equal(events[0].env.VITE_MERIDIAN_DEV_MODE, 'fixture-only');

  fixture.interrupt();
  assert.deepEqual(await fixture.result, { code: 0, signal: null }, fixture.output());
  await assertStopped(state);
  assert.match(fixture.output(), /Shutdown complete/);
});

test('connected startup seeds, waits for the backend, starts Vite, and shuts both down', { timeout: 20_000 }, async (t) => {
  const fixture = await launchFixture(t, 'backend-connected');
  await fixture.ready();
  const backend = await readState(path.join(fixture.directory, 'backend.json'));
  const vite = await readState(path.join(fixture.directory, 'vite.json'));
  const events = (await readFile(path.join(fixture.directory, 'events.ndjson'), 'utf8')).trim().split('\n').map(JSON.parse);
  assert.deepEqual(events.map(({ label }) => label), ['seed', 'backend', 'vite']);
  assert.ok(events[0].args.includes('--seed-only'));
  const backendArgs = events[1].args;
  assert.ok(backendArgs.includes('watch'));
  assert.ok(backendArgs.includes('--no-hot-reload'));
  assert.ok(backendArgs.includes('--property:UseSharedCompilation=false'));
  const appDelimiter = backendArgs.indexOf('--');
  assert.equal(appDelimiter, backendArgs.lastIndexOf('--'), 'only application arguments follow a delimiter');
  assert.ok(backendArgs.indexOf('run') > 0 && backendArgs.indexOf('run') < appDelimiter,
    'run must be the watch command, not an argument passed to the backend application');
  assert.equal(backendArgs[appDelimiter + 1], '--demo');
  assert.equal(events[1].env.DOTNET_WATCH_PROCESS_CLEANUP_TIMEOUT_MS, '10000',
    'watch must allow dotnet run to forward cooperative shutdown to its host child');
  assert.equal(events[2].env.VITE_MERIDIAN_DEV_MODE, 'backend-connected');
  assert.equal(events[2].env.MERIDIAN_API_BASE_URL, `http://127.0.0.1:${fixture.backendPort}`);
  assert.ok(fixture.output().indexOf('Backend ready:') < fixture.output().indexOf('Starting vite'));

  fixture.interrupt();
  assert.deepEqual(await fixture.result, { code: 0, signal: null }, fixture.output());
  await assertStopped(backend);
  await assertStopped(vite);
});

test('a Vite startup failure stops the backend and its descendant', { timeout: 20_000 }, async (t) => {
  const fixture = await launchFixture(t, 'backend-connected', 'vite-failure');
  assert.deepEqual(await fixture.result, { code: 1, signal: null }, fixture.output());
  const backend = await readState(path.join(fixture.directory, 'backend.json'));
  await assertStopped(backend);
  await assertPortAvailable(fixture.frontendPort);
  assert.match(fixture.output(), /Vite exited unexpectedly/);
  assert.match(fixture.output(), /Shutdown complete/);
});

test('Ctrl+C while the backend is unready cancels startup and cleans its process tree', { timeout: 15_000 }, async (t) => {
  const fixture = await launchFixture(t, 'backend-connected', 'backend-unready');
  const backend = await readState(path.join(fixture.directory, 'backend.json'));
  await delay(100);
  await assert.rejects(readFile(path.join(fixture.directory, 'vite.json')), { code: 'ENOENT' });
  assert.doesNotMatch(fixture.output(), /Backend ready:|\[dev\] Ready \(/);

  fixture.interrupt();
  assert.deepEqual(await fixture.result, { code: 0, signal: null }, fixture.output());
  await assertStopped(backend);
  await assertPortAvailable(fixture.frontendPort);
});

test('backend exit after readiness stops Vite and all owned descendants', { timeout: 20_000 }, async (t) => {
  const fixture = await launchFixture(t, 'backend-connected');
  await fixture.ready();
  const backend = await readState(path.join(fixture.directory, 'backend.json'));
  const vite = await readState(path.join(fixture.directory, 'vite.json'));

  process.kill(backend.pid, 'SIGTERM');
  assert.deepEqual(await fixture.result, { code: 1, signal: null }, fixture.output());
  await assertStopped(backend);
  await assertStopped(vite);
  assert.match(fixture.output(), /Backend watch exited unexpectedly/);
});

test('a seed failure prevents backend and Vite startup', { timeout: 15_000 }, async (t) => {
  const fixture = await launchFixture(t, 'backend-connected', 'seed-failure');
  assert.deepEqual(await fixture.result, { code: 1, signal: null }, fixture.output());
  const events = (await readFile(path.join(fixture.directory, 'events.ndjson'), 'utf8')).trim().split('\n').map(JSON.parse);
  assert.deepEqual(events.map(({ label }) => label), ['seed']);
  await assertPortAvailable(fixture.frontendPort);
  await assertPortAvailable(fixture.backendPort);
  assert.match(fixture.output(), /Demo seed failed/);
});

test('startup refuses an occupied port before launching any process', { timeout: 10_000 }, async (t) => {
  const server = await listen(t, (_request, response) => response.end('existing developer server'));
  const occupiedPort = server.address().port;
  const [, backendPort] = await reservePorts(t);
  const fixture = await launchFixture(t, 'backend-connected', 'running', [occupiedPort, backendPort]);

  assert.deepEqual(await fixture.result, { code: 1, signal: null }, fixture.output());
  await assert.rejects(readFile(path.join(fixture.directory, 'events.ndjson')), { code: 'ENOENT' });
  assert.equal(await (await fetch(`http://127.0.0.1:${occupiedPort}`)).text(), 'existing developer server');
});
