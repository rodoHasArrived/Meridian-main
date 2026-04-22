using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
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
            Currency: null,
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
