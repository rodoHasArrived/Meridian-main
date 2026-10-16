Set-StrictMode -Version Latest

function Assert-MeridianDesktopBuildReceipt {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Receipt,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][string]$Framework,
        [Parameter(Mandatory = $true)][string]$BinaryName
    )

    if (-not $Receipt.Contains('schemaVersion') -or
        ($Receipt.schemaVersion -isnot [long] -and $Receipt.schemaVersion -isnot [int]) -or $Receipt.schemaVersion -ne 1) {
        throw 'Unsupported or missing desktop build receipt schemaVersion.'
    }

    foreach ($field in @('repoRoot', 'projectPath', 'configuration', 'framework', 'outputDirectory', 'executablePath', 'assemblyPath', 'depsJsonPath', 'runtimeConfigPath')) {
        if (-not $Receipt.Contains($field) -or $Receipt[$field] -isnot [string] -or [string]::IsNullOrWhiteSpace($Receipt[$field])) {
            throw "Desktop build receipt is missing a nonempty string field '$field'."
        }
    }

    if (-not $Receipt.Contains('buildIsolationKey') -or $Receipt.buildIsolationKey -isnot [string] -or
        ($Receipt.buildIsolationKey -ne '' -and $Receipt.buildIsolationKey -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]*$')) {
        throw 'Desktop build receipt has a missing or invalid buildIsolationKey.'
    }

    if ($Receipt.configuration -cne $Configuration -or $Receipt.framework -cne $Framework) {
        throw "Desktop build receipt configuration/framework '$($Receipt.configuration) / $($Receipt.framework)' is incompatible with '$Configuration / $Framework'."
    }

    $expectedExe = Get-MeridianProjectBinaryPath -RepoRoot $RepoRoot -ProjectPath $ProjectPath `
        -Configuration $Configuration -Framework $Framework -BinaryName $BinaryName -IsolationKey $Receipt.buildIsolationKey
    $outputDirectory = Split-Path -Parent $expectedExe
    $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($BinaryName)
    $expectedPaths = @{
        repoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
        projectPath = if ([System.IO.Path]::IsPathFullyQualified($ProjectPath)) { [System.IO.Path]::GetFullPath($ProjectPath) } else { [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $ProjectPath)) }
        outputDirectory = $outputDirectory
        executablePath = $expectedExe
        assemblyPath = Join-Path $outputDirectory "$assemblyName.dll"
        depsJsonPath = Join-Path $outputDirectory "$assemblyName.deps.json"
        runtimeConfigPath = Join-Path $outputDirectory "$assemblyName.runtimeconfig.json"
    }
    $comparison = if ($IsWindows) { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }
    foreach ($field in $expectedPaths.Keys) {
        if (-not [System.IO.Path]::IsPathFullyQualified($Receipt[$field]) -or
            -not [string]::Equals([System.IO.Path]::GetFullPath($Receipt[$field]), $expectedPaths[$field], $comparison)) {
            throw "Desktop build receipt '$field' is incompatible with this repository/project output: '$($Receipt[$field])'."
        }
    }

    foreach ($field in @('executablePath', 'assemblyPath', 'depsJsonPath', 'runtimeConfigPath')) {
        if (-not (Test-Path -LiteralPath $Receipt[$field] -PathType Leaf)) {
            throw "Desktop build receipt artifact '$field' is missing: '$($Receipt[$field])'."
        }
    }
}

function New-MeridianDesktopBuildReceipt {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][string]$Framework,
        [string]$BinaryName = 'Meridian.Desktop.exe',
        [string]$IsolationKey = ''
    )

    $executablePath = Get-MeridianProjectBinaryPath -RepoRoot $RepoRoot -ProjectPath $ProjectPath `
        -Configuration $Configuration -Framework $Framework -BinaryName $BinaryName -IsolationKey $IsolationKey
    $outputDirectory = Split-Path -Parent $executablePath
    $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($BinaryName)
    $receipt = [ordered]@{
        schemaVersion = 1
        repoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
        projectPath = if ([System.IO.Path]::IsPathFullyQualified($ProjectPath)) { [System.IO.Path]::GetFullPath($ProjectPath) } else { [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $ProjectPath)) }
        buildIsolationKey = $IsolationKey
        configuration = $Configuration
        framework = $Framework
        outputDirectory = $outputDirectory
        executablePath = $executablePath
        assemblyPath = Join-Path $outputDirectory "$assemblyName.dll"
        depsJsonPath = Join-Path $outputDirectory "$assemblyName.deps.json"
        runtimeConfigPath = Join-Path $outputDirectory "$assemblyName.runtimeconfig.json"
    }
    Assert-MeridianDesktopBuildReceipt -Receipt $receipt -RepoRoot $RepoRoot -ProjectPath $ProjectPath `
        -Configuration $Configuration -Framework $Framework -BinaryName $BinaryName
    $receiptPath = Join-Path $outputDirectory 'desktop-build-receipt.json'
    $receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receiptPath -Encoding utf8
    return $receiptPath
}

function Read-MeridianDesktopBuildReceipt {
    param(
        [Parameter(Mandatory = $true)][string]$ReceiptPath,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][string]$Framework,
        [string]$BinaryName = 'Meridian.Desktop.exe'
    )

    if (-not (Test-Path -LiteralPath $ReceiptPath -PathType Leaf)) {
        throw "Desktop build receipt not found: '$ReceiptPath'."
    }

    $receipt = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json -AsHashtable
    if ($receipt -isnot [System.Collections.IDictionary]) {
        throw 'Desktop build receipt must be a JSON object.'
    }
    Assert-MeridianDesktopBuildReceipt -Receipt $receipt -RepoRoot $RepoRoot -ProjectPath $ProjectPath `
        -Configuration $Configuration -Framework $Framework -BinaryName $BinaryName
    return $receipt
}
