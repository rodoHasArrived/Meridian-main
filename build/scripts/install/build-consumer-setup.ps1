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
Invoke-Checked "publish Meridian.Setup" { dotnet publish (Join-Path $repoRoot "src/Meridian.Setup/Meridian.Setup.csproj") -c $Configuration -r win-x64 --self-contained true -o $setupPublish }
$setup = Join-Path $setupPublish "Meridian-Setup.exe"
if (-not (Test-Path $setup)) {
    $produced = (Get-ChildItem -LiteralPath $setupPublish -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name) -join ', '
    throw "Meridian-Setup.exe was not produced in $setupPublish. Files present: $produced"
}

# Append the product payload to the finished executable.
#
# It is deliberately not an EmbeddedResource: Roslyn serialises resources into the PE image's
# mapped field data and overflows on a payload this size, so the compile failed outright with
# ArgumentOutOfRangeException (mappedFieldDataStreamRva). Appending runs BEFORE signing so the
# payload falls inside the Authenticode hash, which covers everything but the checksum field and
# the certificate table.
#
# Layout, read back by src/Meridian.Setup/PayloadPackage.cs:
#   [ executable image ][ ZIP archive ][ 138-byte ASCII trailer ]
# The trailer is `MDNSETUP1\n`, then zero-padded 20-digit `offset=` and `length=` lines, then a
# `sha256=` line of 64 lowercase hex digits, each line terminated by a single LF.
$payloadMegabytes = [math]::Round(((Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
$archivePath = Join-Path $outputRoot "payload.zip"
[IO.Compression.ZipFile]::CreateFromDirectory(
    $payloadRoot,
    $archivePath,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

$archiveLength = (Get-Item -LiteralPath $archivePath).Length
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$archiveOffset = (Get-Item -LiteralPath $setup).Length

$target = [IO.File]::Open($setup, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $source = [IO.File]::OpenRead($archivePath)
    try { $source.CopyTo($target) } finally { $source.Dispose() }

    $trailer = "MDNSETUP1`noffset={0:D20}`nlength={1:D20}`nsha256={2}`n" -f `
        $archiveOffset, $archiveLength, $archiveHash
    $trailerBytes = [Text.Encoding]::ASCII.GetBytes($trailer)
    if ($trailerBytes.Length -ne 138) {
        throw "The payload trailer must be exactly 138 bytes; built $($trailerBytes.Length)."
    }
    $target.Write($trailerBytes, 0, $trailerBytes.Length)
} finally { $target.Dispose() }

# Neither the staging tree nor the intermediate archive belongs in the artifact, and the SBOM and
# SHA256SUMS steps downstream enumerate this directory.
Remove-Item -LiteralPath $archivePath -Force
Remove-Item -LiteralPath $payloadRoot -Recurse -Force

Write-Host "[consumer-setup] Appended payload: $payloadMegabytes MB uncompressed, $([math]::Round(($archiveLength / 1MB), 1)) MB compressed; Meridian-Setup.exe: $([math]::Round(((Get-Item $setup).Length / 1MB), 1)) MB"

# Prove the packaged executable can find and read the payload that was just appended to it, using
# the same reader a user's machine will use. This is what keeps the writer above and
# PayloadPackage from drifting apart.
$verifyLog = Join-Path $outputRoot "verify-payload.log"
$verify = Start-Process -FilePath $setup -ArgumentList (@("--verify-payload") + $Runtimes) `
    -Wait -PassThru -NoNewWindow -RedirectStandardError $verifyLog
if (Test-Path -LiteralPath $verifyLog) {
    Get-Content -LiteralPath $verifyLog | ForEach-Object { Write-Host "[consumer-setup] $_" }
    Remove-Item -LiteralPath $verifyLog -Force
}
if ($verify.ExitCode -ne 0) {
    throw "The packaged Meridian-Setup.exe could not read its own payload (exit code $($verify.ExitCode))."
}

if (-not [string]::IsNullOrWhiteSpace($SigningCertificate)) {
    # signtool.exe lives in the Windows SDK and is not on PATH on the hosted Windows image.
    $signTool = Resolve-WindowsSdkTool -ToolName "signtool.exe"
    & $signTool sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /f $SigningCertificate /p $SigningCertificatePassword $setup
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed." }
}

Copy-Item $setup (Join-Path $outputRoot "Meridian-Setup.exe") -Force
Get-FileHash (Join-Path $outputRoot "Meridian-Setup.exe") -Algorithm SHA256 | Format-List
