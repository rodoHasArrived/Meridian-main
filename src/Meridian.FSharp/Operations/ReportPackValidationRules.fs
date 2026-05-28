namespace Meridian.FSharp.Operations

[<CLIMutable>]
type ReportPackValidationFacts =
    {
        RunCount: int
        TrialBalanceCount: int
        AssetClassSectionCount: int
        SecurityMissingCount: int
        OpenReconciliationBreakCount: int
        StaleReplayCount: int
        UnresolvedSecurityMasterConflictCount: int
        JournalEntryCount: int
        LedgerEntryCount: int
        FormatCount: int
    }

[<CLIMutable>]
type ReportPackValidationIssueRuleDto =
    {
        Code: string
        Severity: string
        Title: string
        Message: string
        AffectedSection: string
        SuggestedAction: string
        EvidenceLink: string
    }

module ReportPackValidationRules =

    let private issue code severity title message section action evidence =
        {
            Code = code
            Severity = severity
            Title = title
            Message = message
            AffectedSection = section
            SuggestedAction = action
            EvidenceLink = evidence
        }

    let validate (facts: ReportPackValidationFacts) =
        [|
            if facts.RunCount = 0 then
                issue
                    "report-pack.no-contributing-runs"
                    "Warning"
                    "No contributing runs"
                    "No recorded fund-scoped runs contributed ledger data for this report pack."
                    ""
                    "Record or select a fund-scoped strategy or paper run before approving the report pack."
                    "/workstation/strategy/runs"

            if facts.TrialBalanceCount = 0 then
                issue
                    "report-pack.empty-trial-balance"
                    "Critical"
                    "Empty trial balance"
                    "The generated report pack contains no trial-balance rows."
                    "trial-balance"
                    "Confirm ledger postings are available for the reporting period before regenerating the pack."
                    "/workstation/accounting"

            if facts.AssetClassSectionCount = 0 then
                issue
                    "report-pack.empty-asset-class-sections"
                    "Warning"
                    "No asset-class sections"
                    "The generated report pack contains no asset-class sections."
                    "asset-class-sections"
                    "Resolve ledger and Security Master classification inputs before report review."
                    "/workstation/reporting/report-packs"

            if facts.SecurityMissingCount > 0 then
                issue
                    "report-pack.missing-security-master-classification"
                    "Warning"
                    "Missing Security Master classification"
                    (sprintf "%d trial-balance row(s) could not be resolved through Security Master." facts.SecurityMissingCount)
                    "trial-balance"
                    "Complete Security Master classification for unresolved report symbols."
                    "/workstation/data/security-master"

            if facts.OpenReconciliationBreakCount > 0 then
                issue
                    "report-pack.open-reconciliation-breaks"
                    "Critical"
                    "Open reconciliation breaks"
                    (sprintf "%d open reconciliation break(s) were present at generation time." facts.OpenReconciliationBreakCount)
                    "reconciliation"
                    "Resolve or approve reconciliation breaks before final report-pack sign-off."
                    "/workstation/accounting/reconciliation"

            if facts.StaleReplayCount > 0 then
                issue
                    "report-pack.stale-replay-evidence"
                    "Critical"
                    "Stale replay evidence"
                    (sprintf "%d replay verification evidence item(s) are stale for this report pack context." facts.StaleReplayCount)
                    "execution-replay"
                    "Re-run paper replay verification and regenerate report-pack evidence before publishing."
                    "/workstation/trading/readiness"

            if facts.UnresolvedSecurityMasterConflictCount > 0 then
                issue
                    "report-pack.unresolved-security-master-conflicts"
                    "Critical"
                    "Unresolved Security Master conflicts"
                    (sprintf "%d unresolved Security Master conflict(s) block publication readiness." facts.UnresolvedSecurityMasterConflictCount)
                    "security-master"
                    "Resolve Security Master conflicts and refresh downstream report-pack lineage evidence."
                    "/workstation/data/security-master"

            if facts.JournalEntryCount = 0 || facts.LedgerEntryCount = 0 then
                issue
                    "report-pack.missing-ledger-postings"
                    "Critical"
                    "Missing ledger postings"
                    "The report pack was generated without journal or ledger entries in the source snapshot."
                    "ledger"
                    "Confirm ledger posting and period-close inputs before approving the report pack."
                    "/workstation/accounting"

            if facts.FormatCount = 0 then
                issue
                    "report-pack.no-export-formats"
                    "Critical"
                    "No export formats"
                    "The report pack has no requested export artifact formats."
                    ""
                    "Regenerate the report pack with at least one supported artifact format."
                    "/workstation/reporting/report-packs"
        |]

[<Sealed>]
type ReportPackValidationInterop private () =

    static member Validate(facts: ReportPackValidationFacts) =
        ReportPackValidationRules.validate facts
