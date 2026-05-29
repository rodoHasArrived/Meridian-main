using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ui.Shared.Contracts.Reconciliation;

namespace Meridian.Ui.Services.Services.Reconciliation;

public sealed class ReconciliationApiService(
    ICanonicalStatementStore importStore,
    IReconciliationCaseStore caseStore,
    IReconciliationBreakStore breakStore) : IReconciliationApiService
{
    public async Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(CancellationToken ct = default)
        => (await importStore.ListImportsAsync(ct).ConfigureAwait(false))
            .Select(static import => new StatementImportSummaryDto(
                import.ImportId,
                import.Broker,
                import.StatementDate.ToString("yyyy-MM-dd"),
                import.ImportedAtUtc.ToString("O"),
                import.RawRowCount,
                import.NormalizedRowCount))
            .ToList();

    public async Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(CancellationToken ct = default)
        => (await importStore.ListImportsAsync(ct).ConfigureAwait(false))
            .Select(ToSummary)
            .ToList();

    public async Task<StatementRunDto?> CreateStatementRunAsync(StatementRunCreateDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCreateRequest(request);

        var sourceFileHash = string.IsNullOrWhiteSpace(request.SourceFileHash)
            ? await ComputeSourceFileHashAsync(request.SourcePath, ct).ConfigureAwait(false)
            : request.SourceFileHash.Trim().ToUpperInvariant();
        var statementService = new CsvBrokerStatementService(importStore);
        var imported = await statementService.ImportAsync(ToImportRequest(request, sourceFileHash), ct).ConfigureAwait(false);
        var matcher = new StatementMatchingService();
        var outcomes = matcher.MatchRows(imported.Rows);
        var breaks = matcher
            .BuildBreakRecords(imported.Import.ImportId, imported.Import.ImportId, imported.Rows, outcomes)
            .Select(static item => item with
            {
                EvidenceLink = $"/api/workstation/reconciliation/statement-runs/{Uri.EscapeDataString(item.ImportId)}#row-{Uri.EscapeDataString(item.SourceReference)}"
            })
            .ToArray();
        await breakStore.WriteAsync(breaks, ct).ConfigureAwait(false);

        var cases = BuildStatementCases(imported.Import, imported.Rows, outcomes, breaks, request.ImportedBy);
        foreach (var reconciliationCase in cases)
        {
            await caseStore.SaveAsync(reconciliationCase, ct).ConfigureAwait(false);
        }

        var breakDtos = breaks.Select(ToRunBreakDto).ToArray();
        var status = breakDtos.Length == 0 ? StatementRunStatus.Completed : StatementRunStatus.ReviewRequired;
        return ToRunDto(imported.Import, breakDtos, status, request.Notes, cases);
    }

    public async Task<StatementRunDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
    {
        var import = await FindImportAsync(runId, ct).ConfigureAwait(false);
        if (import is null)
        {
            return null;
        }

        var breaks = await ListBreakDtosAsync(import.ImportId, ct).ConfigureAwait(false);
        var cases = await ListCasesForImportAsync(import.ImportId, ct).ConfigureAwait(false);
        return ToRunDto(
            import,
            breaks,
            breaks.Any(static item => string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase))
                ? StatementRunStatus.ReviewRequired
                : StatementRunStatus.Completed,
            cases: cases);
    }

    public async Task<StatementRunValidationDto?> GetStatementRunValidationAsync(string runId, CancellationToken ct = default)
    {
        var run = await GetStatementRunAsync(runId, ct).ConfigureAwait(false);
        return run is null
            ? null
            : new StatementRunValidationDto(
                run.RunId ?? runId,
                run.ValidationIssues ?? [],
                run.ValidationIssues?.Any(static issue => issue.Severity is StatementValidationSeverity.Critical or StatementValidationSeverity.Error) == true);
    }

    public async Task<IReadOnlyList<StatementRunBreakDto>?> ListStatementRunBreaksAsync(string runId, CancellationToken ct = default)
    {
        var import = await FindImportAsync(runId, ct).ConfigureAwait(false);
        return import is null ? null : await ListBreakDtosAsync(import.ImportId, ct).ConfigureAwait(false);
    }

    public async Task<StatementRunDto?> ReconcileStatementRunAsync(string runId, StatementRunReconcileRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var import = await FindImportAsync(runId, ct).ConfigureAwait(false);
        if (import is null)
        {
            return null;
        }

        var breaks = await ListBreakDtosAsync(import.ImportId, ct).ConfigureAwait(false);
        var cases = await ListCasesForImportAsync(import.ImportId, ct).ConfigureAwait(false);
        return ToRunDto(import, breaks, breaks.Count == 0 ? StatementRunStatus.Completed : StatementRunStatus.ReviewRequired, cases: cases);
    }

    public async Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(CancellationToken ct = default)
        => (await breakStore.ListOpenAsync(ct).ConfigureAwait(false))
            .Select(static item => new StatementRunExceptionDto(
                item.BreakId,
                item.RunId,
                item.ImportId,
                item.SourceReference,
                item.BreakCode,
                item.Category,
                item.Delta,
                item.Tolerance,
                item.ToleranceBreached,
                item.CreatedAtUtc.ToString("O"),
                item.Status))
            .ToList();

    public async Task<IReadOnlyList<StatementBreakDto>> ListOpenStatementBreaksAsync(CancellationToken ct = default)
        => (await breakStore.ListOpenAsync(ct).ConfigureAwait(false))
            .Select(static item => new StatementBreakDto(
                BreakId: item.BreakId,
                BreakType: MapBreakType(item.BreakCode),
                Severity: item.ToleranceBreached ? StatementValidationSeverity.Error : StatementValidationSeverity.Warning,
                MatchTier: StatementMatchTier.Manual,
                StatementReference: item.SourceReference,
                Description: $"{item.Category} break {item.BreakCode} requires statement reconciliation review.",
                StatementAmount: item.Delta,
                BookAmount: null,
                Delta: item.Delta,
                Tolerance: item.Tolerance,
                Currency: null,
                CreatedAtUtc: item.CreatedAtUtc,
                Status: item.Status,
                InternalReference: item.RunId,
                Owner: null,
                LastObservedAtUtc: item.CreatedAtUtc,
                RecommendedAction: "ReviewAndResolve",
                EvidenceLink: $"/api/workstation/reconciliation/exceptions/{Uri.EscapeDataString(item.BreakId)}"))
            .ToList();

    public async Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(CancellationToken ct = default)
        => (await caseStore.ListAsync(ct).ConfigureAwait(false))
            .Where(static item => string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Status, "InReview", StringComparison.OrdinalIgnoreCase))
            .Select(ToCaseSummary)
            .ToList();

    public async Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(CancellationToken ct = default)
    {
        var breaks = await breakStore.ListOpenAsync(ct).ConfigureAwait(false);
        var cases = await caseStore.ListAsync(ct).ConfigureAwait(false);
        return breaks
            .GroupBy(static item => string.IsNullOrWhiteSpace(item.ImportId) ? item.RunId : item.ImportId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var relatedCases = cases.Where(c => string.Equals(c.ImportId, group.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
                var criticalCount = group.Count(static item => item.ToleranceBreached);
                return new ReconciliationQueueAccountStatusDto(
                    AccountId: Guid.Empty,
                    AccountCode: group.Key,
                    QueueState: criticalCount > 0 ? "Blocked" : "Review",
                    UnresolvedBreakCount: group.Count(),
                    SignOffReady: group.Count() == 0 && relatedCases.All(static c => string.Equals(c.Status, "Resolved", StringComparison.OrdinalIgnoreCase)),
                    NextBestAction: criticalCount > 0
                        ? "Assign critical reconciliation breaks and capture resolution evidence."
                        : "Review open reconciliation breaks before operator sign-off.",
                    BlockerReason: criticalCount > 0
                        ? "Tolerance-breached breaks remain unresolved."
                        : "Unresolved breaks remain in the reconciliation queue.",
                    EvidenceLinks: group.Select(item => $"/api/workstation/reconciliation/break-queue/{Uri.EscapeDataString(item.BreakId)}").ToList());
            })
            .ToList();
    }

    private static StatementRunSummaryDto ToSummary(CanonicalStatementImport import) =>
        new(
            import.ImportId,
            import.ImportId,
            StatementRunStatus.Completed,
            import.ImportedAtUtc,
            import.ImportedAtUtc,
            import.NormalizedRowCount,
            0,
            0,
            0);

    private async Task<CanonicalStatementImport?> FindImportAsync(string runId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        return (await importStore.ListImportsAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(import => string.Equals(import.ImportId, runId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<StatementRunBreakDto>> ListBreakDtosAsync(string runId, CancellationToken ct)
        => (await breakStore.ListOpenAsync(ct).ConfigureAwait(false))
            .Where(item => string.Equals(item.RunId, runId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.ImportId, runId, StringComparison.OrdinalIgnoreCase))
            .Select(ToRunBreakDto)
            .ToList();

    private static StatementRunDto ToRunDto(
        CanonicalStatementImport import,
        IReadOnlyList<StatementRunBreakDto> breaks,
        StatementRunStatus status,
        string? notes = null,
        IReadOnlyList<ReconciliationCase>? cases = null)
    {
        var source = new StatementSourceDto(
            SourceId: import.ImportId,
            SourceKind: import.Broker,
            SourceSystem: import.SourceInstitution,
            AccountId: import.FundAccountId,
            AccountName: import.ExternalAccountId,
            Currency: null,
            PeriodStart: import.StatementPeriodStart,
            PeriodEnd: import.StatementPeriodEnd,
            StatementAsOfUtc: import.ImportedAtUtc,
            CustodianId: import.SourceInstitution,
            ExternalAccountId: import.ExternalAccountId);

        return new StatementRunDto(
            RunId: import.ImportId,
            Status: status,
            Source: source,
            StartedAtUtc: import.ImportedAtUtc,
            CompletedAtUtc: status is StatementRunStatus.Completed ? import.ImportedAtUtc : null,
            SourceFileName: import.OriginalFileName,
            SourceFileHash: import.SourceFileHash,
            MappingProfileId: import.MappingProfileId,
            MappingProfileVersion: null,
            ToleranceProfileId: import.ToleranceProfileId,
            ToleranceProfileVersion: null,
            ImportedBy: import.ImportedBy,
            ImportedAtUtc: import.ImportedAtUtc,
            ValidationIssues: [],
            Positions: [],
            CashBalances: [],
            Transactions: [],
            MatchSummary: new StatementMatchSummaryDto(
                StatementItemCount: import.NormalizedRowCount,
                MatchedItemCount: Math.Max(0, import.NormalizedRowCount - breaks.Count),
                AutoMatchedItemCount: Math.Max(0, import.NormalizedRowCount - breaks.Count),
                ManualMatchedItemCount: 0,
                ReviewRequiredItemCount: breaks.Count,
                BreakCount: breaks.Count,
                PositionBreakCount: breaks.Count(static item => item.BreakType is StatementBreakType.PositionMarketValueMismatch or StatementBreakType.PositionQuantityMismatch),
                CashBreakCount: breaks.Count(static item => item.BreakType is StatementBreakType.CashBalanceMismatch),
                TransactionBreakCount: breaks.Count(static item => item.BreakType is StatementBreakType.TransactionAmountMismatch)),
            Breaks: breaks.Select(static item => new StatementBreakDto(
                item.BreakId,
                item.BreakType,
                item.ToleranceBreached ? StatementValidationSeverity.Error : StatementValidationSeverity.Warning,
                item.ToleranceBreached ? StatementMatchTier.Unmatched : StatementMatchTier.WithinTolerance,
                item.SourceReference,
                item.Category,
                item.Delta,
                null,
                item.Delta,
                item.Tolerance,
                null,
                item.CreatedAtUtc,
                item.Status)).ToList(),
            Cases: cases?.Select(ToStatementCaseDto).ToList() ?? [],
            ImportId: import.ImportId,
            FundProfileId: import.FundAccountId,
            FundAccountId: Guid.TryParse(import.FundAccountId, out var accountId) ? accountId : null,
            Notes: notes);
    }

    private async Task<IReadOnlyList<ReconciliationCase>> ListCasesForImportAsync(string importId, CancellationToken ct)
        => (await caseStore.ListAsync(ct).ConfigureAwait(false))
            .Where(item => string.Equals(item.ImportId, importId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static BrokerStatementImportRequest ToImportRequest(StatementRunCreateDto request, string sourceFileHash)
        => new(
            request.Broker.Trim(),
            request.SourceInstitution.Trim(),
            request.FundAccountId.Trim(),
            request.ExternalAccountId.Trim(),
            request.StatementPeriodStart,
            request.StatementPeriodEnd,
            request.SourcePath.Trim(),
            string.IsNullOrWhiteSpace(request.OriginalFileName) ? Path.GetFileName(request.SourcePath) : request.OriginalFileName.Trim(),
            request.MappingProfileId.Trim(),
            request.ToleranceProfileId.Trim(),
            request.ImportedBy.Trim(),
            sourceFileHash);

    private static StatementRunBreakDto ToRunBreakDto(ReconciliationBreakRecord item)
        => new(
            item.BreakId,
            item.RunId,
            item.ImportId,
            item.SourceReference,
            MapBreakType(item.BreakCode),
            item.Category,
            item.Delta,
            item.Tolerance,
            item.ToleranceBreached,
            item.CreatedAtUtc,
            item.Status);

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
                            new Meridian.Domain.Reconciliation.ReconciliationCaseComment(
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

    private static StatementReconciliationCaseDto ToStatementCaseDto(ReconciliationCase item)
        => new(
            CaseId: item.CaseId,
            RunId: item.ImportId,
            Status: item.Status,
            Priority: item.Priority,
            Title: item.Reason,
            Summary: item.BreakExplanation?.Summary ?? item.Rationale,
            BreakIds: item.EvidenceReferences
                .Where(static reference => reference.StartsWith("statement-row:", StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            CreatedAtUtc: item.CreatedAtUtc,
            LastUpdatedAtUtc: item.LastUpdatedAtUtc,
            LastUpdatedBy: item.LastUpdatedBy,
            Owner: item.Owner,
            DueAtUtc: item.DueAtUtc,
            ResolvedAtUtc: item.Resolution?.ResolvedAtUtc,
            ResolutionCode: item.Resolution?.ResolutionCode,
            ResolutionSummary: item.Resolution?.Summary,
            EvidenceLink: item.EvidenceReferences.FirstOrDefault());

    private static ReconciliationCaseSummaryDto ToCaseSummary(ReconciliationCase item)
        => new(
            item.CaseId,
            item.ImportId,
            item.Status,
            item.Reason,
            item.Confidence,
            item.Rationale,
            item.CreatedAtUtc.ToString("O"),
            Assignee: item.Owner,
            Priority: item.Priority,
            SlaDueAtUtc: item.DueAtUtc?.ToString("O"),
            SlaBreachedAtUtc: item.SlaBreachedAtUtc?.ToString("O"),
            SlaState: item.SlaBreachedAtUtc.HasValue ? "Breached" : "OnTrack",
            BusinessAgeHours: Math.Max(0, (DateTimeOffset.UtcNow - item.CreatedAtUtc).TotalHours),
            ResolutionCode: item.Resolution?.ResolutionCode,
            ResolutionNote: item.Resolution?.Summary,
            SignedOffBy: item.Resolution?.SignedOffBy,
            SignedOffAtUtc: item.Resolution?.SignedOffAtUtc?.ToString("O"),
            Version: item.History.Count + item.AuditEvents.Count);

    private static void ValidateCreateRequest(StatementRunCreateDto request)
    {
        if (request.StatementPeriodEnd < request.StatementPeriodStart)
        {
            throw new ArgumentException("Statement period end must be on or after statement period start.", nameof(request));
        }

        foreach (var (name, value) in new[]
        {
            (nameof(request.Broker), request.Broker),
            (nameof(request.SourceInstitution), request.SourceInstitution),
            (nameof(request.FundAccountId), request.FundAccountId),
            (nameof(request.ExternalAccountId), request.ExternalAccountId),
            (nameof(request.SourcePath), request.SourcePath),
            (nameof(request.MappingProfileId), request.MappingProfileId),
            (nameof(request.ToleranceProfileId), request.ToleranceProfileId),
            (nameof(request.ImportedBy), request.ImportedBy)
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{name} is required.", name);
            }
        }
    }

    private static async Task<string> ComputeSourceFileHashAsync(string sourcePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(sourcePath);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static StatementBreakType MapBreakType(string breakCode)
    {
        if (breakCode.Contains("cash", StringComparison.OrdinalIgnoreCase))
        {
            return StatementBreakType.CashBalanceMismatch;
        }

        if (breakCode.Contains("transaction", StringComparison.OrdinalIgnoreCase) || breakCode.Contains("txn", StringComparison.OrdinalIgnoreCase))
        {
            return StatementBreakType.TransactionAmountMismatch;
        }

        if (breakCode.Contains("security", StringComparison.OrdinalIgnoreCase) || breakCode.Contains("symbol", StringComparison.OrdinalIgnoreCase))
        {
            return StatementBreakType.SecurityIdentifierMismatch;
        }

        if (breakCode.Contains("quantity", StringComparison.OrdinalIgnoreCase) || breakCode.Contains("qty", StringComparison.OrdinalIgnoreCase))
        {
            return StatementBreakType.PositionQuantityMismatch;
        }

        if (breakCode.Contains("position", StringComparison.OrdinalIgnoreCase))
        {
            return StatementBreakType.PositionMarketValueMismatch;
        }

        return StatementBreakType.Unknown;
    }
}
