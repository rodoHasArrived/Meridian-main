using System.Text.Json;
using Bogus;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.TestHelpers.Builders;

/// <summary>
/// Fluent builder for Security Master DTO test instances.
/// Generates <see cref="SecuritySummaryDto"/> and <see cref="SecurityDetailDto"/> records
/// with realistic random values for fields the test does not override.
/// </summary>
/// <example>
/// <code>
/// var summary = new SecurityBuilder().ForSymbol("AAPL").AsEquity().Build();
/// var detail = new SecurityBuilder().ForSymbol("AAPL").AsEquity().BuildDetail();
/// var securities = new SecurityBuilder().CreateManySummary(20);
/// </code>
/// </example>
public sealed class SecurityBuilder
{
    // Instance-level Faker so parallel test execution does not race on a shared static.
    private readonly Faker _faker = new();

    // Shared empty JSON element; JsonSerializer.SerializeToElement returns a self-contained
    // value whose lifetime is independent of any JsonDocument.
    private static readonly JsonElement EmptyJson =
        JsonSerializer.SerializeToElement(new { });

    private Guid _securityId = Guid.NewGuid();
    private string _assetClass = "Equity";
    private SecurityStatusDto _status = SecurityStatusDto.Active;
    private string? _displayName;
    private string? _primaryIdentifier;
    private string _currency = "USD";
    private long _version = 1L;

    /// <summary>
    /// Uses the ticker as the primary identifier and derives a plausible display name.
    /// </summary>
    public SecurityBuilder ForSymbol(string ticker)
    {
        _primaryIdentifier = ticker;
        _displayName ??= $"{ticker} Inc.";
        return this;
    }

    /// <summary>Sets the display name explicitly.</summary>
    public SecurityBuilder WithDisplayName(string displayName)
    {
        _displayName = displayName;
        return this;
    }

    /// <summary>Sets the security ID.</summary>
    public SecurityBuilder WithSecurityId(Guid securityId)
    {
        _securityId = securityId;
        return this;
    }

    /// <summary>Sets the asset class to "Equity" (default).</summary>
    public SecurityBuilder AsEquity()
    {
        _assetClass = "Equity";
        return this;
    }

    /// <summary>Sets the asset class to "Bond".</summary>
    public SecurityBuilder AsBond()
    {
        _assetClass = "Bond";
        return this;
    }

    /// <summary>Sets an arbitrary asset class string.</summary>
    public SecurityBuilder WithAssetClass(string assetClass)
    {
        _assetClass = assetClass;
        return this;
    }

    /// <summary>Sets the security status.</summary>
    public SecurityBuilder WithStatus(SecurityStatusDto status)
    {
        _status = status;
        return this;
    }

    /// <summary>Sets the currency (default: "USD").</summary>
    public SecurityBuilder WithCurrency(string currency)
    {
        _currency = currency;
        return this;
    }

    /// <summary>Sets the record version number.</summary>
    public SecurityBuilder WithVersion(long version)
    {
        _version = version;
        return this;
    }

    /// <summary>Builds a <see cref="SecuritySummaryDto"/>.</summary>
    public SecuritySummaryDto Build()
    {
        return new SecuritySummaryDto(
            SecurityId: _securityId,
            AssetClass: _assetClass,
            Status: _status,
            DisplayName: _displayName ?? _faker.Company.CompanyName(),
            PrimaryIdentifier: _primaryIdentifier ?? _faker.Finance.Currency().Code,
            Currency: _currency,
            Version: _version);
    }

    /// <summary>
    /// Builds a <see cref="SecurityDetailDto"/> with empty JSON term objects and a single
    /// primary ticker identifier.
    /// </summary>
    public SecurityDetailDto BuildDetail()
    {
        var identifier = new SecurityIdentifierDto(
            Kind: SecurityIdentifierKind.Ticker,
            Value: _primaryIdentifier ?? _faker.Finance.Currency().Code,
            IsPrimary: true,
            ValidFrom: DateTimeOffset.UtcNow.AddYears(-5));

        return new SecurityDetailDto(
            SecurityId: _securityId,
            AssetClass: _assetClass,
            Status: _status,
            DisplayName: _displayName ?? _faker.Company.CompanyName(),
            Currency: _currency,
            CommonTerms: EmptyJson,
            AssetSpecificTerms: EmptyJson,
            Identifiers: [identifier],
            Aliases: [],
            Version: _version,
            EffectiveFrom: DateTimeOffset.UtcNow.AddYears(-5),
            EffectiveTo: null);
    }

    /// <summary>
    /// Builds a sequence of <paramref name="count"/> <see cref="SecuritySummaryDto"/> records
    /// with distinct IDs and randomized tickers.
    /// </summary>
    public IReadOnlyList<SecuritySummaryDto> CreateManySummary(int count)
    {
        var results = new List<SecuritySummaryDto>(count);
        for (var i = 0; i < count; i++)
        {
            var ticker = _faker.Finance.Currency().Code + i;
            results.Add(new SecurityBuilder()
                .ForSymbol(ticker)
                .WithAssetClass(_assetClass)
                .WithStatus(_status)
                .WithCurrency(_currency)
                .Build());
        }
        return results;
    }
}
