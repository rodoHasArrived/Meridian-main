using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterQueryServiceEquityTermsTests
{
    [Fact]
    public async Task GetPreferredEquityTermsAsync_ReturnsPreferredTerms_ForConvertiblePreferredProjection()
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateEquityProjection(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    shareClass = "A",
                    classification = "ConvertiblePreferred",
                    preferredTerms = new
                    {
                        dividendRate = 6.25m,
                        dividendType = "Cumulative",
                        redemptionPrice = 25.00m,
                        redemptionDate = new DateOnly(2032, 1, 15),
                        callableDate = new DateOnly(2030, 1, 15),
                        participationTerms = new
                        {
                            participatesInCommonDividends = true,
                            additionalDividendThreshold = 1.50m
                        },
                        liquidationPreference = new
                        {
                            kind = "Senior",
                            multiple = 1.0m
                        }
                    },
                    convertibleTerms = new
                    {
                        underlyingSecurityId = Guid.NewGuid(),
                        conversionRatio = 2.5m
                    }
                })));

        var service = CreateQueryService(store);

        var result = await service.GetPreferredEquityTermsAsync(securityId);

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(securityId);
        result.Classification.Should().Be("ConvertiblePreferred");
        result.DividendRate.Should().Be(6.25m);
        result.DividendType.Should().Be("Cumulative");
        result.IsCumulative.Should().BeTrue();
        result.RedemptionPrice.Should().Be(25.00m);
        result.RedemptionDate.Should().Be(new DateOnly(2032, 1, 15));
        result.CallableDate.Should().Be(new DateOnly(2030, 1, 15));
        result.ParticipatesInCommonDividends.Should().BeTrue();
        result.AdditionalDividendThreshold.Should().Be(1.50m);
        result.LiquidationPreferenceKind.Should().Be("Senior");
        result.LiquidationPreferenceMultiple.Should().Be(1.0m);
        result.Version.Should().Be(7);
    }

    [Fact]
    public async Task GetConvertibleEquityTermsAsync_ReturnsConvertibleTerms_ForConvertiblePreferredProjection()
    {
        var securityId = Guid.NewGuid();
        var underlyingSecurityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateEquityProjection(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    shareClass = "A",
                    classification = "ConvertiblePreferred",
                    preferredTerms = new
                    {
                        dividendType = "Fixed",
                        liquidationPreference = new
                        {
                            kind = "Pari"
                        }
                    },
                    convertibleTerms = new
                    {
                        underlyingSecurityId,
                        conversionRatio = 3.0m,
                        conversionPrice = 48.00m,
                        conversionStartDate = new DateOnly(2027, 1, 15),
                        conversionEndDate = new DateOnly(2031, 12, 31)
                    }
                })));

        var service = CreateQueryService(store);

        var result = await service.GetConvertibleEquityTermsAsync(securityId);

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(securityId);
        result.Classification.Should().Be("ConvertiblePreferred");
        result.UnderlyingSecurityId.Should().Be(underlyingSecurityId);
        result.ConversionRatio.Should().Be(3.0m);
        result.ConversionPrice.Should().Be(48.00m);
        result.ConversionStartDate.Should().Be(new DateOnly(2027, 1, 15));
        result.ConversionEndDate.Should().Be(new DateOnly(2031, 12, 31));
        result.Version.Should().Be(7);
    }

    [Fact]
    public async Task GetPreferredEquityTermsAsync_ReturnsNull_WhenProjectionIsNotPreferred()
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateEquityProjection(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    shareClass = "Common",
                    classification = "Common"
                })));

        var service = CreateQueryService(store);

        var result = await service.GetPreferredEquityTermsAsync(securityId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetConvertibleEquityTermsAsync_ReturnsNull_WhenSecurityIsNotEquity()
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityProjectionRecord(
                SecurityId: securityId,
                AssetClass: "Bond",
                Status: SecurityStatusDto.Active,
                DisplayName: "Meridian Bond",
                Currency: "USD",
                PrimaryIdentifierKind: "Ticker",
                PrimaryIdentifierValue: "MBND",
                CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Meridian Bond", currency = "USD" }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new { schemaVersion = 1, maturity = new DateOnly(2030, 1, 1) }),
                Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test", updatedBy = "codex", asOf = DateTimeOffset.UtcNow }),
                Version: 2,
                EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
                EffectiveTo: null,
                Identifiers: new[] { new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MBND", true, DateTimeOffset.UtcNow.AddDays(-10), null, null) },
                Aliases: Array.Empty<SecurityAliasDto>()));

        var service = CreateQueryService(store);

        var result = await service.GetConvertibleEquityTermsAsync(securityId);

        result.Should().BeNull();
    }

    private static SecurityMasterQueryService CreateQueryService(ISecurityMasterStore store)
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        return new SecurityMasterQueryService(eventStore, store, rebuilder);
    }

    private static SecurityProjectionRecord CreateEquityProjection(Guid securityId, JsonElement assetSpecificTerms)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Meridian Preferred",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "MPFD",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Meridian Preferred",
                currency = "USD",
                exchange = "XNYS",
                lotSize = 100,
                tickSize = 0.01m
            }),
            AssetSpecificTerms: assetSpecificTerms,
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                updatedBy = "codex",
                asOf = DateTimeOffset.UtcNow
            }),
            Version: 7,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
            EffectiveTo: null,
            Identifiers: new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MPFD", true, DateTimeOffset.UtcNow.AddDays(-10), null, null)
            },
            Aliases: Array.Empty<SecurityAliasDto>());
}
