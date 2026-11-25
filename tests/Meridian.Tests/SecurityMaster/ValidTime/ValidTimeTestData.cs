using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster.ValidTime;

/// <summary>Synthetic ADR-034 timelines for valid-time consumer tests.</summary>
internal static class ValidTimeTestData
{
    public static SecurityEconomicDefinitionRecord Definition(Guid securityId, decimal couponRate, string displayName = "Synthetic 5Y Note")
        => new(
            SecurityId: securityId,
            AssetClass: "Bond",
            AssetFamily: null,
            SubType: "Bond",
            TypeName: "Bond",
            IssuerType: null,
            RiskCountry: "US",
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            Currency: "USD",
            Classification: Json("{}"),
            CommonTerms: Json($$"""{"displayName":"{{displayName}}","currency":"USD"}"""),
            EconomicTerms: Json($$"""{"couponRate":{{couponRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"""),
            Provenance: Json("{}"),
            Version: 1,
            EffectiveFrom: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            Identifiers: [],
            LegacyAssetClass: null,
            LegacyAssetSpecificTerms: null);

    public static SecurityTermsValidTimeSegment Segment(
        SecurityEconomicDefinitionRecord definition, long streamVersion, DateTimeOffset validFrom, DateTimeOffset? validTo)
        => new(definition.SecurityId, streamVersion, validFrom, validFrom, validTo, definition);

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static DateTimeOffset Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);
}

/// <summary>A fixed timeline, answering both reads by the ADR-034 rule.</summary>
internal sealed class FakeValidTimeReader : ISecurityTermsValidTimeReader
{
    private readonly IReadOnlyList<SecurityTermsValidTimeSegment> _timeline;

    public FakeValidTimeReader(params SecurityTermsValidTimeSegment[] timeline) => _timeline = timeline;

    public Task<SecurityTermsValidTimeSegment?> GetValidAtAsync(Guid securityId, DateTimeOffset validAt, DateTimeOffset? knownAt = null, CancellationToken ct = default)
        => Task.FromResult(_timeline.Where(segment => segment.SecurityId == securityId).LastOrDefault(segment => segment.ValidFrom <= validAt));

    public Task<IReadOnlyList<SecurityTermsValidTimeSegment>> GetTimelineAsync(Guid securityId, DateTimeOffset? knownAt = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SecurityTermsValidTimeSegment>>(_timeline.Where(segment => segment.SecurityId == securityId).ToArray());
}
