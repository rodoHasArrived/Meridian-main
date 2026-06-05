using Meridian.FinancialOperations.Reconciliation;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using WorkstationStatementMatchTier = Meridian.Contracts.Workstation.StatementMatchTier;

namespace Meridian.Ui.Services.Services.Reconciliation;

public sealed class ReconciliationApiService(
    IStatementRunWorkflowService statementRunWorkflowService) : IReconciliationApiService
{
    public async Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(CancellationToken ct = default)
        => (await statementRunWorkflowService.ListImportsAsync(ct).ConfigureAwait(false))
            .Select(static import => new StatementImportSummaryDto(
                import.ImportId,
                import.Broker,
                import.StatementDate.ToString("yyyy-MM-dd"),
                import.ImportedAtUtc.ToString("O"),
                import.RawRowCount,
                import.NormalizedRowCount))
            .ToList();

    public async Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(CancellationToken ct = default)
        => (await statementRunWorkflowService.ListImportsAsync(ct).ConfigureAwait(false))
            .Select(ToSummary)
            .ToList();

    public async Task<StatementRunDto?> CreateStatementRunAsync(StatementRunCreateDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCreateRequest(request);

        var result = await statementRunWorkflowService
            .CreateAsync(ToWorkflowRequest(request), ct)
            .ConfigureAwait(false);
        var breakDtos = result.Breaks.Select(ToRunBreakDto).ToArray();
        var status = breakDtos.Length == 0 ? StatementRunStatus.Completed : StatementRunStatus.ReviewRequired;
        return ToRunDto(result.Import, breakDtos, status, request.Notes, result.Cases);
    }

    public async Task<StatementRunDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
    {
        var result = await statementRunWorkflowService.GetAsync(runId, ct).ConfigureAwait(false);
        if (result is null)
        {
            return null;
        }

        var breaks = result.Breaks.Select(ToRunBreakDto).ToArray();
        var cases = result.Cases;
        return ToRunDto(
            result.Import,
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
        var result = await statementRunWorkflowService.GetAsync(runId, ct).ConfigureAwait(false);
        return result is null ? null : result.Breaks.Select(ToRunBreakDto).ToArray();
    }

    public async Task<StatementRunDto?> ReconcileStatementRunAsync(string runId, StatementRunReconcileRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await statementRunWorkflowService.GetAsync(runId, ct).ConfigureAwait(false);
        if (result is null)
        {
            return null;
        }

        var breaks = result.Breaks.Select(ToRunBreakDto).ToArray();
        return ToRunDto(
            result.Import,
            breaks,
            breaks.Length == 0 ? StatementRunStatus.Completed : StatementRunStatus.ReviewRequired,
            cases: result.Cases);
    }

    public async Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(CancellationToken ct = default)
        => (await statementRunWorkflowService.ListOpenBreaksAsync(ct).ConfigureAwait(false))
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
        => (await statementRunWorkflowService.ListOpenBreaksAsync(ct).ConfigureAwait(false))
            .Select(static item => new StatementBreakDto(
                BreakId: item.BreakId,
                BreakType: MapBreakType(item.BreakCode),
                Severity: item.ToleranceBreached ? StatementValidationSeverity.Error : StatementValidationSeverity.Warning,
                MatchTier: WorkstationStatementMatchTier.Manual,
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
        => (await statementRunWorkflowService.ListCasesAsync(ct).ConfigureAwait(false))
            .Where(static item => string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Status, "InReview", StringComparison.OrdinalIgnoreCase))
            .Select(ToCaseSummary)
            .ToList();

    public async Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(CancellationToken ct = default)
    {
        var breaks = await statementRunWorkflowService.ListOpenBreaksAsync(ct).ConfigureAwait(false);
        var cases = await statementRunWorkflowService.ListCasesAsync(ct).ConfigureAwait(false);
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
                item.ToleranceBreached ? WorkstationStatementMatchTier.Unmatched : WorkstationStatementMatchTier.WithinTolerance,
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

    private static StatementRunRequest ToWorkflowRequest(StatementRunCreateDto request)
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
            string.IsNullOrWhiteSpace(request.SourceFileHash) ? string.Empty : request.SourceFileHash.Trim());

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
            EvidenceLink: item.EvidenceReferences.FirstOrDefault(),
            Disposition: item.Disposition,
            AgingDays: item.AgingDays,
            CommentThreads: item.CommentThreads.Select(ToStatementCaseCommentThreadDto).ToArray(),
            Attachments: item.Attachments.Select(ToStatementCaseAttachmentDto).ToArray(),
            BreakExplanation: item.BreakExplanation is null ? null : ToStatementBreakExplanationDto(item.BreakExplanation),
            AuditEvents: item.AuditEvents.Select(ToStatementCaseAuditEventDto).ToArray());

    private static StatementReconciliationCaseCommentThreadDto ToStatementCaseCommentThreadDto(ReconciliationCaseCommentThread item) =>
        new(
            item.ThreadId,
            item.Subject,
            item.Comments.Select(static comment => new StatementReconciliationCaseCommentDto(
                comment.CommentId,
                comment.Body,
                comment.Actor,
                comment.CreatedAtUtc,
                comment.ParentCommentId)).ToArray());

    private static StatementReconciliationCaseAttachmentDto ToStatementCaseAttachmentDto(ReconciliationCaseAttachment item) =>
        new(
            item.AttachmentId,
            item.EvidenceKind,
            item.SourceSystem,
            item.SourceReference,
            item.ContentHash,
            item.Route,
            item.AttachedAtUtc);

    private static StatementReconciliationBreakExplanationDto ToStatementBreakExplanationDto(ReconciliationBreakExplanation item) =>
        new(
            item.Summary,
            item.SourceSystems,
            item.ProbableCause,
            item.LedgerImpact,
            item.SuggestedNextAction,
            item.RequiredSignoffRole,
            item.EvidenceLinks);

    private static StatementReconciliationCaseAuditEventDto ToStatementCaseAuditEventDto(ReconciliationCaseAuditEvent item) =>
        new(
            item.EventId,
            item.EventType,
            item.OccurredAtUtc,
            item.Actor,
            item.Detail);

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
