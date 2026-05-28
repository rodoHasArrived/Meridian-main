module Meridian.FSharp.Tests.ReportPackValidationRulesTests

open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Operations

[<Fact>]
let ``Report pack validation emits deterministic issue rules for missing evidence`` () =
    let facts =
        {
            RunCount = 0
            TrialBalanceCount = 0
            AssetClassSectionCount = 0
            SecurityMissingCount = 2
            OpenReconciliationBreakCount = 1
            StaleReplayCount = 1
            UnresolvedSecurityMasterConflictCount = 1
            JournalEntryCount = 0
            LedgerEntryCount = 0
            FormatCount = 0
        }

    let issues = ReportPackValidationInterop.Validate(facts)

    issues |> Array.map _.Code |> should contain "report-pack.no-contributing-runs"
    issues |> Array.map _.Code |> should contain "report-pack.open-reconciliation-breaks"
    issues |> Array.map _.Code |> should contain "report-pack.no-export-formats"
    issues |> Array.exists (fun issue -> issue.Severity = "Critical") |> should equal true

[<Fact>]
let ``Report pack validation emits no base issues for complete facts`` () =
    let facts =
        {
            RunCount = 1
            TrialBalanceCount = 1
            AssetClassSectionCount = 1
            SecurityMissingCount = 0
            OpenReconciliationBreakCount = 0
            StaleReplayCount = 0
            UnresolvedSecurityMasterConflictCount = 0
            JournalEntryCount = 1
            LedgerEntryCount = 1
            FormatCount = 1
        }

    ReportPackValidationInterop.Validate(facts) |> should be Empty
