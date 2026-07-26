using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed class StatementRunWorkflowService(
    ICanonicalStatementStore importStore,
    IReconciliationCaseStore caseStore,
    IReconciliationBreakStore breakStore,
    IBrokerStatementService brokerStatementService,
    IStatementReconciliationValidationService validationService,
    IStatementBreakDispositionService? breakDispositionService = null) : IStatementRunWorkflowService
{
    public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken cancellationToken = default)
        => importStore.ListImportsAsync(cancellationToken);

    public async Task<StatementRunWorkflowResult> CreateAsync(StatementRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = await NormalizeAndValidateAsync(request, cancellationToken).ConfigureAwait(false);
        var imported = await brokerStatementService.ImportAsync(ToImportRequest(normalizedRequest), cancellationToken).ConfigureAwait(false);
        var matcher = new StatementMatchingService();
        var outcomes = matcher.MatchRows(imported.Rows);
        var breaks = matcher
            .BuildBreakRecords(imported.Import.ImportId, imported.Import.ImportId, imported.Rows, outcomes)
            .Select(static item => item with
            {
                EvidenceLink = $"/api/workstation/reconciliation/statement-runs/{Uri.EscapeDataString(item.ImportId)}#row-{Uri.EscapeDataString(item.SourceReference)}",
                Version = 1
            })
            .ToArray();
        await breakStore.WriteAsync(breaks, cancellationToken).ConfigureAwait(false);

        var cases = BuildStatementCases(imported.Import, imported.Rows, outcomes, breaks, normalizedRequest.ImportedBy);
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

    public async Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(CancellationToken cancellationToken = default)
    {
        if (breakDispositionService is not null)
        {
            await breakDispositionService.ResumePendingAsync(cancellationToken).ConfigureAwait(false);
        }

        return await breakStore.ListOpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(CancellationToken cancellationToken = default)
    {
        if (breakDispositionService is not null)
        {
            await breakDispositionService.ResumePendingAsync(cancellationToken).ConfigureAwait(false);
        }

        return await caseStore.ListAsync(cancellationToken).ConfigureAwait(false);
    }

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

    private static IReadOnlyList<ReconciliationCase> BuildStatementCases(
        CanonicalStatementImport import,
        IReadOnlyList<CanonicalStatementRow> rows,
        IReadOnlyList<MatchOutcome> outcomes,
        IReadOnlyList<ReconciliationBreakRecord> breaks,
        string actor)
    {
        var now = DateTimeOffset.UtcNow;
        var rowByReference = rows.ToDictionary(
            row => $"{import.ImportId}:{row.SourceRowNumber}",
            StringComparer.OrdinalIgnoreCase);
        var outcomeByChecksum = outcomes.ToDictionary(
            outcome => outcome.RowChecksum,
            StringComparer.OrdinalIgnoreCase);

        return breaks.Select(breakRecord =>
        {
            rowByReference.TryGetValue(breakRecord.SourceReference, out var row);
            var sourceRowHash = row?.RawChecksum ?? breakRecord.SourceReference;
            outcomeByChecksum.TryGetValue(sourceRowHash, out var outcome);
            var evidenceLink = breakRecord.EvidenceLink ?? $"/api/workstation/reconciliation/statement-runs/{Uri.EscapeDataString(import.ImportId)}#row-{Uri.EscapeDataString(breakRecord.SourceReference)}";
            var evidenceReferences = new[] { evidenceLink, $"statement-row:{breakRecord.SourceReference}", $"statement-hash:{sourceRowHash}" };
            var explanation = BuildBreakExplanation(import, row, breakRecord, outcome, evidenceReferences);
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
                Confidence: outcome?.Confidence ?? 0.25m,
                Rationale: outcome?.Rationale ?? breakRecord.Category,
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
                BreakId = breakRecord.BreakId,
                Version = breakRecord.Version,
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
        MatchOutcome? outcome,
        IReadOnlyList<string> evidenceReferences)
    {
        var sourceSystem = string.IsNullOrWhiteSpace(import.SourceInstitution) ? import.Broker : import.SourceInstitution;
        var rowLabel = row is null ? breakRecord.SourceReference : $"row {row.SourceRowNumber}";
        var activityType = row?.ActivityType ?? breakRecord.Category;
        var amount = row is null ? breakRecord.Delta : Math.Abs(row.CashAmount) + Math.Abs(row.Quantity * row.Price);

        return new ReconciliationBreakExplanation(
            Summary: $"{activityType} break from {sourceSystem} statement {rowLabel}.",
            SourceSystems: [sourceSystem, "Meridian ledger", "Meridian positions"],
            ProbableCause: outcome?.Rationale ?? "External statement row did not match retained Meridian ledger, position, or cash evidence.",
            LedgerImpact: $"Ledger, cash, or position balances may require review for {import.FundAccountId}; unmatched statement exposure is {amount:G29}.",
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
