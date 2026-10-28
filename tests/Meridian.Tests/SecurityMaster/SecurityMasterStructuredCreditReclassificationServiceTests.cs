using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMasterStructuredCreditReclassificationServiceTests
{
    private static (SecurityMasterStructuredCreditReclassificationService Service, ISecurityMasterStore Store) BuildSut()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        var service = new SecurityMasterStructuredCreditReclassificationService(
            store, NullLogger<SecurityMasterStructuredCreditReclassificationService>.Instance);
        return (service, store);
    }

    [Fact]
    public async Task BuildReportAsync_ProposesBlockedCandidate_ForSecuritizedBondSubclass()
    {
        var (sut, store) = BuildSut();
        var bond = MakeSecurity(Guid.NewGuid(), "Bond", new
        {
            maturity = "2040-03-25",
            couponType = "Fixed",
            couponRate = 5.5,
            subclass = "AgencyMbs",
            par = 1000,
            seniority = "Senior"
        });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { bond });

        var report = await sut.BuildReportAsync();

        report.SecuritiesScanned.Should().Be(1);
        report.UnreadableCount.Should().Be(0);
        report.CandidateCount.Should().Be(1);
        report.ReadyCount.Should().Be(0);
        report.BlockedCount.Should().Be(1);

        var candidate = report.Candidates.Single();
        candidate.SecurityId.Should().Be(bond.SecurityId);
        candidate.SourceAssetClass.Should().Be("Bond");
        candidate.SourceLabel.Should().Be("AgencyMbs");
        candidate.Disposition.Should().Be(StructuredCreditReclassificationDisposition.Blocked);
        candidate.ProposedKind.Should().Be("AgencyMbs");
        candidate.Blockers.Select(static blocker => blocker.Code).Should().Equal(
            new[] { "structured_credit_tranche_required", "structured_credit_collateral_type_required" },
            "a bond carries no tranche or collateral description and the report never guesses them");

        var derived = candidate.DerivedAssetSpecificTerms;
        derived.GetProperty("originalFace").GetDecimal().Should().Be(1000m);
        derived.GetProperty("couponOrIndex").GetString().Should().Be("5.5");
        derived.GetProperty("maturity").GetString().Should().Be("2040-03-25");
        derived.GetProperty("structuredCreditKind").GetString().Should().Be("AgencyMbs");
        derived.GetProperty("seniority").GetString().Should().Be("Senior");
        derived.TryGetProperty("tranche", out _).Should().BeFalse("blocked proposals list only the terms the source carried");
        derived.TryGetProperty(SecurityTermsEnvelope.FormatKey, out _).Should().BeFalse("a partial draft is not a stamped terms document");
    }

    [Fact]
    public async Task BuildReportAsync_ProposesReadyCandidate_WithStampedTerms_ForCompleteCustomProfile()
    {
        var (sut, store) = BuildSut();
        var custom = MakeSecurity(Guid.NewGuid(), "CustomAsset", new
        {
            customProfileId = "legacy-clo",
            profileVersion = 2,
            category = "CLO",
            profileFields = new
            {
                tranche = "B",
                poolId = "CLO-2026-1",
                collateralType = "Leveraged loans",
                originalFace = 2500000,
                currentFactor = 0.91,
                couponOrIndex = "SOFR+325",
                maturity = "2037-10-20",
                seniority = "Mezzanine"
            }
        });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { custom });

        var report = await sut.BuildReportAsync();

        report.ReadyCount.Should().Be(1);
        report.BlockedCount.Should().Be(0);
        var candidate = report.Candidates.Single();
        candidate.SourceAssetClass.Should().Be("CustomAsset");
        candidate.SourceLabel.Should().Be("CLO");
        candidate.Disposition.Should().Be(StructuredCreditReclassificationDisposition.Ready);
        candidate.ProposedKind.Should().Be("Clo");
        candidate.Blockers.Should().BeEmpty();

        var terms = candidate.DerivedAssetSpecificTerms;
        terms.GetProperty(SecurityTermsEnvelope.FormatKey).GetString().Should().Be(SecurityTermsFormats.Flat);
        terms.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(
            SecurityAssetClassCatalog.GetOrDefault("StructuredCredit").TermsVersion,
            "a ready proposal is the exact document a governed amendment would persist");
        terms.GetProperty("tranche").GetString().Should().Be("B");
        terms.GetProperty("poolId").GetString().Should().Be("CLO-2026-1");
        terms.GetProperty("collateralType").GetString().Should().Be("Leveraged loans");
        terms.GetProperty("originalFace").GetDecimal().Should().Be(2500000m);
        terms.GetProperty("currentFactor").GetDecimal().Should().Be(0.91m);
        terms.GetProperty("couponOrIndex").GetString().Should().Be("SOFR+325");
        terms.GetProperty("structuredCreditKind").GetString().Should().Be("Clo");
        terms.GetProperty("seniority").GetString().Should().Be("Mezzanine");
        terms.GetProperty("factorScheduleEntries").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task BuildReportAsync_OrdersReadyBeforeBlocked_AndSkipsInactiveAndUnrelatedRecords()
    {
        var (sut, store) = BuildSut();
        var equity = MakeSecurity(Guid.NewGuid(), "Equity", new { classification = "Common" });
        var corporateBond = MakeSecurity(Guid.NewGuid(), "Bond", new { maturity = "2030-01-01", couponType = "Fixed", couponRate = 4, subclass = "Corporate" });
        var blockedBond = MakeSecurity(Guid.NewGuid(), "Bond", new { maturity = "2035-01-01", couponType = "Fixed", couponRate = 4, subclass = "Clo" });
        var readyCustom = MakeSecurity(Guid.NewGuid(), "CustomAsset", new
        {
            customProfileId = "legacy-abs",
            category = "ABS",
            profileFields = new { tranche = "A", collateralType = "Auto loans", originalFace = 100, couponOrIndex = "3.1" }
        });
        var inactive = MakeSecurity(Guid.NewGuid(), "Bond", new { maturity = "2035-01-01", subclass = "Cdo" }, SecurityStatusDto.Inactive);
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { equity, corporateBond, blockedBond, readyCustom, inactive });

        var report = await sut.BuildReportAsync();

        report.SecuritiesScanned.Should().Be(4, "inactive rows are not scanned");
        report.CandidateCount.Should().Be(2);
        report.Candidates.Select(static candidate => candidate.SecurityId)
            .Should().Equal(new[] { readyCustom.SecurityId, blockedBond.SecurityId }, "ready proposals lead the report");
    }

    [Fact]
    public async Task BuildReportAsync_CountsUnreadableRecords_WithoutFailingTheReport()
    {
        var (sut, store) = BuildSut();
        var unreadable = MakeSecurity(Guid.NewGuid(), "Bond", new { subclass = "Clo" });
        var candidate = MakeSecurity(Guid.NewGuid(), "Bond", new { maturity = "2035-01-01", couponType = "Fixed", couponRate = 4, subclass = "Clo" });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { unreadable, candidate });

        var report = await sut.BuildReportAsync();

        report.SecuritiesScanned.Should().Be(2);
        report.UnreadableCount.Should().Be(1, "a bond without a maturity cannot be mapped into the domain");
        report.Candidates.Should().ContainSingle(item => item.SecurityId == candidate.SecurityId);
    }

    [Fact]
    public async Task BuildReportAsync_BlocksABondDeclaringScheduleConventions_InsteadOfDroppingThem()
    {
        // A bond stamped 2 by a node that writes the block declares schedule conventions. For a
        // StructuredCredit lacks issueDate/paymentFrequency anchors, so carrying the block would
        // create a declared contract its schedule factory cannot generate.
        var (sut, store) = BuildSut();
        var bond = MakeSecurity(Guid.NewGuid(), "Bond", new
        {
            maturity = "2040-03-25",
            couponType = "Fixed",
            couponRate = 5.5,
            subclass = "AgencyMbs",
            par = 1000,
            seniority = "Senior",
            assetClassTermsVersion = 2,
            scheduleConventions = new { businessDayConvention = "ModifiedFollowing", paymentCalendars = new[] { "USNY" } }
        });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { bond });

        var report = await sut.BuildReportAsync();

        report.UnreadableCount.Should().Be(0, "this node reads a bond stamped at the version it reads through");
        report.ReadyCount.Should().Be(0);
        report.BlockedCount.Should().Be(1);

        var candidate = report.Candidates.Single();
        candidate.Disposition.Should().Be(StructuredCreditReclassificationDisposition.Blocked);
        candidate.Blockers.Select(static blocker => blocker.Code).Should().Equal(
            "structured_credit_schedule_conventions_unwritable",
            "structured_credit_tranche_required",
            "structured_credit_collateral_type_required");
        candidate.Blockers[0].Message.Should().Be(
            "The bond declares scheduleConventions, but the StructuredCredit schema has no top-level issueDate or paymentFrequency; " +
            "repair the declared schedule before reclassification.");

        var derived = candidate.DerivedAssetSpecificTerms;
        derived.TryGetProperty("scheduleConventions", out _).Should().BeFalse("a blocked draft never carries a term this node cannot write");
        derived.TryGetProperty(SecurityTermsEnvelope.FormatKey, out _).Should().BeFalse("no proposed terms are stamped for a blocked candidate");
        derived.TryGetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey, out _).Should().BeFalse();
    }

    [Fact]
    public async Task BuildReportAsync_CarriesBondCallSchedulesIntoTheRepairDraft_WithoutAnUnwritableBlocker()
    {
        // Call/put arrays are writable at StructuredCredit version 4. The tranche/collateral
        // repair blockers remain, while the complete declared call window survives in the draft.
        var (sut, store) = BuildSut();
        var bond = MakeSecurity(Guid.NewGuid(), "Bond", new
        {
            maturity = "2040-03-25",
            couponType = "Fixed",
            couponRate = 5.5,
            subclass = "AgencyMbs",
            par = 1000,
            seniority = "Senior",
            isCallable = true,
            callDate = "2030-03-25",
            assetClassTermsVersion = 3,
            callSchedule = new[] { new { callDate = "2030-03-25", callEndDate = "2031-03-25", exerciseStyle = "European", callPrice = 100, minimumNoticeDays = 5, maximumNoticeDays = 20, noticeDayType = "Calendar" } }
        });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { bond });

        var report = await sut.BuildReportAsync();

        report.UnreadableCount.Should().Be(0, "this node reads a bond stamped at the version it reads through");
        report.ReadyCount.Should().Be(0);
        report.BlockedCount.Should().Be(1);

        var candidate = report.Candidates.Single();
        candidate.Disposition.Should().Be(StructuredCreditReclassificationDisposition.Blocked);
        candidate.Blockers.Select(static blocker => blocker.Code).Should().Equal(
            "structured_credit_tranche_required",
            "structured_credit_collateral_type_required");
        var derived = candidate.DerivedAssetSpecificTerms;
        var call = derived.GetProperty(SecurityVersionedTerms.CallScheduleKey)[0];
        call.GetProperty("callEndDate").GetString().Should().Be("2031-03-25");
        call.GetProperty("minimumNoticeDays").GetInt32().Should().Be(5);
        call.GetProperty("maximumNoticeDays").GetInt32().Should().Be(20);
        call.GetProperty("noticeDayType").GetString().Should().Be("Calendar");
        derived.TryGetProperty("putSchedule", out _).Should().BeFalse();
        derived.TryGetProperty(SecurityTermsEnvelope.FormatKey, out _).Should().BeFalse("no proposed terms are stamped for a blocked candidate");
        derived.TryGetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey, out _).Should().BeFalse();
    }

    [Fact]
    public async Task BuildReportAsync_ABondDeclaringConventionsAndPuts_KeepsTheAnchorBlockerAndCarriesPuts()
    {
        // The unsupported-conventions blocker remains; writable put windows survive for repair.
        var (sut, store) = BuildSut();
        var bond = MakeSecurity(Guid.NewGuid(), "Bond", new
        {
            maturity = "2040-03-25",
            couponType = "Fixed",
            couponRate = 5.5,
            subclass = "AgencyMbs",
            par = 1000,
            seniority = "Senior",
            assetClassTermsVersion = 3,
            scheduleConventions = new { businessDayConvention = "ModifiedFollowing", paymentCalendars = new[] { "USNY" } },
            putSchedule = new[] { new { putDate = "2035-03-25", exerciseStyle = "European", putPrice = 100 } }
        });
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { bond });

        var report = await sut.BuildReportAsync();

        report.UnreadableCount.Should().Be(0);
        report.BlockedCount.Should().Be(1);

        var candidate = report.Candidates.Single();
        candidate.Disposition.Should().Be(StructuredCreditReclassificationDisposition.Blocked);
        candidate.Blockers.Select(static blocker => blocker.Code).Should().Equal(
            "structured_credit_schedule_conventions_unwritable",
            "structured_credit_tranche_required",
            "structured_credit_collateral_type_required");
        candidate.Blockers[0].Message.Should().Be(
            "The bond declares scheduleConventions, but the StructuredCredit schema has no top-level issueDate or paymentFrequency; " +
            "repair the declared schedule before reclassification.");

        var derived = candidate.DerivedAssetSpecificTerms;
        derived.TryGetProperty("scheduleConventions", out _).Should().BeFalse();
        derived.GetProperty(SecurityVersionedTerms.PutScheduleKey)[0].GetProperty("putDate").GetString().Should().Be("2035-03-25");
        derived.TryGetProperty(SecurityTermsEnvelope.FormatKey, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("nullBlockAtVersion2")]
    [InlineData("emptyBlockAtVersion2")]
    [InlineData("strayPopulatedBlockAtVersion1")]
    [InlineData("emptySchedulesAtVersion3")]
    [InlineData("undatedEmptyEntriesAtVersion3")]
    [InlineData("strayPopulatedSchedulesAtVersion2")]
    public async Task BuildReportAsync_BondDeclaringNoConventions_IsProposedExactlyAsAPlainBond(string variant)
    {
        // A block or schedule that declares nothing, and one on a row stamped below its version (never
        // decoded), leave the proposal exactly as it is for the same bond without the key.
        var (sut, store) = BuildSut();
        var plainTerms = new JsonObject
        {
            ["maturity"] = "2040-03-25",
            ["couponType"] = "Fixed",
            ["couponRate"] = 5.5,
            ["subclass"] = "AgencyMbs",
            ["par"] = 1000,
            ["seniority"] = "Senior"
        };
        var variantTerms = plainTerms.DeepClone().AsObject();
        switch (variant)
        {
            case "nullBlockAtVersion2":
                variantTerms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 2;
                variantTerms["scheduleConventions"] = null;
                break;
            case "emptyBlockAtVersion2":
                variantTerms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 2;
                variantTerms["scheduleConventions"] = new JsonObject { ["businessDayConvention"] = null, ["paymentCalendars"] = new JsonArray() };
                break;
            case "strayPopulatedBlockAtVersion1":
                variantTerms["scheduleConventions"] = new JsonObject { ["businessDayConvention"] = "ModifiedFollowing" };
                break;
            case "emptySchedulesAtVersion3":
                variantTerms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 3;
                variantTerms["callSchedule"] = new JsonArray();
                variantTerms["putSchedule"] = new JsonArray();
                break;
            case "undatedEmptyEntriesAtVersion3":
                variantTerms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 3;
                variantTerms["callSchedule"] = new JsonArray((JsonNode?)null, new JsonObject(), new JsonObject { ["callDate"] = null });
                variantTerms["putSchedule"] = new JsonArray(new JsonObject { ["putDate"] = null, ["putPrice"] = null });
                break;
            case "strayPopulatedSchedulesAtVersion2":
                variantTerms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 2;
                variantTerms["callSchedule"] = new JsonArray(new JsonObject { ["callDate"] = "2030-03-25", ["callPrice"] = 100 });
                variantTerms["putSchedule"] = new JsonArray(new JsonObject { ["putDate"] = "2035-03-25" });
                break;
        }

        var plain = MakeSecurity(Guid.NewGuid(), "Bond", plainTerms);
        var withVariant = MakeSecurity(Guid.NewGuid(), "Bond", variantTerms);
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { plain, withVariant });

        var report = await sut.BuildReportAsync();

        report.UnreadableCount.Should().Be(0);
        var expected = report.Candidates.Single(candidate => candidate.SecurityId == plain.SecurityId);
        var actual = report.Candidates.Single(candidate => candidate.SecurityId == withVariant.SecurityId);
        actual.Disposition.Should().Be(expected.Disposition);
        actual.ProposedKind.Should().Be(expected.ProposedKind);
        actual.Blockers.Should().Equal(expected.Blockers);
        actual.DerivedAssetSpecificTerms.GetRawText().Should().Be(expected.DerivedAssetSpecificTerms.GetRawText());
    }

    private static SecurityProjectionRecord MakeSecurity(
        Guid securityId,
        string assetClass,
        object assetSpecificTerms,
        SecurityStatusDto status = SecurityStatusDto.Active)
        => new(
            SecurityId: securityId,
            AssetClass: assetClass,
            Status: status,
            DisplayName: $"Test {assetClass}",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "TEST",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = $"Test {assetClass}", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(assetSpecificTerms),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test", updatedBy = "test", asOf = "2026-01-01T00:00:00+00:00" }),
            Version: 1,
            EffectiveFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            Identifiers: Array.Empty<SecurityIdentifierDto>(),
            Aliases: Array.Empty<SecurityAliasDto>());
}
