# Web Workstation Installer

Use `build/scripts/install/install-web-workstation.ps1` to install the browser workstation as a
local Windows application without using the Vite development server.

The installer builds the React workstation, publishes the Meridian local host, copies the built
bundle to the installed host content root, creates runtime directories, writes a first-run config
when needed, and creates Desktop and Start Menu shortcuts named `Meridian Web Workstation`.

```powershell
.\build\scripts\install\install-web-workstation.ps1
```

The root Windows installer exposes the same workflow through `-Mode WebWorkstation`:

```powershell
.\build\scripts\install\install.ps1 -Mode WebWorkstation
```

The installed shortcut starts `Meridian.exe --mode workstation --http-port 8080 --config
%LOCALAPPDATA%\Meridian\appsettings.json`, waits for `http://localhost:8080/healthz`, and opens
`http://localhost:8080/workstation/`.

`workstation` mode starts only the local UI/API host. It does not auto-connect providers, start
market-data subscriptions, or run the collector pipeline; use `--mode desktop` when the retained
desktop-local host must run the UI server and collector side by side.

Each installer run is upgrade-aware:

- backs up the current `%LOCALAPPDATA%\Meridian` state to
  `%LOCALAPPDATA%\Meridian\backups\install-<timestamp>\`
- preserves the active config and resolved data root instead of recreating them
- removes stale Desktop or Start Menu shortcuts that still point at missing older installs
- skips file replacement when the installed host and workstation bundle already match the latest
  published output, which avoids unnecessary downtime under low disk pressure
- writes an install manifest to
  `%LOCALAPPDATA%\Meridian\service\web-workstation-install-manifest.json`
  so future upgrades can inspect the last install source, backup path, and cleanup actions

## Options

```powershell
# Show the install plan without building or copying files.
.\build\scripts\install\install-web-workstation.ps1 -PlanOnly
.\build\scripts\install\install.ps1 -Mode WebWorkstation -PlanOnly

# Reuse an already built dashboard bundle.
.\build\scripts\install\install-web-workstation.ps1 -SkipDashboardBuild
.\build\scripts\install\install.ps1 -Mode WebWorkstation -SkipDashboardBuild

# Install without a Start Menu shortcut.
.\build\scripts\install\install-web-workstation.ps1 -NoStartMenuShortcut
.\build\scripts\install\install.ps1 -Mode WebWorkstation -NoStartMenuShortcut

# Install ARM64 host binaries.
.\build\scripts\install\install-web-workstation.ps1 -RuntimeIdentifier win-arm64
.\build\scripts\install\install.ps1 -Mode WebWorkstation -WebRuntimeIdentifier win-arm64
```

Defaults:

- install root: `%LOCALAPPDATA%\Programs\Meridian Web Workstation`
- app data root: `%LOCALAPPDATA%\Meridian`
- config path: `%LOCALAPPDATA%\Meridian\appsettings.json`
- backup root: `%LOCALAPPDATA%\Meridian\backups`
- install manifest: `%LOCALAPPDATA%\Meridian\service\web-workstation-install-manifest.json`
- first-run data source: `Synthetic`
- workstation URL: `http://localhost:8080/workstation/`

The host serves static workstation files from its working directory at `wwwroot/workstation`. The
installer therefore copies `src/Meridian.Ui/wwwroot/workstation` into the install root after the
dashboard build completes. Persistent runtime state stays under `%LOCALAPPDATA%\Meridian`; the
installer removes empty legacy `data/` and `artifacts/` placeholders from the install root so the
installed app directory remains binary-focused.

## Smoke Test

Use the install smoke when changing the installer, launcher, host publish, or workstation asset
staging path:

```powershell
.\build\scripts\install\smoke-web-workstation-install.ps1
```

The smoke runs `build/scripts/install/install.ps1 -Mode WebWorkstation` into an isolated artifact
directory, starts the installed `Meridian.exe` from that installed copy, verifies
`http://localhost:<port>/healthz`, verifies `http://localhost:<port>/workstation/`, and writes logs
under `artifacts/install-smoke/web-workstation/<timestamp>/`. It removes the isolated installed
copy after a passing run unless `-KeepInstalledCopy` is specified. Use `-SkipDashboardBuild` only
when `src/Meridian.Ui/wwwroot/workstation` already contains the bundle you intend to validate.

## Troubleshooting

- If `npm run build` fails with `ENOTEMPTY` or `EPERM` under `src/Meridian.Ui/wwwroot/workstation`,
  stop stale Vite preview processes and rerun the installer.
- If restore or publish fails because `C:` is low on space, clear generated build output and NuGet
  caches before retrying.
- If the installer reports stale shortcuts, inspect the Desktop or Start Menu entries that pointed
  at missing side-by-side installs before recreating manual launchers.
- Use `-PlanOnly` first when changing install paths or ports.
