using System.Globalization;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Execution.Adapters;
using Meridian.Execution.Sdk;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
using Meridian.Execution.PaperMatching;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Execution.Events;

/// <summary>Restores the runtime projection from the posted book before OMS or HTTP admission starts.</summary>
public sealed class CanonicalPaperPortfolioBootstrapper(
    AutomatedTradeFillAccountingOptions options, TradeFillLedgerPostingContext context,
    PaperTradingPortfolio portfolio, ILedgerJournalStore journals, IConfiguredLedgerAccountResolver accounts,
    ISecurityMasterQueryService securities, LedgerPostingConsumer? consumer = null,
    ITradeFillHandoffFailureStore? fallbackStore = null, PaperTradingCostOptions? costs = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public static PaperTradingPortfolio CreatePortfolio(AutomatedTradeFillAccountingOptions options, LiveMarketDataCache? cache = null)
    {
        var grant = options.Grant ?? throw new InvalidOperationException("Canonical paper recovery requires a configured posting grant.");
        if (!Guid.TryParse(grant.FinancialAccountId, out var id) || id == Guid.Empty
            || !grant.FinancialAccountId.Equals(id.ToString("D"), StringComparison.OrdinalIgnoreCase) || grant.SourceMode != "Synthetic")
            throw new InvalidOperationException("Canonical paper execution requires the grant's exact financial-account GUID and Synthetic source mode.");
        var portfolio = new PaperTradingPortfolio([new AccountDefinition(grant.FinancialAccountId,
            "Canonical synthetic paper account", AccountKind.Brokerage, 0m)]);
        portfolio.RequireCanonicalRecovery(cache);
        return portfolio;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;
            var grant = options.Grant!;
            if (context.AccountingProtocolVersion != 2 || context.LedgerBookId != grant.LedgerBookId
                || context.PeriodId != grant.PeriodId || context.AccountingPolicyId != grant.PolicyId
                || context.AccountingPolicyVersion != grant.PolicyVersion || portfolio.PrimaryAccountId != grant.FinancialAccountId)
                throw new InvalidOperationException("Canonical paper startup context differs from its exact granted account, book, period or policy.");
            using var tenant = FundScopeTenantAuthority.Enter(grant.TenantId, "canonical-paper-portfolio-recovery");
            if (journals is not IAutomatedPostingGrantStore grants
                || await grants.GetAsync(grant.GrantId, grant.TenantId, ct).ConfigureAwait(false) is not { IsActive: true } retained
                || retained.Reference != grant || retained.PolicySnapshot is null)
                throw new InvalidOperationException("Canonical paper startup requires its current retained operator-authorized grant and policy.");
            await grants.ValidateCurrentAsync(grant, ct).ConfigureAwait(false);
            if (consumer is not null)
            {
                var fallback = fallbackStore is null ? [] : await fallbackStore.LoadPendingAsync(ct).ConfigureAwait(false);
                await consumer.RecoverBeforeHostStartupAsync(fallback.Select(item => item.TradeEvent).ToArray(), ct).ConfigureAwait(false);
                if (fallbackStore is not null)
                    foreach (var item in fallback)
                        await fallbackStore.MarkReplayedAsync(item.TradeEvent.FillId, ct).ConfigureAwait(false);
            }
            if (journals is not ILedgerOpenPositionReportingSnapshotSource snapshots)
                throw new InvalidOperationException("Canonical paper startup requires an authoritative coherent cash/lot snapshot.");
            var book = await journals.GetLedgerBookAsync(context.LedgerBookId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The canonical paper book is unavailable.");
            if (book.FundStructureNodeKind == Meridian.Contracts.FundStructure.FundStructureNodeKindDto.Account
                && book.FundStructureNodeId != Guid.Parse(grant.FinancialAccountId))
                throw new InvalidOperationException("The account-owned book differs from the granted financial account.");
            if (book.BaseCurrency != "USD" || book.AccountingPolicyId != grant.PolicyId || book.AccountingPolicyVersion != grant.PolicyVersion)
                throw new InvalidOperationException("Canonical paper startup book currency or policy differs from its grant.");
            var cutoff = DateTimeOffset.UtcNow;
            var snapshot = await snapshots.CaptureOpenPositionReportingSnapshotAsync(new LedgerJournalEntryQuery(
                LedgerBookId: book.LedgerBookId, OccurredTo: cutoff, EffectiveTo: DateOnly.FromDateTime(cutoff.UtcDateTime)), context.PeriodId, ct).ConfigureAwait(false);
            var businessDate = DateOnly.FromDateTime(cutoff.UtcDateTime);
            if (snapshot.Period is not { Status: "Open" } period || period.PeriodId != grant.PeriodId
                || period.LedgerBookId != grant.LedgerBookId || period.Version != context.ExpectedPeriodVersion
                || businessDate < period.StartDate || businessDate > period.EndDate)
                throw new InvalidOperationException("Canonical paper startup period authority changed; review the configured posting scope.");
            var open = snapshot.OpenPositions ?? throw new InvalidOperationException("Canonical paper startup inventory coverage is unavailable.");
            if (open.Blockers.Count != 0 || open.CoverageBlockers.Any(item =>
                string.Equals(item.Account.FinancialAccountId, grant.FinancialAccountId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Canonical paper startup inventory coverage is incomplete for the granted account.");
            var node = await accounts.ResolveAsync(new(grant.TenantId, grant.CompanyId, book.FundProfileId,
                book.LedgerBookId, options.CashAccountPath, "Asset", grant.MappingRevision, FinancialAccountId: grant.FinancialAccountId), ct).ConfigureAwait(false);
            var cashAccount = new LedgerAccount(node.AccountName, LedgerAccountType.Asset, FinancialAccountId: grant.FinancialAccountId);
            if (cashAccount != LedgerAccounts.CashFor(grant.FinancialAccountId))
                throw new InvalidOperationException("Canonical cash mapping must retain the native account-scoped Cash account.");
            foreach (var mapping in new[] { (options.SecuritiesAccountPath, "Asset"), (options.RealizedGainAccountPath, "Revenue"),
                (options.RealizedLossAccountPath, "Expense"), (options.CommissionAccountPath, "Expense"), (options.FeeAccountPath, "Expense") })
            {
                var mapped = await accounts.ResolveAsync(new(grant.TenantId, grant.CompanyId, book.FundProfileId, book.LedgerBookId,
                    mapping.Item1, mapping.Item2, grant.MappingRevision, FinancialAccountId: grant.FinancialAccountId), ct).ConfigureAwait(false);
                if (mapping.Item1 == options.RealizedGainAccountPath && mapped.AccountName != LedgerAccounts.RealizedGain.Name
                    || mapping.Item1 == options.RealizedLossAccountPath && mapped.AccountName != LedgerAccounts.RealizedLoss.Name)
                    throw new InvalidOperationException("Canonical result mappings must retain the native account-scoped Realized Gain and Realized Loss accounts.");
            }
            var cashLines = snapshot.Journals.SelectMany(record => record.Entry.Lines).Where(line => line.Account == cashAccount).ToArray();
            if (cashLines.Length == 0 || cashLines.Any(line => line.Currency is not { TransactionCurrency: "USD", FunctionalCurrency: "USD", FxRateToFunctional: 1m }
                || line.Currency.TransactionDebit != line.Debit || line.Currency.TransactionCredit != line.Credit))
                throw new InvalidOperationException("Canonical paper startup requires proven typed USD opening cash for its exact account.");
            var restored = new List<CanonicalPaperPositionRecovery>();
            foreach (var group in open.Lots.Where(lot => string.Equals(lot.Account.FinancialAccountId, grant.FinancialAccountId, StringComparison.OrdinalIgnoreCase)).GroupBy(lot => lot.Account.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(group.Key) || group.Select(lot => lot.SecurityId).Distinct().Count() != 1
                    || group.Select(lot => lot.BookPositionId).Distinct().Count() != 1)
                    throw new InvalidOperationException("Canonical paper holdings require unambiguous security, symbol and book-position identities.");
                var security = await securities.GetByIdAsync(group.First().SecurityId, ct).ConfigureAwait(false);
                if (security is null || security.Currency != "USD" || SecurityAssetClassCatalog.ResolveLabel(security.AssetClass, out var kind) != SecurityAssetClassLabelResolution.Resolved || kind.AssetClass != "Equity")
                    throw new InvalidOperationException("Canonical paper recovery supports USD cash-equity holdings only.");
                var trusted = new List<JournalEntry>();
                var batchIds = group.SelectMany(lot => new[] { lot.OriginatingMutationBatchId, lot.LastMutationBatchId })
                    .Concat(snapshot.TaxLotDisposalHistory.Where(disposal =>
                        string.Equals(disposal.Account.FinancialAccountId, grant.FinancialAccountId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(disposal.Account.Symbol, group.Key, StringComparison.OrdinalIgnoreCase))
                        .Select(disposal => (Guid?)disposal.MutationBatchId))
                    .Where(id => id.HasValue).Select(id => id!.Value).Distinct();
                foreach (var batchId in batchIds)
                {
                    var receipt = await journals.GetAtomicTaxLotPostingAsync(batchId, ct).ConfigureAwait(false);
                    if (receipt is null || receipt.MutationKind is not (AtomicTaxLotMutationKind.Acquisition or AtomicTaxLotMutationKind.Disposal))
                        continue;
                    var entry = receipt.Journal.Entry;
                    if (entry.Metadata.SecurityId != security.SecurityId || entry.Metadata.FillId is null
                        || !string.Equals(entry.Metadata.FinancialAccountId, grant.FinancialAccountId, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(entry.Metadata.Symbol, group.Key, StringComparison.OrdinalIgnoreCase)
                        || entry.Metadata.Tags is not { } tags
                        || !tags.TryGetValue("sourceEventType", out var source) || source != "execution.trade-fill"
                        || !tags.TryGetValue("automation.actionOrigin", out var origin) || origin != "AuthorizedSystem"
                        || !snapshot.Journals.Any(record => record.Entry.JournalEntryId == entry.JournalEntryId)
                        || !receipt.RetainedEvidence.Any(evidence => evidence.SubjectType == "ExecutionFill"
                            && evidence.SubjectId == entry.Metadata.FillId.Value.ToString("D") && evidence.SourceSystem == "SyntheticOMS"))
                        continue;
                    trusted.Add(entry);
                }
                var observation = trusted.OrderByDescending(entry => entry.Timestamp).Select(ReadPrice).FirstOrDefault(value => value is not null);
                if (observation is null)
                    throw new InvalidOperationException("Canonical paper recovery has no retained actual fill-price observation for holding '" + group.Key + "'.");
                var lots = new List<CanonicalPaperLotRecovery>();
                foreach (var record in group.OrderBy(lot => lot.AcquiredDate).ThenBy(lot => lot.TaxLotRecordId))
                {
                    var lot = record.ToOpenLot();
                    if (lot.Acquisition.QuantityBasis != LotQuantityBasis.Units || lot.Acquisition.Direction != OpenLotDirection.Long
                        || lot.Acquisition.FunctionalCurrency != "USD" || lot.Acquisition.AcquisitionCurrency != "USD"
                        || lot.Acquisition.CorporateActionLineage is { } origin && observation.Value.At.Date < origin.EffectiveDate.ToDateTime(TimeOnly.MinValue))
                        throw new InvalidOperationException("Canonical paper recovery requires supported long unit lots and a post-transformation price observation.");
                    lots.Add(new(lot.LotId, lot.OpenQuantity, lot.OpenFunctionalCostBasis / lot.OpenQuantity,
                        lot.OpenFunctionalCostBasis, new DateTimeOffset(lot.AcquiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), lot.TaxLotRecordId));
                }
                restored.Add(new(group.Key!, lots, observation.Value.Price, observation.Value.At, "RetainedExecutionFill"));
            }
            var realized = snapshot.Journals.Where(record => string.Equals(record.Entry.Metadata.FinancialAccountId, grant.FinancialAccountId, StringComparison.OrdinalIgnoreCase)
                    && record.Entry.Metadata.ActivityType == "execution.trade-fill")
                .SelectMany(record => record.Entry.Lines).Sum(line => line.Account.AccountType == LedgerAccountType.Revenue
                    ? line.Credit - line.Debit : line.Account.AccountType == LedgerAccountType.Expense ? line.Credit - line.Debit : 0m);
            portfolio.RestoreCanonicalState(grant.FinancialAccountId, cashLines.Sum(line => line.Debit - line.Credit), realized, restored, context.PostingScope);
            portfolio.SetCanonicalAdmissionAuthority(ValidateAdmissionAsync);
            _initialized = true;
        }
        finally { _gate.Release(); }
    }

    private async Task<string?> ValidateAdmissionAsync(OrderRequest request, CancellationToken ct)
    {
        try
        {
            var grant = options.Grant!;
            using var tenant = FundScopeTenantAuthority.Enter(grant.TenantId, "canonical-paper-order-admission");
            await ((IAutomatedPostingGrantStore)journals).ValidateCurrentAsync(grant, ct).ConfigureAwait(false);
            if (request.Type != OrderType.Market || request.LimitPrice is not > 0m || request.Quantity <= 0m
                || request.OptionContract is not null || request.Legs is { Count: > 0 })
                return "Canonical paper accounting supports immediate unit market orders with an explicit simulated price only; resting orders and derivatives require a separate reviewed mode.";
            var security = await securities.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, request.Symbol, null, ct,
                DateTimeOffset.UtcNow).ConfigureAwait(false);
            if (security is not { Status: SecurityStatusDto.Active, Currency: "USD" }
                || SecurityAssetClassCatalog.ResolveLabel(security.AssetClass, out var descriptor) != SecurityAssetClassLabelResolution.Resolved
                || descriptor.AssetClass != "Equity" || LotQuantity.UnitFor(descriptor.AssetClass, security.AssetSpecificTerms) is not (SecurityLotUnitKind.Shares, 1m))
                return "Canonical paper accounting requires an authoritative active USD cash-equity security with unit shares.";
            var now = DateTimeOffset.UtcNow;
            var date = DateOnly.FromDateTime(now.UtcDateTime);
            var snapshot = await ((ILedgerOpenPositionReportingSnapshotSource)journals).CaptureOpenPositionReportingSnapshotAsync(
                new LedgerJournalEntryQuery(LedgerBookId: grant.LedgerBookId, OccurredTo: now, EffectiveTo: date), grant.PeriodId, ct).ConfigureAwait(false);
            if (snapshot.Period is not { Status: "Open" } period || period.Version != context.ExpectedPeriodVersion
                || date < period.StartDate || date > period.EndDate || snapshot.OpenPositions is not { } open
                || open.Blockers.Count != 0 || open.CoverageBlockers.Any(item => SameAccount(item.Account.FinancialAccountId, grant.FinancialAccountId)))
                return "Canonical period or inventory authority changed; stop orders and review recovery.";
            var book = await journals.GetLedgerBookAsync(grant.LedgerBookId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Canonical book is unavailable.");
            var cash = await accounts.ResolveAsync(new(grant.TenantId, grant.CompanyId, book.FundProfileId, book.LedgerBookId,
                options.CashAccountPath, "Asset", grant.MappingRevision, FinancialAccountId: grant.FinancialAccountId), ct).ConfigureAwait(false);
            var cashAccount = new LedgerAccount(cash.AccountName, LedgerAccountType.Asset, FinancialAccountId: grant.FinancialAccountId);
            var lines = snapshot.Journals.SelectMany(record => record.Entry.Lines).Where(line => line.Account == cashAccount).ToArray();
            if (lines.Length == 0 || lines.Any(line => line.Currency is not { TransactionCurrency: "USD", FunctionalCurrency: "USD", FxRateToFunctional: 1m }
                || line.Currency.TransactionDebit != line.Debit || line.Currency.TransactionCredit != line.Credit))
                return "Canonical cash authority is incomplete.";
            var holdings = open.Lots.Where(lot => SameAccount(lot.Account.FinancialAccountId, grant.FinancialAccountId))
                .GroupBy(lot => lot.Account.Symbol ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => (Quantity: group.Sum(lot => lot.OpenQuantity),
                    Basis: group.Sum(lot => lot.ToOpenLot().OpenFunctionalCostBasis)), StringComparer.OrdinalIgnoreCase);
            if (!portfolio.MatchesCanonicalAuthority(lines.Sum(line => line.Debit - line.Credit), holdings))
                return "Canonical cash, holdings or exact basis differ from the runtime projection; wait for accounting catch-up or stop and restart after reviewed changes.";
            var cost = new PaperTradingCostModel(costs).Compute(request.Quantity, request.LimitPrice.Value, request.LimitPrice);
            return portfolio.ValidateCanonicalFunding(request, cost.ExplicitCost);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { return "Canonical accounting authority is unavailable: " + ex.Message; }
    }

    private static bool SameAccount(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static (decimal Price, DateTimeOffset At)? ReadPrice(JournalEntry entry)
        => entry.Metadata.Tags is not null && entry.Metadata.Tags.TryGetValue("tradeFill.price", out var text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && price > 0m
            ? (price, entry.Timestamp) : null;
}
