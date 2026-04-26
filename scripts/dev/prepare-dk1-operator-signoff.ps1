param(
    [string]$OutputPath = "",
    [switch]$Validate,
    [switch]$Force,
    [switch]$Json
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dateStamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
$requiredOperatorOwners = @("Data Operations", "Provider Reliability", "Trading")

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts/provider-validation/_automation/$dateStamp/dk1-operator-signoff.json"
}
else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}

function ConvertTo-RelativePath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($repoRoot.Length + 1).Replace('\', '/')
    }

    return $fullPath
}

function Get-ObjectPropertyValue {
    param(
        [object]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Object -or -not ($Object.PSObject.Properties.Name -contains $Name)) {
        return $null
    }

    return $Object.PSObject.Properties[$Name].Value
}

function Test-ApprovedDecision {
    param([string]$Decision)

    if ([string]::IsNullOrWhiteSpace($Decision)) {
        return $false
    }

    return @("approved", "signed", "complete", "completed") -contains $Decision.Trim().ToLowerInvariant()
}

function Get-OperatorSignoffApprovalItems {
    param([object]$Payload)

    if ($null -ne $Payload -and $Payload.PSObject.Properties.Name -contains "approvals") {
        return @($Payload.approvals)
    }

    if ($null -ne $Payload -and $Payload.PSObject.Properties.Name -contains "signoffs") {
        return @($Payload.signoffs)
    }

    if ($null -ne $Payload -and $Payload.PSObject.Properties.Name -contains "owners") {
        return @($Payload.owners)
    }

    return @()
}

function Get-OperatorSignoffReview {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$RequiredOwners
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        throw "Operator sign-off file was not found: $resolvedPath"
    }

    $payload = Get-Content -Raw -LiteralPath $resolvedPath | ConvertFrom-Json
    $approvalItems = @(Get-OperatorSignoffApprovalItems -Payload $payload)
    $approvalRows = New-Object System.Collections.Generic.List[object]
    $validSignedOwners = New-Object System.Collections.Generic.List[string]
    $validSignedAtValues = New-Object System.Collections.Generic.List[datetimeoffset]

    foreach ($approval in $approvalItems) {
        $owner = [string](Get-ObjectPropertyValue -Object $approval -Name "owner")
        $signedBy = [string](Get-ObjectPropertyValue -Object $approval -Name "signedBy")
        $signedAtRaw = [string](Get-ObjectPropertyValue -Object $approval -Name "signedAtUtc")
        if ([string]::IsNullOrWhiteSpace($signedAtRaw)) {
            $signedAtRaw = [string](Get-ObjectPropertyValue -Object $approval -Name "signedAt")
        }

        $decision = [string](Get-ObjectPropertyValue -Object $approval -Name "decision")
        if ([string]::IsNullOrWhiteSpace($decision)) {
            $decision = [string](Get-ObjectPropertyValue -Object $approval -Name "status")
        }

        $rationale = [string](Get-ObjectPropertyValue -Object $approval -Name "rationale")
        if ([string]::IsNullOrWhiteSpace($rationale)) {
            $rationale = [string](Get-ObjectPropertyValue -Object $approval -Name "reason")
        }

        $missingRequirements = New-Object System.Collections.Generic.List[string]
        if ([string]::IsNullOrWhiteSpace($owner)) {
            $missingRequirements.Add("owner")
        }
        if ([string]::IsNullOrWhiteSpace($signedBy)) {
            $missingRequirements.Add("signedBy")
        }
        if (-not (Test-ApprovedDecision -Decision $decision)) {
            $missingRequirements.Add("approvedDecision")
        }
        if ([string]::IsNullOrWhiteSpace($rationale)) {
            $missingRequirements.Add("rationale")
        }

        $signedAtValue = [DateTimeOffset]::MinValue
        $hasSignedAt = [DateTimeOffset]::TryParse($signedAtRaw, [ref]$signedAtValue)
        if (-not $hasSignedAt) {
            $missingRequirements.Add("signedAtUtc")
        }

        $status = if ($missingRequirements.Count -eq 0) { "valid" } else { "invalid" }
        if ($status -eq "valid") {
            foreach ($requiredOwner in $RequiredOwners) {
                if ([string]::Equals($requiredOwner, $owner, [System.StringComparison]::OrdinalIgnoreCase) -and
                    -not $validSignedOwners.Contains($requiredOwner)) {
                    $validSignedOwners.Add($requiredOwner)
                    $validSignedAtValues.Add($signedAtValue)
                }
            }
        }

        $approvalRows.Add([ordered]@{
            owner = $owner
            signedBy = $signedBy
            signedAtUtc = if ($hasSignedAt) { $signedAtValue.ToUniversalTime().ToString("O") } else { $null }
            decision = $decision
            rationale = $rationale
            status = $status
            missingRequirements = $missingRequirements.ToArray()
        })
    }

    $missingOwners = @(
        $RequiredOwners |
            Where-Object { $validSignedOwners -notcontains $_ }
    )
    $completedAtUtc = if ($validSignedAtValues.Count -gt 0) {
        @($validSignedAtValues | Sort-Object -Descending | Select-Object -First 1)[0].ToUniversalTime().ToString("O")
    }
    else {
        $null
    }
    $status = if ($missingOwners.Count -eq 0 -and $RequiredOwners.Count -gt 0) {
        "signed"
    }
    elseif ($validSignedOwners.Count -gt 0) {
        "partial"
    }
    else {
        "pending"
    }

    return [ordered]@{
        requiredOwners = $RequiredOwners
        status = $status
        validForDk1Exit = $status -eq "signed"
        signedOwners = $validSignedOwners.ToArray()
        missingOwners = $missingOwners
        completedAtUtc = $completedAtUtc
        sourcePath = ConvertTo-RelativePath -Path $resolvedPath
        approvals = $approvalRows.ToArray()
    }
}

function New-OperatorSignoffTemplate {
    param([Parameter(Mandatory)][string[]]$RequiredOwners)

    return [ordered]@{
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
        purpose = "DK1 operator sign-off for the Alpaca/Robinhood/Yahoo pilot parity packet."
        requiredOwners = $RequiredOwners
        instructions = @(
            "Only fill this file after Data Operations, Provider Reliability, and Trading have reviewed the DK1 packet.",
            "Each approval must include owner, signedBy, signedAtUtc, approved decision, and rationale.",
            "Run prepare-dk1-operator-signoff.ps1 -Validate before passing this file to run-wave1-provider-validation.ps1 or generate-dk1-pilot-parity-packet.ps1."
        )
        approvals = @(
            foreach ($owner in $RequiredOwners) {
                [ordered]@{
                    owner = $owner
                    signedBy = ""
                    signedAtUtc = ""
                    decision = "pending"
                    rationale = ""
                }
            }
        )
    }
}

if ($Validate) {
    $review = Get-OperatorSignoffReview -Path $OutputPath -RequiredOwners $requiredOperatorOwners
    if ($Json) {
        $review | ConvertTo-Json -Depth 5
    }
    else {
        Write-Host "DK1 operator sign-off status: $($review.status)"
        Write-Host "Signed owners: $(if ($review.signedOwners.Count -eq 0) { 'none' } else { $review.signedOwners -join ', ' })"
        Write-Host "Missing owners: $(if ($review.missingOwners.Count -eq 0) { 'none' } else { $review.missingOwners -join ', ' })"
    }

    if (-not $review.validForDk1Exit) {
        throw "DK1 operator sign-off is not complete. Missing owners: $(if ($review.missingOwners.Count -eq 0) { 'none' } else { $review.missingOwners -join ', ' })"
    }

    return
}

if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Operator sign-off file already exists: $OutputPath. Re-run with -Force to replace it."
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$template = New-OperatorSignoffTemplate -RequiredOwners $requiredOperatorOwners
$template | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

if ($Json) {
    [ordered]@{
        path = ConvertTo-RelativePath -Path $OutputPath
        status = "template-written"
        requiredOwners = $requiredOperatorOwners
    } | ConvertTo-Json -Depth 5
}
else {
    Write-Host "DK1 operator sign-off template written to:"
    Write-Host "  $OutputPath"
}
