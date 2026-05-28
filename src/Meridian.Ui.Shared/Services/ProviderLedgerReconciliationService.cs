using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;
using Meridian.Storage.Archival;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Compares the latest provider account projection with Meridian's internal account ledger snapshot.
/// </summary>
public sealed class ProviderLedgerReconciliationService
{
    private const string DefaultActor = "provider-ledger-reconciliation";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly BrokeragePortfolioSyncService _brokerageSync;
    private readonly IFundAccountService _fundAccountService;
    private readonly BrokeragePortfolioSyncOptions _options;
    private readonly ISecurityReferenceLookup? _securityReferenceLookup;
    private readonly ISecurityValidationGateService? _securityValidationGate;
    private readonly ILogger<ProviderLedgerReconciliationService> _logger;

    public ProviderLedgerReconciliationService(
        BrokeragePortfolioSyncService brokerageSync,
        IFundAccountService fundAccountService,
        BrokeragePortfolioSyncOptions options,
        ILogger<ProviderLedgerReconciliationService> logger,
        ISecurityReferenceLookup? securityReferenceLookup = null,
        ISecurityValidationGateService? securityValidationGate = null)
    {
        _brokerageSync = brokerageSync ?? throw new ArgumentNullException(nameof(brokerageSync));
        _fundAccountService = fundAccountService ?? throw new ArgumentNullException(nameof(fundAccountService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _securityReferenceLookup = securityReferenceLookup;
        _securityValidationGate = securityValidationGate;
    }

    public async Task<ProviderLedgerReconciliationDetailDto> RunAsync(
        Guid accountId,
        ProviderLedgerReconciliationRequestDto? request = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        request ??= new ProviderLedgerReconciliationRequestDto();
        var tolerance = Math.Abs(request.AmountTolerance);
        var staleAfterMinutes = Math.Max(1, request.ProviderStaleAfterMinutes);
        var runId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var previousLatest = await GetLatestAsync(accountId, ct).ConfigureAwait(false);
        var lifecycle = new BreakLifecycleContext(
            CreatedAt: createdAt,
            AmountTolerance: tolerance,
            DefaultOwner: NormalizeOwner(request.DefaultBreakOwner) ?? "fund-accounting",
            SignedOffBreakKeys: new HashSet<string>(request.SignedOffBreakKeys ?? [], StringComparer.OrdinalIgnoreCase),
            SignedOffBy: NormalizeOwner(request.SignedOffBy) ?? NormalizeOwner(request.RequestedBy),
            PreviousBreaksByKey: BuildPreviousBreakMap(previousLatest));

        var providerProjection = await _brokerageSync.GetActivityAsync(accountId, ct).ConfigureAwait(false);
        var internalSnapshot = await _fundAccountService.GetLatestBalanceSnapshotAsync(accountId, ct).ConfigureAwait(false);
        var checks = new List<ProviderLedgerReconciliationCheckDto>();
        var breaks = new List<ProviderLedgerReconciliationBreakDto>();
        var securityMasterPassports = new List<ProviderSecurityMasterPassportDto>();
        var warnings = new List<string>();
        var evidenceLinks = new List<string>();

        if (providerProjection is null)
        {
            AddBreak(
                runId,
                lifecycle,
                checks,
                breaks,
                "provider-projection-available",
                "Provider projection is available",
                ProviderLedgerReconciliationCheckStatusDto.Blocked,
                "PROVIDER_PROJECTION_MISSING",
                ReconciliationBreakCategory.MissingPortfolioCoverage,
                ReconciliationBreakSeverity.Critical,
                "provider-sync",
                "provider-sync",
                null,
                null,
                "No brokerage sync projection exists for this fund account.");
        }
        else
        {
            evidenceLinks.Add(providerProjection.ProjectionPath);
            evidenceLinks.Add(providerProjection.RawSnapshotPath);
            AddMatched(
                checks,
                "provider-projection-available",
                "Provider projection is available",
                ReconciliationBreakCategory.MissingPortfolioCoverage,
                "provider-sync",
                "provider-sync",
                null,
                null,
                "Latest brokerage sync projection is available.");

            var staleAfter = TimeSpan.FromMinutes(staleAfterMinutes);
            if (providerProjection.Status.IsStale || createdAt - providerProjection.SyncedAt > staleAfter)
            {
                AddBreak(
                    runId,
                    lifecycle,
                    checks,
                    breaks,
                    "provider-projection-freshness",
                    "Provider projection freshness",
                    ProviderLedgerReconciliationCheckStatusDto.Break,
                    "PROVIDER_PROJECTION_STALE",
                    ReconciliationBreakCategory.TimingMismatch,
                    ReconciliationBreakSeverity.Medium,
                    "provider-sync",
                    "provider-sync",
                    null,
                    null,
                    $"Provider projection is older than {staleAfterMinutes} minute(s).");
            }
            else
            {
                AddMatched(
                    checks,
                    "provider-projection-freshness",
                    "Provider projection freshness",
                    ReconciliationBreakCategory.TimingMismatch,
                    "provider-sync",
                    "provider-sync",
                    null,
                    null,
                    "Provider projection is within the configured freshness window.");
            }
        }

        if (internalSnapshot is null)
        {
            AddBreak(
                runId,
                lifecycle,
                checks,
                breaks,
                "internal-ledger-snapshot-available",
                "Internal ledger snapshot is available",
                ProviderLedgerReconciliationCheckStatusDto.Blocked,
                "INTERNAL_LEDGER_SNAPSHOT_MISSING",
                ReconciliationBreakCategory.MissingLedgerCoverage,
                ReconciliationBreakSeverity.Critical,
                "internal-ledger",
                "internal-ledger",
                null,
                null,
                "No internal account ledger/balance snapshot exists for this fund account.");
        }
        else
        {
            AddMatched(
                checks,
                "internal-ledger-snapshot-available",
                "Internal ledger snapshot is available",
                ReconciliationBreakCategory.MissingLedgerCoverage,
                "internal-ledger",
                "internal-ledger",
                null,
                null,
                "Internal account ledger/balance snapshot is available.");
        }

        if (providerProjection?.Balance is not null && internalSnapshot is not null)
        {
            AddAmountCheck(
                runId,
                lifecycle,
                checks,
                breaks,
                "cash-balance",
                "Cash balance",
                ReconciliationBreakCategory.CashMismatch,
                "CASH_BALANCE_MISMATCH",
                internalSnapshot.CashBalance,
                providerProjection.Balance.Cash,
                tolerance,
                ReconciliationBreakSeverity.High,
                "internal-ledger",
                "provider-sync");

            if (internalSnapshot.SecuritiesMarketValue is null)
            {
                AddBreak(
                    runId,
                    lifecycle,
                    checks,
                    breaks,
                    "securities-market-value",
                    "Securities market value",
                    ProviderLedgerReconciliationCheckStatusDto.Blocked,
                    "INTERNAL_SECURITIES_VALUE_MISSING",
                    ReconciliationBreakCategory.MissingLedgerCoverage,
                    ReconciliationBreakSeverity.High,
                    "internal-ledger",
                    "provider-sync",
                    null,
                    providerProjection.Positions.Sum(static position => position.MarketValue),
                    "Internal account snapshot does not include securities market value.");
            }
            else
            {
                AddAmountCheck(
                    runId,
                    lifecycle,
                    checks,
                    breaks,
                    "securities-market-value",
                    "Securities market value",
                    ReconciliationBreakCategory.AmountMismatch,
                    "SECURITIES_MARKET_VALUE_MISMATCH",
                    internalSnapshot.SecuritiesMarketValue.Value,
                    providerProjection.Positions.Sum(static position => position.MarketValue),
                    tolerance,
                    ReconciliationBreakSeverity.High,
                    "internal-ledger",
                    "provider-sync");
            }

            var internalEquity = internalSnapshot.CashBalance
                + (internalSnapshot.SecuritiesMarketValue ?? 0m)
                + (internalSnapshot.AccruedInterest ?? 0m)
                + (internalSnapshot.PendingSettlement ?? 0m);
            AddAmountCheck(
                runId,
                lifecycle,
                checks,
                breaks,
                "total-equity",
                "Total equity",
                ReconciliationBreakCategory.AmountMismatch,
                "TOTAL_EQUITY_MISMATCH",
                internalEquity,
                providerProjection.Balance.Equity,
                tolerance,
                ReconciliationBreakSeverity.High,
                "internal-ledger",
                "provider-sync");
        }
        else if (providerProjection is not null && providerProjection.Balance is null)
        {
            AddBreak(
                runId,
                lifecycle,
                checks,
                breaks,
                "provider-balance-available",
                "Provider balance is available",
                ProviderLedgerReconciliationCheckStatusDto.Blocked,
                "PROVIDER_BALANCE_MISSING",
                ReconciliationBreakCategory.MissingPortfolioCoverage,
                ReconciliationBreakSeverity.Critical,
                "provider-sync",
                "provider-sync",
                null,
                null,
                "Brokerage sync projection does not include a balance snapshot.");
        }

        if (providerProjection is not null)
        {
            await AddSecurityCoverageChecksAsync(
                    runId,
                    lifecycle,
                    providerProjection,
                    request.RequestedBy,
                    checks,
                    breaks,
                    securityMasterPassports,
                    createdAt,
                    ct)
                .ConfigureAwait(false);
        }

        var hasBlockedCheck = checks.Any(static check => check.Status == ProviderLedgerReconciliationCheckStatusDto.Blocked);
        var status = hasBlockedCheck
            ? ProviderLedgerReconciliationStatusDto.Blocked
            : breaks.Count > 0
                ? ProviderLedgerReconciliationStatusDto.Breaks
                : ProviderLedgerReconciliationStatusDto.Matched;

        if (status == ProviderLedgerReconciliationStatusDto.Blocked)
        {
            warnings.Add("Provider-ledger reconciliation is blocked until required source data is available.");
        }

        var detailPath = BuildRunDetailPath(accountId, runId);
        var summary = new ProviderLedgerReconciliationSummaryDto(
            ReconciliationRunId: runId,
            AccountId: accountId,
            CreatedAt: createdAt,
            Status: status,
            TotalChecks: checks.Count,
            MatchedChecks: checks.Count(static check => check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched),
            BreakCount: breaks.Count,
            SecurityIssueCount: breaks.Count(static item => item.Code.StartsWith("SM_", StringComparison.OrdinalIgnoreCase)),
            OpenBreakCount: breaks.Count(static item => item.SignOffState != ProviderLedgerReconciliationBreakSignOffStateDto.SignedOff),
            SignedOffBreakCount: breaks.Count(static item => item.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.SignedOff),
            OldestBreakAgeMinutes: breaks.Count == 0 ? 0 : breaks.Max(static item => item.AgeMinutes),
            AmountTolerance: tolerance,
            ProviderStaleAfterMinutes: staleAfterMinutes,
            ProviderId: providerProjection?.Link.ProviderId,
            ExternalAccountId: providerProjection?.Link.ExternalAccountId,
            ProviderSyncedAt: providerProjection?.SyncedAt,
            InternalAsOfDate: internalSnapshot?.AsOfDate,
            DetailPath: detailPath);

        var detail = new ProviderLedgerReconciliationDetailDto(
            summary,
            checks,
            breaks,
            warnings,
            evidenceLinks.Where(static link => !string.IsNullOrWhiteSpace(link)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            securityMasterPassports);

        await PersistAsync(accountId, detail, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Provider-ledger reconciliation {ReconciliationRunId} for account {AccountId} completed with {Status}",
            runId,
            accountId,
            status);
        return detail;
    }

    public async Task<ProviderLedgerReconciliationDetailDto?> GetLatestAsync(
        Guid accountId,
        CancellationToken ct = default)
    {
        var path = BuildLatestDetailPath(accountId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer
            .DeserializeAsync<ProviderLedgerReconciliationDetailDto>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
    }

    private async Task AddSecurityCoverageChecksAsync(
        Guid runId,
        BreakLifecycleContext lifecycle,
        FundAccountBrokerageSyncActivityDto providerProjection,
        string? requestedBy,
        List<ProviderLedgerReconciliationCheckDto> checks,
        List<ProviderLedgerReconciliationBreakDto> breaks,
        List<ProviderSecurityMasterPassportDto> securityMasterPassports,
        DateTimeOffset observedAt,
        CancellationToken ct)
    {
        var positions = providerProjection.Positions
            .Where(static position => !string.IsNullOrWhiteSpace(position.Symbol))
            .GroupBy(static position => position.Symbol.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        foreach (var position in positions)
        {
            ct.ThrowIfCancellationRequested();
            var symbol = position.Symbol.Trim().ToUpperInvariant();
            var checkId = $"security-master:{symbol}";

            if (position.Security is not null)
            {
                securityMasterPassports.Add(BuildSecurityMasterPassport(
                    providerProjection,
                    position,
                    position.Security,
                    validation: null,
                    status: MapPassportStatus(position.Security),
                    confidenceScore: position.Security.IsInferredMatch ? 85m : 100m,
                    resolutionSource: "provider-position",
                    reason: "Provider position already carries a resolved Security Master reference.",
                    observedAt: observedAt));
                AddMatched(
                    checks,
                    checkId,
                    $"Security Master identity for {symbol}",
                    ReconciliationBreakCategory.ClassificationGap,
                    "security-master",
                    "provider-sync",
                    null,
                    null,
                    "Provider position already carries a resolved Security Master reference.");
                continue;
            }

            var resolved = _securityReferenceLookup is null
                ? null
                : await _securityReferenceLookup
                    .GetByCanonicalAsync(
                        new SecurityReferenceLookupRequest(
                            IdentifierKind: SecurityIdentifierKind.Ticker.ToString(),
                            IdentifierValue: symbol,
                            Symbol: symbol,
                            Currency: position.Currency,
                            AssetClass: position.AssetClass,
                            Source: "provider-ledger-reconciliation"),
                        ct)
                    .ConfigureAwait(false);

            if (resolved is not null)
            {
                securityMasterPassports.Add(BuildSecurityMasterPassport(
                    providerProjection,
                    position,
                    resolved,
                    validation: null,
                    status: MapPassportStatus(resolved),
                    confidenceScore: resolved.IsInferredMatch ? 80m : 90m,
                    resolutionSource: "security-master-lookup",
                    reason: "Provider position resolved through the shared Security Master lookup.",
                    observedAt: observedAt));
                AddMatched(
                    checks,
                    checkId,
                    $"Security Master identity for {symbol}",
                    ReconciliationBreakCategory.ClassificationGap,
                    "security-master",
                    "provider-sync",
                    null,
                    null,
                    "Provider position resolved through the shared Security Master lookup.");
                continue;
            }

            var code = "SM_PROVIDER_POSITION_SECURITY_UNRESOLVED";
            var reason = $"Provider position '{symbol}' could not be resolved to a Security Master record.";
            var severity = ReconciliationBreakSeverity.High;
            if (_securityValidationGate is not null)
            {
                var validation = await _securityValidationGate
                    .ValidateSymbolAsync(
                        symbol,
                        SecurityValidationWorkflowDto.ReconciliationBreakIntake,
                        workflowReference: runId.ToString("N"),
                        actor: string.IsNullOrWhiteSpace(requestedBy) ? DefaultActor : requestedBy.Trim(),
                        persistSnapshot: false,
                        ct)
                    .ConfigureAwait(false);

                if (validation.IsResolved && !validation.IsBlocked)
                {
                    securityMasterPassports.Add(BuildSecurityMasterPassport(
                        providerProjection,
                        position,
                        security: null,
                        validation: validation,
                        status: ProviderSecurityMasterPassportStatusDto.Resolved,
                        confidenceScore: 80m,
                        resolutionSource: "security-validation-gate",
                        reason: "Security Master validation accepted the provider position.",
                        observedAt: observedAt));
                    AddMatched(
                        checks,
                        checkId,
                        $"Security Master identity for {symbol}",
                        ReconciliationBreakCategory.ClassificationGap,
                        "security-master",
                        "provider-sync",
                        null,
                        null,
                        "Security Master validation accepted the provider position.");
                    continue;
                }

                var issue = validation.Report.Issues.FirstOrDefault();
                if (issue is not null)
                {
                    code = issue.Code;
                    reason = $"Security Master validation {issue.Code}: {issue.Message}";
                    severity = MapSecurityValidationSeverity(issue.Severity);
                }

                securityMasterPassports.Add(BuildSecurityMasterPassport(
                    providerProjection,
                    position,
                    security: null,
                    validation: validation,
                    status: validation.IsBlocked || validation.Report.HasBlockingIssues
                        ? ProviderSecurityMasterPassportStatusDto.Blocked
                        : ProviderSecurityMasterPassportStatusDto.Unresolved,
                    confidenceScore: 0m,
                    resolutionSource: "security-validation-gate",
                    reason: reason,
                    observedAt: observedAt));
            }
            else
            {
                securityMasterPassports.Add(BuildSecurityMasterPassport(
                    providerProjection,
                    position,
                    security: null,
                    validation: null,
                    status: ProviderSecurityMasterPassportStatusDto.Unresolved,
                    confidenceScore: 0m,
                    resolutionSource: "unresolved",
                    reason: reason,
                    observedAt: observedAt));
            }

            AddBreak(
                runId,
                lifecycle,
                checks,
                breaks,
                checkId,
                $"Security Master identity for {symbol}",
                ProviderLedgerReconciliationCheckStatusDto.Break,
                code,
                ReconciliationBreakCategory.ClassificationGap,
                severity,
                "security-master",
                "provider-sync",
                null,
                null,
                reason,
                symbol,
                "/workstation/data/security-master");
        }
    }

    private static ProviderSecurityMasterPassportDto BuildSecurityMasterPassport(
        FundAccountBrokerageSyncActivityDto providerProjection,
        FundAccountBrokeragePositionDto position,
        WorkstationSecurityReference? security,
        SecurityValidationGateResultDto? validation,
        ProviderSecurityMasterPassportStatusDto status,
        decimal confidenceScore,
        string resolutionSource,
        string reason,
        DateTimeOffset observedAt)
    {
        var issues = validation?.Report.Issues ?? [];
        var identifierConflicts = issues
            .Where(static issue =>
                issue.Code.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
                || issue.Title.Contains("conflict", StringComparison.OrdinalIgnoreCase)
                || issue.AffectedFields.Any(static field => field.Contains("identifier", StringComparison.OrdinalIgnoreCase)))
            .Select(static issue => issue.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var issueCodes = issues
            .Select(static issue => issue.Code)
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var freshnessMinutes = Math.Max(0, (int)Math.Floor((observedAt - providerProjection.SyncedAt).TotalMinutes));

        return new ProviderSecurityMasterPassportDto(
            Symbol: position.Symbol.Trim().ToUpperInvariant(),
            ProviderId: providerProjection.Link.ProviderId,
            ExternalAccountId: providerProjection.Link.ExternalAccountId,
            ProviderSyncedAt: providerProjection.SyncedAt,
            ProviderIsStale: providerProjection.Status.IsStale,
            AssetClass: position.AssetClass,
            Currency: position.Currency,
            PositionId: position.PositionId,
            SecurityId: security?.SecurityId ?? validation?.SecurityId,
            SecurityDisplayName: security?.DisplayName,
            SecurityStatus: security?.Status,
            Status: status,
            ConfidenceScore: confidenceScore,
            ResolutionSource: resolutionSource,
            IdentifierConflicts: identifierConflicts,
            ValidationIssueCodes: issueCodes,
            OverrideHistory: [],
            ObservedAt: observedAt,
            FreshnessMinutes: freshnessMinutes,
            Reason: reason);
    }

    private async Task PersistAsync(
        Guid accountId,
        ProviderLedgerReconciliationDetailDto detail,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(detail, JsonOptions);
        await AtomicFileWriter.WriteAsync(BuildRunDetailPath(accountId, detail.Summary.ReconciliationRunId), json, ct)
            .ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(BuildLatestDetailPath(accountId), json, ct)
            .ConfigureAwait(false);
    }

    private string BuildRunDetailPath(Guid accountId, Guid runId)
        => Path.Combine(BuildAccountDirectory(accountId), "runs", $"{runId:N}.json");

    private string BuildLatestDetailPath(Guid accountId)
        => Path.Combine(BuildAccountDirectory(accountId), "latest.json");

    private string BuildAccountDirectory(Guid accountId)
        => Path.Combine(_options.RootDirectory, "reconciliation", accountId.ToString("N"));

    private static void AddAmountCheck(
        Guid runId,
        BreakLifecycleContext lifecycle,
        List<ProviderLedgerReconciliationCheckDto> checks,
        List<ProviderLedgerReconciliationBreakDto> breaks,
        string checkId,
        string label,
        ReconciliationBreakCategory category,
        string code,
        decimal expectedAmount,
        decimal actualAmount,
        decimal tolerance,
        ReconciliationBreakSeverity severity,
        string expectedSource,
        string actualSource)
    {
        var amountCheck = ReconciliationCaseWorkflowInterop.EvaluateProviderLedgerAmountCheck(
            label,
            expectedAmount,
            actualAmount,
            tolerance);
        if (amountCheck.IsMatched)
        {
            AddMatched(
                checks,
                checkId,
                label,
                category,
                expectedSource,
                actualSource,
                expectedAmount,
                actualAmount,
                amountCheck.Reason);
            return;
        }

        AddBreak(
            runId,
            lifecycle,
            checks,
            breaks,
            checkId,
            label,
            ProviderLedgerReconciliationCheckStatusDto.Break,
            code,
            category,
            severity,
            expectedSource,
            actualSource,
            expectedAmount,
            actualAmount,
            amountCheck.Reason);
    }

    private static void AddMatched(
        List<ProviderLedgerReconciliationCheckDto> checks,
        string checkId,
        string label,
        ReconciliationBreakCategory category,
        string expectedSource,
        string actualSource,
        decimal? expectedAmount,
        decimal? actualAmount,
        string reason)
    {
        checks.Add(new ProviderLedgerReconciliationCheckDto(
            checkId,
            label,
            ProviderLedgerReconciliationCheckStatusDto.Matched,
            category,
            expectedSource,
            actualSource,
            expectedAmount,
            actualAmount,
            actualAmount.HasValue && expectedAmount.HasValue ? actualAmount.Value - expectedAmount.Value : (decimal?)null,
            ReconciliationBreakSeverity.Info,
            reason));
    }

    private static void AddBreak(
        Guid runId,
        BreakLifecycleContext lifecycle,
        List<ProviderLedgerReconciliationCheckDto> checks,
        List<ProviderLedgerReconciliationBreakDto> breaks,
        string checkId,
        string label,
        ProviderLedgerReconciliationCheckStatusDto status,
        string code,
        ReconciliationBreakCategory category,
        ReconciliationBreakSeverity severity,
        string expectedSource,
        string actualSource,
        decimal? expectedAmount,
        decimal? actualAmount,
        string reason,
        string? symbol = null,
        string? evidenceLink = null)
    {
        decimal? variance = actualAmount.HasValue && expectedAmount.HasValue
            ? actualAmount.Value - expectedAmount.Value
            : (decimal?)null;
        checks.Add(new ProviderLedgerReconciliationCheckDto(
            checkId,
            label,
            status,
            category,
            expectedSource,
            actualSource,
            expectedAmount,
            actualAmount,
            variance,
            severity,
            reason));
        var breakKey = BuildBreakKey(checkId, code, symbol);
        lifecycle.PreviousBreaksByKey.TryGetValue(breakKey, out var previousBreak);
        var firstObservedAt = previousBreak?.FirstObservedAt ?? lifecycle.CreatedAt;
        var isSignedOff = lifecycle.SignedOffBreakKeys.Contains(breakKey)
            || previousBreak?.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.SignedOff;
        var owner = previousBreak?.Owner ?? lifecycle.DefaultOwner;
        var signedOffBy = isSignedOff
            ? lifecycle.SignedOffBy ?? previousBreak?.SignedOffBy
            : null;
        var signedOffAt = isSignedOff
            ? (lifecycle.SignedOffBreakKeys.Contains(breakKey) ? lifecycle.CreatedAt : previousBreak?.SignedOffAt)
            : null;
        var signOffState = isSignedOff
            ? ProviderLedgerReconciliationBreakSignOffStateDto.SignedOff
            : string.IsNullOrWhiteSpace(owner)
                ? ProviderLedgerReconciliationBreakSignOffStateDto.Open
                : ProviderLedgerReconciliationBreakSignOffStateDto.Assigned;
        var ageMinutes = Math.Max(0, (int)Math.Floor((lifecycle.CreatedAt - firstObservedAt).TotalMinutes));

        breaks.Add(new ProviderLedgerReconciliationBreakDto(
            $"{runId:N}:{NormalizeBreakIdPart(checkId)}",
            checkId,
            code,
            category,
            severity,
            expectedSource,
            actualSource,
            expectedAmount,
            actualAmount,
            variance,
            reason,
            symbol,
            evidenceLink,
            breakKey,
            owner,
            lifecycle.AmountTolerance,
            firstObservedAt,
            lifecycle.CreatedAt,
            ageMinutes,
            signOffState,
            signedOffBy,
            signedOffAt));
    }

    private static IReadOnlyDictionary<string, ProviderLedgerReconciliationBreakDto> BuildPreviousBreakMap(
        ProviderLedgerReconciliationDetailDto? previousLatest)
    {
        if (previousLatest is null)
        {
            return new Dictionary<string, ProviderLedgerReconciliationBreakDto>(StringComparer.OrdinalIgnoreCase);
        }

        return previousLatest.Breaks
            .Select(item => new
            {
                Key = string.IsNullOrWhiteSpace(item.BreakKey)
                    ? BuildBreakKey(item.CheckId, item.Code, item.Symbol)
                    : item.BreakKey,
                Break = item
            })
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.Break.LastObservedAt ?? previousLatest.Summary.CreatedAt).First().Break,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildBreakKey(string checkId, string code, string? symbol)
        => string.Join(
            ":",
            "provider-ledger",
            NormalizeBreakIdPart(checkId),
            NormalizeBreakIdPart(code),
            NormalizeBreakIdPart(string.IsNullOrWhiteSpace(symbol) ? "account" : symbol));

    private static string? NormalizeOwner(string? owner)
        => string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();

    private sealed record BreakLifecycleContext(
        DateTimeOffset CreatedAt,
        decimal AmountTolerance,
        string? DefaultOwner,
        IReadOnlySet<string> SignedOffBreakKeys,
        string? SignedOffBy,
        IReadOnlyDictionary<string, ProviderLedgerReconciliationBreakDto> PreviousBreaksByKey);

    private static string NormalizeBreakIdPart(string value)
        => string.Join("-", value.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Replace(':', '-')
            .ToLowerInvariant();

    private static ReconciliationBreakSeverity MapSecurityValidationSeverity(SecurityValidationSeverityDto severity)
        => severity switch
        {
            SecurityValidationSeverityDto.Critical => ReconciliationBreakSeverity.Critical,
            SecurityValidationSeverityDto.Error => ReconciliationBreakSeverity.High,
            SecurityValidationSeverityDto.Warning => ReconciliationBreakSeverity.Medium,
            SecurityValidationSeverityDto.Info => ReconciliationBreakSeverity.Info,
            _ => ReconciliationBreakSeverity.Medium
        };

    private static ProviderSecurityMasterPassportStatusDto MapPassportStatus(WorkstationSecurityReference security)
        => security.CoverageStatus == WorkstationSecurityCoverageStatus.Resolved && !security.IsInferredMatch
            ? ProviderSecurityMasterPassportStatusDto.Resolved
            : ProviderSecurityMasterPassportStatusDto.Inferred;
}
