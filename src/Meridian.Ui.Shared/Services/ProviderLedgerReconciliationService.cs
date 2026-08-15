using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;
using Meridian.ProviderSdk;
using Meridian.Storage.Archival;
using Meridian.Storage.SecurityMaster;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Compares the latest provider account projection with Meridian's internal account ledger snapshot.
/// </summary>
public sealed partial class ProviderLedgerReconciliationService
{
    private const string DefaultActor = "provider-ledger-reconciliation";
    private const string OperationKind = "provider-ledger.reconciliation";
    private const string RunIntentSchemaVersion = "meridian.provider-ledger-reconciliation.intent.v1";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks = new(StringComparer.Ordinal);
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
    private readonly ICapabilityRouter? _capabilityRouter;
    private readonly IReconciliationBreakQueueRepository? _breakQueueRepository;
    private readonly IOperatorOverridesStore? _operatorOverridesStore;
    private readonly ISecurityMasterConflictService? _securityMasterConflictService;
    private readonly ILedgerBookService? _ledgerBookService;
    private readonly IFundProfileTenancyRegistry? _fundProfileTenancyRegistry;
    private readonly ILogger<ProviderLedgerReconciliationService> _logger;

    public ProviderLedgerReconciliationService(
        BrokeragePortfolioSyncService brokerageSync,
        IFundAccountService fundAccountService,
        BrokeragePortfolioSyncOptions options,
        ILogger<ProviderLedgerReconciliationService> logger,
        ISecurityReferenceLookup? securityReferenceLookup = null,
        ISecurityValidationGateService? securityValidationGate = null,
        ICapabilityRouter? capabilityRouter = null,
        IReconciliationBreakQueueRepository? breakQueueRepository = null,
        IOperatorOverridesStore? operatorOverridesStore = null,
        ISecurityMasterConflictService? securityMasterConflictService = null,
        ILedgerBookService? ledgerBookService = null,
        IFundProfileTenancyRegistry? fundProfileTenancyRegistry = null)
    {
        _brokerageSync = brokerageSync ?? throw new ArgumentNullException(nameof(brokerageSync));
        _fundAccountService = fundAccountService ?? throw new ArgumentNullException(nameof(fundAccountService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _securityReferenceLookup = securityReferenceLookup;
        _securityValidationGate = securityValidationGate;
        _capabilityRouter = capabilityRouter;
        _breakQueueRepository = breakQueueRepository;
        _operatorOverridesStore = operatorOverridesStore;
        _securityMasterConflictService = securityMasterConflictService;
        _ledgerBookService = ledgerBookService;
        _fundProfileTenancyRegistry = fundProfileTenancyRegistry;
    }

    public async Task<ProviderLedgerReconciliationDetailDto> RunAsync(
        Guid accountId,
        ProviderLedgerReconciliationRequestDto? request = null,
        CancellationToken ct = default)
        => await RunInternalAsync(
                accountId,
                accessScope: null,
                verifiedLedgerBook: null,
                request,
                ct)
            .ConfigureAwait(false);

    public async Task<ProviderLedgerReconciliationDetailDto> RunAsync(
        Guid accountId,
        ReconciliationBreakQueueScope accessScope,
        ProviderLedgerReconciliationRequestDto? request = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accessScope);
        request ??= new ProviderLedgerReconciliationRequestDto();
        var operationId = NormalizeOperationId(request.OperationId);
        request = request with { OperationId = operationId };
        var authority = await VerifyAuthoritativeScopeAsync(accountId, accessScope, ct)
            .ConfigureAwait(false);
        if (!authority.IsVerified)
        {
            return BuildAuthorityFailureDetail(
                accountId,
                operationId,
                ComputeRequestHash(accountId, accessScope, request),
                authority.ErrorCode!,
                authority.Error!);
        }

        return await RunInternalAsync(accountId, accessScope, authority.LedgerBook, request, ct)
            .ConfigureAwait(false);
    }

    private async Task<ProviderLedgerReconciliationDetailDto> RunInternalAsync(
        Guid accountId,
        ReconciliationBreakQueueScope? accessScope,
        LedgerBookDto? verifiedLedgerBook,
        ProviderLedgerReconciliationRequestDto? request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        request ??= new ProviderLedgerReconciliationRequestDto();
        if (request.SignedOffBreakKeys is { Count: > 0 } || !string.IsNullOrWhiteSpace(request.SignedOffBy))
        {
            throw new ArgumentException(
                "Provider-ledger reconciliation is comparison-only. Resolve, waive, supersede, and sign off through the governed reconciliation casework service.",
                nameof(request));
        }

        var operationId = NormalizeOperationId(request.OperationId);
        var requestHash = ComputeRequestHash(accountId, accessScope, request);
        var operationLockKey = $"{accountId:N}:{Meridian.Contracts.Integrity.Sha256Digest.ComputeUtf8(operationId)}";
        var operationGate = OperationLocks.GetOrAdd(operationLockKey, static _ => new SemaphoreSlim(1, 1));
        await operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existingIntent = await ReadRunIntentAsync(accountId, operationId, ct).ConfigureAwait(false);
            if (existingIntent is not null &&
                (!string.Equals(existingIntent.OperationId, operationId, StringComparison.Ordinal) ||
                 !string.Equals(existingIntent.RequestHashSha256, requestHash, StringComparison.OrdinalIgnoreCase)))
            {
                return BuildIdempotencyConflictDetail(accountId, operationId, requestHash, existingIntent);
            }

            if (existingIntent is not null)
            {
                var retained = await GetRunDetailAsync(accountId, existingIntent.RunId, ct).ConfigureAwait(false);
                if (retained?.Outcome is { State: not OperationTerminalState.Failed })
                {
                    return retained;
                }
            }

            var startedAt = DateTimeOffset.UtcNow;
            var activeIntent = new ProviderLedgerReconciliationRunIntent(
                SchemaVersion: RunIntentSchemaVersion,
                OperationId: operationId,
                RunId: existingIntent?.RunId ?? Guid.NewGuid(),
                AccountId: accountId,
                RequestHashSha256: requestHash,
                InputHashSha256: existingIntent?.InputHashSha256,
                AttemptNumber: (existingIntent?.AttemptNumber ?? 0) + 1,
                StartedAtUtc: existingIntent?.StartedAtUtc ?? startedAt,
                UpdatedAtUtc: startedAt,
                State: "Running",
                TerminalState: null,
                FailureReason: null);
            await PersistRunIntentAsync(activeIntent, ct).ConfigureAwait(false);

            try
            {
                return await RunCoreAsync(
                        accountId,
                        accessScope,
                        verifiedLedgerBook,
                        request,
                        activeIntent,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                var cancelled = BuildUnexpectedFailureDetail(
                    accountId,
                    activeIntent,
                    activeIntent.InputHashSha256 ?? requestHash,
                    "PROVIDER_RECONCILIATION_CANCELLED",
                    "Provider-ledger reconciliation was cancelled before all required postconditions could be verified.",
                    exceptionType: typeof(OperationCanceledException).FullName);
                await TryPersistTerminalFailureAsync(activeIntent, cancelled, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Provider-ledger reconciliation {OperationId} for account {AccountId} failed before terminal persistence",
                    operationId,
                    accountId);
                var failed = BuildUnexpectedFailureDetail(
                    accountId,
                    activeIntent,
                    activeIntent.InputHashSha256 ?? requestHash,
                    "PROVIDER_RECONCILIATION_FAILED",
                    "Provider-ledger reconciliation failed before all required postconditions could be verified.",
                    ex.GetType().FullName);
                await TryPersistTerminalFailureAsync(activeIntent, failed, CancellationToken.None).ConfigureAwait(false);
                return failed;
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task<ProviderLedgerReconciliationDetailDto> RunCoreAsync(
        Guid accountId,
        ReconciliationBreakQueueScope? accessScope,
        LedgerBookDto? verifiedLedgerBook,
        ProviderLedgerReconciliationRequestDto request,
        ProviderLedgerReconciliationRunIntent activeIntent,
        CancellationToken ct)
    {
        var tolerance = Math.Abs(request.AmountTolerance);
        var staleAfterMinutes = Math.Max(1, request.ProviderStaleAfterMinutes);
        var runId = activeIntent.RunId;
        var createdAt = activeIntent.StartedAtUtc;
        var previousLatest = await GetLatestAsync(accountId, ct).ConfigureAwait(false);
        var lifecycle = new BreakLifecycleContext(
            CreatedAt: createdAt,
            AmountTolerance: tolerance,
            DefaultOwner: NormalizeOwner(request.DefaultBreakOwner) ?? "fund-accounting",
            SignedOffBreakKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SignedOffBy: null,
            PreviousBreaksByKey: BuildPreviousBreakMap(previousLatest));

        var providerProjection = await _brokerageSync.GetActivityAsync(accountId, ct).ConfigureAwait(false);
        var internalSnapshot = await _fundAccountService.GetLatestBalanceSnapshotAsync(accountId, ct).ConfigureAwait(false);
        ProviderLedgerScope? ledgerScope = null;
        string? ledgerScopeError = null;
        try
        {
            ledgerScope = await ResolvePrimaryLedgerScopeAsync(
                    accountId,
                    internalSnapshot?.AsOfDate,
                    verifiedLedgerBook,
                    ct)
                .ConfigureAwait(false);
            if (ledgerScope.Period is null || !ledgerScope.AsOfDate.HasValue)
            {
                ledgerScopeError = "Provider-ledger reconciliation requires an exact accounting period and as-of date before close/report casework can be retained.";
            }
        }
        catch (InvalidOperationException ex)
        {
            ledgerScopeError = ex.Message;
        }

        var checks = new List<ProviderLedgerReconciliationCheckDto>();
        var breaks = new List<ProviderLedgerReconciliationBreakDto>();
        var securityMasterPassports = new List<ProviderSecurityMasterPassportDto>();
        var warnings = new List<string>();
        var evidenceLinks = new List<string>();

        if (ledgerScopeError is not null)
        {
            AddBreak(
                runId,
                lifecycle,
                checks,
                breaks,
                "accounting-scope-resolved",
                "Accounting book and period scope is resolved",
                ProviderLedgerReconciliationCheckStatusDto.Blocked,
                "ACCOUNTING_SCOPE_UNRESOLVED",
                ReconciliationBreakCategory.MissingLedgerCoverage,
                ReconciliationBreakSeverity.Critical,
                "ledger-book-service",
                "ledger-book-service",
                null,
                null,
                ledgerScopeError);
        }
        else
        {
            AddMatched(
                checks,
                "accounting-scope-resolved",
                "Accounting book and period scope is resolved",
                ReconciliationBreakCategory.MissingLedgerCoverage,
                "ledger-book-service",
                "ledger-book-service",
                null,
                null,
                $"Primary ledger book '{ledgerScope!.Book.LedgerBookId:D}' and accounting period '{ledgerScope.Period!.PeriodId:D}' cover as-of date '{ledgerScope.AsOfDate:yyyy-MM-dd}'.");
        }

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

            await AddProviderCapabilityChecksAsync(
                    runId,
                    lifecycle,
                    providerProjection,
                    checks,
                    breaks,
                    ct)
                .ConfigureAwait(false);

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
                    staleAfterMinutes,
                    ct)
                .ConfigureAwait(false);
        }

        var detailPath = BuildRunDetailPath(accountId, runId);
        IReadOnlyList<CustodianPositionLineDto> custodianPositions = internalSnapshot is null
            ? []
            : await _fundAccountService
                .GetCustodianPositionsAsync(accountId, internalSnapshot.AsOfDate, ct)
                .ConfigureAwait(false);
        IReadOnlyList<BankStatementLineDto> bankStatementLines = internalSnapshot is null
            ? []
            : await _fundAccountService
                .GetBankStatementLinesAsync(accountId, internalSnapshot.AsOfDate, internalSnapshot.AsOfDate, ct)
                .ConfigureAwait(false);
        var shadowBookComparison = BuildShadowBookComparison(
            accountId,
            createdAt,
            tolerance,
            providerProjection,
            internalSnapshot,
            custodianPositions,
            bankStatementLines);
        AddShadowBookBreaks(runId, lifecycle, checks, breaks, shadowBookComparison);
        var corporateActionReadiness = BuildCorporateActionReadiness(
            providerProjection,
            checks,
            securityMasterPassports,
            evidenceLinks);

        var inputHash = ComputeOperationInputHash(
            accountId,
            accessScope,
            request,
            providerProjection,
            internalSnapshot,
            ledgerScope,
            ledgerScopeError,
            custodianPositions,
            bankStatementLines,
            checks,
            securityMasterPassports);
        if (activeIntent.InputHashSha256 is { Length: > 0 } retainedInputHash &&
            !string.Equals(retainedInputHash, inputHash, StringComparison.OrdinalIgnoreCase))
        {
            var conflict = BuildInputConflictDetail(accountId, activeIntent, inputHash, retainedInputHash);
            await PersistAsync(accountId, conflict, ct).ConfigureAwait(false);
            await PersistRunIntentAsync(
                    activeIntent with
                    {
                        InputHashSha256 = retainedInputHash,
                        UpdatedAtUtc = conflict.Outcome!.CompletedAtUtc,
                        State = "Blocked",
                        TerminalState = OperationTerminalState.Blocked,
                        FailureReason = conflict.Outcome.Issues[0].Message
                    },
                    ct)
                .ConfigureAwait(false);
            return conflict;
        }

        activeIntent = activeIntent with { InputHashSha256 = inputHash, UpdatedAtUtc = DateTimeOffset.UtcNow };
        await PersistRunIntentAsync(activeIntent, ct).ConfigureAwait(false);

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

        var provisionalDetail = new ProviderLedgerReconciliationDetailDto(
            summary,
            checks,
            breaks,
            warnings,
            evidenceLinks.Where(static link => !string.IsNullOrWhiteSpace(link)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            securityMasterPassports,
            shadowBookComparison,
            corporateActionReadiness);

        ProviderCaseworkPersistenceResult casework;
        try
        {
            casework = await SeedBreakQueueCasesAsync(
                    provisionalDetail,
                    accessScope,
                    request,
                    ledgerScope,
                    ledgerScopeError,
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Provider-ledger reconciliation {ReconciliationRunId} failed while retaining reconciliation casework",
                runId);
            var failed = provisionalDetail with
            {
                Outcome = BuildPersistenceFailureOutcome(
                    activeIntent,
                    inputHash,
                    summary,
                    "RECONCILIATION_CASEWORK_PERSISTENCE_FAILED",
                    "The reconciliation was evaluated, but one or more required break cases could not be durably retained.",
                    ex.GetType().FullName,
                    caseworkRetained: false,
                    runRecordRetained: true)
            };
            var retained = await TryPersistTerminalFailureAsync(activeIntent, failed, CancellationToken.None)
                .ConfigureAwait(false);
            return retained
                ? failed
                : failed with
                {
                    Outcome = BuildPersistenceFailureOutcome(
                        activeIntent,
                        inputHash,
                        summary,
                        "RECONCILIATION_TERMINAL_PERSISTENCE_FAILED",
                        "Reconciliation casework persistence failed and the terminal run detail could not be durably retained. The pre-casework run intent remains the recovery anchor.",
                        ex.GetType().FullName,
                        caseworkRetained: false,
                        runRecordRetained: false)
                };
        }

        var detail = provisionalDetail with
        {
            Outcome = BuildTerminalOutcome(activeIntent, inputHash, summary, warnings, casework)
        };
        try
        {
            await PersistAsync(accountId, detail, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Provider-ledger reconciliation {ReconciliationRunId} retained casework but failed to persist terminal run detail",
                runId);
            var failed = provisionalDetail with
            {
                Outcome = BuildPersistenceFailureOutcome(
                    activeIntent,
                    inputHash,
                    summary,
                    "RECONCILIATION_TERMINAL_PERSISTENCE_FAILED",
                    "Reconciliation casework was retained, but the terminal run detail could not be durably persisted. The pre-casework run intent remains the recovery anchor.",
                    ex.GetType().FullName,
                    caseworkRetained: casework.IsSatisfied,
                    runRecordRetained: false)
            };
            await TryPersistRunIntentAsync(
                    activeIntent with
                    {
                        UpdatedAtUtc = failed.Outcome.CompletedAtUtc,
                        State = "Failed",
                        TerminalState = OperationTerminalState.Failed,
                        FailureReason = failed.Outcome.Issues[0].Message
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            return failed;
        }

        await TryPersistRunIntentAsync(
                activeIntent with
                {
                    UpdatedAtUtc = detail.Outcome!.CompletedAtUtc,
                    State = detail.Outcome.State.ToString(),
                    TerminalState = detail.Outcome.State,
                    FailureReason = detail.Outcome.Issues.FirstOrDefault(static issue => issue.Severity == OperationIssueSeverity.Error)?.Message
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "Provider-ledger reconciliation {ReconciliationRunId} for account {AccountId} completed with {Status} and verified state {TerminalState}",
            runId,
            accountId,
            status,
            detail.Outcome.State);
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

    public async Task<ProviderLedgerReconciliationDetailDto?> GetLatestAsync(
        Guid accountId,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accessScope);
        var authority = await VerifyAuthoritativeScopeAsync(accountId, accessScope, ct)
            .ConfigureAwait(false);
        return authority.IsVerified
            ? await GetLatestAsync(accountId, ct).ConfigureAwait(false)
            : null;
    }

    private static ProviderShadowBookComparisonDto BuildShadowBookComparison(
        Guid accountId,
        DateTimeOffset createdAt,
        decimal tolerance,
        FundAccountBrokerageSyncActivityDto? providerProjection,
        AccountBalanceSnapshotDto? internalSnapshot,
        IReadOnlyList<CustodianPositionLineDto> custodianPositions,
        IReadOnlyList<BankStatementLineDto> bankStatementLines)
    {
        var currency = internalSnapshot?.Currency
            ?? providerProjection?.Balance?.Currency
            ?? providerProjection?.Positions.FirstOrDefault()?.Currency
            ?? "USD";
        var providerMarketValue = providerProjection?.Positions.Sum(static position => position.MarketValue);
        var providerUnrealizedPnl = providerProjection?.Positions.Sum(static position => position.UnrealizedPnl);
        decimal? providerRealizedPnl = providerProjection is null
            ? null
            : providerProjection.Fills.Any(static fill => fill.RealizedPnl.HasValue)
                ? providerProjection.Fills.Sum(static fill => fill.RealizedPnl ?? 0m)
                : null;
        decimal? providerIncomeCashFlow = providerProjection is null
            ? (decimal?)null
            : providerProjection.CashTransactions
                .Where(static transaction => IsIncomeTransaction(transaction.TransactionType))
                .Sum(static transaction => transaction.Amount);
        decimal? internalEquity = internalSnapshot is null
            ? (decimal?)null
            : internalSnapshot.CashBalance
                + (internalSnapshot.SecuritiesMarketValue ?? 0m)
                + (internalSnapshot.AccruedInterest ?? 0m)
                + (internalSnapshot.PendingSettlement ?? 0m);

        var aggregateLines = new[]
        {
            BuildShadowBookLine(
                "account-cash",
                "Account cash",
                "internal-ledger",
                "provider-sync",
                internalSnapshot?.CashBalance,
                providerProjection?.Balance?.Cash,
                tolerance,
                "Cash is compared between Meridian's internal shadow book and the provider balance snapshot."),
            BuildShadowBookLine(
                "positions-market-value",
                "Position market value",
                "internal-ledger",
                "provider-sync",
                internalSnapshot?.SecuritiesMarketValue,
                providerMarketValue,
                tolerance,
                "Securities market value is compared against provider position market value."),
            BuildShadowBookLine(
                "total-equity",
                "Total equity",
                "internal-ledger",
                "provider-sync",
                internalEquity,
                providerProjection?.Balance?.Equity,
                tolerance,
                "Total equity compares cash, securities, accrued interest, and pending settlement against provider equity."),
            BuildShadowBookLine(
                "income-accrual",
                "Income and accrual",
                "internal-ledger",
                "provider-sync",
                internalSnapshot?.AccruedInterest,
                providerIncomeCashFlow,
                tolerance,
                "Provider income cash movements are compared with internal accrued interest until a full income roll-forward is available."),
            BuildShadowBookLine(
                "pending-settlement",
                "Pending settlement",
                "internal-ledger",
                "provider-sync",
                internalSnapshot?.PendingSettlement,
                null,
                tolerance,
                "Provider sync does not yet retain pending-settlement exposure for account-level shadow-book comparison."),
            BuildShadowBookLine(
                "unrealized-pnl",
                "Unrealized P&L",
                "internal-ledger",
                "provider-sync",
                internalSnapshot?.UnrealizedPnl,
                providerUnrealizedPnl,
                tolerance,
                "Unrealized P&L is compared between Meridian's internal account snapshot and provider position marks."),
            BuildShadowBookLine(
                "realized-pnl",
                "Realized P&L",
                "internal-ledger",
                "provider-sync",
                internalSnapshot?.RealizedPnl,
                providerRealizedPnl,
                tolerance,
                "Realized P&L is compared only when provider fills retain explicit realized P&L.")
        };
        var lines = aggregateLines
            .Concat(BuildBankStatementComparisonLines(providerProjection, internalSnapshot, bankStatementLines, tolerance))
            .Concat(BuildCustodianPositionComparisonLines(providerProjection, custodianPositions, tolerance))
            .ToArray();

        return new ProviderShadowBookComparisonDto(
            accountId,
            createdAt,
            currency,
            lines.Count(static line => line.Status is not ProviderLedgerReconciliationCheckStatusDto.Blocked),
            lines.Count(static line => line.Status is ProviderLedgerReconciliationCheckStatusDto.Matched),
            lines.Count(static line => line.Status is ProviderLedgerReconciliationCheckStatusDto.Break),
            lines.Count(static line => line.Status is ProviderLedgerReconciliationCheckStatusDto.Blocked),
            lines);
    }

    private static IReadOnlyList<ProviderShadowBookComparisonLineDto> BuildCustodianPositionComparisonLines(
        FundAccountBrokerageSyncActivityDto? providerProjection,
        IReadOnlyList<CustodianPositionLineDto> custodianPositions,
        decimal tolerance)
    {
        if (custodianPositions.Count == 0)
        {
            return [];
        }

        var providerBySymbol = providerProjection?.Positions
            .Where(static position => !string.IsNullOrWhiteSpace(position.Symbol))
            .GroupBy(static position => NormalizePositionKey(position.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => new PositionComparisonAmount(
                    group.First().Symbol.Trim(),
                    group.Sum(static position => position.Quantity),
                    group.Sum(static position => position.MarketValue),
                    group.Sum(static position => Math.Abs(position.Quantity) * position.AverageEntryPrice)),
                StringComparer.OrdinalIgnoreCase) ?? [];
        var custodianByIdentifier = custodianPositions
            .Where(static position => !string.IsNullOrWhiteSpace(position.Identifier))
            .GroupBy(static position => NormalizePositionKey(position.Identifier), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => new PositionComparisonAmount(
                    group.First().Identifier.Trim(),
                    group.Sum(static position => position.IsShort ? -Math.Abs(position.Quantity) : position.Quantity),
                    group.Sum(static position => position.MarketValue),
                    group.All(static position => position.CostBasis.HasValue)
                        ? group.Sum(static position => position.CostBasis!.Value)
                        : null),
                StringComparer.OrdinalIgnoreCase);
        var keys = providerBySymbol.Keys
            .Concat(custodianByIdentifier.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var lines = new List<ProviderShadowBookComparisonLineDto>(keys.Length * 2);
        foreach (var key in keys)
        {
            providerBySymbol.TryGetValue(key, out var provider);
            custodianByIdentifier.TryGetValue(key, out var custodian);
            var display = provider?.DisplayName ?? custodian?.DisplayName ?? key;
            lines.Add(BuildShadowBookLine(
                $"position-quantity:{display}",
                $"Position {display} quantity",
                "custodian-statement",
                "provider-sync",
                custodian?.Quantity,
                provider?.Quantity,
                tolerance,
                "Position quantity is compared between retained custodian statement lines and provider positions."));
            lines.Add(BuildShadowBookLine(
                $"position-market-value:{display}",
                $"Position {display} market value",
                "custodian-statement",
                "provider-sync",
                custodian?.MarketValue,
                provider?.MarketValue,
                tolerance,
                "Position market value is compared between retained custodian statement lines and provider positions."));
            lines.Add(BuildShadowBookLine(
                $"position-cost-basis:{display}",
                $"Position {display} cost basis",
                "custodian-statement",
                "provider-sync",
                custodian?.CostBasis,
                provider?.CostBasis,
                tolerance,
                "Position cost basis is compared between retained custodian statement lines and provider average-entry-price evidence."));
        }

        return lines;
    }

    private static IReadOnlyList<ProviderShadowBookComparisonLineDto> BuildBankStatementComparisonLines(
        FundAccountBrokerageSyncActivityDto? providerProjection,
        AccountBalanceSnapshotDto? internalSnapshot,
        IReadOnlyList<BankStatementLineDto> bankStatementLines,
        decimal tolerance)
    {
        if (bankStatementLines.Count == 0)
        {
            return [];
        }

        var closingBalance = bankStatementLines
            .Where(static line => line.ClosingBalance.HasValue)
            .OrderBy(static line => line.ValueDate)
            .ThenBy(static line => line.TransactionDate)
            .LastOrDefault()
            ?.ClosingBalance;
        var bankIncomeCashFlow = bankStatementLines
            .Where(static line => IsIncomeTransaction(line.TransactionType))
            .Sum(static line => line.Amount);
        decimal? providerIncomeCashFlow = providerProjection is null
            ? null
            : providerProjection.CashTransactions
                .Where(static transaction => IsIncomeTransaction(transaction.TransactionType))
                .Sum(static transaction => transaction.Amount);

        return
        [
            BuildShadowBookLine(
                "bank-statement-cash",
                "Bank statement cash",
                "internal-ledger",
                "bank-statement",
                internalSnapshot?.CashBalance,
                closingBalance,
                tolerance,
                "Cash is compared between Meridian's internal shadow book and retained bank statement closing balance."),
            BuildShadowBookLine(
                "bank-statement-income-cash-flow",
                "Bank statement income cash flow",
                "bank-statement",
                "provider-activity",
                bankIncomeCashFlow,
                providerIncomeCashFlow,
                tolerance,
                "Income cash flow is compared between retained bank statement lines and provider cash activity.")
        ];
    }

    private static void AddShadowBookBreaks(
        Guid runId,
        BreakLifecycleContext lifecycle,
        List<ProviderLedgerReconciliationCheckDto> checks,
        List<ProviderLedgerReconciliationBreakDto> breaks,
        ProviderShadowBookComparisonDto shadowBookComparison)
    {
        foreach (var line in shadowBookComparison.Lines)
        {
            if (line.Status != ProviderLedgerReconciliationCheckStatusDto.Break ||
                IsPrimaryAmountCheckDimension(line.Dimension))
            {
                continue;
            }

            AddBreak(
                runId,
                lifecycle,
                checks,
                breaks,
                $"shadow-book:{line.Dimension}",
                line.Label,
                ProviderLedgerReconciliationCheckStatusDto.Break,
                BuildShadowBookBreakCode(line.Dimension),
                MapShadowBookBreakCategory(line.Dimension),
                MapShadowBookBreakSeverity(line.Dimension),
                line.InternalSource,
                line.ProviderSource,
                line.InternalAmount,
                line.ProviderAmount,
                line.Reason,
                TryGetShadowBookSymbol(line.Dimension),
                "/workstation/accounting/reconciliation");
        }
    }

    private static bool IsPrimaryAmountCheckDimension(string dimension) =>
        dimension.Equals("account-cash", StringComparison.OrdinalIgnoreCase) ||
        dimension.Equals("positions-market-value", StringComparison.OrdinalIgnoreCase) ||
        dimension.Equals("total-equity", StringComparison.OrdinalIgnoreCase);

    private static string BuildShadowBookBreakCode(string dimension)
        => $"SHADOW_BOOK_{NormalizeBreakIdPart(dimension).Replace('-', '_').ToUpperInvariant()}_MISMATCH";

    private static ReconciliationBreakCategory MapShadowBookBreakCategory(string dimension)
    {
        if (dimension.Contains("cash", StringComparison.OrdinalIgnoreCase))
        {
            return ReconciliationBreakCategory.CashMismatch;
        }

        if (dimension.StartsWith("bank-statement-", StringComparison.OrdinalIgnoreCase) ||
            dimension.StartsWith("position-", StringComparison.OrdinalIgnoreCase))
        {
            return ReconciliationBreakCategory.ExternalStatementMismatch;
        }

        return ReconciliationBreakCategory.AmountMismatch;
    }

    private static ReconciliationBreakSeverity MapShadowBookBreakSeverity(string dimension)
    {
        if (dimension.Contains("realized-pnl", StringComparison.OrdinalIgnoreCase) ||
            dimension.Contains("unrealized-pnl", StringComparison.OrdinalIgnoreCase))
        {
            return ReconciliationBreakSeverity.High;
        }

        return ReconciliationBreakSeverity.Medium;
    }

    private static string? TryGetShadowBookSymbol(string dimension)
    {
        var separatorIndex = dimension.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex < 0 || separatorIndex == dimension.Length - 1)
        {
            return null;
        }

        return dimension[(separatorIndex + 1)..].Trim().ToUpperInvariant();
    }

    private static ProviderShadowBookComparisonLineDto BuildShadowBookLine(
        string dimension,
        string label,
        string internalSource,
        string providerSource,
        decimal? internalAmount,
        decimal? providerAmount,
        decimal tolerance,
        string reason)
    {
        if (!internalAmount.HasValue || !providerAmount.HasValue)
        {
            var missing = !internalAmount.HasValue && !providerAmount.HasValue
                ? "internal and provider amounts are unavailable"
                : !internalAmount.HasValue
                    ? "internal amount is unavailable"
                    : "provider amount is unavailable";
            return new ProviderShadowBookComparisonLineDto(
                dimension,
                label,
                internalSource,
                providerSource,
                internalAmount,
                providerAmount,
                null,
                ProviderLedgerReconciliationCheckStatusDto.Blocked,
                $"{reason} Shadow-book comparison is unavailable because {missing}.");
        }

        var variance = providerAmount.Value - internalAmount.Value;
        var status = Math.Abs(variance) <= tolerance
            ? ProviderLedgerReconciliationCheckStatusDto.Matched
            : ProviderLedgerReconciliationCheckStatusDto.Break;
        return new ProviderShadowBookComparisonLineDto(
            dimension,
            label,
            internalSource,
            providerSource,
            internalAmount,
            providerAmount,
            variance,
            status,
            status == ProviderLedgerReconciliationCheckStatusDto.Matched
                ? $"{reason} Variance {variance:0.######} is within tolerance {tolerance:0.######}."
                : $"{reason} Variance {variance:0.######} exceeds tolerance {tolerance:0.######}.");
    }

    private static string NormalizePositionKey(string value)
        => value.Trim().ToUpperInvariant();

    private sealed record PositionComparisonAmount(
        string DisplayName,
        decimal Quantity,
        decimal MarketValue,
        decimal? CostBasis);

    private async Task<ProviderCaseworkPersistenceResult> SeedBreakQueueCasesAsync(
        ProviderLedgerReconciliationDetailDto detail,
        ReconciliationBreakQueueScope? accessScope,
        ProviderLedgerReconciliationRequestDto request,
        ProviderLedgerScope? ledgerScope,
        string? ledgerScopeError,
        CancellationToken ct)
    {
        var caseCount = detail.Breaks.Count
            + (detail.CorporateActionReadiness?.EvidenceCandidates.Count(static candidate =>
                candidate.Status is ProviderLedgerReconciliationCheckStatusDto.Break or ProviderLedgerReconciliationCheckStatusDto.Blocked) ?? 0)
            + (detail.SecurityMasterPassports?.Count(IsStaleResolvedSecurityMasterPassport) ?? 0);
        if (accessScope is null)
        {
            if (caseCount == 0)
            {
                return new ProviderCaseworkPersistenceResult(0, 0, IsSatisfied: true, IsBlocked: false, [], null);
            }

            return new ProviderCaseworkPersistenceResult(
                caseCount,
                0,
                IsSatisfied: false,
                IsBlocked: true,
                [],
                "Provider-ledger reconciliation requires an authoritative tenant and company scope before casework can be retained.");
        }

        if (ledgerScope is null || ledgerScope.Period is null || !ledgerScope.AsOfDate.HasValue)
        {
            return new ProviderCaseworkPersistenceResult(
                caseCount,
                0,
                IsSatisfied: false,
                IsBlocked: true,
                [],
                ledgerScopeError ?? "Exact primary ledger-book, accounting-period, and as-of scope is unavailable.");
        }

        if (_fundProfileTenancyRegistry is null)
        {
            return new ProviderCaseworkPersistenceResult(
                caseCount,
                0,
                IsSatisfied: false,
                IsBlocked: true,
                [],
                "The authoritative fund-profile tenancy registry is unavailable.");
        }

        FundProfileOwnership? ownership;
        try
        {
            ownership = await _fundProfileTenancyRegistry
                .ResolveAsync(ledgerScope.Book.FundProfileId, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Provider-ledger reconciliation could not verify ownership for fund profile {FundProfileId}",
                ledgerScope.Book.FundProfileId);
            return new ProviderCaseworkPersistenceResult(
                caseCount,
                0,
                IsSatisfied: false,
                IsBlocked: true,
                [],
                "The authoritative fund-profile owner could not be verified.");
        }

        if (ownership is null
            || !ownership.IsHeldBy(accessScope.TenantId)
            || string.IsNullOrWhiteSpace(ownership.CompanyId)
            || !string.Equals(
                ownership.CompanyId.Trim(),
                accessScope.CompanyId,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderCaseworkPersistenceResult(
                caseCount,
                0,
                IsSatisfied: false,
                IsBlocked: true,
                [],
                "The provider-ledger fund profile is not owned by the authenticated tenant and company.");
        }

        if (caseCount == 0)
        {
            return new ProviderCaseworkPersistenceResult(0, 0, IsSatisfied: true, IsBlocked: false, [], null);
        }

        if (_breakQueueRepository is null)
        {
            return new ProviderCaseworkPersistenceResult(
                caseCount,
                0,
                IsSatisfied: false,
                IsBlocked: true,
                [],
                "The durable reconciliation break queue is unavailable.");
        }

        var items = new List<ReconciliationBreakQueueItem>(caseCount);

        foreach (var breakRow in detail.Breaks)
        {
            ct.ThrowIfCancellationRequested();
            items.Add(ApplyLedgerPeriodScope(
                BuildBreakQueueItem(detail, breakRow, request, ledgerScope.Book),
                ledgerScope,
                accessScope));
        }

        if (detail.CorporateActionReadiness?.EvidenceCandidates.Count > 0)
        {
            var ledgerEffectsByCandidateId = detail.CorporateActionReadiness.LedgerEffects
                .Where(static effect => !string.IsNullOrWhiteSpace(effect.CandidateId))
                .ToDictionary(static effect => effect.CandidateId, StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in detail.CorporateActionReadiness.EvidenceCandidates.Where(static candidate =>
                         candidate.Status is ProviderLedgerReconciliationCheckStatusDto.Break or ProviderLedgerReconciliationCheckStatusDto.Blocked))
            {
                ct.ThrowIfCancellationRequested();
                ledgerEffectsByCandidateId.TryGetValue(candidate.CandidateId, out var ledgerEffect);
                items.Add(ApplyLedgerPeriodScope(
                    BuildCorporateActionCandidateCase(detail, candidate, ledgerEffect, request, ledgerScope.Book),
                    ledgerScope,
                    accessScope));
            }
        }

        if (detail.SecurityMasterPassports?.Count > 0)
        {
            foreach (var passport in detail.SecurityMasterPassports.Where(IsStaleResolvedSecurityMasterPassport))
            {
                ct.ThrowIfCancellationRequested();
                items.Add(ApplyLedgerPeriodScope(
                    BuildStaleSecurityMasterPassportCase(detail, passport, request, ledgerScope.Book),
                    ledgerScope,
                    accessScope));
            }
        }

        var retainedCaseIds = new List<string>(items.Count);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var existing = await _breakQueueRepository
                .GetByIdAsync(accessScope, item.BreakId, ct)
                .ConfigureAwait(false);
            if (existing is null)
            {
                await _breakQueueRepository
                    .CreateIfMissingAsync(accessScope, item, ct)
                    .ConfigureAwait(false);
                existing = await _breakQueueRepository
                    .GetByIdAsync(accessScope, item.BreakId, ct)
                    .ConfigureAwait(false);
            }

            if (existing is null)
            {
                throw new InvalidOperationException(
                    $"Reconciliation case '{item.BreakId}' was not readable after the queue accepted its persistence request.");
            }

            if (!HasEquivalentProviderCaseIdentity(existing, item))
            {
                return new ProviderCaseworkPersistenceResult(
                    items.Count,
                    retainedCaseIds.Count,
                    IsSatisfied: false,
                    IsBlocked: true,
                    retainedCaseIds,
                    $"Existing reconciliation case '{item.BreakId}' is bound to different source evidence or accounting scope. Resolve or supersede that case before retaining this run.");
            }

            retainedCaseIds.Add(existing.BreakId);
        }

        return new ProviderCaseworkPersistenceResult(
            items.Count,
            retainedCaseIds.Count,
            IsSatisfied: retainedCaseIds.Count == items.Count,
            IsBlocked: false,
            retainedCaseIds,
            null);
    }

    private static bool HasEquivalentProviderCaseIdentity(
        ReconciliationBreakQueueItem existing,
        ReconciliationBreakQueueItem candidate)
        => string.Equals(existing.BreakId, candidate.BreakId, StringComparison.Ordinal)
            && string.Equals(existing.SourceType, candidate.SourceType, StringComparison.Ordinal)
            && string.Equals(existing.SourceSystem, candidate.SourceSystem, StringComparison.Ordinal)
            && string.Equals(existing.SourceReference, candidate.SourceReference, StringComparison.Ordinal)
            && string.Equals(existing.SourceFingerprint, candidate.SourceFingerprint, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.TenantId, candidate.TenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.CompanyId, candidate.CompanyId, StringComparison.OrdinalIgnoreCase)
            && existing.LedgerBookId == candidate.LedgerBookId
            && string.Equals(existing.AccountingPeriodId, candidate.AccountingPeriodId, StringComparison.Ordinal)
            && existing.AsOfDate == candidate.AsOfDate;

    private async Task<ProviderLedgerScope> ResolvePrimaryLedgerScopeAsync(
        Guid accountId,
        DateOnly? asOfDate,
        LedgerBookDto? verifiedLedgerBook,
        CancellationToken ct)
    {
        var service = _ledgerBookService ?? throw new InvalidOperationException(
            "Provider-ledger reconciliation cannot create close/report casework without ILedgerBookService.");
        LedgerBookDto book;
        if (verifiedLedgerBook is null)
        {
            var books = await service.ListBooksAsync(
                    new LedgerBookQuery(
                        FundStructureNodeId: accountId,
                        AccountingBasis: AccountingBasisKindDto.Primary),
                    ct)
                .ConfigureAwait(false);
            if (books.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Provider-ledger reconciliation requires exactly one primary ledger book for fund account '{accountId:D}', but found {books.Count}.");
            }

            book = books[0];
        }
        else
        {
            book = verifiedLedgerBook;
        }
        if (book.LedgerBookId == Guid.Empty
            || book.FundStructureNodeId != accountId
            || book.AccountingBasis != AccountingBasisKindDto.Primary
            || string.IsNullOrWhiteSpace(book.FundProfileId)
            || string.IsNullOrWhiteSpace(book.BaseCurrency)
            || book.BaseCurrency.Trim().Length != 3)
        {
            throw new InvalidOperationException(
                $"Provider-ledger reconciliation found an incomplete or mismatched primary ledger book for fund account '{accountId:D}'.");
        }

        LedgerPeriodDto? period = null;
        if (asOfDate.HasValue)
        {
            var periods = await service.ListPeriodsAsync(
                    new LedgerPeriodQuery(
                        LedgerBookId: book.LedgerBookId,
                        AccountingBasis: AccountingBasisKindDto.Primary),
                    ct)
                .ConfigureAwait(false);
            var matches = periods
                .Where(candidate => candidate.LedgerBookId == book.LedgerBookId
                    && candidate.AccountingBasis == AccountingBasisKindDto.Primary
                    && candidate.StartDate <= asOfDate.Value
                    && candidate.EndDate >= asOfDate.Value)
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Provider-ledger reconciliation requires exactly one primary accounting period containing as-of date '{asOfDate:yyyy-MM-dd}' for ledger book '{book.LedgerBookId:D}', but found {matches.Length}.");
            }
            period = matches[0];
        }

        return new ProviderLedgerScope(book, period, asOfDate);
    }

    private async Task<ProviderLedgerAuthorityVerification> VerifyAuthoritativeScopeAsync(
        Guid accountId,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct)
    {
        if (_ledgerBookService is null || _fundProfileTenancyRegistry is null)
        {
            return ProviderLedgerAuthorityVerification.Failed(
                "PROVIDER_RECONCILIATION_AUTHORITY_UNAVAILABLE",
                "Provider-ledger reconciliation authority requires the ledger-book service and fund-profile tenancy registry.");
        }

        AccountSummaryDto? account;
        IReadOnlyList<LedgerBookDto> books;
        FundProfileOwnership? ownership;
        try
        {
            account = await _fundAccountService.GetAccountAsync(accountId, ct).ConfigureAwait(false);
            if (account is null
                || !account.IsActive
                || !account.FundId.HasValue
                || account.FundId.Value == Guid.Empty)
            {
                return ProviderLedgerAuthorityVerification.Failed(
                    "PROVIDER_RECONCILIATION_ACCOUNT_NOT_AUTHORIZED",
                    "Provider-ledger reconciliation requires an active account bound to a canonical fund.");
            }

            books = await _ledgerBookService
                .ListBooksAsync(
                    new LedgerBookQuery(
                        FundStructureNodeId: accountId,
                        AccountingBasis: AccountingBasisKindDto.Primary),
                    ct)
                .ConfigureAwait(false);
            var fundProfileId = account.FundId.Value.ToString("D");
            var matchingBooks = books
                .Where(book =>
                    book.FundStructureNodeId == accountId
                    && book.AccountingBasis == AccountingBasisKindDto.Primary
                    && string.Equals(
                        book.FundProfileId?.Trim(),
                        fundProfileId,
                        StringComparison.OrdinalIgnoreCase))
                .DistinctBy(static book => book.LedgerBookId)
                .ToArray();
            if (matchingBooks.Length != 1)
            {
                return ProviderLedgerAuthorityVerification.Failed(
                    "PROVIDER_RECONCILIATION_LEDGER_SCOPE_NOT_AUTHORIZED",
                    "Provider-ledger reconciliation requires exactly one primary ledger book bound to the account's canonical fund.");
            }

            ownership = await _fundProfileTenancyRegistry
                .ResolveAsync(fundProfileId, ct)
                .ConfigureAwait(false);
            if (ownership is null
                || !ownership.IsHeldBy(accessScope.TenantId)
                || string.IsNullOrWhiteSpace(ownership.CompanyId)
                || !string.Equals(
                    ownership.CompanyId.Trim(),
                    accessScope.CompanyId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ProviderLedgerAuthorityVerification.Failed(
                    "PROVIDER_RECONCILIATION_FUND_NOT_AUTHORIZED",
                    "The provider-ledger fund is not owned by the authenticated tenant and company.");
            }

            return ProviderLedgerAuthorityVerification.Verified(matchingBooks[0]);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Provider-ledger reconciliation authority verification failed for account {AccountId}",
                accountId);
            return ProviderLedgerAuthorityVerification.Failed(
                "PROVIDER_RECONCILIATION_AUTHORITY_UNAVAILABLE",
                "Provider-ledger reconciliation authority could not be verified.");
        }
    }

    private static ReconciliationBreakQueueItem ApplyLedgerPeriodScope(
        ReconciliationBreakQueueItem item,
        ProviderLedgerScope scope,
        ReconciliationBreakQueueScope accessScope)
    {
        var scopedFingerprint = ComputeQueueSourceFingerprint(
            item.SourceFingerprint,
            accessScope.TenantId,
            accessScope.CompanyId,
            scope.Book.FundProfileId,
            scope.Book.LedgerBookId,
            scope.Book.AccountingBasis,
            scope.Book.AccountingPolicyId,
            scope.Book.AccountingPolicyVersion,
            scope.Period?.PeriodId,
            scope.AsOfDate);
        var hasExactScope = scope.Period is not null && scope.AsOfDate.HasValue;
        return item with
        {
            TenantId = accessScope.TenantId,
            CompanyId = accessScope.CompanyId,
            AccountingPeriodId = scope.Period?.PeriodId.ToString("D"),
            AsOfDate = scope.AsOfDate,
            SourceFingerprint = scopedFingerprint,
            EvidenceLinks = (item.EvidenceLinks ?? [])
                .Append($"urn:sha256:{scopedFingerprint}")
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ExceptionRoute = hasExactScope
                ? item.ExceptionRoute
                : "accounting/reconciliation/scope-resolution",
            LifecycleRationale = hasExactScope
                ? item.LifecycleRationale
                : $"{item.LifecycleRationale} Exact accounting period/as-of scope is unavailable; this case is quarantined from close/report evidence until scope resolution.",
            BlockedOutputs = hasExactScope
                ? item.BlockedOutputs
                : ["reconciliation-scope-resolution"]
        };
    }

    private static ReconciliationBreakQueueItem BuildBreakQueueItem(
        ProviderLedgerReconciliationDetailDto detail,
        ProviderLedgerReconciliationBreakDto breakRow,
        ProviderLedgerReconciliationRequestDto request,
        LedgerBookDto ledgerBook)
    {
        var summary = detail.Summary;
        var signedOff = breakRow.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.SignedOff;
        var signedOffBy = NormalizeOwner(breakRow.SignedOffBy);
        var signedOffAt = breakRow.SignedOffAt ?? (signedOff ? summary.CreatedAt : null);
        var status = signedOff
            ? ReconciliationBreakQueueStatus.Resolved
            : ReconciliationBreakQueueStatus.Open;
        var isSecurityMasterIdentityCase = IsSecurityMasterIdentityBreak(breakRow);
        var signoffRole = isSecurityMasterIdentityCase ? "Security Master steward" : "Fund accounting";
        var assignedTo = isSecurityMasterIdentityCase
            ? NormalizeOwner(request.DefaultBreakOwner) ?? "security-master-steward"
            : breakRow.Owner;
        var signoffStatus = signedOff
            ? "signed-off"
            : breakRow.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.Assigned
                ? "assigned"
                : "pending-signoff";
        var caseId = BuildQueueBreakId(summary.AccountId, breakRow);
        var latestRoute = UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest.Replace(
            "{accountId}",
            summary.AccountId.ToString("D"),
            StringComparison.Ordinal);
        var syncCursor = string.Join(
            "|",
            summary.ProviderId ?? "provider-unknown",
            summary.ExternalAccountId ?? "external-account-unknown",
            summary.ProviderSyncedAt?.ToString("O") ?? "provider-sync-missing",
            summary.ReconciliationRunId.ToString("N"));
        var sourceSnapshotCursor = string.Join(
            "|",
            summary.ProviderId ?? "provider-unknown",
            summary.ExternalAccountId ?? "external-account-unknown",
            summary.ProviderSyncedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "provider-sync-missing");
        var passport = isSecurityMasterIdentityCase
            ? FindPassportForBreak(detail, breakRow)
            : null;
        var explanation = BuildBreakExplanation(summary, breakRow, latestRoute, syncCursor, passport);
        var evidenceLinks = explanation.EvidenceLinks
            .Append(breakRow.EvidenceLink)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var sourceFingerprint = ComputeQueueSourceFingerprint(
            "provider-ledger-break",
            caseId,
            sourceSnapshotCursor,
            breakRow.ExpectedAmount,
            breakRow.ActualAmount,
            breakRow.Variance);
        var blockedOutputs = signedOff
            ? Array.Empty<string>()
            : ["accounting-close", "certified-reporting"];

        return new ReconciliationBreakQueueItem(
            BreakId: caseId,
            RunId: summary.ReconciliationRunId.ToString("N"),
            StrategyName: "Provider ledger reconciliation",
            Category: breakRow.Category,
            Status: status,
            Variance: breakRow.Variance ?? 0m,
            Reason: breakRow.Reason,
            AssignedTo: assignedTo,
            DetectedAt: breakRow.FirstObservedAt ?? summary.CreatedAt,
            LastUpdatedAt: breakRow.LastObservedAt ?? summary.CreatedAt,
            ReviewedBy: signedOff ? signedOffBy : null,
            ReviewedAt: signedOffAt,
            ResolvedBy: signedOff ? signedOffBy : null,
            ResolvedAt: signedOffAt,
            ResolutionNote: signedOff ? "Provider-ledger reconciliation break signed off." : breakRow.Reason,
            Severity: breakRow.Severity,
            ExceptionRoute: isSecurityMasterIdentityCase
                ? "security-master/unresolved-provider-symbols"
                : "accounting/reconciliation/provider-ledger",
            ToleranceProfileId: isSecurityMasterIdentityCase
                ? "security-master-identity"
                : $"provider-ledger:{summary.ProviderId ?? "unknown"}",
            ToleranceBand: breakRow.Tolerance,
            RequiredSignoffRole: signoffRole,
            SignoffStatus: signoffStatus,
            FundAccountId: summary.AccountId.ToString("D"),
            ExplainabilitySummary: BuildExplainabilitySummary(summary, breakRow, passport),
            RoutingTarget: latestRoute,
            RoutingDetail: breakRow.CheckId,
            RecommendedAction: BuildRecommendedAction(breakRow),
            LifecycleState: signedOff ? ReconciliationCaseLifecycleState.Posted : ReconciliationCaseLifecycleState.Open,
            LifecycleRationale: signedOff
                ? "Provider-ledger break key was signed off in the reconciliation request."
                : isSecurityMasterIdentityCase
                    ? "Auto-generated from unresolved provider Security Master identity in provider-ledger reconciliation."
                    : "Auto-generated from provider-ledger reconciliation break.",
            ExternalAccountId: summary.ExternalAccountId,
            CustodianId: summary.ProviderId,
            UpstreamSyncCursor: syncCursor,
            LastUpstreamSyncAt: summary.ProviderSyncedAt,
            SignoffHistory: signedOff && signedOffBy is not null && signedOffAt.HasValue
                ? [new ReconciliationCaseSignoffRecord(signedOffBy, signoffRole, "Resolved", breakRow.Reason, signedOffAt.Value)]
                : null,
            Team: isSecurityMasterIdentityCase ? "Security Master" : null,
            Counterparty: summary.ProviderId,
            StateTransitions: [],
            EvidenceLinks: evidenceLinks,
            SourceType: "provider-ledger-reconciliation",
            SourceSystem: summary.ProviderId ?? "provider-unknown",
            SourceReference: breakRow.BreakKey ?? breakRow.CheckId,
            SourceFingerprint: sourceFingerprint,
            BreakExplanation: explanation,
            LedgerBookId: ledgerBook.LedgerBookId,
            Measures: BuildProviderBreakMeasures(breakRow, ledgerBook.BaseCurrency),
            BlockedOutputs: blockedOutputs)
        {
            FundProfileId = ledgerBook.FundProfileId
        };
    }

    private static ProviderSecurityMasterPassportDto? FindPassportForBreak(
        ProviderLedgerReconciliationDetailDto detail,
        ProviderLedgerReconciliationBreakDto breakRow)
    {
        if (string.IsNullOrWhiteSpace(breakRow.Symbol) || detail.SecurityMasterPassports is null)
        {
            return null;
        }

        return detail.SecurityMasterPassports.FirstOrDefault(passport =>
            string.Equals(passport.Symbol, breakRow.Symbol.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSecurityMasterIdentityBreak(ProviderLedgerReconciliationBreakDto breakRow)
        => breakRow.Category == ReconciliationBreakCategory.ClassificationGap &&
           (breakRow.Code.StartsWith("SM_", StringComparison.OrdinalIgnoreCase) ||
            breakRow.CheckId.StartsWith("security-master:", StringComparison.OrdinalIgnoreCase));

    private static string BuildQueueBreakId(Guid accountId, ProviderLedgerReconciliationBreakDto breakRow)
        => string.Join(
            ":",
            "provider-ledger",
            accountId.ToString("N"),
            NormalizeBreakIdPart(breakRow.BreakKey ?? $"{breakRow.CheckId}:{breakRow.Code}"));

    private static ReconciliationBreakQueueItem BuildCorporateActionCandidateCase(
        ProviderLedgerReconciliationDetailDto detail,
        ProviderCorporateActionEvidenceCandidateDto candidate,
        ProviderCorporateActionLedgerEffectDto? ledgerEffect,
        ProviderLedgerReconciliationRequestDto request,
        LedgerBookDto ledgerBook)
    {
        var summary = detail.Summary;
        var latestRoute = UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest.Replace(
            "{accountId}",
            summary.AccountId.ToString("D"),
            StringComparison.Ordinal);
        var severity = candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Blocked
            ? ReconciliationBreakSeverity.High
            : ReconciliationBreakSeverity.Medium;
        var syncCursor = string.Join(
            "|",
            summary.ProviderId ?? candidate.ProviderId,
            summary.ExternalAccountId ?? candidate.ExternalAccountId,
            summary.ProviderSyncedAt?.ToString("O") ?? "provider-sync-missing",
            candidate.CandidateId);
        var reason = string.IsNullOrWhiteSpace(candidate.Reason)
            ? $"{candidate.CandidateType} requires controller review before close or valuation support."
            : candidate.Reason;
        var caseId = BuildCorporateActionCandidateCaseId(summary.AccountId, candidate);
        var explanation = BuildCorporateActionCandidateBreakExplanation(summary, candidate, reason, latestRoute, syncCursor);
        var evidenceLinks = explanation.EvidenceLinks
            .Append(candidate.ProviderEventId)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ReconciliationBreakQueueItem(
            BreakId: caseId,
            RunId: summary.ReconciliationRunId.ToString("N"),
            StrategyName: "Provider corporate-action evidence",
            Category: MapCorporateActionCandidateCategory(candidate),
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: 0m,
            Reason: reason,
            AssignedTo: NormalizeOwner(request.DefaultBreakOwner) ?? "security-master-steward",
            DetectedAt: candidate.ObservedAt,
            LastUpdatedAt: summary.CreatedAt,
            Severity: severity,
            ExceptionRoute: "accounting/reconciliation/provider-ledger/corporate-actions",
            ToleranceProfileId: $"provider-corporate-actions:{summary.ProviderId ?? candidate.ProviderId}",
            RequiredSignoffRole: "Security Master steward",
            SignoffStatus: "pending-signoff",
            FundAccountId: summary.AccountId.ToString("D"),
            ExplainabilitySummary: BuildCorporateActionCandidateExplainability(summary, candidate, ledgerEffect),
            RoutingTarget: latestRoute,
            RoutingDetail: candidate.CandidateId,
            RecommendedAction: "Resolve Security Master attribution or provider corporate-action/factor feed routing before using this evidence for ledger valuation or close.",
            LifecycleState: ReconciliationCaseLifecycleState.Open,
            LifecycleRationale: "Auto-generated from degraded provider corporate-action or factor-schedule evidence.",
            ExternalAccountId: summary.ExternalAccountId ?? candidate.ExternalAccountId,
            CustodianId: summary.ProviderId ?? candidate.ProviderId,
            UpstreamSyncCursor: syncCursor,
            LastUpstreamSyncAt: summary.ProviderSyncedAt,
            Team: "Security Master",
            Counterparty: summary.ProviderId ?? candidate.ProviderId,
            StateTransitions: [],
            EvidenceLinks: evidenceLinks,
            SourceType: "provider-corporate-action",
            SourceSystem: summary.ProviderId ?? candidate.ProviderId,
            SourceReference: candidate.ProviderEventId ?? candidate.CandidateId,
            SourceFingerprint: ComputeQueueSourceFingerprint(
                "provider-corporate-action",
                caseId,
                syncCursor,
                candidate.Amount,
                candidate.Quantity,
                ledgerEffect?.CashAmount),
            BreakExplanation: explanation,
            LedgerBookId: ledgerBook.LedgerBookId,
            Measures: BuildUnavailableProviderMeasures(
                ledgerBook.BaseCurrency,
                "The provider corporate-action candidate does not contain an authoritative expected and actual value pair.",
                "The provider corporate-action candidate does not contain an authoritative expected and actual quantity pair.",
                "The provider corporate-action candidate does not contain an authoritative expected and actual cost-basis pair."),
            BlockedOutputs: ["accounting-close", "certified-reporting"])
        {
            FundProfileId = ledgerBook.FundProfileId
        };
    }

    private static ReconciliationBreakQueueItem BuildStaleSecurityMasterPassportCase(
        ProviderLedgerReconciliationDetailDto detail,
        ProviderSecurityMasterPassportDto passport,
        ProviderLedgerReconciliationRequestDto request,
        LedgerBookDto ledgerBook)
    {
        var summary = detail.Summary;
        var latestRoute = UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest.Replace(
            "{accountId}",
            summary.AccountId.ToString("D"),
            StringComparison.Ordinal);
        var provider = summary.ProviderId ?? passport.ProviderId;
        var externalAccount = summary.ExternalAccountId ?? passport.ExternalAccountId;
        var syncCursor = string.Join(
            "|",
            provider,
            externalAccount,
            summary.ProviderSyncedAt?.ToString("O") ?? passport.ProviderSyncedAt.ToString("O"),
            passport.Symbol,
            "stale-security-master-passport");
        var reason = $"Provider-to-Security Master mapping for {passport.Symbol} is resolved but backed by stale provider evidence ({passport.FreshnessMinutes} minute(s) old).";
        var caseId = BuildStaleSecurityMasterPassportCaseId(summary.AccountId, passport);
        var explanation = BuildStaleSecurityMasterPassportBreakExplanation(passport, latestRoute, syncCursor);

        return new ReconciliationBreakQueueItem(
            BreakId: caseId,
            RunId: summary.ReconciliationRunId.ToString("N"),
            StrategyName: "Provider Security Master passport",
            Category: ReconciliationBreakCategory.ClassificationGap,
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: 0m,
            Reason: reason,
            AssignedTo: NormalizeOwner(request.DefaultBreakOwner) ?? "security-master-steward",
            DetectedAt: passport.ProviderSyncedAt,
            LastUpdatedAt: summary.CreatedAt,
            Severity: ReconciliationBreakSeverity.Medium,
            ExceptionRoute: "security-master/stale-provider-mappings",
            ToleranceProfileId: "security-master-provider-freshness",
            RequiredSignoffRole: "Security Master steward",
            SignoffStatus: "pending-signoff",
            FundAccountId: summary.AccountId.ToString("D"),
            ExplainabilitySummary: BuildStaleSecurityMasterPassportExplainability(summary, passport),
            RoutingTarget: latestRoute,
            RoutingDetail: passport.SecurityId?.ToString("D") ?? passport.Symbol,
            RecommendedAction: "Refresh provider evidence or confirm the Security Master mapping before using this provider position for ledger close or report provenance.",
            LifecycleState: ReconciliationCaseLifecycleState.Open,
            LifecycleRationale: "Auto-generated from stale provider evidence on a resolved Security Master passport.",
            ExternalAccountId: externalAccount,
            CustodianId: provider,
            UpstreamSyncCursor: syncCursor,
            LastUpstreamSyncAt: summary.ProviderSyncedAt ?? passport.ProviderSyncedAt,
            Team: "Security Master",
            Counterparty: provider,
            StateTransitions: [],
            EvidenceLinks: explanation.EvidenceLinks,
            SourceType: "provider-security-master-passport",
            SourceSystem: provider,
            SourceReference: passport.SecurityId?.ToString("D") ?? passport.Symbol,
            SourceFingerprint: ComputeQueueSourceFingerprint(
                "provider-security-master-passport",
                caseId,
                syncCursor,
                passport.ConfidenceScore,
                passport.FreshnessMinutes,
                passport.ProviderSyncedAt.UtcTicks),
            BreakExplanation: explanation,
            LedgerBookId: ledgerBook.LedgerBookId,
            Measures: BuildUnavailableProviderMeasures(
                ledgerBook.BaseCurrency,
                "A stale Security Master passport is identity evidence and does not contain an authoritative expected and actual value pair.",
                "A stale Security Master passport is identity evidence and does not contain an authoritative expected and actual quantity pair.",
                "A stale Security Master passport is identity evidence and does not contain an authoritative expected and actual cost-basis pair."),
            BlockedOutputs: ["accounting-close", "certified-reporting"])
        {
            FundProfileId = ledgerBook.FundProfileId
        };
    }

    private static string BuildCorporateActionCandidateCaseId(
        Guid accountId,
        ProviderCorporateActionEvidenceCandidateDto candidate)
        => string.Join(
            ":",
            "provider-ledger-corporate-action",
            accountId.ToString("N"),
            NormalizeBreakIdPart(candidate.CandidateId));

    private static string BuildStaleSecurityMasterPassportCaseId(
        Guid accountId,
        ProviderSecurityMasterPassportDto passport)
        => string.Join(
            ":",
            "provider-ledger-security-master-stale",
            accountId.ToString("N"),
            NormalizeBreakIdPart(passport.SecurityId?.ToString("N") ?? passport.Symbol));

    private static IReadOnlyList<ReconciliationBreakMeasureDto> BuildProviderBreakMeasures(
        ProviderLedgerReconciliationBreakDto breakRow,
        string baseCurrency)
    {
        var currency = baseCurrency.Trim().ToUpperInvariant();
        var hasExactValue = breakRow.ExpectedAmount.HasValue
            && breakRow.ActualAmount.HasValue
            && breakRow.Variance.HasValue
            && breakRow.Variance.Value == breakRow.ActualAmount.Value - breakRow.ExpectedAmount.Value;
        var comparisonKind = breakRow.Code.Contains("POSITION_QUANTITY", StringComparison.OrdinalIgnoreCase)
            ? ReconciliationBreakMeasureKindDto.Quantity
            : breakRow.Code.Contains("POSITION_COST_BASIS", StringComparison.OrdinalIgnoreCase)
                ? ReconciliationBreakMeasureKindDto.CostBasis
                : ReconciliationBreakMeasureKindDto.Value;
        ReconciliationBreakMeasureDto BuildMeasure(
            ReconciliationBreakMeasureKindDto kind,
            string unit,
            string unavailableReason)
        {
            if (kind == comparisonKind && hasExactValue)
            {
                return new ReconciliationBreakMeasureDto(
                    kind,
                    breakRow.ExpectedAmount,
                    breakRow.ActualAmount,
                    breakRow.Variance,
                    breakRow.Tolerance.HasValue ? Math.Abs(breakRow.Tolerance.Value) : null,
                    unit);
            }

            return new ReconciliationBreakMeasureDto(
                kind,
                Expected: null,
                Actual: null,
                Variance: null,
                Tolerance: kind == comparisonKind && breakRow.Tolerance.HasValue
                    ? Math.Abs(breakRow.Tolerance.Value)
                    : null,
                Unit: unit,
                UnavailableReason: kind == comparisonKind
                    ? $"The provider break does not contain a complete, arithmetically consistent expected and actual {kind.ToString().ToLowerInvariant()} comparison."
                    : unavailableReason);
        }

        return
        [
            BuildMeasure(
                ReconciliationBreakMeasureKindDto.Value,
                currency,
                "This item-level break compares a different measure and does not provide a separate expected and actual value pair."),
            BuildMeasure(
                ReconciliationBreakMeasureKindDto.Quantity,
                "units",
                "This item-level break compares a different measure and does not provide a separate expected and actual quantity pair."),
            BuildMeasure(
                ReconciliationBreakMeasureKindDto.CostBasis,
                currency,
                "This item-level break compares a different measure and does not provide a separate expected and actual cost-basis pair.")
        ];
    }

    private static IReadOnlyList<ReconciliationBreakMeasureDto> BuildUnavailableProviderMeasures(
        string baseCurrency,
        string valueReason,
        string quantityReason,
        string costBasisReason)
    {
        var currency = baseCurrency.Trim().ToUpperInvariant();
        return
        [
            new ReconciliationBreakMeasureDto(
                ReconciliationBreakMeasureKindDto.Value,
                Expected: null,
                Actual: null,
                Variance: null,
                Tolerance: null,
                Unit: currency,
                UnavailableReason: valueReason),
            new ReconciliationBreakMeasureDto(
                ReconciliationBreakMeasureKindDto.Quantity,
                Expected: null,
                Actual: null,
                Variance: null,
                Tolerance: null,
                Unit: "units",
                UnavailableReason: quantityReason),
            new ReconciliationBreakMeasureDto(
                ReconciliationBreakMeasureKindDto.CostBasis,
                Expected: null,
                Actual: null,
                Variance: null,
                Tolerance: null,
                Unit: currency,
                UnavailableReason: costBasisReason)
        ];
    }

    private static string ComputeQueueSourceFingerprint(params object?[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            var text = CanonicalScalar(value);
            builder.Append(text.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(text)
                .Append('|');
        }

        return Meridian.Contracts.Integrity.Sha256Digest.ComputeUtf8(builder.ToString());
    }

    private static ReconciliationBreakCategory MapCorporateActionCandidateCategory(
        ProviderCorporateActionEvidenceCandidateDto candidate)
        => candidate.SecurityId is null && !string.IsNullOrWhiteSpace(candidate.Symbol)
            ? ReconciliationBreakCategory.ClassificationGap
            : ReconciliationBreakCategory.MissingPortfolioCoverage;

    private static string BuildCorporateActionCandidateExplainability(
        ProviderLedgerReconciliationSummaryDto summary,
        ProviderCorporateActionEvidenceCandidateDto candidate,
        ProviderCorporateActionLedgerEffectDto? ledgerEffect)
        => string.Join(
            ", ",
            new[]
            {
                $"provider={summary.ProviderId ?? candidate.ProviderId}",
                $"externalAccount={summary.ExternalAccountId ?? candidate.ExternalAccountId}",
                $"candidate={candidate.CandidateType}",
                $"providerEventId={candidate.ProviderEventId ?? "unknown"}",
                $"symbol={candidate.Symbol ?? "account"}",
                $"securityId={candidate.SecurityId?.ToString("D") ?? "unresolved"}",
                $"requiredFeed={candidate.RequiredFeed}",
                $"evidenceSource={candidate.EvidenceSource}",
                $"amount={FormatAmount(candidate.Amount)}",
                ledgerEffect is null ? null : $"ledgerEffect={ledgerEffect.LedgerEffectKind}",
                ledgerEffect?.EffectiveDate is null ? null : $"effectiveDate={ledgerEffect.EffectiveDate.Value:yyyy-MM-dd}",
                ledgerEffect?.Factor is null ? null : $"factor={FormatAmount(ledgerEffect.Factor)}",
                ledgerEffect?.CashAmount is null ? null : $"cashAmount={FormatAmount(ledgerEffect.CashAmount)}",
                ledgerEffect?.PrincipalAmount is null ? null : $"principalAmount={FormatAmount(ledgerEffect.PrincipalAmount)}",
                ledgerEffect?.IncomeAmount is null ? null : $"incomeAmount={FormatAmount(ledgerEffect.IncomeAmount)}",
                string.IsNullOrWhiteSpace(ledgerEffect?.Currency) ? null : $"currency={ledgerEffect.Currency}",
                ledgerEffect is null ? null : $"journalLines={ledgerEffect.JournalLines.Count}",
                $"status={candidate.Status}"
            }.Where(static part => !string.IsNullOrWhiteSpace(part)));

    private static string BuildStaleSecurityMasterPassportExplainability(
        ProviderLedgerReconciliationSummaryDto summary,
        ProviderSecurityMasterPassportDto passport)
        => string.Join(
            ", ",
            new[]
            {
                $"provider={summary.ProviderId ?? passport.ProviderId}",
                $"externalAccount={summary.ExternalAccountId ?? passport.ExternalAccountId}",
                $"symbol={passport.Symbol}",
                $"securityId={passport.SecurityId?.ToString("D") ?? "unresolved"}",
                $"resolutionSource={passport.ResolutionSource}",
                $"confidence={FormatAmount(passport.ConfidenceScore)}",
                $"freshnessMinutes={passport.FreshnessMinutes}",
                $"validationIssues={string.Join("|", passport.ValidationIssueCodes)}",
                $"status={passport.Status}"
            });

    private static string BuildExplainabilitySummary(
        ProviderLedgerReconciliationSummaryDto summary,
        ProviderLedgerReconciliationBreakDto breakRow,
        ProviderSecurityMasterPassportDto? passport = null)
        => string.Join(
            ", ",
            new[]
            {
                $"provider={summary.ProviderId ?? "unknown"}",
                $"externalAccount={summary.ExternalAccountId ?? "unknown"}",
                $"check={breakRow.CheckId}",
                $"code={breakRow.Code}",
                $"expected={FormatAmount(breakRow.ExpectedAmount)}",
                $"actual={FormatAmount(breakRow.ActualAmount)}",
                $"variance={FormatAmount(breakRow.Variance)}",
                $"symbol={breakRow.Symbol ?? "account"}",
                passport is null ? null : $"passportStatus={passport.Status}",
                passport is null ? null : $"resolutionSource={passport.ResolutionSource}",
                passport is null ? null : $"confidence={FormatAmount(passport.ConfidenceScore)}",
                passport is null ? null : $"freshnessMinutes={passport.FreshnessMinutes}",
                passport is null ? null : $"providerStale={passport.ProviderIsStale}",
                passport is null ? null : $"validationIssues={string.Join("|", passport.ValidationIssueCodes)}",
                passport is null ? null : $"identifierConflicts={string.Join("|", passport.IdentifierConflicts)}",
                passport is null ? null : $"overrideCount={passport.OverrideHistory.Count}"
            }.Where(static part => !string.IsNullOrWhiteSpace(part)));

    private static string BuildRecommendedAction(ProviderLedgerReconciliationBreakDto breakRow)
        => breakRow.Code.StartsWith("SM_", StringComparison.OrdinalIgnoreCase)
            ? "Resolve or approve the Security Master identity before sign-off."
            : "Review provider evidence against the internal ledger snapshot and sign off or resolve the break.";

    private static ReconciliationBreakExplanationDto BuildBreakExplanation(
        ProviderLedgerReconciliationSummaryDto summary,
        ProviderLedgerReconciliationBreakDto breakRow,
        string latestRoute,
        string syncCursor,
        ProviderSecurityMasterPassportDto? passport = null)
    {
        var provider = summary.ProviderId ?? "unknown provider";
        var externalAccount = summary.ExternalAccountId ?? "unknown external account";
        var symbol = string.IsNullOrWhiteSpace(breakRow.Symbol) ? "account" : breakRow.Symbol;
        var variance = FormatAmount(breakRow.Variance);
        var probableCause = breakRow.Category switch
        {
            ReconciliationBreakCategory.CashMismatch =>
                "Provider cash, pending-settlement, or internal ledger cash evidence does not match within the account tolerance band.",
            ReconciliationBreakCategory.MissingPortfolioCoverage =>
                "Provider position evidence could not be paired with an internal position or Security Master mapping.",
            ReconciliationBreakCategory.ClassificationGap =>
                "Security Master classification or identifier coverage is incomplete for provider evidence used by reconciliation.",
            ReconciliationBreakCategory.MissingLedgerCoverage =>
                "The internal ledger is missing a retained posting or journal preview for provider-side account activity.",
            ReconciliationBreakCategory.TimingMismatch =>
                "Provider and Meridian evidence were captured outside the configured freshness or as-of tolerance window.",
            _ =>
                "Provider and Meridian accounting evidence produced a reconciliation variance that requires operator review."
        };

        return new ReconciliationBreakExplanationDto(
            Summary: $"{HumanizeBreakCategory(breakRow.Category)} for {symbol} from {provider} account {externalAccount}; variance {variance}.",
            SourceSystems: [provider, "Meridian ledger", "Meridian positions", "Security Master"],
            ProbableCause: probableCause,
            LedgerImpact: BuildBreakLedgerImpact(breakRow, variance, passport),
            SuggestedNextAction: BuildRecommendedAction(breakRow),
            EvidenceLinks: [latestRoute, breakRow.CheckId, syncCursor]);
    }

    private static string BuildBreakLedgerImpact(
        ProviderLedgerReconciliationBreakDto breakRow,
        string variance,
        ProviderSecurityMasterPassportDto? passport)
    {
        var impact = $"Expected {FormatAmount(breakRow.ExpectedAmount)}, actual {FormatAmount(breakRow.ActualAmount)}, variance {variance}; close readiness should treat the related ledger, cash, position, and report evidence as blocked until resolved or signed off.";
        if (passport is null)
        {
            return impact;
        }

        return $"{impact} Provider-to-Security Master passport status {passport.Status}, confidence {FormatAmount(passport.ConfidenceScore)}, resolution source {passport.ResolutionSource}, freshness {passport.FreshnessMinutes} minute(s), validation issues {FormatIssueList(passport.ValidationIssueCodes)}, identifier conflicts {FormatIssueList(passport.IdentifierConflicts)}, override history count {passport.OverrideHistory.Count}.";
    }

    private static string FormatIssueList(IReadOnlyList<string> values) =>
        values.Count == 0 ? "none" : string.Join("|", values);

    private static ReconciliationBreakExplanationDto BuildCorporateActionCandidateBreakExplanation(
        ProviderLedgerReconciliationSummaryDto summary,
        ProviderCorporateActionEvidenceCandidateDto candidate,
        string reason,
        string latestRoute,
        string syncCursor)
    {
        var provider = summary.ProviderId ?? candidate.ProviderId;
        var symbol = candidate.Symbol ?? "account";
        return new ReconciliationBreakExplanationDto(
            Summary: $"{candidate.CandidateType} evidence for {symbol} needs Security Master review before close support.",
            SourceSystems: [provider, "Security Master", "Provider corporate-action/factor evidence", "Meridian ledger"],
            ProbableCause: reason,
            LedgerImpact: $"Corporate-action, factor, dividend, interest, or valuation support for {symbol} may be missing or stale; ledger accrual and report-pack evidence should not be treated as ready until attribution is resolved.",
            SuggestedNextAction: "Resolve Security Master attribution or provider corporate-action/factor feed routing before using this evidence for ledger valuation or close.",
            EvidenceLinks: [latestRoute, candidate.CandidateId, syncCursor]);
    }

    private static ReconciliationBreakExplanationDto BuildStaleSecurityMasterPassportBreakExplanation(
        ProviderSecurityMasterPassportDto passport,
        string latestRoute,
        string syncCursor)
        => new(
            Summary: $"Resolved provider Security Master mapping for {passport.Symbol} is stale; confidence {FormatAmount(passport.ConfidenceScore)}.",
            SourceSystems: [passport.ProviderId, "Security Master", "Provider Security Master passport", "Meridian ledger"],
            ProbableCause: "The latest retained provider projection is older than the reconciliation freshness tolerance even though the provider symbol still resolves to a Security Master identity.",
            LedgerImpact: "Ledger close, report-line provenance, and valuation support should not rely on the stale provider mapping until provider evidence is refreshed or the mapping is explicitly confirmed.",
            SuggestedNextAction: "Refresh provider evidence or confirm the Security Master mapping with steward sign-off.",
            EvidenceLinks: [latestRoute, passport.Symbol, syncCursor]);

    private static bool IsStaleResolvedSecurityMasterPassport(ProviderSecurityMasterPassportDto passport)
        => passport.ProviderIsStale &&
           passport.Status is ProviderSecurityMasterPassportStatusDto.Resolved or ProviderSecurityMasterPassportStatusDto.Inferred &&
           passport.SecurityId.HasValue;

    private static string HumanizeBreakCategory(ReconciliationBreakCategory category)
        => category.ToString().Replace("Mismatch", " mismatch", StringComparison.Ordinal);

    private static string FormatAmount(decimal? amount)
        => amount.HasValue ? amount.Value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture) : "n/a";

    private async Task AddProviderCapabilityChecksAsync(
        Guid runId,
        BreakLifecycleContext lifecycle,
        FundAccountBrokerageSyncActivityDto providerProjection,
        List<ProviderLedgerReconciliationCheckDto> checks,
        List<ProviderLedgerReconciliationBreakDto> breaks,
        CancellationToken ct)
    {
        if (_capabilityRouter is null)
        {
            return;
        }

        foreach (var capability in RequiredProviderLedgerCapabilities)
        {
            await AddProviderCapabilityCheckAsync(
                    runId,
                    lifecycle,
                    providerProjection,
                    capability,
                    checks,
                    breaks,
                    missingStatus: ProviderLedgerReconciliationCheckStatusDto.Blocked,
                    missingCode: "PROVIDER_CAPABILITY_UNROUTABLE",
                    missingSeverity: ReconciliationBreakSeverity.Critical,
                    ct)
                .ConfigureAwait(false);
        }

        foreach (var assetClass in providerProjection.Positions
                     .Select(static position => NormalizeAssetClass(position.AssetClass))
                     .Where(static assetClass => !string.IsNullOrWhiteSpace(assetClass))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static assetClass => assetClass, StringComparer.OrdinalIgnoreCase))
        {
            await AddProviderCapabilityCheckAsync(
                    runId,
                    lifecycle,
                    providerProjection,
                    ProviderCapabilityKind.AccountPositions,
                    checks,
                    breaks,
                    missingStatus: ProviderLedgerReconciliationCheckStatusDto.Break,
                    missingCode: "PROVIDER_ASSET_CLASS_POSITION_CAPABILITY_MISSING",
                    missingSeverity: ReconciliationBreakSeverity.Medium,
                    ct,
                    assetClass: assetClass,
                    checkIdSuffix: NormalizeCheckIdPart(assetClass),
                    labelSuffix: $"for {assetClass} positions")
                .ConfigureAwait(false);

            await AddProviderCapabilityCheckAsync(
                    runId,
                    lifecycle,
                    providerProjection,
                    ProviderCapabilityKind.HistoricalQuotes,
                    checks,
                    breaks,
                    missingStatus: ProviderLedgerReconciliationCheckStatusDto.Break,
                    missingCode: "PROVIDER_QUOTE_HISTORY_CAPABILITY_MISSING",
                    missingSeverity: ReconciliationBreakSeverity.Medium,
                    ct,
                    assetClass: assetClass,
                    checkIdSuffix: NormalizeCheckIdPart(assetClass),
                    labelSuffix: $"for {assetClass} valuation marks")
                .ConfigureAwait(false);
        }

        if (providerProjection.Positions.Count > 0)
        {
            await AddProviderCapabilityCheckAsync(
                    runId,
                    lifecycle,
                    providerProjection,
                    ProviderCapabilityKind.CorporateActions,
                    checks,
                    breaks,
                    missingStatus: ProviderLedgerReconciliationCheckStatusDto.Break,
                    missingCode: "PROVIDER_CORPORATE_ACTION_CAPABILITY_MISSING",
                    missingSeverity: ReconciliationBreakSeverity.Medium,
                    ct)
                .ConfigureAwait(false);
        }

        if (providerProjection.Positions.Any(static position => IsFixedIncomeOrStructuredAssetClass(position.AssetClass)) ||
            (providerProjection.CorporateActions ?? []).Any(static action =>
                IsFactorScheduleEvent(action.EventType) ||
                IsAmortizationScheduleEvent(action.EventType) ||
                IsLoanScheduleEvent(action.EventType)))
        {
            await AddProviderCapabilityCheckAsync(
                    runId,
                    lifecycle,
                    providerProjection,
                    ProviderCapabilityKind.FactorSchedule,
                    checks,
                    breaks,
                    missingStatus: ProviderLedgerReconciliationCheckStatusDto.Break,
                    missingCode: "PROVIDER_FACTOR_SCHEDULE_CAPABILITY_MISSING",
                    missingSeverity: ReconciliationBreakSeverity.Medium,
                    ct)
                .ConfigureAwait(false);
        }
    }

    private async Task AddProviderCapabilityCheckAsync(
        Guid runId,
        BreakLifecycleContext lifecycle,
        FundAccountBrokerageSyncActivityDto providerProjection,
        ProviderCapabilityKind capability,
        List<ProviderLedgerReconciliationCheckDto> checks,
        List<ProviderLedgerReconciliationBreakDto> breaks,
        ProviderLedgerReconciliationCheckStatusDto missingStatus,
        string missingCode,
        ReconciliationBreakSeverity missingSeverity,
        CancellationToken ct,
        string? assetClass = null,
        string? symbol = null,
        string? checkIdSuffix = null,
        string? labelSuffix = null)
    {
        ct.ThrowIfCancellationRequested();
        var routeContext = new ProviderRouteContext(
            capability,
            Workspace: "accounting",
            AccountId: providerProjection.FundAccountId,
            Symbol: symbol,
            AssetClass: assetClass,
            RequireProductionReady: capability is ProviderCapabilityKind.ReconciliationFeed);
        var result = await _capabilityRouter!.RouteAsync(routeContext, ct).ConfigureAwait(false);
        var checkId = string.IsNullOrWhiteSpace(checkIdSuffix)
            ? $"provider-capability:{capability}"
            : $"provider-capability:{capability}:{checkIdSuffix}";
        var label = string.IsNullOrWhiteSpace(labelSuffix)
            ? $"Provider capability {capability}"
            : $"Provider capability {capability} {labelSuffix}";
        var expectedSource = "provider-capability-matrix";
        var actualSource = providerProjection.Link.ProviderId;

        if (result.IsSuccess)
        {
            var selected = result.SelectedDecision;
            AddMatched(
                checks,
                checkId,
                label,
                ReconciliationBreakCategory.MissingPortfolioCoverage,
                expectedSource,
                actualSource,
                null,
                null,
                selected is null
                    ? $"Capability '{capability}' is routable for provider-ledger reconciliation."
                    : $"Capability '{capability}' is routable through connection '{selected.ConnectionId}'.");
            return;
        }

        var reason = BuildProviderCapabilityBlockReason(capability, result);
        AddBreak(
            runId,
            lifecycle,
            checks,
            breaks,
            checkId,
            label,
            missingStatus,
            missingCode,
            ReconciliationBreakCategory.MissingPortfolioCoverage,
            missingSeverity,
            expectedSource,
            actualSource,
            null,
            null,
            reason,
            evidenceLink: "/workstation/settings/providers");
    }

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

    private sealed record ProviderLedgerScope(
        LedgerBookDto Book,
        LedgerPeriodDto? Period,
        DateOnly? AsOfDate);

    private sealed record ProviderLedgerAuthorityVerification(
        bool IsVerified,
        LedgerBookDto? LedgerBook,
        string? ErrorCode,
        string? Error)
    {
        public static ProviderLedgerAuthorityVerification Verified(LedgerBookDto ledgerBook)
            => new(true, ledgerBook, null, null);

        public static ProviderLedgerAuthorityVerification Failed(string errorCode, string error)
            => new(false, null, errorCode, error);
    }

    private sealed record ProviderCaseworkPersistenceResult(
        int RequiredCount,
        int RetainedCount,
        bool IsSatisfied,
        bool IsBlocked,
        IReadOnlyList<string> CaseIds,
        string? Error);

    private sealed record ProviderLedgerReconciliationRunIntent(
        string SchemaVersion,
        string OperationId,
        Guid RunId,
        Guid AccountId,
        string RequestHashSha256,
        string? InputHashSha256,
        int AttemptNumber,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        string State,
        OperationTerminalState? TerminalState,
        string? FailureReason);

    private static readonly ProviderCapabilityKind[] RequiredProviderLedgerCapabilities =
    [
        ProviderCapabilityKind.AccountBalances,
        ProviderCapabilityKind.AccountPositions,
        ProviderCapabilityKind.ReconciliationFeed
    ];

    private static string NormalizeAssetClass(string? assetClass)
        => string.IsNullOrWhiteSpace(assetClass) ? "Unknown" : assetClass.Trim();

    private static string NormalizeCheckIdPart(string value)
        => string.Join("-", value.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Replace(':', '-')
            .Replace(' ', '-')
            .ToLowerInvariant();

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
