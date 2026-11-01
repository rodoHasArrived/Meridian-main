using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Curves;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class BondCalculationReplayTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordedCutoffUsesRetainedTermsAndNeverReloadsCurrentTerms()
    {
        var retained = Bond(5m);
        var current = Bond(9m) with { Version = 2 };
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(retained.SecurityId, Arg.Any<CancellationToken>()).Returns(current);
        query.GetRecordedByIdAsOfAsync(retained.SecurityId, AsOf, Arg.Any<CancellationToken>()).Returns(retained);
        var service = Service(query);

        var result = await service.GetProjectionAsync(retained.SecurityId, StructuredCashFlowScenario.Base,
            new BondCalculationContext(AsOf, KnownAt: AsOf));

        result!.BlockedReason.Should().BeNull();
        result.Schedule.Sum(row => row.InterestAmount).Should().Be(10m);
        result.InputManifest!.Single(input => input.Kind == "SecurityTerms").Version.Should().Be(1);
        await query.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingHistoryAndPostCutoffSourceAreExplicitlyBlocked()
    {
        var security = Bond(5m);
        var query = Substitute.For<ISecurityMasterQueryService>();
        var missing = await Service(query).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base,
            new BondCalculationContext(AsOf, KnownAt: AsOf));
        missing!.BlockedReason.Should().Contain("No retained Security Master");

        var source = new SecurityCashFlowSourceDto(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
            AsOf.AddDays(1), false, null, null);
        var blocked = await Service(query, source: source).GetProjectionAsync(security, StructuredCashFlowScenario.Base,
            new BondCalculationContext(AsOf, KnownAt: AsOf));
        blocked!.Schedule.Should().BeEmpty();
        blocked.BlockedReason.Should().Contain("source assignment");
    }

    [Fact]
    public async Task RetainedReceiptSurvivesRestartAndTermChanges_AndRefusesTampering()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bond-replay-" + Guid.NewGuid().ToString("N"));
        try
        {
            var security = Bond(5m);
            var query = Substitute.For<ISecurityMasterQueryService>();
            query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
            var service = Service(query, receipts: new FileSecurityCashFlowReceiptStore(directory));
            var receipt = await service.CaptureProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base,
                new BondCalculationContext(AsOf));
            receipt!.Projection.BlockedReason.Should().BeNull();
            receipt.Projection.InputManifest!.Single(input => input.Kind == "SecurityTerms").ContentSha256.Should().HaveLength(64);

            query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(Bond(9m) with { Version = 2 });
            var restarted = Service(Substitute.For<ISecurityMasterQueryService>(), receipts: new FileSecurityCashFlowReceiptStore(directory));
            var replay = await restarted.ReplayProjectionAsync(receipt.ContentSha256);
            replay!.BlockedReason.Should().BeNull();
            replay.Schedule.Should().Equal(receipt.Projection.Schedule);

            // Caller-supplied replacement terms cannot replace the retained identity.
            var arbitrary = receipt with { Security = Bond(99m) };
            var retainedReplay = await restarted.ReplayProjectionAsync(arbitrary);
            retainedReplay.Schedule.Should().Equal(receipt.Projection.Schedule);

            var path = Path.Combine(directory, receipt.ContentSha256 + ".json");
            var json = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, json.Replace($"\"ModelVersion\":\"{SecurityMasterCashFlowService.ContextCalculationVersion}\"", "\"ModelVersion\":\"tampered\"", StringComparison.Ordinal));
            var act = () => restarted.ReplayProjectionAsync(receipt.ContentSha256);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task UnretainedCallerReceiptCannotBeReplayed()
    {
        var security = Bond(5m);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var service = Service(query);
        var receipt = await service.CaptureProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf));
        var replay = await service.ReplayProjectionAsync(receipt!);
        replay.Schedule.Should().BeEmpty();
        replay.BlockedReason.Should().Contain("not retained");
    }

    [Fact]
    public async Task ChangedCalendarContentBlocksReceiptReplay()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bond-calendar-replay-" + Guid.NewGuid().ToString("N"));
        try
        {
            var security = Bond(5m);
            var payload = JsonNode.Parse(security.AssetSpecificTerms.GetRawText())!;
            payload["scheduleConventions"] = JsonNode.Parse("""{"accrualAdjusted":false,"couponRateUnit":"Percent","businessDayConvention":"Following","paymentCalendars":["REPLAY"]}""");
            security = security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(payload) };
            var query = Substitute.For<ISecurityMasterQueryService>();
            query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
            var receipts = new FileSecurityCashFlowReceiptStore(directory);
            var original = Service(query, receipts: receipts, calendars: new StaticBusinessDayCalendarSource([new HolidayBusinessDayCalendar("REPLAY", 1)]));
            var receipt = await original.CaptureProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf));
            receipt!.Projection.BlockedReason.Should().BeNull();
            receipt.Projection.ScheduleEvidence!.Calendars.Should().NotBeEmpty();

            var changed = Service(query, receipts: new FileSecurityCashFlowReceiptStore(directory),
                calendars: new StaticBusinessDayCalendarSource([new HolidayBusinessDayCalendar("REPLAY", 2)]));
            var replay = await changed.ReplayProjectionAsync(receipt.ContentSha256);
            replay!.Schedule.Should().BeEmpty();
            replay.BlockedReason.Should().Contain("calendar").And.Contain("content changed");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task CanonicalDeclaredFloatingBondUsesObservedResetAndForecastWithCapAndFloor()
    {
        var security = Bond(5m);
        var payload = JsonNode.Parse(security.AssetSpecificTerms.GetRawText())!;
        payload["couponType"] = "Floating";
        payload["couponRate"] = null;
        payload["floatingIndex"] = "SOFR";
        payload["spreadBps"] = 100;
        payload["currentIndexRate"] = 0.03m;
        payload["resetSchedule"] = JsonNode.Parse("""[{"resetDate":"2026-01-01","fixingRate":0.03}]""");
        payload["capRate"] = 0.035m;
        payload["floorRate"] = 0.025m;
        payload["paymentFrequency"] = "Quarterly";
        payload["scheduleConventions"] = JsonNode.Parse("""{"accrualAdjusted":false,"couponRateUnit":"Fraction"}""");
        security = security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(payload) };
        var curve = new SecurityForwardCurve("SOFR", new DateOnly(2026, 1, 1),
            [new(new DateOnly(2026, 1, 1), 0.01m), new(new DateOnly(2028, 1, 1), 0.01m)]);
        var forwards = Substitute.For<ISecurityForwardCurveProvider>();
        forwards.GetForwardCurveAsync("SOFR", Arg.Any<BondCalculationContext>(), Arg.Any<CancellationToken>()).Returns(curve);
        var source = Substitute.For<ISecurityMasterCashFlowStore>();
        source.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityCashFlowSourceDto(
            security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, AsOf, false, null, null));
        var service = new SecurityMasterCashFlowService(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]),
            source, [], Substitute.For<ISecurityMasterQueryService>(), NullLogger<SecurityMasterCashFlowService>.Instance, forwardCurves: forwards);

        var projection = await service.GetProjectionAsync(security, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf));

        projection!.BlockedReason.Should().BeNull();
        projection.Schedule[0].InterestAmount.Should().Be(0.875m, "the observed first reset is capped at 3.5%");
        projection.Schedule.Skip(1).Should().OnlyContain(row => row.InterestAmount == 0.625m, "forecast resets are floored at 2.5%");
        projection.Schedule.Sum(row => row.PrincipalAmount).Should().Be(100m);
        projection.Schedule.Should().OnlyContain(row => row.UnadjustedStart != null && row.AccrualEnd != null);
    }

    [Fact]
    public async Task ForwardCurveProviderPropagatesKnowledgeCutoffAndPinsVersionEvidence()
    {
        var definition = ValuationTestSecurities.ForwardDefinition();
        var version = ValuationTestSecurities.Version(definition, DateOnly.FromDateTime(AsOf.UtcDateTime),
            (new DateOnly(2026, 1, 1), 0.03m), (new DateOnly(2028, 1, 1), 0.04m)) with
        { RecordedAt = AsOf };
        var store = Substitute.For<ISecurityCurveStore>();
        store.ListDefinitionsAsync(SecurityCurveKind.Forward, indexName: "SOFR", ct: Arg.Any<CancellationToken>()).Returns([definition]);
        store.GetLatestVersionAsOfAsync(definition.CurveId, new DateOnly(2026, 1, 1), AsOf, Arg.Any<CancellationToken>()).Returns(version);
        var provider = new StoredSecurityForwardCurveProvider(store, NullLogger<StoredSecurityForwardCurveProvider>.Instance);

        var curve = await provider.GetForwardCurveAsync("SOFR", new BondCalculationContext(AsOf, KnownAt: AsOf));

        curve!.RetainedInput!.VersionId.Should().Be(version.CurveVersionId);
        curve.RetainedInput.ContentSha256.Should().Be(version.ContentSha256);
        await store.Received(1).GetLatestVersionAsOfAsync(definition.CurveId, new DateOnly(2026, 1, 1), AsOf, Arg.Any<CancellationToken>());
    }

    private static SecurityMasterCashFlowService Service(ISecurityMasterQueryService query,
        SecurityCashFlowSourceDto? source = null, ISecurityCashFlowReceiptStore? receipts = null,
        IBusinessDayCalendarSource? calendars = null)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => source ?? new SecurityCashFlowSourceDto(
            call.Arg<Guid>(), StructuredCashFlowSourceKind.CalculatedBullet, AsOf, false, null, null));
        return new SecurityMasterCashFlowService(calendars ?? new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]),
            store, [], query, NullLogger<SecurityMasterCashFlowService>.Instance, receiptStore: receipts);
    }

    private static SecurityDetailDto Bond(decimal coupon) => new(
        Guid.Parse("172d94fe-0000-4000-8000-000000000001"), "Bond", SecurityStatusDto.Active, "Replay bond", "USD",
        JsonSerializer.SerializeToElement(new { }),
        JsonSerializer.SerializeToElement(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-01",
            maturity = "2028-01-01",
            par = 100m,
            couponType = "Fixed",
            couponRate = coupon,
            paymentFrequency = "Annual",
            dayCount = "30/360",
            scheduleConventions = new { accrualAdjusted = false, couponRateUnit = "Percent" }
        }), [], [], 1, AsOf, null);
}
