\
<#
STOP_COLLECTOR.ps1
Stops Collector/UI using pid files in .\run\
#>
$ErrorActionPreference = "SilentlyContinue"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Run  = Join-Path $Root "run"
$CollectorPidFile = Join-Path $Run "collector.pid"
$UiPidFile        = Join-Path $Run "ui.pid"

function Stop-ByPidFile($PidFile, $Name) {
  if (Test-Path $PidFile) {
    $pid = (Get-Content $PidFile | Select-Object -First 1)
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

Stop-ByPidFile $UiPidFile "UI"
Stop-ByPidFile $CollectorPidFile "Collector"
Remove-Item $UiPidFile,$CollectorPidFile -ErrorAction SilentlyContinue
Write-Host "[INFO] Done."
