Set-StrictMode -Version Latest

function Resolve-MeridianWorkflowProfilePath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProfileName,
        [string]$ProfileRoot = 'scripts/dev/workflow-profiles'
    )

    $root = if ([System.IO.Path]::IsPathRooted($ProfileRoot)) {
        [System.IO.Path]::GetFullPath($ProfileRoot)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $ProfileRoot))
    }

    return [System.IO.Path]::GetFullPath((Join-Path $root ($ProfileName + '.json')))
}

function Get-MeridianWorkflowProfile {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProfileName,
        [string]$ProfileRoot = 'scripts/dev/workflow-profiles'
    )

    $profilePath = Resolve-MeridianWorkflowProfilePath -RepoRoot $RepoRoot -ProfileName $ProfileName -ProfileRoot $ProfileRoot
    if (-not (Test-Path -LiteralPath $profilePath)) {
        throw "Workflow profile '$ProfileName' was not found at '$profilePath'."
    }

    $profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json -AsHashtable
    return [ordered]@{
        name = $ProfileName
        path = $profilePath
        root = [System.IO.Path]::GetDirectoryName($profilePath)
        data = $profile
    }
}

function Get-MeridianWorkflowProfileValue {
    param(
        [hashtable]$Table,
        [Parameter(Mandatory = $true)][string]$Key,
        $Fallback = $null
    )

    if ($null -ne $Table -and $Table.Contains($Key) -and $null -ne $Table[$Key] -and "$($Table[$Key])" -ne '') {
        return $Table[$Key]
    }

    return $Fallback
}

function Test-MeridianWorkflowProfile {
    param(
        [Parameter(Mandatory = $true)][hashtable]$ProfileData,
        [switch]$NoFixture,
        [switch]$ReuseExistingApp
    )

    $errors = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]

    $build = Get-MeridianWorkflowProfileValue -Table $ProfileData -Key 'build' -Fallback @{}
    $fixture = Get-MeridianWorkflowProfileValue -Table $ProfileData -Key 'fixture' -Fallback @{}
    $hostProfile = Get-MeridianWorkflowProfileValue -Table $ProfileData -Key 'host' -Fallback @{}
    $screenshots = Get-MeridianWorkflowProfileValue -Table $ProfileData -Key 'screenshots' -Fallback @{}

    foreach ($key in @('projectPath', 'configuration', 'framework', 'exeName')) {
        if ([string]::IsNullOrWhiteSpace([string](Get-MeridianWorkflowProfileValue -Table $build -Key $key -Fallback ''))) {
            $errors.Add("build.$key is required.")
        }
    }

    $fixtureRequired = [bool](Get-MeridianWorkflowProfileValue -Table $fixture -Key 'required' -Fallback $false)
    if ($fixtureRequired -and $NoFixture) {
        $errors.Add('Profile requires fixture mode, but -NoFixture was specified.')
    }

    $baseUrl = [string](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'baseUrl' -Fallback '')
    if ([string]::IsNullOrWhiteSpace($baseUrl)) {
        $errors.Add('host.baseUrl is required.')
    }
    else {
        $parsedBaseUri = $null
        if (-not [System.Uri]::TryCreate($baseUrl, [System.UriKind]::Absolute, [ref]$parsedBaseUri)) {
            $errors.Add("host.baseUrl '$baseUrl' must be an absolute URI.")
        }
    }

    $healthPath = [string](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'healthPath' -Fallback '')
    if ([string]::IsNullOrWhiteSpace($healthPath)) {
        $errors.Add('host.healthPath is required.')
    }

    $outputRoot = [string](Get-MeridianWorkflowProfileValue -Table $screenshots -Key 'outputRoot' -Fallback '')
    if ([string]::IsNullOrWhiteSpace($outputRoot)) {
        $warnings.Add('screenshots.outputRoot is not set; caller must provide output path.')
    }

    $retention = Get-MeridianWorkflowProfileValue -Table $screenshots -Key 'retention' -Fallback @{}
    $maxAgeDays = [int](Get-MeridianWorkflowProfileValue -Table $retention -Key 'maxAgeDays' -Fallback 14)
    $retainLatest = [int](Get-MeridianWorkflowProfileValue -Table $retention -Key 'retainLatest' -Fallback 10)
    if ($maxAgeDays -lt 0) {
        $errors.Add('screenshots.retention.maxAgeDays must be >= 0.')
    }

    if ($retainLatest -lt 0) {
        $errors.Add('screenshots.retention.retainLatest must be >= 0.')
    }

    if ($ReuseExistingApp -and $fixtureRequired) {
        $warnings.Add('Reusing an existing app can bypass fixture initialization from this profile.')
    }

    return [ordered]@{
        isValid = $errors.Count -eq 0
        errors = @($errors)
        warnings = @($warnings)
        resolved = [ordered]@{
            fixtureRequired = $fixtureRequired
            outputRoot = $outputRoot
            retention = [ordered]@{
                maxAgeDays = $maxAgeDays
                retainLatest = $retainLatest
            }
        }
    }
}
