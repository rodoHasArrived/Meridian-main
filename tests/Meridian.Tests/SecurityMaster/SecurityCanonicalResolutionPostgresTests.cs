using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Review P1: provider ingests (Polygon, EDGAR) store tickers with their feed as provider, while order
/// admission, the historical symbol timeline and backtest corporate-action adjustment resolve with no
/// provider. Provider is identity only for provider-scoped kinds, so those consumers must still find
/// the security, and an identifier claimed by several securities must never resolve to one of them.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityCanonicalResolutionPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityCanonicalResolutionPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task Gate_AdmitsTickerMasteredByAProviderIngest()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var ticker = UniqueTicker("PG");
        var securityId = Guid.NewGuid();
        await store.UpsertProjectionAsync(Projection(securityId, ticker,
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, EffectiveFrom, null, "polygon")));

        var gate = new SecurityMasterGate((ISecurityResolver)new SecurityResolver(store), NullLogger<SecurityMasterGate>.Instance);

        var result = await gate.CheckAsync(ticker);

        result.IsApproved.Should().BeTrue("a ticker's provider is provenance, not identity");
    }

    [SecurityMasterDatabaseFact]
    public async Task ProviderSymbol_StillRequiresItsProvider()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var ticker = UniqueTicker("PS");
        var providerSymbol = $"{ticker}.POLY";
        var securityId = Guid.NewGuid();
        await store.UpsertProjectionAsync(Projection(securityId, ticker,
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, EffectiveFrom),
            new SecurityIdentifierDto(SecurityIdentifierKind.ProviderSymbol, providerSymbol, false, EffectiveFrom, null, "polygon")));
        var resolver = new SecurityResolver(store);

        var withProvider = await resolver.ResolveCanonicalAsync(Request(SecurityIdentifierKind.ProviderSymbol, providerSymbol, "polygon"));
        var otherProvider = await resolver.ResolveCanonicalAsync(Request(SecurityIdentifierKind.ProviderSymbol, providerSymbol, "bloomberg"));

        withProvider.Status.Should().Be(SecurityResolutionStatus.Resolved);
        withProvider.SecurityId.Should().Be(securityId);
        otherProvider.Status.Should().Be(SecurityResolutionStatus.NotFound);
    }

    [SecurityMasterDatabaseFact]
    public async Task SharedCusip_IsAmbiguous_AndTheGateRefusesNamingBothSecurities()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var cusip = $"9{Guid.NewGuid().ToString("N")[..7].ToUpperInvariant()}1";
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstTicker = UniqueTicker("A1");
        var secondTicker = UniqueTicker("A2");
        await store.UpsertProjectionAsync(Projection(first, firstTicker,
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, firstTicker, true, EffectiveFrom),
            new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, cusip, false, EffectiveFrom, null, "bloomberg")));
        await store.UpsertProjectionAsync(Projection(second, secondTicker,
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, secondTicker, true, EffectiveFrom),
            new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, cusip, false, EffectiveFrom, null, "edgar")));
        var resolver = new SecurityResolver(store);

        var resolution = await resolver.ResolveCanonicalAsync(Request(SecurityIdentifierKind.Cusip, cusip, null));
        var plain = await resolver.ResolveAsync(Request(SecurityIdentifierKind.Cusip, cusip, null));

        resolution.Status.Should().Be(SecurityResolutionStatus.Ambiguous);
        resolution.CandidateSecurityIds.Should().BeEquivalentTo([first, second]);
        plain.Should().BeNull("an ambiguous identifier never auto-picks a security");
    }

    [SecurityMasterDatabaseFact]
    public async Task Gate_RefusesAmbiguousTicker_NamingEveryClaimant()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var ticker = UniqueTicker("AM");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await store.UpsertProjectionAsync(Projection(first, ticker,
            new SecurityIdentifierDto(SecurityIdentifierKind.Isin, $"US{Guid.NewGuid().ToString("N")[..9].ToUpperInvariant()}1", true, EffectiveFrom),
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, false, EffectiveFrom, null, "polygon")));
        await store.UpsertProjectionAsync(Projection(second, ticker,
            new SecurityIdentifierDto(SecurityIdentifierKind.Isin, $"US{Guid.NewGuid().ToString("N")[..9].ToUpperInvariant()}2", true, EffectiveFrom),
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, false, EffectiveFrom, null, "edgar")));
        var gate = new SecurityMasterGate((ISecurityResolver)new SecurityResolver(store), NullLogger<SecurityMasterGate>.Instance);

        var result = await gate.CheckAsync(ticker);

        result.IsApproved.Should().BeFalse();
        result.Reason.Should().Contain("ambiguous").And.Contain(first.ToString()).And.Contain(second.ToString());
    }

    [SecurityMasterDatabaseFact]
    public async Task RecycledTicker_WithNoValidClaimOnTheDate_IsNotFound()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var ticker = UniqueTicker("RC");
        var securityId = Guid.NewGuid();
        var claimFrom = DateTimeOffset.UtcNow.AddDays(-30);
        var claimTo = DateTimeOffset.UtcNow.AddDays(-20);
        await store.UpsertProjectionAsync(Projection(securityId, $"{ticker}X",
            new SecurityIdentifierDto(SecurityIdentifierKind.Isin, $"US{Guid.NewGuid().ToString("N")[..9].ToUpperInvariant()}1", true, claimFrom),
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, false, claimFrom, claimTo, "polygon")));
        var resolver = new SecurityResolver(store);

        var during = await resolver.ResolveCanonicalAsync(Request(SecurityIdentifierKind.Ticker, ticker, null) with { AsOfUtc = claimFrom.AddDays(1) });
        var after = await resolver.ResolveCanonicalAsync(Request(SecurityIdentifierKind.Ticker, ticker, null));

        during.Status.Should().Be(SecurityResolutionStatus.Resolved);
        after.Status.Should().Be(SecurityResolutionStatus.NotFound);
    }

    private static readonly DateTimeOffset EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-60);

    private static string UniqueTicker(string prefix) => $"{prefix}{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private static ResolveSecurityRequest Request(SecurityIdentifierKind kind, string value, string? provider)
        => new(kind, value, provider, DateTimeOffset.UtcNow, ActiveOnly: true);

    private static SecurityProjectionRecord Projection(Guid securityId, string primaryTicker, params SecurityIdentifierDto[] identifiers)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: $"Canonical {primaryTicker}",
            Currency: "USD",
            PrimaryIdentifierKind: identifiers[0].Kind.ToString(),
            PrimaryIdentifierValue: identifiers[0].Value,
            CommonTerms: JsonSerializer.SerializeToElement(new { currency = "USD", exchange = "XNAS" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { termsFormat = SecurityTermsFormats.Flat, shareClass = "Common" }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test" }),
            Version: 1,
            EffectiveFrom: EffectiveFrom,
            EffectiveTo: null,
            Identifiers: identifiers,
            Aliases: []);
}
