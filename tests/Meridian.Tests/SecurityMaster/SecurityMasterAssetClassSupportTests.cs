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
}
