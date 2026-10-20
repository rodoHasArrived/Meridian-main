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
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

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
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().Contain(v =>
            v.RuleId == "MA001"
            && v.Category == DataQualityRuleCategory.MinimumAttribute
            && v.SecurityId == security.SecurityId);
    }

    [Fact]
    public async Task RunQualityChecksAsync_ReportsMissingMaturity_WhenBondMaturityIsNotAString()
    {
        var (sut, store) = BuildSut();
        var security = MakeSecurity(
            Guid.NewGuid(),
            "Bond",
            currency: "USD",
            assetSpecificTerms: new { maturity = 123 });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

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
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().NotContain(v => v.RuleId == "MA001");
    }

    [Theory]
    [InlineData("FxForward")]
    [InlineData("NonDeliverableForward")]
    public async Task RunQualityChecksAsync_DoesNotFlagMaturity_ForForwardsDatedByValueDate(string assetClass)
    {
        // Forwards are dated by valueDate, which the terms schema already requires; MA001 reads only
        // maturity/maturityDate, so flagging them would raise a false error on every record.
        var (sut, store) = BuildSut();
        var security = MakeSecurity(Guid.NewGuid(), assetClass, currency: "USD",
            assetSpecificTerms: new { baseCurrency = "EUR", quoteCurrency = "USD", valueDate = "2026-09-02" });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().NotContain(v => v.RuleId == "MA001");
    }

    [Fact]
    public async Task RunQualityChecksAsync_ReportsMissingMaturity_ForCreditDefaultSwapWithoutMaturityDate()
    {
        var (sut, store) = BuildSut();
        var security = MakeSecurity(Guid.NewGuid(), "CreditDefaultSwap", currency: "USD",
            assetSpecificTerms: new { referenceEntity = "Meridian Industries Inc", protectionSide = "Buy" });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().Contain(v => v.RuleId == "MA001" && v.SecurityId == security.SecurityId);
    }

    [Fact]
    public async Task RunQualityChecksAsync_FlagsOptionMissingStrike()
    {
        var (sut, store) = BuildSut();
        var security = MakeSecurity(Guid.NewGuid(), "Option", currency: "USD",
            assetSpecificTerms: new { expiry = DateTimeOffset.UtcNow.AddDays(30) });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().Contain(v =>
            v.RuleId == "MA003"
            && v.Category == DataQualityRuleCategory.MinimumAttribute
            && v.FieldPath == "assetSpecificTerms.strike");
        report.Violations.Should().NotContain(v => v.RuleId == "MA003" && v.FieldPath == "assetSpecificTerms.expiry");
    }

    [Fact]
    public async Task RunQualityChecksAsync_RequiredTerms_ComeFromTheSchema_NotFromLegacySpellings()
    {
        // "expirationDate" is not a key the Option schema declares (the canonical mapper writes
        // "expiry"), so a row carrying only that spelling has no expiry as far as the schema is
        // concerned. An Equity row with no asset-specific terms at all declares nothing Required.
        var (sut, store) = BuildSut();
        var option = MakeSecurity(Guid.NewGuid(), "Option", currency: "USD",
            assetSpecificTerms: new { expirationDate = DateTimeOffset.UtcNow.AddDays(30), strike = 100m });
        var equity = MakeSecurity(Guid.NewGuid(), "Equity", currency: "USD");
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { option, equity });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().Contain(v =>
            v.RuleId == "MA003" && v.SecurityId == option.SecurityId && v.FieldPath == "assetSpecificTerms.expiry");
        report.Violations.Should().NotContain(v =>
            v.RuleId == "MA003" && v.SecurityId == option.SecurityId && v.FieldPath == "assetSpecificTerms.strike");
        report.Violations.Should().NotContain(v => v.RuleId == "MA003" && v.SecurityId == equity.SecurityId);
    }

    [Fact]
    public async Task RunQualityChecksAsync_FlagsExpiredWarrant_UnderTheSchemaExpiryKey()
    {
        // The expiry rule used to be gated on the Option class name, so an expired warrant (whose
        // schema declares the same "expiry" field) was never reported.
        var (sut, store) = BuildSut();
        var warrant = MakeSecurity(Guid.NewGuid(), "Warrant", currency: "USD",
            assetSpecificTerms: new
            {
                underlyingId = Guid.NewGuid(),
                warrantType = "Call",
                expiry = DateTimeOffset.UtcNow.AddDays(-5)
            });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { warrant });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().Contain(v =>
            v.RuleId == "VC003"
            && v.Category == DataQualityRuleCategory.ValueConsistency
            && v.SecurityId == warrant.SecurityId
            && v.FieldPath == "assetSpecificTerms.expiry");
        report.Violations.Should().NotContain(v => v.RuleId == "MA003" && v.SecurityId == warrant.SecurityId);
    }

    [Fact]
    public async Task RunQualityChecksAsync_FlagsStaleSecurityTerms()
    {
        var (sut, store) = BuildSut();
        var security = MakeStaleSecurityTerms(Guid.NewGuid());
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

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
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { invalid });

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
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { security });

        var report = await sut.RunQualityChecksAsync();

        report.SecuritiesScanned.Should().Be(0);
        report.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task RunQualityChecksAsync_ReadsRequiredTermsOfProfileBackedClasses_FromProfileFields()
    {
        // A profile-backed record persists its governed scalar terms beneath profileFields, so the
        // required-term rule must look there before declaring the envelope root incomplete.
        var (sut, store) = BuildSut();
        var complete = MakeSecurity(Guid.NewGuid(), "PrivateFundInterest", currency: "USD",
            assetSpecificTerms: new
            {
                customProfileId = "private-fund",
                profileVersion = 1,
                profileFields = new
                {
                    gpSponsor = "Meridian Capital",
                    strategy = "Buyout",
                    vintage = 2024,
                    commitment = 1_000_000m,
                    navDate = "2026-06-30"
                }
            });
        var incomplete = MakeSecurity(Guid.NewGuid(), "PrivateFundInterest", currency: "USD",
            assetSpecificTerms: new
            {
                customProfileId = "private-fund",
                profileVersion = 1,
                profileFields = new { strategy = "Buyout", vintage = 2024, commitment = 1_000_000m, navDate = "2026-06-30" }
            });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { complete, incomplete });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().NotContain(v => v.RuleId == "MA003" && v.SecurityId == complete.SecurityId);
        report.Violations.Should().ContainSingle(v => v.RuleId == "MA003" && v.SecurityId == incomplete.SecurityId)
            .Which.FieldPath.Should().Be("assetSpecificTerms.gpSponsor");
    }

    [Fact]
    public async Task RunQualityChecksAsync_ValidatesAnAliasedClass_AgainstItsCanonicalSchema()
    {
        // The schema is keyed by canonical names; a record carrying the PrivateFund alias is held
        // to PrivateFundInterest's required terms rather than escaping validation.
        var (sut, store) = BuildSut();
        var aliased = MakeSecurity(Guid.NewGuid(), "PrivateFund", currency: "USD",
            assetSpecificTerms: new
            {
                profileFields = new { strategy = "Buyout", vintage = 2024, commitment = 1_000_000m, navDate = "2026-06-30" }
            });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { aliased });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().ContainSingle(v => v.RuleId == "MA003" && v.SecurityId == aliased.SecurityId)
            .Which.FieldPath.Should().Be("assetSpecificTerms.gpSponsor");
    }

    [Fact]
    public async Task RunQualityChecksAsync_HoldsNumbersToTheDeclaredNumericType_AndProfileFieldsToProfileBackedClasses()
    {
        var (sut, store) = BuildSut();
        // A fractional integer is unreadable to the canonical mapper's TryGetInt32, so it is missing.
        var fractionalVintage = MakeSecurity(Guid.NewGuid(), "PrivateFundInterest", currency: "USD",
            assetSpecificTerms: new
            {
                profileFields = new { gpSponsor = "Meridian Capital", strategy = "Buyout", vintage = 2024.5, commitment = 1_000_000m, navDate = "2026-06-30" }
            });
        // A Future's mapper reads the envelope root only; terms hidden under profileFields do not count.
        var futureUnderProfileFields = MakeSecurity(Guid.NewGuid(), "Future", currency: "USD",
            assetSpecificTerms: new
            {
                profileFields = new { rootSymbol = "ES", contractMonth = "Z6", multiplier = 50m, expiry = "2091-12-19" }
            });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { fractionalVintage, futureUnderProfileFields });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().ContainSingle(v => v.RuleId == "MA003" && v.SecurityId == fractionalVintage.SecurityId)
            .Which.FieldPath.Should().Be("assetSpecificTerms.vintage");
        report.Violations.Where(v => v.RuleId == "MA003" && v.SecurityId == futureUnderProfileFields.SecurityId)
            .Select(v => v.FieldPath)
            .Should().BeEquivalentTo(
                "assetSpecificTerms.rootSymbol", "assetSpecificTerms.contractMonth",
                "assetSpecificTerms.expiry", "assetSpecificTerms.multiplier");
    }

    [Fact]
    public async Task RunQualityChecksAsync_DoesNotReportADateOnlyExpiryAsExpired_OnItsOwnDate()
    {
        // The schema's expiry is a date; a date-only value reads as midnight UTC, so comparing it to
        // the current instant would report an instrument expiring today as already expired.
        var (sut, store) = BuildSut();
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var expiresToday = MakeSecurity(Guid.NewGuid(), "Future", currency: "USD",
            assetSpecificTerms: new { rootSymbol = "ES", contractMonth = "Z6", multiplier = 50m, expiry = today });
        var expiredYesterday = MakeSecurity(Guid.NewGuid(), "Future", currency: "USD",
            assetSpecificTerms: new
            {
                rootSymbol = "ES",
                contractMonth = "Z6",
                multiplier = 50m,
                expiry = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd")
            });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { expiresToday, expiredYesterday });

        var report = await sut.RunQualityChecksAsync();

        report.Violations.Should().NotContain(v => v.RuleId == "VC003" && v.SecurityId == expiresToday.SecurityId);
        report.Violations.Should().Contain(v => v.RuleId == "VC003" && v.SecurityId == expiredYesterday.SecurityId);
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
