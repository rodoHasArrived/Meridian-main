import { appendFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseOptions, runDev } from '../../../../scripts/dev/web-dev.mjs';
import { startOwnedProcess } from '../../../../scripts/dev/owned-process.mjs';

const [repoRoot, mode, frontendPort, backendPort, scenario = 'running'] = process.argv.slice(2);
const processFixture = fileURLToPath(new URL('./process-tree.mjs', import.meta.url));
process.on('message', (message) => {
  if (message.type === 'interrupt') process.emit('SIGINT');
});

try {
  await runDev(parseOptions([
    '--mode', mode,
    '--port', frontendPort,
    '--backend-port', backendPort,
    '--data-dir', path.join(repoRoot, 'data'),
    '--startup-timeout', '5000',
  ]), {
    repoRoot,
    spawnProcess({ label, command, args, env }) {
      const event = { label, command, args, env: {
        VITE_MERIDIAN_DEV_MODE: env.VITE_MERIDIAN_DEV_MODE,
        MERIDIAN_API_BASE_URL: env.MERIDIAN_API_BASE_URL,
        DOTNET_WATCH_PROCESS_CLEANUP_TIMEOUT_MS: env.DOTNET_WATCH_PROCESS_CLEANUP_TIMEOUT_MS,
      } };
      appendFileSync(path.join(repoRoot, 'events.ndjson'), `${JSON.stringify(event)}\n`);
      const failure = (scenario === 'vite-failure' && label === 'vite') ||
        (scenario === 'seed-failure' && label === 'seed');
      return startOwnedProcess({
        label,
        command: process.execPath,
        args: failure ? ['-e', 'process.exit(7)'] : label === 'seed' ? ['-e', 'process.exit(0)'] : [
          processFixture, 'tree', path.join(repoRoot, `${label}.json`),
          label === 'backend' ? backendPort : frontendPort,
        ],
        cwd: repoRoot,
        env: scenario === 'backend-unready' && label === 'backend' ? { ...env, TEST_HTTP_STATUS: '503' } : env,
      });
    },
  });
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  process.disconnect?.();
}
