namespace Meridian.Contracts.Workstation;

/// <summary>Ledger NAV at one date.</summary>
/// <param name="AsOf">The day the NAV is measured at the end of (UTC).</param>
/// <param name="TotalNav">Ledger NAV at the end of that day.</param>
/// <param name="ComponentCount">NAV components the ledger attributed at that date.</param>
/// <param name="LongExposure">Securities held long, at the ledger's last posted mark.</param>
/// <param name="ShortExposure">Short securities payable, as a positive amount.</param>
/// <param name="GrossExposure">Long plus short exposure.</param>
/// <param name="NetExposure">Long minus short exposure.</param>
public sealed record FundNavTrendPointDto(
    DateOnly AsOf,
    decimal TotalNav,
    int ComponentCount,
    decimal LongExposure = 0m,
    decimal ShortExposure = 0m,
    decimal GrossExposure = 0m,
    decimal NetExposure = 0m);

/// <summary>
/// A fund's ledger NAV at each month-end over a recent window, ending with NAV at the as-of date.
/// Every point is the posted ledger as it stood on that date, through the same NAV attribution the
/// workspace and the NAV comparison use.
/// </summary>
public sealed record FundNavTrendDto(
    string FundProfileId,
    string Subject,
    string Currency,
    IReadOnlyList<FundNavTrendPointDto> Points,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedAt);
