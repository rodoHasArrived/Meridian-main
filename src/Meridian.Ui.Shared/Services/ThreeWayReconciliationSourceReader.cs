using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Reads posted journal/lot truth, independent paper-OMS snapshots and retained broker observations.
/// The broker-backed legacy IPortfolioState is deliberately not an OMS source.
/// </summary>
public sealed class ThreeWayReconciliationSourceReader(
    BrokeragePortfolioSyncService brokerage,
    FileThreeWayReconciliationEvidenceStore evidence,
    ThreeWayReconciliationOptions options,
    IAccountQueryService? accounts = null,
    ILedgerJournalStore? ledger = null,
    IFundProfileTenancyRegistry? tenancy = null,
    PaperTradingPortfolio? oms = null,
    ISecurityResolver? securities = null,
    TimeProvider? timeProvider = null) : IThreeWayReconciliationSourceReader
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<ThreeWayReconciliationScopeDto> AuthorizeAsync(Guid accountId,
        ReconciliationBreakQueueScope accessScope, Guid ledgerBookId, Guid periodId, CancellationToken ct = default)
    {
        if (accounts is null || ledger is null || tenancy is null)
            throw new InvalidOperationException("Three-way reconciliation requires durable ledger, account and ownership services.");
        var account = await accounts.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        var book = await ledger.GetLedgerBookAsync(ledgerBookId, ct).ConfigureAwait(false);
        var period = await ledger.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
        if (account is null || !account.IsActive || account.EntityId is null || book is null ||
            period?.LedgerBookId != ledgerBookId || book.AccountingBasis != Meridian.Contracts.Ledger.AccountingBasisKindDto.Primary ||
            !((book.FundStructureNodeKind == FundStructureNodeKindDto.Account && book.FundStructureNodeId == accountId)
                || (book.FundStructureNodeKind == FundStructureNodeKindDto.Fund && book.FundStructureNodeId == account.FundId)
                || (book.FundStructureNodeKind == FundStructureNodeKindDto.Entity && book.FundStructureNodeId == account.EntityId)) ||
            !string.Equals(book.BaseCurrency, "USD", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(account.BaseCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Account, USD primary book and period authority do not agree.");
        var owner = await tenancy.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (owner is null || !owner.IsHeldBy(accessScope.TenantId) ||
            !string.Equals(owner.CompanyId, accessScope.CompanyId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The accounting book is not owned by the authenticated tenant/company.");
        var link = await brokerage.GetStatusAsync(accountId, ct).ConfigureAwait(false);
        return new(owner.TenantId.Trim(), owner.CompanyId!.Trim(), book.FundProfileId, account.EntityId.Value,
            accountId, ledgerBookId, periodId, "USD", link.ProviderId ?? "unlinked", link.ExternalAccountId ?? "unlinked");
    }

    public async Task CaptureAsync(ThreeWayReconciliationScopeDto scope, CancellationToken ct = default)
    {
        // PaperTradingPortfolio.TakeSnapshot holds the portfolio mutation lock and retains exact
        // per-account quantity and cost; global portfolio aggregates cannot supply account authority.
        var currentOms = oms?.GetAccountSnapshot(scope.FinancialAccountId.ToString("D"));
        if (currentOms is not null)
        {
            var failures = new List<string>();
            if (currentOms.MarginBalance != 0m || currentOms.ShortMarketValue != 0m)
                failures.Add("Only long cash-equity OMS accounts are supported.");
            var positions = new List<ThreeWayPositionDto>();
            foreach (var position in currentOms.Positions)
            {
                var id = await ResolveAsync(position.Symbol, currentOms.AsOf, ct).ConfigureAwait(false);
                if (id is null || position.ContractMultiplier != 1m || position.ExactQuantity < 0m)
                    failures.Add($"OMS position {position.Symbol} lacks supported canonical share identity.");
                positions.Add(new(id ?? Guid.Empty, position.Symbol, position.ExactQuantity,
                    position.ExactQuantity * position.AverageCostBasis));
            }
            var source = new ThreeWaySourceSnapshotDto("oms", currentOms.AsOf.ToUniversalTime(),
                "oms:" + scope.FinancialAccountId.ToString("D") + ":" + currentOms.AsOf.ToUniversalTime().ToString("O"),
                currentOms.Cash, positions, ThreeWayReconciliationOptions.SupportedBasisConvention, failures, "simulated");
            await evidence.SaveSourceAsync(scope, source, ct).ConfigureAwait(false);
        }

        var provider = await brokerage.GetActivityAsync(scope.FinancialAccountId, ct).ConfigureAwait(false);
        if (provider?.Balance is not null && provider.Status.LastSuccessfulSyncAt is { } observedAt)
        {
            var failures = new List<string>();
            if (provider.FundAccountId != scope.FinancialAccountId || provider.Link.FundAccountId != scope.FinancialAccountId ||
                !string.Equals(provider.Link.ProviderId, scope.ProviderId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(provider.Link.ExternalAccountId, scope.ExternalAccountId, StringComparison.Ordinal) ||
                provider.Status.Health != WorkstationBrokerageSyncHealth.Healthy || provider.Balance.MarginBalance != 0m ||
                !string.Equals(provider.Balance.Currency, scope.Currency, StringComparison.OrdinalIgnoreCase))
                failures.Add("Broker account identity, coverage, cash convention or currency is unavailable.");
            var positions = new List<ThreeWayPositionDto>();
            foreach (var position in provider.Positions)
            {
                var canonicalId = await ResolveAsync(position.Symbol, observedAt, ct).ConfigureAwait(false);
                if (canonicalId is null || position.Security?.SecurityId != canonicalId ||
                    position.Quantity < 0m || !IsEquity(position.Security?.AssetClass) ||
                    !string.Equals(position.Currency ?? provider.Balance.Currency, scope.Currency, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"Broker position {position.Symbol} lacks a consistent supported share identity/currency.");
                positions.Add(new(canonicalId ?? Guid.Empty, position.Symbol, position.Quantity,
                    position.Quantity * position.AverageEntryPrice));
            }
            await evidence.SaveSourceAsync(scope, new ThreeWaySourceSnapshotDto("broker", observedAt.ToUniversalTime(),
                provider.RawSnapshotPath, provider.Balance.Cash, positions, options.BrokerCostBasisConvention, failures,
                    DataProvenanceExtensions.IsSimulatedOriginToken(scope.ProviderId) ? "simulated" : "real")
            {
                CashActivities = provider.CashTransactions.Select(activity => new ThreeWayCashActivityDto(
                    activity.TransactionId, activity.TransactionType, activity.Amount, activity.Currency,
                    activity.PostedAt.ToUniversalTime(), provider.RawSnapshotPath)).ToArray()
            }, ct)
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<ThreeWaySourceSnapshotDto>> ReadAsync(ThreeWayReconciliationScopeDto scope,
        DateTimeOffset cutoffUtc, CancellationToken ct = default)
    {
        var period = await ledger!.GetPeriodAsync(scope.AccountingPeriodId, ct).ConfigureAwait(false);
        var date = DateOnly.FromDateTime(cutoffUtc.UtcDateTime);
        if (period is null || date < period.StartDate || date > period.EndDate)
            throw new ArgumentException("Comparison cutoff must be within the selected accounting period.");
        var result = new List<ThreeWaySourceSnapshotDto>();
        try
        {
            result.Add(await ReadLedgerAsync(scope, cutoffUtc, ct).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or LedgerValidationException or ArgumentException)
        {
            result.Add(Unavailable("ledger", cutoffUtc, "The authoritative journal/lot cut is unavailable or incomplete."));
        }
        foreach (var source in new[] { "oms", "broker" })
        {
            try
            {
                var snapshot = await evidence.ReadSourceAsync(scope, source, cutoffUtc, ct).ConfigureAwait(false);
                result.Add(snapshot ?? Unavailable(source, cutoffUtc, "No independent retained source observation exists at or before the cutoff."));
            }
            catch (Exception exception) when (exception is InvalidOperationException or JsonException or IOException or ArgumentException)
            {
                result.Add(Unavailable(source, cutoffUtc, "Source history has conflicting or invalid retained evidence."));
            }
        }
        return result;
    }

    private async Task<ThreeWaySourceSnapshotDto> ReadLedgerAsync(ThreeWayReconciliationScopeDto scope,
        DateTimeOffset cutoff, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(cutoff.UtcDateTime);
        var query = new LedgerJournalEntryQuery(LedgerBookId: scope.LedgerBookId, EffectiveTo: date, OccurredTo: cutoff);
        if (ledger is not ILedgerOpenPositionReportingSnapshotSource consistentSource)
            throw new InvalidOperationException("Three-way reconciliation requires an atomic journal and historical-basis capture.");
        var snapshot = await consistentSource.CaptureOpenPositionReportingSnapshotAsync(query, scope.AccountingPeriodId, ct)
            .ConfigureAwait(false);
        if (snapshot.OpenPositions is null || snapshot.OpenPositions.AsOf != date || snapshot.OpenPositions.CutoffUtc != cutoff)
            throw new InvalidOperationException("The historical lot snapshot does not cover the requested cutoff.");
        var journals = snapshot.Journals;
        var accountId = scope.FinancialAccountId.ToString("D");
        var selected = journals.SelectMany(journal => journal.Entry.Lines
            .Where(line => string.Equals(line.Account.FinancialAccountId ?? line.Dimensions?.AccountId
                ?? journal.Entry.Metadata.FinancialAccountId, accountId, StringComparison.OrdinalIgnoreCase))
            .Select(line => (Journal: journal, Line: line))).ToArray();
        var failures = snapshot.OpenPositions.Blockers.Concat(snapshot.OpenPositions.CoverageBlockers
            .Where(blocker => string.Equals(blocker.Account.FinancialAccountId, accountId, StringComparison.OrdinalIgnoreCase))
            .Select(blocker => blocker.Reason)).ToList();
        if (selected.Length == 0)
            failures.Add("No posted opening state or account activity establishes this account's ledger coverage.");
        var cashLines = selected.Where(row => IsCash(row.Line.Account)).ToArray();
        if (cashLines.Length == 0)
            failures.Add("No authoritative opening cash account is present.");
        foreach (var row in selected)
            if (row.Line.Currency is not { TransactionCurrency: "USD", FunctionalCurrency: "USD", FxRateToFunctional: 1m })
                failures.Add("An account journal line lacks explicit supported currency evidence.");
        var inventoryRows = selected.Where(row => !IsCash(row.Line.Account)).Select(row =>
            (row.Journal, row.Line, Role: LedgerInventoryAccountRoleClassifier.Classify(row.Line,
                row.Journal.Entry.Metadata, snapshot.OpenPositions.HistoricalLotAccounts))).ToArray();
        if (inventoryRows.Any(row => row.Role == LedgerInventoryAccountRole.UnknownInstrumentAsset))
            failures.Add("An account instrument asset lacks canonical inventory or source-proven non-lot classification.");
        var carrying = inventoryRows.Where(row => row.Role == LedgerInventoryAccountRole.LotCarrying).ToArray();
        var ownedLots = snapshot.OpenPositions.Lots.Where(lot =>
            string.Equals(lot.Account.FinancialAccountId, accountId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var positions = new List<ThreeWayPositionDto>();
        foreach (var lot in ownedLots)
        {
            var canonical = lot.ToOpenLot();
            if (canonical.Acquisition.QuantityBasis != LotQuantityBasis.Units || canonical.Acquisition.Direction != OpenLotDirection.Long ||
                canonical.Acquisition.FunctionalCurrency != "USD" || canonical.Acquisition.AcquisitionCurrency != "USD" ||
                string.IsNullOrWhiteSpace(lot.Account.Symbol))
                failures.Add("Canonical lots contain unsupported direction, quantity basis, symbol or currency.");
            positions.Add(new(canonical.SecurityId, lot.Account.Symbol ?? string.Empty, canonical.OpenQuantity,
                canonical.OpenFunctionalCostBasis));
        }
        foreach (var row in carrying)
        {
            var security = row.Line.Dimensions?.InstrumentId ?? row.Journal.Entry.Metadata.SecurityId;
            var position = row.Line.Dimensions?.PositionId;
            if (position is null && row.Journal.Entry.Metadata.Tags is { } tags && tags.TryGetValue("bookPositionId", out var text) && Guid.TryParse(text, out var parsed))
                position = parsed;
            if (security is null || security == Guid.Empty || position is null || position == Guid.Empty)
                failures.Add("An instrument carrying journal lacks canonical security/book-position identity.");
        }
        foreach (var group in carrying.GroupBy(row => row.Line.Account))
        {
            // Canonical lot basis must explain the historical cost-carrying balance. Merely
            // having instrument dimensions is not proof that legacy journal-only holdings are
            // zero. Explicit fair-value marks change carrying value rather than lot cost.
            var costBalance = group.Where(row => row.Journal.Entry.Metadata.ActivityType != "fair-value-mark")
                .Sum(row => row.Line.Debit - row.Line.Credit);
            var canonicalBasis = ownedLots.Where(lot => lot.Account == group.Key)
                .Sum(lot => lot.ToOpenLot().OpenFunctionalCostBasis);
            if (Math.Abs(costBalance - canonicalBasis) > 0.01m)
                failures.Add("Instrument carrying cost is not fully explained by canonical historical open-lot basis.");
        }
        var identity = journals.OrderBy(row => row.GlobalSequence).Select(row => (row.Entry.JournalEntryId, row.GlobalSequence)).ToArray();
        var grouped = positions.GroupBy(position => position.SecurityId).Select(group => new ThreeWayPositionDto(group.Key,
            group.First().Symbol, group.Sum(position => position.Quantity), group.Sum(position => position.CostBasis))).ToArray();
        var proof = string.Join("\n", identity.Select(row => $"{row.JournalEntryId:D}:{row.GlobalSequence}")) +
            JsonSerializer.Serialize(grouped, ThreeWayReconciliationJsonContext.Default.ThreeWayPositionDtoArray);
        return new("ledger", cutoff, "urn:sha256:" + Sha256Digest.ComputeUtf8(proof),
            cashLines.Length == 0 ? null : cashLines.Sum(row => row.Line.Debit - row.Line.Credit),
            grouped, ThreeWayReconciliationOptions.SupportedBasisConvention, failures.Distinct().ToArray(),
            DataProvenanceExtensions.Strongest(selected.Select(row =>
                row.Journal.Entry.Metadata.Tags is { } tags && tags.TryGetValue("dataProvenance", out var token)
                    ? DataProvenanceExtensions.ParseTokenOrSimulated(token) : DataProvenance.Real)).Token())
        {
            AccountingPeriodVersion = snapshot.Period?.Version,
            PostedExternalTransactionIds = selected.Select(row => row.Journal.Entry).DistinctBy(entry => entry.JournalEntryId)
                .Select(ExternalIdentity).Where(identity => identity is not null).Select(identity => identity!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            TransactionIdentityCoverageComplete = selected.Select(row => row.Journal.Entry).DistinctBy(entry => entry.JournalEntryId)
                .Where(entry => entry.Metadata.ActivityType is not ("execution.trade-commission" or "trade-commission")
                    && entry.Lines.Any(line => IsCash(line.Account) && line.Credit > 0m)
                    && entry.Lines.Any(line => line.Account.AccountType == LedgerAccountType.Expense && line.Debit > 0m))
                .All(entry => ExternalIdentity(entry) is not null)
        };
    }

    private static string? ExternalIdentity(JournalEntry entry)
    {
        if (entry.Metadata.Tags is { } tags && tags.TryGetValue("externalTransactionId", out var id) && !string.IsNullOrWhiteSpace(id))
            return id;
        const string correctionPrefix = "three-way-fee:";
        return entry.Metadata.SettlementReference?.StartsWith(correctionPrefix, StringComparison.Ordinal) == true
            ? entry.Metadata.SettlementReference[correctionPrefix.Length..] : null;
    }

    private Task<Guid?> ResolveAsync(string symbol, DateTimeOffset asOf, CancellationToken ct)
        => securities is null ? Task.FromResult<Guid?>(null)
            : securities.ResolveAsync(new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, symbol, null, asOf), ct);

    private static bool IsEquity(string? assetClass)
        => SecurityAssetClassCatalog.TryResolveLabel(assetClass, out var descriptor) && descriptor.SupportsEquityClassification;

    private static bool IsCash(LedgerAccount account)
        => account.AccountType == LedgerAccountType.Asset && account.Name.StartsWith("Cash", StringComparison.OrdinalIgnoreCase);

    private static ThreeWaySourceSnapshotDto Unavailable(string name, DateTimeOffset cutoff, string reason)
        => new(name, cutoff, "unavailable", null, [], "Unknown", [reason]);
}
