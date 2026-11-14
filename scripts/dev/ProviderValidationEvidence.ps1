Set-StrictMode -Version Latest

function Get-MeridianProviderTestEvidence {
    param(
        [Parameter(Mandatory)][string]$TrxPath,
        [string[]]$ExpectedEvidence = @()
    )

    $receipt = [ordered]@{
        success = $false
        total = 0
        executed = 0
        passed = 0
        failed = 0
        evidenceCounts = [ordered]@{}
        errors = @()
    }
    foreach ($anchor in $ExpectedEvidence) { $receipt.evidenceCounts[$anchor] = 0 }

    try {
        if (-not (Test-Path -LiteralPath $TrxPath)) { throw 'Test result file is missing.' }
        $settings = [System.Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $reader = [System.Xml.XmlReader]::Create($TrxPath, $settings)
        try {
            $document = [System.Xml.XmlDocument]::new()
            $document.XmlResolver = $null
            $document.Load($reader)
        }
        finally { $reader.Dispose() }

        $ns = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
        $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $counters = $document.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters', $ns)
        if ($null -eq $counters) { throw 'Test result counters are missing.' }
        foreach ($name in @('total', 'executed', 'passed', 'failed')) {
            $value = 0
            if (-not [int]::TryParse($counters.GetAttribute($name), [ref]$value) -or $value -lt 0) {
                throw 'Test result counters are invalid.'
            }
            $receipt[$name] = $value
        }

        $summary = $document.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)
        if ($summary.GetAttribute('outcome') -in @('Failed', 'Aborted', 'Error', 'Timeout') -or
            $document.SelectNodes('/t:TestRun/t:ResultSummary/t:RunInfos/t:RunInfo[@outcome="Error"]', $ns).Count -gt 0) {
            $receipt.errors += 'The test run reported a run-level failure.'
        }

        $results = @($document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
        $passedResults = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'Passed' })
        if ($receipt.executed -eq 0 -or $receipt.total -ne $receipt.executed -or
            $receipt.executed -ne $receipt.passed -or $receipt.failed -ne 0 -or
            $results.Count -ne $receipt.total -or $passedResults.Count -ne $receipt.passed) {
            $receipt.errors += 'The step must execute at least one test, with all tests passed and no skipped results.'
        }

        $definitions = @{}
        foreach ($definition in $document.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest', $ns)) {
            $method = $definition.SelectSingleNode('t:TestMethod', $ns)
            if ($null -ne $method) {
                $definitions[$definition.GetAttribute('id')] = "$($method.GetAttribute('className')).$($method.GetAttribute('name'))"
            }
        }
        foreach ($result in $passedResults) {
            $id = $result.GetAttribute('testId')
            if (-not $definitions.ContainsKey($id)) { continue }
            foreach ($anchor in $ExpectedEvidence) {
                if ($definitions[$id].Contains($anchor, [System.StringComparison]::Ordinal)) {
                    $receipt.evidenceCounts[$anchor]++
                }
            }
        }
        foreach ($anchor in $ExpectedEvidence) {
            if ($receipt.evidenceCounts[$anchor] -eq 0) {
                $receipt.errors += "No passed test matched claimed evidence '$anchor'."
            }
        }
        $receipt.success = $receipt.errors.Count -eq 0
    }
    catch {
        # Do not copy provider/test exception text or XML payloads into public receipts.
        $receipt.errors += 'The test result file is missing, malformed, or has invalid counters.'
    }
    return $receipt
}
