using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using ContractStatementBreakDto = Meridian.Contracts.Workstation.StatementBreakDto;
using ContractStatementBreakType = Meridian.Contracts.Workstation.StatementBreakType;
using ContractStatementMatchTier = Meridian.Contracts.Workstation.StatementMatchTier;
using ContractStatementRunBreakDto = Meridian.Contracts.Workstation.StatementRunBreakDto;
using ContractStatementRunCreateDto = Meridian.Contracts.Workstation.StatementRunCreateDto;
using ContractStatementRunDto = Meridian.Contracts.Workstation.StatementRunDto;
using ContractStatementRunReconcileRequestDto = Meridian.Contracts.Workstation.StatementRunReconcileRequestDto;
using ContractStatementRunStatus = Meridian.Contracts.Workstation.StatementRunStatus;
using ContractStatementRunValidationDto = Meridian.Contracts.Workstation.StatementRunValidationDto;
using ContractStatementSourceDto = Meridian.Contracts.Workstation.StatementSourceDto;
using ContractStatementValidationSeverity = Meridian.Contracts.Workstation.StatementValidationSeverity;
using ContractStatementMatchSummaryDto = Meridian.Contracts.Workstation.StatementMatchSummaryDto;

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
    {
        var imports = await importStore.ListImportsAsync(ct).ConfigureAwait(false);
        var breaks = await breakStore.ListOpenAsync(ct).ConfigureAwait(false);
        var cases = await caseStore.ListAsync(ct).ConfigureAwait(false);
        return imports.Select(import => ToStatementRunSummary(import, breaks, cases)).ToList();
    }

    public async Task<ContractStatementRunDto?> CreateStatementRunAsync(ContractStatementRunCreateDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCreateRequest(request);

        var sourceFileHash = string.IsNullOrWhiteSpace(request.SourceFileHash)
            ? await ComputeSourceFileHashAsync(request.SourcePath, ct).ConfigureAwait(false)
            : request.SourceFileHash.Trim().ToUpperInvariant();
        var importId = StatementDuplicateKey.Create(
            request.FundAccountId,
            request.StatementPeriodStart,
            request.StatementPeriodEnd,
            sourceFileHash);

        var import = new CanonicalStatementImport(
            importId,
            request.Broker.Trim(),
            request.StatementPeriodEnd,
            DateTimeOffset.UtcNow,
            request.SourcePath.Trim(),
            sourceFileHash,
            RawRowCount: 0,
            NormalizedRowCount: 0)
        {
            SourceInstitution = request.SourceInstitution.Trim(),
            FundAccountId = request.FundAccountId.Trim(),
            ExternalAccountId = request.ExternalAccountId.Trim(),
            StatementPeriodStart = request.StatementPeriodStart,
            StatementPeriodEnd = request.StatementPeriodEnd,
            OriginalFileName = string.IsNullOrWhiteSpace(request.OriginalFileName)
                ? Path.GetFileName(request.SourcePath)
                : request.OriginalFileName.Trim(),
            MappingProfileId = request.MappingProfileId.Trim(),
            ToleranceProfileId = request.ToleranceProfileId.Trim(),
            ImportedBy = request.ImportedBy.Trim(),
            SourceFileHash = sourceFileHash,
            DuplicateKey = importId
        };

        await importStore.SaveImportAsync(import, [], ct).ConfigureAwait(false);
        return ToRunDto(import, [], ContractStatementRunStatus.Completed, request.Notes);
    }

    public async Task<ContractStatementRunDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
    {
        var import = await FindImportAsync(runId, ct).ConfigureAwait(false);
        if (import is null)
        {
            return null;
        }

        var breaks = await ListBreakDtosAsync(import.ImportId, ct).ConfigureAwait(false);
        return ToRunDto(
            import,
            breaks,
            breaks.Any(static item => string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase))
                ? ContractStatementRunStatus.ReviewRequired
                : ContractStatementRunStatus.Completed);
    }

    public async Task<ContractStatementRunValidationDto?> GetStatementRunValidationAsync(string runId, CancellationToken ct = default)
    {
        var run = await GetStatementRunAsync(runId, ct).ConfigureAwait(false);
        return run is null
            ? null
            : new ContractStatementRunValidationDto(run.RunId ?? runId, run.ValidationIssues ?? [], IsBlocked: false);
    }

    public async Task<IReadOnlyList<ContractStatementRunBreakDto>?> ListStatementRunBreaksAsync(string runId, CancellationToken ct = default)
    {
        var import = await FindImportAsync(runId, ct).ConfigureAwait(false);
        return import is null ? null : await ListBreakDtosAsync(import.ImportId, ct).ConfigureAwait(false);
    }

    public async Task<ContractStatementRunDto?> ReconcileStatementRunAsync(string runId, ContractStatementRunReconcileRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var import = await FindImportAsync(runId, ct).ConfigureAwait(false);
        if (import is null)
        {
            return null;
        }

        var breaks = await ListBreakDtosAsync(import.ImportId, ct).ConfigureAwait(false);
        return ToRunDto(import, breaks, breaks.Count == 0 ? ContractStatementRunStatus.Completed : ContractStatementRunStatus.ReviewRequired);
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

    public async Task<IReadOnlyList<ContractStatementBreakDto>> ListOpenStatementBreaksAsync(CancellationToken ct = default)
        => (await breakStore.ListOpenAsync(ct).ConfigureAwait(false))
            .Select(static item => new ContractStatementBreakDto(
                BreakId: item.BreakId,
                BreakType: MapBreakType(item.BreakCode),
                Severity: item.ToleranceBreached ? ContractStatementValidationSeverity.Error : ContractStatementValidationSeverity.Warning,
                MatchTier: ContractStatementMatchTier.Manual,
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

    private static StatementRunSummaryDto ToStatementRunSummary(
        CanonicalStatementImport import,
        IReadOnlyList<ReconciliationBreakRecord> breaks,
        IReadOnlyList<ReconciliationCase> cases)
    {
        var relatedBreaks = breaks
            .Where(item => string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.RunId, import.ImportId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var relatedCases = cases
            .Where(item => string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var openExceptionCount = relatedBreaks.Count(static item => string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase));
        var matchedCount = Math.Max(0, import.NormalizedRowCount - relatedBreaks.Length);
        var evidenceLink = BuildEvidenceLink(import, relatedBreaks, relatedCases, matchedCount);

        return new StatementRunSummaryDto(
            import.ImportId,
            import.ImportId,
            import.ImportedAtUtc.ToString("O"),
            import.ImportedAtUtc.ToString("O"),
            matchedCount,
            0,
            0,
            openExceptionCount,
            [evidenceLink]);
    }

    private static StatementRunEvidenceLinkDto BuildEvidenceLink(
        CanonicalStatementImport import,
        IReadOnlyList<ReconciliationBreakRecord> breaks,
        IReadOnlyList<ReconciliationCase> cases,
        int matchedCount)
    {
        var runId = import.ImportId;
        var breakIds = breaks.Select(static item => item.BreakId).Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        var caseIds = cases.Select(static item => item.CaseId).Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray();

        return new StatementRunEvidenceLinkDto(
            EvidenceId: $"statement-run:{runId}",
            EvidenceRoute: $"/api/workstation/evidence/statement-run/{Uri.EscapeDataString(runId)}",
            RunId: runId,
            SourceFileHash: FirstNonEmpty(import.SourceFileHash, import.SourceChecksum),
            BrokerCustodian: FirstNonEmpty(import.SourceInstitution, import.Broker),
            Account: FirstNonEmpty(import.ExternalAccountId, import.FundAccountId),
            StatementPeriodStart: import.StatementPeriodStart.ToString("yyyy-MM-dd"),
            StatementPeriodEnd: import.StatementPeriodEnd.ToString("yyyy-MM-dd"),
            MappingProfileId: FirstNonEmpty(import.MappingProfileId, "unknown"),
            MappingProfileVersion: 1,
            ToleranceProfileId: FirstNonEmpty(import.ToleranceProfileId, "statement-default"),
            ToleranceProfileVersion: 1,
            ValidationSummary: "Passed: 0 issue(s), 0 error(s), 0 warning(s).",
            MatchSummary: $"{matchedCount}/{import.NormalizedRowCount} item(s) matched; {breakIds.Length} break(s); {caseIds.Length} case(s).",
            BreakIds: breakIds,
            CaseIds: caseIds,
            ImportedBy: FirstNonEmpty(import.ImportedBy, "system"),
            ImportedAtUtc: import.ImportedAtUtc.ToString("O"),
            ReconciledBy: FirstNonEmpty(import.ImportedBy, "system"),
            ReconciledAtUtc: import.ImportedAtUtc.ToString("O"));
    }

    private async Task<CanonicalStatementImport?> FindImportAsync(string runId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        return (await importStore.ListImportsAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(import => string.Equals(import.ImportId, runId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<ContractStatementRunBreakDto>> ListBreakDtosAsync(string runId, CancellationToken ct)
        => (await breakStore.ListOpenAsync(ct).ConfigureAwait(false))
            .Where(item => string.Equals(item.RunId, runId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.ImportId, runId, StringComparison.OrdinalIgnoreCase))
            .Select(static item => new ContractStatementRunBreakDto(
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
                item.Status))
            .ToList();

    private static ContractStatementRunDto ToRunDto(
        CanonicalStatementImport import,
        IReadOnlyList<ContractStatementRunBreakDto> breaks,
        ContractStatementRunStatus status,
        string? notes = null)
    {
        var source = new ContractStatementSourceDto(
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

        return new ContractStatementRunDto(
            RunId: import.ImportId,
            Status: status,
            Source: source,
            StartedAtUtc: import.ImportedAtUtc,
            CompletedAtUtc: status is ContractStatementRunStatus.Completed ? import.ImportedAtUtc : null,
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
            MatchSummary: new ContractStatementMatchSummaryDto(
                StatementItemCount: import.NormalizedRowCount,
                MatchedItemCount: Math.Max(0, import.NormalizedRowCount - breaks.Count),
                AutoMatchedItemCount: Math.Max(0, import.NormalizedRowCount - breaks.Count),
                ManualMatchedItemCount: 0,
                ReviewRequiredItemCount: breaks.Count,
                BreakCount: breaks.Count,
                PositionBreakCount: breaks.Count(static item => item.BreakType is ContractStatementBreakType.PositionMarketValueMismatch or ContractStatementBreakType.PositionQuantityMismatch),
                CashBreakCount: breaks.Count(static item => item.BreakType is ContractStatementBreakType.CashBalanceMismatch),
                TransactionBreakCount: breaks.Count(static item => item.BreakType is ContractStatementBreakType.TransactionAmountMismatch)),
            Breaks: breaks.Select(static item => new ContractStatementBreakDto(
                item.BreakId,
                item.BreakType,
                item.ToleranceBreached ? ContractStatementValidationSeverity.Error : ContractStatementValidationSeverity.Warning,
                item.ToleranceBreached ? ContractStatementMatchTier.Unmatched : ContractStatementMatchTier.WithinTolerance,
                item.SourceReference,
                item.Category,
                item.Delta,
                null,
                item.Delta,
                item.Tolerance,
                null,
                item.CreatedAtUtc,
                item.Status)).ToList(),
            Cases: [],
            ImportId: import.ImportId,
            FundProfileId: import.FundAccountId,
            FundAccountId: Guid.TryParse(import.FundAccountId, out var accountId) ? accountId : null,
            Notes: notes);
    }

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

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static void ValidateCreateRequest(ContractStatementRunCreateDto request)
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

    private static ContractStatementBreakType MapBreakType(string breakCode)
    {
        if (breakCode.Contains("cash", StringComparison.OrdinalIgnoreCase))
        {
            return ContractStatementBreakType.CashBalanceMismatch;
        }

        if (breakCode.Contains("transaction", StringComparison.OrdinalIgnoreCase) || breakCode.Contains("txn", StringComparison.OrdinalIgnoreCase))
        {
            return ContractStatementBreakType.TransactionAmountMismatch;
        }

        if (breakCode.Contains("security", StringComparison.OrdinalIgnoreCase) || breakCode.Contains("symbol", StringComparison.OrdinalIgnoreCase))
        {
            return ContractStatementBreakType.SecurityIdentifierMismatch;
        }

        if (breakCode.Contains("quantity", StringComparison.OrdinalIgnoreCase) || breakCode.Contains("qty", StringComparison.OrdinalIgnoreCase))
        {
            return ContractStatementBreakType.PositionQuantityMismatch;
        }

        if (breakCode.Contains("position", StringComparison.OrdinalIgnoreCase))
        {
            return ContractStatementBreakType.PositionMarketValueMismatch;
        }

        return ContractStatementBreakType.Unknown;
    }
}
