import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

const [mode, stateFile, requestedPort = '0'] = process.argv.slice(2);
const server = createServer((_request, response) => {
  response.writeHead(Number(process.env.TEST_HTTP_STATUS ?? 200), {
    'Content-Type': 'application/json',
    ...(process.env.MERIDIAN_DEV_SESSION ? { 'X-Meridian-Dev-Session': process.env.MERIDIAN_DEV_SESSION } : {}),
  });
  response.end(JSON.stringify({ pid: process.pid }));
});

let descendant;
let descendantState;
if (mode === 'tree' || mode === 'orphan') {
  descendant = spawn(process.execPath, [fileURLToPath(import.meta.url), 'server', `${stateFile}.child`], {
    stdio: ['ignore', 'inherit', 'inherit', 'ipc'],
  });
  descendantState = await new Promise((resolve, reject) => {
    descendant.once('message', resolve);
    descendant.once('error', reject);
    descendant.once('exit', (code) => reject(new Error(`Descendant exited early: ${code}`)));
  });
}

server.listen(Number(requestedPort), '127.0.0.1', async () => {
  const state = {
    pid: process.pid,
    port: server.address().port,
    ...(descendantState ? { descendant: descendantState } : {}),
  };
  await writeFile(stateFile, JSON.stringify(state));
  process.send?.(state);
  process.disconnect?.();
  if (mode === 'orphan') setTimeout(() => process.exit(23), 50);
});

process.on('SIGINT', async () => {
  await writeFile(`${stateFile}.interrupted`, 'SIGINT');
  if (process.env.TEST_IGNORE_INTERRUPT === '1') return;
  if (process.env.TEST_INTERRUPT_DELAY_MS) await delay(Number(process.env.TEST_INTERRUPT_DELAY_MS));
  server.close();
  server.closeAllConnections();
  // The group owner must signal the descendant; this fixture never forwards signals.
  // Waiting for its exit also lets the parent reap it on hosts without an init reaper.
  if (descendant && descendant.exitCode === null && descendant.signalCode === null) {
    await new Promise((resolve) => descendant.once('exit', resolve));
  }
  await writeFile(`${stateFile}.flushed`, 'shutdown complete');
  process.exit(0);
});
