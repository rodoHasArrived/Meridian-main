using System.Text.Json.Serialization;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Issuer is a case-insensitive substring; currency, coupon and agency rating labels are exact filters.</summary>
public sealed record BondScreeningQuery(DateOnly AsOf, DateTimeOffset KnownAtUtc,
    DateOnly MaturityFrom, DateOnly MaturityTo, string? Issuer = null, string? Currency = null,
    string? CouponKind = null, DateOnly? CallProtectedThrough = null, string? RatingAgency = null,
    IReadOnlyList<string>? AcceptedRatings = null, int PageSize = 50, string? Cursor = null);

public sealed record BondScreeningRow(BondReferenceDto Reference, IReadOnlyList<BondMarketObservationDto> Ratings,
    IReadOnlyList<string> Gaps);
public sealed record BondScreeningGap(Guid SecurityId, string Reason);
public sealed record BondScreeningPageDto(IReadOnlyList<BondScreeningRow> Rows, string? NextCursor,
    IReadOnlyList<BondScreeningGap> Gaps, int SourcePagesRead, bool ReferenceKnowledgeTimeVerified,
    IReadOnlyList<string> Limitations);

public interface IBondScreeningService
{
    Task<BondScreeningPageDto> ScreenAsync(string tenantId, BondScreeningQuery query, CancellationToken ct = default);
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BondScreeningQuery))]
[JsonSerializable(typeof(BondScreeningPageDto))]
[JsonSerializable(typeof(BondReferencePageDto))]
public partial class BondScreeningJsonContext : JsonSerializerContext;
