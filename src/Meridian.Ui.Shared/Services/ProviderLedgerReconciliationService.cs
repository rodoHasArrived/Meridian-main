using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.ProviderSdk;
using Meridian.Storage.Archival;
using Meridian.Storage.SecurityMaster;
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
    private readonly ICapabilityRouter? _capabilityRouter;
    private readonly IReconciliationBreakQueueRepository? _breakQueueRepository;
    private readonly IOperatorOverridesStore? _operatorOverridesStore;
    private readonly ISecurityMasterConflictService? _securityMasterConflictService;
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
        ISecurityMasterConflictService? securityMasterConflictService = null)
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

        var detail = new ProviderLedgerReconciliationDetailDto(
            summary,
            checks,
            breaks,
            warnings,
            evidenceLinks.Where(static link => !string.IsNullOrWhiteSpace(link)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            securityMasterPassports,
            shadowBookComparison,
            corporateActionReadiness);

        await SeedBreakQueueCasesAsync(detail, request, ct).ConfigureAwait(false);
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
                    group.Sum(static position => position.MarketValue)),
                StringComparer.OrdinalIgnoreCase) ?? [];
        var custodianByIdentifier = custodianPositions
            .Where(static position => !string.IsNullOrWhiteSpace(position.Identifier))
            .GroupBy(static position => NormalizePositionKey(position.Identifier), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => new PositionComparisonAmount(
                    group.First().Identifier.Trim(),
                    group.Sum(static position => position.IsShort ? -Math.Abs(position.Quantity) : position.Quantity),
                    group.Sum(static position => position.MarketValue)),
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
        decimal MarketValue);

    private static ProviderCorporateActionReadinessDto BuildCorporateActionReadiness(
        FundAccountBrokerageSyncActivityDto? providerProjection,
        IReadOnlyList<ProviderLedgerReconciliationCheckDto> checks,
        IReadOnlyList<ProviderSecurityMasterPassportDto> securityMasterPassports,
        IReadOnlyList<string> evidenceLinks)
    {
        if (providerProjection is null)
        {
            return new ProviderCorporateActionReadinessDto(
                ProviderCorporateActionsRoutable: false,
                Status: ProviderLedgerReconciliationCheckStatusDto.Blocked,
                PositionCount: 0,
                SecurityResolvedCount: 0,
                EquityPositionCount: 0,
                FixedIncomeOrStructuredPositionCount: 0,
                FactorScheduleCandidateCount: 0,
                IncomeCashTransactionCount: 0,
                DividendCashTransactionCount: 0,
                InterestCashTransactionCount: 0,
                RequiredFeeds: [],
                MissingFeeds: ["provider-projection"],
                Warnings: ["Corporate-action readiness is blocked until a provider projection is retained."],
                EvidenceLinks: [],
                Lines:
                [
                    new ProviderCorporateActionReadinessLineDto(
                        "provider-projection",
                        "Provider projection",
                        ProviderLedgerReconciliationCheckStatusDto.Blocked,
                        "provider-projection",
                        "provider-sync",
                        0,
                        "No brokerage sync projection exists for this fund account.")
                ]);
        }

        var positions = providerProjection.Positions;
        var corporateActionEvents = providerProjection.CorporateActions ?? [];
        var equityPositionCount = positions.Count(static position => IsEquityAssetClass(position.AssetClass));
        var fixedIncomeOrStructuredPositionCount = positions.Count(static position => IsFixedIncomeOrStructuredAssetClass(position.AssetClass));
        var incomeCashTransactionCount = providerProjection.CashTransactions.Count(static transaction => IsIncomeTransaction(transaction.TransactionType));
        var dividendCashTransactionCount = providerProjection.CashTransactions.Count(static transaction => IsDividendTransaction(transaction.TransactionType));
        var interestCashTransactionCount = providerProjection.CashTransactions.Count(static transaction => IsInterestTransaction(transaction.TransactionType));
        var principalCashTransactionCount = providerProjection.CashTransactions.Count(static transaction => IsPrincipalReturnTransaction(transaction.TransactionType));
        var providerCorporateActionEventCount = corporateActionEvents.Count;
        var amortizationScheduleEventCount = corporateActionEvents.Count(static action => IsAmortizationScheduleEvent(action.EventType));
        var factorScheduleEventCount = corporateActionEvents.Count(static action => IsFactorScheduleEvent(action.EventType));
        var loanScheduleEventCount = corporateActionEvents.Count(static action => IsLoanScheduleEvent(action.EventType));
        var factorLikeScheduleEventCount = amortizationScheduleEventCount + factorScheduleEventCount + loanScheduleEventCount;
        var positionSecurityIdentityCount = positions
            .Where(static position => !string.IsNullOrWhiteSpace(position.Symbol))
            .Select(static position => position.Symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var securityResolvedCount = securityMasterPassports.Count(static passport =>
            passport.Status is ProviderSecurityMasterPassportStatusDto.Resolved or ProviderSecurityMasterPassportStatusDto.Inferred);
        var candidateCount = equityPositionCount
            + fixedIncomeOrStructuredPositionCount
            + incomeCashTransactionCount
            + principalCashTransactionCount
            + providerCorporateActionEventCount;
        var nonFactorCorporateActionEventCount = Math.Max(0, providerCorporateActionEventCount - factorLikeScheduleEventCount);
        var unresolvedSecurityCount = Math.Max(0, positionSecurityIdentityCount - securityResolvedCount);
        var corporateActionCapability = checks.FirstOrDefault(static check =>
            string.Equals(check.CheckId, "provider-capability:CorporateActions", StringComparison.OrdinalIgnoreCase));
        var factorScheduleCapability = checks.FirstOrDefault(static check =>
            string.Equals(check.CheckId, "provider-capability:FactorSchedule", StringComparison.OrdinalIgnoreCase));
        var hasCorporateActionCapabilityEvidence = corporateActionCapability is not null;
        var hasFactorScheduleCapabilityEvidence = factorScheduleCapability is not null;
        var providerCorporateActionsRoutable =
            corporateActionCapability?.Status == ProviderLedgerReconciliationCheckStatusDto.Matched;
        var factorScheduleRoutable =
            factorScheduleCapability?.Status == ProviderLedgerReconciliationCheckStatusDto.Matched;
        var requiresCorporateActionCapability =
            equityPositionCount > 0 ||
            incomeCashTransactionCount > 0 ||
            nonFactorCorporateActionEventCount > 0;
        var requiresFactorScheduleCapability =
            fixedIncomeOrStructuredPositionCount > 0 ||
            principalCashTransactionCount > 0 ||
            factorLikeScheduleEventCount > 0;
        var hasRequiredCorporateActionCapabilityEvidence =
            !requiresCorporateActionCapability || hasCorporateActionCapabilityEvidence;
        var hasRequiredFactorScheduleCapabilityEvidence =
            !requiresFactorScheduleCapability || hasFactorScheduleCapabilityEvidence;
        var requiredProviderCapabilitiesRoutable =
            (!requiresCorporateActionCapability || providerCorporateActionsRoutable) &&
            (!requiresFactorScheduleCapability || factorScheduleRoutable);
        var requiredFeeds = new List<string>();
        var missingFeeds = new List<string>();
        var warnings = new List<string>();
        var lines = new List<ProviderCorporateActionReadinessLineDto>();
        var evidenceCandidates = new List<ProviderCorporateActionEvidenceCandidateDto>();
        var passportsBySymbol = securityMasterPassports
            .Where(static passport => !string.IsNullOrWhiteSpace(passport.Symbol))
            .GroupBy(static passport => NormalizeSymbol(passport.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (equityPositionCount > 0)
        {
            requiredFeeds.Add("splits");
            requiredFeeds.Add("dividends");
        }

        if (fixedIncomeOrStructuredPositionCount > 0)
        {
            requiredFeeds.Add("factor-schedule");
            requiredFeeds.Add("coupon-schedule");
        }

        if (incomeCashTransactionCount > 0)
        {
            requiredFeeds.Add("income-cash-activity");
        }

        if (principalCashTransactionCount > 0)
        {
            requiredFeeds.Add("principal-cash-activity");
            requiredFeeds.Add("factor-schedule");
        }

        if (providerCorporateActionEventCount > 0)
        {
            requiredFeeds.Add("provider-corporate-actions");
        }

        if (factorScheduleEventCount > 0)
        {
            requiredFeeds.Add("factor-schedule");
        }

        if (amortizationScheduleEventCount > 0)
        {
            requiredFeeds.Add("amortization-schedule");
            requiredFeeds.Add("factor-schedule");
        }

        if (loanScheduleEventCount > 0)
        {
            requiredFeeds.Add("loan-schedule");
            requiredFeeds.Add("factor-schedule");
        }

        if (candidateCount > 0 &&
            (!hasRequiredCorporateActionCapabilityEvidence || !hasRequiredFactorScheduleCapabilityEvidence))
        {
            missingFeeds.Add("provider-capability-matrix");
            warnings.Add("Provider capability routing metadata is not registered, so corporate-action feed readiness cannot be confirmed.");
        }
        else if (requiresCorporateActionCapability && !providerCorporateActionsRoutable)
        {
            missingFeeds.Add("provider-corporate-actions");
            warnings.Add("Provider corporate-action capability is not routable for this account; controller review is required before relying on split, dividend, or factor evidence.");
        }

        if (requiresFactorScheduleCapability && hasFactorScheduleCapabilityEvidence && !factorScheduleRoutable)
        {
            missingFeeds.Add("factor-schedule");
            warnings.Add("Provider factor-schedule capability is not routable for this account; fixed-income, structured, amortization, or paydown evidence requires controller review.");
        }

        if (unresolvedSecurityCount > 0)
        {
            missingFeeds.Add("security-master-identities");
            warnings.Add($"{unresolvedSecurityCount} provider position(s) are missing a resolved Security Master identity for corporate-action/factor attribution.");
        }

        lines.Add(BuildCorporateActionReadinessLine(
            "equity-corporate-actions",
            "Equity corporate actions",
            "splits,dividends",
            "provider-sync",
            equityPositionCount,
            candidateCount,
            hasCorporateActionCapabilityEvidence,
            providerCorporateActionsRoutable,
            unresolvedSecurityCount,
            "corporate-action",
            "Equity positions require split and dividend evidence before provider values can be used as accounting support."));

        lines.Add(BuildCorporateActionReadinessLine(
            "factor-schedule",
            "Factor schedule candidates",
            "factor-schedule,coupon-schedule",
            "security-master-provider",
            fixedIncomeOrStructuredPositionCount,
            candidateCount,
            hasFactorScheduleCapabilityEvidence,
            factorScheduleRoutable,
            unresolvedSecurityCount,
            "factor-schedule",
            "Fixed income and structured positions require factor, coupon, amortization, or paydown schedules for valuation support."));

        lines.Add(BuildCorporateActionReadinessLine(
            "income-cash-activity",
            "Income cash activity",
            "income-cash-activity",
            "provider-activity",
            incomeCashTransactionCount,
            candidateCount,
            hasCorporateActionCapabilityEvidence,
            providerCorporateActionsRoutable,
            unresolvedSecurityCount,
            "corporate-action",
            "Dividend, interest, coupon, and distribution cash movements require retained provider activity and Security Master attribution."));

        lines.Add(BuildCorporateActionReadinessLine(
            "principal-cash-activity",
            "Principal cash activity",
            "principal-cash-activity,factor-schedule",
            "provider-activity",
            principalCashTransactionCount,
            candidateCount,
            hasFactorScheduleCapabilityEvidence,
            factorScheduleRoutable,
            unresolvedSecurityCount,
            "factor-schedule",
            "Principal, amortization, and paydown cash movements require retained provider activity, factor schedule support, and Security Master attribution."));

        var providerEventsHaveCapabilityEvidence = nonFactorCorporateActionEventCount == 0
            ? hasFactorScheduleCapabilityEvidence
            : factorLikeScheduleEventCount == 0
                ? hasCorporateActionCapabilityEvidence
                : hasCorporateActionCapabilityEvidence && hasFactorScheduleCapabilityEvidence;
        var providerEventsRoutable = nonFactorCorporateActionEventCount == 0
            ? factorScheduleRoutable
            : factorLikeScheduleEventCount == 0
                ? providerCorporateActionsRoutable
                : providerCorporateActionsRoutable && factorScheduleRoutable;
        lines.Add(BuildCorporateActionReadinessLine(
            "provider-corporate-action-events",
            "Provider corporate-action events",
            providerCorporateActionEventCount == factorLikeScheduleEventCount && factorLikeScheduleEventCount > 0
                ? "factor-schedule"
                : "provider-corporate-actions",
            "provider-corporate-action",
            providerCorporateActionEventCount,
            candidateCount,
            providerEventsHaveCapabilityEvidence,
            providerEventsRoutable,
            unresolvedSecurityCount,
            factorLikeScheduleEventCount > 0 && nonFactorCorporateActionEventCount == 0
                ? "factor-schedule"
                : "corporate-action/factor-schedule",
            "Retained provider corporate-action, factor, and loan-schedule events are direct evidence for split, dividend, amortization, paydown, loan schedule, or factor support."));

        if (candidateCount == 0)
        {
            warnings.Add("No corporate-action-sensitive positions, provider corporate-action events, income transactions, or principal cash movements were present in the provider projection.");
        }

        var status = candidateCount == 0
            ? ProviderLedgerReconciliationCheckStatusDto.Matched
            : !hasRequiredCorporateActionCapabilityEvidence || !hasRequiredFactorScheduleCapabilityEvidence
                ? ProviderLedgerReconciliationCheckStatusDto.Blocked
                : requiredProviderCapabilitiesRoutable && unresolvedSecurityCount == 0
                    ? ProviderLedgerReconciliationCheckStatusDto.Matched
                    : ProviderLedgerReconciliationCheckStatusDto.Break;

        foreach (var position in positions)
        {
            if (!IsEquityAssetClass(position.AssetClass) &&
                !IsFixedIncomeOrStructuredAssetClass(position.AssetClass))
            {
                continue;
            }

            passportsBySymbol.TryGetValue(NormalizeSymbol(position.Symbol), out var passport);
            var candidateType = IsFixedIncomeOrStructuredAssetClass(position.AssetClass)
                ? "FactorScheduleCandidate"
                : "EquityCorporateActionCandidate";
            var requiredFeed = candidateType == "FactorScheduleCandidate"
                ? "factor-schedule,coupon-schedule"
                : "splits,dividends";
            var providerEventId = string.IsNullOrWhiteSpace(position.PositionId)
                ? position.Symbol
                : position.PositionId;
            var candidateStatus = BuildCorporateActionCandidateStatus(
                candidateType == "FactorScheduleCandidate"
                    ? hasFactorScheduleCapabilityEvidence
                    : hasCorporateActionCapabilityEvidence,
                candidateType == "FactorScheduleCandidate"
                    ? factorScheduleRoutable
                    : providerCorporateActionsRoutable,
                passport,
                requiresSecurityIdentity: true);

            evidenceCandidates.Add(new ProviderCorporateActionEvidenceCandidateDto(
                CandidateId: BuildCorporateActionCandidateId(
                    providerProjection.Link.ProviderId,
                    providerProjection.Link.ExternalAccountId,
                    candidateType,
                    providerEventId,
                    position.Symbol),
                CandidateType: candidateType,
                Symbol: NormalizeOptional(position.Symbol),
                SecurityId: passport?.SecurityId ?? position.Security?.SecurityId,
                SecurityDisplayName: passport?.SecurityDisplayName ?? position.Security?.DisplayName,
                Status: candidateStatus,
                RequiredFeed: requiredFeed,
                EvidenceSource: "provider-position",
                ProviderId: providerProjection.Link.ProviderId,
                ExternalAccountId: providerProjection.Link.ExternalAccountId,
                ProviderEventId: providerEventId,
                ObservedAt: providerProjection.SyncedAt,
                Amount: position.MarketValue,
                Quantity: position.Quantity,
                Currency: NormalizeOptional(position.Currency) ?? NormalizeOptional(providerProjection.Balance?.Currency),
                Reason: BuildCorporateActionCandidateReason(
                    candidateType,
                    candidateType == "FactorScheduleCandidate"
                        ? hasFactorScheduleCapabilityEvidence
                        : hasCorporateActionCapabilityEvidence,
                    candidateType == "FactorScheduleCandidate"
                        ? factorScheduleRoutable
                        : providerCorporateActionsRoutable,
                    passport,
                    requiresSecurityIdentity: true,
                    candidateType == "FactorScheduleCandidate" ? "factor-schedule" : "corporate-action")));
        }

        foreach (var transaction in providerProjection.CashTransactions.Where(static transaction =>
                     IsIncomeTransaction(transaction.TransactionType) ||
                     IsPrincipalReturnTransaction(transaction.TransactionType)))
        {
            passportsBySymbol.TryGetValue(NormalizeSymbol(transaction.Symbol), out var passport);
            var isPrincipalReturn = IsPrincipalReturnTransaction(transaction.TransactionType);
            var candidateType = isPrincipalReturn
                ? "PrincipalCashActivity"
                : IsDividendTransaction(transaction.TransactionType)
                    ? "DividendCashActivity"
                    : IsInterestTransaction(transaction.TransactionType)
                        ? "InterestCashActivity"
                        : "DistributionCashActivity";
            var requiresSecurityIdentity = !string.IsNullOrWhiteSpace(transaction.Symbol);
            var candidateStatus = BuildCorporateActionCandidateStatus(
                isPrincipalReturn ? hasFactorScheduleCapabilityEvidence : hasCorporateActionCapabilityEvidence,
                isPrincipalReturn ? factorScheduleRoutable : providerCorporateActionsRoutable,
                passport,
                requiresSecurityIdentity);

            evidenceCandidates.Add(new ProviderCorporateActionEvidenceCandidateDto(
                CandidateId: BuildCorporateActionCandidateId(
                    providerProjection.Link.ProviderId,
                    providerProjection.Link.ExternalAccountId,
                    candidateType,
                    transaction.TransactionId,
                    transaction.Symbol),
                CandidateType: candidateType,
                Symbol: NormalizeOptional(transaction.Symbol),
                SecurityId: passport?.SecurityId,
                SecurityDisplayName: passport?.SecurityDisplayName,
                Status: candidateStatus,
                RequiredFeed: isPrincipalReturn ? "principal-cash-activity,factor-schedule" : "income-cash-activity",
                EvidenceSource: "provider-activity",
                ProviderId: providerProjection.Link.ProviderId,
                ExternalAccountId: providerProjection.Link.ExternalAccountId,
                ProviderEventId: transaction.TransactionId,
                ObservedAt: transaction.PostedAt,
                Amount: transaction.Amount,
                Quantity: null,
                Currency: NormalizeOptional(transaction.Currency),
                Reason: BuildCorporateActionCandidateReason(
                    candidateType,
                    isPrincipalReturn ? hasFactorScheduleCapabilityEvidence : hasCorporateActionCapabilityEvidence,
                    isPrincipalReturn ? factorScheduleRoutable : providerCorporateActionsRoutable,
                    passport,
                    requiresSecurityIdentity,
                    isPrincipalReturn ? "factor-schedule" : "corporate-action")));
        }

        foreach (var action in corporateActionEvents)
        {
            passportsBySymbol.TryGetValue(NormalizeSymbol(action.Symbol), out var passport);
            var candidateType = ResolveCorporateActionEventCandidateType(action.EventType);
            var requiredFeed = ResolveCorporateActionEventRequiredFeed(action.EventType);
            var requiresSecurityIdentity = !string.IsNullOrWhiteSpace(action.Symbol);
            var isScheduleEvent = IsScheduleEvidenceCandidate(candidateType);
            var candidateStatus = BuildCorporateActionCandidateStatus(
                isScheduleEvent
                    ? hasFactorScheduleCapabilityEvidence
                    : hasCorporateActionCapabilityEvidence,
                isScheduleEvent
                    ? factorScheduleRoutable
                    : providerCorporateActionsRoutable,
                passport,
                requiresSecurityIdentity);
            var amount = action.Amount ?? action.Factor;

            evidenceCandidates.Add(new ProviderCorporateActionEvidenceCandidateDto(
                CandidateId: BuildCorporateActionCandidateId(
                    providerProjection.Link.ProviderId,
                    providerProjection.Link.ExternalAccountId,
                    candidateType,
                    action.EventId,
                    action.Symbol),
                CandidateType: candidateType,
                Symbol: NormalizeOptional(action.Symbol),
                SecurityId: passport?.SecurityId,
                SecurityDisplayName: passport?.SecurityDisplayName,
                Status: candidateStatus,
                RequiredFeed: requiredFeed,
                EvidenceSource: "provider-corporate-action",
                ProviderId: providerProjection.Link.ProviderId,
                ExternalAccountId: providerProjection.Link.ExternalAccountId,
                ProviderEventId: action.EventId,
                ObservedAt: providerProjection.SyncedAt,
                Amount: amount,
                Quantity: action.Quantity,
                Currency: NormalizeOptional(action.Currency) ?? NormalizeOptional(providerProjection.Balance?.Currency),
                Reason: BuildCorporateActionCandidateReason(
                    candidateType,
                    isScheduleEvent
                        ? hasFactorScheduleCapabilityEvidence
                        : hasCorporateActionCapabilityEvidence,
                    isScheduleEvent
                        ? factorScheduleRoutable
                        : providerCorporateActionsRoutable,
                    passport,
                    requiresSecurityIdentity,
                    isScheduleEvent ? "factor-schedule" : "corporate-action")));
        }

        var orderedCandidates = evidenceCandidates
            .OrderBy(static candidate => candidate.CandidateType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static candidate => candidate.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static candidate => candidate.ProviderEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var ledgerEffects = BuildCorporateActionLedgerEffects(orderedCandidates);
        return new ProviderCorporateActionReadinessDto(
            ProviderCorporateActionsRoutable: providerCorporateActionsRoutable,
            Status: status,
            PositionCount: positions.Count,
            SecurityResolvedCount: securityResolvedCount,
            EquityPositionCount: equityPositionCount,
            FixedIncomeOrStructuredPositionCount: fixedIncomeOrStructuredPositionCount,
            FactorScheduleCandidateCount: fixedIncomeOrStructuredPositionCount,
            IncomeCashTransactionCount: incomeCashTransactionCount,
            DividendCashTransactionCount: dividendCashTransactionCount,
            InterestCashTransactionCount: interestCashTransactionCount,
            RequiredFeeds: requiredFeeds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MissingFeeds: missingFeeds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings: warnings.ToArray(),
            EvidenceLinks: evidenceLinks.Where(static link => !string.IsNullOrWhiteSpace(link)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Lines: lines,
            ProviderCorporateActionEventCount: providerCorporateActionEventCount,
            FactorScheduleEventCount: factorScheduleEventCount,
            FactorScheduleRoutable: factorScheduleRoutable,
            LoanScheduleEventCount: loanScheduleEventCount)
        {
            EvidenceCandidates = orderedCandidates,
            LedgerEffects = ledgerEffects,
            SecurityMasterScheduleFeeds = BuildSecurityMasterScheduleFeeds(orderedCandidates, ledgerEffects),
            PrincipalCashTransactionCount = principalCashTransactionCount,
            AmortizationScheduleEventCount = amortizationScheduleEventCount
        };
    }

    private static IReadOnlyList<ProviderCorporateActionLedgerEffectDto> BuildCorporateActionLedgerEffects(
        IReadOnlyList<ProviderCorporateActionEvidenceCandidateDto> candidates)
        => candidates
            .Select(BuildCorporateActionLedgerEffect)
            .Where(static effect => effect is not null)
            .Cast<ProviderCorporateActionLedgerEffectDto>()
            .ToArray();

    private static bool IsScheduleEvidenceCandidate(string candidateType) =>
        string.Equals(candidateType, "FactorScheduleEvent", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(candidateType, "AmortizationScheduleEvent", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(candidateType, "LoanScheduleEvent", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ProviderSecurityMasterScheduleFeedDto> BuildSecurityMasterScheduleFeeds(
        IReadOnlyList<ProviderCorporateActionEvidenceCandidateDto> candidates,
        IReadOnlyList<ProviderCorporateActionLedgerEffectDto> ledgerEffects)
    {
        var candidatesById = candidates.ToDictionary(static candidate => candidate.CandidateId, StringComparer.OrdinalIgnoreCase);
        return ledgerEffects
            .Where(static effect => IsSecurityMasterScheduleFeedEffect(effect.LedgerEffectKind))
            .Select(effect =>
            {
                candidatesById.TryGetValue(effect.CandidateId, out var candidate);
                var canUpdateSecurityMaster = effect.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                    effect.SecurityId.HasValue &&
                    !string.Equals(effect.LedgerEffectKind, "FactorScheduleCoverageCandidate", StringComparison.OrdinalIgnoreCase);
                var canSupportLedgerValuation = effect.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                    !string.Equals(effect.LedgerEffectKind, "FactorScheduleCoverageCandidate", StringComparison.OrdinalIgnoreCase);
                return new ProviderSecurityMasterScheduleFeedDto(
                    FeedId: $"security-master-feed:{NormalizeBreakIdPart(effect.CandidateId)}",
                    CandidateId: effect.CandidateId,
                    CandidateType: effect.CandidateType,
                    FeedKind: MapSecurityMasterScheduleFeedKind(effect.LedgerEffectKind),
                    RequiredFeed: candidate?.RequiredFeed ?? ResolveRequiredFeedFromLedgerEffect(effect.LedgerEffectKind),
                    EvidenceSource: candidate?.EvidenceSource ?? "provider-ledger",
                    ProviderId: candidate?.ProviderId ?? "unknown-provider",
                    ExternalAccountId: candidate?.ExternalAccountId ?? "unknown-account",
                    ProviderEventId: effect.ProviderEventId,
                    Symbol: effect.Symbol,
                    SecurityId: effect.SecurityId,
                    EffectiveDate: effect.EffectiveDate,
                    Factor: effect.Factor,
                    CashAmount: effect.CashAmount,
                    PrincipalAmount: effect.PrincipalAmount,
                    IncomeAmount: effect.IncomeAmount,
                    Currency: effect.Currency,
                    LedgerEffectKind: effect.LedgerEffectKind,
                    Status: effect.Status,
                    CanUpdateSecurityMaster: canUpdateSecurityMaster,
                    CanSupportLedgerValuation: canSupportLedgerValuation,
                    Reason: BuildSecurityMasterScheduleFeedReason(effect, canUpdateSecurityMaster, canSupportLedgerValuation));
            })
            .OrderBy(static feed => feed.EffectiveDate)
            .ThenBy(static feed => feed.FeedKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static feed => feed.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSecurityMasterScheduleFeedEffect(string ledgerEffectKind) =>
        string.Equals(ledgerEffectKind, "FactorScheduleValuationInput", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "AmortizationScheduleValuationInput", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "LoanScheduleValuationInput", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "CorporateActionCoverageInput", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "DividendIncomeRecognition", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "DistributionIncomeRecognition", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "CashIncomeRecognition", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "PrincipalReturnRecognition", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ledgerEffectKind, "FactorScheduleCoverageCandidate", StringComparison.OrdinalIgnoreCase);

    private static string MapSecurityMasterScheduleFeedKind(string ledgerEffectKind) =>
        ledgerEffectKind switch
        {
            "FactorScheduleValuationInput" => "SecurityMasterFactorHistory",
            "AmortizationScheduleValuationInput" => "SecurityMasterAmortizationSchedule",
            "LoanScheduleValuationInput" => "SecurityMasterLoanSchedule",
            "CorporateActionCoverageInput" => "SecurityMasterCorporateAction",
            "DividendIncomeRecognition" or "DistributionIncomeRecognition" or "CashIncomeRecognition" => "SecurityMasterIncomeSchedule",
            "PrincipalReturnRecognition" => "SecurityMasterPrincipalSchedule",
            "FactorScheduleCoverageCandidate" => "SecurityMasterFactorCoverageRequirement",
            _ => "SecurityMasterScheduleEvidence"
        };

    private static string ResolveRequiredFeedFromLedgerEffect(string ledgerEffectKind) =>
        ledgerEffectKind switch
        {
            "FactorScheduleValuationInput" or "FactorScheduleCoverageCandidate" => "factor-schedule",
            "AmortizationScheduleValuationInput" => "amortization-schedule,factor-schedule",
            "LoanScheduleValuationInput" => "loan-schedule,factor-schedule",
            "DividendIncomeRecognition" or "DistributionIncomeRecognition" or "CashIncomeRecognition" => "income-cash-activity",
            "PrincipalReturnRecognition" => "principal-cash-activity,factor-schedule",
            "CorporateActionCoverageInput" => "provider-corporate-actions",
            _ => "provider-ledger"
        };

    private static string BuildSecurityMasterScheduleFeedReason(
        ProviderCorporateActionLedgerEffectDto effect,
        bool canUpdateSecurityMaster,
        bool canSupportLedgerValuation)
    {
        if (canUpdateSecurityMaster && canSupportLedgerValuation)
        {
            return $"{effect.LedgerEffectKind} is matched, Security Master-attributed, and ready to feed schedule/factor history plus downstream ledger valuation support.";
        }

        if (!effect.SecurityId.HasValue)
        {
            return $"{effect.LedgerEffectKind} cannot update Security Master schedule history until the provider evidence resolves to a Security Master identity.";
        }

        if (string.Equals(effect.LedgerEffectKind, "FactorScheduleCoverageCandidate", StringComparison.OrdinalIgnoreCase))
        {
            return "Fixed-income or structured holdings require provider factor/coupon schedule evidence before Security Master history or ledger valuation can be updated.";
        }

        return $"{effect.LedgerEffectKind} requires controller review before it can feed Security Master schedule history or ledger valuation support.";
    }

    private static ProviderCorporateActionLedgerEffectDto? BuildCorporateActionLedgerEffect(
        ProviderCorporateActionEvidenceCandidateDto candidate)
    {
        var eventDate = DateOnly.FromDateTime(candidate.ObservedAt.UtcDateTime);
        if (string.Equals(candidate.CandidateType, "FactorScheduleEvent", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderCorporateActionLedgerEffectDto(
                candidate.CandidateId,
                candidate.CandidateType,
                candidate.Symbol,
                candidate.SecurityId,
                candidate.ProviderEventId,
                "FactorScheduleValuationInput",
                eventDate,
                Factor: candidate.Amount,
                CashAmount: null,
                PrincipalAmount: null,
                IncomeAmount: null,
                candidate.Currency,
                candidate.Status,
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                    ? "Retained provider factor evidence can feed Security Master factor history and downstream ledger valuation; journal amount generation still requires par and prior-factor context."
                    : candidate.Reason,
                JournalLines: []);
        }

        if (string.Equals(candidate.CandidateType, "AmortizationScheduleEvent", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderCorporateActionLedgerEffectDto(
                candidate.CandidateId,
                candidate.CandidateType,
                candidate.Symbol,
                candidate.SecurityId,
                candidate.ProviderEventId,
                "AmortizationScheduleValuationInput",
                eventDate,
                Factor: candidate.Quantity,
                CashAmount: candidate.Amount,
                PrincipalAmount: candidate.Amount,
                IncomeAmount: null,
                candidate.Currency,
                candidate.Status,
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                    ? "Retained provider amortization schedule evidence can feed Security Master amortization history and downstream ledger valuation; final journal generation still requires amortization policy context."
                    : candidate.Reason,
                JournalLines: []);
        }

        if (string.Equals(candidate.CandidateType, "LoanScheduleEvent", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderCorporateActionLedgerEffectDto(
                candidate.CandidateId,
                candidate.CandidateType,
                candidate.Symbol,
                candidate.SecurityId,
                candidate.ProviderEventId,
                "LoanScheduleValuationInput",
                eventDate,
                Factor: candidate.Quantity,
                CashAmount: candidate.Amount,
                PrincipalAmount: candidate.Amount,
                IncomeAmount: null,
                candidate.Currency,
                candidate.Status,
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                    ? "Retained provider loan schedule evidence can feed Security Master schedule history and downstream ledger valuation; final journal generation still requires amortization policy context."
                    : candidate.Reason,
                JournalLines: []);
        }

        if (string.Equals(candidate.CandidateType, "FactorScheduleCandidate", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderCorporateActionLedgerEffectDto(
                candidate.CandidateId,
                candidate.CandidateType,
                candidate.Symbol,
                candidate.SecurityId,
                candidate.ProviderEventId,
                "FactorScheduleCoverageCandidate",
                eventDate,
                Factor: null,
                CashAmount: null,
                PrincipalAmount: null,
                IncomeAmount: null,
                candidate.Currency,
                candidate.Status,
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                    ? "Fixed-income or structured position requires factor/coupon schedule coverage before final ledger valuation support."
                    : candidate.Reason,
                JournalLines: []);
        }

        if (string.Equals(candidate.CandidateType, "InterestCashActivity", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCashIncomeLedgerEffect(
                candidate,
                eventDate,
                "CashIncomeRecognition",
                "Coupon Income");
        }

        if (string.Equals(candidate.CandidateType, "PrincipalCashActivity", StringComparison.OrdinalIgnoreCase))
        {
            return BuildPrincipalCashLedgerEffect(candidate, eventDate);
        }

        if (string.Equals(candidate.CandidateType, "DividendCashActivity", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.CandidateType, "DistributionCashActivity", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCashIncomeLedgerEffect(
                candidate,
                eventDate,
                string.Equals(candidate.CandidateType, "DividendCashActivity", StringComparison.OrdinalIgnoreCase)
                    ? "DividendIncomeRecognition"
                    : "DistributionIncomeRecognition",
                "Dividend Income");
        }

        if (string.Equals(candidate.CandidateType, "EquityCorporateActionCandidate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.CandidateType, "CorporateActionEvent", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderCorporateActionLedgerEffectDto(
                candidate.CandidateId,
                candidate.CandidateType,
                candidate.Symbol,
                candidate.SecurityId,
                candidate.ProviderEventId,
                "CorporateActionCoverageInput",
                eventDate,
                Factor: null,
                CashAmount: candidate.Amount,
                PrincipalAmount: null,
                IncomeAmount: null,
                candidate.Currency,
                candidate.Status,
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                    ? "Retained provider corporate-action evidence can support Security Master event review and downstream ledger valuation."
                    : candidate.Reason,
                JournalLines: []);
        }

        return null;
    }

    private static ProviderCorporateActionLedgerEffectDto BuildCashIncomeLedgerEffect(
        ProviderCorporateActionEvidenceCandidateDto candidate,
        DateOnly eventDate,
        string ledgerEffectKind,
        string incomeAccount)
    {
        var amount = candidate.Amount;
        var journalLines = candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched && amount.HasValue
            ? new[]
            {
                new ExpectedJournalPreviewLineDto("Cash", "Asset", null, Math.Abs(amount.Value), 0m),
                new ExpectedJournalPreviewLineDto(incomeAccount, "Revenue", candidate.Symbol, 0m, Math.Abs(amount.Value))
            }
            : [];
        return new ProviderCorporateActionLedgerEffectDto(
            candidate.CandidateId,
            candidate.CandidateType,
            candidate.Symbol,
            candidate.SecurityId,
            candidate.ProviderEventId,
            ledgerEffectKind,
            eventDate,
            Factor: null,
            CashAmount: amount,
            PrincipalAmount: null,
            IncomeAmount: amount,
            candidate.Currency,
            candidate.Status,
            candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                ? "Retained provider income cash activity has enough Security Master attribution to preview the expected cash/income journal support."
                : candidate.Reason,
            journalLines);
    }

    private static ProviderCorporateActionLedgerEffectDto BuildPrincipalCashLedgerEffect(
        ProviderCorporateActionEvidenceCandidateDto candidate,
        DateOnly eventDate)
    {
        var amount = candidate.Amount;
        var journalLines = candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched && amount.HasValue
            ? new[]
            {
                new ExpectedJournalPreviewLineDto("Cash", "Asset", null, Math.Abs(amount.Value), 0m),
                new ExpectedJournalPreviewLineDto("Investment Principal", "Asset", candidate.Symbol, 0m, Math.Abs(amount.Value))
            }
            : [];
        return new ProviderCorporateActionLedgerEffectDto(
            candidate.CandidateId,
            candidate.CandidateType,
            candidate.Symbol,
            candidate.SecurityId,
            candidate.ProviderEventId,
            "PrincipalReturnRecognition",
            eventDate,
            Factor: null,
            CashAmount: amount,
            PrincipalAmount: amount,
            IncomeAmount: null,
            candidate.Currency,
            candidate.Status,
            candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched
                ? "Retained provider principal, amortization, or paydown activity has enough Security Master attribution to preview the expected cash/principal journal support."
                : candidate.Reason,
            journalLines);
    }

    private static ProviderCorporateActionReadinessLineDto BuildCorporateActionReadinessLine(
        string dimension,
        string label,
        string requiredFeed,
        string evidenceSource,
        int count,
        int candidateCount,
        bool hasCapabilityEvidence,
        bool providerCorporateActionsRoutable,
        int unresolvedSecurityCount,
        string capabilityLabel,
        string reason)
    {
        if (count == 0)
        {
            return new ProviderCorporateActionReadinessLineDto(
                dimension,
                label,
                ProviderLedgerReconciliationCheckStatusDto.Matched,
                requiredFeed,
                evidenceSource,
                count,
                $"{reason} No matching provider records were present in this projection.");
        }

        if (!hasCapabilityEvidence)
        {
            return new ProviderCorporateActionReadinessLineDto(
                dimension,
                label,
                ProviderLedgerReconciliationCheckStatusDto.Blocked,
                requiredFeed,
                evidenceSource,
                count,
                $"{reason} Provider capability routing metadata is unavailable for {candidateCount} corporate-action-sensitive record(s).");
        }

        if (!providerCorporateActionsRoutable)
        {
            return new ProviderCorporateActionReadinessLineDto(
                dimension,
                label,
                ProviderLedgerReconciliationCheckStatusDto.Break,
                requiredFeed,
                evidenceSource,
                count,
                $"{reason} Provider {capabilityLabel} capability is not routable for this account.");
        }

        if (unresolvedSecurityCount > 0)
        {
            return new ProviderCorporateActionReadinessLineDto(
                dimension,
                label,
                ProviderLedgerReconciliationCheckStatusDto.Break,
                requiredFeed,
                evidenceSource,
                count,
                $"{reason} {unresolvedSecurityCount} position(s) need Security Master identity resolution before attribution is complete.");
        }

        return new ProviderCorporateActionReadinessLineDto(
            dimension,
            label,
            ProviderLedgerReconciliationCheckStatusDto.Matched,
            requiredFeed,
            evidenceSource,
            count,
            $"{reason} Provider capability and Security Master identity coverage are available.");
    }

    private static ProviderLedgerReconciliationCheckStatusDto BuildCorporateActionCandidateStatus(
        bool hasCapabilityEvidence,
        bool providerCorporateActionsRoutable,
        ProviderSecurityMasterPassportDto? passport,
        bool requiresSecurityIdentity)
    {
        if (!hasCapabilityEvidence)
        {
            return ProviderLedgerReconciliationCheckStatusDto.Blocked;
        }

        if (!providerCorporateActionsRoutable)
        {
            return ProviderLedgerReconciliationCheckStatusDto.Break;
        }

        return requiresSecurityIdentity && !IsResolvedSecurityPassport(passport)
            ? ProviderLedgerReconciliationCheckStatusDto.Break
            : ProviderLedgerReconciliationCheckStatusDto.Matched;
    }

    private static string BuildCorporateActionCandidateReason(
        string candidateType,
        bool hasCapabilityEvidence,
        bool providerCorporateActionsRoutable,
        ProviderSecurityMasterPassportDto? passport,
        bool requiresSecurityIdentity,
        string capabilityLabel)
    {
        if (!hasCapabilityEvidence)
        {
            return $"{candidateType} cannot be promoted until provider capability routing metadata is available.";
        }

        if (!providerCorporateActionsRoutable)
        {
            return $"{candidateType} requires controller review because provider {capabilityLabel} capability is not routable.";
        }

        if (passport is not null && IsResolvedSecurityPassport(passport))
        {
            return $"{candidateType} has provider evidence and resolved Security Master attribution.";
        }

        if (!requiresSecurityIdentity)
        {
            return $"{candidateType} has provider account-level income evidence and no provider symbol was supplied for Security Master attribution.";
        }

        return $"{candidateType} has provider evidence but needs Security Master identity attribution.";
    }

    private static bool IsResolvedSecurityPassport(ProviderSecurityMasterPassportDto? passport)
        => passport?.Status is ProviderSecurityMasterPassportStatusDto.Resolved or ProviderSecurityMasterPassportStatusDto.Inferred;

    private static string BuildCorporateActionCandidateId(
        string providerId,
        string externalAccountId,
        string candidateType,
        string? providerEventId,
        string? symbol)
        => string.Join(
            ":",
            "provider-corporate-action",
            NormalizeCandidateToken(providerId, "provider"),
            NormalizeCandidateToken(externalAccountId, "account"),
            NormalizeCandidateToken(candidateType, "candidate"),
            NormalizeCandidateToken(providerEventId, "event"),
            NormalizeCandidateToken(symbol, "symbol"));

    private static string NormalizeCandidateToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().Replace(' ', '-').ToLowerInvariant();
    }

    private static string NormalizeSymbol(string? symbol)
        => string.IsNullOrWhiteSpace(symbol) ? string.Empty : symbol.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsIncomeTransaction(string transactionType)
    {
        if (string.IsNullOrWhiteSpace(transactionType))
        {
            return false;
        }

        return transactionType.Contains("dividend", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("interest", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("coupon", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("income", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("distribution", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrincipalReturnTransaction(string transactionType)
        => !string.IsNullOrWhiteSpace(transactionType) &&
           (transactionType.Contains("principal", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("paydown", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("amortization", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("amortisation", StringComparison.OrdinalIgnoreCase));

    private static bool IsDividendTransaction(string transactionType)
        => !string.IsNullOrWhiteSpace(transactionType) &&
           (transactionType.Contains("dividend", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("distribution", StringComparison.OrdinalIgnoreCase));

    private static bool IsInterestTransaction(string transactionType)
        => !string.IsNullOrWhiteSpace(transactionType) &&
           (transactionType.Contains("interest", StringComparison.OrdinalIgnoreCase) ||
            transactionType.Contains("coupon", StringComparison.OrdinalIgnoreCase));

    private static bool IsFactorScheduleEvent(string eventType)
        => !string.IsNullOrWhiteSpace(eventType) &&
           !IsLoanScheduleEvent(eventType) &&
           !IsAmortizationScheduleEvent(eventType) &&
           (eventType.Contains("factor", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("paydown", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("principal", StringComparison.OrdinalIgnoreCase));

    private static bool IsAmortizationScheduleEvent(string eventType)
        => !string.IsNullOrWhiteSpace(eventType) &&
           !IsLoanScheduleEvent(eventType) &&
           (eventType.Contains("amortization", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("amortisation", StringComparison.OrdinalIgnoreCase));

    private static bool IsLoanScheduleEvent(string eventType)
        => !string.IsNullOrWhiteSpace(eventType) &&
           eventType.Contains("loan", StringComparison.OrdinalIgnoreCase) &&
           eventType.Contains("schedule", StringComparison.OrdinalIgnoreCase);

    private static string ResolveCorporateActionEventCandidateType(string eventType)
    {
        if (IsLoanScheduleEvent(eventType))
        {
            return "LoanScheduleEvent";
        }

        if (IsAmortizationScheduleEvent(eventType))
        {
            return "AmortizationScheduleEvent";
        }

        if (IsFactorScheduleEvent(eventType))
        {
            return "FactorScheduleEvent";
        }

        if (!string.IsNullOrWhiteSpace(eventType) &&
            eventType.Contains("split", StringComparison.OrdinalIgnoreCase))
        {
            return "SplitCorporateActionEvent";
        }

        return IsDividendTransaction(eventType)
            ? "DividendCorporateActionEvent"
            : "ProviderCorporateActionEvent";
    }

    private static string ResolveCorporateActionEventRequiredFeed(string eventType)
    {
        if (IsLoanScheduleEvent(eventType))
        {
            return "loan-schedule,factor-schedule";
        }

        if (IsAmortizationScheduleEvent(eventType))
        {
            return "amortization-schedule,factor-schedule";
        }

        if (IsFactorScheduleEvent(eventType))
        {
            return "factor-schedule";
        }

        if (!string.IsNullOrWhiteSpace(eventType) &&
            eventType.Contains("split", StringComparison.OrdinalIgnoreCase))
        {
            return "splits";
        }

        return IsDividendTransaction(eventType)
            ? "dividends"
            : "provider-corporate-actions";
    }

    private static bool IsEquityAssetClass(string assetClass)
        => !string.IsNullOrWhiteSpace(assetClass) &&
           (assetClass.Contains("equity", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("stock", StringComparison.OrdinalIgnoreCase));

    private static bool IsFixedIncomeOrStructuredAssetClass(string assetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass))
        {
            return false;
        }

        return assetClass.Contains("bond", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("fixed", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("treasury", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("mbs", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("abs", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("loan", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("structured", StringComparison.OrdinalIgnoreCase) ||
            assetClass.Contains("mortgage", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SeedBreakQueueCasesAsync(
        ProviderLedgerReconciliationDetailDto detail,
        ProviderLedgerReconciliationRequestDto request,
        CancellationToken ct)
    {
        if (_breakQueueRepository is null)
        {
            return;
        }

        foreach (var breakRow in detail.Breaks)
        {
            ct.ThrowIfCancellationRequested();
            var item = BuildBreakQueueItem(detail, breakRow, request);
            var created = await _breakQueueRepository.CreateIfMissingAsync(item, ct).ConfigureAwait(false);
            if (!created && breakRow.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.SignedOff)
            {
                await ApplySignedOffStateToExistingCaseAsync(item, breakRow, request, ct).ConfigureAwait(false);
            }
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
                await _breakQueueRepository
                    .CreateIfMissingAsync(BuildCorporateActionCandidateCase(detail, candidate, ledgerEffect, request), ct)
                    .ConfigureAwait(false);
            }
        }

        if (detail.SecurityMasterPassports?.Count > 0)
        {
            foreach (var passport in detail.SecurityMasterPassports.Where(IsStaleResolvedSecurityMasterPassport))
            {
                ct.ThrowIfCancellationRequested();
                await _breakQueueRepository
                    .CreateIfMissingAsync(BuildStaleSecurityMasterPassportCase(detail, passport, request), ct)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ApplySignedOffStateToExistingCaseAsync(
        ReconciliationBreakQueueItem desired,
        ProviderLedgerReconciliationBreakDto breakRow,
        ProviderLedgerReconciliationRequestDto request,
        CancellationToken ct)
    {
        if (_breakQueueRepository is null)
        {
            return;
        }

        var existing = await _breakQueueRepository.GetByIdAsync(desired.BreakId, ct).ConfigureAwait(false);
        if (existing is null ||
            existing.Status is ReconciliationBreakQueueStatus.Resolved
                or ReconciliationBreakQueueStatus.Dismissed
                or ReconciliationBreakQueueStatus.SignedOff)
        {
            return;
        }

        var actor = NormalizeOwner(breakRow.SignedOffBy) ??
            NormalizeOwner(request.SignedOffBy) ??
            NormalizeOwner(request.RequestedBy) ??
            DefaultActor;

        if (existing.Status == ReconciliationBreakQueueStatus.Open)
        {
            var review = await _breakQueueRepository.StartReviewAsync(
                    new ReviewReconciliationBreakRequest(
                        existing.BreakId,
                        existing.AssignedTo ?? actor,
                        actor,
                        "Provider-ledger reconciliation break selected for sign-off.",
                        existing.Team),
                    ct)
                .ConfigureAwait(false);
            if (review.Status != ReconciliationBreakQueueTransitionStatus.Success)
            {
                _logger.LogWarning(
                    "Provider-ledger reconciliation could not start review for break case {BreakId}: {Error}",
                    existing.BreakId,
                    review.Error);
                return;
            }
        }

        var resolve = await _breakQueueRepository.ResolveAsync(
                new ResolveReconciliationBreakRequest(
                    existing.BreakId,
                    ReconciliationBreakQueueStatus.Resolved,
                    actor,
                    "Provider-ledger reconciliation break signed off.",
                    $"Signed off break key {breakRow.BreakKey ?? breakRow.CheckId}."),
                ct)
            .ConfigureAwait(false);
        if (resolve.Status != ReconciliationBreakQueueTransitionStatus.Success)
        {
            _logger.LogWarning(
                "Provider-ledger reconciliation could not resolve signed-off break case {BreakId}: {Error}",
                existing.BreakId,
                resolve.Error);
            return;
        }

        if (resolve.Item is null)
        {
            return;
        }

        var signOff = await _breakQueueRepository.ApplyCaseworkCommandAsync(
                new ReconciliationCaseworkCommand(
                    existing.BreakId,
                    ReconciliationCaseworkAction.SignOff,
                    actor,
                    $"provider-ledger-signoff:{existing.BreakId}:{breakRow.BreakKey ?? breakRow.CheckId}",
                    existing.RunId,
                    "provider-ledger-reconciliation",
                    resolve.Item.Version,
                    Reason: "Provider-ledger reconciliation request carried a controller sign-off.",
                    Note: "Provider-ledger reconciliation break signed off.",
                    Privileged: string.Equals(resolve.Item.ResolvedBy, actor, StringComparison.OrdinalIgnoreCase)),
                ct)
            .ConfigureAwait(false);
        if (signOff.Status != ReconciliationBreakQueueTransitionStatus.Success)
        {
            _logger.LogWarning(
                "Provider-ledger reconciliation could not sign off break case {BreakId}: {Error}",
                existing.BreakId,
                signOff.Error);
        }
    }

    private static ReconciliationBreakQueueItem BuildBreakQueueItem(
        ProviderLedgerReconciliationDetailDto detail,
        ProviderLedgerReconciliationBreakDto breakRow,
        ProviderLedgerReconciliationRequestDto request)
    {
        var summary = detail.Summary;
        var signedOff = breakRow.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.SignedOff;
        var signedOffBy = NormalizeOwner(breakRow.SignedOffBy) ??
            NormalizeOwner(request.SignedOffBy) ??
            NormalizeOwner(request.RequestedBy);
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
        var passport = isSecurityMasterIdentityCase
            ? FindPassportForBreak(detail, breakRow)
            : null;

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
            BreakExplanation: BuildBreakExplanation(summary, breakRow, latestRoute, syncCursor, passport));
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
        ProviderLedgerReconciliationRequestDto request)
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

        return new ReconciliationBreakQueueItem(
            BreakId: BuildCorporateActionCandidateCaseId(summary.AccountId, candidate),
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
            BreakExplanation: BuildCorporateActionCandidateBreakExplanation(summary, candidate, reason, latestRoute, syncCursor));
    }

    private static ReconciliationBreakQueueItem BuildStaleSecurityMasterPassportCase(
        ProviderLedgerReconciliationDetailDto detail,
        ProviderSecurityMasterPassportDto passport,
        ProviderLedgerReconciliationRequestDto request)
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

        return new ReconciliationBreakQueueItem(
            BreakId: BuildStaleSecurityMasterPassportCaseId(summary.AccountId, passport),
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
            BreakExplanation: BuildStaleSecurityMasterPassportBreakExplanation(passport, latestRoute, syncCursor));
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

    private async Task AddSecurityCoverageChecksAsync(
        Guid runId,
        BreakLifecycleContext lifecycle,
        FundAccountBrokerageSyncActivityDto providerProjection,
        string? requestedBy,
        List<ProviderLedgerReconciliationCheckDto> checks,
        List<ProviderLedgerReconciliationBreakDto> breaks,
        List<ProviderSecurityMasterPassportDto> securityMasterPassports,
        DateTimeOffset observedAt,
        int providerStaleAfterMinutes,
        CancellationToken ct)
    {
        var positions = providerProjection.Positions
            .Where(static position => !string.IsNullOrWhiteSpace(position.Symbol))
            .GroupBy(static position => position.Symbol.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        IReadOnlyList<SecurityMasterConflict> openIdentifierConflicts = _securityMasterConflictService is null
            ? Array.Empty<SecurityMasterConflict>()
            : await _securityMasterConflictService.GetOpenConflictsAsync(ct).ConfigureAwait(false);

        foreach (var position in positions)
        {
            ct.ThrowIfCancellationRequested();
            var symbol = position.Symbol.Trim().ToUpperInvariant();
            var checkId = $"security-master:{symbol}";

            if (position.Security is not null)
            {
                var overrideHistory = await GetSecurityMasterOverrideHistoryAsync(position.Security.SecurityId, ct)
                    .ConfigureAwait(false);
                if (AddInactiveSecurityMasterBreak(
                    runId,
                    lifecycle,
                    providerProjection,
                    position,
                    position.Security,
                    checks,
                    breaks,
                    securityMasterPassports,
                    checkId,
                    symbol,
                    observedAt,
                    providerStaleAfterMinutes,
                    overrideHistory,
                    openIdentifierConflicts))
                {
                    continue;
                }

                securityMasterPassports.Add(BuildSecurityMasterPassport(
                    providerProjection,
                    position,
                    position.Security,
                    validation: null,
                    status: MapPassportStatus(position.Security),
                    confidenceScore: position.Security.IsInferredMatch ? 85m : 100m,
                    resolutionSource: "provider-position",
                    reason: "Provider position already carries a resolved Security Master reference.",
                    observedAt: observedAt,
                    providerStaleAfterMinutes: providerStaleAfterMinutes,
                    overrideHistory: overrideHistory,
                    openIdentifierConflicts: openIdentifierConflicts));
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
                var overrideHistory = await GetSecurityMasterOverrideHistoryAsync(resolved.SecurityId, ct)
                    .ConfigureAwait(false);
                if (AddInactiveSecurityMasterBreak(
                    runId,
                    lifecycle,
                    providerProjection,
                    position,
                    resolved,
                    checks,
                    breaks,
                    securityMasterPassports,
                    checkId,
                    symbol,
                    observedAt,
                    providerStaleAfterMinutes,
                    overrideHistory,
                    openIdentifierConflicts))
                {
                    continue;
                }

                securityMasterPassports.Add(BuildSecurityMasterPassport(
                    providerProjection,
                    position,
                    resolved,
                    validation: null,
                    status: MapPassportStatus(resolved),
                    confidenceScore: resolved.IsInferredMatch ? 80m : 90m,
                    resolutionSource: "security-master-lookup",
                    reason: "Provider position resolved through the shared Security Master lookup.",
                    observedAt: observedAt,
                    providerStaleAfterMinutes: providerStaleAfterMinutes,
                    overrideHistory: overrideHistory,
                    openIdentifierConflicts: openIdentifierConflicts));
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
                    var overrideHistory = await GetSecurityMasterOverrideHistoryAsync(validation.SecurityId, ct)
                        .ConfigureAwait(false);
                    securityMasterPassports.Add(BuildSecurityMasterPassport(
                        providerProjection,
                        position,
                        security: null,
                        validation: validation,
                        status: ProviderSecurityMasterPassportStatusDto.Resolved,
                        confidenceScore: 80m,
                        resolutionSource: "security-validation-gate",
                        reason: "Security Master validation accepted the provider position.",
                        observedAt: observedAt,
                        providerStaleAfterMinutes: providerStaleAfterMinutes,
                        overrideHistory: overrideHistory,
                        openIdentifierConflicts: openIdentifierConflicts));
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
                    observedAt: observedAt,
                    providerStaleAfterMinutes: providerStaleAfterMinutes,
                    overrideHistory: [],
                    openIdentifierConflicts: openIdentifierConflicts));
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
                    observedAt: observedAt,
                    providerStaleAfterMinutes: providerStaleAfterMinutes,
                    overrideHistory: [],
                    openIdentifierConflicts: openIdentifierConflicts));
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

    private static bool AddInactiveSecurityMasterBreak(
        Guid runId,
        BreakLifecycleContext lifecycle,
        FundAccountBrokerageSyncActivityDto providerProjection,
        FundAccountBrokeragePositionDto position,
        WorkstationSecurityReference security,
        List<ProviderLedgerReconciliationCheckDto> checks,
        List<ProviderLedgerReconciliationBreakDto> breaks,
        List<ProviderSecurityMasterPassportDto> securityMasterPassports,
        string checkId,
        string symbol,
        DateTimeOffset observedAt,
        int providerStaleAfterMinutes,
        IReadOnlyList<string> overrideHistory,
        IReadOnlyList<SecurityMasterConflict> openIdentifierConflicts)
    {
        if (security.Status == SecurityStatusDto.Active)
        {
            return false;
        }

        var reason = $"Security Master reference for provider position '{symbol}' is {security.Status}; active approved Security Master status is required for ledger posting and close readiness.";
        securityMasterPassports.Add(BuildSecurityMasterPassport(
            providerProjection,
            position,
            security,
            validation: null,
            status: ProviderSecurityMasterPassportStatusDto.Blocked,
            confidenceScore: 0m,
            resolutionSource: "security-master-status",
            reason: reason,
            observedAt: observedAt,
            providerStaleAfterMinutes: providerStaleAfterMinutes,
            overrideHistory: overrideHistory,
            openIdentifierConflicts: openIdentifierConflicts));

        AddBreak(
            runId,
            lifecycle,
            checks,
            breaks,
            checkId,
            $"Security Master identity for {symbol}",
            ProviderLedgerReconciliationCheckStatusDto.Blocked,
            "SM_SECURITY_NOT_ACTIVE",
            ReconciliationBreakCategory.ClassificationGap,
            ReconciliationBreakSeverity.Critical,
            "active-security-master",
            "provider-sync",
            null,
            null,
            reason,
            symbol,
            "/workstation/data/security-master");
        return true;
    }

    private static string BuildProviderCapabilityBlockReason(
        ProviderCapabilityKind capability,
        ProviderRouteResult result)
    {
        var reason = string.IsNullOrWhiteSpace(result.PolicyGate)
            ? $"Provider capability '{capability}' is not routable for provider-ledger reconciliation."
            : result.PolicyGate;
        var skipped = result.SkippedCandidates
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Take(3)
            .ToArray();

        return skipped.Length == 0
            ? reason
            : $"{reason} Skipped: {string.Join(" ", skipped)}";
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
        DateTimeOffset observedAt,
        int providerStaleAfterMinutes,
        IReadOnlyList<string> overrideHistory,
        IReadOnlyList<SecurityMasterConflict> openIdentifierConflicts)
    {
        var issues = validation?.Report.Issues ?? [];
        var securityId = security?.SecurityId ?? validation?.SecurityId;
        var openConflictSummaries = FormatOpenIdentifierConflicts(securityId, openIdentifierConflicts);
        var identifierConflicts = issues
            .Where(static issue =>
                issue.Code.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
                || issue.Title.Contains("conflict", StringComparison.OrdinalIgnoreCase)
                || issue.AffectedFields.Any(static field => field.Contains("identifier", StringComparison.OrdinalIgnoreCase)))
            .Select(static issue => issue.Code)
            .Concat(openConflictSummaries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var freshnessMinutes = Math.Max(0, (int)Math.Floor((observedAt - providerProjection.SyncedAt).TotalMinutes));
        var providerEvidenceStale = providerProjection.Status.IsStale || freshnessMinutes > providerStaleAfterMinutes;
        var issueCodes = issues
            .Select(static issue => issue.Code)
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Concat(providerEvidenceStale ? ["PROVIDER_EVIDENCE_STALE"] : [])
            .Concat(openConflictSummaries.Length > 0 ? ["SM_IDENTIFIER_CONFLICT"] : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var adjustedConfidenceScore = confidenceScore;
        if (providerEvidenceStale && adjustedConfidenceScore > 70m)
        {
            adjustedConfidenceScore = 70m;
        }

        if (openConflictSummaries.Length > 0 && adjustedConfidenceScore > 60m)
        {
            adjustedConfidenceScore = 60m;
        }

        var reasonParts = new List<string> { reason };
        if (providerEvidenceStale)
        {
            reasonParts.Add("Provider evidence is stale for this reconciliation run.");
        }

        if (openConflictSummaries.Length > 0)
        {
            reasonParts.Add($"{openConflictSummaries.Length} open Security Master identifier conflict(s) involve this resolved instrument.");
        }

        return new ProviderSecurityMasterPassportDto(
            Symbol: position.Symbol.Trim().ToUpperInvariant(),
            ProviderId: providerProjection.Link.ProviderId,
            ExternalAccountId: providerProjection.Link.ExternalAccountId,
            ProviderSyncedAt: providerProjection.SyncedAt,
            ProviderIsStale: providerEvidenceStale,
            AssetClass: position.AssetClass,
            Currency: position.Currency,
            PositionId: position.PositionId,
            SecurityId: securityId,
            SecurityDisplayName: security?.DisplayName,
            SecurityStatus: security?.Status,
            Status: status,
            ConfidenceScore: adjustedConfidenceScore,
            ResolutionSource: resolutionSource,
            IdentifierConflicts: identifierConflicts,
            ValidationIssueCodes: issueCodes,
            OverrideHistory: overrideHistory,
            ObservedAt: observedAt,
            FreshnessMinutes: freshnessMinutes,
            Reason: string.Join(" ", reasonParts));
    }

    private static string[] FormatOpenIdentifierConflicts(
        Guid? securityId,
        IReadOnlyList<SecurityMasterConflict> openIdentifierConflicts)
    {
        var resolvedSecurityId = securityId.GetValueOrDefault();
        if (resolvedSecurityId == Guid.Empty || openIdentifierConflicts.Count == 0)
        {
            return [];
        }

        return openIdentifierConflicts
            .Where(conflict =>
                conflict.SecurityId == resolvedSecurityId &&
                string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static conflict => conflict.DetectedAt)
            .Select(static conflict =>
                $"conflict={conflict.ConflictId:N}; kind={conflict.ConflictKind}; field={conflict.FieldPath}; providers={conflict.ProviderA}/{conflict.ProviderB}")
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> GetSecurityMasterOverrideHistoryAsync(
        Guid? securityId,
        CancellationToken ct)
    {
        var resolvedSecurityId = securityId.GetValueOrDefault();
        if (_operatorOverridesStore is null || resolvedSecurityId == Guid.Empty)
        {
            return [];
        }

        var overrides = await _operatorOverridesStore.GetAsync(resolvedSecurityId, ct).ConfigureAwait(false);
        if (overrides is null || overrides.AuditTrail.Count == 0)
        {
            return [];
        }

        return overrides.AuditTrail
            .OrderByDescending(static entry => entry.OccurredAt)
            .Take(10)
            .Select(FormatOverrideHistory)
            .ToArray();
    }

    private static string FormatOverrideHistory(SecurityOverrideAuditEntryDto entry)
    {
        var parts = new List<string>
        {
            entry.EventType,
            entry.ApprovalStatus.ToString(),
            $"actor={entry.Actor}",
            $"at={entry.OccurredAt:O}"
        };

        if (!string.IsNullOrWhiteSpace(entry.Reviewer))
        {
            parts.Add($"reviewer={entry.Reviewer.Trim()}");
        }

        if (entry.ReviewedAt.HasValue)
        {
            parts.Add($"reviewedAt={entry.ReviewedAt.Value:O}");
        }

        if (!string.IsNullOrWhiteSpace(entry.ReasonCode))
        {
            parts.Add($"reason={entry.ReasonCode.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Comment))
        {
            parts.Add($"comment={entry.Comment.Trim()}");
        }

        return string.Join("; ", parts);
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
