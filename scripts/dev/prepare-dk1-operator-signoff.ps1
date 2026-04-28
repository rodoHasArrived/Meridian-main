param(
    [string]$OutputPath = "",
    [string]$PacketPath = "",
    [switch]$Validate,
    [switch]$Force,
    [switch]$Json,
    [string]$CheckpointPath = "",
    [string[]]$ForceCheckpointStep = @(),
    [switch]$AllowCheckpointInputMismatch
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SharedPreflight.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $PSScriptRoot "SharedCheckpoint.ps1")
$dateStamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
$requiredOperatorOwners = @("Data Operations", "Provider Reliability", "Trading")

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts/provider-validation/_automation/$dateStamp/dk1-operator-signoff.json"
}
else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}
if ([string]::IsNullOrWhiteSpace($CheckpointPath)) {
    $CheckpointPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $OutputPath) "dk1-operator-signoff.checkpoint.json"))
}
$checkpoint = Initialize-MeridianCheckpoint `
    -Workflow "prepare-dk1-operator-signoff" `
    -CheckpointPath $CheckpointPath `
    -InputObject ([ordered]@{
        outputPath = $OutputPath
        packetPath = $PacketPath
        validate = [bool]$Validate
        force = [bool]$Force
        json = [bool]$Json
    }) `
    -ForceStep $ForceCheckpointStep `
    -AllowInputMismatch:$AllowCheckpointInputMismatch

$outputDirectory = Split-Path -Parent $OutputPath
$requiredPaths = @()
if (-not [string]::IsNullOrWhiteSpace($PacketPath)) {
    $requiredPaths += [System.IO.Path]::GetFullPath($PacketPath)
}

$preflight = Invoke-MeridianPreflight `
    -Scenario 'dk1-operator-signoff' `
    -RequiredPaths $requiredPaths `
    -WritableDirectories @($outputDirectory) `
    -EmitJson `
    -AllowWarnings

if ($preflight.status -eq 'blocked') {
    $preflightPath = Join-Path $outputDirectory 'preflight.json'
    $preflight | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $preflightPath -Encoding utf8
    throw "Preflight failed. See '$preflightPath' for diagnostics."
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

    if ($Object -is [System.Collections.IDictionary] -and $Object.Contains($Name)) {
        return $Object[$Name]
    }

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

function Get-Dk1PacketReview {
    param([Parameter(Mandatory)][string]$Path)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        throw "DK1 parity packet was not found: $resolvedPath"
    }

    $packet = Get-Content -Raw -LiteralPath $resolvedPath | ConvertFrom-Json
    $status = [string](Get-ObjectPropertyValue -Object $packet -Name "status")
    $generatedAtUtc = [string](Get-ObjectPropertyValue -Object $packet -Name "generatedAtUtc")
    $sourceSummary = [string](Get-ObjectPropertyValue -Object $packet -Name "sourceSummary")
    $sourceResult = [string](Get-ObjectPropertyValue -Object $packet -Name "sourceResult")
    $blockers = @(Get-ObjectPropertyValue -Object $packet -Name "blockers")

    $sampleReview = Get-ObjectPropertyValue -Object $packet -Name "sampleReview"
    $requiredSampleCountValue = Get-ObjectPropertyValue -Object $sampleReview -Name "requiredCount"
    $requiredSampleCount = if ($null -ne $requiredSampleCountValue) { [int]$requiredSampleCountValue } else { 0 }
    $samples = @(Get-ObjectPropertyValue -Object $sampleReview -Name "samples")
    $readySampleCount = @(
        $samples | Where-Object {
            [string]::Equals(
                [string](Get-ObjectPropertyValue -Object $_ -Name "status"),
                "ready",
                [System.StringComparison]::OrdinalIgnoreCase)
        }
    ).Count

    $evidenceDocuments = @(Get-ObjectPropertyValue -Object $packet -Name "evidenceDocuments")
    $validatedEvidenceDocumentCount = @(
        $evidenceDocuments | Where-Object {
            [string]::Equals(
                [string](Get-ObjectPropertyValue -Object $_ -Name "status"),
                "validated",
                [System.StringComparison]::OrdinalIgnoreCase)
        }
    ).Count

    $trustRationaleContract = Get-ObjectPropertyValue -Object $packet -Name "trustRationaleContract"
    $baselineThresholdContract = Get-ObjectPropertyValue -Object $packet -Name "baselineThresholdContract"
    $trustRationaleContractStatus = [string](Get-ObjectPropertyValue -Object $trustRationaleContract -Name "status")
    $baselineThresholdContractStatus = [string](Get-ObjectPropertyValue -Object $baselineThresholdContract -Name "status")

    $samplesReady = $requiredSampleCount -gt 0 -and $readySampleCount -eq $requiredSampleCount
    $documentsValidated = $evidenceDocuments.Count -gt 0 -and $validatedEvidenceDocumentCount -eq $evidenceDocuments.Count
    $contractsValidated =
        [string]::Equals($trustRationaleContractStatus, "validated", [System.StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals($baselineThresholdContractStatus, "validated", [System.StringComparison]::OrdinalIgnoreCase)
    $validForOperatorReview =
        [string]::Equals($status, "ready-for-operator-review", [System.StringComparison]::OrdinalIgnoreCase) -and
        $blockers.Count -eq 0 -and
        $samplesReady -and
        $documentsValidated -and
        $contractsValidated

    return [ordered]@{
        path = ConvertTo-RelativePath -Path $resolvedPath
        status = $status
        generatedAtUtc = $generatedAtUtc
        sourceSummary = $sourceSummary
        sourceResult = $sourceResult
        requiredSampleCount = $requiredSampleCount
        readySampleCount = $readySampleCount
        evidenceDocumentCount = $evidenceDocuments.Count
        validatedEvidenceDocumentCount = $validatedEvidenceDocumentCount
        trustRationaleContractStatus = $trustRationaleContractStatus
        baselineThresholdContractStatus = $baselineThresholdContractStatus
        validForOperatorReview = $validForOperatorReview
        blockers = $blockers
        samples = @(
            $samples | ForEach-Object {
                [ordered]@{
                    id = [string](Get-ObjectPropertyValue -Object $_ -Name "id")
                    status = [string](Get-ObjectPropertyValue -Object $_ -Name "status")
                    missingRequirements = @(Get-ObjectPropertyValue -Object $_ -Name "missingRequirements")
                }
            }
        )
        evidenceDocuments = @(
            $evidenceDocuments | ForEach-Object {
                [ordered]@{
                    name = [string](Get-ObjectPropertyValue -Object $_ -Name "name")
                    gate = [string](Get-ObjectPropertyValue -Object $_ -Name "gate")
                    status = [string](Get-ObjectPropertyValue -Object $_ -Name "status")
                    path = [string](Get-ObjectPropertyValue -Object $_ -Name "path")
                    missingRequirements = @(Get-ObjectPropertyValue -Object $_ -Name "missingRequirements")
                }
            }
        )
    }
}

function Assert-PacketReadyForOperatorReview {
    param([object]$PacketReview)

    if ($null -eq $PacketReview) {
        return
    }

    $validForOperatorReview = [bool](Get-ObjectPropertyValue -Object $PacketReview -Name "validForOperatorReview")
    if ($validForOperatorReview) {
        return
    }

    $status = [string](Get-ObjectPropertyValue -Object $PacketReview -Name "status")
    $blockers = @(Get-ObjectPropertyValue -Object $PacketReview -Name "blockers")
    $blockerText = if ($blockers.Count -eq 0) { "packet evidence is incomplete" } else { $blockers -join "; " }
    throw "DK1 parity packet is not ready for operator sign-off. Status: $status. Blockers: $blockerText"
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
        [Parameter(Mandatory)][string[]]$RequiredOwners,
        [object]$PacketReview = $null
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

    $packetBindingMissingRequirements = New-Object System.Collections.Generic.List[string]
    $payloadPacketReview = Get-ObjectPropertyValue -Object $payload -Name "packetReview"
    if ($null -ne $PacketReview) {
        $expectedPacketPath = [string](Get-ObjectPropertyValue -Object $PacketReview -Name "path")
        $expectedGeneratedAtUtc = [string](Get-ObjectPropertyValue -Object $PacketReview -Name "generatedAtUtc")
        $expectedStatus = [string](Get-ObjectPropertyValue -Object $PacketReview -Name "status")

        if (-not [bool](Get-ObjectPropertyValue -Object $PacketReview -Name "validForOperatorReview")) {
            $packetBindingMissingRequirements.Add("packetReadyForOperatorReview")
        }

        if ($null -eq $payloadPacketReview) {
            $packetBindingMissingRequirements.Add("packetReview")
        }
        else {
            $actualPacketPath = [string](Get-ObjectPropertyValue -Object $payloadPacketReview -Name "path")
            $actualGeneratedAtUtc = [string](Get-ObjectPropertyValue -Object $payloadPacketReview -Name "generatedAtUtc")
            $actualStatus = [string](Get-ObjectPropertyValue -Object $payloadPacketReview -Name "status")

            if (-not [string]::Equals($expectedPacketPath, $actualPacketPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                $packetBindingMissingRequirements.Add("packetPath")
            }
            if (-not [string]::Equals($expectedGeneratedAtUtc, $actualGeneratedAtUtc, [System.StringComparison]::OrdinalIgnoreCase)) {
                $packetBindingMissingRequirements.Add("packetGeneratedAtUtc")
            }
            if (-not [string]::Equals($expectedStatus, $actualStatus, [System.StringComparison]::OrdinalIgnoreCase)) {
                $packetBindingMissingRequirements.Add("packetStatus")
            }
        }
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
        validForDk1Exit = $status -eq "signed" -and $packetBindingMissingRequirements.Count -eq 0
        signedOwners = $validSignedOwners.ToArray()
        missingOwners = $missingOwners
        completedAtUtc = $completedAtUtc
        sourcePath = ConvertTo-RelativePath -Path $resolvedPath
        packetBindingStatus = if ($packetBindingMissingRequirements.Count -eq 0) { "valid" } else { "invalid" }
        packetBindingMissingRequirements = $packetBindingMissingRequirements.ToArray()
        packetReview = $payloadPacketReview
        approvals = $approvalRows.ToArray()
    }
}

function New-OperatorSignoffTemplate {
    param(
        [Parameter(Mandatory)][string[]]$RequiredOwners,
        [object]$PacketReview = $null
    )

    $template = [ordered]@{
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
        purpose = "DK1 operator sign-off for the Alpaca/Robinhood/Yahoo pilot parity packet."
        requiredOwners = $RequiredOwners
        instructions = @(
            "Only fill this file after Data Operations, Provider Reliability, and Trading have reviewed the DK1 packet.",
            "When packetReview is present, do not copy this sign-off file to another DK1 packet; regenerate the template for each reviewed packet.",
            "Each approval must include owner, signedBy, signedAtUtc, approved decision, and rationale.",
            "Run prepare-dk1-operator-signoff.ps1 -Validate before passing this file to run-wave1-provider-validation.ps1 or generate-dk1-pilot-parity-packet.ps1."
        )
    }

    if ($null -ne $PacketReview) {
        $template.packetReview = $PacketReview
    }

    $template.approvals = @(
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

    return $template
}

$packetReview = if ([string]::IsNullOrWhiteSpace($PacketPath)) {
    $null
}
else {
    Get-Dk1PacketReview -Path $PacketPath
}

Assert-PacketReadyForOperatorReview -PacketReview $packetReview

if ($Validate) {
    $validateStepStarted = $false
    if (Test-MeridianCheckpointStepShouldRun -Context $checkpoint -StepId "validate-signoff") {
        Start-MeridianCheckpointStep -Context $checkpoint -StepId "validate-signoff" -Description "Validate existing DK1 operator sign-off."
        $validateStepStarted = $true
    }
    $review = Get-OperatorSignoffReview -Path $OutputPath -RequiredOwners $requiredOperatorOwners -PacketReview $packetReview
    if ($Json) {
        $review | ConvertTo-Json -Depth 8
    }
    else {
        Write-Host "DK1 operator sign-off status: $($review.status)"
        Write-Host "Packet binding status: $($review.packetBindingStatus)"
        Write-Host "Signed owners: $(if ($review.signedOwners.Count -eq 0) { 'none' } else { $review.signedOwners -join ', ' })"
        Write-Host "Missing owners: $(if ($review.missingOwners.Count -eq 0) { 'none' } else { $review.missingOwners -join ', ' })"
    }

    if (-not $review.validForDk1Exit) {
        $packetBindingMissing = @($review.packetBindingMissingRequirements)
        $packetBindingDetail = if ($packetBindingMissing.Count -eq 0) {
            "none"
        }
        else {
            $packetBindingMissing -join ", "
        }
        throw "DK1 operator sign-off is not complete. Missing owners: $(if ($review.missingOwners.Count -eq 0) { 'none' } else { $review.missingOwners -join ', ' }); packet binding requirements: $packetBindingDetail"
    }
    if ($validateStepStarted) {
        Complete-MeridianCheckpointStep -Context $checkpoint -StepId "validate-signoff" -ArtifactPointers @($OutputPath)
    }

    return
}

if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Operator sign-off file already exists: $OutputPath. Re-run with -Force to replace it."
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$template = New-OperatorSignoffTemplate -RequiredOwners $requiredOperatorOwners -PacketReview $packetReview
if (Test-MeridianCheckpointStepShouldRun -Context $checkpoint -StepId "write-signoff-template") {
    Start-MeridianCheckpointStep -Context $checkpoint -StepId "write-signoff-template" -Description "Write DK1 operator sign-off template."
    $template | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    Complete-MeridianCheckpointStep -Context $checkpoint -StepId "write-signoff-template" -ArtifactPointers @($OutputPath)
}

if ($Json) {
    [ordered]@{
        path = ConvertTo-RelativePath -Path $OutputPath
        status = "template-written"
        requiredOwners = $requiredOperatorOwners
        packetReview = $packetReview
    } | ConvertTo-Json -Depth 8
}
else {
    Write-Host "DK1 operator sign-off template written to:"
    Write-Host "  $OutputPath"
}
