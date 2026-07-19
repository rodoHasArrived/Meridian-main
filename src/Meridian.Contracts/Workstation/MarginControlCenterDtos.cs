namespace Meridian.Contracts.Workstation;

/// <summary>Provider-reported margin side by side with a non-authoritative Meridian estimate.</summary>
public sealed record MarginControlAccountDto(
    string ProviderId = default!,
    string AccountId = default!,
    DateTimeOffset AsOf = default,
    string SnapshotPhase = default!,
    string CertificationState = default!,
    string Currency = default!,
    string MarginRegime = default!,
    decimal Cash = default,
    decimal Equity = default,
    decimal BuyingPower = default,
    decimal? ProviderInitialMargin = null,
    decimal? ProviderMaintenanceMargin = null,
    decimal? ProviderExcessLiquidity = null,
    decimal? ProviderMarginLoan = null,
    string ShadowModelName = default!,
    decimal? ShadowInitialMargin = null,
    decimal? ShadowMaintenanceMargin = null,
    decimal? ShadowExcessLiquidity = null,
    decimal? MaintenanceVariance = null,
    string RiskLevel = default!,
    bool? ActivityComplete = null,
    IReadOnlyList<string> Restrictions = default!,
    IReadOnlyList<MarginPositionContributionDto> PositionContributions = default!,
    int OptionLifecycleEventCount = default,
    int BorrowPositionCount = default,
    int TaxLotCount = default,
    string EvidencePath = default!,
    string? CertifiedBy = null,
    DateTimeOffset? CertifiedAtUtc = null,
    string? CertificationNote = null);

public sealed record MarginPositionContributionDto(
    string Symbol = default!,
    decimal Quantity = default,
    decimal MarketValue = default,
    decimal ShadowInitialMargin = default,
    decimal ShadowMaintenanceMargin = default,
    string? BorrowStatus = null,
    decimal? BorrowRate = null,
    int TaxLotCount = default,
    int OptionLifecycleEventCount = default,
    string? SecurityId = null,
    string? SecurityMasterSource = null);

public sealed record MarginControlPrimeSummaryDto(
    string ProviderId = default!,
    int AccountCount = default,
    decimal TotalEquity = default,
    decimal? ProviderMaintenanceMargin = null,
    decimal? ProviderExcessLiquidity = null,
    int CriticalAccountCount = default);

public sealed record MarginControlAlertDto(
    string Severity = default!,
    string ProviderId = default!,
    string AccountId = default!,
    string Code = default!,
    string Message = default!,
    string SuggestedAction = default!);

public sealed record MarginControlCenterDto(
    DateTimeOffset GeneratedAtUtc = default,
    IReadOnlyList<MarginControlAccountDto> Accounts = default!,
    IReadOnlyList<MarginControlPrimeSummaryDto> PrimeSummaries = default!,
    IReadOnlyList<MarginControlAlertDto> Alerts = default!,
    int ProviderCount = default,
    int AccountCount = default,
    int ProvisionalAccountCount = default,
    int EndOfDayCertificationCandidateCount = default,
    string AuthorityNote = default!,
    string NextAction = default!);

public sealed record MarginCertificationRequestDto(
    string ProviderId = default!,
    string AccountId = default!,
    DateTimeOffset AsOf = default,
    string EvidencePath = default!,
    string Note = default!);

public sealed record MarginCertificationResultDto(
    string ProviderId = default!,
    string AccountId = default!,
    DateTimeOffset AsOf = default,
    string EvidencePath = default!,
    string CertifiedBy = default!,
    DateTimeOffset CertifiedAtUtc = default,
    string Note = default!,
    string Status = default!);
