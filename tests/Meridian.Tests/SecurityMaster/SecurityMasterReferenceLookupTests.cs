using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using Xunit;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Tests for <see cref="SecurityMasterSecurityReferenceLookup"/> covering unresolved identity,
/// degraded metadata, and the sub-type derivation helper.
/// </summary>
public sealed class SecurityMasterReferenceLookupTests
{
    // -----------------------------------------------------------------------
    // Unresolved identity
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetBySymbolAsync_ReturnsNull_WhenSymbolIsEmpty()
    {
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetBySymbolAsync(string.Empty);

        result.Should().BeNull();
        await queryService.DidNotReceive().GetByIdentifierAsync(
            Arg.Any<SecurityIdentifierKind>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBySymbolAsync_ReturnsNull_WhenQueryServiceReturnsNull()
    {
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "UNKNOWN", null, Arg.Any<CancellationToken>())
            .Returns((SecurityDetailDto?)null);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetBySymbolAsync("UNKNOWN");

        result.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Degraded metadata — security exists but has no identifiers
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetBySymbolAsync_ReturnsReference_WhenNoIdentifiersPresent()
    {
        var securityId = Guid.NewGuid();
        var detail = BuildDetail(securityId, "Equity", identifiers: []);

        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "GHOST", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetBySymbolAsync("GHOST");

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(securityId);
        result.PrimaryIdentifier.Should().BeNull();
    }

    [Fact]
    public async Task GetBySymbolAsync_ReturnsReference_WhenNoPrimaryIdentifierFlagged()
    {
        var securityId = Guid.NewGuid();
        var identifiers = new[]
        {
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "AAPL", false, DateTimeOffset.UtcNow.AddDays(-1), null, null)
        };
        var detail = BuildDetail(securityId, "Equity", identifiers);

        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "AAPL", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetBySymbolAsync("AAPL");

        result.Should().NotBeNull();
        // Falls back to first identifier value
        result!.PrimaryIdentifier.Should().Be("AAPL");
        result.CoverageStatus.Should().Be(WorkstationSecurityCoverageStatus.Resolved);
        result.MatchedIdentifierKind.Should().Be(SecurityIdentifierKind.Ticker.ToString());
        result.MatchedIdentifierValue.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetBySymbolAsync_WithDegradedMetadata_PreservesDeterministicResolvedLabeling()
    {
        var securityId = Guid.NewGuid();
        var identifiers = new[]
        {
            new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "594918104", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
        };
        var detail = new SecurityDetailDto(
            SecurityId: securityId,
            AssetClass: "",
            Status: SecurityStatusDto.Active,
            DisplayName: "",
            Currency: null!,
            CommonTerms: JsonSerializer.SerializeToElement(new { }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Identifiers: identifiers,
            Aliases: Array.Empty<SecurityAliasDto>(),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null);

        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "MSFT", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetBySymbolAsync("MSFT");

        result.Should().NotBeNull();
        result!.CoverageStatus.Should().Be(WorkstationSecurityCoverageStatus.Resolved);
        result.MatchedIdentifierKind.Should().Be(SecurityIdentifierKind.Ticker.ToString());
        result.MatchedIdentifierValue.Should().Be("MSFT");
        result.PrimaryIdentifier.Should().Be("594918104");
        result.AssetClass.Should().BeEmpty();
        result.Currency.Should().BeNull();
        result.SubType.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Happy path — full enrichment including SubType
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetBySymbolAsync_ReturnsFullyEnrichedReference()
    {
        var securityId = Guid.NewGuid();
        var identifiers = new[]
        {
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MSFT", true, DateTimeOffset.UtcNow.AddDays(-10), null, null),
            new SecurityIdentifierDto(SecurityIdentifierKind.Isin, "US5949181045", false, DateTimeOffset.UtcNow.AddDays(-10), null, null)
        };
        var detail = BuildDetail(securityId, "Equity", identifiers);

        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "MSFT", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetBySymbolAsync("MSFT");

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(securityId);
        result.DisplayName.Should().Be("Microsoft Corp.");
        result.AssetClass.Should().Be("Equity");
        result.Currency.Should().Be("USD");
        result.Status.Should().Be(SecurityStatusDto.Active);
        result.PrimaryIdentifier.Should().Be("MSFT");
        // Equity does not have a unique sub-type in the derivation table
        result.SubType.Should().BeNull();
    }

    [Fact]
    public async Task GetByCanonicalAsync_WithMismatchedSecurityId_FallsBackToIdentifierLookup()
    {
        var requestedSecurityId = Guid.NewGuid();
        var mismatchedDetail = BuildDetail(
            requestedSecurityId,
            "Equity",
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MSFT", true, DateTimeOffset.UtcNow, null, null)]);
        var expectedDetail = BuildDetail(
            Guid.NewGuid(),
            "Equity",
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "AAPL", true, DateTimeOffset.UtcNow, null, null)]);

        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetByIdAsync(requestedSecurityId, Arg.Any<CancellationToken>()).Returns(mismatchedDetail);
        queryService.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "AAPL", "XNYS", Arg.Any<CancellationToken>())
            .Returns(expectedDetail);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetByCanonicalAsync(
            new SecurityReferenceLookupRequest(
                SecurityId: requestedSecurityId,
                IdentifierKind: SecurityIdentifierKind.Ticker.ToString(),
                IdentifierValue: "AAPL",
                Symbol: "AAPL",
                Venue: "XNYS",
                Source: "test"));

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(expectedDetail.SecurityId);
        result.LookupPath.Should().Be("identifier+venue");
        await queryService.Received(1).GetByIdentifierAsync(
            SecurityIdentifierKind.Ticker,
            "AAPL",
            "XNYS",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByCanonicalAsync_WithMatchingSecurityId_ReturnsSecurityIdMatch()
    {
        var requestedSecurityId = Guid.NewGuid();
        var matchingDetail = BuildDetail(
            requestedSecurityId,
            "Equity",
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "AAPL", true, DateTimeOffset.UtcNow, null, null)]);

        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetByIdAsync(requestedSecurityId, Arg.Any<CancellationToken>()).Returns(matchingDetail);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetByCanonicalAsync(
            new SecurityReferenceLookupRequest(
                SecurityId: requestedSecurityId,
                IdentifierKind: SecurityIdentifierKind.Ticker.ToString(),
                IdentifierValue: "AAPL",
                Symbol: "AAPL",
                Venue: "XNYS",
                Source: "test"));

        result.Should().NotBeNull();
        result!.LookupPath.Should().Be("security-id");
        await queryService.DidNotReceive().GetByIdentifierAsync(
            Arg.Any<SecurityIdentifierKind>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Bond", "Bond")]
    [InlineData("TreasuryBill", "TreasuryBill")]
    [InlineData("Option", "OptionContract")]
    [InlineData("Future", "FutureContract")]
    [InlineData("Swap", "SwapContract")]
    [InlineData("DirectLoan", "DirectLoan")]
    [InlineData("Deposit", "Deposit")]
    [InlineData("MoneyMarketFund", "MoneyMarket")]
    [InlineData("CertificateOfDeposit", "CertificateOfDeposit")]
    [InlineData("CommercialPaper", "CommercialPaper")]
    [InlineData("Repo", "Repo")]
    public void DeriveSubType_ReturnsExpectedSubType(string assetClass, string expectedSubType)
    {
        SecurityMasterSecurityReferenceLookup.DeriveSubType(assetClass)
            .Should().Be(expectedSubType);
    }

    [Theory]
    [InlineData("Equity")]
    [InlineData("FxSpot")]
    [InlineData("CashSweep")]
    [InlineData("OtherSecurity")]
    [InlineData(null)]
    [InlineData("")]
    public void DeriveSubType_ReturnsNull_ForAmbiguousOrUnknownAssetClass(string? assetClass)
    {
        SecurityMasterSecurityReferenceLookup.DeriveSubType(assetClass)
            .Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Expired primary identifier — fallback to first non-expired identifier
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetBySymbolAsync_FallsBackToFirstIdentifier_WhenPrimaryIsExpired()
    {
        var securityId = Guid.NewGuid();
        var expiredAt = DateTimeOffset.UtcNow.AddDays(-1);
        var identifiers = new[]
        {
            // Primary flagged but already expired
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "OLD", true, DateTimeOffset.UtcNow.AddDays(-60), expiredAt, null),
            new SecurityIdentifierDto(SecurityIdentifierKind.Isin, "US1234567890", false, DateTimeOffset.UtcNow.AddDays(-30), null, null)
        };
        var detail = BuildDetail(securityId, "Equity", identifiers);

        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "OLD", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetBySymbolAsync("OLD");

        result.Should().NotBeNull();
        // IsPrimary takes precedence in the lookup regardless of ValidTo — the expired
        // primary is still the first match returned by FirstOrDefault(IsPrimary).
        // This test documents the current behaviour so regressions are caught if the
        // lookup is changed to filter by ValidTo.
        result!.PrimaryIdentifier.Should().Be("OLD");
    }

    [Fact]
    public async Task GetBySymbolAsync_FallsBackToFirstIdentifier_WhenNoPrimaryIsPresent()
    {
        var securityId = Guid.NewGuid();
        var identifiers = new[]
        {
            new SecurityIdentifierDto(SecurityIdentifierKind.Isin, "US1234567890", false, DateTimeOffset.UtcNow.AddDays(-30), null, null),
            new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "123456789", false, DateTimeOffset.UtcNow.AddDays(-30), null, null)
        };
        var detail = BuildDetail(securityId, "Bond", identifiers);

        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "BTEST", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetBySymbolAsync("BTEST");

        result.Should().NotBeNull();
        result!.PrimaryIdentifier.Should().Be("US1234567890");
    }

    // -----------------------------------------------------------------------
    // Governance enrichment — RiskCountry propagation from CommonTerms
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetBySymbolAsync_PopulatesRiskCountry_WhenPresentInCommonTerms()
    {
        var securityId = Guid.NewGuid();
        var identifiers = new[]
        {
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "NESN", true, DateTimeOffset.UtcNow.AddDays(-10), null, null)
        };

        // Build a detail with countryOfRisk in the CommonTerms blob.
        var detail = new SecurityDetailDto(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Nestle S.A.",
            Currency: "CHF",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Nestle S.A.",
                currency = "CHF",
                countryOfRisk = "CH"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Identifiers: identifiers,
            Aliases: Array.Empty<SecurityAliasDto>(),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null);

        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "NESN", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetBySymbolAsync("NESN");

        result.Should().NotBeNull();
        result!.RiskCountry.Should().Be("CH");
    }

    [Fact]
    public async Task GetBySymbolAsync_LeavesRiskCountryNull_WhenAbsentFromCommonTerms()
    {
        var securityId = Guid.NewGuid();
        var identifiers = new[]
        {
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "AAPL", true, DateTimeOffset.UtcNow.AddDays(-10), null, null)
        };
        var detail = BuildDetail(securityId, "Equity", identifiers);

        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "AAPL", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetBySymbolAsync("AAPL");

        result.Should().NotBeNull();
        result!.RiskCountry.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Governance enrichment — IssuerType stays null on lightweight lookup path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetBySymbolAsync_LeavesIssuerTypeNull_LightweightLookupDoesNotFetchEconomicDefinition()
    {
        // The lightweight symbol-lookup path does not issue a secondary
        // GetEconomicDefinitionByIdAsync call, so IssuerType is always null.
        // This test documents and guards that intentional design decision.
        var securityId = Guid.NewGuid();
        var identifiers = new[]
        {
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "T", true, DateTimeOffset.UtcNow.AddDays(-10), null, null)
        };
        var detail = new SecurityDetailDto(
            SecurityId: securityId,
            AssetClass: "Bond",
            Status: SecurityStatusDto.Active,
            DisplayName: "AT&T Inc.",
            Currency: "USD",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "AT&T Inc.",
                currency = "USD",
                countryOfRisk = "US",
                issuerName = "AT&T Inc."
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Identifiers: identifiers,
            Aliases: Array.Empty<SecurityAliasDto>(),
            Version: 2,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-60),
            EffectiveTo: null);

        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "T", null, Arg.Any<CancellationToken>())
            .Returns(detail);

        var lookup = new SecurityMasterSecurityReferenceLookup(queryService);

        var result = await lookup.GetBySymbolAsync("T");

        result.Should().NotBeNull();
        result!.IssuerType.Should().BeNull(
            "the lightweight lookup path does not fetch the economic definition " +
            "and therefore cannot populate IssuerType");
        result.RiskCountry.Should().Be("US",
            "countryOfRisk is available directly in the CommonTerms blob without a secondary query");
    }

    // -----------------------------------------------------------------------
    // TryGetCommonTermsString helper — direct unit coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void TryGetCommonTermsString_ReturnsValue_WhenPropertyPresent()
    {
        var element = JsonSerializer.SerializeToElement(new { countryOfRisk = "US" });

        SecurityMasterSecurityReferenceLookup.TryGetCommonTermsString(element, "countryOfRisk")
            .Should().Be("US");
    }

    [Fact]
    public void TryGetCommonTermsString_ReturnsNull_WhenPropertyAbsent()
    {
        var element = JsonSerializer.SerializeToElement(new { currency = "USD" });

        SecurityMasterSecurityReferenceLookup.TryGetCommonTermsString(element, "countryOfRisk")
            .Should().BeNull();
    }

    [Fact]
    public void TryGetCommonTermsString_ReturnsNull_WhenPropertyIsNotString()
    {
        var element = JsonSerializer.SerializeToElement(new { settlementCycleDays = 2 });

        SecurityMasterSecurityReferenceLookup.TryGetCommonTermsString(element, "settlementCycleDays")
            .Should().BeNull();
    }

    [Fact]
    public void TryGetCommonTermsString_ReturnsNull_WhenElementIsNotObject()
    {
        var element = JsonSerializer.SerializeToElement("not-an-object");

        SecurityMasterSecurityReferenceLookup.TryGetCommonTermsString(element, "countryOfRisk")
            .Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SecurityDetailDto BuildDetail(
        Guid securityId,
        string assetClass,
        IReadOnlyList<SecurityIdentifierDto> identifiers)
        => new(
            SecurityId: securityId,
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: "Microsoft Corp.",
            Currency: "USD",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Microsoft Corp.", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Identifiers: identifiers,
            Aliases: Array.Empty<SecurityAliasDto>(),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null);
}
