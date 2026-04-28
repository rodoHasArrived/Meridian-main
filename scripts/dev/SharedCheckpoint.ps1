Set-StrictMode -Version Latest

<<<<<<< HEAD
function Test-MeridianDictionaryContainsKey {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Dictionary,
        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    if ($Dictionary -is [System.Collections.IDictionary]) {
        return $Dictionary.Contains($Key)
    }

    if ($Dictionary.PSObject.Methods.Name -contains 'ContainsKey') {
        return $Dictionary.ContainsKey($Key)
    }

    return $false
}

=======
>>>>>>> b5c7c82199e6c9c4f6326de878307e78c9d5e790
function Get-MeridianCheckpointInputHash {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject
    )

    $json = $InputObject | ConvertTo-Json -Depth 20 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($bytes)
        return ([System.BitConverter]::ToString($hashBytes)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Save-MeridianCheckpointContext {
    param([Parameter(Mandatory = $true)]$Context)

    $payload = [ordered]@{
        checkpointConventionVersion = 1
        workflow = $Context.Workflow
        runId = $Context.Data.runId
        inputHash = $Context.Data.inputHash
        inputSnapshot = $Context.Data.inputSnapshot
        metadata = $Context.Data.metadata
        steps = $Context.Data.steps
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
    }

    $directory = Split-Path -Parent $Context.Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $payload | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Context.Path -Encoding utf8
}

function Initialize-MeridianCheckpoint {
    param(
        [Parameter(Mandatory = $true)][string]$Workflow,
        [Parameter(Mandatory = $true)][string]$CheckpointPath,
        [Parameter(Mandatory = $true)][object]$InputObject,
        [string[]]$ForceStep = @(),
        [switch]$AllowInputMismatch
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($CheckpointPath)
    $inputHash = Get-MeridianCheckpointInputHash -InputObject $InputObject
    $existing = $null

    if (Test-Path -LiteralPath $resolvedPath) {
        $existing = Get-Content -Raw -LiteralPath $resolvedPath | ConvertFrom-Json -AsHashtable
    }

    if ($null -ne $existing) {
<<<<<<< HEAD
        $existingHash = if (Test-MeridianDictionaryContainsKey -Dictionary $existing -Key 'inputHash') { [string]$existing.inputHash } else { '' }
=======
        $existingHash = if ($existing.ContainsKey('inputHash')) { [string]$existing.inputHash } else { '' }
>>>>>>> b5c7c82199e6c9c4f6326de878307e78c9d5e790
        if (-not [string]::Equals($existingHash, $inputHash, [System.StringComparison]::OrdinalIgnoreCase) -and -not $AllowInputMismatch) {
            throw "Checkpoint input hash mismatch for '$Workflow'. Existing: $existingHash, current: $inputHash. Re-run with -AllowCheckpointInputMismatch to override."
        }
    }

    $data = [ordered]@{
<<<<<<< HEAD
        runId = if ($null -ne $existing -and (Test-MeridianDictionaryContainsKey -Dictionary $existing -Key 'runId')) { [string]$existing.runId } else { [guid]::NewGuid().ToString('n') }
        inputHash = $inputHash
        inputSnapshot = $InputObject
        metadata = if ($null -ne $existing -and (Test-MeridianDictionaryContainsKey -Dictionary $existing -Key 'metadata')) { $existing.metadata } else { [ordered]@{} }
        steps = if ($null -ne $existing -and (Test-MeridianDictionaryContainsKey -Dictionary $existing -Key 'steps')) { $existing.steps } else { [ordered]@{} }
=======
        runId = if ($null -ne $existing -and $existing.ContainsKey('runId')) { [string]$existing.runId } else { [guid]::NewGuid().ToString('n') }
        inputHash = $inputHash
        inputSnapshot = $InputObject
        metadata = if ($null -ne $existing -and $existing.ContainsKey('metadata')) { [hashtable]$existing.metadata } else { [ordered]@{} }
        steps = if ($null -ne $existing -and $existing.ContainsKey('steps')) { [hashtable]$existing.steps } else { [ordered]@{} }
>>>>>>> b5c7c82199e6c9c4f6326de878307e78c9d5e790
    }

    $context = [pscustomobject]@{
        Workflow = $Workflow
        Path = $resolvedPath
        ForceStep = @($ForceStep | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        Data = $data
    }

    Save-MeridianCheckpointContext -Context $context
    return $context
}

function Test-MeridianCheckpointStepShouldRun {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$StepId
    )

    if (@($Context.ForceStep) -contains $StepId) {
        return $true
    }

<<<<<<< HEAD
    if (-not (Test-MeridianDictionaryContainsKey -Dictionary $Context.Data.steps -Key $StepId)) {
=======
    if (-not $Context.Data.steps.ContainsKey($StepId)) {
>>>>>>> b5c7c82199e6c9c4f6326de878307e78c9d5e790
        return $true
    }

    $status = [string]$Context.Data.steps[$StepId].state
    return $status -ne 'succeeded'
}

function Start-MeridianCheckpointStep {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$StepId,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $Context.Data.steps[$StepId] = [ordered]@{
        state = 'running'
        description = $Description
        timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
        inputHash = $Context.Data.inputHash
        artifactPointers = @()
    }
    Save-MeridianCheckpointContext -Context $Context
}

function Complete-MeridianCheckpointStep {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$StepId,
        [object[]]$ArtifactPointers = @()
    )

<<<<<<< HEAD
    if (-not (Test-MeridianDictionaryContainsKey -Dictionary $Context.Data.steps -Key $StepId)) {
=======
    if (-not $Context.Data.steps.ContainsKey($StepId)) {
>>>>>>> b5c7c82199e6c9c4f6326de878307e78c9d5e790
        throw "Cannot complete checkpoint step '$StepId' because it was not started."
    }

    $Context.Data.steps[$StepId].state = 'succeeded'
    $Context.Data.steps[$StepId].timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
    $Context.Data.steps[$StepId].artifactPointers = @($ArtifactPointers)
    Save-MeridianCheckpointContext -Context $Context
}

function Fail-MeridianCheckpointStep {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$StepId,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $Context.Data.steps[$StepId] = [ordered]@{
        state = 'failed'
        timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
        inputHash = $Context.Data.inputHash
        error = $Message
        artifactPointers = @()
    }
    Save-MeridianCheckpointContext -Context $Context
}
<<<<<<< HEAD
=======

>>>>>>> b5c7c82199e6c9c4f6326de878307e78c9d5e790
