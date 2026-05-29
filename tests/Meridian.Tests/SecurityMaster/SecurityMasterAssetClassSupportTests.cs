using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterAssetClassSupportTests
{
    [Theory]
    [InlineData("Deposit")]
    [InlineData("MoneyMarketFund")]
    [InlineData("CertificateOfDeposit")]
    [InlineData("CommercialPaper")]
    [InlineData("TreasuryBill")]
    [InlineData("Repo")]
    [InlineData("CashSweep")]
    [InlineData("OtherSecurity")]
    [InlineData("Commodity")]
    [InlineData("CryptoCurrency")]
    [InlineData("Cfd")]
    [InlineData("Warrant")]
    public async Task CreateAsync_SupportsCashAndShortTermSecurityAssetClasses(string assetClass)
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);

        var detail = await service.CreateAsync(
            new CreateSecurityRequest(
                securityId,
                assetClass,
                JsonSerializer.SerializeToElement(new
                {
                    displayName = $"{assetClass} Test Security",
                    currency = "USD",
                    issuerName = "Meridian Treasury"
                }),
                CreateAssetSpecificTerms(assetClass),
                new[]
                {
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.InternalCode,
                        $"{assetClass.ToUpperInvariant()}-{securityId:N}",
                        true,
                        DateTimeOffset.UtcNow.AddDays(-1),
                        null,
                        null)
                },
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "asset class support"),
            CancellationToken.None);

        detail.AssetClass.Should().Be(assetClass);
        detail.DisplayName.Should().Be($"{assetClass} Test Security");
        detail.AssetSpecificTerms.TryGetProperty("schemaVersion", out var schemaVersion).Should().BeTrue();
        schemaVersion.GetInt32().Should().Be(1);

        await eventStore.Received(1).AppendAsync(
            securityId,
            0,
            Arg.Is<IReadOnlyList<SecurityMasterEventEnvelope>>(events =>
                events.Count == 1 &&
                events[0].EventType == "SecurityCreated" &&
                PayloadHasCanonicalClassification(events[0].Payload, assetClass)),
            Arg.Any<CancellationToken>());

        await store.Received(1).UpsertProjectionAsync(
            Arg.Is<SecurityProjectionRecord>(projection => projection.AssetClass == assetClass),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ProfileBackedCustomAsset_PreservesPinnedProfileTerms()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);
        var assetSpecificTerms = CreatePrivateFundProfileTerms("2026-04-30");

        var detail = await service.CreateAsync(
            new CreateSecurityRequest(
                securityId,
                "CustomAsset",
                JsonSerializer.SerializeToElement(new
                {
                    displayName = "Meridian Private Credit Fund I",
                    currency = "USD",
                    issuerName = "GP Capital"
                }),
                assetSpecificTerms,
                new[]
                {
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.InternalCode,
                        "PFI-001",
                        true,
                        DateTimeOffset.UtcNow.AddDays(-1),
                        null,
                        null)
                },
                DateTimeOffset.UtcNow,
                "profile-wizard",
                "codex",
                "PROFILE-CREATE-1",
                "profile backed create"),
            CancellationToken.None);

        detail.AssetClass.Should().Be("CustomAsset");
        detail.AssetSpecificTerms.GetProperty("customProfileId").GetString().Should().Be("private-fund-interest");
        detail.AssetSpecificTerms.GetProperty("profileVersion").GetInt32().Should().Be(1);
        detail.AssetSpecificTerms.GetProperty("profileFields").GetProperty("navDate").GetString().Should().Be("2026-04-30");

        await store.Received(1).UpsertProjectionAsync(
            Arg.Is<SecurityProjectionRecord>(projection =>
                projection.AssetClass == "CustomAsset"
                && ProjectionCarriesPinnedProfileTerms(projection, "private-fund-interest", "2026-04-30")),
            Arg.Any<CancellationToken>());

        await eventStore.Received(1).AppendAsync(
            securityId,
            0,
            Arg.Is<IReadOnlyList<SecurityMasterEventEnvelope>>(events =>
                events.Count == 1
                && events[0].EventType == "SecurityCreated"
                && PayloadCarriesPinnedProfileTerms(events[0].Payload, "CustomAsset", "private-fund-interest", "2026-04-30")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_ProfileBackedCustomAsset_PreservesPinnedProfileTerms()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        eventStore.LoadAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]));
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        snapshotStore.LoadAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SecuritySnapshotRecord?>(null));
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProfileBackedProjection(securityId, CreatePrivateFundProfileTerms("2026-04-30")));
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);
        var amendedTerms = CreatePrivateFundProfileTerms("2026-05-31");

        var detail = await service.AmendTermsAsync(
            new AmendSecurityTermsRequest(
                securityId,
                1,
                null,
                amendedTerms,
                Array.Empty<SecurityIdentifierDto>(),
                Array.Empty<SecurityIdentifierDto>(),
                DateTimeOffset.UtcNow,
                "profile-wizard",
                "codex",
                "PROFILE-AMEND-1",
                "update NAV date"),
            CancellationToken.None);

        detail.AssetClass.Should().Be("CustomAsset");
        detail.Version.Should().Be(2);
        detail.AssetSpecificTerms.GetProperty("customProfileId").GetString().Should().Be("private-fund-interest");
        detail.AssetSpecificTerms.GetProperty("profileFields").GetProperty("navDate").GetString().Should().Be("2026-05-31");

        await store.Received(1).UpsertProjectionAsync(
            Arg.Is<SecurityProjectionRecord>(projection =>
                projection.AssetClass == "CustomAsset"
                && projection.Version == 2
                && ProjectionCarriesPinnedProfileTerms(projection, "private-fund-interest", "2026-05-31")),
            Arg.Any<CancellationToken>());

        await eventStore.Received(1).AppendAsync(
            securityId,
            1,
            Arg.Is<IReadOnlyList<SecurityMasterEventEnvelope>>(events =>
                events.Count == 1
                && events[0].EventType == "TermsAmended"
                && PayloadCarriesPinnedProfileTerms(events[0].Payload, "CustomAsset", "private-fund-interest", "2026-05-31")),
            Arg.Any<CancellationToken>());
    }

    private static JsonElement CreateAssetSpecificTerms(string assetClass)
        => assetClass switch
        {
            "Deposit" => JsonSerializer.SerializeToElement(new
            {
                depositType = "TimeDeposit",
                institutionName = "Meridian Bank",
                maturity = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
                interestRate = 0.045m,
                dayCount = "ACT/360",
                isCallable = false
            }),
            "MoneyMarketFund" => JsonSerializer.SerializeToElement(new
            {
                fundFamily = "Meridian Liquidity",
                sweepEligible = true,
                weightedAverageMaturityDays = 32,
                liquidityFeeEligible = true
            }),
            "CertificateOfDeposit" => JsonSerializer.SerializeToElement(new
            {
                issuerName = "Meridian Bank",
                maturity = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(12)),
                couponRate = 0.051m,
                callableDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
                dayCount = "30/360"
            }),
            "CommercialPaper" => JsonSerializer.SerializeToElement(new
            {
                issuerName = "Meridian Funding LLC",
                maturity = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90)),
                discountRate = 0.049m,
                dayCount = "ACT/360",
                isAssetBacked = true
            }),
            "TreasuryBill" => JsonSerializer.SerializeToElement(new
            {
                maturity = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
                auctionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
                cusip = "912797NA0",
                discountRate = 0.043m
            }),
            "Repo" => JsonSerializer.SerializeToElement(new
            {
                counterparty = "Primary Dealer A",
                startDate = DateOnly.FromDateTime(DateTime.UtcNow),
                endDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                repoRate = 0.052m,
                collateralType = "UST",
                haircut = 0.02m
            }),
            "CashSweep" => JsonSerializer.SerializeToElement(new
            {
                programName = "Brokerage Sweep",
                sweepVehicleType = "MMF",
                sweepFrequency = "Daily",
                targetAccountType = "Margin",
                yieldRate = 0.038m
            }),
            "OtherSecurity" => JsonSerializer.SerializeToElement(new
            {
                category = "CashEquivalent",
                subType = "TreasurySweep",
                maturity = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                issuerName = "US Treasury",
                settlementType = "T+1"
            }),
            "Commodity" => JsonSerializer.SerializeToElement(new
            {
                commodityType = "Metal",
                denomination = "USD/troy oz",
                contractSize = 100m
            }),
            "CryptoCurrency" => JsonSerializer.SerializeToElement(new
            {
                baseCurrency = "BTC",
                quoteCurrency = "USD",
                network = "Bitcoin"
            }),
            "Cfd" => JsonSerializer.SerializeToElement(new
            {
                underlyingAssetClass = "Equity",
                underlyingDescription = "S&P 500 CFD",
                leverage = 10m
            }),
            "Warrant" => JsonSerializer.SerializeToElement(new
            {
                underlyingId = Guid.NewGuid(),
                warrantType = "Call",
                strike = 150m,
                expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(12)),
                multiplier = 1m
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(assetClass), assetClass, "Unsupported test asset class.")
        };

    private static bool PayloadHasCanonicalClassification(JsonElement payload, string assetClass)
        => payload.TryGetProperty("classification", out var classification)
           && payload.TryGetProperty("legacyAssetClass", out var legacyAssetClass)
           && string.Equals(legacyAssetClass.GetString(), assetClass, StringComparison.Ordinal)
           && classification.TryGetProperty("subType", out _)
           && payload.TryGetProperty("economicTerms", out var economicTerms)
           && economicTerms.TryGetProperty("schemaVersion", out var schemaVersion)
           && schemaVersion.GetInt32() == 2;

    private static JsonElement CreatePrivateFundProfileTerms(string navDate)
        => JsonSerializer.SerializeToElement(new
        {
            schemaVersion = SecurityMasterSchemaVersions.CustomAssetProfileTerms,
            customProfileId = "private-fund-interest",
            profileVersion = 1,
            category = "PrivateFunds",
            subType = "Limited Partner Interest",
            valuationProfile = new { pricingSource = "AdministratorNAV" },
            accountingClassification = "PrivateInvestment",
            profileApproval = new
            {
                approvedBy = "risk-committee",
                approvedAtUtc = DateTimeOffset.Parse("2026-05-29T00:00:00Z"),
                approvalReference = "PROFILE-APPROVAL-1"
            },
            profileFields = new
            {
                gpSponsor = "GP Capital",
                strategy = "Private Credit",
                vintage = 2025,
                commitment = 1000000m,
                fundedAmount = 250000m,
                unfundedAmount = 750000m,
                navDate,
                lockup = "3 years"
            }
        });

    private static SecurityProjectionRecord CreateProfileBackedProjection(Guid securityId, JsonElement assetSpecificTerms)
    {
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        return new SecurityProjectionRecord(
            securityId,
            "CustomAsset",
            SecurityStatusDto.Active,
            "Meridian Private Credit Fund I",
            "USD",
            SecurityIdentifierKind.InternalCode.ToString(),
            "PFI-001",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Meridian Private Credit Fund I",
                currency = "USD",
                issuerName = "GP Capital"
            }),
            assetSpecificTerms,
            JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "profile-wizard",
                sourceRecordId = "PROFILE-CREATE-1",
                asOf = effectiveFrom,
                updatedBy = "codex",
                reason = "profile backed create"
            }),
            1,
            effectiveFrom,
            null,
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.InternalCode,
                    "PFI-001",
                    true,
                    effectiveFrom,
                    null,
                    null)
            ],
            []);
    }

    private static bool ProjectionCarriesPinnedProfileTerms(SecurityProjectionRecord projection, string profileId, string navDate)
        => projection.AssetSpecificTerms.TryGetProperty("customProfileId", out var customProfileId)
           && customProfileId.GetString() == profileId
           && projection.AssetSpecificTerms.TryGetProperty("profileVersion", out var profileVersion)
           && profileVersion.GetInt32() == 1
           && projection.AssetSpecificTerms.TryGetProperty("profileFields", out var profileFields)
           && profileFields.TryGetProperty("navDate", out var navDateElement)
           && navDateElement.GetString() == navDate;

    private static bool PayloadCarriesPinnedProfileTerms(
        JsonElement payload,
        string legacyAssetClass,
        string profileId,
        string navDate)
        => payload.TryGetProperty("legacyAssetClass", out var legacyAssetClassElement)
           && legacyAssetClassElement.GetString() == legacyAssetClass
           && payload.TryGetProperty("legacyAssetSpecificTerms", out var legacyTerms)
           && legacyTerms.TryGetProperty("customProfileId", out var customProfileId)
           && customProfileId.GetString() == profileId
           && legacyTerms.TryGetProperty("profileVersion", out var profileVersion)
           && profileVersion.GetInt32() == 1
           && legacyTerms.TryGetProperty("profileFields", out var profileFields)
           && profileFields.TryGetProperty("navDate", out var navDateElement)
           && navDateElement.GetString() == navDate;
}
