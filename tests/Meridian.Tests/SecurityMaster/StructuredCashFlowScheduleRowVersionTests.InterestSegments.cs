using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class StructuredCashFlowScheduleRowVersionTests
{
    [Fact]
    public void VersionOneWireBytesRemainFrozenWhenNewSegmentEvidenceIsPopulated()
    {
        const string historical = """{"PeriodDate":"2027-01-01T00:00:00+00:00","PrincipalAmount":0,"InterestAmount":5,"Factor":1,"RowVersion":1,"InterestMeasuredFrom":"2026-01-01","InterestMeasuredTo":"2027-01-01"}""";
        var row = JsonSerializer.Deserialize(historical,
            SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowScheduleEntry)!;
        JsonSerializer.Serialize(row, SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowScheduleEntry)
            .Should().Be(historical);
        var segments = new[] { new StructuredCashFlowInterestSegment(new(2026, 1, 1), new(2027, 1, 1),
            100m, 5m, 1m, new(2026, 1, 1), new(2027, 1, 1)) { FactorAfter = 1m } };
        var inconsistent = row with { InterestSegments = segments };

        JsonSerializer.Serialize(inconsistent, SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowScheduleEntry)
            .Should().Be(historical);
        SecurityCashFlowScheduleRowCodec.HasSupportedRows(WithRow(inconsistent), 1).Should().BeFalse();
        var frozen = SecurityCashFlowScheduleRowCodec.ForVersion(WithRow(inconsistent), 1).Schedule.Single();
        frozen.InterestSegments.Should().BeNull();
        JsonSerializer.Serialize(frozen, SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowScheduleEntry)
            .Should().Be(historical);

        var current = row with { RowVersion = 2, InterestSegments = segments };
        var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(current,
            SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowScheduleEntry),
            SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowScheduleEntry)!;
        restored.Should().Be(current);
        restored.GetHashCode().Should().Be(current.GetHashCode());
        (restored with { InterestSegments = [segments[0] with { ReferencePrincipal = 99m }] }).Should().NotBe(current);
    }

    [Fact]
    public async Task FactoredReceiptRetainsEverySegmentAndReplaysAfterRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cash-flow-segments-" + Guid.NewGuid().ToString("N"));
        try
        {
            var security = FactoredSecurity();
            var service = Service(new FileSecurityCashFlowReceiptStore(directory), security);
            var receipt = (await service.CaptureProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base,
                new BondCalculationContext(AsOf)))!;
            receipt.ModelVersion.Should().Be("bond-cash-flow-context-v5");
            receipt.Projection.BlockedReason.Should().BeNull();
            var coupon = receipt.Projection.Schedule.First(row => row.InterestAmount > 0m);
            coupon.InterestSegments.Should().HaveCount(2);
            coupon.InterestSegments!.Select(segment => segment.ReferencePrincipal).Should().Equal(800m, 600m);
            coupon.InterestSegments.Select(segment => segment.UnroundedInterestAmount).Should().Equal(20m, 15m);

            var path = Path.Combine(directory, receipt.ContentSha256 + ".json");
            var bytes = await File.ReadAllBytesAsync(path);
            var replay = await Service(new FileSecurityCashFlowReceiptStore(directory), security)
                .ReplayProjectionAsync(receipt.ContentSha256);
            replay!.BlockedReason.Should().BeNull();
            replay.Schedule.Should().Equal(receipt.Projection.Schedule);
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("Start", false)]
    [InlineData("End", true)]
    [InlineData("ReferencePrincipal", false)]
    [InlineData("ReferencePrincipal", true)]
    [InlineData("UnroundedInterestAmount", true)]
    [InlineData("Factor", true)]
    [InlineData("FactorAfter", true)]
    [InlineData("PeriodStart", true)]
    [InlineData("PeriodEnd", true)]
    [InlineData("RowVersion", true)]
    [InlineData("ModelVersion", true)]
    public async Task SegmentTamperingRefusesReplayEvenWithARecomputedDigest(string member, bool rehash)
    {
        var store = Substitute.For<ISecurityCashFlowReceiptStore>();
        var security = FactoredSecurity();
        var service = Service(store, security);
        var receipt = (await service.CaptureProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base,
            new BondCalculationContext(AsOf)))!;
        receipt.Projection.BlockedReason.Should().BeNull();
        var row = receipt.Projection.Schedule.First(candidate => candidate.InterestSegments is { Count: > 0 });
        var segment = row.InterestSegments![0];
        var changed = member switch
        {
            "Start" => segment with { Start = segment.Start.AddDays(1) },
            "End" => segment with { End = segment.End.AddDays(-1) },
            "ReferencePrincipal" => segment with { ReferencePrincipal = segment.ReferencePrincipal + 1m },
            "UnroundedInterestAmount" => segment with { UnroundedInterestAmount = segment.UnroundedInterestAmount + 1m },
            "Factor" => segment with { Factor = 0.7m },
            "FactorAfter" => segment with { FactorAfter = 0.5m },
            "PeriodStart" => segment with { PeriodStart = segment.PeriodStart.AddDays(-1) },
            "PeriodEnd" => segment with { PeriodEnd = segment.PeriodEnd.AddDays(1) },
            _ => segment
        };
        var changedRow = row with
        {
            InterestSegments = [changed, .. row.InterestSegments.Skip(1)],
            RowVersion = member == "RowVersion" ? 1 : row.RowVersion
        };
        var tampered = receipt with
        {
            ModelVersion = member == "ModelVersion" ? "bond-cash-flow-context-v4" : receipt.ModelVersion,
            Projection = receipt.Projection with
            { Schedule = receipt.Projection.Schedule.Select(candidate => candidate == row ? changedRow : candidate).ToArray() }
        };
        if (rehash)
            tampered = Rehash(tampered);
        store.GetAsync(tampered.ContentSha256, Arg.Any<CancellationToken>()).Returns(tampered);

        var replay = await service.ReplayProjectionAsync(tampered.ContentSha256);

        replay!.Schedule.Should().BeEmpty();
        replay.BlockedReason.Should().NotBeNull();
    }

    [Theory]
    [InlineData("Gap")]
    [InlineData("Overlap")]
    [InlineData("NegativePrincipal")]
    [InlineData("NegativeInterest")]
    [InlineData("BadFactor")]
    [InlineData("MissingClosingFactor")]
    [InlineData("IncreasingClosingFactor")]
    [InlineData("MismatchedClosingFactor")]
    [InlineData("Unreconciled")]
    public void InvalidSegmentEvidenceCannotBecomeASupportedCurrentRow(string defect)
    {
        var segment = new StructuredCashFlowInterestSegment(new(2026, 1, 1), new(2027, 1, 1),
            100m, 5m, 1m, new(2026, 1, 1), new(2027, 1, 1))
        { FactorAfter = 1m };
        var row = new StructuredCashFlowScheduleEntry(AsOf.AddYears(1), 0m, 5m, 1m)
        {
            RowVersion = 2,
            InterestMeasuredFrom = segment.Start,
            InterestMeasuredTo = segment.End,
            InterestSegments = defect switch
            {
                "Gap" => [segment with { Start = segment.Start.AddDays(1) }],
                "Overlap" => [segment, segment],
                "NegativePrincipal" => [segment with { ReferencePrincipal = -1m }],
                "NegativeInterest" => [segment with { UnroundedInterestAmount = -1m }],
                "BadFactor" => [segment with { Factor = 1.1m }],
                "MissingClosingFactor" => [segment with { FactorAfter = null }],
                "IncreasingClosingFactor" => [segment with { FactorAfter = 1.1m }],
                "MismatchedClosingFactor" => [
                    segment with { End = new(2026, 7, 1), UnroundedInterestAmount = 2.5m, FactorAfter = 0.8m },
                    segment with { Start = new(2026, 7, 1), UnroundedInterestAmount = 2.5m }
                ],
                _ => [segment with { UnroundedInterestAmount = 5.0001m }]
            }
        };

        SecurityCashFlowScheduleRowCodec.HasSupportedRows(WithRow(row), 2).Should().BeFalse();
    }

    private static StructuredCashFlowProjectionDto WithRow(StructuredCashFlowScheduleEntry row)
        => new(Security().SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
            StructuredCashFlowScenario.Base, AsOf, [row]);

    private static SecurityDetailDto FactoredSecurity() => Security() with
    {
        AssetClass = "StructuredCredit",
        AssetSpecificTerms = JsonSerializer.SerializeToElement(new
        {
            assetClassTermsVersion = 6,
            rateUnit = "Percent",
            issueDate = "2026-01-01",
            maturityDate = "2028-01-01",
            par = 1000m,
            fixedCouponRate = 5m,
            paymentFrequency = "Annual",
            dayCountConvention = "30/360",
            currentFactor = 0.8m,
            factorDate = "2026-01-01",
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2026-01-01", factor = 0.8m },
                new { asOfDate = "2026-07-01", factor = 0.6m }
            }
        })
    };
}
