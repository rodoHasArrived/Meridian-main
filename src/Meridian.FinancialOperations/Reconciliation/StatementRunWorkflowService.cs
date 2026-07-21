using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed class StatementRunWorkflowService(
    ICanonicalStatementStore importStore,
    IReconciliationCaseStore caseStore,
    IReconciliationBreakStore breakStore,
    IBrokerStatementService brokerStatementService,
    IStatementReconciliationValidationService validationService,
    IInternalReconciliationPopulationProvider? populationProvider = null,
    IReconciliationFxRateProvider? fxRateProvider = null) : IStatementRunWorkflowService
{
    // The internal book to reconcile against and the FX seam used to normalize foreign-currency
    // lines. Both default to safe, fail-closed implementations: an empty book (every row becomes a
    // break) and identity-only FX (same-currency reconciles exactly, cross-currency breaks) until a
    // deployment wires real populations and rates.
    private readonly IInternalReconciliationPopulationProvider _populationProvider =
        populationProvider ?? EmptyInternalReconciliationPopulationProvider.Instance;
    private readonly IReconciliationFxRateProvider _fxRateProvider =
        fxRateProvider ?? IdentityReconciliationFxRateProvider.Instance;

    public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken cancellationToken = default)
        => importStore.ListImportsAsync(cancellationToken);

    public async Task<StatementRunWorkflowResult> CreateAsync(StatementRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = await NormalizeAndValidateAsync(request, cancellationToken).ConfigureAwait(false);
        var imported = await brokerStatementService.ImportAsync(ToImportRequest(normalizedRequest), cancellationToken).ConfigureAwait(false);

        // Reconcile the imported statement against Meridian's own book. The population provider
        // supplies the internal positions, cash, and ledger for this fund account and period; the
        // matching engine then compares statement rows to those records (not to themselves) across
        // exact, tolerance, candidate, and unmatched tiers.
        var baseCurrency = StatementRunMatcher.DefaultBaseCurrency;
        var populations = await _populationProvider
            .GetPopulationsAsync(
                new InternalReconciliationPopulationContext(
                    imported.Import.FundAccountId,
                    imported.Import.ExternalAccountId,
                    imported.Import.StatementPeriodStart,
                    imported.Import.StatementPeriodEnd,
                    baseCurrency),
                cancellationToken)
            .ConfigureAwait(false);

        var createdAtUtc = DateTimeOffset.UtcNow;
        var matchResult = StatementRunMatcher.Match(
            imported.Import,
            imported.Rows,
            populations,
            StatementToleranceProfile.Default,
            _fxRateProvider,
            baseCurrency,
            createdAtUtc);

        var linkedBreaks = matchResult.Breaks
            .Select(static item => item with { Record = item.Record with { EvidenceLink = BuildEvidenceLink(item.Record) } })
            .ToArray();
        var breaks = linkedBreaks.Select(static item => item.Record).ToArray();
        await breakStore.WriteAsync(breaks, cancellationToken).ConfigureAwait(false);

        var cases = BuildStatementCases(imported.Import, linkedBreaks, normalizedRequest.ImportedBy);
        foreach (var reconciliationCase in cases)
        {
            await caseStore.SaveAsync(reconciliationCase, cancellationToken).ConfigureAwait(false);
        }

        return new StatementRunWorkflowResult(imported.Import, breaks, cases);
    }

    public async Task<StatementRunWorkflowResult?> GetAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var imports = await importStore.ListImportsAsync(cancellationToken).ConfigureAwait(false);
        var import = imports.FirstOrDefault(item => string.Equals(item.ImportId, runId, StringComparison.OrdinalIgnoreCase));
        if (import is null)
        {
            return null;
        }

        var breaks = await ListOpenBreaksAsync(cancellationToken).ConfigureAwait(false);
        var cases = await ListCasesAsync(cancellationToken).ConfigureAwait(false);
        return new StatementRunWorkflowResult(
            import,
            breaks.Where(item =>
                    string.Equals(item.RunId, import.ImportId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            cases.Where(item => string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(CancellationToken cancellationToken = default)
        => breakStore.ListOpenAsync(cancellationToken);

    public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(CancellationToken cancellationToken = default)
        => caseStore.ListAsync(cancellationToken);

    private async Task<StatementRunRequest> NormalizeAndValidateAsync(StatementRunRequest request, CancellationToken cancellationToken)
    {
        var sourceFileHash = string.IsNullOrWhiteSpace(request.SourceFileHash)
            ? await ComputeSourceFileHashAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
            : request.SourceFileHash.Trim().ToUpperInvariant();
        await validationService.ValidateAsync(
            new StatementReconciliationValidationRequest(request.Broker, request.SourcePath, request.MappingProfileId),
            cancellationToken).ConfigureAwait(false);
        return request with
        {
            Broker = request.Broker.Trim(),
            SourceInstitution = request.SourceInstitution.Trim(),
            FundAccountId = request.FundAccountId.Trim(),
            ExternalAccountId = request.ExternalAccountId.Trim(),
            SourcePath = request.SourcePath.Trim(),
            OriginalFileName = request.OriginalFileName.Trim(),
            MappingProfileId = request.MappingProfileId.Trim(),
            ToleranceProfileId = request.ToleranceProfileId.Trim(),
            ImportedBy = request.ImportedBy.Trim(),
            SourceFileHash = sourceFileHash
        };
    }

    private static BrokerStatementImportRequest ToImportRequest(StatementRunRequest request)
        => new(
            request.Broker,
            request.SourceInstitution,
            request.FundAccountId,
            request.ExternalAccountId,
            request.StatementPeriodStart,
            request.StatementPeriodEnd,
            request.SourcePath,
            request.OriginalFileName,
            request.MappingProfileId,
            request.ToleranceProfileId,
            request.ImportedBy,
            request.SourceFileHash);

    private static string BuildEvidenceLink(ReconciliationBreakRecord record)
        => $"/api/workstation/reconciliation/statement-runs/{Uri.EscapeDataString(record.ImportId)}#row-{Uri.EscapeDataString(record.SourceReference)}";

    private static IReadOnlyList<ReconciliationCase> BuildStatementCases(
        CanonicalStatementImport import,
        IReadOnlyList<StatementRunBreak> breaks,
        string actor)
    {
        return breaks.Select(item =>
        {
            var breakRecord = item.Record;
            // Anchor every case, history entry, comment, and audit event to the break's own
            // creation timestamp so the run's records share one consistent instant.
            var now = breakRecord.CreatedAtUtc;
            var engineResult = item.EngineResult;
            var row = item.StatementRow;
            var sourceRowHash = row?.RawChecksum ?? breakRecord.SourceReference;
            var evidenceLink = breakRecord.EvidenceLink ?? BuildEvidenceLink(breakRecord);
            var evidenceReferences = new[] { evidenceLink, $"statement-row:{breakRecord.SourceReference}", $"statement-hash:{sourceRowHash}" };
            var explanation = BuildBreakExplanation(import, row, breakRecord, engineResult, evidenceReferences);
            var attachment = new ReconciliationCaseAttachment(
                AttachmentId: $"statement-row:{breakRecord.ImportId}:{breakRecord.SourceReference}",
                EvidenceKind: "ExternalStatementRow",
                SourceSystem: import.Broker,
                SourceReference: breakRecord.SourceReference,
                ContentHash: sourceRowHash,
                Route: evidenceLink,
                AttachedAtUtc: now);

            return new ReconciliationCase(
                CaseId: $"case:{breakRecord.BreakId}",
                ImportId: import.ImportId,
                Status: "Open",
                Reason: explanation.Summary,
                Confidence: engineResult.Confidence,
                Rationale: string.IsNullOrWhiteSpace(engineResult.Explanation) ? breakRecord.Category : engineResult.Explanation,
                CreatedAtUtc: now,
                History:
                [
                    new ReconciliationCaseHistoryEntry(now, "None", "Open", "Case created from external statement break.")
                    {
                        Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim(),
                        EvidenceId = evidenceLink
                    }
                ])
            {
                Owner = "fund-ops",
                Priority = breakRecord.ToleranceBreached ? "High" : "Normal",
                DueAtUtc = now.AddDays(breakRecord.ToleranceBreached ? 1 : 2),
                LastUpdatedAtUtc = now,
                LastUpdatedBy = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim(),
                Disposition = "NeedsInvestigation",
                AgingDays = 0,
                EvidenceReferences = evidenceReferences,
                Attachments = [attachment],
                BreakExplanation = explanation,
                CommentThreads =
                [
                    new ReconciliationCaseCommentThread(
                        "statement-intake",
                        "External statement intake",
                        [
                            new ReconciliationCaseComment(
                                Guid.NewGuid().ToString("N"),
                                $"{explanation.Summary} Suggested next action: {explanation.SuggestedNextAction}",
                                "system",
                                now)
                        ])
                ],
                AuditEvents =
                [
                    new ReconciliationCaseAuditEvent(
                        Guid.NewGuid().ToString("N"),
                        "ExternalStatementCaseCreated",
                        now,
                        "system",
                        $"Case created from statement break {breakRecord.BreakId}.")
                ]
            };
        }).ToArray();
    }

    private static ReconciliationBreakExplanation BuildBreakExplanation(
        CanonicalStatementImport import,
        CanonicalStatementRow? row,
        ReconciliationBreakRecord breakRecord,
        StatementMatchResult engineResult,
        IReadOnlyList<string> evidenceReferences)
    {
        var sourceSystem = string.IsNullOrWhiteSpace(import.SourceInstitution) ? import.Broker : import.SourceInstitution;
        var rowLabel = row is null ? breakRecord.SourceReference : $"row {row.SourceRowNumber}";
        var activityType = row?.ActivityType ?? breakRecord.Category;
        var side = engineResult.BrokerEvidenceReference is null ? "internal-record" : "statement";
        var amount = row is null ? breakRecord.Delta : Math.Abs(row.CashAmount) + Math.Abs(row.Quantity * row.Price);
        var descriptor = engineResult.MatchTier == StatementMatchTier.Candidate ? "candidate review" : "break";

        return new ReconciliationBreakExplanation(
            Summary: $"{activityType} {descriptor} from {sourceSystem} {rowLabel}.",
            SourceSystems: [sourceSystem, "Meridian ledger", "Meridian positions"],
            ProbableCause: string.IsNullOrWhiteSpace(engineResult.Explanation)
                ? "External statement row did not match retained Meridian ledger, position, or cash evidence."
                : engineResult.Explanation,
            LedgerImpact: $"Ledger, cash, or position balances may require review for {import.FundAccountId}; unmatched {side} exposure is {amount:G29}.",
            SuggestedNextAction: "Assign the case, compare the external statement row to retained ledger and position evidence, then attach support before disposition.",
            RequiredSignoffRole: breakRecord.ToleranceBreached ? "Fund accounting" : "Fund operations",
            EvidenceLinks: evidenceReferences);
    }

    private static async Task<string> ComputeSourceFileHashAsync(string sourcePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(sourcePath);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
