using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMasterDataQualityServiceTests
{
    private static (SecurityMasterDataQualityService Service, ISecurityMasterStore Store) BuildSut()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        var reportStore = Substitute.For<ISecurityMasterQualityReportStore>();
        reportStore.SaveAsync(Arg.Any<SecurityMasterQualityReportDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var service = new SecurityMasterDataQualityService(
            store, reportStore, NullLogger<SecurityMasterDataQualityService>.Instance);
        return (service, store);
    }

    [Fact]
    public async Task RunQualityChecksAsync_FlagsInvalidCurrencyCode()
    {
        var (sut, store) = BuildSut();
        var security = MakeSecurity(Guid.NewGuid(), "Equity", currency: "XXX");
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().Contain(v =>
            v.RuleId == "TX001"
            && v.Category == DataQualityRuleCategory.TaxonomyCheck
            && v.SecurityId == security.SecurityId);
    }

    [Fact]
    public async Task RunQualityChecksAsync_FlagsBondMissingMaturity()
    {
        var (sut, store) = BuildSut();
        var security = MakeSecurity(Guid.NewGuid(), "Bond", currency: "USD");
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().Contain(v =>
            v.RuleId == "MA001"
            && v.Category == DataQualityRuleCategory.MinimumAttribute
            && v.SecurityId == security.SecurityId);
    }

    [Fact]
    public async Task RunQualityChecksAsync_DoesNotFlagMaturity_ForEquity()
    {
        var (sut, store) = BuildSut();
        var security = MakeSecurity(Guid.NewGuid(), "Equity", currency: "USD");
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().NotContain(v => v.RuleId == "MA001");
    }

    [Fact]
    public async Task RunQualityChecksAsync_FlagsOptionMissingStrike()
    {
        var (sut, store) = BuildSut();
        var security = MakeSecurity(Guid.NewGuid(), "Option", currency: "USD",
            assetSpecificTerms: new { expirationDate = DateTimeOffset.UtcNow.AddDays(30) });
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().Contain(v =>
            v.RuleId == "MA003"
            && v.Category == DataQualityRuleCategory.MinimumAttribute);
    }

    [Fact]
    public async Task RunQualityChecksAsync_FlagsStaleSecurityTerms()
    {
        var (sut, store) = BuildSut();
        var security = MakeStaleSecurityTerms(Guid.NewGuid());
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().Contain(v =>
            v.RuleId == "SM001"
            && v.Category == DataQualityRuleCategory.StalenessMonitor);
    }

    [Fact]
    public async Task RunQualityChecksAsync_FlagsEffectiveToBeforeEffectiveFrom()
    {
        var (sut, store) = BuildSut();
        var security = MakeSecurity(Guid.NewGuid(), "Equity", currency: "USD");
        // Manually override to invalid date range
        var invalid = security with
        {
            EffectiveFrom = DateTimeOffset.UtcNow,
            EffectiveTo = DateTimeOffset.UtcNow.AddDays(-1)
        };
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { invalid });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().Contain(v =>
            v.RuleId == "VC001"
            && v.Category == DataQualityRuleCategory.ValueConsistency);
    }

    [Fact]
    public async Task RunQualityChecksAsync_SkipsInactiveSecurities()
    {
        var (sut, store) = BuildSut();
        var security = MakeSecurity(Guid.NewGuid(), "Bond", currency: "XXX") with
        {
            Status = SecurityStatusDto.Inactive
        };
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.SecuritiesScanned.Should().Be(0);
        report.Violations.Should().BeEmpty();
    }

    private static SecurityProjectionRecord MakeSecurity(
        Guid securityId,
        string assetClass,
        string currency,
        object? assetSpecificTerms = null)
        => new(
            SecurityId: securityId,
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: $"Test {assetClass}",
            Currency: currency,
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "TEST",
            CommonTerms: JsonSerializer.SerializeToElement(new { currency }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(assetSpecificTerms ?? new { }),
            Provenance: JsonSerializer.SerializeToElement(new { }),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null,
            Identifiers: Array.Empty<SecurityIdentifierDto>(),
            Aliases: Array.Empty<SecurityAliasDto>());

    private static SecurityProjectionRecord MakeStaleSecurityTerms(Guid securityId)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Stale Security",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "STALE",
            CommonTerms: JsonSerializer.SerializeToElement(new { currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Provenance: JsonSerializer.SerializeToElement(new { }),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-200),  // > 180-day staleness threshold
            EffectiveTo: null,
            Identifiers: Array.Empty<SecurityIdentifierDto>(),
            Aliases: Array.Empty<SecurityAliasDto>());
}
