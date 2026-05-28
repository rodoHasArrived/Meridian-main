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
            .Select(i => new StatementImportSummaryDto(i.ImportId, i.Broker, i.StatementDate.ToString("yyyy-MM-dd"), i.ImportedAtUtc.ToString("O"), i.RawRowCount, i.NormalizedRowCount))
            .ToList();

    public async Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(CancellationToken ct = default)
    {
        var imports = await importStore.ListImportsAsync(ct).ConfigureAwait(false);
        var breaks = await breakStore.ListOpenAsync(ct).ConfigureAwait(false);
        var cases = await caseStore.ListAsync(ct).ConfigureAwait(false);
        return imports.Select(i => ToStatementRunSummary(i, breaks, cases)).ToList();
    }

    public async Task<StatementRunSummaryDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
        => (await ListStatementRunsAsync(ct).ConfigureAwait(false)).FirstOrDefault(x => string.Equals(x.RunId, runId, StringComparison.OrdinalIgnoreCase));
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
        return ToRunDto(import, [], status: StatementRunStatus.Completed, notes: request.Notes);
    }

    public async Task<StatementRunDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
    {
        var import = await FindImportAsync(runId, ct).ConfigureAwait(false);
        if (import is null)
        {
            return null;
        }

        var breaks = await ListBreakDtosAsync(import.ImportId, ct).ConfigureAwait(false);
        return ToRunDto(import, breaks, breaks.Any(static b => string.Equals(b.Status, "Open", StringComparison.OrdinalIgnoreCase))
            ? StatementRunStatus.ReviewRequired
            : StatementRunStatus.Completed);
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
        return ToRunDto(import, breaks, breaks.Count == 0 ? StatementRunStatus.Completed : StatementRunStatus.ReviewRequired);
    }

    public async Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(CancellationToken ct = default)
        => (await breakStore.ListOpenAsync(ct).ConfigureAwait(false))
            .Select(x => new StatementRunExceptionDto(x.BreakId, x.RunId, x.ImportId, x.SourceReference, x.BreakCode, x.Category, x.Delta, x.Tolerance, x.ToleranceBreached, x.CreatedAtUtc.ToString("O"), x.Status))
            .ToList();

    public async Task<IReadOnlyList<StatementBreakDto>> ListOpenStatementBreaksAsync(CancellationToken ct = default)
        => (await breakStore.ListOpenAsync(ct)).Select(x => new StatementBreakDto(
            BreakId: x.BreakId,
            BreakType: MapStatementBreakType(x.Category, x.BreakCode),
            Severity: x.ToleranceBreached ? StatementValidationSeverity.Error : StatementValidationSeverity.Warning,
            MatchTier: StatementMatchTier.Manual,
            StatementReference: x.SourceReference,
            Description: $"{x.Category} break {x.BreakCode} requires statement reconciliation review.",
            StatementAmount: x.Delta,
            BookAmount: null,
            Delta: x.Delta,
            Tolerance: x.Tolerance,
            Currency: null,
            CreatedAtUtc: x.CreatedAtUtc,
            Status: x.Status,
            InternalReference: x.RunId,
            Owner: null,
            LastObservedAtUtc: x.CreatedAtUtc,
            RecommendedAction: "ReviewAndResolve",
            EvidenceLink: $"/api/workstation/reconciliation/exceptions/{Uri.EscapeDataString(x.BreakId)}")).ToList();

    public async Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(CancellationToken ct = default)
        => (await caseStore.ListAsync(ct).ConfigureAwait(false))
            .Where(c => c.Status == "Open")
            .Select(c => new ReconciliationCaseSummaryDto(c.CaseId, c.ImportId, c.Status, c.Reason, c.Confidence, c.Rationale, c.CreatedAtUtc.ToString("O")))
            .ToList();

    public async Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(CancellationToken ct = default)
    {
        var openCases = (await caseStore.ListAsync(ct).ConfigureAwait(false)).Where(c => c.Status == "Open").ToList();
        return openCases
            .GroupBy(c => c.ImportId)
            .Select(group => new ReconciliationQueueAccountStatusDto(
                AccountId: Guid.Empty,
                AccountCode: group.Key,
                QueueState: group.Any(c => c.Confidence < 0.5m) ? "Blocked" : "Review",
                UnresolvedBreakCount: group.Count(),
                SignOffReady: false,
                NextBestAction: "Resolve open reconciliation breaks before operator sign-off.",
                BlockerReason: "Unresolved breaks remain in the reconciliation queue.",
                EvidenceLinks: group.Select(c => $"/api/workstation/reconciliation/cases/{Uri.EscapeDataString(c.CaseId)}").ToList()))
            .ToList();
    }

    private static StatementRunSummaryDto ToStatementRunSummary(
        Meridian.Domain.Reconciliation.CanonicalStatementImport import,
        IReadOnlyList<Meridian.Domain.Reconciliation.ReconciliationBreakRecord> breaks,
        IReadOnlyList<Meridian.Domain.Reconciliation.ReconciliationCase> cases)
    {
        var relatedBreaks = breaks
            .Where(item => string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.RunId, import.ImportId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var relatedCases = cases
            .Where(item => string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var openExceptionCount = relatedBreaks.Count(item => string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase));
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
        Meridian.Domain.Reconciliation.CanonicalStatementImport import,
        IReadOnlyList<Meridian.Domain.Reconciliation.ReconciliationBreakRecord> breaks,
        IReadOnlyList<Meridian.Domain.Reconciliation.ReconciliationCase> cases,
        int matchedCount)
    {
        var runId = import.ImportId;
        var breakIds = breaks
            .Select(static item => item.BreakId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var caseIds = cases
            .Select(static item => item.CaseId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceFileHash = FirstNonEmpty(import.SourceFileHash, import.SourceChecksum);
        var brokerCustodian = FirstNonEmpty(import.SourceInstitution, import.Broker);
        var account = FirstNonEmpty(import.ExternalAccountId, import.FundAccountId);
        var validationSummary = "Passed: 0 issue(s), 0 error(s), 0 warning(s).";
        var matchSummary = $"{matchedCount}/{import.NormalizedRowCount} item(s) matched; {breakIds.Length} break(s); {caseIds.Length} case(s).";

        return new StatementRunEvidenceLinkDto(
            EvidenceId: $"statement-run:{runId}",
            EvidenceRoute: $"/api/workstation/evidence/statement-run/{Uri.EscapeDataString(runId)}",
            RunId: runId,
            SourceFileHash: sourceFileHash,
            BrokerCustodian: brokerCustodian,
            Account: account,
            StatementPeriodStart: import.StatementPeriodStart.ToString("yyyy-MM-dd"),
            StatementPeriodEnd: import.StatementPeriodEnd.ToString("yyyy-MM-dd"),
            MappingProfileId: FirstNonEmpty(import.MappingProfileId, "unknown"),
            MappingProfileVersion: 1,
            ToleranceProfileId: FirstNonEmpty(import.ToleranceProfileId, "statement-default"),
            ToleranceProfileVersion: 1,
            ValidationSummary: validationSummary,
            MatchSummary: matchSummary,
            BreakIds: breakIds,
            CaseIds: caseIds,
            ImportedBy: FirstNonEmpty(import.ImportedBy, "system"),
            ImportedAtUtc: import.ImportedAtUtc.ToString("O"),
            ReconciledBy: FirstNonEmpty(import.ImportedBy, "system"),
            ReconciledAtUtc: import.ImportedAtUtc.ToString("O"));
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static StatementBreakType MapStatementBreakType(string category, string breakCode)
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
            .Where(breakRecord => string.Equals(breakRecord.RunId, runId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(breakRecord.ImportId, runId, StringComparison.OrdinalIgnoreCase))
            .Select(static breakRecord => new StatementRunBreakDto(
                breakRecord.BreakId,
                breakRecord.RunId,
                breakRecord.ImportId,
                breakRecord.SourceReference,
                MapBreakType(breakRecord.BreakCode),
                breakRecord.Category,
                breakRecord.Delta,
                breakRecord.Tolerance,
                breakRecord.ToleranceBreached,
                breakRecord.CreatedAtUtc,
                breakRecord.Status))
            .ToList();

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
        string? notes = null)
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
                PositionBreakCount: breaks.Count(static b => b.BreakType is StatementBreakType.PositionMarketValueMismatch or StatementBreakType.PositionQuantityMismatch),
                CashBreakCount: breaks.Count(static b => b.BreakType is StatementBreakType.CashBalanceMismatch),
                TransactionBreakCount: breaks.Count(static b => b.BreakType is StatementBreakType.TransactionAmountMismatch)),
            Breaks: breaks.Select(static b => new StatementBreakDto(
                b.BreakId,
                b.BreakType,
                b.ToleranceBreached ? StatementValidationSeverity.Error : StatementValidationSeverity.Warning,
                b.ToleranceBreached ? StatementMatchTier.Unmatched : StatementMatchTier.WithinTolerance,
                b.SourceReference,
                b.Category,
                b.Delta,
                null,
                b.Delta,
                b.Tolerance,
                null,
                b.CreatedAtUtc,
                b.Status)).ToList(),
            Cases: [],
            ImportId: import.ImportId,
            FundProfileId: import.FundAccountId,
            FundAccountId: Guid.TryParse(import.FundAccountId, out var accountId) ? accountId : null,
            Notes: notes);
    }

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

        if (breakCode.Contains("transaction", StringComparison.OrdinalIgnoreCase))
        {
            return StatementBreakType.TransactionAmountMismatch;
        }

        if (breakCode.Contains("security", StringComparison.OrdinalIgnoreCase))
        {
            return StatementBreakType.SecurityIdentifierMismatch;
        }

        if (breakCode.Contains("quantity", StringComparison.OrdinalIgnoreCase))
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
