Set-StrictMode -Version Latest

function ConvertTo-MeridianBuildSlug {
    param([Parameter(Mandatory = $true)][string]$Value)

    $slug = $Value.ToLowerInvariant() -replace '[^a-z0-9]+', '-'
    $slug = $slug.Trim('-')
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return 'build'
    }

    return $slug
}

function New-MeridianBuildIsolationKey {
    param([string]$Prefix = 'automation')

    $slug = ConvertTo-MeridianBuildSlug -Value $Prefix
    $timestamp = Get-Date -Format 'yyyyMMddHHmmss'
    return "$slug-$PID-$timestamp"
}

function Get-MeridianProjectOutputRoot {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [string]$IsolationKey
    )

    if ([string]::IsNullOrWhiteSpace($IsolationKey)) {
        return $null
    }

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot "artifacts/bin/$IsolationKey/$projectName"))
}

function Get-MeridianProjectBinaryPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][string]$Framework,
        [Parameter(Mandatory = $true)][string]$BinaryName,
        [string]$IsolationKey
    )

    if ([string]::IsNullOrWhiteSpace($IsolationKey)) {
        $projectDirectory = Split-Path -Parent $ProjectPath
        return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot (Join-Path $projectDirectory "bin/$Configuration/$Framework/$BinaryName")))
    }

    $outputRoot = Get-MeridianProjectOutputRoot -RepoRoot $RepoRoot -ProjectPath $ProjectPath -IsolationKey $IsolationKey
    return [System.IO.Path]::GetFullPath((Join-Path $outputRoot "$Configuration/$Framework/$BinaryName"))
}

function Get-MeridianBuildArguments {
    param(
        [string]$IsolationKey,
        [string]$TargetFramework,
        [string[]]$AdditionalProperties = @(),
        [switch]$EnableFullWpfBuild
    )

    $args = @(
        '/p:EnableWindowsTargeting=true',
        '-maxcpucount:1',
        '/nr:false'
    )

    if (-not [string]::IsNullOrWhiteSpace($IsolationKey)) {
        $args += "/p:MeridianBuildIsolationKey=$IsolationKey"
    }

    if (-not [string]::IsNullOrWhiteSpace($TargetFramework)) {
        $args += "/p:TargetFramework=$TargetFramework"
    }

    if ($EnableFullWpfBuild) {
        $args += '/p:EnableFullWpfBuild=true'
    }

    foreach ($property in $AdditionalProperties) {
        if ([string]::IsNullOrWhiteSpace($property)) {
            continue
        }

        if ($property.StartsWith('/p:', [System.StringComparison]::OrdinalIgnoreCase) -or
            $property.StartsWith('-p:', [System.StringComparison]::OrdinalIgnoreCase)) {
            $args += $property
            continue
        }

        $args += "/p:$property"
    }

    return $args
}
