Set-StrictMode -Version Latest

function New-PreflightIssue {
    param(
        [Parameter(Mandatory = $true)][string]$Check,
        [Parameter(Mandatory = $true)][string]$Message,
        [Parameter(Mandatory = $true)][string]$Recommendation
    )

    return [ordered]@{
        check = $Check
        message = $Message
        recommendation = $Recommendation
    }
}

function Test-PreflightDirectoryWritable {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        if (-not (Test-Path -LiteralPath $Path)) {
            New-Item -ItemType Directory -Force -Path $Path | Out-Null
        }

        $probe = Join-Path $Path (".preflight-write-{0}.tmp" -f ([System.Guid]::NewGuid().ToString('N')))
        Set-Content -LiteralPath $probe -Value 'ok' -Encoding utf8
        Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
        return $true
    }
    catch {
        return $false
    }
}

function Invoke-MeridianPreflight {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Scenario,
        [string[]]$RequiredCommands = @(),
        [string[]]$RequiredPaths = @(),
        [string[]]$WritableDirectories = @(),
        [string[]]$RequiredEnvironmentVariables = @(),
        [hashtable]$FeatureFlagExpectations = @{},
        [switch]$RequireWindows,
        [switch]$EmitJson,
        [switch]$AllowWarnings,
        [string]$SuccessNextAction = 'Proceed with workflow execution.'
    )

    $blockingChecks = New-Object System.Collections.Generic.List[object]
    $warnings = New-Object System.Collections.Generic.List[object]

    if ($RequireWindows -and -not ($IsWindows -or $env:OS -eq 'Windows_NT')) {
        $blockingChecks.Add((New-PreflightIssue -Check 'platform.windows' -Message 'Workflow requires Windows.' -Recommendation 'Run this workflow on a Windows host with WPF/UI automation support.'))
    }

    foreach ($command in $RequiredCommands) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            $blockingChecks.Add((New-PreflightIssue -Check ("command.{0}" -f $command) -Message "Required command '$command' was not found in PATH." -Recommendation "Install '$command' and ensure PATH is configured before rerunning."))
        }
    }

    foreach ($path in $RequiredPaths) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and -not (Test-Path -LiteralPath $path)) {
            $blockingChecks.Add((New-PreflightIssue -Check ("path.{0}" -f ($path -replace '[^A-Za-z0-9._/-]', '_')) -Message "Required path '$path' was not found." -Recommendation 'Verify the path exists or regenerate required inputs before rerunning.'))
        }
    }

    foreach ($directory in $WritableDirectories) {
        if ([string]::IsNullOrWhiteSpace($directory)) {
            continue
        }

        if (-not (Test-PreflightDirectoryWritable -Path $directory)) {
            $blockingChecks.Add((New-PreflightIssue -Check ("write.{0}" -f ($directory -replace '[^A-Za-z0-9._/-]', '_')) -Message "Directory '$directory' is not writable." -Recommendation 'Fix permissions or choose a writable output directory.'))
        }
    }

    foreach ($variable in $RequiredEnvironmentVariables) {
        $value = [Environment]::GetEnvironmentVariable($variable)
        if ([string]::IsNullOrWhiteSpace($value)) {
            $blockingChecks.Add((New-PreflightIssue -Check ("env.{0}" -f $variable) -Message "Required environment variable '$variable' is missing." -Recommendation "Set '$variable' before running this workflow."))
        }
    }

    foreach ($feature in $FeatureFlagExpectations.Keys) {
        $expectedValue = [string]$FeatureFlagExpectations[$feature]
        $actual = [Environment]::GetEnvironmentVariable($feature)
        if ([string]::IsNullOrWhiteSpace($actual)) {
            $warnings.Add((New-PreflightIssue -Check ("feature.{0}" -f $feature) -Message "Feature flag '$feature' is not set." -Recommendation "Expected value '$expectedValue' when this workflow runs outside fixture mode."))
            continue
        }

        if (-not [string]::Equals($actual, $expectedValue, [System.StringComparison]::OrdinalIgnoreCase)) {
            $warnings.Add((New-PreflightIssue -Check ("feature.{0}" -f $feature) -Message "Feature flag '$feature' is '$actual' (expected '$expectedValue')." -Recommendation 'Confirm the configured flag value matches the intended workflow mode.'))
        }
    }

    $status = if ($blockingChecks.Count -gt 0) { 'blocked' } elseif ($warnings.Count -gt 0 -and -not $AllowWarnings) { 'warning' } else { 'ok' }
    $nextAction = if ($blockingChecks.Count -gt 0) {
        'Resolve blocking checks and rerun preflight.'
    }
    elseif ($warnings.Count -gt 0 -and -not $AllowWarnings) {
        'Review warnings and decide whether to proceed.'
    }
    else {
        $SuccessNextAction
    }

    $result = [ordered]@{
        scenario = $Scenario
        status = $status
        blockingChecks = @($blockingChecks.ToArray())
        warnings = @($warnings.ToArray())
        nextAction = $nextAction
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    }

    if ($EmitJson) {
        $result | ConvertTo-Json -Depth 8 | Write-Host
    }

    return [pscustomobject]$result
}
