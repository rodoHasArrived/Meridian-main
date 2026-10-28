using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Entities.FundStructure;
using Meridian.FSharp.CashFlowInterop;
using static Meridian.Application.FundStructure.FundStructureProjector;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Shared cash-flow read orchestration over a captured structure and authorized accounts.
/// Persistence services own snapshot capture, query validation, scope authorization and the clock;
/// this service reads account history and optional Security Master inputs without mutating state.
/// </summary>
internal sealed partial class FundStructureCashFlowReadService
{
    private readonly IFundAccountService _fundAccountService;
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;

    internal FundStructureCashFlowReadService(
        IFundAccountService fundAccountService,
        ISecurityMasterQueryService? securityMasterQueryService)
    {
        _fundAccountService = fundAccountService ?? throw new ArgumentNullException(nameof(fundAccountService));
        _securityMasterQueryService = securityMasterQueryService;
    }

    private static readonly StringComparer AssignmentComparer = StringComparer.OrdinalIgnoreCase;
    private const string DefaultCashFlowCurrency = "USD";
    private const string SecurityMasterInstrumentAssignmentType = "SecurityMasterInstrument";
    private const string SecurityInstrumentAssignmentType = "SecurityInstrument";
    private const string SecurityMasterRuleSourceKind = "SecurityMasterRule";
    private const string SecurityMasterCorporateActionSourceKind = "SecurityMasterCorporateAction";

    internal async Task<GovernanceCashFlowViewDto?> GetViewAsync(
        GovernanceCashFlowQuery query,
        StructureSnapshot snapshot,
        IReadOnlyList<AccountSummaryDto> visibleAccounts,
        DateTimeOffset asOf,
        FundStructureSharedDataAccessDto? sharedDataAccess,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var historicalDays = Math.Max(1, query.HistoricalDays);
        var forecastDays = Math.Max(1, query.ForecastDays);
        var bucketDays = Math.Max(1, query.BucketDays);
        var historicalWindowStart = StartOfDayUtc(asOf).AddDays(-(historicalDays - 1));
        var projectionWindowStart = StartOfDayUtc(asOf).AddDays(1);
        var projectionWindowEnd = projectionWindowStart.AddDays(forecastDays);
        var scoped = FilterForOrganizationScope(
            snapshot,
            visibleAccounts,
            query.OrganizationId,
            query.BusinessId,
            query.ActiveOnly,
            asOf);
        var ledgerAssignments = BuildLedgerAssignments(scoped.Assignments);
        var resolvedScope = ResolveCashFlowScope(query, scoped, ledgerAssignments);
        if (resolvedScope is null)
        {
            return null;
        }

        var currency = ResolveCashFlowCurrency(query.Currency, resolvedScope);
        var accountWindows = new List<AccountCashFlowWindow>(resolvedScope.Accounts.Count);
        foreach (var account in resolvedScope.Accounts)
        {
            ct.ThrowIfCancellationRequested();
            accountWindows.Add(await BuildAccountCashFlowWindowAsync(
                    account,
                    scoped,
                    currency,
                    historicalWindowStart,
                    asOf,
                    projectionWindowStart,
                    projectionWindowEnd,
                    forecastDays,
                    bucketDays,
                    ct)
                .ConfigureAwait(false));
        }

        var accountViews = accountWindows
            .Select(window => new GovernanceCashFlowAccountViewDto(
                window.Account.AccountId,
                window.Account.AccountCode,
                window.Account.DisplayName,
                window.Account.BaseCurrency,
                window.Account.LedgerReference,
                window.CurrentCashBalance,
                window.RealizedNetFlow,
                window.ProjectedNetFlow,
                window.RealizedEntries.Count,
                window.ProjectedEntries.Count,
                window.LatestSnapshotDate,
                window.UsedTrendFallback,
                window.SecurityProjectedEntryCount,
                window.UsedSecurityMasterRules,
                window.Account.SharedDataAccess))
            .OrderBy(static account => account.DisplayName)
            .ToList();

        var realizedEntries = accountWindows
            .SelectMany(static window => window.RealizedEntries)
            .OrderBy(static entry => entry.EventDate)
            .ThenBy(static entry => entry.AccountDisplayName)
            .ToList();
        var projectedEntries = accountWindows
            .SelectMany(static window => window.ProjectedEntries)
            .OrderBy(static entry => entry.EventDate)
            .ThenBy(static entry => entry.AccountDisplayName)
            .ToList();

        var realizedLadder = BuildCashFlowLadder(
            historicalWindowStart,
            historicalDays,
            bucketDays,
            currency,
            realizedEntries);
        var projectedLadder = BuildCashFlowLadder(
            projectionWindowStart,
            forecastDays,
            bucketDays,
            currency,
            projectedEntries);
        var varianceBuckets = BuildVarianceBuckets(realizedLadder, projectedLadder);
        decimal? currentCashBalance = accountViews.Any(static account => account.CurrentCashBalance is null)
            ? null
            : accountViews.Sum(static account => account.CurrentCashBalance!.Value);
        var varianceSummary = new GovernanceCashFlowVarianceSummaryDto(
            RealizedInflows: realizedLadder.TotalProjectedInflows,
            RealizedOutflows: realizedLadder.TotalProjectedOutflows,
            RealizedNetFlow: realizedLadder.NetPosition,
            ProjectedInflows: projectedLadder.TotalProjectedInflows,
            ProjectedOutflows: projectedLadder.TotalProjectedOutflows,
            ProjectedNetFlow: projectedLadder.NetPosition,
            VarianceAmount: projectedLadder.NetPosition - realizedLadder.NetPosition,
            ComparisonBasis: "Projected next window vs trailing realized window");

        return new GovernanceCashFlowViewDto(
            resolvedScope.Scope,
            asOf,
            historicalWindowStart,
            projectionWindowEnd,
            currency,
            historicalDays,
            forecastDays,
            bucketDays,
            accountViews.Count,
            currentCashBalance,
            currentCashBalance + projectedLadder.NetPosition,
            accountViews,
            realizedEntries,
            projectedEntries,
            realizedLadder,
            projectedLadder,
            varianceSummary,
            varianceBuckets,
            sharedDataAccess,
            projectedEntries.Count(static entry => entry.SecurityId.HasValue));
    }

    private async Task<AccountCashFlowWindow> BuildAccountCashFlowWindowAsync(
        AccountSummaryDto account,
        StructureScope scoped,
        string currency,
        DateTimeOffset historicalWindowStart,
        DateTimeOffset asOf,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd,
        int forecastDays,
        int bucketDays,
        CancellationToken ct)
    {
        var historicalStartDate = DateOnly.FromDateTime(historicalWindowStart.UtcDateTime);
        var asOfDate = DateOnly.FromDateTime(asOf.UtcDateTime);
        var balanceHistoryTask = _fundAccountService.GetBalanceHistoryAsync(
            account.AccountId,
            historicalStartDate,
            asOfDate,
            ct);
        var bankLinesTask = _fundAccountService.GetBankStatementLinesAsync(
            account.AccountId,
            ct: ct);

        await Task.WhenAll(balanceHistoryTask, bankLinesTask).ConfigureAwait(false);

        var balanceHistory = (await balanceHistoryTask.ConfigureAwait(false))
            .Where(snapshot => MatchesCurrency(snapshot.Currency, currency))
            .OrderBy(snapshot => snapshot.AsOfDate)
            .ThenBy(snapshot => snapshot.RecordedAt)
            .ToList();
        var latestSnapshot = balanceHistory
            .OrderByDescending(static snapshot => snapshot.AsOfDate)
            .ThenByDescending(static snapshot => snapshot.RecordedAt)
            .FirstOrDefault();
        // Statement headers establish their effective date. Feed rows retain their own
        // value dates even when another transaction moves the delivery's header forward.
        var retainedBankLines = (await bankLinesTask.ConfigureAwait(false))
            .Where(line => (line.IsTransactionFeed == true ? line.ValueDate : line.StatementDate) <= asOfDate).ToArray();
        var authoritativeBankLines = BankStatementClosingBalanceAuthority.SelectAuthoritativeLines(retainedBankLines,
            currency, historicalStartDate, DateOnly.FromDateTime(projectionWindowEnd.AddTicks(-1).UtcDateTime))
            ?? throw new BankStatementEvidenceUnavailableException();
        var bankLines = authoritativeBankLines
            .Where(line => MatchesCurrency(line.Currency, currency))
            .OrderBy(line => line.StatementDate)
            .ThenBy(line => line.ValueDate)
            .ToList();

        var historicalWindowEndExclusive = StartOfDayUtc(asOf).AddDays(1);
        var realizedBankLines = bankLines
            .Where(line =>
            {
                var eventDate = ToDateTimeOffset(line.ValueDate);
                return eventDate >= historicalWindowStart && eventDate < historicalWindowEndExclusive;
            })
            .ToList();
        var projectedBankLines = bankLines
            .Where(line =>
            {
                var eventDate = ToDateTimeOffset(line.ValueDate);
                return eventDate >= projectionWindowStart && eventDate < projectionWindowEnd;
            })
            .ToList();

        var realizedEntries = BuildRealizedEntries(
            account,
            realizedBankLines,
            balanceHistory,
            currency,
            historicalWindowStart,
            historicalWindowEndExclusive);
        var projectedEntryBuild = await BuildProjectedEntriesAsync(
            account,
            scoped,
            projectedBankLines,
            latestSnapshot,
            balanceHistory,
            currency,
            projectionWindowStart,
            projectionWindowEnd,
            forecastDays,
            bucketDays,
            ct).ConfigureAwait(false);
        var currentCashBalance = latestSnapshot?.CashBalance
            ?? GetLatestRunningBalance(retainedBankLines, asOf, currency);

        return new AccountCashFlowWindow(
            account,
            currentCashBalance,
            latestSnapshot?.AsOfDate,
            realizedEntries,
            projectedEntryBuild.Entries,
            realizedEntries.Sum(static entry => entry.Amount),
            projectedEntryBuild.Entries.Sum(static entry => entry.Amount),
            projectedEntryBuild.UsedTrendFallback,
            projectedEntryBuild.SecurityProjectedEntryCount,
            projectedEntryBuild.UsedSecurityMasterRules);
    }

    private static IReadOnlyList<GovernanceCashFlowEntryDto> BuildRealizedEntries(
        AccountSummaryDto account,
        IReadOnlyList<BankStatementLineDto> realizedBankLines,
        IReadOnlyList<AccountBalanceSnapshotDto> balanceHistory,
        string currency,
        DateTimeOffset windowStart,
        DateTimeOffset windowEndExclusive)
    {
        var bankEntries = realizedBankLines
            .Select(line => new GovernanceCashFlowEntryDto(
                EventDate: ToDateTimeOffset(line.ValueDate),
                Amount: line.Amount,
                Currency: currency,
                EventKind: NormalizeCashFlowEventKind(line.TransactionType, line.Amount),
                SourceKind: "BankStatement",
                AccountId: account.AccountId,
                AccountDisplayName: account.DisplayName,
                LedgerReference: account.LedgerReference,
                Description: line.Description,
                IsProjected: false))
            .OrderBy(static entry => entry.EventDate)
            .ToList();

        if (bankEntries.Count > 0)
        {
            return bankEntries;
        }

        return balanceHistory
            .Zip(balanceHistory.Skip(1), (previous, current) => new { previous, current })
            .Select(pair => new
            {
                pair.current,
                Delta = pair.current.CashBalance - pair.previous.CashBalance
            })
            .Where(item => item.Delta != 0m)
            .Select(item => new GovernanceCashFlowEntryDto(
                EventDate: ToDateTimeOffset(item.current.AsOfDate),
                Amount: item.Delta,
                Currency: currency,
                EventKind: "BalanceSnapshotDelta",
                SourceKind: "BalanceSnapshot",
                AccountId: account.AccountId,
                AccountDisplayName: account.DisplayName,
                LedgerReference: account.LedgerReference,
                Description: $"Cash balance delta captured on {item.current.AsOfDate:yyyy-MM-dd}.",
                IsProjected: false))
            .Where(entry => entry.EventDate >= windowStart && entry.EventDate < windowEndExclusive)
            .OrderBy(static entry => entry.EventDate)
            .ToList();
    }

    private async Task<ProjectedEntryBuildResult> BuildProjectedEntriesAsync(
        AccountSummaryDto account,
        StructureScope scoped,
        IReadOnlyList<BankStatementLineDto> projectedBankLines,
        AccountBalanceSnapshotDto? latestSnapshot,
        IReadOnlyList<AccountBalanceSnapshotDto> balanceHistory,
        string currency,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd,
        int forecastDays,
        int bucketDays,
        CancellationToken ct)
    {
        var entries = projectedBankLines
            .Select(line => new GovernanceCashFlowEntryDto(
                EventDate: ToDateTimeOffset(line.ValueDate),
                Amount: line.Amount,
                Currency: currency,
                EventKind: NormalizeCashFlowEventKind(line.TransactionType, line.Amount),
                SourceKind: "BankStatement",
                AccountId: account.AccountId,
                AccountDisplayName: account.DisplayName,
                LedgerReference: account.LedgerReference,
                Description: line.Description,
                IsProjected: true))
            .ToList();

        var securityRuleProjection = await BuildSecurityMasterProjectedEntriesAsync(
            account,
            scoped,
            latestSnapshot,
            currency,
            projectionWindowStart,
            projectionWindowEnd,
            entries,
            ct).ConfigureAwait(false);
        if (securityRuleProjection.Entries.Count > 0)
        {
            entries.AddRange(securityRuleProjection.Entries);
        }

        if (latestSnapshot is not null)
        {
            if (!securityRuleProjection.ConsumedPendingSettlement
                && latestSnapshot.PendingSettlement.HasValue
                && latestSnapshot.PendingSettlement.Value != 0m)
            {
                entries.Add(new GovernanceCashFlowEntryDto(
                    projectionWindowStart,
                    latestSnapshot.PendingSettlement.Value,
                    currency,
                    "PendingSettlement",
                    "BalanceSnapshot",
                    account.AccountId,
                    account.DisplayName,
                    account.LedgerReference,
                    "Pending settlement carried forward from the latest balance snapshot.",
                    IsProjected: true));
            }

            if (!securityRuleProjection.ConsumedAccruedInterest
                && latestSnapshot.AccruedInterest.HasValue
                && latestSnapshot.AccruedInterest.Value != 0m)
            {
                entries.Add(new GovernanceCashFlowEntryDto(
                    projectionWindowStart,
                    latestSnapshot.AccruedInterest.Value,
                    currency,
                    "Coupon",
                    "BalanceSnapshot",
                    account.AccountId,
                    account.DisplayName,
                    account.LedgerReference,
                    "Accrued interest carried forward from the latest balance snapshot.",
                    IsProjected: true));
            }
        }

        entries = entries
            .Where(entry => entry.EventDate >= projectionWindowStart && entry.EventDate < projectionWindowEnd)
            .OrderBy(static entry => entry.EventDate)
            .ToList();

        if (entries.Count > 0)
        {
            return new ProjectedEntryBuildResult(
                entries,
                UsedTrendFallback: false,
                SecurityProjectedEntryCount: entries.Count(static entry => entry.SecurityId.HasValue),
                UsedSecurityMasterRules: securityRuleProjection.Entries.Count > 0);
        }

        if (balanceHistory.Count < 2)
        {
            return new ProjectedEntryBuildResult(
                entries,
                UsedTrendFallback: false,
                SecurityProjectedEntryCount: 0,
                UsedSecurityMasterRules: false);
        }

        var firstSnapshot = balanceHistory.First();
        var lastSnapshot = balanceHistory.Last();
        var elapsedDays = Math.Max(1, lastSnapshot.AsOfDate.DayNumber - firstSnapshot.AsOfDate.DayNumber);
        var averageDailyChange = (lastSnapshot.CashBalance - firstSnapshot.CashBalance) / elapsedDays;
        if (averageDailyChange == 0m)
        {
            return new ProjectedEntryBuildResult(
                entries,
                UsedTrendFallback: false,
                SecurityProjectedEntryCount: 0,
                UsedSecurityMasterRules: false);
        }

        var bucketCount = Math.Max(1, (int)Math.Ceiling(forecastDays / (double)bucketDays));
        for (var bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
        {
            var bucketOffset = bucketIndex * bucketDays;
            var remainingDays = forecastDays - bucketOffset;
            if (remainingDays <= 0)
            {
                break;
            }

            var daysInBucket = Math.Min(bucketDays, remainingDays);
            entries.Add(new GovernanceCashFlowEntryDto(
                projectionWindowStart.AddDays(bucketOffset),
                averageDailyChange * daysInBucket,
                currency,
                "BalanceTrend",
                "BalanceTrend",
                account.AccountId,
                account.DisplayName,
                account.LedgerReference,
                $"Trend projection derived from {elapsedDays} day(s) of cash-balance history.",
                IsProjected: true));
        }

        var orderedEntries = entries.OrderBy(static entry => entry.EventDate).ToList();
        return new ProjectedEntryBuildResult(
            orderedEntries,
            UsedTrendFallback: true,
            SecurityProjectedEntryCount: 0,
            UsedSecurityMasterRules: false);
    }

    private static decimal? GetLatestRunningBalance(
        IReadOnlyList<BankStatementLineDto> bankLines,
        DateTimeOffset asOf,
        string currency) =>
        BankStatementClosingBalanceAuthority.SelectClosingBalance(
            bankLines.Where(line => ToDateTimeOffset(line.StatementDate) <= asOf),
            currency, DateOnly.FromDateTime(asOf.UtcDateTime));

    private static GovernanceCashFlowLadderDto BuildCashFlowLadder(
        DateTimeOffset anchor,
        int windowDays,
        int bucketDays,
        string currency,
        IReadOnlyList<GovernanceCashFlowEntryDto> entries)
    {
        var effectiveBucketDays = Math.Max(1, bucketDays);
        var effectiveWindowDays = Math.Max(1, windowDays);
        var windowEnd = anchor.AddDays(effectiveWindowDays);
        var inputs = entries
            .Where(entry => MatchesCurrency(entry.Currency, currency))
            .Where(entry => entry.EventDate >= anchor && entry.EventDate < windowEnd)
            .Select((entry, index) => new CashFlowProjectionInput
            {
                FlowId = CreateSyntheticFlowId(entry.AccountId, entry.EventKind, entry.EventDate, index),
                SecurityGuid = entry.SecurityId ?? Guid.Empty,
                EventKindLabel = entry.EventKind,
                ExpectedAmount = entry.Amount,
                ExpectedCurrency = currency,
                DueDate = entry.EventDate,
                IsPrincipalFlow = IsPrincipalCashFlow(entry.EventKind),
                IsIncomeFlow = IsIncomeCashFlow(entry.EventKind),
                Notes = entry.Description ?? string.Empty
            })
            .ToArray();
        var ladder = CashFlowProjector.BuildLadder(anchor, currency, effectiveBucketDays, inputs);
        var bucketMap = ladder.Buckets.ToDictionary(bucket => bucket.BucketStart);
        var bucketCount = Math.Max(1, (int)Math.Ceiling(effectiveWindowDays / (double)effectiveBucketDays));
        var buckets = new List<GovernanceCashFlowBucketDto>(bucketCount);

        for (var bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
        {
            var bucketStart = anchor.AddDays(bucketIndex * effectiveBucketDays);
            var bucketEnd = bucketStart.AddDays(Math.Min(effectiveBucketDays, effectiveWindowDays - (bucketIndex * effectiveBucketDays)));
            if (!bucketMap.TryGetValue(bucketStart, out var bucket))
            {
                buckets.Add(new GovernanceCashFlowBucketDto(
                    bucketIndex,
                    bucketStart,
                    bucketEnd,
                    0m,
                    0m,
                    0m,
                    currency,
                    0));
                continue;
            }

            buckets.Add(new GovernanceCashFlowBucketDto(
                bucketIndex,
                bucketStart,
                bucketEnd,
                bucket.ProjectedInflows,
                bucket.ProjectedOutflows,
                bucket.NetFlow,
                currency,
                bucket.EventCount));
        }

        return new GovernanceCashFlowLadderDto(
            anchor,
            windowEnd,
            currency,
            effectiveBucketDays,
            buckets.Sum(static bucket => bucket.ProjectedInflows),
            buckets.Sum(static bucket => bucket.ProjectedOutflows),
            buckets.Sum(static bucket => bucket.NetFlow),
            buckets);
    }

    private static IReadOnlyList<GovernanceCashFlowVarianceBucketDto> BuildVarianceBuckets(
        GovernanceCashFlowLadderDto realizedLadder,
        GovernanceCashFlowLadderDto projectedLadder)
    {
        var maxBucketCount = Math.Max(realizedLadder.Buckets.Count, projectedLadder.Buckets.Count);
        var buckets = new List<GovernanceCashFlowVarianceBucketDto>(maxBucketCount);

        for (var index = 0; index < maxBucketCount; index++)
        {
            var realizedBucket = index < realizedLadder.Buckets.Count
                ? realizedLadder.Buckets[index]
                : CreateEmptyBucket(realizedLadder, index);
            var projectedBucket = index < projectedLadder.Buckets.Count
                ? projectedLadder.Buckets[index]
                : CreateEmptyBucket(projectedLadder, index);

            buckets.Add(new GovernanceCashFlowVarianceBucketDto(
                BucketIndex: index,
                RealizedBucketStart: realizedBucket.BucketStart,
                RealizedBucketEnd: realizedBucket.BucketEnd,
                ProjectedBucketStart: projectedBucket.BucketStart,
                ProjectedBucketEnd: projectedBucket.BucketEnd,
                RealizedInflows: realizedBucket.ProjectedInflows,
                RealizedOutflows: realizedBucket.ProjectedOutflows,
                RealizedNetFlow: realizedBucket.NetFlow,
                ProjectedInflows: projectedBucket.ProjectedInflows,
                ProjectedOutflows: projectedBucket.ProjectedOutflows,
                ProjectedNetFlow: projectedBucket.NetFlow,
                VarianceAmount: projectedBucket.NetFlow - realizedBucket.NetFlow,
                RealizedEventCount: realizedBucket.EventCount,
                ProjectedEventCount: projectedBucket.EventCount,
                Currency: projectedBucket.Currency));
        }

        return buckets;
    }

    private static GovernanceCashFlowBucketDto CreateEmptyBucket(
        GovernanceCashFlowLadderDto ladder,
        int bucketIndex)
    {
        var bucketStart = ladder.AsOf.AddDays(bucketIndex * ladder.BucketDays);
        var bucketEnd = bucketStart.AddDays(ladder.BucketDays);
        return new GovernanceCashFlowBucketDto(
            bucketIndex,
            bucketStart,
            bucketEnd,
            0m,
            0m,
            0m,
            ladder.Currency,
            0);
    }

    private static DateTimeOffset StartOfDayUtc(DateTimeOffset timestamp) =>
        new(timestamp.UtcDateTime.Date, TimeSpan.Zero);

    private static DateTimeOffset ToDateTimeOffset(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);

    private static bool MatchesCurrency(string? left, string right) =>
        string.Equals(left?.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCashFlowEventKind(string? transactionType, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(transactionType))
        {
            return amount >= 0m ? "Proceeds" : "Fee";
        }

        if (transactionType.Contains("dividend", StringComparison.OrdinalIgnoreCase))
        {
            return "Dividend";
        }

        if (transactionType.Contains("interest", StringComparison.OrdinalIgnoreCase))
        {
            return "Coupon";
        }

        if (transactionType.Contains("fee", StringComparison.OrdinalIgnoreCase))
        {
            return "Fee";
        }

        if (transactionType.Contains("principal", StringComparison.OrdinalIgnoreCase))
        {
            return "Principal";
        }

        return transactionType.Trim();
    }

    private static bool IsPrincipalCashFlow(string eventKind) =>
        eventKind.Contains("principal", StringComparison.OrdinalIgnoreCase)
        || eventKind.Contains("maturity", StringComparison.OrdinalIgnoreCase)
        || eventKind.Contains("redemption", StringComparison.OrdinalIgnoreCase)
        || eventKind.Contains("settlement", StringComparison.OrdinalIgnoreCase);

    private static bool IsIncomeCashFlow(string eventKind) =>
        eventKind.Contains("coupon", StringComparison.OrdinalIgnoreCase)
        || eventKind.Contains("dividend", StringComparison.OrdinalIgnoreCase)
        || eventKind.Contains("interest", StringComparison.OrdinalIgnoreCase);

    private static Guid CreateSyntheticFlowId(
        Guid accountId,
        string eventKind,
        DateTimeOffset eventDate,
        int ordinal)
    {
        var payload = $"{accountId:D}|{eventKind}|{eventDate:O}|{ordinal}";
        return new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
    }

    private sealed record ResolvedCashFlowScope(
        GovernanceCashFlowScopeDto Scope,
        IReadOnlyList<AccountSummaryDto> Accounts,
        string BaseCurrency);

    private sealed record AccountCashFlowWindow(
        AccountSummaryDto Account,
        decimal? CurrentCashBalance,
        DateOnly? LatestSnapshotDate,
        IReadOnlyList<GovernanceCashFlowEntryDto> RealizedEntries,
        IReadOnlyList<GovernanceCashFlowEntryDto> ProjectedEntries,
        decimal RealizedNetFlow,
        decimal ProjectedNetFlow,
        bool UsedTrendFallback,
        int SecurityProjectedEntryCount,
        bool UsedSecurityMasterRules);

    private sealed record ProjectedEntryBuildResult(
        IReadOnlyList<GovernanceCashFlowEntryDto> Entries,
        bool UsedTrendFallback,
        int SecurityProjectedEntryCount,
        bool UsedSecurityMasterRules);

    private sealed record SecurityMasterProjectionResult(
        IReadOnlyList<GovernanceCashFlowEntryDto> Entries,
        bool ConsumedAccruedInterest,
        bool ConsumedPendingSettlement)
    {
        public static SecurityMasterProjectionResult Empty { get; } = new([], false, false);
    }

    private sealed record SecurityMasterInstrumentAssignment(
        Guid AssignmentId,
        Guid NodeId,
        Guid SecurityId,
        decimal? IncomeAmount,
        decimal? PrincipalAmount,
        decimal? Units,
        DateOnly? FirstProjectedDate,
        string? Currency,
        string? Notes,
        int Priority,
        bool IsPrimary,
        DateTimeOffset EffectiveFrom);

    private sealed record SecurityMasterInstrumentAssignmentPayload(
        Guid SecurityId,
        decimal? IncomeAmount = null,
        decimal? ExpectedCashAmount = null,
        decimal? PrincipalAmount = null,
        decimal? Units = null,
        DateOnly? FirstProjectedDate = null,
        DateOnly? NextPaymentDate = null,
        string? Currency = null,
        string? Notes = null);
}
