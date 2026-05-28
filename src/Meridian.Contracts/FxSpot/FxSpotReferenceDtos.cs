namespace Meridian.Contracts.FxSpot;

public sealed record FxSpotReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string BaseCurrency,
    string QuoteCurrency,
    string PairCode,
    string PrimaryIdentifier,
    long Version);

public sealed record FxSpotPairsForCurrencyDto(
    string Currency,
    IReadOnlyList<FxSpotReferenceDto> Pairs);
