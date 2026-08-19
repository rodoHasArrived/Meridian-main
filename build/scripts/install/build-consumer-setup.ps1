[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "artifacts/consumer-setup",
    [string]$PostgreSqlPayloadRoot = $env:MDC_POSTGRES_PAYLOAD_ROOT,

    # Each runtime bundled here needs a matching PostgreSQL server payload, and PostgreSQL
    # publishes no Windows ARM64 server build. Callers therefore declare exactly what they can
    # supply rather than the script assuming both and failing at the payload check.
    [ValidateSet("win-x64", "win-arm64")]
    [string[]]$Runtimes = @("win-x64", "win-arm64"),

    [string]$SigningCertificate,
    [string]$SigningCertificatePassword
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "windows-sdk-tools.ps1")

# $ErrorActionPreference does not apply to native exit codes, so a failing `dotnet publish` used to
# continue silently and surface only as "Meridian-Setup.exe was not produced" - with the real
# compiler or packaging error nowhere in the log.
function Invoke-Checked {
    param([Parameter(Mandatory)][string]$What, [Parameter(Mandatory)][scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed with exit code $LASTEXITCODE."
    }
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$outputRoot = Join-Path $repoRoot $OutputDirectory
$payloadRoot = Join-Path $outputRoot "payload"
if (-not $outputRoot.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must resolve inside the repository."
}
Remove-Item -LiteralPath $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($PostgreSqlPayloadRoot)) {
    throw "A runtime-specific PostgreSQL payload is required. Set -PostgreSqlPayloadRoot or MDC_POSTGRES_PAYLOAD_ROOT."
}
$postgresPayloadRoot = [IO.Path]::GetFullPath($PostgreSqlPayloadRoot)

Push-Location (Join-Path $repoRoot "src/Meridian.Ui/dashboard")
try {
    Invoke-Checked "npm ci" { npm ci --prefer-offline --no-audit --no-fund }
    Invoke-Checked "npm run build" { npm run build }
} finally { Pop-Location }

Write-Host "[consumer-setup] Bundling runtimes: $($Runtimes -join ', ')"
foreach ($runtime in $Runtimes) {
    $runtimeRoot = Join-Path $payloadRoot $runtime
    $hostRoot = Join-Path $runtimeRoot "host"
    $desktopRoot = Join-Path $runtimeRoot "desktop"
    New-Item -ItemType Directory -Path $hostRoot, $desktopRoot -Force | Out-Null

    $runtimePostgresRoot = Join-Path $postgresPayloadRoot $runtime
    if (-not (Test-Path (Join-Path $runtimePostgresRoot "bin\postgres.exe")) -or
        -not (Test-Path (Join-Path $runtimePostgresRoot "bin\pg_ctl.exe")) -or
        -not (Test-Path (Join-Path $runtimePostgresRoot "bin\initdb.exe"))) {
        throw "The PostgreSQL payload for $runtime must contain bin\postgres.exe, bin\pg_ctl.exe, and bin\initdb.exe under $runtimePostgresRoot."
    }
    Copy-Item -Path $runtimePostgresRoot -Destination (Join-Path $runtimeRoot "database") -Recurse -Force

    Invoke-Checked "publish Meridian host ($runtime)" { dotnet publish (Join-Path $repoRoot "src/Meridian/Meridian.csproj") -c $Configuration -r $runtime --self-contained true -o $hostRoot }
    Invoke-Checked "publish Meridian.Wpf ($runtime)" { dotnet publish (Join-Path $repoRoot "src/Meridian.Wpf/Meridian.Wpf.csproj") -c $Configuration -r $runtime --self-contained true -o $desktopRoot /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None }
    Invoke-Checked "publish Meridian.LifecycleSupervisor ($runtime)" { dotnet publish (Join-Path $repoRoot "src/Meridian.LifecycleSupervisor/Meridian.LifecycleSupervisor.csproj") -c $Configuration -r $runtime --self-contained true -o $runtimeRoot }
    Invoke-Checked "publish Meridian.Launcher ($runtime)" { dotnet publish (Join-Path $repoRoot "src/Meridian.Launcher/Meridian.Launcher.csproj") -c $Configuration -r $runtime --self-contained true -o $runtimeRoot }

    $workstationAssets = Join-Path $repoRoot "src/Meridian.Ui/wwwroot/workstation"
    if (-not (Test-Path $workstationAssets)) { throw "Browser workstation assets were not produced at $workstationAssets" }
    Copy-Item -Path $workstationAssets -Destination (Join-Path $hostRoot "wwwroot") -Recurse -Force
}

$setupPublish = Join-Path $outputRoot "publish"
Invoke-Checked "publish Meridian.Setup" { dotnet publish (Join-Path $repoRoot "src/Meridian.Setup/Meridian.Setup.csproj") -c $Configuration -r win-x64 --self-contained true -o $setupPublish /p:MeridianPayloadDir=$payloadRoot }
$setup = Join-Path $setupPublish "Meridian-Setup.exe"
if (-not (Test-Path $setup)) {
    $produced = (Get-ChildItem -LiteralPath $setupPublish -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name) -join ', '
    throw "Meridian-Setup.exe was not produced in $setupPublish. Files present: $produced"
}
$payloadMegabytes = [math]::Round(((Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
Write-Host "[consumer-setup] Embedded payload: $payloadMegabytes MB; Meridian-Setup.exe: $([math]::Round(((Get-Item $setup).Length / 1MB), 1)) MB"

if (-not [string]::IsNullOrWhiteSpace($SigningCertificate)) {
    # signtool.exe lives in the Windows SDK and is not on PATH on the hosted Windows image.
    $signTool = Resolve-WindowsSdkTool -ToolName "signtool.exe"
    & $signTool sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /f $SigningCertificate /p $SigningCertificatePassword $setup
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed." }
}

Copy-Item $setup (Join-Path $outputRoot "Meridian-Setup.exe") -Force
Get-FileHash (Join-Path $outputRoot "Meridian-Setup.exe") -Algorithm SHA256 | Format-List
