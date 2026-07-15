[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "artifacts/consumer-setup",
    [string]$SigningCertificate,
    [string]$SigningCertificatePassword
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$outputRoot = Join-Path $repoRoot $OutputDirectory
$payloadRoot = Join-Path $outputRoot "payload"
if (-not $outputRoot.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must resolve inside the repository."
}
Remove-Item -LiteralPath $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null

Push-Location (Join-Path $repoRoot "src/Meridian.Ui/dashboard")
try {
    npm ci --prefer-offline --no-audit --no-fund
    npm run build
} finally { Pop-Location }

foreach ($runtime in @("win-x64", "win-arm64")) {
    $runtimeRoot = Join-Path $payloadRoot $runtime
    $hostRoot = Join-Path $runtimeRoot "host"
    $desktopRoot = Join-Path $runtimeRoot "desktop"
    New-Item -ItemType Directory -Path $hostRoot, $desktopRoot -Force | Out-Null

    dotnet publish (Join-Path $repoRoot "src/Meridian/Meridian.csproj") -c $Configuration -r $runtime --self-contained true -o $hostRoot
    dotnet publish (Join-Path $repoRoot "src/Meridian.Wpf/Meridian.Wpf.csproj") -c $Configuration -r $runtime --self-contained true -o $desktopRoot /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None
    dotnet publish (Join-Path $repoRoot "src/Meridian.Launcher/Meridian.Launcher.csproj") -c $Configuration -r $runtime --self-contained true -o $runtimeRoot

    $workstationAssets = Join-Path $repoRoot "src/Meridian.Ui/wwwroot/workstation"
    if (-not (Test-Path $workstationAssets)) { throw "Browser workstation assets were not produced at $workstationAssets" }
    Copy-Item -Path $workstationAssets -Destination (Join-Path $hostRoot "wwwroot") -Recurse -Force
}

$setupPublish = Join-Path $outputRoot "publish"
dotnet publish (Join-Path $repoRoot "src/Meridian.Setup/Meridian.Setup.csproj") -c $Configuration -r win-x64 --self-contained true -o $setupPublish /p:MeridianPayloadDir=$payloadRoot
$setup = Join-Path $setupPublish "Meridian-Setup.exe"
if (-not (Test-Path $setup)) { throw "Meridian-Setup.exe was not produced." }

if (-not [string]::IsNullOrWhiteSpace($SigningCertificate)) {
    $signTool = (Get-Command signtool.exe -ErrorAction Stop).Source
    & $signTool sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /f $SigningCertificate /p $SigningCertificatePassword $setup
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed." }
}

Copy-Item $setup (Join-Path $outputRoot "Meridian-Setup.exe") -Force
Get-FileHash (Join-Path $outputRoot "Meridian-Setup.exe") -Algorithm SHA256 | Format-List
