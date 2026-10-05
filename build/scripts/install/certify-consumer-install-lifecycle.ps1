#Requires -Version 7.0
<#
.SYNOPSIS
Certifies the production-signed consumer EXE on a clean native Windows x64 runner.
.DESCRIPTION
Runs the downloaded EXE and the installed repair/uninstall entrypoints. No build,
SDK, external database, MSIX installation, or certificate-store mutation supplies
lifecycle evidence. A failed receipt is retained even when a probe throws.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CurrentPackage,
    [string]$PriorPackage = "",
    [switch]$FirstRelease,
    [string]$PriorReleaseTag = "",
    [Parameter(Mandatory)][string]$PredecessorEvidencePath,
    [Parameter(Mandatory)][string]$ReceiptPath,
    [string]$ExpectedPublisher = "",
    [ValidateRange(30, 600)][int]$TimeoutSeconds = 240
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$steps = [Collections.Generic.List[object]]::new()
$receiptFullPath = [IO.Path]::GetFullPath($ReceiptPath)
[IO.Directory]::CreateDirectory((Split-Path -Parent $receiptFullPath)) | Out-Null
$receipt = [ordered]@{
    schemaVersion = 1
    project = "consumer-setup"
    runtime = "win-x64"
    architecture = "x64"
    sourceCommit = $env:GITHUB_SHA
    workflowRunId = $env:GITHUB_RUN_ID
    workflowRunAttempt = $env:GITHUB_RUN_ATTEMPT
    startedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    mode = $null
    publisherTrust = "pre-trusted-certificate-chain"
    currentPackage = $null
    priorPackage = $null
    priorReleaseTag = $null
    predecessor = $null
    status = "failed"
    steps = $steps
}
$installRoot = $null
$dataRoot = $null
$ownsInstall = $false
$savedEnvironment = @{}
$configHash = $null
$sentinel = [Guid]::NewGuid().ToString("N")

function Add-Step([string]$Name, [string]$Status, [string]$Detail) {
    $steps.Add([ordered]@{ name = $Name; status = $Status; detail = $Detail })
    Write-Host "[consumer-certification] $Name`: $Status - $Detail"
}

function Get-Digest([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Package([string]$Path) {
    $package = Get-Item -LiteralPath $Path
    if ($package.PSIsContainer -or $package.Name -cne "Meridian-Setup.exe") {
        throw "Consumer certification requires the exact Meridian-Setup.exe file."
    }
    return $package
}

function Invoke-Executable {
    param([string]$Path, [string[]]$Arguments = @(), [hashtable]$Environment = @{}, [int[]]$AllowedExitCodes = @(0), [switch]$CaptureOutput)
    $start = [Diagnostics.ProcessStartInfo]::new($Path)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = Split-Path -Parent $Path
    # Runtime-spawning entrypoints must not redirect inherited handles: their
    # persistent descendants could keep the pipes open after the entrypoint exits.
    $start.RedirectStandardOutput = [bool]$CaptureOutput
    $start.RedirectStandardError = [bool]$CaptureOutput
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($key in $Environment.Keys) { $start.Environment[$key] = $Environment[$key] }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if ($CaptureOutput) {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
        }
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "$(Split-Path -Leaf $Path) exceeded the execution deadline."
        }
        $output = ""
        $errorOutput = ""
        if ($CaptureOutput) {
            if (-not $stdout.Wait(10000) -or -not $stderr.Wait(10000)) {
                throw "$(Split-Path -Leaf $Path) did not close its captured output within the drain deadline."
            }
            $output = $stdout.GetAwaiter().GetResult()
            $errorOutput = $stderr.GetAwaiter().GetResult()
        }
        if ($process.ExitCode -notin $AllowedExitCodes) {
            throw "$(Split-Path -Leaf $Path) exited $($process.ExitCode): $errorOutput"
        }
        return $output.Trim()
    }
    finally { $process.Dispose() }
}

function Assert-Signature($Package) {
    # Windows validates Authenticode against its existing trusted stores, including
    # timestamp policy. Importing a PFX or a throwaway root would invalidate this proof.
    $signature = Get-AuthenticodeSignature -LiteralPath $Package.FullName
    if ($signature.Status -ne "Valid" -or $null -eq $signature.SignerCertificate) {
        throw "Production Authenticode validation failed for $($Package.Name): $($signature.Status)."
    }
    $expected = [Security.Cryptography.X509Certificates.X500DistinguishedName]::new($ExpectedPublisher)
    if ($signature.SignerCertificate.SubjectName.Name -ine $expected.Name) {
        throw "Consumer installer signer does not match the configured production publisher."
    }
    return "$($signature.SignerCertificate.Subject) / $($signature.SignerCertificate.Thumbprint)"
}

function Get-Status {
    $path = Join-Path $dataRoot "runtime\lifecycle\supervisor-session.json"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try { return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable }
    catch [Management.Automation.ItemNotFoundException] { return $null } # Session shutdown removed the file between probes.
}

function Wait-Ready([string]$PreviousSession = "") {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $status = Get-Status
        if ($null -ne $status -and $status['running'] -and $null -ne $status['host'] -and
            $null -ne $status['database'] -and $null -ne $status['httpPort'] -and
            $status['sessionId'] -ne $PreviousSession) {
            try {
                $ready = Invoke-RestMethod -Uri "http://127.0.0.1:$($status.httpPort)/readyz" -TimeoutSec 5
                if ($ready.state -eq "Ready" -and $ready.readiness -eq "Ready" -and $ready.acceptingWork -eq $true) {
                    return $status
                }
            }
            catch { } # A starting host can refuse HTTP until its readiness gate opens.
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Installed consumer host did not reach Ready/acceptingWork with a fresh lifecycle session."
}

function Assert-OwnedProcess($Identity, [string]$ExpectedPath) {
    $expected = [IO.Path]::GetFullPath($ExpectedPath)
    if ([IO.Path]::GetFullPath([string]$Identity.executablePath) -ine $expected) {
        throw "Lifecycle process identity does not refer to the installed payload."
    }
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($Identity.processId)"
    if ($null -eq $process -or $process.ExecutablePath -ine $expected -or
        [Math]::Abs((([DateTimeOffset]$process.CreationDate) - [DateTimeOffset]$Identity.startedAtUtc).TotalSeconds) -gt 2) {
        throw "Installed process identity could not be independently verified through Windows."
    }
}

function Assert-BundledDatabase($Status) {
    Assert-OwnedProcess $Status.host (Join-Path $installRoot "host\Meridian.exe")
    if ($Status.database.mode -ne "Dedicated" -or
        [IO.Path]::GetFullPath([string]$Status.database.dataDirectory) -ine (Join-Path $dataRoot "postgresql\data")) {
        throw "Consumer startup must own its dedicated data cluster."
    }
    Assert-OwnedProcess $Status.database (Join-Path $installRoot "database\bin\postgres.exe")
    [void](Invoke-Sql $Status "SELECT 1;")
}

function Invoke-Sql($Status, [string]$Sql) {
    $secretPath = Join-Path $userRoot "service\lifecycle-postgresql-password.dpapi"
    $passwordBytes = [Security.Cryptography.ProtectedData]::Unprotect(
        [IO.File]::ReadAllBytes($secretPath),
        [Text.Encoding]::UTF8.GetBytes("Meridian.LifecycleSupervisor.v1"),
        [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $password = [Text.Encoding]::UTF8.GetString($passwordBytes)
    try {
        # Invoke only the installed client; never a runner's PostgreSQL on PATH.
        return Invoke-Executable -Path (Join-Path $installRoot "database\bin\psql.exe") -Arguments @(
            "-X", "-w", "-h", "127.0.0.1", "-p", [string]$Status.database.port,
            "-U", [Environment]::UserName, "-d", "postgres", "-v", "ON_ERROR_STOP=1", "-At", "-c", $Sql
        ) -Environment @{ PGPASSWORD = $password } -CaptureOutput
    }
    finally {
        [Array]::Clear($passwordBytes, 0, $passwordBytes.Length)
        $password = $null
    }
}

function Assert-Preserved($Status) {
    if ((Get-Digest $configPath) -ne $configHash -or
        (Get-Content -LiteralPath $sentinelPath -Raw).Trim() -cne $sentinel -or
        (Invoke-Sql $Status "SELECT value FROM public.meridian_consumer_certification WHERE id = 1;") -cne $sentinel) {
        throw "Consumer lifecycle operation did not preserve configuration, files, and the SQL sentinel."
    }
}

function Assert-Installed($Package, [string]$ExpectedDigest) {
    $manifest = Get-Content -LiteralPath (Join-Path $installRoot ".meridian-install-manifest.json") -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.runtime -ne "win-x64" -or $manifest.files.Count -eq 0) {
        throw "Installed consumer manifest is not a complete win-x64 payload."
    }
    foreach ($entry in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $installRoot $entry.relativePath))
        if (-not $path.StartsWith($installRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            (Get-Item -LiteralPath $path).Length -ne $entry.length -or (Get-Digest $path) -ine $entry.sha256) {
            throw "Installed consumer payload failed its manifest check."
        }
    }
    if ((Get-Digest (Join-Path $installRoot "Meridian-Setup.exe")) -cne $ExpectedDigest -or
        (Get-Digest $Package.FullName) -cne $ExpectedDigest) {
        throw "Installed consumer setup differs from the exact certified signed artifact."
    }
    $product = Get-ItemProperty -LiteralPath $registryPath
    if ($product.InstallLocation -ine $installRoot -or $product.DisplayVersion -ne $manifest.productVersion -or
        $product.ModifyPath -ne ('"' + (Join-Path $installRoot "Meridian-Setup.exe") + '" --repair') -or
        $product.UninstallString -ne ('"' + (Join-Path $installRoot "Meridian-Setup.exe") + '" --uninstall') -or
        -not (Test-Path -LiteralPath $shortcutPath)) {
        throw "Consumer installation did not register its real repair/uninstall entrypoints and Start Menu shortcut."
    }
    return $manifest
}

function Test-Launch([string]$StepName, [string]$PreviousSession = "") {
    $status = Wait-Ready $PreviousSession
    # The public entrypoint verifies a request-bound terminal startup receipt.
    [void](Invoke-Executable -Path (Join-Path $installRoot "Meridian.exe"))
    Assert-BundledDatabase $status
    $baseUrl = "http://127.0.0.1:$($status.httpPort)"
    foreach ($endpoint in @("/api/auth/me", "/api/status")) {
        $anonymous = Invoke-WebRequest -Uri ($baseUrl + $endpoint) -MaximumRedirection 0 -SkipHttpErrorCheck -TimeoutSec 10
        if ($anonymous.StatusCode -notin @(401, 403)) { throw "Installed consumer accepted an unauthenticated protected request." }
    }
    $loginBody = @{ username = "consumer-certification"; password = "test-password"; returnUrl = "/workstation/" } | ConvertTo-Json -Compress
    $login = Invoke-WebRequest -Uri "$baseUrl/api/auth/login" -Method Post -ContentType "application/json" -Body $loginBody -SessionVariable authenticated -TimeoutSec 10
    if ($login.StatusCode -ne 200 -or ($login.Content | ConvertFrom-Json).success -ne $true) {
        throw "Installed consumer authentication did not establish a session."
    }
    $me = Invoke-RestMethod -Uri "$baseUrl/api/auth/me" -WebSession $authenticated -TimeoutSec 10
    if ($me.username -cne "consumer-certification") { throw "Installed consumer session belongs to an unexpected user." }
    $protectedStatus = Invoke-WebRequest -Uri "$baseUrl/api/status" -WebSession $authenticated -TimeoutSec 10
    if ($protectedStatus.StatusCode -ne 200) { throw "Authenticated consumer session cannot access protected host status." }
    $shell = Invoke-WebRequest -Uri "$baseUrl/workstation/" -WebSession $authenticated -TimeoutSec 10
    $asset = [regex]::Match([string]$shell.Content, '(?:src|href)=["''](?<path>/workstation/(?:assets/)[^"'']+)["'']')
    if ($shell.StatusCode -ne 200 -or -not $asset.Success) { throw "Installed workstation shell or assets are unavailable." }
    $assetResponse = Invoke-WebRequest -Uri ($baseUrl + $asset.Groups["path"].Value) -WebSession $authenticated -TimeoutSec 10
    if ($assetResponse.StatusCode -ne 200) { throw "Installed workstation asset did not load." }
    if ($null -ne $configHash) { Assert-Preserved $status }
    Add-Step $StepName "passed" "Installed launcher receipt, authenticated session, workstation asset, Ready host, and bundled PostgreSQL verified; session $($status.sessionId)."
    return $status
}

function Stop-Lifecycle {
    $status = Get-Status
    [void](Invoke-Executable -Path (Join-Path $installRoot "Meridian.LifecycleSupervisor.exe") -Arguments @("stop") -AllowedExitCodes @(0, 3))
    if ($null -ne $status) {
        foreach ($identity in @($status['host'], $status['database'])) {
            if ($null -ne $identity -and $null -ne $identity.processId -and
                (Get-Process -Id $identity.processId -ErrorAction SilentlyContinue)) {
                throw "Owned consumer host or database remains running after cooperative stop."
            }
        }
    }
}

try {
    if (-not [OperatingSystem]::IsWindows() -or
        [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne "X64" -or
        [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne "X64") {
        throw "Consumer certification requires a native clean Windows x64 runner and x64 PowerShell."
    }
    if ($receipt.sourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
        $receipt.workflowRunId -notmatch '^[1-9][0-9]*$' -or $receipt.workflowRunAttempt -notmatch '^[1-9][0-9]*$') {
        throw "Consumer receipt requires a commit, workflow run, and attempt binding."
    }
    $current = Get-Package $CurrentPackage
    $currentDigest = Get-Digest $current.FullName
    $receipt.currentPackage = [ordered]@{ name = $current.Name; sha256 = $currentDigest }
    $predecessor = Get-Content -LiteralPath $PredecessorEvidencePath -Raw | ConvertFrom-Json
    if ($predecessor.schemaVersion -ne 1 -or $predecessor.project -ne "consumer-setup" -or
        $predecessor.runtime -ne "win-x64" -or $predecessor.sourceCommit -cne $receipt.sourceCommit -or
        [string]$predecessor.workflowRunId -cne $receipt.workflowRunId -or
        [string]$predecessor.workflowRunAttempt -cne $receipt.workflowRunAttempt -or
        $predecessor.firstRelease -isnot [bool] -or [string]::IsNullOrWhiteSpace($predecessor.reason)) {
        throw "Consumer predecessor evidence is missing or bound to a different artifact run."
    }
    $receipt.predecessor = $predecessor
    if ($PSBoundParameters.ContainsKey("FirstRelease") -and [bool]$FirstRelease -ne $predecessor.firstRelease) {
        throw "Requested release mode differs from resolved consumer predecessor evidence."
    }
    $FirstRelease = [bool]$predecessor.firstRelease
    $receipt.mode = if ($FirstRelease) { "first-release" } else { "n-1-update" }
    $prior = $null
    if ($FirstRelease) {
        if ($predecessor.consumerReleaseCount -ne 0 -or $null -ne $predecessor.priorPackage -or
            -not [string]::IsNullOrWhiteSpace($PriorPackage) -or -not [string]::IsNullOrWhiteSpace($PriorReleaseTag)) {
            throw "First consumer release exception requires proof that no published consumer predecessor exists."
        }
    }
    else {
        if ([string]::IsNullOrWhiteSpace($PriorPackage)) { $PriorPackage = $predecessor.priorPackage.path }
        if ([string]::IsNullOrWhiteSpace($PriorReleaseTag)) { $PriorReleaseTag = $predecessor.priorReleaseTag }
        $prior = Get-Package $PriorPackage
        $priorDigest = Get-Digest $prior.FullName
        if ($PriorReleaseTag -cne $predecessor.priorReleaseTag -or $PriorReleaseTag -notmatch '^v' -or
            $priorDigest -cne $predecessor.priorPackage.sha256) {
            throw "N-1 consumer installer is absent or mismatched."
        }
        $receipt.priorReleaseTag = $PriorReleaseTag
        $receipt.priorPackage = [ordered]@{ name = $prior.Name; sha256 = $priorDigest }
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedPublisher)) {
        [xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot "../../../src/Meridian.Wpf/Package.appxmanifest") -Raw
        $ExpectedPublisher = $manifest.Package.Identity.Publisher
    }
    $publisher = Assert-Signature $current
    if ($null -ne $prior) { [void](Assert-Signature $prior) }
    Add-Step "verify-signature" "passed" "Production publisher $publisher validated against the runner's existing trust stores."
    [void](Invoke-Executable -Path $current.FullName -Arguments @("--verify-payload", "win-x64"))
    if ($null -ne $prior) { [void](Invoke-Executable -Path $prior.FullName -Arguments @("--verify-payload", "win-x64")) }
    Add-Step "verify-payload" "passed" "Exact signed current and available N-1 EXEs read and verify their bundled win-x64 payloads."

    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $installRoot = Join-Path $localAppData "Programs\Meridian"
    $userRoot = Join-Path $localAppData "Meridian"
    $dataRoot = Join-Path $userRoot "Data"
    $registryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Meridian"
    $shortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)) "Programs\Meridian.url"
    if ((Test-Path -LiteralPath $installRoot) -or (Test-Path -LiteralPath ($installRoot + ".rollback")) -or
        (Test-Path -LiteralPath $userRoot) -or (Test-Path -LiteralPath $registryPath) -or
        (Test-Path -LiteralPath $shortcutPath) -or (Get-AppxPackage -Name "Meridian*") -or
        (Get-CimInstance Win32_Process -Filter "Name = 'Meridian.exe' OR Name = 'Meridian.LifecycleSupervisor.exe'") -or
        (Get-ChildItem -LiteralPath (Split-Path -Parent $installRoot) -Directory -Filter ".Meridian.stage-*" -ErrorAction SilentlyContinue)) {
        throw "Runner is not clean: existing Meridian installation, state, registration, or runtime was found."
    }
    Add-Step "clean-runner" "passed" "Native Windows x64; no prior consumer/MSIX installation, product registration, data, staging directories, or Meridian processes."
    $ownsInstall = $true
    $configPath = Join-Path $userRoot "appsettings.json"
    $sentinelPath = Join-Path $dataRoot "consumer-certification-sentinel.txt"
    [IO.Directory]::CreateDirectory($dataRoot) | Out-Null
    @{ DataRoot = $dataRoot; DataSource = "Synthetic"; Synthetic = @{ Enabled = $true }; Symbols = @();
       Backfill = @{ Enabled = $false; Provider = "stooq" }; Logging = @{ Level = "Information" } } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM
    Set-Content -LiteralPath $sentinelPath -Value $sentinel -Encoding utf8NoBOM
    $environment = @{
        MDC_AUTH_MODE = "required"; MDC_USERS = $null; MDC_USERNAME = "consumer-certification"
        MDC_PASSWORD_HASH = 'pbkdf2-sha256$210000$oOQU8zfLm/Pzwrl8VZlatQ==$ePPcBmch9qAIfhbablmoBT/tKPGb/TKmFBHlFWKV1uU='
        MDC_CONFIG_PATH = $configPath; MDC_POSTGRES_HOME = $null; MDC_DISABLE_RATE_LIMIT = $null
    }
    foreach ($key in $environment.Keys) {
        $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
        [Environment]::SetEnvironmentVariable($key, $environment[$key], "Process")
    }
    if ($null -ne $prior) {
        [void](Invoke-Executable -Path $prior.FullName)
        $priorManifest = Assert-Installed $prior $priorDigest
        Add-Step "install-prior" "passed" "Installed exact signed N-1 consumer EXE from $PriorReleaseTag ($priorDigest)."
        $status = Test-Launch "launch-prior"
    }
    else {
        foreach ($name in @("install-prior", "launch-prior")) {
            Add-Step $name "not-applicable" "First consumer release: $($predecessor.reason)"
        }
        [void](Invoke-Executable -Path $current.FullName)
        [void](Assert-Installed $current $currentDigest)
        Add-Step "install-current" "passed" "Installed exact signed consumer EXE ($currentDigest)."
        $status = Test-Launch "launch-current"
    }
    [void](Invoke-Sql $status "CREATE TABLE public.meridian_consumer_certification (id integer PRIMARY KEY, value text NOT NULL); INSERT INTO public.meridian_consumer_certification VALUES (1, '$sentinel');")
    $configHash = Get-Digest $configPath
    Assert-Preserved $status
    if ($null -ne $prior) {
        $priorSession = $status.sessionId
        [void](Invoke-Executable -Path $current.FullName)
        $currentManifest = Assert-Installed $current $currentDigest
        if ([DateTimeOffset]$currentManifest.stagedAtUtc -le [DateTimeOffset]$priorManifest.stagedAtUtc) {
            throw "Consumer upgrade did not promote a fresh verified installation transaction."
        }
        Add-Step "install-current" "passed" "Installed exact signed consumer EXE ($currentDigest) over N-1."
        $status = Test-Launch "launch-current" $priorSession
        Add-Step "update-current" "passed" "Published N-1 EXE $priorDigest replaced by current EXE $currentDigest in a fresh verified transaction; configuration, files, and SQL sentinel preserved."
    }
    else { Add-Step "update-current" "not-applicable" "First consumer release: $($predecessor.reason)" }
    Add-Step "bundled-database-ready" "passed" "SCRAM-authenticated SQL executed against installed database/bin/postgres.exe, independently verified PID $($status.database.processId), dedicated cluster $($status.database.dataDirectory)."

    $beforeRepair = $status.sessionId
    Stop-Lifecycle
    $damagedPath = Join-Path $installRoot "host\Meridian.exe"
    $originalHostDigest = Get-Digest $damagedPath
    Remove-Item -LiteralPath $damagedPath -Force
    if (Test-Path -LiteralPath $damagedPath) { throw "Repair probe failed to damage the installed host payload." }
    $repairStarted = [DateTimeOffset]::UtcNow
    [void](Invoke-Executable -Path (Join-Path $installRoot "Meridian-Setup.exe") -Arguments @("--repair"))
    # Installed setup detaches before promotion. Its initial exit alone proves nothing.
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $repaired = $false
        if (Test-Path -LiteralPath (Join-Path $installRoot ".meridian-install-manifest.json")) {
            $repairManifest = Get-Content -LiteralPath (Join-Path $installRoot ".meridian-install-manifest.json") -Raw | ConvertFrom-Json
            $repaired = [DateTimeOffset]$repairManifest.stagedAtUtc -ge $repairStarted -and (Test-Path -LiteralPath $damagedPath)
        }
        if (-not $repaired) { Start-Sleep -Milliseconds 500 }
    } while (-not $repaired -and [DateTimeOffset]::UtcNow -lt $deadline)
    if (-not $repaired -or (Get-Digest $damagedPath) -cne $originalHostDigest) {
        throw "Installed --repair did not complete a fresh transaction restoring the damaged payload."
    }
    [void](Assert-Installed $current $currentDigest)
    $status = Test-Launch "launch-after-repair" $beforeRepair
    Add-Step "repair-current" "passed" "Installed --repair detached, promoted a fresh verified payload, and restored the missing host with its original SHA-256; data and SQL sentinel preserved."
    $beforeRestart = $status
    [void](Invoke-Executable -Path (Join-Path $installRoot "Meridian.LifecycleSupervisor.exe") -Arguments @("restart"))
    $status = Test-Launch "launch-after-restart" $beforeRestart.sessionId
    if ($status.host.processId -eq $beforeRestart.host.processId -or $status.database.processId -eq $beforeRestart.database.processId) {
        throw "Consumer restart did not replace both owned host and bundled database process identities."
    }
    Add-Step "restart-current" "passed" "Cooperative restart produced a new session, host PID, and bundled database PID with preserved configuration, files, and SQL sentinel."

    if ($null -ne $prior) {
        $beforeRollback = $status.sessionId
        [void](Invoke-Executable -Path $prior.FullName)
        [void](Assert-Installed $prior $priorDigest)
        $status = Test-Launch "launch-after-rollback" $beforeRollback
        Add-Step "rollback-prior" "passed" "Reinstalled exact signed N-1 EXE from $PriorReleaseTag; authenticated startup and existing SQL sentinel verified."
        $beforeRestore = $status.sessionId
        $restoringAt = [DateTimeOffset]::UtcNow
        [void](Invoke-Executable -Path $current.FullName)
        $restoredManifest = Assert-Installed $current $currentDigest
        if ([DateTimeOffset]$restoredManifest.stagedAtUtc -lt $restoringAt) {
            throw "Restoring current after rollback did not promote a fresh consumer transaction."
        }
        $status = Test-Launch "launch-after-restore" $beforeRestore
        Add-Step "restore-current" "passed" "Restored exact signed current EXE after N-1 rollback, preserving configuration, files, and SQL sentinel before uninstalling the candidate."
    }
    else {
        foreach ($name in @("rollback-prior", "launch-after-rollback")) {
            Add-Step $name "not-applicable" "First consumer release: $($predecessor.reason)"
        }
    }
    Assert-Preserved $status
    Stop-Lifecycle
    # Capture stopped cluster bytes so mutable PostgreSQL files cannot hide data loss.
    $preservedFiles = @{}
    foreach ($file in Get-ChildItem -LiteralPath $userRoot -File -Recurse) {
        $preservedFiles[$file.FullName] = Get-Digest $file.FullName
    }
    if ($preservedFiles.Count -eq 0) { throw "Uninstall preservation snapshot is empty." }
    [void](Invoke-Executable -Path (Join-Path $installRoot "Meridian-Setup.exe") -Arguments @("--uninstall"))
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while (((Test-Path -LiteralPath $installRoot) -or (Test-Path -LiteralPath ($installRoot + ".rollback"))) -and [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    if ((Test-Path -LiteralPath $installRoot) -or (Test-Path -LiteralPath ($installRoot + ".rollback")) -or
        (Test-Path -LiteralPath $registryPath) -or (Test-Path -LiteralPath $shortcutPath)) {
        throw "Consumer uninstall did not remove application files, rollback files, registration, and shortcut."
    }
    Add-Step "uninstall" "passed" "Installed --uninstall removed application and rollback directories, product registration, and shortcut after cooperative shutdown."
    foreach ($path in $preservedFiles.Keys) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Digest $path) -cne $preservedFiles[$path]) {
            throw "Consumer uninstall deleted or changed a preserved configuration, credential, or data file."
        }
    }
    if ((Get-Digest $configPath) -cne $configHash -or (Get-Content -LiteralPath $sentinelPath -Raw).Trim() -cne $sentinel) {
        throw "Consumer uninstall did not preserve user configuration and data."
    }
    Add-Step "preserve-data" "passed" "Configuration, file and SQL sentinels survived every lifecycle operation; all $($preservedFiles.Count) stopped user-state/cluster files retained byte-for-byte after uninstall."
    if ((Get-Digest $current.FullName) -cne $currentDigest) { throw "Certified current EXE digest changed during lifecycle testing." }
    $receipt.status = "passed"
}
catch {
    $receipt.error = $_.Exception.Message
    throw
}
finally {
    if ($ownsInstall -and $null -ne $installRoot -and
        (Test-Path -LiteralPath (Join-Path $installRoot "Meridian.LifecycleSupervisor.exe"))) {
        try { Stop-Lifecycle } catch { Write-Warning "Consumer certification cleanup could not stop its owned runtime." }
    }
    foreach ($key in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], "Process")
    }
    $receipt.completedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    $receipt | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $receiptFullPath -Encoding utf8NoBOM
}

Write-Host "[consumer-certification] Lifecycle passed ($($receipt.mode)). Receipt: $receiptFullPath"
