using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Typed economics of one corporate action, read from the wide <see cref="CorporateActionDto"/>
/// (its per-event nullable columns plus the JSON payload) into the one shape that event family
/// actually uses. Consumers switch on the case instead of probing nullable fields that belong to
/// other event types; a new event type maps onto an existing family or onto
/// <see cref="PayloadCorporateActionTerms"/>, never onto another nullable column.
/// </summary>
public abstract record CorporateActionTerms(string EventType)
{
    /// <summary>
    /// The typed terms for <paramref name="action"/>. The event type is normalized through the
    /// descriptor catalog first, so provider aliases map to the canonical family.
    /// </summary>
    public static CorporateActionTerms From(CorporateActionDto action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var eventType = CorporateActionEventTypes.Normalize(action.EventType);
        var payload = action.Payload;
        return eventType switch
        {
            CorporateActionEventTypes.Dividend or CorporateActionEventTypes.SpecialDividend =>
                new CashDistributionCorporateActionTerms(
                    eventType,
                    action.DividendPerShare,
                    action.Currency,
                    ParseComponent(CorporateActionPayloads.ReadString(payload, CorporateActionPayloads.DistributionComponent))
                        ?? SecurityIncomeComponent.Income),
            CorporateActionEventTypes.ReturnOfCapital =>
                new CashDistributionCorporateActionTerms(
                    eventType,
                    action.DividendPerShare ?? CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.CapitalReturnPerShare),
                    action.Currency,
                    SecurityIncomeComponent.ReturnOfCapital),
            CorporateActionEventTypes.StockSplit or CorporateActionEventTypes.ReverseStockSplit =>
                new SplitCorporateActionTerms(eventType, action.SplitRatio),
            CorporateActionEventTypes.SpinOff =>
                new SecurityDistributionCorporateActionTerms(eventType, action.NewSecurityId, action.DistributionRatio),
            CorporateActionEventTypes.RightsDistribution =>
                new SecurityDistributionCorporateActionTerms(
                    eventType,
                    ReadGuid(payload, CorporateActionPayloads.RightsSecurityId),
                    CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.RightsPerHeldUnit)),
            CorporateActionEventTypes.MergerAbsorption =>
                new ExchangeCorporateActionTerms(
                    eventType,
                    action.AcquirerSecurityId,
                    action.ExchangeRatio,
                    CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.CashConsiderationPerUnit),
                    IsVoluntary: false),
            CorporateActionEventTypes.Rule144ARegSExchange or CorporateActionEventTypes.Conversion =>
                new ExchangeCorporateActionTerms(
                    eventType,
                    action.NewSecurityId,
                    action.ExchangeRatio,
                    CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.CashConsiderationPerUnit),
                    CorporateActionPayloads.ReadBoolean(payload, CorporateActionPayloads.IsVoluntary)),
            CorporateActionEventTypes.ExchangeOffer =>
                new ExchangeCorporateActionTerms(
                    eventType,
                    action.NewSecurityId ?? ReadGuid(payload, CorporateActionPayloads.SuccessorSecurityId),
                    action.ExchangeRatio,
                    CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.CashConsiderationPerUnit),
                    CorporateActionPayloads.ReadBoolean(payload, CorporateActionPayloads.IsVoluntary)),
            CorporateActionEventTypes.RightsIssue =>
                new SubscriptionCorporateActionTerms(
                    eventType, action.SubscriptionPricePerShare, action.RightsPerShare, null, null, null),
            CorporateActionEventTypes.RightsExercise =>
                new SubscriptionCorporateActionTerms(
                    eventType,
                    CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.ExercisePricePerUnit),
                    null,
                    ReadGuid(payload, CorporateActionPayloads.RightsSecurityId),
                    ReadGuid(payload, CorporateActionPayloads.SuccessorSecurityId),
                    CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.UnitsPerRight)),
            CorporateActionEventTypes.BondCall or CorporateActionEventTypes.BondMaturityRedemption or CorporateActionEventTypes.PutRedemption =>
                new RedemptionCorporateActionTerms(
                    eventType,
                    action.RedemptionPricePercentOfPar,
                    CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.CashConsiderationPerUnit),
                    CorporateActionPayloads.ReadBoolean(payload, CorporateActionPayloads.IsPartialRedemption),
                    CorporateActionPayloads.ReadBoolean(payload, CorporateActionPayloads.IsMakeWhole)),
            CorporateActionEventTypes.PrincipalPaydown =>
                new PrincipalPaydownCorporateActionTerms(
                    eventType,
                    action.DistributionRatio,
                    CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.PrincipalAmount),
                    CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.PostPaydownFactor)),
            CorporateActionEventTypes.TenderOffer =>
                new OfferCorporateActionTerms(
                    eventType,
                    CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.OfferPricePerShare),
                    CorporateActionPayloads.ReadDate(payload, CorporateActionPayloads.OfferExpiryDate),
                    CorporateActionPayloads.ReadBoolean(payload, CorporateActionPayloads.IsPartialTender),
                    CorporateActionPayloads.ReadBoolean(payload, CorporateActionPayloads.IsVoluntary)),
            CorporateActionEventTypes.SymbolChange or CorporateActionEventTypes.NameChange
                or CorporateActionEventTypes.Delisting or CorporateActionEventTypes.FuturesExpiry =>
                new ReferenceChangeCorporateActionTerms(
                    eventType,
                    action.NewSecurityId,
                    CorporateActionPayloads.ReadDate(payload, CorporateActionPayloads.FinalTradingDate),
                    CorporateActionPayloads.ReadString(payload, CorporateActionPayloads.DelistingReason)),
            _ => new PayloadCorporateActionTerms(eventType, payload)
        };
    }

    /// <summary>
    /// Event types whose economics live only in the versioned payload (their schema is in
    /// <see cref="CorporateActionPayloadSchemaCatalog"/>). Every other catalog event type maps to a
    /// typed family; the coverage test holds the two lists to the catalog.
    /// </summary>
    public static IReadOnlySet<string> PayloadOnlyEventTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        CorporateActionEventTypes.AdvanceRefunding,
        CorporateActionEventTypes.BankruptcyDistribution,
        CorporateActionEventTypes.ConsentSolicitation,
        CorporateActionEventTypes.FractionalUnitsCashInLieu,
        CorporateActionEventTypes.PaymentInKind,
        CorporateActionEventTypes.OptionContractAdjustment,
        CorporateActionEventTypes.CryptoFork,
    };

    private static SecurityIncomeComponent? ParseComponent(string? value)
        => value is not null && Enum.TryParse<SecurityIncomeComponent>(value.Trim(), ignoreCase: true, out var component) && Enum.IsDefined(component)
            ? component
            : null;

    private static Guid? ReadGuid(JsonElement? payload, string key)
        => CorporateActionPayloads.ReadString(payload, key) is { } text && Guid.TryParse(text, out var id) ? id : null;
}

/// <summary>Cash paid per unit: dividends, special dividends, fund distributions, returns of capital.</summary>
public sealed record CashDistributionCorporateActionTerms(
    string EventType,
    decimal? AmountPerUnit,
    string? Currency,
    SecurityIncomeComponent Component) : CorporateActionTerms(EventType);

/// <summary>Unit count scaled by a ratio (forward or reverse split).</summary>
public sealed record SplitCorporateActionTerms(string EventType, decimal? Ratio) : CorporateActionTerms(EventType);

/// <summary>New units of another security distributed per unit held (spin-off, rights distribution).</summary>
public sealed record SecurityDistributionCorporateActionTerms(
    string EventType,
    Guid? DistributedSecurityId,
    decimal? UnitsPerHeldUnit) : CorporateActionTerms(EventType);

/// <summary>Held units exchanged for a successor security, optionally with cash (merger, conversion, exchange).</summary>
public sealed record ExchangeCorporateActionTerms(
    string EventType,
    Guid? SuccessorSecurityId,
    decimal? ExchangeRatio,
    decimal? CashPerUnit,
    bool? IsVoluntary) : CorporateActionTerms(EventType);

/// <summary>Holder pays to receive units (rights issue, rights exercise).</summary>
public sealed record SubscriptionCorporateActionTerms(
    string EventType,
    decimal? PricePerUnit,
    decimal? RightsPerHeldUnit,
    Guid? RightsSecurityId,
    Guid? SuccessorSecurityId,
    decimal? UnitsPerRight) : CorporateActionTerms(EventType);

/// <summary>Principal redeemed at a price (call, maturity, put).</summary>
public sealed record RedemptionCorporateActionTerms(
    string EventType,
    decimal? PricePercentOfPar,
    decimal? CashPerUnit,
    bool? IsPartial,
    bool? IsMakeWhole) : CorporateActionTerms(EventType);

/// <summary>Principal returned on a factor-amortizing instrument.</summary>
public sealed record PrincipalPaydownCorporateActionTerms(
    string EventType,
    decimal? PaydownRatio,
    decimal? PrincipalAmount,
    decimal? PostPaydownFactor) : CorporateActionTerms(EventType);

/// <summary>An offer the holder may accept (tender offer).</summary>
public sealed record OfferCorporateActionTerms(
    string EventType,
    decimal? OfferPricePerUnit,
    DateOnly? ExpiryDate,
    bool? IsPartial,
    bool? IsVoluntary) : CorporateActionTerms(EventType);

/// <summary>Reference-data change with no cash or unit economics (symbol, name, delisting, expiry).</summary>
public sealed record ReferenceChangeCorporateActionTerms(
    string EventType,
    Guid? SuccessorSecurityId,
    DateOnly? FinalTradingDate,
    string? Reason) : CorporateActionTerms(EventType);

/// <summary>Event types whose economics are carried only by the versioned JSON payload.</summary>
public sealed record PayloadCorporateActionTerms(string EventType, JsonElement? Payload) : CorporateActionTerms(EventType);
