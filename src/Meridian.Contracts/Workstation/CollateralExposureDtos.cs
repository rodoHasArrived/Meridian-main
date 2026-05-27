namespace Meridian.Contracts.Workstation;

public sealed record ExposureSnapshotDto(
    DateTimeOffset AsOfUtc,
    string IngestionMode,
    IReadOnlyList<CounterpartyExposureDto> Counterparties,
    IReadOnlyList<ThresholdBreachDto> Breaches,
    IReadOnlyList<CollateralCallDto> CollateralCalls,
    IReadOnlyList<ExposureTrendPointDto> Trend);

public sealed record CounterpartyExposureDto(
    string Counterparty,
    decimal NetExposure,
    decimal GrossExposure,
    decimal CollateralBalance,
    decimal HaircutAdjustedCollateral,
    MarginRequirementDto MarginRequirement,
    bool CollateralSufficient,
    IReadOnlyList<ProductExposureDto> Products);

public sealed record ProductExposureDto(string ProductType, decimal NetExposure, decimal GrossExposure);

public sealed record MarginRequirementDto(decimal InitialMargin, decimal VariationMargin, decimal TotalRequired);

public sealed record HaircutRuleDto(string AssetClass, decimal HaircutPercent, decimal AdvanceRatePercent);

public sealed record CollateralCallDto(
    string CallId,
    string Counterparty,
    decimal RequiredAmount,
    decimal PostedAmount,
    DateTimeOffset DueByUtc,
    string Status,
    string Reason);

public sealed record ThresholdBreachDto(
    string Counterparty,
    string ThresholdLevel,
    decimal ObservedExposure,
    decimal LimitExposure,
    DateTimeOffset DetectedAtUtc,
    string Message);

public sealed record ExposureTrendPointDto(DateTimeOffset AsOfUtc, decimal NetExposure, decimal CollateralCoverageRatio);
