using System.Globalization;
using Meridian.Ledger;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.Ledger;

/// <summary>Known-symbol results over retained disposal evidence. Null amounts always carry an explicit availability reason.</summary>
public sealed record GeneralLedgerSymbolPnlRow(
    string Symbol, string? FinancialAccountId, string FunctionalCurrency,
    string RealizedAvailability, string? Proceeds, string? CostBasis, string? BookRealizedGainLoss,
    string? EconomicRealizedGainLoss, string RealizedReason,
    string UnrealizedAvailability, string? UnrealizedGainLoss, string UnrealizedReason,
    IReadOnlyList<Guid> DisposalJournalIds);

/// <summary>Immutable approved close marks plus dated instrument currency/unit identity retained with the export.</summary>
public sealed record GeneralLedgerUnrealizedInputs(SecurityClosePriceSetDto ClosePrices,
    IReadOnlyList<SecurityDetailDto> Securities);

/// <summary>Uses only disposal facts captured with the journals; never queries today's positions or manufactures marks.</summary>
public static class GeneralLedgerSymbolPnlBuilder
{
    public const string SchemaVersion = "symbol-pnl.v1";
    public const string UnrealizedBlocker = "An authoritative as-of open-position quantity/basis snapshot and approved marks/FX bound to it are not present in this journal snapshot. Unrealized P&L is unavailable; it is not zero.";

    public static IReadOnlyList<GeneralLedgerSymbolPnlRow> Build(LedgerBookRecord book, LedgerReportingSnapshot snapshot,
        GeneralLedgerUnrealizedInputs? unrealizedInputs = null)
    {
        var journals = snapshot.Journals.ToDictionary(record => record.Entry.JournalEntryId, record => record.Entry);
        var keys = snapshot.Journals.SelectMany(record => record.Entry.Lines.Select(line =>
                (Symbol: line.Account.Symbol ?? record.Entry.Metadata.Symbol,
                    Account: line.Account.FinancialAccountId ?? record.Entry.Metadata.FinancialAccountId)))
            .Concat(snapshot.TaxLotDisposalHistory.Select(disposal =>
                (Symbol: disposal.Account.Symbol, Account: disposal.Account.FinancialAccountId)))
            .Concat((snapshot.OpenPositions?.Lots ?? []).Select(lot =>
                (Symbol: lot.Account.Symbol, Account: lot.Account.FinancialAccountId)))
            .Concat((snapshot.OpenPositions?.ClosedLots ?? []).Select(lot =>
                (Symbol: lot.Account.Symbol, Account: lot.Account.FinancialAccountId)))
            .Where(key => !string.IsNullOrWhiteSpace(key.Symbol))
            .Select(key => (Symbol: key.Symbol!.Trim().ToUpperInvariant(), Account: key.Account?.Trim().ToUpperInvariant()))
            .Distinct().OrderBy(key => key.Symbol, StringComparer.Ordinal).ThenBy(key => key.Account, StringComparer.Ordinal);
        var results = new List<GeneralLedgerSymbolPnlRow>();
        foreach (var key in keys)
        {
            var disposals = snapshot.TaxLotDisposalHistory.Where(disposal =>
                string.Equals(disposal.Account.Symbol, key.Symbol, StringComparison.OrdinalIgnoreCase)
                && string.Equals(disposal.Account.FinancialAccountId, key.Account, StringComparison.OrdinalIgnoreCase)).ToArray();
            var projections = new List<LedgerTaxLotReliefProjection>();
            var missing = disposals.Length == 0 || disposals.Select(disposal => disposal.MutationBatchId).Distinct().Count() != disposals.Length;
            foreach (var disposal in disposals)
            {
                try
                {
                    if (!journals.TryGetValue(disposal.JournalEntryId, out var journal))
                        throw new InvalidOperationException("Disposal does not belong to the captured journal population.");
                    projections.Add(CanonicalDisposalHistoryProjector.Project(disposal, journal, book.LedgerBookId, book.BaseCurrency));
                }
                catch (Exception exception) when (exception is LedgerValidationException or InvalidOperationException or ArgumentException)
                {
                    missing = true;
                }
            }
            // Availability is bounded to retained disposal evidence, not a claim of tax finality or complete trading coverage.
            string? Total(Func<LedgerTaxLotReliefProjection, decimal> selector)
                => missing ? null : projections.Sum(selector).ToString(CultureInfo.InvariantCulture);
            var unrealized = CalculateUnrealized(book, snapshot, key.Symbol, key.Account, unrealizedInputs);
            results.Add(new(key.Symbol, key.Account, book.BaseCurrency,
                missing ? "MissingEvidence" : "AvailableForRetainedDisposals", Total(projection => projection.Proceeds),
                Total(projection => projection.CostBasis), Total(projection => projection.RecognizedGainOrLoss),
                Total(projection => projection.RealizedGainOrLoss),
                missing ? "Retained canonical disposal evidence is absent, duplicated or inconsistent. No zero result is inferred."
                    : "Amounts reconcile retained canonical relief to posted journal economics; tax finality and completeness outside these disposal journals are not established.",
                unrealized.Amount is null ? "Unavailable" : "Available", unrealized.Amount, unrealized.Reason,
                disposals.Select(disposal => disposal.JournalEntryId).Distinct().Order().ToArray()));
        }
        return results.AsReadOnly();
    }

    private static (string? Amount, string Reason) CalculateUnrealized(LedgerBookRecord book, LedgerReportingSnapshot snapshot,
        string symbol, string? financialAccount, GeneralLedgerUnrealizedInputs? inputs)
    {
        var positions = snapshot.OpenPositions;
        if (positions is null || positions.AsOf != snapshot.Period?.EndDate || positions.Blockers.Count > 0)
            return (null, positions?.Blockers.FirstOrDefault() ?? UnrealizedBlocker);
        var coverageFailure = positions.CoverageBlockers.FirstOrDefault(blocker =>
            string.Equals(blocker.Account.FinancialAccountId, financialAccount, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(blocker.Account.Symbol) || string.Equals(blocker.Account.Symbol, symbol, StringComparison.OrdinalIgnoreCase)));
        if (coverageFailure is not null)
            return (null, coverageFailure.Reason);
        if (inputs is null || inputs.ClosePrices.Approval is null)
            return (null, "No independently approved close-price snapshot is available for the exact account/book/date scope.");
        var prices = inputs.ClosePrices.Snapshot;
        try
        { SecurityClosePriceEvidence.ValidateApproval(prices, inputs.ClosePrices.Approval); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        { return (null, "The selected close-price snapshot or its independent approval evidence is invalid."); }
        if (book.FundStructureNodeKind != FundStructureNodeKindDto.Account || prices.AccountId != book.FundStructureNodeId.ToString("D")
            || prices.BookId != book.LedgerBookId.ToString("D") || prices.ValuationDate != positions.AsOf)
            return (null, "Approved marks do not match the exact ledger account, book and valuation date.");
        var lots = positions.Lots.Where(lot => string.Equals(lot.Account.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(lot.Account.FinancialAccountId, financialAccount, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (lots.Length == 0)
            return positions.ClosedLots.Any(lot => lot.LedgerBookId == book.LedgerBookId && lot.OpenQuantity == 0m && string.Equals(lot.Account.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                && string.Equals(lot.Account.FinancialAccountId, financialAccount, StringComparison.OrdinalIgnoreCase))
                ? ("0", "Retained canonical zero-quantity mutation snapshots establish closure and the as-of carrying population reconciles.")
                : (null, "No captured canonical open lot establishes eligible quantity and basis for this symbol; no zero holding or unrealized result is inferred.");
        decimal amount = 0m;
        foreach (var lot in lots)
        {
            var security = inputs.Securities.SingleOrDefault(item => item.SecurityId == lot.SecurityId);
            var price = prices.Lines.SingleOrDefault(item => item.SecurityId == lot.SecurityId);
            if (lot.LedgerBookId != book.LedgerBookId || lot.Acquisition is not { QuantityBasis: LotQuantityBasis.Units, Direction: OpenLotDirection.Long } acquisition
                || security is null || SecurityAssetClassCatalog.ResolveLabel(security.AssetClass, out var descriptor) != SecurityAssetClassLabelResolution.Resolved
                || descriptor is not { SupportsEquityClassification: true } || security.Currency != book.BaseCurrency
                || acquisition.AcquisitionCurrency != book.BaseCurrency || acquisition.FunctionalCurrency != book.BaseCurrency
                || price is not { Unit: SecurityPriceUnit.CurrencyPerUnit, ClosePrice: > 0m }
                || security.EffectiveFrom > prices.EvaluatedAsOf || security.EffectiveTo <= prices.EvaluatedAsOf)
                return (null, "Supported v1 unrealized P&L requires long unit common equity, dated instrument identity, same-currency basis and an approved per-unit close mark; no multiplier or FX is inferred.");
            var canonical = lot.ToOpenLot();
            OpenLotValidation.Validate(canonical);
            amount += canonical.OpenQuantity * price.ClosePrice.Value - canonical.OpenFunctionalCostBasis;
        }
        return (amount.ToString(CultureInfo.InvariantCulture),
            "Approved close mark times retained as-of open quantity less retained functional basis; exact same-currency long common-equity scope.");
    }
}
