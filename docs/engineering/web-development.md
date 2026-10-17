# Browser Development Launcher

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

Run the browser workstation with one launcher on Windows, macOS, or Linux. It coordinates the
seeded development host, backend source watching, Vite readiness, and owned-process cleanup.
Run the following commands from the repository root.

## Install and choose a mode

Use Node.js 22.12 or newer and npm. Backend-connected mode also requires the .NET 10 SDK on
`PATH`; fixture-only mode does not need .NET or a database. Windows uses the built-in Windows
PowerShell to establish process ownership before starting a service.

```sh
npm ci
npm --prefix src/Meridian.Ui/dashboard ci
npm run dev
```

Open the URL printed after `[dev] Ready`, normally `http://127.0.0.1:5173/workstation/`.
The launcher does not open a browser automatically.

| Mode | Command | API behavior |
| --- | --- | --- |
| Backend-connected | `npm run dev` or `npm run dev:backend` | Seeds once, starts `dotnet watch`, waits for the host, then starts Vite. API requests reach the seeded host; backend failures remain errors and never become fixture responses. |
| Fixture-only | `npm run dev:fixtures` | Starts only Vite. Supported API requests return labelled fixtures without contacting a backend. Unsupported requests return an explicit error. |

Both modes retain Vite browser hot reload. Backend-connected mode uses `dotnet watch
--no-hot-reload`: a backend source change rebuilds and restarts the host. The launcher gives
watch ten seconds to clean up each run (`DOTNET_WATCH_PROCESS_CLEANUP_TIMEOUT_MS`); .NET 10's
zero-timeout default can kill the `dotnet run` wrapper before it stops its host child. During a restart, API
requests may fail until the host is ready again. The launcher sets matching server/browser
development modes and suppresses .NET's separate browser refresh mechanism.

The package-local `npm --prefix src/Meridian.Ui/dashboard run dev` remains a standalone Vite
command with its legacy automatic fallback when no mode is configured. Use the root launcher
commands above when you need an explicit fixture-only or backend-connected session.

## Options and readiness

Pass options after npm's `--`, or invoke the script with an explicit mode:

```sh
npm run dev -- --port 5174 --backend-port 8081 --data-dir artifacts/dev-web-second
node scripts/dev/web-dev.mjs --mode fixture-only --port 5174
node scripts/dev/web-dev.mjs --help
```

| Flag | Default | Purpose |
| --- | --- | --- |
| `--mode` | Set by the npm script | `fixture-only` or `backend-connected`; required for direct script invocation. |
| `--port` | `5173` | Vite loopback port. |
| `--backend-port` | `8080` | Seeded host loopback port; must differ from Vite in backend-connected mode. |
| `--data-dir` | `artifacts/dev-web` | Retained demo parent directory, resolved relative to the repository root. |
| `--startup-timeout` | `600000` | Milliseconds allowed for each build/seed or HTTP readiness stage, including a cold restore/build. |

The launcher checks both IPv4 (`127.0.0.1`) and IPv6 (`::1`, when available) for occupied ports.
It fails with the address and port instead of reusing a server, killing an existing listener, or
silently choosing another port. Vite also receives `--strictPort`. Backend-connected mode checks
both ports again after seeding, waits for HTTP 200 from the host's `/readyz` with the current
launcher's `x-meridian-dev-session` marker, then waits for HTTP 200 and the same marker from
Vite's `/workstation/` before announcing readiness. Demo readiness can be Degraded when
in-memory governance is intentional; `/healthz` alone is only a liveness check.

Failed startup, readiness timeout, or an unexpected service exit stops the processes started by
that launcher. Increase `--startup-timeout` for a slow initial restore/build; inspect the labelled
seed/backend output for SDK, restore, configuration, or compilation errors.

On mounted filesystems or hosts with a low inotify quota, enable .NET's polling watcher before
launching: `export DOTNET_USE_POLLING_FILE_WATCHER=1` in bash/zsh, or
`$env:DOTNET_USE_POLLING_FILE_WATCHER = "1"` in PowerShell.

## Seeded data and shutdown

The default session writes its own `artifacts/dev-web/appsettings.dev.json` and seeds
`artifacts/dev-web/data/demo-workspace`. It does not use the normal workstation data root.
Seeding is idempotent and runs once per launcher invocation. Watch restarts use `--demo`, so
they reopen the workspace without reseeding. Restarting the launcher reseeds its deterministic
sample records and market history. Use separate `--data-dir` values for concurrent sessions.

File-backed demo casework, strategy runs, market history, fund account, portfolio snapshots,
journal drafts, and report packs survive shutdown. Posted money-path records in the default
in-memory governance profile do not survive a backend restart. The demo defaults to Development,
optional authentication, and a local anonymous Admin role when those settings are unset, and
binds only to loopback. Existing database, environment, and authentication settings are respected;
they can change startup requirements. See [the seeded demo guide](../start/README.md#see-it-working-one-command-demo).

Press Ctrl+C in the launcher terminal to stop the session. On macOS/Linux it sends SIGINT to its
owned process groups, allows five seconds for the complete group to drain, then kills remaining
members. On Windows each service starts inside an owned Job Object. Closing its handle stops all
members, including descendants whose immediate parent has exited. This is forced termination;
graceful host draining is only supported by the POSIX shutdown path. If Windows cannot establish
job ownership, startup fails before the service runs. The launcher never
selects processes by executable name or kills a process because it occupies a port. Seeded data
is retained after shutdown.

## Validation and manual acceptance

Run launcher tests and focused development-mode tests from the repository root:

```sh
node --test scripts/dev/web-dev.test.mjs
npm --prefix src/Meridian.Ui/dashboard run test:vitest -- src/vite-config.test.ts src/lib/api.development-modes.test.ts
```

The launcher tests exercise readiness deadlines, port collisions, and process ownership. They
do not replace a rendered browser check or a real .NET watch restart. For an acceptance run:

1. Start `npm run dev:fixtures`, wait for `[dev] Ready`, and open the printed workstation URL.
   Confirm sample provenance. In browser DevTools set `window.__meridianHmrCheck = 1`, then
   temporarily append `body { outline: 4px solid magenta; outline-offset: -4px; }` to
   `src/Meridian.Ui/dashboard/src/styles/index.css`. Save and confirm the outline appears
   without reloading the page and the console marker remains `1`. Remove only that temporary
   rule and confirm the outline disappears.
2. While this session runs, start a second `npm run dev:fixtures` in another terminal. Confirm
   an occupied-port error and that the original browser session still works. Stop the first
   session with Ctrl+C and wait for `[dev] Shutdown complete.`
3. Start `npm run dev`, wait for readiness, and confirm `/readyz` on the printed backend URL
   returns 200 and workstation API responses have no `x-meridian-dev-fixture` header. In
   `src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs`, temporarily change only the `/healthz`
   response string from `healthy` to `healthy-dev-check`. Save, observe the watch restart, wait
   for `/readyz` to return 200, and check `/healthz` for the new string. Revert only that string
   and confirm a second restart returns `healthy`. The demo data directory should persist and
   watch output should not run the seed command again.
4. Press Ctrl+C during the connected session. Confirm shutdown completes and both development
   URLs stop responding. Run the same command again to prove the ports are available, then
   stop it again. Any unrelated server left running for the check must remain reachable.

Record the OS, commands, browser observations, restart result, and shutdown result with the task
evidence. Repeat on each platform you claim to have verified; Windows termination behavior must
be checked independently from POSIX signal handling. Restore temporary source edits before
committing. These steps describe acceptance checks, not a record of completed verification.
