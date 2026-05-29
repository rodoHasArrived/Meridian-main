using System.Security.Cryptography;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ui.Shared.Contracts.Reconciliation;

namespace Meridian.Ui.Services.Services.Reconciliation;

public sealed class ReconciliationApiService : IReconciliationApiService
{
    internal const long MaxStatementSourceFileBytes = 100L * 1024L * 1024L;
    private const string ImportRootEnvironmentVariable = "MERIDIAN_STATEMENT_IMPORT_ROOT";

    private readonly ICanonicalStatementStore importStore;
    private readonly IReconciliationCaseStore caseStore;
    private readonly IReconciliationBreakStore breakStore;
    private readonly string statementImportRoot;

    public ReconciliationApiService(
        ICanonicalStatementStore importStore,
        IReconciliationCaseStore caseStore,
        IReconciliationBreakStore breakStore)
        : this(importStore, caseStore, breakStore, ResolveDefaultStatementImportRoot())
    {
    }

    public ReconciliationApiService(
        ICanonicalStatementStore importStore,
        IReconciliationCaseStore caseStore,
        IReconciliationBreakStore breakStore,
        string statementImportRoot)
    {
        ArgumentNullException.ThrowIfNull(importStore);
        ArgumentNullException.ThrowIfNull(caseStore);
        ArgumentNullException.ThrowIfNull(breakStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(statementImportRoot);

        this.importStore = importStore;
        this.caseStore = caseStore;
        this.breakStore = breakStore;
        this.statementImportRoot = NormalizeDirectoryPath(statementImportRoot);
    }

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

        var sourceFile = ResolveStatementSourceFile(request.SourcePath);
        var sourceFileHash = await ComputeSourceFileHashAsync(sourceFile.FullName, ct).ConfigureAwait(false);
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
            sourceFile.FullName,
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
                ? sourceFile.Name
                : request.OriginalFileName.Trim(),
            MappingProfileId = request.MappingProfileId.Trim(),
            ToleranceProfileId = request.ToleranceProfileId.Trim(),
            ImportedBy = request.ImportedBy.Trim(),
            SourceFileHash = sourceFileHash,
            DuplicateKey = importId
        };

        await importStore.SaveImportAsync(import, [], ct).ConfigureAwait(false);
        return ToRunDto(import, [], StatementRunStatus.Completed, request.Notes);
    }

    public async Task<StatementRunDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
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
            .Select(static item => new StatementRunBreakDto(
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

    private FileInfo ResolveStatementSourceFile(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath.Trim());
        if (!IsPathInsideDirectory(fullPath, statementImportRoot))
        {
            throw new ArgumentException("Statement source file must be staged under the configured reconciliation import root.", nameof(sourcePath));
        }

        var sourceFile = new FileInfo(fullPath);
        if (!sourceFile.Exists)
        {
            throw new FileNotFoundException("Statement source file was not found in the configured reconciliation import root.", fullPath);
        }

        if ((sourceFile.Attributes & FileAttributes.Directory) != 0 || (sourceFile.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException("Statement source must be a regular file and cannot be a directory or link.", nameof(sourcePath));
        }

        if (sourceFile.Length > MaxStatementSourceFileBytes)
        {
            throw new ArgumentException($"Statement source file exceeds the {MaxStatementSourceFileBytes} byte workstation import limit.", nameof(sourcePath));
        }

        return sourceFile;
    }

    private static async Task<string> ComputeSourceFileHashAsync(string sourcePath, CancellationToken ct)
    {
        var options = new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            Share = FileShare.Read,
            BufferSize = 64 * 1024
        };

        await using var stream = new FileStream(sourcePath, options);
        if (!stream.CanSeek || stream.Length > MaxStatementSourceFileBytes)
        {
            throw new ArgumentException("Statement source must be a bounded regular file.", nameof(sourcePath));
        }

        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string ResolveDefaultStatementImportRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(ImportRootEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(AppContext.BaseDirectory, "data", "reconciliation", "statement-import-staging")
            : configuredRoot;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path.Trim());
        return fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return path.StartsWith(directory, comparison);
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
