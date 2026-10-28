# Browser Development Launcher

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-28

Run the browser workstation with one launcher on Windows, macOS, or Linux. It coordinates the
seeded development host, backend source watching, Vite readiness, and owned-process cleanup.
Run the following commands from the repository root.

## Install and choose a mode

Use Python 3.11 or newer for the environment command, Node.js 24, and npm to match the
devcontainer and browser CI. Backend-connected mode also requires the .NET SDK selected by
[global.json](../../global.json) on `PATH`; fixture-only mode does not need .NET or a database.
Windows uses the built-in Windows
PowerShell to establish process ownership before starting a service.

```sh
python build/python/cli/buildctl.py environment --profile backend-connected --restore --json-output .ai/environment.json
npm run dev
```

For fixture-only work, restore the same two npm lockfiles without requiring .NET or a database:

```sh
python build/python/cli/buildctl.py environment --profile fixture-only --restore --json-output .ai/environment.json
npm run dev:fixtures
```

Omit `--restore` for a read-only prerequisite and dependency-availability check. Restore uses
`npm ci --include=optional` in both package roots and, for backend-connected mode, restores the
host project. It stops on the first failed install. The report records tool versions, input
hashes, and restore step timings; it never captures environment variables or raw package-manager
output. A successful check establishes availability, not that an existing dependency tree matches
the lockfile. Use `--restore` after dependency changes to establish a fresh install.

The fingerprint combines dependency, launcher, and fixture input hashes with tool versions and
platform information for comparing sessions.
It does not claim bit-for-bit identical builds: `global.json` retains its SDK roll-forward policy
and runtime versions are also reported individually. Restore does not seed, reset, or delete workspace
data; the existing launcher owns seeding. Install toolchains separately when preflight reports
them missing. Python script/CI dependencies are separate from browser runtime dependencies; install
`build/scripts/ci/requirements.txt` into a virtual environment when running those checks. The
devcontainer installs that pinned dependency set automatically.

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

For named API states, use the [shared browser scenario guide](../development/browser-scenarios.md).
The accounting preview URL selector, Vitest helper, and Playwright capture consume the same typed
scenario modules, including explicit empty, delayed, forbidden, and failed responses.

## Common setup failures

Run a quick, read-only launcher diagnosis before starting services:

```sh
npm run dev:doctor
npm run dev:fixtures -- --doctor
node scripts/dev/web-dev.mjs --mode backend-connected --doctor --json
```

The doctor checks the running Node version against the dashboard's `engines.node` runtime floor
and Vite's engine range in the dashboard lockfile,
whether the installed Vite CLI can load on this OS via `--version` (including native dependencies),
the SDK selected by `global.json` in backend-connected mode, and
both loopback addresses for the selected mode's ports. Fixture-only diagnosis never probes .NET
or the backend port. It reports all blockers with recovery commands and exits nonzero when any
check fails. Command probes have ten-second deadlines; the Vite probe disables Node's compile
cache to avoid writing cache files. `--json` returns the same results as structured data and requires `--doctor`.
Use `--port` and `--backend-port` with the doctor to check an alternate configuration.

The doctor does not install packages, seed data, write configuration, start development services,
or stop existing listeners. Availability is a point-in-time check; a passing result does not prove
dependency freshness, successful builds, backend readiness, or browser functionality. Use the
`buildctl environment` command above for the broader environment report and explicit restore.

| Symptom | Check and recovery |
| --- | --- |
| SDK not found or incompatible SDK | Run `dotnet --version` from the repository root and compare with `global.json`. Install a compatible .NET 10 SDK before using backend-connected mode, or use `npm run dev:fixtures` for browser-only work. |
| Missing Vite or a platform-specific native package | Rerun `npm ci` and `npm --prefix src/Meridian.Ui/dashboard ci --include=optional` on the current OS. Keep both lockfiles; avoid sharing `node_modules` between Windows, WSL, and containers. |
| Address already in use | Stop the owning session with Ctrl+C, or choose free `--port` and `--backend-port` values. The launcher deliberately refuses to reuse another server. |
| Host fails before readiness | Inspect the labelled seed/backend output for restore, build, database, or authentication failures. Existing environment settings override the demo defaults; check the [environment reference](../reference/environment-variables.md) and [dev container database setup](../start/README.md#dev-container). |
| Cold restore/build exceeds the deadline | Increase `--startup-timeout`, for example `npm run dev -- --startup-timeout 900000`, after checking the reported build errors. |
| Backend file edits do not trigger a restart | Enable the polling watcher described below before launching on mounted filesystems or hosts with limited file watches. |

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
| `--doctor` | Off | Check selected-mode launcher prerequisites and exit without starting services. |
| `--json` | Off | Emit structured diagnostic results; requires `--doctor`. |

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
node --test scripts/dev/web-dev.test.mjs scripts/dev/web-doctor.test.mjs
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
