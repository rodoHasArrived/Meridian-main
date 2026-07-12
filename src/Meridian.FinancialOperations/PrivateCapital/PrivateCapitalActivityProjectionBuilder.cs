using Meridian.Contracts.Banking;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.PrivateCapital;

public sealed record PrivateCapitalActivityProjectionInput(
    string FundProfileId,
    Guid? LedgerBookId,
    IReadOnlyList<ManualJournalEntryDraftDto> Drafts,
    IReadOnlyList<AccountingActionAuditEventDto> Audit,
    IReadOnlyList<BankTransactionDto> BankTransactions,
    PostedPrivateCapitalActivityProjection? PostedProjection,
    IReadOnlyList<ReportPackWorkflowRecordDto>? ReportPackWorkflowRecords = null)
{
    public IReadOnlyList<ReportPackWorkflowRecordDto> ReportPackWorkflowRecords { get; init; } =
        ReportPackWorkflowRecords ?? [];
}

public sealed record PostedPrivateCapitalActivityProjection(
    PrivateCapitalFundEventLedgerProjection Projection,
    IReadOnlyDictionary<Guid, string> JournalEntryCurrencies);

public static class PrivateCapitalActivityProjectionBuilder
{
    private const decimal BalanceTolerance = LedgerToleranceConstants.Balance;
    private static readonly IReadOnlyDictionary<Guid, string> EmptyJournalEntryCurrencies = new Dictionary<Guid, string>();

    private sealed record PrivateCapitalCapitalAccountSubledgerSource(
        string SubledgerEntryId,
        string CapitalAccountId,
        string? InvestorId,
        string Currency,
        string FundEventId,
        string FundEventType,
        ManualJournalEntryTypeDto EntryType,
        ManualJournalEntryStatusDto ApprovalState,
        Guid JournalEntryId,
        DateOnly EffectiveDate,
        decimal GrossAmount,
        decimal NetCapitalActivity,
        string Memo,
        IReadOnlyList<string> EvidenceLinks,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
        DateTimeOffset UpdatedAtUtc,
        bool IsPosted);

    private sealed record PrivateCapitalReportOutputAccountScope(
        string CapitalAccountId,
        string? InvestorId,
        decimal NetCapitalActivity);

    private sealed record PrivateCapitalReportOutputReadinessProjection(
        string Label,
        string Reason,
        string NextAction,
        string? NextActionRoute);
    public static PrivateCapitalActivityProjectionDto Build(PrivateCapitalActivityProjectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var fundProfileId = input.FundProfileId;
        var ledgerBookId = input.LedgerBookId;
        var drafts = input.Drafts;
        var audit = input.Audit;
        var bankTransactions = input.BankTransactions;
        var postedProjection = input.PostedProjection;
        var projectedAtUtc = DateTimeOffset.UtcNow;
        var fundEvents = new List<PrivateCapitalFundEventDto>();
        var ledgerImpacts = new List<PrivateCapitalLedgerImpactDto>();
        var projectionIssues = new List<AccountingConfigurationValidationIssueDto>();

        foreach (var draft in drafts)
        {
            var context = NormalizeTreasuryContext(draft.TreasuryContext, draft.AccountingDate);
            if (!IsPrivateCapitalActivityCandidate(draft.EntryType, context))
            {
                continue;
            }

            if (context?.EffectiveDate is null ||
                string.IsNullOrWhiteSpace(context.FundEventId) ||
                string.IsNullOrWhiteSpace(context.FundEventType) ||
                string.IsNullOrWhiteSpace(context.CapitalAccountId))
            {
                projectionIssues.Add(Issue(
                    "manual-je.private-capital-context-pending",
                    AccountingConfigurationValidationSeverityDto.Warning,
                    "Private-capital activity projection skipped a manual journal entry because fund event or capital account context is incomplete.",
                    draft.JournalEntryId.ToString("D"),
                    "Validate and complete treasury ledger context before relying on capital-account activity."));
                continue;
            }

            var grossAmount = Math.Max(Math.Abs(draft.TotalDebits), Math.Abs(draft.TotalCredits));
            if (grossAmount == 0m)
            {
                var debitAmount = draft.Lines
                    .Where(line => line.Side == AccountingTemplateLineSideDto.Debit)
                    .Sum(line => Math.Abs(line.Amount));
                var creditAmount = draft.Lines
                    .Where(line => line.Side == AccountingTemplateLineSideDto.Credit)
                    .Sum(line => Math.Abs(line.Amount));
                grossAmount = Math.Max(debitAmount, creditAmount);
            }

            var evidenceLinks = MergeEvidenceLinks(
                draft.EvidenceLinks,
                draft.EvidenceAttachments?.Select(attachment => attachment.Uri).ToArray());

            ledgerImpacts.Add(BuildPrivateCapitalLedgerImpact(draft, context, evidenceLinks));

            fundEvents.Add(new PrivateCapitalFundEventDto(
                context.FundEventId,
                context.FundEventType,
                draft.EntryType,
                draft.Status,
                draft.JournalEntryId,
                context.EffectiveDate.Value,
                context.CapitalAccountId,
                NormalizeOptional(context.InvestorId),
                string.IsNullOrWhiteSpace(draft.Currency) ? "USD" : draft.Currency.Trim().ToUpperInvariant(),
                grossAmount,
                CalculateNetCapitalActivity(draft.EntryType, grossAmount),
                draft.Memo?.Trim() ?? string.Empty,
                NormalizeOptional(context.PaymentIntentId),
                NormalizeOptional(context.SettlementReference),
                evidenceLinks,
                draft.ValidationIssues,
                draft.UpdatedAtUtc,
                ApprovalId: NormalizeOptional(draft.ApprovalId)));
        }

        var orderedFundEvents = fundEvents
            .OrderByDescending(item => item.EffectiveDate)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var postedJournalEntryCurrencies = postedProjection?.JournalEntryCurrencies ?? EmptyJournalEntryCurrencies;
        var postedLedgerEvents = postedProjection?.Projection.Events ?? [];
        var postedEvents = postedLedgerEvents
            .Select(item => MapPostedFundEvent(item, postedJournalEntryCurrencies))
            .ToArray();
        var postedEventIds = postedEvents
            .Select(static item => item.FundEventId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeDraftFundEvents = orderedFundEvents
            .Where(item => !postedEventIds.Contains(item.FundEventId))
            .ToArray();
        var combinedFundEvents = activeDraftFundEvents
            .Concat(postedEvents)
            .OrderByDescending(item => item.EffectiveDate)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var capitalAccountSubledgerEntries = BuildCapitalAccountSubledgerEntries(
            activeDraftFundEvents
                .Select(MapFundEventSubledgerSource)
                .Concat(postedLedgerEvents.SelectMany(item => MapPostedCapitalAccountSubledgerSources(item, postedJournalEntryCurrencies)))
                .ToArray());
        var capitalAccounts = BuildCapitalAccounts(capitalAccountSubledgerEntries);
        var orderedLedgerImpacts = ledgerImpacts
            .Where(item => !postedEventIds.Contains(item.FundEventId))
            .Concat(postedLedgerEvents.SelectMany(item => MapPostedLedgerImpacts(item, postedJournalEntryCurrencies)))
            .OrderByDescending(item => item.EffectiveDate)
            .ThenBy(item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.JournalEntryId)
            .ToArray();
        var projectionCurrency = combinedFundEvents
            .Select(item => item.Currency)
            .FirstOrDefault(currency => !string.IsNullOrWhiteSpace(currency)) ?? string.Empty;
        var reportPackWorkflowRecords = input.ReportPackWorkflowRecords;
        var postingReadyByFundEventId = BuildPostingReadyByFundEventId(orderedLedgerImpacts);
        var reportOutputs = orderedFundEvents
            .Where(item => !postedEventIds.Contains(item.FundEventId))
            .Select(item => BuildPrivateCapitalReportOutput(fundProfileId, ledgerBookId, item, postingReadyByFundEventId))
            .Concat(BuildPostedReportOutputs(fundProfileId, ledgerBookId, postedLedgerEvents, postedJournalEntryCurrencies, reportPackWorkflowRecords))
            .OrderByDescending(item => item.EffectiveDate)
            .ThenBy(item => item.ReportOutputType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ReportOutputId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var publishedReportOutputCount = CountPublishedReportOutputs(
            fundProfileId,
            postedLedgerEvents,
            reportPackWorkflowRecords);
        var fundEventRecords = PrivateCapitalFundEventLedgerRecordBuilder.Build(
            fundProfileId,
            combinedFundEvents,
            capitalAccountSubledgerEntries,
            orderedLedgerImpacts,
            reportOutputs);
        var capitalAccountSubledgers = PrivateCapitalCapitalAccountSubledgerBuilder.Build(
            fundProfileId,
            ledgerBookId,
            projectedAtUtc,
            capitalAccounts,
            fundEventRecords,
            capitalAccountSubledgerEntries,
            orderedLedgerImpacts,
            reportOutputs,
            projectionIssues);
        var paymentIntents = BuildPaymentIntentWorkflows(
            fundProfileId,
            ledgerBookId,
            fundEventRecords,
            drafts,
            audit,
            bankTransactions);

        return new PrivateCapitalActivityProjectionDto(
            fundProfileId,
            ledgerBookId,
            projectedAtUtc,
            combinedFundEvents.Length,
            capitalAccounts.Count,
            combinedFundEvents.Count(item => item.JournalStatus is ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved),
            combinedFundEvents.Count(item => item.JournalStatus == ManualJournalEntryStatusDto.Submitted),
            postedEvents.Length,
            publishedReportOutputCount,
            combinedFundEvents.Sum(item => item.NetCapitalActivity),
            projectionCurrency,
            combinedFundEvents,
            capitalAccounts,
            capitalAccountSubledgerEntries,
            orderedLedgerImpacts,
            reportOutputs,
            projectionIssues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.TargetId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            fundEventRecords,
            capitalAccountSubledgers,
            paymentIntents);
    }

    private static IReadOnlyList<PaymentIntentWorkflowDto> BuildPaymentIntentWorkflows(
        string fundProfileId,
        Guid? ledgerBookId,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> fundEventRecords,
        IReadOnlyList<ManualJournalEntryDraftDto> drafts,
        IReadOnlyList<AccountingActionAuditEventDto> audit,
        IReadOnlyList<BankTransactionDto> bankTransactions)
    {
        var draftsByJournalEntryId = drafts.ToDictionary(static draft => draft.JournalEntryId);
        return fundEventRecords
            .Where(static record => !string.IsNullOrWhiteSpace(record.PaymentIntentId))
            .GroupBy(static record => record.PaymentIntentId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildPaymentIntentWorkflow(
                fundProfileId,
                ledgerBookId,
                group.Key,
                group
                    .OrderByDescending(static record => record.EffectiveDate)
                    .ThenBy(static record => record.FundEventId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                draftsByJournalEntryId,
                audit,
                bankTransactions))
            .OrderByDescending(static workflow => workflow.ExpectedCashMovement.EffectiveDate)
            .ThenBy(static workflow => workflow.PaymentIntentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PaymentIntentWorkflowDto BuildPaymentIntentWorkflow(
        string fundProfileId,
        Guid? ledgerBookId,
        string paymentIntentId,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyDictionary<Guid, ManualJournalEntryDraftDto> draftsByJournalEntryId,
        IReadOnlyList<AccountingActionAuditEventDto> audit,
        IReadOnlyList<BankTransactionDto> bankTransactions)
    {
        var primary = records[0];
        draftsByJournalEntryId.TryGetValue(primary.JournalEntryId, out var primaryDraft);
        var settlementReference = NormalizeOptional(primary.SettlementReference)
            ?? records.Select(static record => NormalizeOptional(record.SettlementReference))
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var allEvidenceLinks = BuildPaymentIntentEvidenceLinks(records, draftsByJournalEntryId, audit, paymentIntentId, settlementReference);
        var expectedCashMovement = BuildExpectedCashMovement(
            fundProfileId,
            ledgerBookId,
            paymentIntentId,
            settlementReference,
            records,
            allEvidenceLinks);
        var approvalChain = BuildPaymentIntentApprovalChain(records, draftsByJournalEntryId);
        var bankEvidence = BuildPaymentIntentBankEvidence(paymentIntentId, settlementReference, records, bankTransactions, allEvidenceLinks);
        var reconciliationLinks = BuildPaymentIntentReconciliationLinks(paymentIntentId, settlementReference, records, allEvidenceLinks);
        var auditHistory = BuildPaymentIntentAuditHistory(
            paymentIntentId,
            settlementReference,
            records,
            draftsByJournalEntryId,
            audit,
            bankEvidence,
            reconciliationLinks);
        var status = ResolvePaymentIntentWorkflowStatus(records, approvalChain, bankEvidence, reconciliationLinks);
        var evidenceRoute = PrivateCapitalActivityRoutes.BuildPaymentIntentEvidenceRoute(paymentIntentId);
        var workbenchRoute = PrivateCapitalActivityRoutes.BuildPaymentIntentWorkbenchRoute(fundProfileId, ledgerBookId, paymentIntentId);
        var requester = NormalizeOptional(primaryDraft?.PreparedBy)
            ?? NormalizeOptional(primaryDraft?.SubmittedBy)
            ?? "ledger-posting";
        var requestedAtUtc = primaryDraft?.CreatedAtUtc ?? primary.FundEvent.UpdatedAtUtc;

        return new PaymentIntentWorkflowDto(
            paymentIntentId,
            settlementReference,
            fundProfileId,
            ledgerBookId,
            primary.FundEventId,
            primary.JournalEntryId,
            requester,
            requestedAtUtc,
            status,
            FormatPaymentIntentWorkflowStatus(status),
            BuildPaymentIntentReadinessReason(status, records, bankEvidence, reconciliationLinks),
            "Full payment execution is explicitly deferred in v0.18; this layer only retains intent, control, cash-evidence, reconciliation, and audit history before any bank-side instruction.",
            expectedCashMovement,
            evidenceRoute,
            workbenchRoute,
            approvalChain,
            bankEvidence,
            reconciliationLinks,
            auditHistory);
    }

    private static PaymentIntentExpectedCashMovementDto BuildExpectedCashMovement(
        string fundProfileId,
        Guid? ledgerBookId,
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<string> evidenceLinks)
    {
        var primary = records[0];
        var netActivity = records.Sum(static record => record.NetCapitalActivity);
        var amount = records.Count == 1
            ? Math.Abs(primary.NetCapitalActivity)
            : records.Sum(static record => Math.Abs(record.NetCapitalActivity));
        var direction = ResolvePaymentIntentDirection(records, netActivity);
        var currency = records
            .Select(static record => NormalizeCurrency(record.Currency))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "USD";
        var purpose = records.Count == 1
            ? $"{primary.FundEventType} for {primary.CapitalAccountId}"
            : $"{records.Count} private-capital fund events";
        var payee = ResolvePaymentIntentPayee(fundProfileId, primary, direction);
        var accountScope = BuildPaymentIntentAccountScope(fundProfileId, ledgerBookId, primary);
        var businessPurpose = NormalizeOptional(primary.Memo) ?? purpose;
        var approvalPolicy = ResolvePaymentIntentApprovalPolicy(records);

        return new PaymentIntentExpectedCashMovementDto(
            paymentIntentId,
            direction,
            amount,
            currency,
            records.Select(static record => record.EffectiveDate).DefaultIfEmpty(DateOnly.MinValue).Max(),
            settlementReference,
            primary.FundEventId,
            primary.FundEventType,
            primary.CapitalAccountId,
            primary.InvestorId,
            purpose,
            payee,
            accountScope,
            businessPurpose,
            approvalPolicy,
            BuildPaymentIntentSourceEvidenceLinks(records, evidenceLinks));
    }

    private static string ResolvePaymentIntentPayee(
        string fundProfileId,
        PrivateCapitalFundEventLedgerRecordDto primary,
        PaymentIntentCashDirectionDto direction)
    {
        if (direction == PaymentIntentCashDirectionDto.Inflow)
        {
            return $"fund:{fundProfileId}";
        }

        return NormalizeOptional(primary.InvestorId)
            ?? NormalizeOptional(primary.CapitalAccountId)
            ?? $"fund:{fundProfileId}";
    }

    private static string BuildPaymentIntentAccountScope(
        string fundProfileId,
        Guid? ledgerBookId,
        PrivateCapitalFundEventLedgerRecordDto primary)
    {
        var parts = new List<string> { $"fund:{fundProfileId}" };
        if (ledgerBookId.HasValue)
        {
            parts.Add($"book:{ledgerBookId.Value:D}");
        }

        parts.Add(primary.CapitalAccountId);
        if (!string.IsNullOrWhiteSpace(primary.InvestorId))
        {
            parts.Add(primary.InvestorId);
        }

        return string.Join(" / ", parts);
    }

    private static string ResolvePaymentIntentApprovalPolicy(IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        if (records.Any(static record => record.ApprovalState is ManualJournalEntryStatusDto.Approved))
        {
            return "Controller approval retained before execution-deferred reliance";
        }

        if (records.Any(static record => record.ApprovalState is ManualJournalEntryStatusDto.Submitted))
        {
            return "Controller approval pending before execution-deferred reliance";
        }

        return "Controller approval required before execution-deferred reliance";
    }

    private static IReadOnlyList<string> BuildPaymentIntentSourceEvidenceLinks(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<string> evidenceLinks)
    {
        return records
            .Select(static record => record.ActivityRoute)
            .Concat(records.Select(static record => record.EvidenceRoute))
            .Concat(evidenceLinks)
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PaymentIntentCashDirectionDto ResolvePaymentIntentDirection(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        decimal netActivity)
    {
        if (records.All(static record => record.FundEvent.EntryType is ManualJournalEntryTypeDto.CapitalCall or ManualJournalEntryTypeDto.Subscription))
        {
            return PaymentIntentCashDirectionDto.Inflow;
        }

        if (records.All(static record => record.FundEvent.EntryType is ManualJournalEntryTypeDto.Distribution or ManualJournalEntryTypeDto.Redemption or ManualJournalEntryTypeDto.ManagementFee))
        {
            return PaymentIntentCashDirectionDto.Outflow;
        }

        return netActivity > 0m
            ? PaymentIntentCashDirectionDto.Inflow
            : netActivity < 0m
                ? PaymentIntentCashDirectionDto.Outflow
                : PaymentIntentCashDirectionDto.Neutral;
    }

    private static IReadOnlyList<PaymentIntentApprovalStepDto> BuildPaymentIntentApprovalChain(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyDictionary<Guid, ManualJournalEntryDraftDto> draftsByJournalEntryId)
    {
        var steps = new List<PaymentIntentApprovalStepDto>();
        var sequence = 1;
        foreach (var record in records.OrderBy(static record => record.EffectiveDate).ThenBy(static record => record.FundEventId, StringComparer.OrdinalIgnoreCase))
        {
            draftsByJournalEntryId.TryGetValue(record.JournalEntryId, out var draft);
            var requester = NormalizeOptional(draft?.PreparedBy) ?? "ledger-posting";
            steps.Add(new PaymentIntentApprovalStepDto(
                sequence++,
                "Requester",
                requester,
                "Requested",
                draft?.CreatedAtUtc,
                record.ActivityRoute));

            if (draft?.SubmittedAtUtc is not null || !string.IsNullOrWhiteSpace(draft?.SubmittedBy) || !string.IsNullOrWhiteSpace(record.ApprovalId))
            {
                steps.Add(new PaymentIntentApprovalStepDto(
                    sequence++,
                    "Controller approval",
                    NormalizeOptional(draft?.SubmittedBy) ?? NormalizeOptional(record.ApprovalId) ?? "controller",
                    record.ApprovalState.ToString(),
                    draft?.SubmittedAtUtc ?? record.FundEvent.UpdatedAtUtc,
                    record.ApprovalRoute));
            }
            else
            {
                steps.Add(new PaymentIntentApprovalStepDto(
                    sequence++,
                    "Controller approval",
                    "controller",
                    "Pending",
                    null,
                    record.ApprovalRoute));
            }
        }

        return steps
            .GroupBy(step => $"{step.Role}:{step.Actor}:{step.Status}:{step.EvidenceRoute}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select((step, index) => step with { Sequence = index + 1 })
            .ToArray();
    }

    private static IReadOnlyList<PaymentIntentBankEvidenceDto> BuildPaymentIntentBankEvidence(
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<BankTransactionDto> bankTransactions,
        IReadOnlyList<string> evidenceLinks)
    {
        var evidence = new List<PaymentIntentBankEvidenceDto>();
        var matchedTransactions = bankTransactions
            .Where(transaction => MatchesPaymentIntentBankTransaction(transaction, paymentIntentId, settlementReference, records))
            .OrderByDescending(static transaction => transaction.RecordedAt)
            .ThenBy(static transaction => transaction.BankTransactionId)
            .ToArray();

        foreach (var transaction in matchedTransactions)
        {
            var isReturn = IsReturnBankTransaction(transaction);
            evidence.Add(new PaymentIntentBankEvidenceDto(
                $"bank-transaction:{transaction.BankTransactionId:D}",
                isReturn ? "BankReturn" : "BankConfirmation",
                isReturn ? "Returned" : "Confirmed",
                isReturn
                    ? $"Bank-side transaction {transaction.BankTransactionId:D} indicates a returned, voided, or reversed payment intent."
                    : $"Bank-side transaction {transaction.BankTransactionId:D} confirms expected cash movement.",
                transaction.BankTransactionId,
                transaction.TransactionType,
                transaction.Amount,
                NormalizeCurrency(transaction.Currency),
                transaction.EffectiveDate,
                transaction.RecordedAt,
                NormalizeOptional(transaction.ExternalRef),
                BuildBankTransactionEvidenceRoute(transaction),
                NormalizeOptional(transaction.RecordedBy)));
        }

        foreach (var link in SelectPaymentIntentCashEvidenceLinks(evidenceLinks, paymentIntentId, settlementReference))
        {
            evidence.Add(new PaymentIntentBankEvidenceDto(
                $"retained-cash-evidence:{SanitizePaymentIntentPart(link)}",
                "RetainedCashEvidence",
                "Retained",
                $"Retained cash, bank, treasury, or settlement evidence is linked at {link}.",
                EvidenceRoute: link));
        }

        if (evidence.Count == 0)
        {
            evidence.Add(new PaymentIntentBankEvidenceDto(
                $"bank-evidence-missing:{SanitizePaymentIntentPart(paymentIntentId)}",
                "BankConfirmation",
                "Missing",
                "No bank confirmation, return, custodian cash, treasury, or settlement evidence is retained for this payment intent."));
        }

        return evidence
            .GroupBy(static item => item.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<PaymentIntentReconciliationLinkDto> BuildPaymentIntentReconciliationLinks(
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<string> evidenceLinks)
    {
        var reconciliationLinks = SelectReconciliationEvidenceLinks(evidenceLinks, paymentIntentId, settlementReference).ToArray();
        if (reconciliationLinks.Length == 0)
        {
            return
            [
                new PaymentIntentReconciliationLinkDto(
                    $"reconciliation-pending:{SanitizePaymentIntentPart(paymentIntentId)}",
                    "Pending",
                    "No reconciliation case, matching run, or break-review evidence is linked to this payment intent.")
            ];
        }

        return reconciliationLinks
            .Select((link, index) => new PaymentIntentReconciliationLinkDto(
                $"reconciliation-link:{index + 1}:{SanitizePaymentIntentPart(paymentIntentId)}",
                "Ready",
                $"Reconciliation evidence links payment intent {paymentIntentId} to retained cash or ledger review.",
                EvidenceRoute: link,
                ReconciliationCaseId: TryExtractEvidenceToken(link, "case"),
                ReconciliationRunId: TryExtractEvidenceToken(link, "run")))
            .ToArray();
    }

    private static IReadOnlyList<PaymentIntentAuditEventDto> BuildPaymentIntentAuditHistory(
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyDictionary<Guid, ManualJournalEntryDraftDto> draftsByJournalEntryId,
        IReadOnlyList<AccountingActionAuditEventDto> audit,
        IReadOnlyList<PaymentIntentBankEvidenceDto> bankEvidence,
        IReadOnlyList<PaymentIntentReconciliationLinkDto> reconciliationLinks)
    {
        var events = new List<PaymentIntentAuditEventDto>();
        foreach (var record in records.OrderBy(static record => record.FundEvent.UpdatedAtUtc).ThenBy(static record => record.FundEventId, StringComparer.OrdinalIgnoreCase))
        {
            draftsByJournalEntryId.TryGetValue(record.JournalEntryId, out var draft);
            events.Add(new PaymentIntentAuditEventDto(
                $"payment-intent-requested:{record.JournalEntryId:D}",
                draft?.CreatedAtUtc ?? record.FundEvent.UpdatedAtUtc,
                NormalizeOptional(draft?.PreparedBy) ?? "ledger-posting",
                "payment-intent.requested",
                $"Payment intent {paymentIntentId} was captured from treasury context for {record.FundEventType}.",
                record.EvidenceLinks));

            if (draft?.SubmittedAtUtc is not null || record.ApprovalState is ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved)
            {
                events.Add(new PaymentIntentAuditEventDto(
                    $"payment-intent-approval:{record.JournalEntryId:D}",
                    draft?.SubmittedAtUtc ?? record.FundEvent.UpdatedAtUtc,
                    NormalizeOptional(draft?.SubmittedBy) ?? NormalizeOptional(record.ApprovalId) ?? "controller",
                    "payment-intent.approval-state",
                    $"Approval chain state is {record.ApprovalState} for payment intent {paymentIntentId}.",
                    NormalizeOptional(record.ApprovalRoute) is { } approvalRoute
                        ? [approvalRoute]
                        : []));
            }
        }

        foreach (var auditEvent in audit.Where(auditEvent => MatchesPaymentIntentAuditEvent(auditEvent, paymentIntentId, settlementReference, records)))
        {
            events.Add(new PaymentIntentAuditEventDto(
                auditEvent.AuditEventId.ToString("D"),
                auditEvent.RecordedAtUtc,
                auditEvent.Actor,
                auditEvent.Action,
                $"Accounting audit event {auditEvent.Action} is linked to payment intent {paymentIntentId}.",
                auditEvent.EvidenceLinks));
        }

        events.Add(new PaymentIntentAuditEventDto(
            $"payment-intent-cash-evidence:{SanitizePaymentIntentPart(paymentIntentId)}",
            bankEvidence
                .Where(static evidence => evidence.RecordedAtUtc.HasValue)
                .Select(static evidence => evidence.RecordedAtUtc!.Value)
                .DefaultIfEmpty(records.Max(static record => record.FundEvent.UpdatedAtUtc))
                .Max(),
            "treasury-control",
            "payment-intent.cash-evidence-reviewed",
            $"{bankEvidence.Count} bank confirmation, return, or retained cash evidence item(s) are attached to the payment intent.",
            bankEvidence
                .Select(static evidence => evidence.EvidenceRoute)
                .Where(static route => !string.IsNullOrWhiteSpace(route))
                .Select(static route => route!)
                .ToArray()));

        events.Add(new PaymentIntentAuditEventDto(
            $"payment-intent-reconciliation:{SanitizePaymentIntentPart(paymentIntentId)}",
            records.Max(static record => record.FundEvent.UpdatedAtUtc),
            "treasury-control",
            "payment-intent.reconciliation-reviewed",
            $"{reconciliationLinks.Count} reconciliation linkage item(s) are attached to the payment intent.",
            reconciliationLinks
                .Select(static link => link.EvidenceRoute)
                .Where(static route => !string.IsNullOrWhiteSpace(route))
                .Select(static route => route!)
                .ToArray()));

        events.Add(new PaymentIntentAuditEventDto(
            $"payment-intent-execution-deferred:{SanitizePaymentIntentPart(paymentIntentId)}",
            DateTimeOffset.UtcNow,
            "treasury-control",
            "payment-intent.execution-deferred",
            "Full payment execution is deferred; this record is an evidence and control checkpoint only."));

        return events
            .GroupBy(static item => item.AuditEventId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static item => item.RecordedAtUtc)
            .ThenBy(static item => item.Action, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PaymentIntentWorkflowStatusDto ResolvePaymentIntentWorkflowStatus(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PaymentIntentApprovalStepDto> approvalChain,
        IReadOnlyList<PaymentIntentBankEvidenceDto> bankEvidence,
        IReadOnlyList<PaymentIntentReconciliationLinkDto> reconciliationLinks)
    {
        if (records.Any(static record => record.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Blocked) ||
            approvalChain.Any(static step => step.Status is nameof(ManualJournalEntryStatusDto.Rejected) or nameof(ManualJournalEntryStatusDto.NeedsFix)))
        {
            return PaymentIntentWorkflowStatusDto.Blocked;
        }

        if (bankEvidence.Any(static evidence => string.Equals(evidence.Status, "Returned", StringComparison.OrdinalIgnoreCase)))
        {
            return PaymentIntentWorkflowStatusDto.BankReturned;
        }

        if (records.Any(static record => record.PaymentIntentEvidence is null ||
                                         record.PaymentIntentEvidence.Status == PrivateCapitalPaymentIntentEvidenceStatusDto.MissingIntent))
        {
            return PaymentIntentWorkflowStatusDto.EvidenceMissing;
        }

        if (approvalChain.Any(static step => step.Status is "Pending" or nameof(ManualJournalEntryStatusDto.Draft) or nameof(ManualJournalEntryStatusDto.Submitted)))
        {
            return PaymentIntentWorkflowStatusDto.ApprovalPending;
        }

        if (records.Any(static record => record.PaymentIntentEvidence is not null &&
                                         record.PaymentIntentEvidence.Status == PrivateCapitalPaymentIntentEvidenceStatusDto.CashEvidenceMissing))
        {
            return PaymentIntentWorkflowStatusDto.BankEvidencePending;
        }

        if (!bankEvidence.Any(static evidence =>
                string.Equals(evidence.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(evidence.Status, "Retained", StringComparison.OrdinalIgnoreCase)))
        {
            return PaymentIntentWorkflowStatusDto.BankEvidencePending;
        }

        if (!reconciliationLinks.Any(static link => string.Equals(link.Status, "Ready", StringComparison.OrdinalIgnoreCase)))
        {
            return PaymentIntentWorkflowStatusDto.ReconciliationPending;
        }

        return PaymentIntentWorkflowStatusDto.ExecutionDeferred;
    }

    private static string FormatPaymentIntentWorkflowStatus(PaymentIntentWorkflowStatusDto status)
        => status switch
        {
            PaymentIntentWorkflowStatusDto.EvidenceMissing => "Intent evidence missing",
            PaymentIntentWorkflowStatusDto.ApprovalPending => "Approval pending",
            PaymentIntentWorkflowStatusDto.BankEvidencePending => "Bank evidence pending",
            PaymentIntentWorkflowStatusDto.BankReturned => "Bank return captured",
            PaymentIntentWorkflowStatusDto.ReconciliationPending => "Reconciliation pending",
            PaymentIntentWorkflowStatusDto.ExecutionDeferred => "Ready, execution deferred",
            PaymentIntentWorkflowStatusDto.Blocked => "Blocked",
            _ => status.ToString()
        };

    private static string BuildPaymentIntentReadinessReason(
        PaymentIntentWorkflowStatusDto status,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PaymentIntentBankEvidenceDto> bankEvidence,
        IReadOnlyList<PaymentIntentReconciliationLinkDto> reconciliationLinks)
        => status switch
        {
            PaymentIntentWorkflowStatusDto.EvidenceMissing =>
                "A payment intent id or required cash evidence field is missing from the treasury context.",
            PaymentIntentWorkflowStatusDto.ApprovalPending =>
                "Requester and expected movement are captured, but controller approval is not complete.",
            PaymentIntentWorkflowStatusDto.BankEvidencePending =>
                "Approval evidence is captured, but no retained bank confirmation, return, or cash settlement evidence is linked.",
            PaymentIntentWorkflowStatusDto.BankReturned =>
                "A bank-side return, void, rejection, or reversal is attached and blocks execution readiness.",
            PaymentIntentWorkflowStatusDto.ReconciliationPending =>
                "Cash evidence is attached, but reconciliation linkage is not retained.",
            PaymentIntentWorkflowStatusDto.ExecutionDeferred =>
                $"All pre-execution controls are retained across {records.Count} fund event(s), {bankEvidence.Count} bank evidence item(s), and {reconciliationLinks.Count} reconciliation link(s).",
            PaymentIntentWorkflowStatusDto.Blocked =>
                "The linked fund-event readiness or approval state is blocked.",
            _ => "Payment intent workflow requires review."
        };

    private static IReadOnlyList<string> BuildPaymentIntentEvidenceLinks(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyDictionary<Guid, ManualJournalEntryDraftDto> draftsByJournalEntryId,
        IReadOnlyList<AccountingActionAuditEventDto> audit,
        string paymentIntentId,
        string? settlementReference)
    {
        var links = new List<string>();
        links.AddRange(records.SelectMany(static record => record.EvidenceLinks));
        links.AddRange(records.SelectMany(static record => record.PaymentIntentEvidence?.CashEvidenceLinks ?? []));
        foreach (var record in records)
        {
            if (draftsByJournalEntryId.TryGetValue(record.JournalEntryId, out var draft))
            {
                links.AddRange(draft.EvidenceLinks);
                links.AddRange(draft.EvidenceAttachments?.Select(static attachment => attachment.Uri) ?? []);
            }
        }

        links.AddRange(audit
            .Where(auditEvent => MatchesPaymentIntentText(auditEvent.CorrelationId, paymentIntentId, settlementReference) ||
                auditEvent.EvidenceLinks.Any(link => MatchesPaymentIntentText(link, paymentIntentId, settlementReference)))
            .SelectMany(static auditEvent => auditEvent.EvidenceLinks));

        return links
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesPaymentIntentBankTransaction(
        BankTransactionDto transaction,
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        if (MatchesPaymentIntentText(transaction.ExternalRef, paymentIntentId, settlementReference))
        {
            return true;
        }

        return records.Any(record => transaction.EntityId == record.JournalEntryId);
    }

    private static bool IsReturnBankTransaction(BankTransactionDto transaction)
        => transaction.IsVoided ||
           transaction.TransactionType.Contains("return", StringComparison.OrdinalIgnoreCase) ||
           transaction.TransactionType.Contains("reject", StringComparison.OrdinalIgnoreCase) ||
           transaction.TransactionType.Contains("reversal", StringComparison.OrdinalIgnoreCase) ||
           transaction.TransactionType.Contains("void", StringComparison.OrdinalIgnoreCase) ||
           transaction.TransactionType.Contains("fail", StringComparison.OrdinalIgnoreCase);

    private static string BuildBankTransactionEvidenceRoute(BankTransactionDto transaction)
        => $"/api/banking/transactions?entityId={Uri.EscapeDataString(transaction.EntityId.ToString("D"))}&bankTransactionId={Uri.EscapeDataString(transaction.BankTransactionId.ToString("D"))}";

    private static IEnumerable<string> SelectPaymentIntentCashEvidenceLinks(
        IReadOnlyList<string> links,
        string paymentIntentId,
        string? settlementReference)
        => links
            .Where(static link => IsPaymentIntentCashEvidenceLink(link))
            .Where(link => IsScopedPaymentIntentEvidenceLink(link, paymentIntentId, settlementReference));

    private static bool IsPaymentIntentCashEvidenceLink(string link)
        => link.Contains("bank", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("custodian", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("plaid", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("reconciliation", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("settlement", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("treasury", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("wire", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("ach", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("swift", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("return", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("revers", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("reject", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("void", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("failure", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SelectReconciliationEvidenceLinks(
        IReadOnlyList<string> links,
        string paymentIntentId,
        string? settlementReference)
        => links
            .Where(static link =>
                link.Contains("reconciliation", StringComparison.OrdinalIgnoreCase) ||
                link.Contains("reconcile", StringComparison.OrdinalIgnoreCase) ||
                link.Contains("break", StringComparison.OrdinalIgnoreCase) ||
                link.Contains("match", StringComparison.OrdinalIgnoreCase))
            .Where(link => IsScopedPaymentIntentEvidenceLink(link, paymentIntentId, settlementReference));

    private static bool MatchesPaymentIntentAuditEvent(
        AccountingActionAuditEventDto auditEvent,
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
        => MatchesPaymentIntentText(auditEvent.CorrelationId, paymentIntentId, settlementReference) ||
           auditEvent.EvidenceLinks.Any(link => MatchesPaymentIntentText(link, paymentIntentId, settlementReference)) ||
           records.Any(record =>
               MatchesPaymentIntentText(auditEvent.CorrelationId, record.JournalEntryId.ToString("D"), null) ||
               auditEvent.EvidenceLinks.Any(link => MatchesPaymentIntentText(link, record.FundEventId, record.SettlementReference)));

    private static bool MatchesPaymentIntentText(string? value, string paymentIntentId, string? settlementReference)
        => !string.IsNullOrWhiteSpace(value) &&
           (ContainsPaymentIntentIdentifier(value, paymentIntentId) ||
            (!string.IsNullOrWhiteSpace(settlementReference) &&
             ContainsPaymentIntentIdentifier(value, settlementReference)));

    private static bool IsScopedPaymentIntentEvidenceLink(string link, string paymentIntentId, string? settlementReference)
        => !ContainsExplicitPaymentOrSettlementIdentifier(link) ||
           MatchesPaymentIntentText(link, paymentIntentId, settlementReference);

    private static bool ContainsPaymentIntentIdentifier(string value, string identifier)
        => value.Contains(identifier, StringComparison.OrdinalIgnoreCase) ||
           value.Contains(Uri.EscapeDataString(identifier), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsExplicitPaymentOrSettlementIdentifier(string link)
        => link.Contains("payment:", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("payment%3A", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("settlement:", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("settlement%3A", StringComparison.OrdinalIgnoreCase);

    private static string? TryExtractEvidenceToken(string value, string label)
    {
        var marker = $"{label}:";
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var token = value[(index + marker.Length)..]
            .Split(['/', '?', '&', '#'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return NormalizeOptional(token);
    }

    private static string SanitizePaymentIntentPart(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "payment-intent"
            : string.Join("-", value.Trim().Split(
                Path.GetInvalidFileNameChars().Concat([':', '/', '\\', '?', '&', '=']).Distinct().ToArray(),
                StringSplitOptions.RemoveEmptyEntries));

    private static IReadOnlyList<PrivateCapitalCapitalAccountActivityDto> BuildCapitalAccounts(
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> subledgerEntries)
        => subledgerEntries
            .GroupBy(item => new { item.CapitalAccountId, item.InvestorId, item.Currency })
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(item => item.EffectiveDate)
                    .ThenByDescending(item => item.UpdatedAtUtc)
                    .ToArray();
                return new PrivateCapitalCapitalAccountActivityDto(
                    group.Key.CapitalAccountId,
                    group.Key.InvestorId,
                    group.Key.Currency,
                    group.Where(item => item.EntryType == ManualJournalEntryTypeDto.CapitalCall).Sum(item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(item => item.EntryType == ManualJournalEntryTypeDto.Distribution).Sum(item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(item => item.EntryType == ManualJournalEntryTypeDto.Subscription).Sum(item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(item => item.EntryType == ManualJournalEntryTypeDto.Redemption).Sum(item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(item => item.EntryType == ManualJournalEntryTypeDto.ManagementFee).Sum(item => Math.Abs(item.NetCapitalActivity)),
                    group.Sum(item => item.NetCapitalActivity),
                    group.Select(static item => item.FundEventId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    ordered[0].EffectiveDate,
                    ordered[0].FundEventType,
                    group
                        .Select(item => item.FundEventId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray());
            })
            .OrderBy(item => item.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.InvestorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Currency, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> BuildCapitalAccountSubledgerEntries(
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerSource> sources)
    {
        var entries = new List<PrivateCapitalCapitalAccountSubledgerEntryDto>();
        foreach (var group in sources
            .GroupBy(item => new { item.CapitalAccountId, item.InvestorId, item.Currency }))
        {
            var runningNetActivity = 0m;
            foreach (var item in group
                .OrderBy(item => item.EffectiveDate)
                .ThenBy(item => item.UpdatedAtUtc)
                .ThenBy(item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.JournalEntryId))
            {
                runningNetActivity += item.NetCapitalActivity;
                entries.Add(new PrivateCapitalCapitalAccountSubledgerEntryDto(
                    item.SubledgerEntryId,
                    item.CapitalAccountId,
                    item.InvestorId,
                    item.Currency,
                    item.FundEventId,
                    item.FundEventType,
                    item.EntryType,
                    item.ApprovalState,
                    item.JournalEntryId,
                    item.EffectiveDate,
                    item.GrossAmount,
                    item.NetCapitalActivity,
                    runningNetActivity,
                    item.Memo,
                    item.EvidenceLinks,
                    item.ValidationIssues,
                    item.UpdatedAtUtc,
                    item.IsPosted));
            }
        }

        return entries
            .OrderByDescending(item => item.EffectiveDate)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PrivateCapitalCapitalAccountSubledgerSource MapFundEventSubledgerSource(
        PrivateCapitalFundEventDto fundEvent)
        => new(
            $"capital-account-subledger:{fundEvent.CapitalAccountId}:{fundEvent.FundEventId}:{fundEvent.JournalEntryId:D}".ToLowerInvariant(),
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.Currency,
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.EntryType,
            fundEvent.JournalStatus,
            fundEvent.JournalEntryId,
            fundEvent.EffectiveDate,
            fundEvent.GrossAmount,
            fundEvent.NetCapitalActivity,
            fundEvent.Memo,
            fundEvent.EvidenceLinks,
            fundEvent.ValidationIssues,
            fundEvent.UpdatedAtUtc,
            fundEvent.IsPosted);

    private static IEnumerable<PrivateCapitalCapitalAccountSubledgerSource> MapPostedCapitalAccountSubledgerSources(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        if (fundEvent.CapitalAccountImpacts.Count == 0)
        {
            yield break;
        }

        var entryType = MapPrivateCapitalEntryType(fundEvent.FundEventType);
        var approvalState = MapPostedApprovalState(fundEvent.ApprovalState, fundEvent.HasCriticalIssues);
        var currency = ResolvePostedEventCurrency(fundEvent, journalEntryCurrencies);
        var evidenceLinks = BuildPostedFundEventEvidenceLinks(fundEvent);
        var issues = MapPostedIssues(fundEvent);
        var effectiveDate = fundEvent.EffectiveDate ?? DateOnly.FromDateTime(fundEvent.FirstPostedAt.UtcDateTime);
        var journalEntryId = fundEvent.JournalEntryIds.FirstOrDefault();
        var memo = fundEvent.LedgerImpacts.FirstOrDefault()?.Description ?? fundEvent.FundEventType;

        var groupedImpacts = fundEvent.CapitalAccountImpacts
            .GroupBy(static item => new { item.CapitalAccountId, item.InvestorId })
            .OrderBy(static group => group.Key.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static group => group.Key.InvestorId, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedImpacts)
        {
            var netCapitalActivity = group.Sum(static item => item.NetCapitalAccountImpact);
            var grossAmount = group.Sum(static item => Math.Abs(item.NetCapitalAccountImpact));
            var ledgerEntryIds = group
                .SelectMany(static item => item.LedgerEntryIds)
                .Distinct()
                .Order()
                .ToArray();
            var impactKey = ledgerEntryIds.Length == 0
                ? journalEntryId.ToString("D")
                : string.Join("-", ledgerEntryIds.Select(static id => id.ToString("D")));

            yield return new PrivateCapitalCapitalAccountSubledgerSource(
                $"capital-account-subledger:{group.Key.CapitalAccountId}:{fundEvent.FundEventId}:{impactKey}".ToLowerInvariant(),
                group.Key.CapitalAccountId,
                NormalizeOptional(group.Key.InvestorId),
                currency,
                fundEvent.FundEventId,
                fundEvent.FundEventType,
                entryType,
                approvalState,
                journalEntryId,
                effectiveDate,
                grossAmount,
                netCapitalActivity,
                memo,
                evidenceLinks,
                issues,
                fundEvent.LastPostedAt,
                true);
        }
    }

    private static PrivateCapitalFundEventDto MapPostedFundEvent(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        var entryType = MapPrivateCapitalEntryType(fundEvent.FundEventType);
        var currency = ResolvePostedEventCurrency(fundEvent, journalEntryCurrencies);
        var evidenceLinks = BuildPostedFundEventEvidenceLinks(fundEvent);
        var issues = MapPostedIssues(fundEvent);
        var grossAmount = fundEvent.LedgerImpacts.Count > 0
            ? fundEvent.LedgerImpacts.Max(static item => Math.Max(Math.Abs(item.TotalDebits), Math.Abs(item.TotalCredits)))
            : Math.Abs(fundEvent.CapitalAccountImpacts.Sum(static item => item.NetCapitalAccountImpact));
        var netCapitalActivity = fundEvent.CapitalAccountImpacts.Count > 0
            ? fundEvent.CapitalAccountImpacts.Sum(static item => item.NetCapitalAccountImpact)
            : CalculateNetCapitalActivity(entryType, grossAmount);

        return new PrivateCapitalFundEventDto(
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            entryType,
            MapPostedApprovalState(fundEvent.ApprovalState, fundEvent.HasCriticalIssues),
            fundEvent.JournalEntryIds.FirstOrDefault(),
            fundEvent.EffectiveDate ?? DateOnly.FromDateTime(fundEvent.FirstPostedAt.UtcDateTime),
            NormalizeOptional(fundEvent.CapitalAccountId) ?? "capital-account:unassigned",
            NormalizeOptional(fundEvent.InvestorId),
            currency,
            grossAmount,
            netCapitalActivity,
            fundEvent.LedgerImpacts.FirstOrDefault()?.Description ?? fundEvent.FundEventType,
            NormalizeOptional(fundEvent.PaymentIntentId),
            NormalizeOptional(fundEvent.SettlementReference),
            evidenceLinks,
            issues,
            fundEvent.LastPostedAt,
            IsPosted: true,
            ApprovalId: NormalizeOptional(fundEvent.ApprovalId));
    }

    private static IReadOnlyList<string> BuildPostedFundEventEvidenceLinks(
        PrivateCapitalFundEventLedgerEvent fundEvent)
        => fundEvent.EvidenceLinks
            .Concat(fundEvent.ReportOutputs.SelectMany(static item => item.EvidenceLinks))
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<PrivateCapitalLedgerImpactDto> MapPostedLedgerImpacts(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        var approvalState = MapPostedApprovalState(fundEvent.ApprovalState, fundEvent.HasCriticalIssues);
        var currency = ResolvePostedEventCurrency(fundEvent, journalEntryCurrencies);
        var evidenceLinks = fundEvent.EvidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var issues = MapPostedIssues(fundEvent);
        var effectiveDate = fundEvent.EffectiveDate ?? DateOnly.FromDateTime(fundEvent.FirstPostedAt.UtcDateTime);
        var capitalAccountId = NormalizeOptional(fundEvent.CapitalAccountId) ?? "capital-account:unassigned";

        foreach (var impact in fundEvent.LedgerImpacts)
        {
            var capitalAccountImpact = ResolveLedgerImpactCapitalAccount(fundEvent, impact);
            yield return new PrivateCapitalLedgerImpactDto(
                impact.LedgerImpactId,
                impact.JournalEntryId,
                fundEvent.FundEventId,
                fundEvent.FundEventType,
                capitalAccountImpact?.CapitalAccountId ?? capitalAccountId,
                NormalizeOptional(capitalAccountImpact?.InvestorId) ?? NormalizeOptional(fundEvent.InvestorId),
                approvalState,
                effectiveDate,
                currency,
                impact.TotalDebits,
                impact.TotalCredits,
                impact.Imbalance,
                impact.Lines.Count,
                impact.IsBalanced,
                fundEvent.IsPostingReady,
                evidenceLinks,
                impact.Lines.Select(line => MapPostedLedgerLine(line, currency)).ToArray(),
                issues);
        }
    }

    private static PrivateCapitalFundEventCapitalAccountImpact? ResolveLedgerImpactCapitalAccount(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        PrivateCapitalFundEventLedgerImpact ledgerImpact)
    {
        var ledgerEntryIds = ledgerImpact.Lines
            .Select(static line => line.LedgerEntryId)
            .ToHashSet();
        if (ledgerEntryIds.Count == 0)
        {
            return null;
        }

        var matches = fundEvent.CapitalAccountImpacts
            .Where(impact => impact.LedgerEntryIds.Any(ledgerEntryIds.Contains))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static PrivateCapitalLedgerLineImpactDto MapPostedLedgerLine(
        PrivateCapitalFundEventLedgerLine line,
        string currency)
    {
        var side = line.Debit > 0m ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit;
        return new PrivateCapitalLedgerLineImpactDto(
            line.LedgerEntryId.ToString("D"),
            line.AccountName,
            side,
            Math.Abs(line.Debit > 0m ? line.Debit : line.Credit),
            currency,
            NormalizeOptional(line.FinancialAccountId),
            null,
            NormalizeOptional(line.Symbol),
            null);
    }

    private static string ResolvePostedEventCurrency(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        var currencies = fundEvent.JournalEntryIds
            .Select(id => journalEntryCurrencies.TryGetValue(id, out var currency) ? NormalizeCurrency(currency) : null)
            .Where(static currency => !string.IsNullOrWhiteSpace(currency))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return currencies.Length == 0 ? "USD" : currencies[0]!;
    }

    private static IReadOnlyList<PrivateCapitalReportOutputDto> BuildPostedReportOutputs(
        string fundProfileId,
        Guid? ledgerBookId,
        IReadOnlyList<PrivateCapitalFundEventLedgerEvent> postedEvents,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies,
        IReadOnlyList<ReportPackWorkflowRecordDto> reportPackWorkflowRecords)
    {
        if (postedEvents.Count == 0)
        {
            return [];
        }

        var records = reportPackWorkflowRecords
            .Where(record => string.Equals(record.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (records.Length == 0)
        {
            return postedEvents
                .Select(fundEvent => BuildMissingPostedReportOutput(fundProfileId, ledgerBookId, fundEvent, journalEntryCurrencies))
                .ToArray();
        }

        return postedEvents
            .SelectMany(fundEvent =>
            {
                var matched = records
                    .Where(record => MatchesPostedFundEventReport(record, fundEvent))
                    .Select(record => BuildPostedReportOutput(fundProfileId, ledgerBookId, fundEvent, record, journalEntryCurrencies))
                    .ToArray();

                return matched.Length > 0
                    ? matched
                    : [BuildMissingPostedReportOutput(fundProfileId, ledgerBookId, fundEvent, journalEntryCurrencies)];
            })
            .ToArray();
    }

    private static int CountPublishedReportOutputs(
        string fundProfileId,
        IReadOnlyList<PrivateCapitalFundEventLedgerEvent> postedEvents,
        IReadOnlyList<ReportPackWorkflowRecordDto> reportPackWorkflowRecords)
    {
        var publishedOutputKeys = postedEvents
            .SelectMany(static fundEvent => fundEvent.ReportOutputs
                .Where(static output => output.IsPublished)
                .Select(output => BuildPublishedReportOutputKey(fundEvent.FundEventId, output.ReportId, output.ReportOutputId)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var workflowPublishedOutputKeys = reportPackWorkflowRecords
            .Where(record => string.Equals(record.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(IsPublishedReportPack)
            .SelectMany(record => postedEvents
                .Where(fundEvent => MatchesPostedFundEventReport(record, fundEvent))
                .Select(fundEvent => BuildPublishedReportOutputKey(fundEvent.FundEventId, record.ReportId.ToString("D"), null)));
        publishedOutputKeys.UnionWith(workflowPublishedOutputKeys);

        return publishedOutputKeys.Count;
    }

    private static string BuildPublishedReportOutputKey(string fundEventId, string? reportPackId, string? reportOutputId)
    {
        var normalizedFundEventId = string.IsNullOrWhiteSpace(fundEventId) ? "unknown-fund-event" : fundEventId.Trim();
        var normalizedReportPackId = NormalizeOptional(reportPackId);
        if (normalizedReportPackId is not null)
        {
            return $"{normalizedFundEventId}:report-pack:{normalizedReportPackId}";
        }

        var normalizedReportOutputId = NormalizeOptional(reportOutputId);
        return normalizedReportOutputId is null
            ? $"{normalizedFundEventId}:report-output:unknown"
            : $"{normalizedFundEventId}:report-output:{normalizedReportOutputId}";
    }

    private static IReadOnlyDictionary<string, bool> BuildPostingReadyByFundEventId(
        IReadOnlyList<PrivateCapitalLedgerImpactDto> ledgerImpacts)
        => ledgerImpacts
            .GroupBy(static item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Any() && group.All(static item => item.IsPostingReady),
                StringComparer.OrdinalIgnoreCase);

    private static PrivateCapitalReportOutputDto BuildPostedReportOutput(
        string fundProfileId,
        Guid? ledgerBookId,
        PrivateCapitalFundEventLedgerEvent fundEvent,
        ReportPackWorkflowRecordDto record,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        var currency = ResolvePostedEventCurrency(fundEvent, journalEntryCurrencies);
        var matchedProvenance = (record.LineProvenance ?? [])
            .Where(line => MatchesPostedFundEventLine(line, fundEvent))
            .ToArray();
        var accountScope = ResolvePostedReportOutputAccountScope(fundEvent, record, matchedProvenance);
        var reportEvidenceLinks = MergeEvidenceLinks(
            record.Publication?.EvidenceLinks
                .Select(link => link.Route ?? link.EvidenceId)
                .ToArray() ?? [],
            matchedProvenance
                .Select(static line => line.EvidenceId)
                .ToArray());
        var evidenceLinks = MergeEvidenceLinks(
            fundEvent.EvidenceLinks,
            record.Publication?.EvidenceLinks
                .Select(link => link.Route ?? link.EvidenceId)
                .ToArray());
        evidenceLinks = MergeEvidenceLinks(
            evidenceLinks,
            matchedProvenance
                .Select(static line => line.EvidenceId)
                .ToArray());
        var validationIssues = new List<AccountingConfigurationValidationIssueDto>();
        if (!IsPublishedReportPack(record))
        {
            validationIssues.Add(Issue(
                "private-capital.report-output-publication-pending",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Posted private-capital fund event has a report-pack workflow that is not published yet.",
                fundEvent.FundEventId,
                "Approve and publish the governed report pack before stakeholder package delivery."));
        }

        if (reportEvidenceLinks.Count == 0)
        {
            validationIssues.Add(Issue(
                "private-capital.report-output-evidence-missing",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Posted private-capital report output is missing retained report evidence links.",
                fundEvent.FundEventId,
                "Attach retained publication evidence before relying on report output."));
        }

        if (!fundEvent.IsPostingReady)
        {
            validationIssues.Add(Issue(
                "private-capital.report-output-posting-not-ready",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Posted private-capital report output is not ready because the linked fund event is not posting-ready.",
                fundEvent.FundEventId,
                "Repair approval, evidence, ledger impact, or capital-account impact before relying on report output."));
        }

        var isReportReady = fundEvent.IsPostingReady && IsReadyReportPack(record) && reportEvidenceLinks.Count > 0;
        var approvalRoute = BuildPostedReportOutputApprovalRoute(fundProfileId, fundEvent);
        var reportOutputId = $"report-output:{fundEvent.FundEventId}:{record.ReportId:D}".ToLowerInvariant();
        var reportOutputRoute = PrivateCapitalActivityRoutes.BuildReportOutputRoute(
            fundProfileId,
            ledgerBookId,
            reportOutputId,
            fundEvent.FundEventId,
            accountScope.CapitalAccountId,
            accountScope.InvestorId);
        var readiness = BuildReportOutputReadiness(
            isReportReady,
            IsPublishedReportPack(record) && record.Publication is not null,
            validationIssues,
            reportOutputRoute,
            PrivateCapitalActivityRoutes.BuildEvidenceRoute(fundEvent.FundEventId),
            approvalRoute);
        return new PrivateCapitalReportOutputDto(
            reportOutputId,
            "GovernedReportPack",
            $"{record.TemplateId.Name} v{record.TemplateId.Version}",
            $"/api/fund-structure/report-packs/{Uri.EscapeDataString(record.ReportId.ToString("D"))}",
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            accountScope.CapitalAccountId,
            accountScope.InvestorId,
            MapPostedWorkflowState(record.State),
            fundEvent.EffectiveDate ?? DateOnly.FromDateTime(fundEvent.FirstPostedAt.UtcDateTime),
            currency,
            accountScope.NetCapitalActivity,
            evidenceLinks.Count,
            evidenceLinks,
            isReportReady,
            validationIssues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            IsPublished: IsPublishedReportPack(record) && record.Publication is not null,
            ReportPackId: record.ReportId.ToString("D"),
            ReportWorkflowState: record.State.ToString(),
            PublicationManifestId: NormalizeOptional(record.Publication?.ManifestId),
            RetainedManifestPath: NormalizeOptional(record.Publication?.RetainedManifestPath),
            PublicationEvidenceHash: NormalizeOptional(record.Publication?.EvidenceHash),
            PublishedAtUtc: record.Publication?.SignedOffAt,
            PublishedBy: NormalizeOptional(record.Publication?.SignedOffBy),
            ReportLineProvenanceCount: matchedProvenance.Length,
            ReportOutputRoute: reportOutputRoute,
            FundEventRecordRoute: PrivateCapitalActivityRoutes.BuildFundEventRecordRoute(
                fundProfileId,
                ledgerBookId,
                fundEvent.FundEventId),
            CapitalAccountSubledgerRoute: PrivateCapitalActivityRoutes.BuildCapitalAccountSubledgerRoute(
                fundProfileId,
                ledgerBookId,
                accountScope.CapitalAccountId,
                accountScope.InvestorId,
                currency),
            EvidenceRoute: PrivateCapitalActivityRoutes.BuildEvidenceRoute(fundEvent.FundEventId),
            ApprovalRoute: approvalRoute,
            ReadinessLabel: readiness.Label,
            ReadinessReason: readiness.Reason,
            NextAction: readiness.NextAction,
            NextActionRoute: readiness.NextActionRoute);
    }

    private static PrivateCapitalReportOutputDto BuildMissingPostedReportOutput(
        string fundProfileId,
        Guid? ledgerBookId,
        PrivateCapitalFundEventLedgerEvent fundEvent,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        var currency = ResolvePostedEventCurrency(fundEvent, journalEntryCurrencies);
        var accountScope = ResolveMissingPostedReportOutputAccountScope(fundEvent);
        var evidenceLinks = fundEvent.EvidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reportOutputId = $"report-output:{fundEvent.FundEventId}:governed-report-pack-pending".ToLowerInvariant();
        var reportOutputRoute = PrivateCapitalActivityRoutes.BuildReportOutputRoute(
            fundProfileId,
            ledgerBookId,
            reportOutputId,
            fundEvent.FundEventId,
            accountScope.CapitalAccountId,
            accountScope.InvestorId);
        var evidenceRoute = PrivateCapitalActivityRoutes.BuildEvidenceRoute(fundEvent.FundEventId);
        var approvalRoute = BuildPostedReportOutputApprovalRoute(fundProfileId, fundEvent);
        var validationIssues = new[]
        {
            Issue(
                "private-capital.report-output-missing",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Posted private-capital fund event is not linked to a governed report-pack workflow.",
                fundEvent.FundEventId,
                "Generate or attach the governed report pack before stakeholder package delivery.")
        };
        var readiness = BuildReportOutputReadiness(
            isReportReady: false,
            isPublished: false,
            validationIssues,
            reportOutputRoute,
            evidenceRoute,
            approvalRoute);
        return new PrivateCapitalReportOutputDto(
            reportOutputId,
            "GovernedReportPack",
            $"Governed report pack for {fundEvent.FundEventType}",
            PrivateCapitalActivityRoutes.Build(
                fundProfileId,
                fundEvent.FundEventId,
                accountScope.CapitalAccountId,
                accountScope.InvestorId),
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            accountScope.CapitalAccountId,
            accountScope.InvestorId,
            MapPostedApprovalState(fundEvent.ApprovalState, fundEvent.HasCriticalIssues),
            fundEvent.EffectiveDate ?? DateOnly.FromDateTime(fundEvent.FirstPostedAt.UtcDateTime),
            currency,
            accountScope.NetCapitalActivity,
            evidenceLinks.Length,
            evidenceLinks,
            false,
            validationIssues,
            ReportWorkflowState: "Missing",
            ReportOutputRoute: reportOutputRoute,
            FundEventRecordRoute: PrivateCapitalActivityRoutes.BuildFundEventRecordRoute(
                fundProfileId,
                ledgerBookId,
                fundEvent.FundEventId),
            CapitalAccountSubledgerRoute: PrivateCapitalActivityRoutes.BuildCapitalAccountSubledgerRoute(
                fundProfileId,
                ledgerBookId,
                accountScope.CapitalAccountId,
                accountScope.InvestorId,
                currency),
            EvidenceRoute: evidenceRoute,
            ApprovalRoute: approvalRoute,
            ReadinessLabel: readiness.Label,
            ReadinessReason: readiness.Reason,
            NextAction: readiness.NextAction,
            NextActionRoute: readiness.NextActionRoute);
    }

    private static string? BuildPostedReportOutputApprovalRoute(
        string fundProfileId,
        PrivateCapitalFundEventLedgerEvent fundEvent)
    {
        if (fundEvent.JournalEntryIds.Count == 0)
        {
            return null;
        }

        return PrivateCapitalActivityRoutes.BuildApprovalRoute(
            fundProfileId,
            fundEvent.JournalEntryIds[0],
            NormalizeOptional(fundEvent.ApprovalId));
    }

    private static PrivateCapitalReportOutputReadinessProjection BuildReportOutputReadiness(
        bool isReportReady,
        bool isPublished,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues,
        string reportOutputRoute,
        string evidenceRoute,
        string? approvalRoute)
    {
        if (isPublished && isReportReady)
        {
            return new(
                "Published",
                "The report output is published with retained report evidence and linked posting-ready fund-event impact.",
                "Open published report",
                reportOutputRoute);
        }

        if (isReportReady)
        {
            return new(
                "Ready",
                "The report output has retained evidence and linked posting-ready fund-event impact.",
                "Review report output",
                reportOutputRoute);
        }

        var issue = validationIssues
            .OrderByDescending(static item => item.Severity)
            .ThenBy(static item => item.Code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return issue?.Code switch
        {
            "manual-je.private-capital-report-evidence-missing" or
            "private-capital.report-output-evidence-missing" => new(
                "Evidence missing",
                issue.Message,
                "Attach retained evidence",
                evidenceRoute),
            "manual-je.private-capital-report-approval-pending" => new(
                "Approval pending",
                issue.Message,
                "Submit or review approval",
                approvalRoute ?? reportOutputRoute),
            "manual-je.private-capital-report-ledger-impact-not-ready" or
            "private-capital.report-output-posting-not-ready" => new(
                "Posting review",
                issue.Message,
                "Review ledger impact",
                reportOutputRoute),
            "private-capital.report-output-publication-pending" => new(
                "Publication pending",
                issue.Message,
                "Approve and publish report pack",
                reportOutputRoute),
            "private-capital.report-output-missing" => new(
                "Report output missing",
                issue.Message,
                "Generate governed report pack",
                reportOutputRoute),
            _ => new(
                "Report review",
                issue?.Message ?? "Report output readiness has not been satisfied.",
                "Prepare report output",
                reportOutputRoute)
        };
    }

    private static PrivateCapitalReportOutputAccountScope ResolvePostedReportOutputAccountScope(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        ReportPackWorkflowRecordDto record,
        IReadOnlyList<ReportPackLineProvenanceDto> matchedProvenance)
    {
        var targetCapitalAccountId = NormalizeOptional(record.FundAccountId);
        if (targetCapitalAccountId is not null)
        {
            var targetMatches = fundEvent.CapitalAccountImpacts
                .Where(impact => string.Equals(impact.CapitalAccountId, targetCapitalAccountId, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (targetMatches.Length == 1)
            {
                return BuildReportOutputAccountScope(targetMatches[0]);
            }
        }

        var provenanceLedgerEntryIds = matchedProvenance
            .SelectMany(EnumerateLineLedgerEntryIds)
            .ToHashSet();
        if (provenanceLedgerEntryIds.Count > 0)
        {
            var provenanceMatches = fundEvent.CapitalAccountImpacts
                .Where(impact => impact.LedgerEntryIds.Any(provenanceLedgerEntryIds.Contains))
                .Take(2)
                .ToArray();
            if (provenanceMatches.Length == 1)
            {
                return BuildReportOutputAccountScope(provenanceMatches[0]);
            }
        }

        return ResolveMissingPostedReportOutputAccountScope(fundEvent);
    }

    private static PrivateCapitalReportOutputAccountScope ResolveMissingPostedReportOutputAccountScope(
        PrivateCapitalFundEventLedgerEvent fundEvent)
    {
        if (fundEvent.CapitalAccountImpacts.Count == 1)
        {
            return BuildReportOutputAccountScope(fundEvent.CapitalAccountImpacts[0]);
        }

        var netCapitalActivity = fundEvent.CapitalAccountImpacts.Count > 0
            ? fundEvent.CapitalAccountImpacts.Sum(static item => item.NetCapitalAccountImpact)
            : CalculateNetCapitalActivity(
                MapPrivateCapitalEntryType(fundEvent.FundEventType),
                fundEvent.LedgerImpacts.Sum(static item => Math.Max(Math.Abs(item.TotalDebits), Math.Abs(item.TotalCredits))));
        var hasAmbiguousCapitalAccount = fundEvent.CapitalAccountImpacts.Count > 1 ||
            fundEvent.Issues.Any(static item => string.Equals(item.Code, "private-capital.capital-account-conflict", StringComparison.OrdinalIgnoreCase));
        return new PrivateCapitalReportOutputAccountScope(
            hasAmbiguousCapitalAccount
                ? "capital-account:unassigned"
                : NormalizeOptional(fundEvent.CapitalAccountId) ?? "capital-account:unassigned",
            hasAmbiguousCapitalAccount ? null : NormalizeOptional(fundEvent.InvestorId),
            netCapitalActivity);
    }

    private static PrivateCapitalReportOutputAccountScope BuildReportOutputAccountScope(
        PrivateCapitalFundEventCapitalAccountImpact impact)
        => new(
            impact.CapitalAccountId,
            NormalizeOptional(impact.InvestorId),
            impact.NetCapitalAccountImpact);

    private static IEnumerable<Guid> EnumerateLineLedgerEntryIds(ReportPackLineProvenanceDto line)
    {
        if (Guid.TryParse(line.LedgerEntryId, out var ledgerEntryId))
        {
            yield return ledgerEntryId;
        }

        if (Guid.TryParse(line.SourceId, out var sourceId))
        {
            yield return sourceId;
        }
    }

    private static bool MatchesPostedFundEventReport(
        ReportPackWorkflowRecordDto record,
        PrivateCapitalFundEventLedgerEvent fundEvent)
    {
        if ((record.LineProvenance ?? []).Any(line => MatchesPostedFundEventLine(line, fundEvent)))
        {
            return true;
        }

        return EnumerateReportPackEvidencePointers(record)
            .Any(pointer => MatchesPostedFundEventPointer(pointer, fundEvent));
    }

    private static bool MatchesPostedFundEventLine(
        ReportPackLineProvenanceDto line,
        PrivateCapitalFundEventLedgerEvent fundEvent)
    {
        if (MatchesPostedFundEventPointer(line.SourceId, fundEvent))
        {
            return true;
        }

        if (MatchesPostedFundEventPointer(line.EvidenceId, fundEvent) ||
            MatchesPostedFundEventPointer(line.ApprovalId, fundEvent))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(line.LedgerEntryId) &&
            MatchesAnyGuid(line.LedgerEntryId, fundEvent.LedgerEntryIds))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(line.SourceId) &&
            (MatchesAnyGuid(line.SourceId, fundEvent.LedgerEntryIds) ||
             MatchesAnyGuid(line.SourceId, fundEvent.JournalEntryIds)))
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateReportPackEvidencePointers(ReportPackWorkflowRecordDto record)
    {
        foreach (var pointer in EnumerateReportPackEvidenceLinks(record.Publication?.EvidenceLinks))
        {
            yield return pointer;
        }

        foreach (var pointer in EnumerateReportPackEvidenceLinks(record.Restatement?.EvidenceLinks))
        {
            yield return pointer;
        }

        foreach (var pointer in EnumerateReportPackEvidenceLinks(record.Rejection?.EvidenceLinks))
        {
            yield return pointer;
        }

        if (!string.IsNullOrWhiteSpace(record.Publication?.ManifestId))
        {
            yield return record.Publication.ManifestId;
        }

        if (!string.IsNullOrWhiteSpace(record.Publication?.RetainedManifestPath))
        {
            yield return record.Publication.RetainedManifestPath;
        }
    }

    private static IEnumerable<string> EnumerateReportPackEvidenceLinks(
        IReadOnlyList<ReportPackEvidenceLinkDto>? evidenceLinks)
    {
        foreach (var link in evidenceLinks ?? [])
        {
            if (!string.IsNullOrWhiteSpace(link.EvidenceId))
            {
                yield return link.EvidenceId;
            }

            if (!string.IsNullOrWhiteSpace(link.Label))
            {
                yield return link.Label;
            }

            if (!string.IsNullOrWhiteSpace(link.Route))
            {
                yield return link.Route;
            }

            if (!string.IsNullOrWhiteSpace(link.Source))
            {
                yield return link.Source;
            }
        }
    }

    private static bool MatchesPostedFundEventPointer(
        string? value,
        PrivateCapitalFundEventLedgerEvent fundEvent)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var pointer = value.Trim();
        if (pointer.Contains(fundEvent.FundEventId, StringComparison.OrdinalIgnoreCase) ||
            pointer.Contains(Uri.EscapeDataString(fundEvent.FundEventId), StringComparison.OrdinalIgnoreCase) ||
            MatchesAnyGuid(pointer, fundEvent.LedgerEntryIds) ||
            MatchesAnyGuid(pointer, fundEvent.JournalEntryIds))
        {
            return true;
        }

        try
        {
            var unescaped = Uri.UnescapeDataString(pointer);
            return !string.Equals(unescaped, pointer, StringComparison.Ordinal) &&
                (unescaped.Contains(fundEvent.FundEventId, StringComparison.OrdinalIgnoreCase) ||
                 MatchesAnyGuid(unescaped, fundEvent.LedgerEntryIds) ||
                 MatchesAnyGuid(unescaped, fundEvent.JournalEntryIds));
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool MatchesAnyGuid(string value, IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ids.Any(id => value.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            value.Contains(id.ToString("N"), StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> MapPostedIssues(
        PrivateCapitalFundEventLedgerEvent fundEvent)
        => fundEvent.Issues
            .Select(issue => Issue(
                issue.Code,
                MapPostedIssueSeverity(issue.Severity),
                issue.Message,
                fundEvent.FundEventId,
                "Review posted journal metadata, retained evidence, approval, and report output before relying on capital-account reporting."))
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static AccountingConfigurationValidationSeverityDto MapPostedIssueSeverity(
        PrivateCapitalFundEventIssueSeverity severity)
        => severity switch
        {
            PrivateCapitalFundEventIssueSeverity.Critical => AccountingConfigurationValidationSeverityDto.Critical,
            PrivateCapitalFundEventIssueSeverity.Warning => AccountingConfigurationValidationSeverityDto.Warning,
            _ => AccountingConfigurationValidationSeverityDto.Info
        };

    private static ManualJournalEntryStatusDto MapPostedApprovalState(
        PrivateCapitalFundEventApprovalState state,
        bool hasCriticalIssues = false)
        => state switch
        {
            PrivateCapitalFundEventApprovalState.Approved or PrivateCapitalFundEventApprovalState.Posted => ManualJournalEntryStatusDto.Approved,
            PrivateCapitalFundEventApprovalState.Submitted => ManualJournalEntryStatusDto.Submitted,
            PrivateCapitalFundEventApprovalState.Rejected => ManualJournalEntryStatusDto.Rejected,
            _ => hasCriticalIssues ? ManualJournalEntryStatusDto.NeedsFix : ManualJournalEntryStatusDto.Draft
        };

    private static ManualJournalEntryStatusDto MapPostedWorkflowState(ReportPackWorkflowStateDto state)
        => state switch
        {
            ReportPackWorkflowStateDto.Approved or ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated or ReportPackWorkflowStateDto.Archived => ManualJournalEntryStatusDto.Approved,
            ReportPackWorkflowStateDto.Validated or ReportPackWorkflowStateDto.InReview or ReportPackWorkflowStateDto.PendingApproval => ManualJournalEntryStatusDto.Submitted,
            ReportPackWorkflowStateDto.Rejected => ManualJournalEntryStatusDto.Rejected,
            _ => ManualJournalEntryStatusDto.Draft
        };

    private static bool IsReadyReportPack(ReportPackWorkflowRecordDto record)
        => record.State is ReportPackWorkflowStateDto.Approved or ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated;

    private static bool IsPublishedReportPack(ReportPackWorkflowRecordDto record)
        => record.State is ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated;

    private static ManualJournalEntryTypeDto MapPrivateCapitalEntryType(string? fundEventType)
        => fundEventType?.Trim().ToLowerInvariant() switch
        {
            "capitalcall" or "capital call" or "capital-call" => ManualJournalEntryTypeDto.CapitalCall,
            "distribution" => ManualJournalEntryTypeDto.Distribution,
            "subscription" => ManualJournalEntryTypeDto.Subscription,
            "redemption" => ManualJournalEntryTypeDto.Redemption,
            "lptransfer" or "lp transfer" or "lp-transfer" or "transfer" => ManualJournalEntryTypeDto.LpTransfer,
            "managementfee" or "management fee" or "management-fee" => ManualJournalEntryTypeDto.ManagementFee,
            _ => ManualJournalEntryTypeDto.General
        };

    private static bool IsPrivateCapitalActivityCandidate(
        ManualJournalEntryTypeDto entryType,
        TreasuryLedgerContextDto? context)
        => RequiresPrivateCapitalTreasuryContext(entryType) ||
            context is not null && (
                !string.IsNullOrWhiteSpace(context.FundEventId) ||
                !string.IsNullOrWhiteSpace(context.FundEventType) ||
                !string.IsNullOrWhiteSpace(context.CapitalAccountId) ||
                !string.IsNullOrWhiteSpace(context.InvestorId));

    private static decimal CalculateNetCapitalActivity(ManualJournalEntryTypeDto entryType, decimal grossAmount)
        => entryType switch
        {
            ManualJournalEntryTypeDto.CapitalCall => grossAmount,
            ManualJournalEntryTypeDto.Subscription => grossAmount,
            ManualJournalEntryTypeDto.Distribution => -grossAmount,
            ManualJournalEntryTypeDto.Redemption => -grossAmount,
            ManualJournalEntryTypeDto.ManagementFee => -grossAmount,
            _ => 0m
        };

    private static PrivateCapitalLedgerImpactDto BuildPrivateCapitalLedgerImpact(
        ManualJournalEntryDraftDto draft,
        TreasuryLedgerContextDto context,
        IReadOnlyList<string> evidenceLinks)
    {
        var currency = string.IsNullOrWhiteSpace(draft.Currency) ? "USD" : draft.Currency.Trim().ToUpperInvariant();
        var lines = draft.Lines
            .Select(line => new PrivateCapitalLedgerLineImpactDto(
                line.LineId,
                line.AccountPath,
                line.Side,
                Math.Abs(line.Amount),
                string.IsNullOrWhiteSpace(line.Currency) ? currency : line.Currency.Trim().ToUpperInvariant(),
                NormalizeOptional(line.EntityId),
                line.SecurityId,
                NormalizeOptional(line.SecurityDisplayName),
                NormalizeOptional(line.EvidenceLink)))
            .ToArray();
        var totalDebits = draft.TotalDebits != 0m
            ? draft.TotalDebits
            : draft.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Debit).Sum(line => Math.Abs(line.Amount));
        var totalCredits = draft.TotalCredits != 0m
            ? draft.TotalCredits
            : draft.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Credit).Sum(line => Math.Abs(line.Amount));
        var imbalance = draft.Imbalance != 0m ? draft.Imbalance : totalDebits - totalCredits;
        var isBalanced = Math.Abs(imbalance) <= BalanceTolerance;
        var issues = draft.ValidationIssues.ToList();

        if (!isBalanced)
        {
            issues.Add(Issue(
                "manual-je.private-capital-ledger-impact-unbalanced",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Private-capital ledger impact is not balanced.",
                draft.JournalEntryId.ToString("D"),
                "Balance debit and credit lines before submitting or posting the fund event."));
        }

        if (evidenceLinks.Count == 0)
        {
            issues.Add(Issue(
                "manual-je.private-capital-ledger-impact-evidence-missing",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Private-capital ledger impact is missing retained evidence links.",
                draft.JournalEntryId.ToString("D"),
                "Attach retained source, approval, or settlement evidence before approval or report output."));
        }

        if (draft.Status is not (ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved))
        {
            issues.Add(Issue(
                "manual-je.private-capital-ledger-impact-approval-pending",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Private-capital ledger impact is not approval-ready because the journal entry has not been submitted or approved.",
                draft.JournalEntryId.ToString("D"),
                "Submit or approve the fund-event journal before posting or stakeholder package production."));
        }

        var isPostingReady =
            isBalanced &&
            evidenceLinks.Count > 0 &&
            draft.Status is ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved &&
            issues.All(issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical);

        return new PrivateCapitalLedgerImpactDto(
            $"ledger-impact:{context.FundEventId}:{draft.JournalEntryId:D}".ToLowerInvariant(),
            draft.JournalEntryId,
            context.FundEventId!,
            context.FundEventType!,
            context.CapitalAccountId!,
            NormalizeOptional(context.InvestorId),
            draft.Status,
            context.EffectiveDate!.Value,
            currency,
            totalDebits,
            totalCredits,
            imbalance,
            lines.Length,
            isBalanced,
            isPostingReady,
            evidenceLinks,
            lines,
            issues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static PrivateCapitalReportOutputDto BuildPrivateCapitalReportOutput(
        string fundProfileId,
        Guid? ledgerBookId,
        PrivateCapitalFundEventDto fundEvent,
        IReadOnlyDictionary<string, bool> postingReadyByFundEventId)
    {
        var reportOutputType = fundEvent.EntryType switch
        {
            ManualJournalEntryTypeDto.CapitalCall => "CapitalCallNotice",
            ManualJournalEntryTypeDto.Distribution => "DistributionNotice",
            ManualJournalEntryTypeDto.Subscription => "SubscriptionStatement",
            ManualJournalEntryTypeDto.Redemption => "RedemptionStatement",
            ManualJournalEntryTypeDto.LpTransfer => "CapitalAccountTransferStatement",
            ManualJournalEntryTypeDto.ManagementFee => "ManagementFeeSupportPackage",
            _ => "PrivateCapitalActivityStatement"
        };
        var isPostingReady = postingReadyByFundEventId.TryGetValue(fundEvent.FundEventId, out var resolvedPostingReady) && resolvedPostingReady;
        var isReportReady =
            isPostingReady &&
            fundEvent.JournalStatus is ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved &&
            fundEvent.EvidenceLinks.Count > 0 &&
            fundEvent.ValidationIssues.All(issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical);
        var issues = fundEvent.ValidationIssues.ToList();
        if (fundEvent.EvidenceLinks.Count == 0)
        {
            issues.Add(Issue(
                "manual-je.private-capital-report-evidence-missing",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Private-capital report output is missing retained evidence links.",
                fundEvent.FundEventId,
                "Attach retained source, approval, or settlement evidence before stakeholder package publication."));
        }

        if (fundEvent.JournalStatus is not (ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved))
        {
            issues.Add(Issue(
                "manual-je.private-capital-report-approval-pending",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Private-capital report output is not ready because the linked journal entry has not been submitted for approval.",
                fundEvent.FundEventId,
                "Submit or approve the fund-event journal before report-package production."));
        }

        if (!isPostingReady)
        {
            issues.Add(Issue(
                "manual-je.private-capital-report-ledger-impact-not-ready",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Private-capital report output is not ready because the linked ledger and capital-account impact is not posting-ready.",
                fundEvent.FundEventId,
                "Resolve ledger-impact readiness before producing or publishing the report output."));
        }

        var reportOutputId = $"report-output:{fundEvent.FundEventId}:{reportOutputType}".ToLowerInvariant();
        var reportRoute = PrivateCapitalActivityRoutes.Build(
            fundProfileId,
            fundEvent.FundEventId,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId);
        var reportOutputRoute = PrivateCapitalActivityRoutes.BuildReportOutputRoute(
            fundProfileId,
            ledgerBookId,
            reportOutputId,
            fundEvent.FundEventId,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId);
        var evidenceRoute = PrivateCapitalActivityRoutes.BuildEvidenceRoute(fundEvent.FundEventId);
        var approvalRoute = PrivateCapitalActivityRoutes.BuildApprovalRoute(
            fundProfileId,
            fundEvent.JournalEntryId,
            fundEvent.ApprovalId);
        var orderedIssues = issues
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var readiness = BuildReportOutputReadiness(
            isReportReady,
            isPublished: false,
            orderedIssues,
            reportOutputRoute,
            evidenceRoute,
            approvalRoute);
        return new PrivateCapitalReportOutputDto(
            reportOutputId,
            reportOutputType,
            $"{reportOutputType} for {fundEvent.FundEventType}",
            reportRoute,
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.JournalStatus,
            fundEvent.EffectiveDate,
            fundEvent.Currency,
            fundEvent.NetCapitalActivity,
            fundEvent.EvidenceLinks.Count,
            fundEvent.EvidenceLinks,
            isReportReady,
            orderedIssues,
            IsPublished: false,
            ReportWorkflowState: fundEvent.JournalStatus.ToString(),
            ReportOutputRoute: reportOutputRoute,
            FundEventRecordRoute: PrivateCapitalActivityRoutes.BuildFundEventRecordRoute(
                fundProfileId,
                ledgerBookId,
                fundEvent.FundEventId),
            CapitalAccountSubledgerRoute: PrivateCapitalActivityRoutes.BuildCapitalAccountSubledgerRoute(
                fundProfileId,
                ledgerBookId,
                fundEvent.CapitalAccountId,
                fundEvent.InvestorId,
                fundEvent.Currency),
            EvidenceRoute: evidenceRoute,
            ApprovalRoute: approvalRoute,
            ReadinessLabel: readiness.Label,
            ReadinessReason: readiness.Reason,
            NextAction: readiness.NextAction,
            NextActionRoute: readiness.NextActionRoute);
    }

    private static AccountingConfigurationValidationIssueDto Issue(
        string code,
        AccountingConfigurationValidationSeverityDto severity,
        string message,
        string? targetId,
        string? suggestedAction)
        => new(code, severity, message, targetId, suggestedAction);

    private static IReadOnlyList<string> MergeEvidenceLinks(
        IReadOnlyList<string> existing,
        IReadOnlyList<string>? incoming)
        => existing.Concat(incoming ?? [])
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Select(link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static TreasuryLedgerContextDto? NormalizeTreasuryContext(
        TreasuryLedgerContextDto? context,
        DateOnly accountingDate)
    {
        if (context is null)
        {
            return null;
        }

        return context with
        {
            EffectiveDate = context.EffectiveDate ?? accountingDate,
            IdempotencyKey = NormalizeOptional(context.IdempotencyKey),
            FundEventId = NormalizeOptional(context.FundEventId),
            FundEventType = NormalizeOptional(context.FundEventType),
            CapitalAccountId = NormalizeOptional(context.CapitalAccountId),
            InvestorId = NormalizeOptional(context.InvestorId),
            PaymentIntentId = NormalizeOptional(context.PaymentIntentId),
            SettlementReference = NormalizeOptional(context.SettlementReference)
        };
    }

    private static bool RequiresPrivateCapitalTreasuryContext(ManualJournalEntryTypeDto entryType)
        => entryType is ManualJournalEntryTypeDto.CapitalCall
            or ManualJournalEntryTypeDto.Distribution
            or ManualJournalEntryTypeDto.Subscription
            or ManualJournalEntryTypeDto.Redemption
            or ManualJournalEntryTypeDto.LpTransfer
            or ManualJournalEntryTypeDto.ManagementFee;

    private static string NormalizeCurrency(string? value)
        => string.IsNullOrWhiteSpace(value) ? "USD" : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
