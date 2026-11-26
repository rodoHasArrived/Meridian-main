using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    private async Task<FinancialRecordExplorerDto> BuildSecurityInstrumentExplorerAsync(
        string tenantId,
        FundOwnershipScope fundScope,
        FinancialRecordExplorerQueryDto? query,
        FinancialRecordExplorerReadScope scope,
        ReportAccessQueryContext? reportAccess,
        CancellationToken ct)
    {
        var readService = _runReadService;
        if (readService is null)
        {
            return CreateBlockedExplorer(
                SecurityInstrumentExplorerId,
                "Security & Instrument Explorer",
                "Explore Security Master references used by retained accounting and portfolio records.",
                "Strategy run read service is not registered.");
        }

        var source = await TryLoadLatestRunDetailAsync(
            readService,
            fundScope,
            detail => CollectSecurityReferences(detail).Count > 0,
            ct).ConfigureAwait(false);
        if (source is null)
        {
            return await CreateEmptyExplorerAsync(
                SecurityInstrumentExplorerId,
                "Security & Instrument Explorer",
                "Explore Security Master references used by retained accounting and portfolio records.",
                "No source-backed Security Master references are available.",
                tenantId,
                ct).ConfigureAwait(false);
        }

        var run = source.Summary;
        var references = CollectSecurityReferences(source);
        // Each enrichment is loaded only for a caller entitled to its family. Reaching this explorer
        // means holding a Security Master, operations or strategy permission; none of those is a claim
        // on report-pack usage or direct-lending operations, and the enrichment builder already takes
        // both as arguments, so withholding them is a matter of not fetching them.
        // Permission decides whether the family is loaded at all; the access context decides which of
        // its records this caller may see. Both are needed -- ListRecords returns every tenant's
        // workflow records, so a Reporting-entitled caller would otherwise be enriched with another
        // tenant's report-pack usage.
        var reportRecords = scope.Reporting
            ? _reportPackWorkflowService?.ListAccessibleRecords(200, reportAccess) ?? []
            : [];
        var directLendingOperations = scope.DirectLending && _directLendingOperationsReadService is not null
            ? await _directLendingOperationsReadService.GetOperationsAsync(ct: ct).ConfigureAwait(false)
            : null;
        var enrichedRows = new List<(FinancialRecordExplorerRowDto Row, SecurityInstrumentEnrichment Enrichment)>(references.Count);
        for (var index = 0; index < references.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var reference = references[index];
            var enrichment = await BuildSecurityInstrumentEnrichmentAsync(reference, reportRecords, directLendingOperations, scope, ct).ConfigureAwait(false);
            enrichedRows.Add((BuildSecurityRow(run, source, reference, index, enrichment, scope.Strategy), enrichment));
        }

        var rows = enrichedRows.Select(static entry => entry.Row).ToArray();
        var enrichments = enrichedRows.Select(static entry => entry.Enrichment).ToArray();
        var passportCount = enrichments.Count(static enrichment => enrichment.Passport is not null);
        var operationsCount = enrichments.Count(static enrichment => enrichment.Readiness is not null);
        var directLendingCount = enrichments.Count(static enrichment => enrichment.DirectLendingHealth.Count > 0);
        var reportedLineUsageCount = enrichments.Sum(static enrichment => enrichment.ReportLineUsages.Count);
        var termCount = enrichments.Sum(static enrichment => enrichment.Operations?.TermsHistory.Count ?? 0);
        var cashFlowCount = enrichments.Sum(static enrichment => enrichment.Operations?.ProjectedCashFlows.Count ?? 0);
        var reconciliationCount = enrichments.Sum(static enrichment => enrichment.Operations?.ReconciliationResults.Count ?? 0);
        var accountingProjectionCount = enrichments.Sum(static enrichment => enrichment.Operations?.LedgerProjections.Count ?? 0);
        var postedJournalCount = enrichments.Sum(static enrichment => enrichment.InstrumentJournalProofs.Count(static proof => proof.Journal is not null));
        var evidenceCount = enrichments.Sum(static enrichment => CountSecurityEvidenceAnchors(enrichment));
        var auditCount = enrichments.Sum(static enrichment => CountSecurityAuditEvents(enrichment));
        var summaryItems = new List<FinancialRecordExplorerSummaryItemDto>
        {
            new("Securities", rows.Length.ToString(CultureInfo.InvariantCulture), "Distinct resolved or referenced instruments."),
            new("Resolved", references.Count(reference => reference.CoverageStatus == WorkstationSecurityCoverageStatus.Resolved).ToString(CultureInfo.InvariantCulture), "Security Master references with resolved coverage.", FinancialRecordExplorerTone.Success),
            new("Missing", references.Count(reference => reference.CoverageStatus is WorkstationSecurityCoverageStatus.Missing or WorkstationSecurityCoverageStatus.Unavailable).ToString(CultureInfo.InvariantCulture), "References requiring operator follow-up.", FinancialRecordExplorerTone.Warning)
        };
        if (passportCount > 0)
        {
            summaryItems.Add(new("Passports", passportCount.ToString(CultureInfo.InvariantCulture), "Security Master passport/trust payloads available.", FinancialRecordExplorerTone.Info));
        }

        if (operationsCount > 0)
        {
            summaryItems.Add(new("Operations", operationsCount.ToString(CultureInfo.InvariantCulture), "AssetOperations readiness payloads available.", FinancialRecordExplorerTone.Info));
        }

        if (directLendingCount > 0)
        {
            summaryItems.Add(new("Direct Lending", directLendingCount.ToString(CultureInfo.InvariantCulture), "Direct-lending operations posture is linked to Security Master instruments.", FinancialRecordExplorerTone.Info));
        }

        if (termCount > 0)
        {
            summaryItems.Add(new("Terms", termCount.ToString(CultureInfo.InvariantCulture), "Instrument terms and obligation roots available.", FinancialRecordExplorerTone.Info));
        }

        if (cashFlowCount > 0)
        {
            summaryItems.Add(new("Cash Flows", cashFlowCount.ToString(CultureInfo.InvariantCulture), "Projected cash-flow obligations available.", FinancialRecordExplorerTone.Info));
        }

        if (reconciliationCount > 0)
        {
            summaryItems.Add(new("Reconciliations", reconciliationCount.ToString(CultureInfo.InvariantCulture), "AssetOperations reconciliation results available.", FinancialRecordExplorerTone.Info));
        }

        if (accountingProjectionCount > 0)
        {
            summaryItems.Add(new("Accounting Projections", accountingProjectionCount.ToString(CultureInfo.InvariantCulture), "Projected accounting-effect rows are available; they are not posted journals.", FinancialRecordExplorerTone.Info));
        }

        if (postedJournalCount > 0)
        {
            summaryItems.Add(new("Posted Journals", postedJournalCount.ToString(CultureInfo.InvariantCulture), "Durable posted journals resolved by exact ledger-book and source-event scope.", FinancialRecordExplorerTone.Success));
        }

        if (reportedLineUsageCount > 0)
        {
            summaryItems.Add(new("Reported Usage", reportedLineUsageCount.ToString(CultureInfo.InvariantCulture), "Published or restated report lines with retained publication evidence reference these instruments.", FinancialRecordExplorerTone.Success));
        }

        if (evidenceCount > 0)
        {
            summaryItems.Add(new("Evidence", evidenceCount.ToString(CultureInfo.InvariantCulture), "Evidence anchors retained for security-instrument proof chains.", FinancialRecordExplorerTone.Info));
        }

        if (auditCount > 0)
        {
            summaryItems.Add(new("Audit Events", auditCount.ToString(CultureInfo.InvariantCulture), "Audit events retained for security-instrument proof chains.", FinancialRecordExplorerTone.Info));
        }

        return await CreateExplorerAsync(
            SecurityInstrumentExplorerId,
            "Security & Instrument Explorer",
            "Explore Security Master references used by retained accounting and portfolio records.",
            scope.Strategy
                ? $"Source-backed Security Master references from run {run.RunId}."
                : "Source-backed Security Master references retained by portfolio and ledger records.",
            BuildScope(
                run,
                source.Portfolio?.AsOf ?? source.Ledger?.AsOf ?? run.LastUpdatedAt,
                "Security Master",
                scope.Strategy),
            summaryItems,
            BuildSystemViews(SecurityInstrumentExplorerId, "Security references", "Instrument references used by portfolio and ledger records."),
            BuildSecurityFilters(references),
            [
                new("security", "Security", Width: 220),
                new("assetClass", "Asset Class", Width: 120),
                new("currency", "Currency", Width: 90),
                new("status", "Status", Width: 110),
                new("coverage", "Coverage", Width: 120),
                new("trust", "Trust", Width: 120),
                new("identifierConfidence", "Identifier Confidence", Width: 170),
                new("operations", "Operations", Width: 140),
                new("directLending", "Direct Lending", Width: 150),
                new("cashFlow", "Cash Flow", Width: 120),
                new("ledger", "Accounting Projection", Width: 160),
                new("terms", "Terms / Obligations", Width: 170),
                new("reportUsage", "Reported Usage", Width: 170),
                new("evidence", "Evidence", Width: 140),
                new("auditTrail", "Audit Trail", Width: 140),
                new("identifier", "Identifier", Width: 160),
                new("source", "Source", Width: 120)
            ],
            rows,
            BuildExplorerProofActions(run, UiApiRoutes.WorkstationSecurityMasterSearch, scope.Strategy),
            tenantId,
            query,
            ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<FinancialRecordExplorerFilterDto> BuildSecurityFilters(
        IReadOnlyList<WorkstationSecurityReference> references)
        => references
            .Select(static reference => reference.AssetClass)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Select(static value => new FinancialRecordExplorerFilterDto($"asset-{Slugify(value)}", "Asset class", value))
            .ToArray();

    private static FinancialRecordExplorerCellDto BuildTrustCell(InstrumentPassportDto? passport)
    {
        if (passport is null)
        {
            return new("trust", "-");
        }

        return new(
            "trust",
            passport.TrustPosture.Tone.ToString(),
            passport.TrustPosture.TrustScore.ToString(CultureInfo.InvariantCulture),
            ToneFromTrustPosture(passport.TrustPosture.Tone),
            BuildInstrumentPassportHref(passport.SecurityId));
    }

    private static FinancialRecordExplorerCellDto BuildIdentifierConfidenceCell(InstrumentPassportDto? passport)
    {
        if (passport is null)
        {
            return new("identifierConfidence", "-");
        }

        return new(
            "identifierConfidence",
            BuildIdentifierConfidenceLabel(passport),
            BuildIdentifierConfidenceDetail(passport),
            ToneFromIdentifierConfidence(passport),
            BuildInstrumentPassportHref(passport.SecurityId));
    }

    private static FinancialRecordExplorerCellDto BuildDirectLendingCell(SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.DirectLendingHealth.Count == 0)
        {
            return new("directLending", "-");
        }

        return new(
            "directLending",
            BuildDirectLendingSummary(enrichment),
            enrichment.DirectLendingHealth.Count.ToString(CultureInfo.InvariantCulture),
            ToneFromDirectLending(enrichment),
            BuildDirectLendingHref(enrichment));
    }

    private static FinancialRecordExplorerCellDto BuildAssetOperationsCell(AssetOperationsReadinessDto? readiness)
        => readiness is null
            ? new("operations", "-")
            : new(
                "operations",
                readiness.Status,
                BuildReadinessDetail(readiness),
                ToneFromAssetOperationsReadiness(readiness),
                BuildAssetOperationsHref(readiness.SecurityId));

    private static FinancialRecordExplorerCellDto BuildCashFlowCell(AssetOperationsDetailDto? operations)
        => operations is null
            ? new("cashFlow", "-")
            : new(
                "cashFlow",
                BuildCashFlowSummary(operations),
                BuildCashFlowDetail(operations),
                ToneFromCashFlows(operations),
                BuildAssetOperationsHref(operations.Subject.SecurityId));

    private static FinancialRecordExplorerCellDto BuildLedgerCell(AssetOperationsDetailDto? operations)
        => operations is null
            ? new("ledger", "-")
            : new(
                "ledger",
                BuildLedgerProjectionSummary(operations),
                BuildLedgerProjectionDetail(operations),
                ToneFromLedgerProjections(operations),
                BuildAssetOperationsHref(operations.Subject.SecurityId));

    private static FinancialRecordExplorerCellDto BuildTermsCell(AssetOperationsDetailDto? operations)
        => operations is null
            ? new("terms", "-")
            : new(
                "terms",
                BuildTermsObligationsSummary(operations),
                BuildTermsObligationsDetail(operations),
                ToneFromTerms(operations),
                BuildAssetOperationsHref(operations.Subject.SecurityId));

    private static FinancialRecordExplorerCellDto BuildReportUsageCell(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
        => enrichment.ReportLineUsages.Count == 0
            ? new("reportUsage", "-")
            : new(
                "reportUsage",
                BuildReportUsageSummary(enrichment.ReportLineUsages),
                BuildReportedLineUsageDetail(enrichment.ReportLineUsages),
                FinancialRecordExplorerTone.Success,
                BuildSecurityReportLineHref(reference, enrichment));

    private static FinancialRecordExplorerCellDto BuildEvidenceCell(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
        => new(
            "evidence",
            BuildSecurityEvidenceSummary(enrichment),
            BuildSecurityEvidenceDetail(enrichment),
            ToneFromSecurityEvidence(enrichment),
            BuildSecurityEvidenceHref(reference, enrichment));

    private static FinancialRecordExplorerCellDto BuildAuditTrailCell(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
        => new(
            "auditTrail",
            BuildSecurityAuditSummary(enrichment),
            BuildSecurityAuditDetail(enrichment),
            ToneFromSecurityAudit(enrichment),
            BuildSecurityAuditHref(reference, enrichment));

    private static string BuildInstrumentIdentityLabel(WorkstationSecurityReference reference)
    {
        var identifier = reference.PrimaryIdentifier ?? reference.MatchedIdentifierValue;
        return HasText(identifier)
            ? $"{identifier} / {reference.DisplayName}"
            : reference.DisplayName;
    }

    private static string BuildInstrumentIdentityDetail(WorkstationSecurityReference reference)
    {
        var securityId = reference.SecurityId == Guid.Empty ? "unresolved" : reference.SecurityId.ToString("D");
        return $"{reference.AssetClass} {reference.SubType ?? "instrument"} in {reference.Currency}; Security Master id {securityId}.";
    }

    private static string BuildIdentifierMapSummary(
        WorkstationSecurityReference reference,
        InstrumentPassportDto? passport)
    {
        if (passport is null)
        {
            return HasText(reference.PrimaryIdentifier)
                ? $"{reference.PrimaryIdentifier} ({reference.MatchedProvider ?? "source"})"
                : "No identifier map";
        }

        return $"{passport.IdentifierSummary.ActiveIdentifierCount.ToString(CultureInfo.InvariantCulture)} identifier{Plural(passport.IdentifierSummary.ActiveIdentifierCount)} / {passport.IdentifierSummary.ProviderMappingCount.ToString(CultureInfo.InvariantCulture)} provider mapping{Plural(passport.IdentifierSummary.ProviderMappingCount)}";
    }

    private static string BuildIdentifierMapDetail(
        WorkstationSecurityReference reference,
        InstrumentPassportDto? passport)
    {
        if (passport is null)
        {
            return reference.ResolutionReason ?? "Identifier map evidence is not available from Security Master.";
        }

        var providers = passport.IdentifierSummary.ProviderMappings
            .Select(static mapping => mapping.Provider ?? mapping.MappingSource)
            .Where(HasText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        return providers.Length == 0
            ? passport.IdentifierSummary.Summary
            : $"{passport.IdentifierSummary.Summary} Providers: {string.Join(", ", providers)}.";
    }

    private static FinancialRecordExplorerTone ToneFromIdentifierMap(
        WorkstationSecurityReference reference,
        InstrumentPassportDto? passport)
    {
        if (passport is not null)
        {
            return passport.IdentifierSummary.HasPrimaryIdentifier && passport.IdentifierSummary.HasProviderMappings
                ? FinancialRecordExplorerTone.Success
                : FinancialRecordExplorerTone.Warning;
        }

        return HasText(reference.PrimaryIdentifier) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning;
    }

    private static string BuildAccountingClassificationSummary(
        WorkstationSecurityReference reference,
        AssetOperationsDetailDto? operations)
    {
        var ledgerBasis = operations?.LedgerProjections
            .Select(static projection => projection.LedgerBasis)
            .FirstOrDefault(HasText);
        return HasText(ledgerBasis)
            ? $"{reference.AssetClass} / {ledgerBasis}"
            : reference.AssetClass;
    }

    private static string BuildAccountingClassificationDetail(
        WorkstationSecurityReference reference,
        AssetOperationsDetailDto? operations)
    {
        var projection = operations?.LedgerProjections
            .OrderByDescending(static item => item.AccountingDate)
            .FirstOrDefault();
        if (projection is null)
        {
            return $"{reference.SubType ?? reference.AssetClass} classification is retained from Security Master coverage.";
        }

        return $"{projection.ProjectionType} is classified on {projection.LedgerBasis} with status {projection.Status}.";
    }

    private static string BuildTrustConflictDetail(SecurityMasterTrustPostureDto posture)
        => posture.HasOpenConflicts
            ? $"{posture.OpenConflictCount.ToString(CultureInfo.InvariantCulture)} open conflict{Plural(posture.OpenConflictCount)}; recommended steward review before accounting close or report publication."
            : $"{posture.Summary} No open Security Master conflicts are retained.";

    private static string BuildTermsObligationsSummary(AssetOperationsDetailDto operations)
    {
        var terms = operations.TermsHistory.Count;
        var obligations = operations.TermsObligationsTimeline?.Events.Count
            ?? operations.ProjectedCashFlows.Count + operations.LifecycleEvents.Count;
        if (terms == 0 && obligations == 0)
        {
            return "No terms";
        }

        return $"{terms.ToString(CultureInfo.InvariantCulture)} term{Plural(terms)} / {obligations.ToString(CultureInfo.InvariantCulture)} obligation{Plural(obligations)}";
    }

    internal static string BuildTermsObligationsDetail(AssetOperationsDetailDto operations)
    {
        var latestTerms = operations.TermsHistory
            .OrderByDescending(static terms => terms.EffectiveDate)
            .FirstOrDefault();
        // "Next" is the first timeline row outside the Lifecycle lane. One kind of row is passed over: a
        // declared call or put notice deadline dated before the timeline's as-of. The scalar call
        // review it replaces was never raised for a date already past, so a declaring bond would
        // otherwise lead with an elapsed deadline where an undeclared bond leads with its next row.
        // The elapsed deadline stays on the timeline with its past-due status and warning.
        var timelineAsOf = operations.TermsObligationsTimeline?.ProjectionAsOf;
        var nextTimelineEvent = operations.TermsObligationsTimeline?.Events
            .FirstOrDefault(timelineEvent => !string.Equals(timelineEvent.EventLane, "Lifecycle", StringComparison.OrdinalIgnoreCase)
                && !(timelineEvent.EffectiveDate < timelineAsOf
                    && timelineEvent.EventKind is Meridian.Instruments.AssetOperations.AssetObligationProjectionService.IssuerCallNoticeDeadlineEventKind
                        or Meridian.Instruments.AssetOperations.AssetObligationProjectionService.HolderPutNoticeDeadlineEventKind));
        var nextFlow = nextTimelineEvent is null
            ? operations.ProjectedCashFlows
                .OrderBy(static flow => flow.DueDate)
                .FirstOrDefault()
            : null;

        if (latestTerms is null && nextTimelineEvent is null && nextFlow is null)
        {
            return "No retained terms or obligation rows are available.";
        }

        var parts = new List<string>();
        if (latestTerms is not null)
        {
            parts.Add($"{latestTerms.Summary} Effective {latestTerms.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.");
        }

        if (nextTimelineEvent is not null)
        {
            var amount = nextTimelineEvent.ExpectedAmount.HasValue
                ? $" for {FormatAmountCurrency(nextTimelineEvent.ExpectedAmount.Value, nextTimelineEvent.Currency)}"
                : string.Empty;
            parts.Add($"Next timeline event is {nextTimelineEvent.EventKind} due {nextTimelineEvent.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{amount}; status {nextTimelineEvent.Status}.");
        }
        else if (nextFlow is not null)
        {
            parts.Add($"Next obligation is {nextFlow.FlowType} due {nextFlow.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} for {FormatAmountCurrency(nextFlow.Amount, nextFlow.Currency)}.");
        }

        return string.Join(" ", parts);
    }

    private static FinancialRecordExplorerTone ToneFromTerms(AssetOperationsDetailDto operations)
        => operations.TermsHistory.Count == 0 &&
           operations.ProjectedCashFlows.Count == 0 &&
           (operations.TermsObligationsTimeline?.Events.Count ?? 0) == 0
            ? FinancialRecordExplorerTone.Warning
            : FinancialRecordExplorerTone.Success;

    private static string BuildReconciliationSummary(AssetOperationsDetailDto operations)
    {
        var count = operations.ReconciliationResults.Count;
        return count == 0
            ? "No reconciliation"
            : $"{count.ToString(CultureInfo.InvariantCulture)} result{Plural(count)}";
    }

    private static string BuildReconciliationDetail(AssetOperationsDetailDto operations)
    {
        var latest = operations.ReconciliationResults
            .OrderByDescending(static result => result.ActualDate ?? result.ExpectedDate ?? DateOnly.MinValue)
            .FirstOrDefault();
        if (latest is null)
        {
            return "No retained reconciliation result is available.";
        }

        var expected = latest.ExpectedAmount.HasValue ? FormatAmountCurrency(latest.ExpectedAmount.Value, "USD") : "no expected amount";
        var actual = latest.ActualAmount.HasValue ? FormatAmountCurrency(latest.ActualAmount.Value, "USD") : "no actual amount";
        return $"{latest.MatchStatus} reconciliation result compares {expected} expected to {actual} actual.";
    }

    private static string BuildProjectedAccountingEffectSummary(AssetOperationsDetailDto operations)
    {
        var count = operations.LedgerProjections.Count;
        return count == 0
            ? "No projected effect"
            : $"{count.ToString(CultureInfo.InvariantCulture)} projected effect{Plural(count)}";
    }

    private static string BuildProjectedAccountingEffectDetail(AssetOperationsDetailDto operations)
    {
        var latest = operations.LedgerProjections
            .OrderByDescending(static projection => projection.AccountingDate)
            .FirstOrDefault();
        if (latest is null)
        {
            return "No retained projected accounting effect is available.";
        }

        var debit = latest.DebitAmount ?? 0m;
        var credit = latest.CreditAmount ?? 0m;
        return $"{latest.ProjectionType} {latest.Status} on {latest.AccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} for {latest.LedgerBasis}; projected debit {FormatAmountCurrency(debit, latest.Currency)} and projected credit {FormatAmountCurrency(credit, latest.Currency)}. This is not a posted journal.";
    }

    private static string BuildFinancialRecordExplorerHref(
        string explorerId,
        IReadOnlyList<string> queryParameters)
    {
        var route = UiApiRoutes.WithParam(UiApiRoutes.WorkstationFinancialRecordExplorer, "explorerId", explorerId);
        return queryParameters.Count == 0
            ? route
            : UiApiRoutes.WithQuery(route, string.Join("&", queryParameters));
    }

    private static IReadOnlyList<string> BuildSecurityQueryParameters(
        WorkstationSecurityReference reference,
        ReportPackLineProvenanceDto? line)
    {
        var parameters = new List<string>();
        AddQueryParameter(parameters, "securityId", reference.SecurityId == Guid.Empty ? null : reference.SecurityId.ToString("D"));
        AddQueryParameter(parameters, "sourceId", reference.PrimaryIdentifier ?? reference.MatchedIdentifierValue ?? reference.DisplayName);
        AddQueryParameter(parameters, "lineKey", line?.LineKey);
        AddQueryParameter(parameters, "evidenceId", line?.EvidenceId);
        return parameters;
    }

    private static void AddQueryParameter(ICollection<string> parameters, string key, string? value)
    {
        if (HasText(value))
        {
            parameters.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value!.Trim())}");
        }
    }

    private static string BuildTrustPostureLabel(SecurityMasterTrustPostureDto posture)
        => $"{posture.Tone} ({posture.TrustScore.ToString(CultureInfo.InvariantCulture)})";

    private static string BuildIdentifierConfidenceLabel(InstrumentPassportDto passport)
    {
        var confidence = SelectBestProviderConfidence(passport);
        if (confidence is not null)
        {
            return $"{EmptyFallback(confidence.Provider, "Provider")} {FormatConfidence(confidence.ConfidenceScore)}";
        }

        if (passport.IdentifierSummary.HasPrimaryIdentifier)
        {
            var kind = EmptyFallback(passport.IdentifierSummary.PrimaryIdentifierKind, "Identifier");
            return $"{kind}: {passport.IdentifierSummary.PrimaryIdentifierValue}";
        }

        return "No primary identifier";
    }

    private static string BuildIdentifierConfidenceDetail(InstrumentPassportDto passport)
    {
        var confidence = SelectBestProviderConfidence(passport);
        if (confidence is null)
        {
            return passport.IdentifierSummary.Summary;
        }

        return $"{confidence.ProviderSource} {confidence.MappingKind} mapping for {confidence.Symbol}: {confidence.ConfidenceReason}";
    }

    private static string BuildReadinessDetail(AssetOperationsReadinessDto readiness)
    {
        var total = readiness.Capabilities.Count;
        var ready = readiness.ReadyCapabilities.Count;
        if (readiness.MissingCapabilities.Count == 0 && readiness.Warnings.Count == 0)
        {
            return $"{ready.ToString(CultureInfo.InvariantCulture)}/{total.ToString(CultureInfo.InvariantCulture)} capability{Plural(total)} ready.";
        }

        return BuildReadinessBlockerDetail(readiness);
    }

    private static string BuildReadinessBlockerDetail(AssetOperationsReadinessDto readiness)
    {
        var blockers = readiness.MissingCapabilities
            .Concat(readiness.Warnings)
            .Where(HasText)
            .Take(4)
            .ToArray();
        return blockers.Length == 0
            ? "No validation blockers retained."
            : string.Join("; ", blockers);
    }

    private static string BuildCashFlowSummary(AssetOperationsDetailDto operations)
    {
        var count = operations.ProjectedCashFlows.Count;
        return count == 0
            ? "No projection"
            : $"{count.ToString(CultureInfo.InvariantCulture)} projected";
    }

    private static string BuildCashFlowDetail(AssetOperationsDetailDto operations)
    {
        var next = operations.ProjectedCashFlows
            .OrderBy(static flow => flow.DueDate)
            .FirstOrDefault();
        if (next is null)
        {
            return "No retained projected cash-flow row is available.";
        }

        return $"Next {next.FlowType} due {next.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} for {FormatAmountCurrency(next.Amount, next.Currency)} ({next.Status}).";
    }

    private static string BuildLedgerProjectionSummary(AssetOperationsDetailDto operations)
    {
        var count = operations.LedgerProjections.Count;
        return count == 0
            ? "No projection"
            : $"{count.ToString(CultureInfo.InvariantCulture)} projection{Plural(count)}";
    }

    private static string BuildLedgerProjectionDetail(AssetOperationsDetailDto operations)
    {
        var latest = operations.LedgerProjections
            .OrderByDescending(static projection => projection.AccountingDate)
            .FirstOrDefault();
        if (latest is null)
        {
            return "No retained ledger-projection row is available.";
        }

        var amount = latest.DebitAmount ?? latest.CreditAmount ?? 0m;
        return $"{latest.ProjectionType} {latest.Status} on {latest.AccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} for {FormatAmountCurrency(amount, latest.Currency)}.";
    }

    private static string BuildReportedLineUsageDetail(IReadOnlyList<SecurityReportLineUsage> usages)
    {
        var lineKeys = usages
            .Select(static usage => usage.Line.LineKey)
            .Where(HasText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
        var publicationIds = usages
            .Select(static usage => usage.Record.Publication?.ManifestId)
            .Where(HasText)
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();
        var lines = lineKeys.Length == 0
            ? "Published or restated report-line provenance references this security."
            : $"Reported lines: {string.Join(", ", lineKeys)}.";
        return publicationIds.Length == 0
            ? lines
            : $"{lines} Publication manifests: {string.Join(", ", publicationIds!)}.";
    }

    private static InstrumentPassportProviderConfidenceDto? SelectBestProviderConfidence(InstrumentPassportDto passport)
        => passport.ProviderConfidence
            .OrderByDescending(static confidence => confidence.ConfidenceScore)
            .FirstOrDefault();

    private static IReadOnlyList<SecurityReportLineUsage> CollectSecurityReportLineUsages(
        WorkstationSecurityReference reference,
        IReadOnlyList<ReportPackWorkflowRecordDto> records)
    {
        var usages = new List<SecurityReportLineUsage>();
        foreach (var record in records.Where(static record => IsAuthoritativelyReported(record)))
        {
            foreach (var line in record.LineProvenance ?? [])
            {
                if (IsSameSecurityReportLine(reference, line))
                {
                    usages.Add(new(record, line));
                }
            }
        }

        return usages;
    }

    private static bool IsSameSecurityReportLine(
        WorkstationSecurityReference reference,
        ReportPackLineProvenanceDto line)
    {
        if (reference.SecurityId != Guid.Empty && HasText(line.SecurityMasterId))
        {
            if (Guid.TryParse(line.SecurityMasterId, out var lineSecurityId) && lineSecurityId == reference.SecurityId)
            {
                return true;
            }

            return false;
        }

        var candidateIdentifiers = new[]
            {
                reference.PrimaryIdentifier,
                reference.MatchedIdentifierValue,
                reference.DisplayName
            }
            .Where(HasText)
            .Select(static value => value!.Trim())
            .ToArray();

        return candidateIdentifiers.Any(identifier =>
            string.Equals(line.SecurityDefinitionId, identifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(line.SourceId, identifier, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildInstrumentPassportHref(Guid securityId)
        => UiApiRoutes.WithParam(UiApiRoutes.WorkstationSecurityMasterInstrumentPassport, "securityId", securityId.ToString("D"));

    private static string BuildReportLineProvenanceHref()
        => UiApiRoutes.WithParam(UiApiRoutes.WorkstationFinancialRecordExplorer, "explorerId", ReportLineProvenanceExplorerId);

    private static string FormatConfidence(decimal score)
    {
        var percentage = score <= 1m ? score * 100m : score;
        return $"{percentage.ToString("0.#", CultureInfo.InvariantCulture)}%";
    }

    private static string FormatAmountCurrency(decimal amount, string currency)
        => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}";

    private static IReadOnlyList<WorkstationSecurityReference> CollectSecurityReferences(StrategyRunDetail detail)
    {
        var portfolioReferences = detail.Portfolio?.Positions.Select(static position => position.Security)
            ?? Enumerable.Empty<WorkstationSecurityReference?>();
        var ledgerReferences = detail.Ledger?.TrialBalance.Select(static line => line.Security)
            ?? Enumerable.Empty<WorkstationSecurityReference?>();
        var references = portfolioReferences
            .Concat(ledgerReferences)
            .Where(static reference => reference is not null)
            .Select(static reference => reference!)
            .GroupBy(static reference => reference.SecurityId == Guid.Empty
                    ? $"{reference.DisplayName}:{reference.PrimaryIdentifier}:{reference.AssetClass}"
                    : reference.SecurityId.ToString("D"),
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static reference => reference.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return references;
    }

    private static bool IsSameSecurity(WorkstationSecurityReference? left, WorkstationSecurityReference right)
    {
        if (left is null)
        {
            return false;
        }

        if (left.SecurityId != Guid.Empty && right.SecurityId != Guid.Empty)
        {
            return left.SecurityId == right.SecurityId;
        }

        return string.Equals(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.PrimaryIdentifier, right.PrimaryIdentifier, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SecurityInstrumentEnrichment(
        InstrumentPassportDto? Passport,
        AssetOperationsDetailDto? Operations,
        AssetOperationsReadinessDto? Readiness,
        IReadOnlyList<DirectLendingOperationsLoanHealthDto> DirectLendingHealth,
        IReadOnlyList<SecurityReportLineUsage> ReportLineUsages,
        IReadOnlyList<InstrumentJournalProof> InstrumentJournalProofs);

    private sealed record SecurityReportLineUsage(
        ReportPackWorkflowRecordDto Record,
        ReportPackLineProvenanceDto Line);
}
