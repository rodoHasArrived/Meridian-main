\
<#
START_COLLECTOR.ps1
Meridian – Canonical Startup (Windows PowerShell)

Features:
  - Build (optional) with/without IBAPI
  - Start Collector
  - Write PID files into .\run\
  - Ctrl+C handling + graceful stop

Usage:
  powershell -ExecutionPolicy Bypass -File .\START_COLLECTOR.ps1

Env vars (optional):
  $env:USE_IBAPI = "true"|"false"
  $env:BUILD     = "true"|"false"
  $env:DOTNET_CONFIGURATION = "Release"|"Debug"
  $env:IB_HOST, IB_PORT, IB_CLIENT_ID
#>

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Src  = Join-Path $Root "src\Meridian"
$Data = Join-Path $Root "data"
$Logs = Join-Path $Root "logs"
$Run  = Join-Path $Root "run"

$Config = $env:DOTNET_CONFIGURATION; if (-not $Config) { $Config = "Release" }
$UseIbApi = $env:USE_IBAPI; if (-not $UseIbApi) { $UseIbApi = "false" }
$DoBuild = $env:BUILD; if (-not $DoBuild) { $DoBuild = "true" }

$IbHost = $env:IB_HOST; if (-not $IbHost) { $IbHost = "127.0.0.1" }
$IbPort = $env:IB_PORT; if (-not $IbPort) { $IbPort = "" }
$IbClientId = $env:IB_CLIENT_ID; if (-not $IbClientId) { $IbClientId = "17" }

New-Item -ItemType Directory -Force -Path $Data,$Logs,$Run | Out-Null

$CollectorPidFile = Join-Path $Run "collector.pid"


function Test-TcpPort($HostName, [int]$Port, [int]$TimeoutMs = 800) {
  try {
    $client = New-Object System.Net.Sockets.TcpClient
    $iar = $client.BeginConnect($HostName, $Port, $null, $null)
    $wait = $iar.AsyncWaitHandle.WaitOne($TimeoutMs, $false)
    if (!$wait) { $client.Close(); return $false }
    $client.EndConnect($iar) | Out-Null
    $client.Close()
    return $true
  } catch { return $false }
}

function Find-IBPort($HostName) {
  if ($env:IB_PORT) { return [int]$env:IB_PORT }
  $ports = @(7497,4002,7496,4001)
  foreach ($p in $ports) {
    if (Test-TcpPort $HostName $p) { return $p }
  }
  return $null
}

function Preflight {
  Write-Host "-----------------------------------------------"
  Write-Host "[PREFLIGHT] Running checks..."

  # Disk space (>=2GB recommended)
  try {
    $drive = (Get-Item $Root).PSDrive.Name
    $free = (Get-PSDrive $drive).Free
    $freeMb = [math]::Round($free / 1MB)
    if ($freeMb -lt 2048) {
      Write-Host "[WARN] Low disk space: ${freeMb}MB free (recommend >= 2048MB)"
    } else {
      Write-Host "[OK] Disk space: ${freeMb}MB free"
    }
  } catch {
    Write-Host "[WARN] Could not determine disk space"
  }

  # Permissions
  foreach ($d in @($Data,$Logs,$Run)) {
    try {
      $t = Join-Path $d ".__writetest"
      "x" | Out-File -FilePath $t -Force
      Remove-Item $t -Force
      Write-Host "[OK] Writable: $d"
    } catch {
      Write-Host "[ERROR] Not writable: $d"
      throw
    }
  }

  # Config sanity
  $cfgPath = Join-Path $Root "appsettings.json"
  if (Test-Path $cfgPath) {
    try {
      $j = Get-Content $cfgPath -Raw | ConvertFrom-Json
      $syms = @()
      if ($j.Symbols) { $syms = $j.Symbols }
      $depth = ($syms | Where-Object { $_.SubscribeDepth -eq $true }).Count
      $trades = ($syms | Where-Object { $_.SubscribeTrades -eq $true }).Count
      Write-Host ("[OK] Config symbols={0} depth_enabled={1} trades_enabled={2}" -f $syms.Count,$depth,$trades)
      if ($depth -gt 0) { Write-Host "[NOTE] L2 depth requires provider depth entitlements for venues." }
    } catch {
      Write-Host "[WARN] Config parse failed: $($_.Exception.Message)"
    }
  } else {
    Write-Host "[WARN] appsettings.json not found."
  }

  # IB reachability
  if ($UseIbApi -eq "true") {
    $p = Find-IBPort $IbHost
    if (-not $p) { throw "[ERROR] Could not autodetect IB port on $IbHost (tried 7497/4002/7496/4001)." }
    $IbPort = "$p"
    $env:IB_PORT = "$p"
    Write-Host "[OK] Detected IB port: $p"
    if (!(Test-TcpPort $IbHost $p)) { throw "[ERROR] IB not reachable at ${IbHost}:$p" }
    Write-Host "[OK] IB reachable at ${IbHost}:$p"
  } else {
    Write-Host "[OK] IBAPI disabled; skipping IB connectivity check."
  }

  Write-Host "[PREFLIGHT] PASSED."
  Write-Host "-----------------------------------------------"
}


function Stop-ByPidFile($PidFile, $Name) {
  if (Test-Path $PidFile) {
    $pid = (Get-Content $PidFile -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($pid) {
      try {
        $p = Get-Process -Id $pid -ErrorAction Stop
        Write-Host "[INFO] Stopping $Name ($pid)"
        $p.CloseMainWindow() | Out-Null
        Start-Sleep -Seconds 2
        if (!$p.HasExited) { Stop-Process -Id $pid -Force }
      } catch { }
    }
  }
}

$global:Stopping = $false
$handler = {
  if ($global:Stopping) { return }
  $global:Stopping = $true
  Write-Host "`n[INFO] Ctrl+C received. Shutting down..."
  Stop-ByPidFile $CollectorPidFile "Collector"
  Remove-Item $CollectorPidFile -ErrorAction SilentlyContinue
  exit 0
}

# Ctrl+C handler
$null = Register-EngineEvent PowerShell.Exiting -Action $handler

Set-Location $Root

Preflight

if ($DoBuild -eq "true") {
  Write-Host "[INFO] Building..."
  if ($UseIbApi -eq "true") {
    dotnet build -p:DefineConstants=IBAPI -c $Config *> (Join-Path $Logs "build.log")
  } else {
    dotnet build -c $Config *> (Join-Path $Logs "build.log")
  }
}

Write-Host "[INFO] Starting Collector..."
if ($UseIbApi -eq "true") {
  $env:IB_HOST = $IbHost
  $env:IB_PORT = $IbPort
  $env:IB_CLIENT_ID = $IbClientId
}

$collectorArgs = @(
  "run",
  "--project", (Join-Path $Src "Meridian.csproj"),
  "--configuration", $Config,
  "--",
  "--watch-config",
  "--http-port", "8080"
)

$collector = Start-Process -FilePath "dotnet" -ArgumentList $collectorArgs -PassThru -NoNewWindow `
  -RedirectStandardOutput (Join-Path $Logs "collector.log") -RedirectStandardError (Join-Path $Logs "collector.err.log")
$collector.Id | Out-File -FilePath $CollectorPidFile -Encoding ascii -Force
Write-Host "[INFO] Collector PID: $($collector.Id)"

Write-Host "-----------------------------------------------"
Write-Host "[INFO] Running."
Write-Host "[INFO] Status: $Data\_status\status.json"
Write-Host "[INFO] Logs: $Logs"
Write-Host "[INFO] Stop: Ctrl+C or run STOP_COLLECTOR.ps1"
Write-Host "==============================================="

# Wait on collector
Wait-Process -Id $collector.Id
& $handler
