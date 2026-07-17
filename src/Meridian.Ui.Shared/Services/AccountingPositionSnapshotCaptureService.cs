using Meridian.Contracts.Domain;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Persists production brokerage positions into the exact durable accounting scopes that reference
/// the synced fund account. A schedule cannot claim an account merely by naming it: fund, entity,
/// book, currency, tenant, and company ownership must all agree with server-owned registries.
/// </summary>
public interface IAccountingPositionSnapshotCaptureService
{
    Task<int> CaptureBrokerageSyncAsync(
        FundAccountBrokerageSyncActivityDto projection,
        DateTimeOffset sourceAsOfUtc,
        CancellationToken ct = default);
}

public sealed class AccountingPositionSnapshotCaptureService : IAccountingPositionSnapshotCaptureService
{
    private readonly IPositionSnapshotStore? _snapshotStore;
    private readonly IDailyValuationPortfolioSource? _dailyValuations;
    private readonly IAutomatedJournalScheduleStore? _automatedJournals;
    private readonly IAccountQueryService? _accounts;
    private readonly ILedgerJournalStore? _ledger;
    private readonly IFundProfileTenancyRegistry? _tenancy;

    public AccountingPositionSnapshotCaptureService(
        IPositionSnapshotStore? snapshotStore,
        IDailyValuationPortfolioSource? dailyValuations,
        IAutomatedJournalScheduleStore? automatedJournals,
        IAccountQueryService? accounts,
        ILedgerJournalStore? ledger,
        IFundProfileTenancyRegistry? tenancy)
    {
        _snapshotStore = snapshotStore;
        _dailyValuations = dailyValuations;
        _automatedJournals = automatedJournals;
        _accounts = accounts;
        _ledger = ledger;
        _tenancy = tenancy;
    }

    public async Task<int> CaptureBrokerageSyncAsync(
        FundAccountBrokerageSyncActivityDto projection,
        DateTimeOffset sourceAsOfUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ct.ThrowIfCancellationRequested();
        if (sourceAsOfUtc == default)
        {
            throw new ArgumentException(
                "A provider-recorded source timestamp is required for accounting position history.",
                nameof(sourceAsOfUtc));
        }

        if (_dailyValuations is null || _automatedJournals is null)
        {
            throw new InvalidOperationException(
                "Accounting position-history capture requires both daily-valuation and automated-journal schedule sources.");
        }

        var candidates = new List<CaptureCandidate>();
        foreach (var item in await _dailyValuations.ListAsync(ct).ConfigureAwait(false))
        {
            foreach (var scope in item.PositionSnapshotScopes.Where(scope => MatchesAccount(scope.AccountId, projection)))
            {
                candidates.Add(new CaptureCandidate(
                    scope.RunId,
                    scope.AccountId,
                    item.TenantId,
                    item.CompanyId,
                    item.FundProfileId,
                    item.LedgerBookId,
                    item.EntityId,
                    item.Currency));
            }
        }

        foreach (var item in await _automatedJournals.ListAsync(ct).ConfigureAwait(false))
        {
            foreach (var scope in item.PositionSnapshotScopes.Where(scope => MatchesAccount(scope.AccountId, projection)))
            {
                candidates.Add(new CaptureCandidate(
                    scope.RunId,
                    scope.AccountId,
                    item.TenantId,
                    item.CompanyId,
                    item.FundProfileId,
                    item.LedgerBookId,
                    item.EntityId,
                    item.Currency));
            }
        }

        if (candidates.Count == 0)
        {
            return 0;
        }

        var bindings = candidates
            .GroupBy(static item => item.ScopeIdentity, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static binding => binding.Key, StringComparer.OrdinalIgnoreCase)
            .Select(binding =>
            {
                var distinctOwners = binding
                    .DistinctBy(static item => item.OwnerIdentity, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (distinctOwners.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"Position snapshot scope '{binding.Key}' is bound to conflicting accounting owners.");
                }

                return distinctOwners[0];
            })
            .ToArray();

        var missingDependencies = new List<string>();
        if (_snapshotStore is null)
            missingDependencies.Add(nameof(IPositionSnapshotStore));
        if (_accounts is null)
            missingDependencies.Add(nameof(IAccountQueryService));
        if (_ledger is null)
            missingDependencies.Add(nameof(ILedgerJournalStore));
        if (_tenancy is null)
            missingDependencies.Add(nameof(IFundProfileTenancyRegistry));
        if (missingDependencies.Count > 0)
        {
            throw new InvalidOperationException(
                $"Accounting position-history capture has configured scopes but required services are unavailable: {string.Join(", ", missingDependencies)}.");
        }

        var account = await _accounts!
            .GetAccountAsync(projection.FundAccountId, ct)
            .ConfigureAwait(false);
        if (account is null || !account.IsActive || account.FundId is null || account.EntityId is null)
        {
            throw new InvalidOperationException(
                $"Synchronized fund account '{projection.FundAccountId:D}' is missing, inactive, or lacks authoritative fund/entity ownership.");
        }

        ValidateSourceScope(projection, account.BaseCurrency);
        var balance = projection.Balance!;
        var positions = BuildCanonicalPositions(projection.Positions);
        var sourceAsOf = sourceAsOfUtc.ToUniversalTime();
        var pendingWrites = new List<AccountSnapshotRecord>();

        // Resolve every owner and inspect every retained history before writing any scope. This
        // prevents a later ownership/history conflict from leaving earlier scopes partially updated.
        foreach (var candidate in bindings)
        {
            ct.ThrowIfCancellationRequested();
            ValidateCandidate(candidate);

            var owner = await ResolveOwnerAsync(
                    candidate,
                    account.FundId.Value,
                    account.EntityId.Value,
                    account.BaseCurrency,
                    ct)
                .ConfigureAwait(false);
            if (owner is null)
            {
                throw new InvalidOperationException(
                    $"Position snapshot scope '{candidate.ScopeIdentity}' does not match the synchronized account's server-owned fund, entity, book, currency, tenant, and company ownership.");
            }

            var snapshot = new AccountSnapshotRecord(
                candidate.RunId.Trim(),
                candidate.AccountId.Trim(),
                projection.Link.DisplayName.Trim(),
                projection.Link.AccountKind.ToString(),
                balance.Cash,
                balance.MarginBalance,
                positions.Sum(static position => position.UnrealisedPnl),
                0m,
                positions,
                sourceAsOf,
                owner.TenantId,
                owner.CompanyId,
                owner.FundProfileId,
                owner.LedgerBookId,
                owner.EntityId);
            var latest = await _snapshotStore!
                .GetLatestSnapshotAsync(candidate.RunId.Trim(), candidate.AccountId.Trim(), owner, ct)
                .ConfigureAwait(false);
            if (latest is null)
            {
                pendingWrites.Add(snapshot);
                continue;
            }

            var latestAsOf = latest.AsOf.ToUniversalTime();
            if (latestAsOf > sourceAsOf)
            {
                throw new InvalidOperationException(
                    $"Position snapshot scope '{candidate.ScopeIdentity}' already contains a newer observation at {latestAsOf:O}; older source {sourceAsOf:O} cannot become authoritative.");
            }

            if (latestAsOf == sourceAsOf)
            {
                var foundRetainedAtSourceTime = false;
                await foreach (var retainedAtSourceTime in _snapshotStore!
                                   .GetSnapshotHistoryAsync(
                                       candidate.RunId.Trim(),
                                       candidate.AccountId.Trim(),
                                       owner,
                                       sourceAsOf,
                                       sourceAsOf,
                                       ct)
                                   .ConfigureAwait(false))
                {
                    foundRetainedAtSourceTime = true;
                    if (!PositionSnapshotEquivalence.AreEquivalent(retainedAtSourceTime, snapshot))
                    {
                        throw new InvalidOperationException(
                            $"Position snapshot scope '{candidate.ScopeIdentity}' contains a different payload at the same source timestamp {sourceAsOf:O}.");
                    }
                }

                if (!foundRetainedAtSourceTime && !PositionSnapshotEquivalence.AreEquivalent(latest, snapshot))
                {
                    throw new InvalidOperationException(
                        $"Position snapshot scope '{candidate.ScopeIdentity}' contains a different payload at the same source timestamp {sourceAsOf:O}.");
                }

                continue;
            }

            pendingWrites.Add(snapshot);
        }

        var captured = 0;
        foreach (var snapshot in pendingWrites)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await _snapshotStore!
                .SaveSnapshotConditionallyAsync(snapshot, ct)
                .ConfigureAwait(false);
            if (outcome == PositionSnapshotSaveOutcome.Appended)
                captured++;
        }

        return captured;
    }

    private static void ValidateSourceScope(
        FundAccountBrokerageSyncActivityDto projection,
        string accountCurrency)
    {
        if (projection.Link is null || projection.Link.FundAccountId != projection.FundAccountId)
        {
            throw new InvalidOperationException(
                "Brokerage projection link does not match the synchronized fund account.");
        }

        if (projection.Balance is null ||
            string.IsNullOrWhiteSpace(projection.Balance.Currency) ||
            string.IsNullOrWhiteSpace(accountCurrency) ||
            !string.Equals(
                projection.Balance.Currency.Trim(),
                accountCurrency.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Brokerage projection balance currency does not match the synchronized account's authoritative base currency.");
        }

        var sourceCurrency = projection.Balance.Currency.Trim();
        var mismatchedPosition = projection.Positions.FirstOrDefault(position =>
            !string.IsNullOrWhiteSpace(position.Currency) &&
            !string.Equals(position.Currency.Trim(), sourceCurrency, StringComparison.OrdinalIgnoreCase));
        if (mismatchedPosition is not null)
        {
            throw new InvalidOperationException(
                $"Brokerage position '{mismatchedPosition.Symbol}' currency '{mismatchedPosition.Currency}' does not match balance currency '{sourceCurrency}'.");
        }
    }

    private static IReadOnlyList<PositionRecord> BuildCanonicalPositions(
        IReadOnlyList<FundAccountBrokeragePositionDto> sourcePositions)
    {
        ArgumentNullException.ThrowIfNull(sourcePositions);
        var positions = new List<PositionRecord>(sourcePositions.Count);
        foreach (var position in sourcePositions)
        {
            if (string.IsNullOrWhiteSpace(position.Symbol))
            {
                throw new InvalidOperationException(
                    "Brokerage positions require a symbol before accounting history can be captured.");
            }

            positions.Add(new PositionRecord(
                position.Symbol.Trim().ToUpperInvariant(),
                position.Quantity,
                position.AverageEntryPrice,
                position.UnrealizedPnl,
                RealisedPnl: 0m));
        }

        return positions
            .OrderBy(static position => position.Symbol, StringComparer.Ordinal)
            .ThenBy(static position => position.Quantity)
            .ThenBy(static position => position.CostBasis)
            .ThenBy(static position => position.UnrealisedPnl)
            .ThenBy(static position => position.RealisedPnl)
            .ToArray();
    }

    private static void ValidateCandidate(CaptureCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.RunId) ||
            !Guid.TryParse(candidate.AccountId, out _))
        {
            throw new InvalidOperationException(
                $"Position snapshot scope '{candidate.ScopeIdentity}' requires a run id and canonical fund-account id.");
        }
    }

    private async Task<PositionSnapshotOwnerScope?> ResolveOwnerAsync(
        CaptureCandidate candidate,
        Guid accountFundId,
        Guid accountEntityId,
        string accountCurrency,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(candidate.TenantId) ||
            string.IsNullOrWhiteSpace(candidate.CompanyId) ||
            string.IsNullOrWhiteSpace(candidate.FundProfileId) ||
            candidate.LedgerBookId == Guid.Empty ||
            string.IsNullOrWhiteSpace(candidate.EntityId) ||
            string.IsNullOrWhiteSpace(candidate.Currency) ||
            !Guid.TryParse(candidate.EntityId, out var entityId) ||
            entityId != accountEntityId ||
            !string.Equals(candidate.Currency.Trim(), accountCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var book = await _ledger!.GetLedgerBookAsync(candidate.LedgerBookId, ct).ConfigureAwait(false);
        if (book is null ||
            book.FundStructureNodeId != accountFundId ||
            !string.Equals(book.FundProfileId, candidate.FundProfileId.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(book.BaseCurrency, candidate.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var ownership = await _tenancy!.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (ownership is null ||
            !ownership.IsHeldBy(candidate.TenantId) ||
            string.IsNullOrWhiteSpace(ownership.CompanyId) ||
            !string.Equals(ownership.CompanyId.Trim(), candidate.CompanyId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new PositionSnapshotOwnerScope(
            ownership.TenantId.Trim(),
            ownership.CompanyId.Trim(),
            book.FundProfileId,
            book.LedgerBookId,
            accountEntityId.ToString("D"));
    }

    private static bool MatchesAccount(string? configuredAccountId, FundAccountBrokerageSyncActivityDto projection)
        => Guid.TryParse(configuredAccountId, out var accountId) && accountId == projection.FundAccountId;

    private sealed record CaptureCandidate(
        string RunId,
        string AccountId,
        string? TenantId,
        string? CompanyId,
        string FundProfileId,
        Guid LedgerBookId,
        string? EntityId,
        string Currency)
    {
        public string ScopeIdentity => string.Join('|', RunId?.Trim(), AccountId?.Trim());

        public string OwnerIdentity => string.Join(
            '|',
            TenantId?.Trim(),
            CompanyId?.Trim(),
            FundProfileId?.Trim(),
            LedgerBookId.ToString("N"),
            EntityId?.Trim(),
            Currency?.Trim());
    }
}
