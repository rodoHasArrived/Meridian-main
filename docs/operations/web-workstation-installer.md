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

The installed shortcut starts `Meridian.exe --mode desktop --http-port 8080 --config
%LOCALAPPDATA%\Meridian\appsettings.json`, waits for `http://localhost:8080/healthz`, and opens
`http://localhost:8080/workstation/`.

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
- workstation URL: `http://localhost:8080/workstation/`

The host serves static workstation files from its working directory at `wwwroot/workstation`. The
installer therefore copies `src/Meridian.Ui/wwwroot/workstation` into the install root after the
dashboard build completes.

## Troubleshooting

- If `npm run build` fails with `ENOTEMPTY` or `EPERM` under `src/Meridian.Ui/wwwroot/workstation`,
  stop stale Vite preview processes and rerun the installer.
- If restore or publish fails because `C:` is low on space, clear generated build output and NuGet
  caches before retrying.
- Use `-PlanOnly` first when changing install paths or ports.
