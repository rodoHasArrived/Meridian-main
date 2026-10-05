# Shared recovery receipt parsing and objective evaluation. Archive speed is not RPO/RTO.

function ConvertFrom-RecoveryJsonElement {
    param([System.Text.Json.JsonElement]$Element)

    switch ($Element.ValueKind.ToString()) {
        'Object' {
            $result = [ordered]@{}
            foreach ($property in $Element.EnumerateObject()) {
                if ($result.Contains($property.Name)) {
                    throw "Duplicate recovery JSON property: $($property.Name)"
                }
                $result[$property.Name] = ConvertFrom-RecoveryJsonElement $property.Value
            }
            return $result
        }
        'Array' {
            $items = @($Element.EnumerateArray() | ForEach-Object { ConvertFrom-RecoveryJsonElement $_ })
            return ,$items
        }
        'String' { return $Element.GetString() }
        'Number' { return $Element.GetDouble() }
        'True' { return $true }
        'False' { return $false }
        'Null' { return $null }
        default { throw "Unsupported recovery JSON value: $($Element.ValueKind)" }
    }
}

function Read-RecoveryJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    # ConvertFrom-Json automatically converts ISO timestamps to DateTime on PS 7.4;
    # its -DateKind String option is only available from PS 7.5. JsonDocument keeps
    # the original strings on both versions, including their required UTC suffix.
    $document = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($Path))
    try {
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw 'Recovery evidence must be a JSON object.'
        }
        return ConvertFrom-RecoveryJsonElement $document.RootElement
    }
    finally { $document.Dispose() }
}

function Test-RecoveryNumber {
    param($Value)
    if ($null -eq $Value) { return $false }
    $numericTypes = @('SByte', 'Byte', 'Int16', 'UInt16', 'Int32', 'UInt32', 'Int64', 'UInt64', 'Single', 'Double', 'Decimal')
    return [Type]::GetTypeCode($Value.GetType()).ToString() -in $numericTypes -and [double]::IsFinite([double]$Value)
}

function Test-RecoveryCommit {
    param($Value)
    # Keep the input untyped so JSON arrays/numbers cannot be coerced to strings.
    # Absolute anchors also reject a trailing newline accepted by the $ anchor.
    return $Value -is [string] -and $Value -cmatch '\A(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})\z'
}

function Get-RecoveryObjectiveEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Receipt,
        [ValidateRange(0.0000001, 3600)][double]$MaximumRpoSeconds = 3600,
        [ValidateRange(0.0000001, 7200)][double]$MaximumRtoSeconds = 7200
    )

    $issues = [Collections.Generic.List[object]]::new()
    $breaches = [Collections.Generic.List[string]]::new()
    $timestamps = @{}
    $now = [DateTimeOffset]::UtcNow
    if (-not (Test-RecoveryNumber $Receipt['schemaVersion']) -or $Receipt['schemaVersion'] -ne 2) {
        $issues.Add(@{ scope = 'common'; message = 'schemaVersion must be 2; archive-only legacy receipts do not prove recovery objectives.' })
    }
    if ($Receipt['status'] -isnot [string] -or $Receipt['status'] -cne 'passed') {
        $issues.Add(@{ scope = 'common'; message = 'Archive operation status must be passed.' })
    }
    if ($Receipt['mode'] -isnot [string] -or $Receipt['mode'] -ne 'Drill') {
        $issues.Add(@{ scope = 'common'; message = 'Objective proof requires a Drill receipt with recorded loss milestones.' })
    }
    if ($Receipt['manifestAuthenticated'] -isnot [bool] -or -not $Receipt['manifestAuthenticated']) {
        $issues.Add(@{ scope = 'common'; message = 'manifestAuthenticated must be true; unauthenticated backup metadata cannot prove recovery objectives.' })
    }
    if ($Receipt['manifestSha256'] -isnot [string] -or $Receipt['manifestSha256'] -cnotmatch '^[0-9a-fA-F]{64}$') {
        $issues.Add(@{ scope = 'common'; message = 'manifestSha256 must identify the authenticated manifest bytes.' })
    }
    foreach ($field in @('sourceCommit', 'drillSourceCommit')) {
        if (-not (Test-RecoveryCommit $Receipt[$field])) {
            $issues.Add(@{ scope = 'common'; message = "$field must be a full 40- or 64-character hexadecimal commit identifier." })
        }
    }
    foreach ($field in @('backupId', 'recoverablePointEvidence', 'reconciliationEvidence', 'operatorAcceptedBy', 'operatorAcceptanceEvidence')) {
        $scope = switch ($field) {
            'recoverablePointEvidence' { 'rpo' }
            { $_ -in @('reconciliationEvidence', 'operatorAcceptedBy', 'operatorAcceptanceEvidence') } { 'rto' }
            default { 'common' }
        }
        if ($Receipt[$field] -isnot [string] -or [string]::IsNullOrWhiteSpace($Receipt[$field])) {
            $issues.Add(@{ scope = $scope; message = "$field must be a nonblank string." })
        }
    }

    $timestampScopes = [ordered]@{
        startedAtUtc = 'common'; completedAtUtc = 'common'
        backupStartedAtUtc = 'rpo'; backupCompletedAtUtc = 'rpo'
        restoreStartedAtUtc = 'rto'; restoreCompletedAtUtc = 'rto'
        lastVerifiedRecoverablePointAtUtc = 'rpo'; recoverablePointVerifiedAtUtc = 'rpo'
        simulatedLossAtUtc = 'common'; lossDeclaredAtUtc = 'rto'
        reconciliationCompletedAtUtc = 'rto'; operatorAcceptedAtUtc = 'rto'
    }
    foreach ($field in $timestampScopes.Keys) {
        $value = $Receipt[$field]
        $parsed = [DateTimeOffset]::MinValue
        if ($value -isnot [string] -or $value -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$' -or
            -not [DateTimeOffset]::TryParse($value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed)) {
            $issues.Add(@{ scope = $timestampScopes[$field]; message = "$field must be a valid UTC ISO-8601 timestamp string ending in Z or +00:00." })
        }
        elseif ($parsed -gt $now) {
            $issues.Add(@{ scope = $timestampScopes[$field]; message = "$field cannot be in the future." })
        }
        else { $timestamps[$field] = $parsed }
    }

    $orders = @(
        @('startedAtUtc', 'completedAtUtc', 'common'),
        @('startedAtUtc', 'recoverablePointVerifiedAtUtc', 'rpo'),
        @('startedAtUtc', 'simulatedLossAtUtc', 'common'),
        @('startedAtUtc', 'restoreStartedAtUtc', 'rto'),
        @('lastVerifiedRecoverablePointAtUtc', 'backupStartedAtUtc', 'rpo'),
        @('backupStartedAtUtc', 'backupCompletedAtUtc', 'rpo'),
        @('backupCompletedAtUtc', 'recoverablePointVerifiedAtUtc', 'rpo'),
        @('lastVerifiedRecoverablePointAtUtc', 'recoverablePointVerifiedAtUtc', 'rpo'),
        @('recoverablePointVerifiedAtUtc', 'simulatedLossAtUtc', 'rpo'),
        @('simulatedLossAtUtc', 'lossDeclaredAtUtc', 'rto'),
        @('lossDeclaredAtUtc', 'restoreStartedAtUtc', 'rto'),
        @('restoreStartedAtUtc', 'restoreCompletedAtUtc', 'rto'),
        @('restoreCompletedAtUtc', 'completedAtUtc', 'common'),
        @('restoreCompletedAtUtc', 'reconciliationCompletedAtUtc', 'rto'),
        @('reconciliationCompletedAtUtc', 'operatorAcceptedAtUtc', 'rto')
    )
    foreach ($order in $orders) {
        if ($timestamps.ContainsKey($order[0]) -and $timestamps.ContainsKey($order[1]) -and $timestamps[$order[0]] -gt $timestamps[$order[1]]) {
            $issues.Add(@{ scope = $order[2]; message = "$($order[0]) must not be after $($order[1])." })
        }
    }

    foreach ($operation in @('backup', 'restore')) {
        $scope = if ($operation -eq 'backup') { 'rpo' } else { 'rto' }
        $durationField = "${operation}DurationSeconds"
        $startedField = "${operation}StartedAtUtc"
        $completedField = "${operation}CompletedAtUtc"
        $duration = $Receipt[$durationField]
        if (-not (Test-RecoveryNumber $duration) -or [double]$duration -lt 0) {
            $issues.Add(@{ scope = $scope; message = "$durationField must be a finite nonnegative number." })
        }
        elseif ($timestamps.ContainsKey($startedField) -and $timestamps.ContainsKey($completedField)) {
            $actual = ($timestamps[$completedField] - $timestamps[$startedField]).TotalSeconds
            # Producers may round archive durations to milliseconds. Objective
            # comparisons below use unrounded timestamp differences.
            if ([Math]::Abs([double]$duration - $actual) -gt 0.001) {
                $issues.Add(@{ scope = $scope; message = "$durationField does not match its archive operation timestamps." })
            }
        }
    }

    $effectiveRpo = $MaximumRpoSeconds
    $effectiveRto = $MaximumRtoSeconds
    foreach ($objective in @('rpo', 'rto')) {
        $budgetField = if ($objective -eq 'rpo') { 'maximumRpoSeconds' } else { 'maximumRtoSeconds' }
        $budget = $Receipt[$budgetField]
        if (-not (Test-RecoveryNumber $budget) -or [double]$budget -le 0) {
            $issues.Add(@{ scope = $objective; message = "$budgetField must be a finite positive number." })
        }
        elseif ($objective -eq 'rpo') { $effectiveRpo = [Math]::Min($effectiveRpo, [double]$budget) }
        else { $effectiveRto = [Math]::Min($effectiveRto, [double]$budget) }
    }

    $measuredRpo = $null
    $measuredRto = $null
    if ($timestamps.ContainsKey('simulatedLossAtUtc') -and $timestamps.ContainsKey('lastVerifiedRecoverablePointAtUtc') -and
        $timestamps['simulatedLossAtUtc'] -ge $timestamps['lastVerifiedRecoverablePointAtUtc']) {
        $measuredRpo = ($timestamps['simulatedLossAtUtc'] - $timestamps['lastVerifiedRecoverablePointAtUtc']).TotalSeconds
    }
    if ($timestamps.ContainsKey('operatorAcceptedAtUtc') -and $timestamps.ContainsKey('lossDeclaredAtUtc') -and
        $timestamps['operatorAcceptedAtUtc'] -ge $timestamps['lossDeclaredAtUtc']) {
        $measuredRto = ($timestamps['operatorAcceptedAtUtc'] - $timestamps['lossDeclaredAtUtc']).TotalSeconds
    }
    $rpoStatus = 'unproven'
    $rtoStatus = 'unproven'
    if ($null -ne $measuredRpo -and @($issues | Where-Object { $_.scope -in @('common', 'rpo') }).Count -eq 0) {
        $rpoStatus = if ($measuredRpo -gt $effectiveRpo) { 'breached' } else { 'proven' }
    }
    if ($null -ne $measuredRto -and @($issues | Where-Object { $_.scope -in @('common', 'rto') }).Count -eq 0) {
        $rtoStatus = if ($measuredRto -gt $effectiveRto) { 'breached' } else { 'proven' }
    }
    if ($null -ne $measuredRpo -and $measuredRpo -gt $effectiveRpo) {
        $breaches.Add("Recoverable-point age $measuredRpo seconds exceeds RPO budget $effectiveRpo seconds.")
    }
    if ($null -ne $measuredRto -and $measuredRto -gt $effectiveRto) {
        $breaches.Add("Declared-loss-to-operator-acceptance time $measuredRto seconds exceeds RTO budget $effectiveRto seconds.")
    }
    $objectiveStatus = if ($issues.Count -gt 0) { 'unproven' } elseif ($breaches.Count -gt 0) { 'breached' } else { 'proven' }
    return [ordered]@{
        objectiveStatus = $objectiveStatus
        rpoStatus = $rpoStatus
        rtoStatus = $rtoStatus
        measuredRpoSeconds = $measuredRpo
        measuredRtoSeconds = $measuredRto
        effectiveMaximumRpoSeconds = $effectiveRpo
        effectiveMaximumRtoSeconds = $effectiveRto
        objectiveErrors = @(@($issues | ForEach-Object { $_.message }) + @($breaches))
    }
}
