# Process Lifecycle Diagnostics

Meridian hosts shut down through cooperative cancellation before any owned process can be
terminated. Installed releases use the persistent per-user lifecycle supervisor; development
launchers retain their narrower run-scoped ownership.

## Desktop Development Launcher

`scripts/dev/run-desktop.ps1` owns both processes it starts during a local desktop run: the
desktop-local host and the WPF shell. The launcher assigns the host a per-run
`MDC_SHUTDOWN_TOKEN`, posts to `POST /api/system/shutdown` with
`X-Meridian-Shutdown-Token` during cleanup, waits for host exit, and only then terminates the
owned host process if cooperative shutdown did not complete.

For `-StartupSmoke` and interrupted launches, the runner also closes the owned WPF shell process
before exiting so a failed smoke pass does not leave a hidden or orphaned `Meridian.Desktop`
instance behind.

## Installed Workstation Sessions

The installed `Meridian.LifecycleSupervisor.exe` is the single owner of the host and dedicated
PostgreSQL process. The public launcher delegates to it. The current-user named-pipe command channel
does not expose the host shutdown capability, and runtime identity JSON contains no secret.

Use the supervisor for managed session control from the install directory:

```powershell
$meridianInstall = Join-Path $env:LOCALAPPDATA "Programs\Meridian"
& (Join-Path $meridianInstall "Meridian.LifecycleSupervisor.exe") preflight
& (Join-Path $meridianInstall "Meridian.LifecycleSupervisor.exe") status
& (Join-Path $meridianInstall "Meridian.LifecycleSupervisor.exe") restart
& (Join-Path $meridianInstall "Meridian.LifecycleSupervisor.exe") stop
```

`stop` asks the host to stop accepting work, drain, flush, and persist its receipt before the
supervisor stops the dedicated database and writes the session receipt. Forced termination is a
deadline fallback and is allowed only when exact process identity still matches. External database
mode is non-owning and never permits database termination.

## Safe Process Checks

Run the lifecycle report before assuming a lingering `dotnet` process belongs to Meridian:

```powershell
pwsh ./scripts/dev/check-meridian-process-lifecycle.ps1
```

The script is report-only by default. Cleanup mode is intentionally narrow:

```powershell
pwsh ./scripts/dev/check-meridian-process-lifecycle.ps1 -CleanupOwned
```

`-CleanupOwned` remains a development-run recovery path. Installed-release recovery must use the
supervisor so host-plus-database ordering and receipt evidence are preserved. Neither path performs
broad `dotnet` cleanup.

## Manual Verification

Use these commands to confirm the shutdown result:

```powershell
Get-Process | Where-Object { $_.ProcessName -like '*Meridian*' }
Get-Process dotnet
```

Do not run broad `Stop-Process dotnet`. If a process remains, verify the command line and runtime state before stopping anything manually.

See the [Lifecycle Control Plane Reference](../reference/lifecycle-control-plane.md) for state,
manifest, endpoint, receipt, and database-ownership contracts.
