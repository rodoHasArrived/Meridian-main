using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Contracts.Integrity;

namespace Meridian.Wpf.ViewModels.Accounting;

/// <summary>Pure presentation projections for the AccountingConfigureViewModel workspace; no mutable view-model state.</summary>
internal static class AccountingWorkbenchPresentation
{

    internal static string FormatEffectiveWindow(DateOnly? effectiveFrom, DateOnly? effectiveTo)
    {
        var from = effectiveFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "open";
        var to = effectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "open";
        return $"{from} to {to}";
    }

    internal static IEnumerable<AccountingWorkbenchRow> BuildLedgerBookWorkflowRows(
        AccountingLedgerBookWorkflowReadinessDto? readiness)
    {
        if (readiness is null)
        {
            yield return new AccountingWorkbenchRow(
                "Ledger-book workflow controls",
                "Unavailable",
                "Shared ledger-book workflow readiness was not returned.",
                "Register production-readiness assessment before certifying workflow lanes.",
                "ledger-book-workflows:unavailable");
            yield break;
        }

        yield return BuildReadinessControlRow(
            "Ledger-book scope",
            readiness.HasLedgerBookScope,
            readiness.LedgerBookId.HasValue ? $"Ledger book {readiness.LedgerBookId.Value:D}" : "Missing ledger-book scope.",
            "Select or create the governed ledger book before certifying workflow controls.",
            "ledger-book-workflows:scope",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Ledger-book scoped evidence",
            readiness.HasLedgerBookScopedEvidence,
            "Retained evidence names the selected ledger book.",
            "Attach ledger-book-scoped workflow certification evidence.",
            "ledger-book-workflows:evidence",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Posting rules",
            readiness.PostingRulesLedgerBookNativeCertified && readiness.HasPostingRulesLedgerBookNativeEvidence,
            "Posting rules and Rules Studio execution are certified for the selected book.",
            "Retain posting-rules, Rules Studio, or posting-candidate evidence for the selected book.",
            "ledger-book-workflows:posting-rules",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Journal lifecycle",
            readiness.JournalLifecycleLedgerBookNativeCertified && readiness.HasJournalLifecycleLedgerBookNativeEvidence,
            "Manual journal lifecycle is certified for the selected book.",
            "Retain journal-lifecycle or manual-journal evidence for the selected book.",
            "ledger-book-workflows:journal-lifecycle",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Close/reporting",
            readiness.CloseReportingLedgerBookNativeCertified && readiness.HasCloseReportingLedgerBookNativeEvidence,
            "Close management and report packages are certified for the selected book.",
            "Retain close-management, report-package, or restatement evidence for the selected book.",
            "ledger-book-workflows:close-reporting",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Close-plan setup",
            readiness.ClosePlanConfigurationLedgerBookNativeCertified && readiness.HasClosePlanConfigurationLedgerBookNativeEvidence,
            "Close-plan configuration is certified for the selected book.",
            "Retain close-plan configuration, setup, checklist, dependency, sign-off, or materiality evidence for the selected book.",
            "ledger-book-workflows:close-plan-configuration",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "External GL",
            readiness.ExternalGlLedgerBookNativeCertified && readiness.HasExternalGlLedgerBookNativeEvidence,
            "External GL import/export review is certified for the selected book.",
            "Retain external-GL import/export evidence for the selected book.",
            "ledger-book-workflows:external-gl",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Reconciliation",
            readiness.ReconciliationLedgerBookNativeCertified && readiness.HasReconciliationLedgerBookNativeEvidence,
            "Reconciliation workflows are certified for the selected book.",
            "Retain reconciliation or break-queue evidence for the selected book.",
            "ledger-book-workflows:reconciliation",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Direct lending projections",
            readiness.DirectLendingLedgerBookNativeCertified && readiness.HasDirectLendingLedgerBookNativeEvidence,
            "Direct-lending projections are certified for the selected book.",
            "Retain direct-lending projection evidence for the selected book.",
            "ledger-book-workflows:direct-lending",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Strategy ledger reads",
            readiness.StrategyLedgerReadLedgerBookNativeCertified && readiness.HasStrategyLedgerReadLedgerBookNativeEvidence,
            "Strategy ledger reads are certified for the selected book.",
            "Retain strategy-run or run-ledger evidence for the selected book.",
            "ledger-book-workflows:strategy-ledger",
            readiness.EvidenceReferences);
    }

    internal static IEnumerable<AccountingWorkbenchRow> BuildProductionGapRows(
        IReadOnlyList<AccountingProductionGapDto> gaps)
    {
        foreach (var gap in gaps)
        {
            var areaText = gap.Areas.Count > 0
                ? string.Join(", ", gap.Areas.Select(static area => FormatProductionReadinessArea(area.ToString())))
                : "No readiness area";
            var blockerText = gap.BlockingIssueCodes.Count > 0
                ? string.Join(", ", gap.BlockingIssueCodes)
                : "No blocking issue codes";
            var issueText = gap.Issues.Count > 0
                ? string.Join(" | ", gap.Issues.Select(static issue => $"{issue.Code}: {issue.Message} -> {issue.SuggestedAction}"))
                : blockerText;
            var routeText = gap.Routes.Count > 0
                ? string.Join(", ", gap.Routes)
                : "No route";

            yield return new AccountingWorkbenchRow(
                gap.Label,
                $"{gap.Status} | {gap.HighestSeverity}",
                $"{gap.Summary} Areas: {areaText}.",
                $"{gap.RequiredAction} | {issueText}",
                $"{gap.Code} | {routeText}");
        }
    }

    internal static IEnumerable<AccountingWorkbenchRow> BuildDimensionalReportingRows(
        AccountingDimensionalReportingReadinessDto? readiness)
    {
        if (readiness is null)
        {
            yield return new AccountingWorkbenchRow(
                "Dimensional reporting controls",
                "Unavailable",
                "Shared dimensional reporting readiness was not returned.",
                "Register production-readiness assessment before certifying dimensional reporting lanes.",
                "dimensional-reporting:unavailable");
            yield break;
        }

        yield return BuildReadinessControlRow(
            "Ledger-book scope",
            readiness.HasLedgerBookScope,
            readiness.LedgerBookId.HasValue ? $"Ledger book {readiness.LedgerBookId.Value:D}" : "Missing ledger-book scope.",
            "Select or create the governed ledger book before certifying dimensional reporting.",
            "dimensional-reporting:scope",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Ledger-book scoped evidence",
            readiness.HasLedgerBookScopedEvidence,
            "Retained evidence names the selected ledger book.",
            "Attach ledger-book-scoped dimensional certification evidence.",
            "dimensional-reporting:evidence",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Period reports",
            readiness.PeriodReportDimensionQueriesCertified && readiness.HasPeriodReportDimensionQueryEvidence,
            "Period reports retain dimensional query evidence.",
            "Retain trial-balance, financial-statement, NAV, or investor-package dimensional evidence.",
            "dimensional-reporting:period-reports",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Cross-period reports",
            readiness.CrossPeriodReportDimensionQueriesCertified && readiness.HasCrossPeriodReportDimensionQueryEvidence,
            "Cross-period reports retain dimensional query evidence.",
            "Retain comparative, roll-forward, or cross-period dimensional evidence.",
            "dimensional-reporting:cross-period",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Journal filters",
            readiness.JournalQueryDimensionFiltersCertified && readiness.HasJournalQueryDimensionFilterEvidence,
            "Journal query filters retain dimensional scope.",
            "Retain journal-query, journal-filter, or ledger-journal dimensional evidence.",
            "dimensional-reporting:journal-filters",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "External export mappings",
            readiness.ExternalExportDimensionMappingCertified && readiness.HasExternalExportDimensionMappingEvidence,
            "External exports retain certified dimension mappings.",
            "Retain external-export or external-GL mapping dimensional evidence.",
            "dimensional-reporting:external-export",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Ledger-line persistence",
            readiness.LedgerLineDimensionsPersistedCertified && readiness.HasLedgerLineDimensionPersistenceEvidence,
            "Posted ledger lines retain canonical dimensions.",
            "Retain ledger-line or journal-line dimension persistence evidence.",
            "dimensional-reporting:ledger-lines",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Trial-balance filters",
            readiness.TrialBalanceDimensionFiltersCertified && readiness.HasTrialBalanceDimensionFilterEvidence,
            "Trial-balance filters retain dimensional scope.",
            "Retain trial-balance dimension-filter or ledger-report-filter evidence.",
            "dimensional-reporting:trial-balance",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Report package provenance",
            readiness.ReportPackageDimensionProvenanceCertified && readiness.HasReportPackageDimensionProvenanceEvidence,
            "Report package provenance retains canonical dimensions.",
            "Retain report-line provenance, package-dimension, or NAV package evidence.",
            "dimensional-reporting:report-package",
            readiness.EvidenceReferences);
    }

    internal static AccountingWorkbenchRow BuildReadinessControlRow(
        string name,
        bool complete,
        string readyDetail,
        string missingAction,
        string key,
        IReadOnlyList<string> evidenceReferences)
        => new(
            name,
            complete ? "Complete" : "Missing",
            complete ? readyDetail : missingAction,
            evidenceReferences.Count > 0 ? string.Join("; ", evidenceReferences.Take(4)) : "No retained evidence.",
            key);

    internal static string FormatProductionReadinessArea(string area)
        => area
            .Replace("ExternalGl", "External GL", StringComparison.Ordinal)
            .Replace("RulesStudio", "Rules Studio", StringComparison.Ordinal)
            .Replace("PostingRules", "Posting Rules", StringComparison.Ordinal)
            .Replace("JournalLifecycle", "Journal Lifecycle", StringComparison.Ordinal)
            .Replace("DimensionalAccounting", "Dimensional Accounting", StringComparison.Ordinal)
            .Replace("CloseReporting", "Close Reporting", StringComparison.Ordinal)
            .Replace("TenantAdministration", "Tenant Administration", StringComparison.Ordinal)
            .Replace("MigrationRollout", "Migration Rollout", StringComparison.Ordinal)
            .Replace("LedgerBooks", "Ledger Books", StringComparison.Ordinal);

    internal static string FormatReadinessScope(string? value)
        => string.IsNullOrWhiteSpace(value) ? "missing" : value.Trim();

    internal static Guid CreateStableGuid(params string?[] parts)
    {
        var input = string.Join("|", parts.Select(static part => part?.Trim() ?? string.Empty));
        var hash = Sha256Digest.ComputeBytesUtf8(input);
        var bytes = hash[..16].ToArray();
        return new Guid(bytes);
    }

    internal static string FormatMigrationArtifactKind(AccountingMigrationRunKindDto kind)
        => kind switch
        {
            AccountingMigrationRunKindDto.LedgerBookScope => "Ledger-book scope",
            AccountingMigrationRunKindDto.HistoricalJournalBackfill => "Historical journal backfill",
            AccountingMigrationRunKindDto.DimensionalBackfill => "Dimensional backfill",
            AccountingMigrationRunKindDto.AccountingConfigurationPromotion => "Configuration promotion",
            AccountingMigrationRunKindDto.CloseReportingEvidence => "Close/reporting evidence",
            _ => kind.ToString()
        };

    internal static string FormatMigrationArtifactScope(AccountingMigrationRunArtifactDto artifact)
    {
        var scope = new[]
        {
            string.IsNullOrWhiteSpace(artifact.FundProfileId) ? null : $"fund {artifact.FundProfileId}",
            artifact.LedgerBookId.HasValue ? $"book {artifact.LedgerBookId.Value:D}" : "fund-level",
            FormatLedgerDimensionSet(artifact.Dimensions)
        }.Where(static item => !string.IsNullOrWhiteSpace(item));
        return string.Join("; ", scope);
    }

    internal static string FormatMigrationWorkerPlanScope(AccountingMigrationRunWorkerPlanDto plan)
    {
        var scope = new[]
        {
            string.IsNullOrWhiteSpace(plan.TenantId) ? null : $"tenant {plan.TenantId}",
            string.IsNullOrWhiteSpace(plan.CompanyId) ? null : $"company {plan.CompanyId}",
            string.IsNullOrWhiteSpace(plan.FundProfileId) ? null : $"fund {plan.FundProfileId}",
            $"book {plan.LedgerBookId:D}",
            FormatLedgerDimensionSet(plan.Dimensions),
            string.IsNullOrWhiteSpace(plan.Summary) ? null : plan.Summary
        }.Where(static item => !string.IsNullOrWhiteSpace(item));
        return string.Join("; ", scope);
    }

    internal static string FormatLedgerDimensionSet(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return string.Empty;
        }

        var values = new List<string>
        {
            FormatDimension("fund", dimensions.FundId),
            FormatDimension("entity", dimensions.EntityId),
            FormatDimension("sleeve", dimensions.SleeveId),
            FormatDimension("strategy", dimensions.StrategyId),
            FormatDimension("investor", dimensions.InvestorId),
            FormatDimension("capital", dimensions.CapitalAccountId),
            dimensions.InstrumentId.HasValue ? $"instrument {dimensions.InstrumentId.Value:D}" : string.Empty,
            FormatDimension("tax lot", dimensions.TaxLotId),
            FormatDimension("cost center", dimensions.CostCenterId),
            FormatDimension("counterparty", dimensions.CounterpartyId),
            FormatDimension("organization", dimensions.OrganizationId),
            FormatDimension("portfolio", dimensions.PortfolioId),
            FormatDimension("book", dimensions.BookId),
            FormatDimension("account", dimensions.AccountId),
            FormatDimension("customer", dimensions.CustomerId),
            FormatDimension("vendor", dimensions.VendorId),
            FormatDimension("project", dimensions.ProjectId)
        };

        values.AddRange(dimensions.ExternalGlDimensions
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => string.IsNullOrWhiteSpace(pair.Value) ? string.Empty : $"external {pair.Key}={pair.Value}"));
        return string.Join(", ", values.Where(static item => !string.IsNullOrWhiteSpace(item)));
    }

    internal static string FormatDimension(string label, string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label} {value.Trim()}";

    internal static string? FirstDimensionValue(IReadOnlyList<string>? values)
        => values?.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    internal static IReadOnlyList<string> BuildManualJournalLifecycleEvidenceLinks(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action)
    {
        var journalId = draft.JournalEntryId.ToString("D");
        var period = string.IsNullOrWhiteSpace(draft.PeriodId)
            ? draft.AccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : draft.PeriodId;
        var actionToken = action.ToString().ToLowerInvariant();
        var ledgerBookScope = draft.LedgerBookId.HasValue
            ? $"ledger-book/{draft.LedgerBookId.Value:D}"
            : "ledger-book/fund-level";
        var required = action switch
        {
            JournalEntryLifecycleActionDto.Approve => $"approval://manual-je/{journalId}/review-approval/{period}/{ledgerBookScope}",
            JournalEntryLifecycleActionDto.Post => $"posting://manual-je/{journalId}/ledger-posting-review/{period}/{ledgerBookScope}",
            JournalEntryLifecycleActionDto.Reverse => $"correction://manual-je/{journalId}/reversal-review/{period}/{ledgerBookScope}",
            JournalEntryLifecycleActionDto.Rebook => $"correction://manual-je/{journalId}/rebook-review/{period}/{ledgerBookScope}",
            JournalEntryLifecycleActionDto.LockAfterClose => $"close://manual-je/{journalId}/period-lock-close-certification/{period}/{ledgerBookScope}",
            JournalEntryLifecycleActionDto.Reject => $"rejection://manual-je/{journalId}/review-rejection/{period}/{ledgerBookScope}",
            _ => $"review://manual-je/{journalId}/{actionToken}/{period}/{ledgerBookScope}"
        };

        return draft.EvidenceLinks
            .Append(required)
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IReadOnlyList<ManualJournalEntryLineDto> BuildManualJournalRebookLines(ManualJournalEntryDraftDto draft)
        => draft.Lines
            .Select(line => line with
            {
                LineId = $"rebook-{line.LineId}",
                Description = string.IsNullOrWhiteSpace(line.Description)
                    ? "Rebooked line from WPF Accounting Configure."
                    : $"{line.Description} Rebooked from WPF Accounting Configure.",
                EvidenceLink = line.EvidenceLink ?? draft.EvidenceLinks.FirstOrDefault()
            })
            .ToArray();

    internal static string BuildManualJournalValidationText(ManualJournalEntryDraftDto draft)
        => draft.ValidationIssues.Any(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            ? $"{draft.ValidationIssues.Count(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)} critical issue(s) block approval submission."
            : $"Manual journal draft is balanced at {draft.TotalDebits:0.##} / {draft.TotalCredits:0.##} {draft.Currency} and ready for approval submission.";

    internal static string BuildManualJournalStatusText(ManualJournalEntryWorkbenchDto workbench)
    {
        var draftText = workbench.Drafts.Count == 0
            ? "No manual journal drafts saved yet. Drafts are durable and approval-gated."
            : $"{workbench.Drafts.Count} manual journal draft(s) loaded; latest status {workbench.Drafts[0].Status}.";
        if (workbench.PrivateCapitalActivity is not { } activity)
        {
            return draftText;
        }

        var issueText = activity.ValidationIssues.Count == 0
            ? "no projection issue(s)"
            : $"{activity.ValidationIssues.Count} projection issue(s)";

        return string.Concat(
            draftText,
            " Private-capital projection: ",
            $"{activity.FundEventCount} event(s), ",
            $"{activity.PostedFundEventCount} posted, ",
            $"{activity.FundEventRecords.Count} event record(s), ",
            $"{activity.CapitalAccountCount} capital account(s), ",
            $"{activity.CapitalAccountSubledgers.Count} account subledger(s), ",
            $"{activity.CapitalAccountSubledgerEntries.Count} subledger movement(s), ",
            $"{activity.PaymentIntents.Count} payment intent(s), ",
            $"{activity.LedgerImpacts.Count} ledger impact(s), ",
            $"{activity.ReportOutputs.Count} report output(s), ",
            $"{activity.PublishedReportOutputCount} published, ",
            issueText,
            ".");
    }

    internal static AccountingWorkbenchRow BuildManualJournalPaymentIntentRow(PaymentIntentWorkflowDto intent)
        => new(
            intent.PaymentIntentId,
            intent.StatusLabel,
            $"{intent.ExpectedCashMovement.Direction} {FormatCurrencyAmount(intent.ExpectedCashMovement.Amount, intent.ExpectedCashMovement.Currency)} | {intent.ExpectedCashMovement.EffectiveDate:yyyy-MM-dd} | requester {intent.Requester} | settlement {intent.SettlementReference ?? "not assigned"}",
            $"payee {intent.ExpectedCashMovement.Payee ?? "not assigned"} | scope {intent.ExpectedCashMovement.AccountScope ?? "not assigned"} | purpose {intent.ExpectedCashMovement.BusinessPurpose ?? intent.ExpectedCashMovement.Purpose} | policy {intent.ExpectedCashMovement.ApprovalPolicy ?? "not assigned"} | {intent.ExpectedCashMovement.SourceEvidenceLinks.Count} source evidence link(s) | {intent.ApprovalChain.Count} approval step(s); {intent.BankEvidence.Count} bank/cash evidence item(s); {BuildPaymentIntentBankEvidenceRecorderText(intent.BankEvidence)} | {intent.ReconciliationLinks.Count} reconciliation link(s); {intent.AuditHistory.Count} audit event(s) | {intent.ReadinessReason} | {intent.ExecutionDeferredReason}",
            intent.EvidenceRoute);

    internal static string BuildPaymentIntentBankEvidenceRecorderText(IReadOnlyList<PaymentIntentBankEvidenceDto> bankEvidence)
    {
        var recorders = bankEvidence
            .Select(static item => item.RecordedBy)
            .Where(static recorder => !string.IsNullOrWhiteSpace(recorder))
            .Select(static recorder => recorder!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        return recorders.Length == 0
            ? "no retained recorder"
            : $"recorded by {string.Join(", ", recorders)}";
    }

    internal static string FormatCurrencyAmount(decimal amount, string currency)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? string.Empty : $" {currency.Trim().ToUpperInvariant()}";
        return $"{amount:0.##}{code}";
    }

    internal static string FormatSignedCurrencyAmount(decimal amount, string currency)
    {
        var prefix = amount > 0m ? "+" : string.Empty;
        return $"{prefix}{FormatCurrencyAmount(amount, currency)}";
    }

    internal static string BuildCapitalAccountSubledgerStatus(PrivateCapitalCapitalAccountSubledgerDto subledger)
    {
        if (!string.IsNullOrWhiteSpace(subledger.ReadinessLabel))
        {
            return subledger.ReadinessLabel;
        }

        if (subledger.ValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            return "Blocked";
        }

        if (subledger.ValidationIssues.Count > 0 ||
            subledger.ApprovalQueueCount > 0 ||
            subledger.EvidenceCategories.Any(static category => !category.IsReady))
        {
            return "Review";
        }

        return subledger.FundEventCount > 0 ? "Ready" : "Empty";
    }

    internal static string FormatSubledgerNextAction(PrivateCapitalCapitalAccountSubledgerDto subledger)
        => string.IsNullOrWhiteSpace(subledger.NextActionRoute)
            ? subledger.NextAction
            : $"{subledger.NextAction} via {subledger.NextActionRoute}";

    internal static string BuildEvidenceCategoryReadinessSummary(IReadOnlyList<PrivateCapitalEvidenceCategoryDto> categories)
    {
        if (categories.Count == 0)
        {
            return "no evidence categories";
        }

        var readyCount = categories.Count(static category => category.IsReady);
        var categoryLabels = string.Join(
            "; ",
            categories.Select(static category => $"{category.Label} {(category.IsReady ? "Ready" : "Missing")}"));
        return $"{readyCount}/{categories.Count} evidence categories ready: {categoryLabels}";
    }

    internal static string FormatSubledgerDateRange(PrivateCapitalCapitalAccountSubledgerDto subledger)
    {
        var first = subledger.FirstEffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "no first date";
        var last = subledger.LastEffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "no last date";
        return $"{first} -> {last} | {subledger.LastFundEventType ?? "no last event"}";
    }

    internal static string FormatRequiredEvidence(IReadOnlyList<string> values)
        => values.Count == 0 ? "none" : string.Join("; ", values);
}
