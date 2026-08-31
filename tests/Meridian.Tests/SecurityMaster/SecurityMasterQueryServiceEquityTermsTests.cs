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
    public async Task EquityTermQueries_CustomClassification_ShouldReturnRawOtherLabel()
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
                    classification = "Other",
                    otherClassification = "CustomHybrid",
                    preferredTerms = new { dividendRate = 5.5m },
                    convertibleTerms = new
                    {
                        underlyingSecurityId,
                        conversionRatio = 1.5m
                    }
                })));
        var service = CreateQueryService(store);

        var preferred = await service.GetPreferredEquityTermsAsync(securityId);
        var convertible = await service.GetConvertibleEquityTermsAsync(securityId);

        preferred.Should().NotBeNull();
        preferred!.Classification.Should().Be("CustomHybrid");
        convertible.Should().NotBeNull();
        convertible!.Classification.Should().Be("CustomHybrid");
    }

    [Fact]
    public async Task GetConvertibleEquityTermsAsync_MalformedNumericClassification_ShouldRemainUnset()
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateEquityProjection(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    classification = 123,
                    convertibleTerms = new
                    {
                        underlyingSecurityId = Guid.NewGuid(),
                        conversionRatio = 1m
                    }
                })));

        var result = await CreateQueryService(store).GetConvertibleEquityTermsAsync(securityId);

        result.Should().NotBeNull();
        result!.Classification.Should().BeNull();
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

    [Fact]
    public async Task GetByIdentifierAsync_FallsBackToNormalizedUniverseMatch_WhenExactStoreLookupMisses()
    {
        var securityId = Guid.NewGuid();
        var projection = CreateEquityProjection(
            securityId,
            JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "Common",
                classification = "Common"
            })) with
        {
            Identifiers =
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Isin,
                    "us-0378331005",
                    true,
                    DateTimeOffset.UtcNow.AddDays(-10),
                    null,
                    null)
            ],
            PrimaryIdentifierKind = "Isin",
            PrimaryIdentifierValue = "us-0378331005"
        };

        var store = Substitute.For<ISecurityMasterStore>();
        store.GetByIdentifierAsync(
                SecurityIdentifierKind.Isin,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<DateTimeOffset>(),
                true,
                Arg.Any<CancellationToken>())
            .Returns((SecurityProjectionRecord?)null);
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns([projection]);

        var service = CreateQueryService(store);

        var result = await service.GetByIdentifierAsync(SecurityIdentifierKind.Isin, "US0378331005", null);

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(securityId);
        result.Identifiers.Should().ContainSingle();
        result.Identifiers[0].NormalizedValue.Should().Be("US0378331005");
    }

    [Fact]
    public async Task GetByIdentifierAsync_UniverseFallback_RejectsOmittedProviderForProviderScopedIdentifier()
    {
        var projection = CreateIsinProjection(Guid.NewGuid(), provider: "xnas", includeIdentifier: true);
        var store = CreateUniverseFallbackStore(projection);
        var service = CreateQueryService(store);

        var result = await service.GetByIdentifierAsync(
            SecurityIdentifierKind.Isin,
            "US0378331005",
            provider: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdentifierAsync_UniverseFallback_RejectsWrongProviderAndPrimaryFieldFallback()
    {
        var projection = CreateIsinProjection(Guid.NewGuid(), provider: "xnas", includeIdentifier: true);
        var store = CreateUniverseFallbackStore(projection);
        var service = CreateQueryService(store);

        var result = await service.GetByIdentifierAsync(
            SecurityIdentifierKind.Isin,
            "US0378331005",
            provider: "refinitiv");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdentifierAsync_UniverseFallback_ResolvesExactNormalizedProvider()
    {
        var securityId = Guid.NewGuid();
        var projection = CreateIsinProjection(securityId, provider: "xnas", includeIdentifier: true);
        var store = CreateUniverseFallbackStore(projection);
        var service = CreateQueryService(store);

        var result = await service.GetByIdentifierAsync(
            SecurityIdentifierKind.Isin,
            "us-0378331005",
            provider: " XnAs ");

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(securityId);
        result.Identifiers.Should().ContainSingle(identifier =>
            identifier.NormalizedValue == "US0378331005"
            && identifier.NormalizedProvider == "XNAS");
    }

    [Fact]
    public async Task GetByIdentifierAsync_UniverseFallback_PreservesProviderlessLegacyPrimaryMatch()
    {
        var securityId = Guid.NewGuid();
        var projection = CreateIsinProjection(securityId, provider: null, includeIdentifier: false);
        var store = CreateUniverseFallbackStore(projection);
        var service = CreateQueryService(store);

        var result = await service.GetByIdentifierAsync(
            SecurityIdentifierKind.Isin,
            "US0378331005",
            provider: null);

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(securityId);
        result.Identifiers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdentifierAsync_UsesRequestedAsOfTimestamp_ForEffectiveDatedLookup()
    {
        var securityId = Guid.NewGuid();
        var asOfUtc = new DateTimeOffset(2026, 5, 20, 15, 30, 0, TimeSpan.Zero);
        var projection = CreateEquityProjection(
            securityId,
            JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "Common",
                classification = "Common"
            }));

        var store = Substitute.For<ISecurityMasterStore>();
        store.GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker,
                "MPFD",
                null,
                asOfUtc,
                true,
                Arg.Any<CancellationToken>())
            .Returns(projection);

        var service = CreateQueryService(store);

        var result = await service.GetByIdentifierAsync(
            SecurityIdentifierKind.Ticker,
            "MPFD",
            null,
            asOfUtc: asOfUtc);

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(securityId);
        await store.Received(1).GetByIdentifierAsync(
            SecurityIdentifierKind.Ticker,
            "MPFD",
            null,
            asOfUtc,
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdentifierAsync_ResolvesOccOptionSymbol_FromNormalizedUniverseFallback()
    {
        var securityId = Guid.NewGuid();
        var projection = new SecurityProjectionRecord(
            SecurityId: securityId,
            AssetClass: "Option",
            Status: SecurityStatusDto.Active,
            DisplayName: "Apple Jun 2024 150 Call",
            Currency: "USD",
            PrimaryIdentifierKind: "OccOptionSymbol",
            PrimaryIdentifierValue: "aapl 240621 c00150000",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Apple Jun 2024 150 Call",
                currency = "USD"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                underlyingId = Guid.NewGuid(),
                putCall = "Call",
                strike = 150m,
                expiry = new DateOnly(2024, 6, 21),
                multiplier = 100m
            }),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                updatedBy = "codex",
                asOf = DateTimeOffset.UtcNow
            }),
            Version: 3,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.OccOptionSymbol,
                    "aapl 240621 c00150000",
                    true,
                    DateTimeOffset.UtcNow.AddDays(-30))
            ],
            Aliases: Array.Empty<SecurityAliasDto>());

        var store = Substitute.For<ISecurityMasterStore>();
        store.GetByIdentifierAsync(
                SecurityIdentifierKind.OccOptionSymbol,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<DateTimeOffset>(),
                true,
                Arg.Any<CancellationToken>())
            .Returns((SecurityProjectionRecord?)null);
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns([projection]);

        var service = CreateQueryService(store);

        var result = await service.GetByIdentifierAsync(
            SecurityIdentifierKind.OccOptionSymbol,
            "AAPL240621C00150000",
            null);

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(securityId);
        result.Identifiers.Should().ContainSingle();
        result.Identifiers[0].NormalizedValue.Should().Be("AAPL240621C00150000");
    }

    [Fact]
    public async Task GetByIdentifierAsync_UsesNormalizedValueAndProvider_BeforeUniverseFallback()
    {
        var securityId = Guid.NewGuid();
        var projection = CreateEquityProjection(
            securityId,
            JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "Common",
                classification = "Common"
            })) with
        {
            Identifiers =
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Isin,
                    "us-0378331005",
                    true,
                    DateTimeOffset.UtcNow.AddDays(-10),
                    null,
                    "xnas")
            ]
        };

        var store = Substitute.For<ISecurityMasterStore>();
        store.GetByIdentifierAsync(
                SecurityIdentifierKind.Isin,
                "US0378331005",
                "XNAS",
                Arg.Any<DateTimeOffset>(),
                true,
                Arg.Any<CancellationToken>())
            .Returns(projection);

        var service = CreateQueryService(store);

        var result = await service.GetByIdentifierAsync(SecurityIdentifierKind.Isin, "us-0378331005", "xnas");

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(securityId);
        result.Identifiers[0].NormalizedValue.Should().Be("US0378331005");
        result.Identifiers[0].NormalizedProvider.Should().Be("XNAS");
        await store.DidNotReceive().LoadAllAsync(Arg.Any<CancellationToken>());
    }

    private static SecurityMasterQueryService CreateQueryService(ISecurityMasterStore store)
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        return new SecurityMasterQueryService(eventStore, store, rebuilder);
    }

    private static ISecurityMasterStore CreateUniverseFallbackStore(SecurityProjectionRecord projection)
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetByIdentifierAsync(
                Arg.Any<SecurityIdentifierKind>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<DateTimeOffset>(),
                true,
                Arg.Any<CancellationToken>())
            .Returns((SecurityProjectionRecord?)null);
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns([projection]);
        return store;
    }

    private static SecurityProjectionRecord CreateIsinProjection(
        Guid securityId,
        string? provider,
        bool includeIdentifier)
        => CreateEquityProjection(
            securityId,
            JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "Common",
                classification = "Common"
            })) with
        {
            PrimaryIdentifierKind = SecurityIdentifierKind.Isin.ToString(),
            PrimaryIdentifierValue = "us-0378331005",
            Identifiers = includeIdentifier
                ? [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Isin,
                        "us-0378331005",
                        true,
                        DateTimeOffset.UtcNow.AddDays(-10),
                        Provider: provider)
                ]
                : []
        };

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
