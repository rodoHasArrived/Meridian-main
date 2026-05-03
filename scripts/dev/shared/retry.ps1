Set-StrictMode -Version Latest

function New-MeridianRetryFailure {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Code,

        [Parameter(Mandatory = $true)]
        [string]$Reason,

        [hashtable]$Data = @{}
    )

    return [pscustomobject]@{
        code = $Code
        reason = $Reason
        data = $Data
    }
}

function Invoke-MeridianRetry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Predicate,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,

        [int]$MaxAttempts = 5,
        [int]$BaseDelayMs = 250,
        [int]$MaxDelayMs = 4000,
        [double]$BackoffMultiplier = 2.0,
        [int]$JitterMs = 120,
        [System.Collections.IList]$TelemetrySink
    )

    if ($MaxAttempts -lt 1) {
        throw "Invoke-MeridianRetry requires MaxAttempts >= 1."
    }

    $startedAt = Get-Date
    $attemptEvents = @()
    $lastFailure = $null
    $result = $null

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $attemptStarted = Get-Date
        $predicateState = $null
        $predicateFailure = $null

        try {
            $predicateState = & $Predicate
        }
        catch {
            $predicateFailure = New-MeridianRetryFailure `
                -Code 'retry.predicate.exception' `
                -Reason $_.Exception.Message `
                -Data @{ exceptionType = $_.Exception.GetType().FullName }
        }

        $predicatePassed = $false
        if ($null -eq $predicateFailure) {
            if ($predicateState -is [bool]) {
                $predicatePassed = $predicateState
            }
            elseif ($null -ne $predicateState -and $predicateState.PSObject.Properties.Name -contains 'ready') {
                $predicatePassed = [bool]$predicateState.ready
            }
            else {
                $predicatePassed = $null -ne $predicateState
            }
        }

        if (-not $predicatePassed) {
            if ($null -eq $predicateFailure) {
                if ($null -ne $predicateState -and $predicateState.PSObject.Properties.Name -contains 'failure' -and $null -ne $predicateState.failure) {
                    $predicateFailure = $predicateState.failure
                }
                else {
                    $predicateFailure = New-MeridianRetryFailure `
                        -Code 'retry.predicate.not_ready' `
                        -Reason 'Readiness predicate did not pass.' `
                        -Data @{
                            state = $predicateState
                        }
                }
            }

            $lastFailure = $predicateFailure
            $attemptEvents += [pscustomobject]@{
                attempt = $attempt
                phase = 'predicate'
                status = 'retry'
                code = $predicateFailure.code
                reason = $predicateFailure.reason
                timestamp = $attemptStarted.ToString('o')
            }
        }
        else {
            try {
                $result = & $Action $predicateState
                $attemptEvents += [pscustomobject]@{
                    attempt = $attempt
                    phase = 'action'
                    status = 'ok'
                    code = $null
                    reason = $null
                    timestamp = (Get-Date).ToString('o')
                }

                $event = [pscustomobject]@{
                    name = $Name
                    status = 'ok'
                    attempts = $attempt
                    startedAt = $startedAt.ToString('o')
                    finishedAt = (Get-Date).ToString('o')
                    durationMs = [int]((Get-Date) - $startedAt).TotalMilliseconds
                    attemptsDetail = $attemptEvents
                    failure = $null
                }

                if ($TelemetrySink) {
                    [void]$TelemetrySink.Add($event)
                }

                return [pscustomobject]@{
                    Success = $true
                    Attempts = $attempt
                    Value = $result
                    Telemetry = $event
                    Failure = $null
                }
            }
            catch {
                $lastFailure = New-MeridianRetryFailure `
                    -Code 'retry.action.exception' `
                    -Reason $_.Exception.Message `
                    -Data @{ exceptionType = $_.Exception.GetType().FullName }

                $attemptEvents += [pscustomobject]@{
                    attempt = $attempt
                    phase = 'action'
                    status = 'retry'
                    code = $lastFailure.code
                    reason = $lastFailure.reason
                    timestamp = (Get-Date).ToString('o')
                }
            }
        }

        if ($attempt -lt $MaxAttempts) {
            $expDelay = [Math]::Min($MaxDelayMs, [int]($BaseDelayMs * [Math]::Pow($BackoffMultiplier, $attempt - 1)))
            $jitter = if ($JitterMs -gt 0) { Get-Random -Minimum 0 -Maximum ($JitterMs + 1) } else { 0 }
            Start-Sleep -Milliseconds ($expDelay + $jitter)
        }
    }

    $failureEvent = [pscustomobject]@{
        name = $Name
        status = 'failed'
        attempts = $MaxAttempts
        startedAt = $startedAt.ToString('o')
        finishedAt = (Get-Date).ToString('o')
        durationMs = [int]((Get-Date) - $startedAt).TotalMilliseconds
        attemptsDetail = $attemptEvents
        failure = $lastFailure
    }

    if ($TelemetrySink) {
        [void]$TelemetrySink.Add($failureEvent)
    }

    return [pscustomobject]@{
        Success = $false
        Attempts = $MaxAttempts
        Value = $null
        Telemetry = $failureEvent
        Failure = $lastFailure
    }
}
