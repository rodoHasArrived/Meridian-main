using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed class CapitalAccountWorkbenchService : ICapitalAccountWorkbenchService
{
    private static readonly IReadOnlyList<string> LiveCapabilities =
    [
        "Investor-level capital account evidence",
        "Governed allocation policy trace from fund-event, subledger, ledger, approval, payment, and report-output evidence",
        "Statement publication, restatement changed-line detail, and restatement evidence lineage",
        "Audit-support drill-through to subledger, evidence, approvals, and report outputs"
    ];

    private static readonly IReadOnlyList<string> PlannedCapabilities =
    [
        "Full cap-table administration",
        "Broad LP portal self-service",
        "Native live-payment execution",
        "Forecasting and backtesting expansion lanes"
    ];

    private readonly IManualJournalEntryWorkbenchService _manualJournalEntryWorkbenchService;
    private readonly ReportPackWorkflowService? _reportPackWorkflowService;

    public CapitalAccountWorkbenchService(
        IManualJournalEntryWorkbenchService manualJournalEntryWorkbenchService,
        ReportPackWorkflowService? reportPackWorkflowService = null)
    {
        _manualJournalEntryWorkbenchService = manualJournalEntryWorkbenchService;
        _reportPackWorkflowService = reportPackWorkflowService;
    }

    public async Task<CapitalAccountWorkbenchDto> GetWorkbenchAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        string? fundEventId = null,
        string? capitalAccountId = null,
        string? investorId = null,
        string? currency = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var normalizedFundEventId = NormalizeOptional(fundEventId);
        var normalizedCapitalAccountId = NormalizeOptional(capitalAccountId);
        var normalizedInvestorId = NormalizeOptional(investorId);
        var normalizedCurrency = NormalizeOptional(currency)?.ToUpperInvariant();
        var activity = await _manualJournalEntryWorkbenchService
            .GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, ct)
            .ConfigureAwait(false);
        var subledgers = FilterSubledgers(activity, normalizedFundEventId, normalizedCapitalAccountId, normalizedInvestorId, normalizedCurrency);
        var validationIssues = BuildValidationIssues(activity, subledgers);
        var investorAccounts = subledgers.Select(BuildInvestorAccount).ToArray();
        var allocationRules = subledgers.SelectMany(BuildAllocationRules).ToArray();
        var statementLineage = subledgers.SelectMany(BuildStatementLineage).ToArray();
        var auditDrillThroughs = BuildAuditDrillThroughs(activity, subledgers, statementLineage);
        var status = BuildStatus(investorAccounts, allocationRules, statementLineage, validationIssues);
        var selectedCurrency = normalizedCurrency ?? subledgers.FirstOrDefault()?.Currency ?? activity.Currency;
        var workbenchRoute = PrivateCapitalActivityRouteBuilder.BuildCapitalAccountWorkbenchRoute(
            activity.FundProfileId,
            activity.LedgerBookId,
            normalizedFundEventId,
            normalizedCapitalAccountId,
            normalizedInvestorId,
            selectedCurrency);

        return new CapitalAccountWorkbenchDto(
            activity.FundProfileId,
            activity.LedgerBookId,
            activity.ProjectedAtUtc,
            normalizedCapitalAccountId,
            normalizedInvestorId,
            selectedCurrency,
            workbenchRoute,
            status.Label,
            status.Reason,
            investorAccounts.Length,
            investorAccounts.Sum(static item => item.FundEventCount),
            statementLineage.Length,
            statementLineage.Count(static item => item.HasRestatementLineage),
            auditDrillThroughs.Count,
            investorAccounts.Sum(static item => item.NetCapitalActivity),
            investorAccounts,
            allocationRules,
            statementLineage,
            auditDrillThroughs,
            validationIssues,
            LiveCapabilities,
            PlannedCapabilities);
    }

    private static IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> FilterSubledgers(
        PrivateCapitalActivityProjectionDto activity,
        string? fundEventId,
        string? capitalAccountId,
        string? investorId,
        string? currency)
    {
        var subledgers = activity.CapitalAccountSubledgers
            .Where(item =>
                Matches(item.CapitalAccountId, capitalAccountId) &&
                Matches(item.InvestorId, investorId) &&
                Matches(item.Currency, currency) &&
                (fundEventId is null || item.FundEventRecords.Any(record => Matches(record.FundEventId, fundEventId))))
            .OrderBy(static item => item.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.InvestorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Currency, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (subledgers.Length > 0)
        {
            return subledgers;
        }

        if (capitalAccountId is null && investorId is null && fundEventId is null && currency is null)
        {
            return activity.CapitalAccountSubledgers;
        }

        return [];
    }

    private static CapitalAccountWorkbenchInvestorAccountDto BuildInvestorAccount(PrivateCapitalCapitalAccountSubledgerDto subledger)
    {
        var readyEvidenceCategoryCount = subledger.EvidenceCategories.Count(static item => item.IsReady);
        var evidenceCategorySummary = subledger.EvidenceCategories.Count == 0
            ? "No allocation evidence categories retained."
            : $"{readyEvidenceCategoryCount}/{subledger.EvidenceCategories.Count} allocation evidence categories ready.";

        return new CapitalAccountWorkbenchInvestorAccountDto(
            BuildAccountKey(subledger),
            subledger.CapitalAccountId,
            subledger.InvestorId,
            subledger.Currency,
            subledger.ActivityRoute,
            subledger.Readiness,
            subledger.ReadinessLabel,
            subledger.ReadinessReason,
            subledger.NextAction,
            subledger.NextActionRoute,
            subledger.OpeningNetActivity,
            subledger.EndingNetActivity,
            subledger.NetCapitalActivity,
            subledger.Contributions,
            subledger.Distributions,
            subledger.Subscriptions,
            subledger.Redemptions,
            subledger.ManagementFees,
            subledger.FundEventCount,
            subledger.PostedFundEventCount,
            subledger.ApprovalQueueCount,
            subledger.PublishedReportOutputCount,
            subledger.EvidenceLinkCount,
            subledger.ValidationIssueCount,
            evidenceCategorySummary,
            subledger.EvidenceLinks,
            subledger.EvidenceCategories,
            subledger.FundEventRecords,
            subledger.SubledgerEntries,
            subledger.LedgerImpacts,
            subledger.ReportOutputs,
            subledger.ValidationIssues,
            subledger.PaymentIntentEvidence);
    }

    private static IEnumerable<CapitalAccountWorkbenchAllocationRuleDto> BuildAllocationRules(
        PrivateCapitalCapitalAccountSubledgerDto subledger)
        => subledger.EvidenceCategories
            .OrderBy(static item => item.CategoryId, StringComparer.OrdinalIgnoreCase)
            .Select(category =>
            {
                var inputs = BuildAllocationInputs(subledger, category.CategoryId);
                var relatedFundEventIds = Normalize(subledger.FundEventRecords.Select(static item => item.FundEventId));

                return new CapitalAccountWorkbenchAllocationRuleDto(
                    $"allocation-rule:{BuildAccountKey(subledger)}:{category.CategoryId}".ToLowerInvariant(),
                    subledger.CapitalAccountId,
                    subledger.InvestorId,
                    category.CategoryId,
                    category.Label,
                    BuildAllocationBasis(category.CategoryId),
                    category.IsReady,
                    category.Summary,
                    ResolveAllocationRoute(subledger, category),
                    category.EvidenceLinkCount,
                    category.EvidenceLinks,
                    category.RequiredEvidence)
                {
                    RuleVersion = BuildAllocationRuleVersion(subledger, category),
                    EffectiveFrom = subledger.FirstEffectiveDate,
                    EffectiveTo = subledger.LastEffectiveDate,
                    Formula = BuildAllocationFormula(category.CategoryId),
                    ApprovalState = BuildAllocationApprovalState(subledger),
                    ApprovalReference = BuildAllocationApprovalReference(subledger),
                    ReplayTrace = BuildAllocationReplayTrace(subledger, inputs.Count),
                    Inputs = inputs,
                    RelatedFundEventIds = relatedFundEventIds
                };
            });

    private IEnumerable<CapitalAccountWorkbenchStatementLineageDto> BuildStatementLineage(
        PrivateCapitalCapitalAccountSubledgerDto subledger)
        => subledger.ReportOutputs
            .OrderByDescending(static item => item.IsPublished)
            .ThenByDescending(static item => item.IsReportReady)
            .ThenBy(static item => item.EffectiveDate)
            .ThenBy(static item => item.ReportOutputId, StringComparer.OrdinalIgnoreCase)
            .Select(output =>
            {
                var workflow = ResolveReportPackRecord(output.ReportPackId);
                var restatement = workflow?.Restatement;
                var restatementEvidenceLinks = NormalizeRestatementEvidence(restatement);
                var restatementChangedLines = BuildRestatementChangedLines(restatement);
                var hasRestatementLineage = restatement is not null || restatementEvidenceLinks.Count > 0;

                return new CapitalAccountWorkbenchStatementLineageDto(
                    $"statement-lineage:{output.ReportOutputId}",
                    output.CapitalAccountId,
                    output.InvestorId,
                    output.ReportOutputId,
                    output.ReportOutputType,
                    output.DisplayName,
                    output.ReportRoute,
                    output.ReportPackId,
                    workflow?.State.ToString() ?? output.ReportWorkflowState,
                    output.IsPublished,
                    output.IsReportReady,
                    output.PublicationManifestId ?? workflow?.Publication?.ManifestId,
                    output.RetainedManifestPath ?? workflow?.Publication?.RetainedManifestPath,
                    output.PublicationEvidenceHash ?? workflow?.Publication?.EvidenceHash,
                    output.PublishedAtUtc ?? workflow?.Publication?.SignedOffAt,
                    output.PublishedBy ?? workflow?.Publication?.SignedOffBy,
                    output.ReportLineProvenanceCount == 0
                        ? workflow?.LineProvenance?.Count ?? 0
                        : output.ReportLineProvenanceCount,
                    hasRestatementLineage,
                    hasRestatementLineage ? "Restatement lineage retained." : "No restatement lineage retained.",
                    restatement?.ReasonCode,
                    restatement?.PriorVersionReportId,
                    restatement?.Approver,
                    restatement?.ChangedLines.Count ?? 0,
                    restatementEvidenceLinks.Count,
                    output.ReportOutputRoute,
                    output.EvidenceRoute,
                    output.CapitalAccountSubledgerRoute,
                    output.EvidenceLinks,
                    restatementEvidenceLinks)
                {
                    RestatementChangedLines = restatementChangedLines
                };
            });

    private IReadOnlyList<CapitalAccountWorkbenchAuditDrillThroughDto> BuildAuditDrillThroughs(
        PrivateCapitalActivityProjectionDto activity,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> subledgers,
        IReadOnlyList<CapitalAccountWorkbenchStatementLineageDto> statementLineage)
    {
        var rows = new List<CapitalAccountWorkbenchAuditDrillThroughDto>
        {
            new(
                "private-capital-activity",
                "activity",
                "Private-capital activity projection",
                "Shared projection backing fund events, capital-account subledger entries, ledger impact, evidence, and report output.",
                PrivateCapitalActivityRouteBuilder.BuildCapitalAccountWorkbenchRoute(activity.FundProfileId, activity.LedgerBookId),
                true,
                activity.FundEvents.SelectMany(static item => item.EvidenceLinks).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Normalize(activity.FundEvents.SelectMany(static item => item.EvidenceLinks)),
                activity.FundEvents.Select(static item => item.FundEventId).ToArray())
        };

        foreach (var subledger in subledgers)
        {
            rows.Add(new(
                $"subledger:{BuildAccountKey(subledger)}",
                "subledger",
                $"{subledger.CapitalAccountId} subledger",
                subledger.ReadinessReason,
                subledger.ActivityRoute,
                true,
                subledger.EvidenceLinkCount,
                subledger.EvidenceLinks,
                subledger.FundEventRecords.Select(static item => item.FundEventId).ToArray()));

            foreach (var record in subledger.FundEventRecords)
            {
                rows.Add(new(
                    $"fund-event:{record.FundEventId}",
                    "fund-event",
                    $"{record.FundEventType} evidence packet",
                    record.ReadinessReason,
                    record.EvidenceRoute,
                    !string.IsNullOrWhiteSpace(record.EvidenceRoute),
                    record.EvidenceLinkCount,
                    record.EvidenceLinks,
                    [record.FundEventId, record.JournalEntryId.ToString("D")]));

                if (!string.IsNullOrWhiteSpace(record.ApprovalRoute))
                {
                    rows.Add(new(
                        $"approval:{record.ApprovalId ?? record.JournalEntryId.ToString("D")}",
                        "approval",
                        $"{record.FundEventType} approval",
                        "Approval state is retained as part of the private-capital audit support chain.",
                        record.ApprovalRoute,
                        true,
                        record.ApprovalId is null ? 0 : 1,
                        Normalize([record.ApprovalId ?? string.Empty, record.ApprovalRoute]),
                        [record.FundEventId, record.JournalEntryId.ToString("D")]));
                }
            }
        }

        foreach (var lineage in statementLineage)
        {
            rows.Add(new(
                $"report-output:{lineage.ReportOutputId}",
                "statement",
                $"{lineage.DisplayName} statement lineage",
                lineage.HasRestatementLineage ? lineage.RestatementStatus : "Published statement route and retained manifest metadata.",
                lineage.ReportOutputRoute ?? lineage.ReportRoute,
                !string.IsNullOrWhiteSpace(lineage.ReportOutputRoute ?? lineage.ReportRoute),
                lineage.EvidenceLinks.Count,
                lineage.EvidenceLinks,
                Normalize([lineage.ReportOutputId, lineage.ReportPackId ?? string.Empty, lineage.PublicationManifestId ?? string.Empty])));

            if (!string.IsNullOrWhiteSpace(lineage.RetainedManifestPath))
            {
                rows.Add(new(
                    $"manifest:{lineage.ReportOutputId}",
                    "manifest",
                    $"{lineage.DisplayName} retained manifest",
                    "Retained publication manifest path and evidence hash are available for audit support.",
                    lineage.RetainedManifestPath,
                    true,
                    string.IsNullOrWhiteSpace(lineage.PublicationEvidenceHash) ? 0 : 1,
                    Normalize([lineage.PublicationEvidenceHash ?? string.Empty]),
                    Normalize([lineage.ReportOutputId, lineage.PublicationManifestId ?? string.Empty])));
            }

            if (lineage.RestatementEvidenceLinks.Count > 0)
            {
                rows.Add(new(
                    $"restatement:{lineage.ReportOutputId}",
                    "restatement",
                    $"{lineage.DisplayName} restatement evidence",
                    lineage.RestatementStatus,
                    lineage.EvidenceRoute ?? lineage.ReportOutputRoute,
                    true,
                    lineage.RestatementEvidenceLinks.Count,
                    lineage.RestatementEvidenceLinks,
                    Normalize([lineage.ReportOutputId, lineage.RestatementPriorVersionReportId?.ToString("D") ?? string.Empty])));
            }
        }

        return rows
            .GroupBy(static item => item.DrillThroughId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static item => item.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ReportPackWorkflowRecordDto? ResolveReportPackRecord(string? reportPackId)
        => Guid.TryParse(reportPackId, out var parsed)
            ? _reportPackWorkflowService?.GetRecord(parsed)
            : null;

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildValidationIssues(
        PrivateCapitalActivityProjectionDto activity,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> subledgers)
    {
        var fundEventIds = subledgers
            .SelectMany(static item => item.FundEventRecords)
            .Select(static item => item.FundEventId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var capitalAccountIds = subledgers
            .Select(static item => item.CapitalAccountId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return subledgers
            .SelectMany(static item => item.ValidationIssues)
            .Concat(activity.ValidationIssues.Where(issue =>
                issue.TargetId is not null &&
                (fundEventIds.Contains(issue.TargetId) || capitalAccountIds.Contains(issue.TargetId))))
            .DistinctBy(static item => $"{item.Severity}|{item.Code}|{item.TargetId}|{item.Message}|{item.SuggestedAction}")
            .OrderByDescending(static item => item.Severity)
            .ThenBy(static item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static (string Label, string Reason) BuildStatus(
        IReadOnlyList<CapitalAccountWorkbenchInvestorAccountDto> investorAccounts,
        IReadOnlyList<CapitalAccountWorkbenchAllocationRuleDto> allocationRules,
        IReadOnlyList<CapitalAccountWorkbenchStatementLineageDto> statementLineage,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues)
    {
        if (investorAccounts.Count == 0)
        {
            return ("No capital accounts", "No investor-level capital accounts matched the selected private-capital filters.");
        }

        if (validationIssues.Any(static item => item.Severity == AccountingConfigurationValidationSeverityDto.Critical) ||
            investorAccounts.Any(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Blocked))
        {
            return ("Blocked", "A critical validation issue or blocked fund-event record prevents audit-ready reliance.");
        }

        if (allocationRules.Any(static item => !item.IsSatisfied))
        {
            return ("Evidence review", "One or more allocation evidence categories still need retained support.");
        }

        if (statementLineage.Any(static item => item.HasRestatementLineage))
        {
            return ("Restated lineage", "Statement lineage includes retained restatement metadata and audit evidence.");
        }

        return ("Ready", "Investor capital-account evidence, allocation rules, statement lineage, and audit drill-throughs are available.");
    }

    private static string BuildAllocationBasis(string categoryId)
        => categoryId switch
        {
            "source-support" => "Fund event source support must be retained before allocation reliance.",
            "capital-account-subledger" => "Capital-account movements must be represented in the running investor subledger.",
            "ledger-impact" => "Ledger impact must be balanced and posting ready.",
            "approval-state" => "Fund-event approval references must be retained.",
            "payment-intent" => "Payment intent references must be captured when a cash movement is expected.",
            "cash-evidence" => "Cash, bank, settlement, or treasury support must be retained for cash movement audit support.",
            "report-output" => "Governed statement or report output must be ready and evidence-linked.",
            _ => "Private-capital allocation support must retain evidence for this category."
        };

    private static string BuildAllocationRuleVersion(
        PrivateCapitalCapitalAccountSubledgerDto subledger,
        PrivateCapitalEvidenceCategoryDto category)
        => $"{category.CategoryId}:projection:{subledger.FundEventCount}.{subledger.SubledgerEntries.Count}.{subledger.LedgerImpacts.Count}.{subledger.ReportOutputs.Count}";

    private static string BuildAllocationFormula(string categoryId)
        => categoryId switch
        {
            "source-support" => "fund_event.source_evidence_count > 0",
            "capital-account-subledger" => "capital_account_subledger.entry_count > 0 and subledger_evidence_count > 0",
            "ledger-impact" => "ledger_impact.is_balanced and ledger_impact.is_posting_ready and ledger_line_evidence_count > 0",
            "approval-state" => "all(fund_event.approval_state in approved_states) and approval_reference_count > 0",
            "payment-intent" => "payment_intent_id exists when cash movement is expected",
            "cash-evidence" => "cash_evidence_link_count > 0 and settlement_reference is retained",
            "report-output" => "all(report_output.is_report_ready) and report_evidence_link_count > 0",
            _ => "required_evidence_count == retained_evidence_count"
        };

    private static string BuildAllocationApprovalState(PrivateCapitalCapitalAccountSubledgerDto subledger)
    {
        if (subledger.FundEventRecords.Count == 0)
        {
            return "No fund-event approvals";
        }

        var states = subledger.FundEventRecords
            .Select(static item => item.ApprovalState.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return states.Length == 1 ? states[0] : string.Join(" / ", states);
    }

    private static string? BuildAllocationApprovalReference(PrivateCapitalCapitalAccountSubledgerDto subledger)
    {
        var references = Normalize(subledger.FundEventRecords
            .SelectMany(static item => new[] { item.ApprovalId ?? string.Empty, item.ApprovalRoute ?? string.Empty }));
        return references.Count == 0 ? null : string.Join(" / ", references);
    }

    private static string BuildAllocationReplayTrace(
        PrivateCapitalCapitalAccountSubledgerDto subledger,
        int inputCount)
        => $"Trace uses {subledger.FundEventRecords.Count} fund event(s), {subledger.SubledgerEntries.Count} subledger entry(ies), {subledger.LedgerImpacts.Count} ledger impact(s), {subledger.ReportOutputs.Count} report output(s), and {inputCount} allocation input(s).";

    private static IReadOnlyList<CapitalAccountWorkbenchAllocationInputDto> BuildAllocationInputs(
        PrivateCapitalCapitalAccountSubledgerDto subledger,
        string categoryId)
        => categoryId switch
        {
            "capital-account-subledger" => subledger.SubledgerEntries
                .Select(static item => new CapitalAccountWorkbenchAllocationInputDto(
                    $"allocation-input:subledger:{item.SubledgerEntryId}".ToLowerInvariant(),
                    "subledger-entry",
                    item.SubledgerEntryId,
                    $"{item.FundEventType} / {item.Memo}",
                    item.NetCapitalActivity,
                    item.Currency,
                    item.EffectiveDate,
                    item.EvidenceLinks.FirstOrDefault()))
                .ToArray(),
            "ledger-impact" => subledger.LedgerImpacts
                .Select(static item => new CapitalAccountWorkbenchAllocationInputDto(
                    $"allocation-input:ledger:{item.LedgerImpactId}".ToLowerInvariant(),
                    "ledger-impact",
                    item.LedgerImpactId,
                    $"{item.FundEventType} / {item.LineCount} line(s)",
                    item.TotalCredits - item.TotalDebits,
                    item.Currency,
                    item.EffectiveDate,
                    item.EvidenceLinks.FirstOrDefault() ??
                    item.Lines.Select(static line => line.EvidenceLink).FirstOrDefault(static link => !string.IsNullOrWhiteSpace(link))))
                .ToArray(),
            "approval-state" => subledger.FundEventRecords
                .Select(static item => new CapitalAccountWorkbenchAllocationInputDto(
                    $"allocation-input:approval:{item.FundEventId}".ToLowerInvariant(),
                    "approval",
                    item.ApprovalId ?? item.FundEventId,
                    $"{item.FundEventType} / {item.ApprovalState}",
                    item.NetCapitalActivity,
                    item.Currency,
                    item.EffectiveDate,
                    item.ApprovalRoute ?? item.EvidenceRoute))
                .ToArray(),
            "payment-intent" or "cash-evidence" => subledger.PaymentIntentEvidence is { } paymentIntent
                ? [new CapitalAccountWorkbenchAllocationInputDto(
                    $"allocation-input:payment:{paymentIntent.PaymentIntentId ?? paymentIntent.SettlementReference ?? BuildAccountKey(subledger)}".ToLowerInvariant(),
                    "payment-intent",
                    paymentIntent.PaymentIntentId ?? paymentIntent.SettlementReference ?? BuildAccountKey(subledger),
                    paymentIntent.Summary,
                    paymentIntent.Amount,
                    paymentIntent.Currency,
                    paymentIntent.EffectiveDate,
                    paymentIntent.EvidenceRoute)]
                : [],
            "report-output" => subledger.ReportOutputs
                .Select(static item => new CapitalAccountWorkbenchAllocationInputDto(
                    $"allocation-input:report:{item.ReportOutputId}".ToLowerInvariant(),
                    "report-output",
                    item.ReportOutputId,
                    $"{item.ReportOutputType} / {item.DisplayName}",
                    item.NetCapitalActivity,
                    item.Currency,
                    item.EffectiveDate,
                    item.ReportOutputRoute ?? item.ReportRoute))
                .ToArray(),
            _ => subledger.FundEventRecords
                .Select(static item => new CapitalAccountWorkbenchAllocationInputDto(
                    $"allocation-input:fund-event:{item.FundEventId}".ToLowerInvariant(),
                    "fund-event",
                    item.FundEventId,
                    $"{item.FundEventType} / {item.Memo}",
                    item.NetCapitalActivity,
                    item.Currency,
                    item.EffectiveDate,
                    item.EvidenceRoute))
                .ToArray()
        };

    private static string? ResolveAllocationRoute(
        PrivateCapitalCapitalAccountSubledgerDto subledger,
        PrivateCapitalEvidenceCategoryDto category)
        => category.CategoryId switch
        {
            "source-support" => subledger.FundEventRecords.FirstOrDefault()?.EvidenceRoute ?? subledger.ActivityRoute,
            "capital-account-subledger" => subledger.ActivityRoute,
            "ledger-impact" => subledger.LedgerImpacts.SelectMany(static item => item.EvidenceLinks).FirstOrDefault()
                ?? subledger.LedgerImpacts.SelectMany(static item => item.Lines).Select(static item => item.EvidenceLink).FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item))
                ?? subledger.ActivityRoute,
            "approval-state" => subledger.FundEventRecords.Select(static item => item.ApprovalRoute).FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item))
                ?? subledger.ActivityRoute,
            "payment-intent" or "cash-evidence" => subledger.PaymentIntentEvidence?.EvidenceRoute ?? subledger.ActivityRoute,
            "report-output" => subledger.ReportOutputs.Select(static item => item.ReportOutputRoute ?? item.ReportRoute).FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item))
                ?? subledger.ActivityRoute,
            _ => subledger.ActivityRoute
        };

    private static IReadOnlyList<string> NormalizeRestatementEvidence(ReportPackRestatementMetadataDto? restatement)
        => Normalize((restatement?.EvidenceLinks ?? [])
            .Select(static link => link.Route ?? link.EvidenceId)
            .Concat((restatement?.ChangedLines ?? [])
                .SelectMany(static line => line.EvidenceLinks ?? [])
                .Select(static link => link.Route ?? link.EvidenceId)));

    private static IReadOnlyList<CapitalAccountWorkbenchRestatementChangedLineDto> BuildRestatementChangedLines(
        ReportPackRestatementMetadataDto? restatement)
        => (restatement?.ChangedLines ?? [])
            .OrderBy(static line => line.LineKey, StringComparer.OrdinalIgnoreCase)
            .Select(static line =>
            {
                var evidenceLinks = Normalize((line.EvidenceLinks ?? []).Select(static link => link.Route ?? link.EvidenceId));
                return new CapitalAccountWorkbenchRestatementChangedLineDto(
                    line.LineKey,
                    line.PreviousValue,
                    line.CurrentValue,
                    evidenceLinks.Count,
                    evidenceLinks);
            })
            .ToArray();

    private static string BuildAccountKey(PrivateCapitalCapitalAccountSubledgerDto subledger)
        => $"{subledger.CapitalAccountId}|{subledger.InvestorId ?? "unassigned"}|{subledger.Currency}";

    private static bool Matches(string? value, string? filter)
        => filter is null || string.Equals(value ?? string.Empty, filter, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> Normalize(IEnumerable<string> values)
        => values
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
