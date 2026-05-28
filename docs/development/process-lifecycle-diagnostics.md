# Process Lifecycle Diagnostics

Meridian hosts should shut down through cooperative cancellation before any owned process is terminated. This applies to the CLI host, WPF desktop shell, and installed Web Workstation host.

## Desktop Development Launcher

`scripts/dev/run-desktop.ps1` owns both processes it starts during a local desktop run: the
desktop-local host and the WPF shell. The launcher assigns the host a per-run
`MDC_SHUTDOWN_TOKEN`, posts to `POST /api/system/shutdown` with
`X-Meridian-Shutdown-Token` during cleanup, waits for host exit, and only then terminates the
owned host process if cooperative shutdown did not complete.

For `-StartupSmoke` and interrupted launches, the runner also closes the owned WPF shell process
before exiting so a failed smoke pass does not leave a hidden or orphaned `Meridian.Desktop`
instance behind.

## Web Workstation Sessions

The installed Web Workstation launcher tracks the host it starts in `%LOCALAPPDATA%\Meridian\service\web-workstation-runtime.json`. The state file records the PID, port, executable path, config path, start time, and local shutdown token.

Use the launcher for managed session control:

```powershell
& "$env:LOCALAPPDATA\Programs\Meridian Web Workstation\Launch-MeridianWebWorkstation.ps1"
& "$env:LOCALAPPDATA\Programs\Meridian Web Workstation\Launch-MeridianWebWorkstation.ps1" -Status
& "$env:LOCALAPPDATA\Programs\Meridian Web Workstation\Launch-MeridianWebWorkstation.ps1" -Stop
```

`-Stop` posts to `POST /api/system/shutdown` with the local shutdown token, waits for process exit, and only terminates the tracked process if the stored metadata still matches the running Meridian process.

## Safe Process Checks

Run the lifecycle report before assuming a lingering `dotnet` process belongs to Meridian:

```powershell
pwsh ./scripts/dev/check-meridian-process-lifecycle.ps1
```

The script is report-only by default. Cleanup mode is intentionally narrow:

```powershell
pwsh ./scripts/dev/check-meridian-process-lifecycle.ps1 -CleanupOwned
```

`-CleanupOwned` uses the stored shutdown token first and only terminates a process when the PID, start time, executable, and Meridian process name still match the runtime state. It never performs broad `dotnet` cleanup.

## Manual Verification

Use these commands to confirm the shutdown result:

```powershell
Get-Process | Where-Object { $_.ProcessName -like '*Meridian*' }
Get-Process dotnet
```

Do not run broad `Stop-Process dotnet`. If a process remains, verify the command line and runtime state before stopping anything manually.
