using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.AssetOperations;

namespace Meridian.Application.SecurityMaster.Valuation;

/// <summary>
/// Adapts retained book-position operands to registered valuation models. No financial operand comes
/// from the caller, no symbol quote is used, and the comparison is not ledger or posting authority.
/// </summary>
public sealed class AssetPositionMarketValuationService(
    IInstrumentPositionProjectionStore positions,
    ISecurityMasterQueryService securities,
    IEnumerable<ISecurityValuationModel> models,
    ISecurityMasterPricingService pricing)
{
    public const string ComparisonBasis = "Market value less retained carrying amount; unposted mark-to-carrying comparison, not realized or tax-cost P/L.";

    public async Task<AssetPositionMarketValuationDto> ValueAsync(AssetPositionMarketValuationRequest request,
        LedgerBookDto ownedBook, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ownedBook);
        var blockers = new List<AssetPositionMarketValuationBlockerDto>();
        var diagnostics = new List<string> { "Retained instrument-position projections are rebuildable observations, not authoritative ledger balances. No accounting entry is created.", "ValuationDate is an economic UTC date; current retained evidence is evaluated now, not a reconstruction of historical transaction visibility." };
        BookPositionDto? position = null;
        PositionEconomicStateDto? state = null;
        SecurityValuationResult? value = null;
        SecurityPriceGoldenCopyDto? selection = null;
        decimal? quantity = null;
        decimal? marketValue = null;
        decimal? comparison = null;
        string basis = "Unavailable";
        string freshness = "Not assessed";
        void Block(string code, string reason, string metric = "All") => blockers.Add(new(code, reason, metric));
        AssetPositionMarketValuationDto Result() => new(request.SecurityId, request.PositionId, request.LedgerBookId,
            request.AccountId, request.ValuationDate, request.EconomicStateId, request.EconomicStateVersion,
            marketValue is null ? "Blocked" : blockers.Count > 0 ? "Partial" : "Ready", ownedBook.FundProfileId,
            position?.Version, state?.Currency ?? ownedBook.BaseCurrency, position?.BookContext.AccountingBasis.ToString(),
            position?.PositionSide, value?.ModelKey ?? "none", value?.ModelVersion ?? "none", basis, ComparisonBasis,
            quantity, state?.CarryingAmount, value?.Price, value?.PriceUnit ?? SecurityPriceUnit.Unspecified,
            marketValue, comparison, value?.ObservedAsOf, value?.KnowledgeAsOf, freshness, selection?.IsStaleFallback,
            selection?.DaysStale, selection?.SelectedSource, selection?.SelectionReceiptId,
            value?.Inputs ?? [], diagnostics, blockers);

        if (request.SecurityId == Guid.Empty || request.PositionId == Guid.Empty || request.LedgerBookId == Guid.Empty
            || request.EconomicStateId == Guid.Empty || request.EconomicStateVersion <= 0 || string.IsNullOrWhiteSpace(request.AccountId)
            || request.ValuationDate == default || request.ValuationDate == DateOnly.MaxValue)
        { Block("InvalidScope", "Exact security, position, book, account, valuation date and economic-state identity/version are required."); return Result(); }
        if (request.KnownAt is not null)
        { Block("HistoricalKnowledgeUnavailable", "Position projections do not retain a transaction-visibility cutoff; a KnownAt request cannot be certified by this read."); return Result(); }
        if (ownedBook.LedgerBookId != request.LedgerBookId)
        { Block("BookScopeMismatch", "The authoritative ledger book does not match the requested scope."); return Result(); }

        var snapshot = await positions.GetAsOfAsync(request.SecurityId, request.LedgerBookId, request.ValuationDate, ct).ConfigureAwait(false);
        if (snapshot.SecurityId != request.SecurityId || snapshot.LedgerBookId != request.LedgerBookId || snapshot.AsOfDate != request.ValuationDate)
        { Block("ProjectionScopeMismatch", "The retained projection does not attest the requested security, book and date."); return Result(); }
        var matches = snapshot.BookPositions.Where(candidate => candidate.PositionId == request.PositionId).ToArray();
        if (matches.Length != 1)
        { Block("PositionUnavailable", "One unambiguous retained book position is required."); return Result(); }
        position = matches[0];
        var book = position.BookContext;
        if (position.SecurityId != request.SecurityId || book.LedgerBookId != ownedBook.LedgerBookId
            || book.FundProfileId != ownedBook.FundProfileId || book.FundStructureNodeId != ownedBook.FundStructureNodeId
            || book.FundStructureNodeKind != ownedBook.FundStructureNodeKind || book.AccountingBasis != ownedBook.AccountingBasis
            || book.AccountingPolicyId != ownedBook.AccountingPolicyId || book.AccountingPolicyVersion != ownedBook.AccountingPolicyVersion
            || book.BaseCurrency != ownedBook.BaseCurrency || position.PrimaryAccountId != request.AccountId)
        { Block("PositionScopeMismatch", "The position account, owner, accounting policy or book differs from the authoritative scope."); return Result(); }
        if (position.EffectiveFrom > request.ValuationDate || position.EffectiveTo < request.ValuationDate
            || !string.Equals(position.Status, "Open", StringComparison.OrdinalIgnoreCase))
        { Block("PositionNotOpen", "The position must be open and effective on the valuation date."); return Result(); }
        var roles = snapshot.InstrumentRoles.Where(role => role.RoleId == position.RoleId).ToArray();
        if (roles.Length != 1 || roles[0].SecurityId != request.SecurityId || roles[0].OwnerScopeId != book.FundProfileId
            || roles[0].OwnerScopeKind != book.FundStructureNodeKind.ToString()
            || roles[0].RoleKind != InstrumentRoleKinds.Holder || roles[0].EconomicSide != InstrumentEconomicSides.Asset
            || roles[0].AccountingSide != InstrumentAccountingSides.Debit || roles[0].EffectiveFrom > request.ValuationDate
            || roles[0].EffectiveTo < request.ValuationDate
            || position.PositionSide is not (BookPositionSides.Long or BookPositionSides.Asset))
        { Block("UnsupportedPositionConvention", "This model adapter supports positive holder-asset positions; short, liability and other signed conventions require their dedicated valuation adapter."); return Result(); }
        var states = snapshot.PositionEconomicStates.Where(candidate => candidate.EconomicStateId == request.EconomicStateId).ToArray();
        state = position.CurrentEconomicState;
        if (state is null || states.Length != 1 || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(states[0]), JsonSerializer.SerializeToElement(state)) || state.PositionId != position.PositionId
            || state.EconomicStateId != request.EconomicStateId || state.Version != request.EconomicStateVersion
            || state.AsOfDate != request.ValuationDate)
        { state = null; Block("EconomicStateChanged", "Reload the exact retained economic state for the valuation date; its identity, version or embedded operands differ."); return Result(); }
        if (state.SourceEvent?.SecurityId is Guid sourceSecurity && sourceSecurity != request.SecurityId
            || state.SourceEvent?.BookPositionId is Guid sourcePosition && sourcePosition != request.PositionId)
        { Block("EconomicStateEvidenceMismatch", "The economic state's source event crosses its security or position boundary."); return Result(); }

        var asOf = new DateTimeOffset(request.ValuationDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var security = await securities.GetRecordedByIdAsOfAsync(request.SecurityId, asOf, ct).ConfigureAwait(false);
        if (security is null || security.SecurityId != request.SecurityId)
        { Block("SecurityUnavailable", "No retained Security Master definition is eligible by the economic cutoff; a current projection is not substituted."); return Result(); }
        if (security.EffectiveFrom > asOf || security.EffectiveTo is { } securityEnd && securityEnd < asOf)
        { Block("SecurityTermsNotEffective", "The retained Security Master terms are not effective at the valuation cutoff."); return Result(); }
        if (security.Currency != state.Currency || state.Currency != ownedBook.BaseCurrency)
        { Block("CurrencyMismatch", "Security, economic-state and book currencies must match; governed FX translation is not supplied by this adapter."); return Result(); }
        if (!SecurityAssetClassCatalog.TryGet(security.AssetClass, out var descriptor) || descriptor.LotMultiplierTermKey is not null
            || descriptor.LotUnit is not (SecurityLotUnitKind.Shares or SecurityLotUnitKind.Units or SecurityLotUnitKind.FaceValue))
        { Block("UnsupportedLotConvention", "The registered market-value kernel has no proven position adapter for this class's contract multiplier, notional, currency-amount or commitment convention."); return Result(); }
        var face = descriptor.LotUnit == SecurityLotUnitKind.FaceValue;
        quantity = face ? state.OriginalFaceAmount : state.Quantity;
        if (quantity is null or <= 0 || state.CarryingAmount < 0)
        { Block("PositionOperandsUnavailable", face ? "A positive retained original face is required; current face or an ambiguous quantity cannot substitute or be factored twice." : "A positive retained unit quantity and a nonnegative carrying amount, when present, are required."); return Result(); }
        var eligible = models.Where(model => model.Kind == descriptor.DefaultValuationModel).ToArray();
        if (eligible.Length != 1)
        { Block("ValuationModelUnavailable", "Exactly one valuation model must be registered for the catalog's declared convention."); return Result(); }
        try
        {
            value = await eligible[0].ValueAsync(new SecurityValuationRequest(request.SecurityId, asOf,
                AccountId: request.AccountId, Quantity: quantity), security, ct).ConfigureAwait(false);
        }
        catch (OverflowException)
        { Block("AmountOverflow", "The retained operands exceed the decimal range of the registered valuation model."); return Result(); }
        diagnostics.AddRange(value.Diagnostics);
        if (!value.Inputs.Any(input => input.Kind == "SecurityTerms"))
            value = value with { Inputs = [.. value.Inputs, new SecurityValuationInputDto("SecurityTerms", security.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), security.SecurityId, security.Version)] };
        if (value.SecurityId != request.SecurityId || value.Currency != state.Currency || value.Quantity != quantity || value.AsOf != asOf)
        { Block("ValuationScopeMismatch", "The valuation result does not match its retained position operands."); return Result(); }
        if (value.IsBlocked)
        { foreach (var blocker in value.Blockers) Block(blocker.Code, blocker.Reason); return Result(); }
        if (value.MarketValue is null || value.Price is null || value.PriceUnit != (face ? SecurityPriceUnit.PercentOfPar : SecurityPriceUnit.CurrencyPerUnit))
        { Block("QuoteConventionUnavailable", "The model did not return a market value in the position's declared quote convention."); return Result(); }

        if (value.Model == SecurityValuationModelKind.QuotedPrice)
        {
            var receipts = value.Inputs.Where(input => input.Kind == "PriceSelectionReceipt" && input.Id is not null && input.Id != Guid.Empty).ToArray();
            if (receipts.Length != 1)
            { Block("PriceReceiptUnavailable", "An exact retained account-scoped price-selection receipt is required."); return Result(); }
            selection = await pricing.GetGoldenCopySelectionAsync(request.SecurityId, request.AccountId, receipts[0].Id!.Value, ct).ConfigureAwait(false);
            if (selection is null || selection.SecurityId != request.SecurityId || selection.SelectionReceiptId != receipts[0].Id
                || selection.GoldenCopyPrice != value.Price || selection.Unit != value.PriceUnit || selection.PriceAsOf != value.ObservedAsOf
                || selection.EvaluatedAsOf != asOf || selection.GoldenCopyPrice <= 0 || selection.PriceAsOf > asOf
                || selection.KnowledgeAsOf is null || selection.KnowledgeAsOf != value.KnowledgeAsOf
                || selection.PriceAsOf > selection.KnowledgeAsOf || receipts[0].Reference != selection.SelectedSource)
            { Block("PriceReceiptMismatch", "The retained price receipt is absent or does not replay the valuation's exact scope, unit, price and observation."); return Result(); }
            if (selection.Quotation is { } quotation)
            {
                if (quotation.Basis == SecurityPriceBasis.Unspecified || !Enum.IsDefined(quotation.Basis)
                    || quotation.Convention == SecurityQuotationConvention.Unspecified || !Enum.IsDefined(quotation.Convention)
                    || quotation.Status == SecurityQuoteStatus.Unspecified || !Enum.IsDefined(quotation.Status)
                    || quotation.SettlementDate == default || string.IsNullOrWhiteSpace(quotation.SettlementBasis)
                    || (quotation.Convention == SecurityQuotationConvention.Per100Face) != (selection.Unit == SecurityPriceUnit.PercentOfPar))
                { Block("QuotationEvidenceMismatch", "The retained quotation basis, convention, status or settlement facts are incomplete or conflict with the selected price unit."); return Result(); }
                if (quotation.Basis == SecurityPriceBasis.Dirty)
                { Block("PriceBasisRequiresNormalization", "The retained dirty quotation requires settlement-aware clean/dirty normalization before this position valuation can be shown."); return Result(); }
                diagnostics.Add($"Retained quotation: {quotation.Basis}; {quotation.Convention}; {quotation.Status}; settlement {quotation.SettlementDate:yyyy-MM-dd} ({quotation.SettlementBasis}).");
            }
            freshness = selection.IsStaleFallback ? "Stale" : "Within pricing hierarchy allowance";
            if (selection.IsStaleFallback)
            { Block("StalePrice", "The retained source selection exceeds its pricing hierarchy freshness allowance; no financial amount is substituted."); return Result(); }
            if (face)
            { Block("AccrualConventionUnavailable", "This adapter has no settlement-aware normalization for a percent-of-par source quote, even when its quotation basis is declared; inspect the instrument valuation workbench."); return Result(); }
            basis = "Registered quoted-price model: retained units × account-scoped currency-per-unit golden-copy price.";
        }
        else
        {
            freshness = "Model input dates retained; no additional curve-age policy assessed";
            if (value.ObservedAsOf is null || value.Diagnostics.Any(item => item.StartsWith("CashFlowSourceStale:", StringComparison.Ordinal) || item.StartsWith("CashFlowSourceFreshnessUnknown:", StringComparison.Ordinal)))
            { Block("ModelInputFreshnessUnavailable", "The valuation model reports stale or undated source inputs; review retained input evidence."); return Result(); }
            basis = face ? "Registered discounted-cash-flow model: original holding face, outstanding factor applied once, dirty value including the next full coupon; clean value and accrued interest are not separated."
                : "Registered discounted-cash-flow model on retained unit quantity; model input conventions and diagnostics are retained.";
        }
        if (face && (state.CurrentFaceAmount is not null || state.CurrentFactor is not null))
        {
            try
            {
                var comparisonDiagnostics = new List<string>();
                var factorFace = state.CurrentFactor is { } retainedFactor
                    ? SecurityValuationMarketValues.Compute(security, quantity, 100m, SecurityPriceUnit.PercentOfPar,
                        request.ValuationDate, comparisonDiagnostics, factor: retainedFactor) : state.CurrentFaceAmount;
                var currentFace = state.CurrentFaceAmount ?? factorFace;
                var fromCurrentFace = SecurityValuationMarketValues.Compute(security, currentFace, value.Price,
                    SecurityPriceUnit.PercentOfPar, request.ValuationDate, comparisonDiagnostics, factor: 1m);
                if (currentFace is null or < 0m || factorFace != currentFace || fromCurrentFace != value.MarketValue)
                { Block("OutstandingFaceMismatch", "The model's factor-adjusted holding value does not reconcile with the retained current face/factor; no duplicate or conflicting factor is applied."); return Result(); }
            }
            catch (OverflowException)
            { Block("OutstandingFaceOverflow", "The retained face/factor comparison exceeds the supported decimal range."); return Result(); }
        }
        marketValue = value.MarketValue;
        if (face || value.Model != SecurityValuationModelKind.QuotedPrice)
            Block("CarryingAccrualBasisUnavailable", "A model/dirty value cannot be compared with carrying amount without a declared matching accrued-interest and carrying basis.", "UnrealizedPnl");
        else if (state.CarryingAmount is null)
            Block("CarryingAmountUnavailable", "The retained projection carries no carrying amount; no cost basis is inferred.", "UnrealizedPnl");
        else
        {
            try
            { comparison = checked(marketValue.Value - state.CarryingAmount.Value); }
            catch (OverflowException) { Block("ComparisonOverflow", "The mark-to-carrying comparison exceeds the supported decimal range.", "UnrealizedPnl"); }
        }
        return Result();
    }
}
