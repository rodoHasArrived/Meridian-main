using System.Globalization;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using WorkstationStatementMatchTier = Meridian.Contracts.Workstation.StatementMatchTier;

namespace Meridian.Ui.Shared.Services;

public class ReconciliationApiService(
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
    {
        var imports = await statementRunWorkflowService.ListImportsAsync(ct).ConfigureAwait(false);
        var openBreaks = await statementRunWorkflowService.ListOpenBreaksAsync(ct).ConfigureAwait(false);
        var openBreakCounts = openBreaks
            .GroupBy(static item => string.IsNullOrWhiteSpace(item.RunId) ? item.ImportId : item.RunId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return imports
            .Select(import => ToSummary(import, openBreakCounts))
            .ToList();
    }

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
    {
        var breaks = await statementRunWorkflowService.ListOpenBreaksAsync(ct).ConfigureAwait(false);
        var casesByBreakId = (await statementRunWorkflowService.ListCasesAsync(ct).ConfigureAwait(false))
            .SelectMany(static reconciliationCase => ResolveCaseBreakIds(reconciliationCase)
                .Select(breakId => new KeyValuePair<string, ReconciliationCase>(breakId, reconciliationCase)))
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static item => item.Value)
                    .OrderByDescending(static item => item.LastUpdatedAtUtc)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        return breaks
            .Select(item => ToStatementBreakDto(item, casesByBreakId))
            .ToList();
    }

    public async Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(CancellationToken ct = default)
        => (await statementRunWorkflowService.ListCasesAsync(ct).ConfigureAwait(false))
            .Where(static item => string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Status, "InReview", StringComparison.OrdinalIgnoreCase))
            .Select(ToCaseSummary)
            .ToList();

    public async Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(CancellationToken ct = default)
    {
        var imports = await statementRunWorkflowService.ListImportsAsync(ct).ConfigureAwait(false);
        var breaks = await statementRunWorkflowService.ListOpenBreaksAsync(ct).ConfigureAwait(false);
        var cases = await statementRunWorkflowService.ListCasesAsync(ct).ConfigureAwait(false);
        var importById = imports.ToDictionary(static item => item.ImportId, StringComparer.OrdinalIgnoreCase);

        return breaks
            .GroupBy(item => ResolveQueueAccountCode(item, importById), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var relatedCases = cases
                    .Where(reconciliationCase => string.Equals(
                        ResolveCaseAccountCode(reconciliationCase, importById),
                        group.Key,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var criticalCount = group.Count(static item => item.ToleranceBreached);
                return new ReconciliationQueueAccountStatusDto(
                    AccountId: Guid.TryParse(group.Key, out var accountId) ? accountId : Guid.Empty,
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

    private static string ResolveQueueAccountCode(
        ReconciliationBreakRecord breakRecord,
        IReadOnlyDictionary<string, CanonicalStatementImport> importById)
    {
        var import = TryGetImport(breakRecord, importById);
        return FirstNonBlank(
            import?.FundAccountId,
            import?.ExternalAccountId,
            breakRecord.ImportId,
            breakRecord.RunId,
            "unknown");
    }

    private static string ResolveCaseAccountCode(
        ReconciliationCase reconciliationCase,
        IReadOnlyDictionary<string, CanonicalStatementImport> importById)
    {
        return importById.TryGetValue(reconciliationCase.ImportId, out var import)
            ? FirstNonBlank(import.FundAccountId, import.ExternalAccountId, reconciliationCase.ImportId, "unknown")
            : FirstNonBlank(reconciliationCase.ImportId, "unknown");
    }

    private static CanonicalStatementImport? TryGetImport(
        ReconciliationBreakRecord breakRecord,
        IReadOnlyDictionary<string, CanonicalStatementImport> importById)
    {
        if (!string.IsNullOrWhiteSpace(breakRecord.ImportId) &&
            importById.TryGetValue(breakRecord.ImportId, out var importByImportId))
        {
            return importByImportId;
        }

        return !string.IsNullOrWhiteSpace(breakRecord.RunId) &&
            importById.TryGetValue(breakRecord.RunId, out var importByRunId)
            ? importByRunId
            : null;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> ResolveCaseBreakIds(ReconciliationCase reconciliationCase)
    {
        const string casePrefix = "case:";
        if (reconciliationCase.CaseId.StartsWith(casePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var breakId = reconciliationCase.CaseId[casePrefix.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(breakId))
            {
                yield return breakId;
            }
        }
    }

    private static StatementBreakDto ToStatementBreakDto(
        ReconciliationBreakRecord item,
        IReadOnlyDictionary<string, ReconciliationCase> casesByBreakId)
    {
        casesByBreakId.TryGetValue(item.BreakId, out var reconciliationCase);
        var owner = FirstNonBlank(reconciliationCase?.Owner);
        var escalation = BuildStatementBreakEscalation(item, reconciliationCase, DateTimeOffset.UtcNow);
        return new StatementBreakDto(
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
            Owner: string.IsNullOrWhiteSpace(owner) ? null : owner,
            LastObservedAtUtc: reconciliationCase?.LastUpdatedAtUtc ?? item.CreatedAtUtc,
            RecommendedAction: FirstNonBlank(reconciliationCase?.BreakExplanation?.SuggestedNextAction, "ReviewAndResolve"),
            EvidenceLink: ResolveStatementBreakEvidenceLink(item, reconciliationCase),
            SlaDueAtUtc: reconciliationCase?.DueAtUtc,
            SlaWarningAtUtc: escalation.WarningAtUtc,
            SlaBreachedAtUtc: escalation.BreachedAtUtc,
            SlaState: escalation.SlaState,
            EscalationLabel: escalation.Label,
            EscalationReason: escalation.Reason);
    }

    private static string ResolveStatementBreakEvidenceLink(
        ReconciliationBreakRecord breakRecord,
        ReconciliationCase? reconciliationCase)
        => FirstNonBlank(
            reconciliationCase?.EvidenceReferences.FirstOrDefault(IsStatementRunEvidenceLink),
            breakRecord.EvidenceLink,
            $"/api/workstation/reconciliation/exceptions/{Uri.EscapeDataString(breakRecord.BreakId)}");

    private static bool IsStatementRunEvidenceLink(string reference)
        => reference.StartsWith("/api/workstation/reconciliation/statement-runs/", StringComparison.OrdinalIgnoreCase);

    private static StatementRunSummaryDto ToSummary(
        CanonicalStatementImport import,
        IReadOnlyDictionary<string, int> openBreakCounts)
    {
        var openExceptionCount = ResolveOpenExceptionCount(import, openBreakCounts);
        var status = openExceptionCount > 0 ? StatementRunStatus.ReviewRequired : StatementRunStatus.Completed;
        var matchedItemCount = Math.Max(0, import.NormalizedRowCount - openExceptionCount);

        return new StatementRunSummaryDto(
            import.ImportId,
            import.ImportId,
            status,
            import.ImportedAtUtc,
            status is StatementRunStatus.Completed ? import.ImportedAtUtc : null,
            matchedItemCount,
            0,
            0,
            openExceptionCount);
    }

    private static int ResolveOpenExceptionCount(
        CanonicalStatementImport import,
        IReadOnlyDictionary<string, int> openBreakCounts)
        => openBreakCounts.TryGetValue(import.ImportId, out var runCount) ? runCount : 0;

    private static StatementRunDto ToRunDto(
        CanonicalStatementImport import,
        IReadOnlyList<StatementRunBreakDto> breaks,
        StatementRunStatus status,
        string? notes = null,
        IReadOnlyList<ReconciliationCase>? cases = null)
    {
        var casesByBreakId = (cases ?? [])
            .SelectMany(static reconciliationCase => ResolveCaseBreakIds(reconciliationCase)
                .Select(breakId => new KeyValuePair<string, ReconciliationCase>(breakId, reconciliationCase)))
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static item => item.Value)
                    .OrderByDescending(static item => item.LastUpdatedAtUtc)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
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
            Breaks: breaks.Select(item => ToStatementBreakDto(item, casesByBreakId)).ToList(),
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

    private static StatementBreakDto ToStatementBreakDto(
        StatementRunBreakDto item,
        IReadOnlyDictionary<string, ReconciliationCase> casesByBreakId)
    {
        casesByBreakId.TryGetValue(item.BreakId, out var reconciliationCase);
        var owner = FirstNonBlank(reconciliationCase?.Owner);
        var escalation = BuildStatementBreakEscalation(item, reconciliationCase, DateTimeOffset.UtcNow);
        return new StatementBreakDto(
            BreakId: item.BreakId,
            BreakType: item.BreakType,
            Severity: item.ToleranceBreached ? StatementValidationSeverity.Error : StatementValidationSeverity.Warning,
            MatchTier: item.ToleranceBreached ? WorkstationStatementMatchTier.Unmatched : WorkstationStatementMatchTier.WithinTolerance,
            StatementReference: item.SourceReference,
            Description: $"{item.Category} break requires statement reconciliation review.",
            StatementAmount: item.Delta,
            BookAmount: null,
            Delta: item.Delta,
            Tolerance: item.Tolerance,
            Currency: null,
            CreatedAtUtc: item.CreatedAtUtc,
            Status: item.Status,
            InternalReference: item.RunId,
            Owner: string.IsNullOrWhiteSpace(owner) ? null : owner,
            LastObservedAtUtc: reconciliationCase?.LastUpdatedAtUtc ?? item.CreatedAtUtc,
            RecommendedAction: FirstNonBlank(reconciliationCase?.BreakExplanation?.SuggestedNextAction, "ReviewAndResolve"),
            EvidenceLink: ResolveStatementBreakEvidenceLink(item, reconciliationCase),
            SlaDueAtUtc: reconciliationCase?.DueAtUtc,
            SlaWarningAtUtc: escalation.WarningAtUtc,
            SlaBreachedAtUtc: escalation.BreachedAtUtc,
            SlaState: escalation.SlaState,
            EscalationLabel: escalation.Label,
            EscalationReason: escalation.Reason);
    }

    private static string ResolveStatementBreakEvidenceLink(
        StatementRunBreakDto breakRecord,
        ReconciliationCase? reconciliationCase)
        => FirstNonBlank(
            reconciliationCase?.EvidenceReferences.FirstOrDefault(IsStatementRunEvidenceLink),
            $"/api/workstation/reconciliation/exceptions/{Uri.EscapeDataString(breakRecord.BreakId)}");

    private static (DateTimeOffset? WarningAtUtc, DateTimeOffset? BreachedAtUtc, string SlaState, string Label, string Reason)
        BuildStatementBreakEscalation(
            ReconciliationBreakRecord breakRecord,
            ReconciliationCase? reconciliationCase,
            DateTimeOffset now)
        => BuildStatementBreakEscalation(
            breakRecord.ToleranceBreached,
            breakRecord.Status,
            breakRecord.CreatedAtUtc,
            reconciliationCase,
            now);

    private static (DateTimeOffset? WarningAtUtc, DateTimeOffset? BreachedAtUtc, string SlaState, string Label, string Reason)
        BuildStatementBreakEscalation(
            StatementRunBreakDto breakRecord,
            ReconciliationCase? reconciliationCase,
            DateTimeOffset now)
        => BuildStatementBreakEscalation(
            breakRecord.ToleranceBreached,
            breakRecord.Status,
            breakRecord.CreatedAtUtc,
            reconciliationCase,
            now);

    private static (DateTimeOffset? WarningAtUtc, DateTimeOffset? BreachedAtUtc, string SlaState, string Label, string Reason)
        BuildStatementBreakEscalation(
            bool toleranceBreached,
            string status,
            DateTimeOffset createdAtUtc,
            ReconciliationCase? reconciliationCase,
            DateTimeOffset now)
    {
        if (reconciliationCase is null)
        {
            return (
                null,
                null,
                "NotStarted",
                "Owner pending",
                toleranceBreached
                    ? "Tolerance-breached statement break has no retained case assignment; assign an owner and capture resolution evidence."
                    : "Statement break has no retained case assignment; assign an owner before close sign-off.");
        }

        var dueAt = reconciliationCase.DueAtUtc;
        var warningAt = dueAt?.AddHours(-12);
        var breachedAt = reconciliationCase.SlaBreachedAtUtc ??
                         (dueAt.HasValue && dueAt.Value <= now ? dueAt : null);
        var isClosed = status.Contains("resolved", StringComparison.OrdinalIgnoreCase) ||
                       status.Contains("dismissed", StringComparison.OrdinalIgnoreCase) ||
                       reconciliationCase.Status.Contains("resolved", StringComparison.OrdinalIgnoreCase);
        var owner = FirstNonBlank(reconciliationCase.Owner);
        var ownerLabel = string.IsNullOrWhiteSpace(owner) ? "owner" : owner;

        if (isClosed)
        {
            return (
                warningAt,
                breachedAt,
                "Stopped",
                "Closed",
                "Statement break case is closed; retain sign-off evidence for audit traceability.");
        }

        if (string.IsNullOrWhiteSpace(owner) ||
            string.Equals(owner, "unassigned", StringComparison.OrdinalIgnoreCase))
        {
            return (
                warningAt,
                breachedAt,
                breachedAt.HasValue ? "Breached" : "NotStarted",
                "Owner pending",
                "Assign an accountable owner before resolving the statement reconciliation break.");
        }

        if (breachedAt.HasValue)
        {
            return (
                warningAt,
                breachedAt,
                "Breached",
                "Escalate",
                $"Statement reconciliation break breached SLA at {FormatEscalationTimestamp(breachedAt.Value)}; escalate to the fund operations lead and retain resolution evidence.");
        }

        if (warningAt.HasValue && warningAt.Value <= now)
        {
            return (
                warningAt,
                null,
                "Warning",
                "SLA warning",
                dueAt.HasValue
                    ? $"Statement reconciliation break is assigned to {ownerLabel} and due by {FormatEscalationTimestamp(dueAt.Value)}."
                    : $"Statement reconciliation break is assigned to {ownerLabel} and requires active follow-up before close sign-off.");
        }

        return (
            warningAt,
            null,
            "OnTrack",
            "Assigned",
            dueAt.HasValue
                ? $"Statement reconciliation break is assigned to {ownerLabel}; SLA due {FormatEscalationTimestamp(dueAt.Value)}."
                : $"Statement reconciliation break is assigned to {ownerLabel}; retain resolution evidence before close sign-off.");
    }

    private static string FormatEscalationTimestamp(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

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
