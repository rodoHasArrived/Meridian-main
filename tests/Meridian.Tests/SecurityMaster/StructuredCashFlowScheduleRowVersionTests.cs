using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class StructuredCashFlowScheduleRowVersionTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CurrentReceiptRetainsMeasuredIntervalAndReplaysAfterRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cash-flow-row-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = Service(new FileSecurityCashFlowReceiptStore(directory));
            var receipt = await service.CaptureProjectionAsync(Security().SecurityId, StructuredCashFlowScenario.Base,
                new BondCalculationContext(AsOf));

            receipt!.ModelVersion.Should().Be("bond-cash-flow-context-v4");
            receipt.Projection.BlockedReason.Should().BeNull();
            receipt.Projection.Schedule.Should().HaveCount(2);
            receipt.Projection.Schedule.Should().OnlyContain(row => row.RowVersion == 1 &&
                row.InterestMeasuredFrom != null && row.InterestMeasuredTo != null);
            var path = Path.Combine(directory, receipt.ContentSha256 + ".json");
            var json = await File.ReadAllTextAsync(path);
            json.Should().Contain("\"RowVersion\":1").And.Contain("\"InterestMeasuredFrom\":\"2026-01-01\"");

            var restarted = Service(new FileSecurityCashFlowReceiptStore(directory));
            var replay = await restarted.ReplayProjectionAsync(receipt.ContentSha256);
            replay!.BlockedReason.Should().BeNull();
            replay.Schedule.Should().Equal(receipt.Projection.Schedule);

            await File.WriteAllTextAsync(path, json.Replace("\"InterestMeasuredFrom\":\"2026-01-01\"",
                "\"InterestMeasuredFrom\":\"2026-01-02\"", StringComparison.Ordinal));
            var act = () => restarted.ReplayProjectionAsync(receipt.ContentSha256);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("bond-cash-flow-context-v1")]
    [InlineData("bond-cash-flow-context-v2")]
    [InlineData("bond-cash-flow-context-v3")]
    public async Task FrozenLegacyRowReceiptsReplayWithoutChangingStoredBytes(string model)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cash-flow-legacy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSecurityCashFlowReceiptStore(directory);
            var receipt = await Service(store).CaptureProjectionAsync(Security().SecurityId,
                StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf));
            receipt!.Projection.BlockedReason.Should().BeNull();
            receipt.Projection.Schedule.Should().HaveCount(2);
            var legacy = Rehash(receipt with
            {
                ModelVersion = model,
                Projection = SecurityCashFlowScheduleRowCodec.Legacy(receipt.Projection)
            });
            await store.RetainAsync(legacy);
            var path = Path.Combine(directory, legacy.ContentSha256 + ".json");
            var originalBytes = await File.ReadAllBytesAsync(path);
            var originalJson = await File.ReadAllTextAsync(path);
            originalJson.Should().NotContain("RowVersion").And.NotContain("InterestMeasured");

            var replay = await Service(new FileSecurityCashFlowReceiptStore(directory)).ReplayProjectionAsync(legacy.ContentSha256);

            replay!.BlockedReason.Should().BeNull();
            replay.Schedule.Should().Equal(legacy.Projection.Schedule);
            replay.Schedule.Should().OnlyContain(row => row.RowVersion == 0 && row.InterestMeasuredFrom == null);
            (await File.ReadAllBytesAsync(path)).Should().Equal(originalBytes);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("InterestMeasuredFrom")]
    [InlineData("InterestMeasuredTo")]
    [InlineData("RowVersion")]
    [InlineData("ModelVersion")]
    public async Task RehashedChangesToCurrentIntervalOrVersionStillRefuseReplay(string member)
    {
        var store = Substitute.For<ISecurityCashFlowReceiptStore>();
        var service = Service(store);
        var original = (await service.CaptureProjectionAsync(Security().SecurityId,
            StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf)))!;
        original.Projection.BlockedReason.Should().BeNull();
        original.Projection.Schedule.Should().HaveCount(2);
        var first = original.Projection.Schedule[0];
        var changed = member switch
        {
            "InterestMeasuredFrom" => first with { InterestMeasuredFrom = first.InterestMeasuredFrom!.Value.AddDays(1) },
            "InterestMeasuredTo" => first with { InterestMeasuredTo = first.InterestMeasuredTo!.Value.AddDays(-1) },
            "RowVersion" => first with { RowVersion = 99 },
            _ => first
        };
        var tampered = Rehash(original with
        {
            ModelVersion = member == "ModelVersion" ? "bond-cash-flow-context-v99" : original.ModelVersion,
            Projection = original.Projection with { Schedule = [changed, .. original.Projection.Schedule.Skip(1)] }
        });
        store.GetAsync(tampered.ContentSha256, Arg.Any<CancellationToken>()).Returns(tampered);

        var replay = await service.ReplayProjectionAsync(tampered.ContentSha256);

        replay!.Schedule.Should().BeEmpty();
        replay.BlockedReason.Should().NotBeNull();
    }

    [Fact]
    public void FrozenLegacyRowCodecPreservesOriginalWireBytesAndCurrentRecordEqualityCoversIntervals()
    {
        const string original = """{"PeriodDate":"2027-01-01T00:00:00+00:00","PrincipalAmount":0,"InterestAmount":5,"Factor":1,"UnadjustedStart":"2026-01-01","UnadjustedEnd":"2027-01-01","AccrualStart":"2026-01-01","AccrualEnd":"2027-01-01"}""";
        var row = JsonSerializer.Deserialize(original, SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowScheduleEntry)!;
        JsonSerializer.Serialize(row, SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowScheduleEntry).Should().Be(original);

        var current = row with { RowVersion = 1, InterestMeasuredFrom = new(2026, 1, 1), InterestMeasuredTo = new(2027, 1, 1) };
        // A current-only value (or a future nonnull member default) cannot grow the frozen v0
        // receipt wire contract. Version validation separately refuses this inconsistent row.
        JsonSerializer.Serialize(current with { RowVersion = 0 },
            SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowScheduleEntry).Should().Be(original);
        current.Should().NotBe(current with { InterestMeasuredFrom = new(2026, 1, 2) });
        current.Should().NotBe(current with { InterestMeasuredTo = new(2026, 12, 31) });
        current.Should().NotBe(current with { RowVersion = 2 });
        var projection = new StructuredCashFlowProjectionDto(Guid.NewGuid(), StructuredCashFlowSourceKind.CalculatedBullet,
            StructuredCashFlowScenario.Base, AsOf, [current]);
        JsonSerializer.Serialize(SecurityCashFlowScheduleRowCodec.Legacy(projection).Schedule.Single(),
            SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowScheduleEntry).Should().Be(original);
    }

    [Fact]
    public void RowVersionCoversLegSchedulesAndDoesNotActivateNewFieldsOnLegacyRows()
    {
        var row = new StructuredCashFlowScheduleEntry(AsOf.AddYears(1), 0m, 5m, 1m)
        { InterestMeasuredFrom = new(2026, 1, 1), InterestMeasuredTo = new(2027, 1, 1) };
        var projection = new StructuredCashFlowProjectionDto(Guid.NewGuid(), StructuredCashFlowSourceKind.CalculatedBullet,
            StructuredCashFlowScenario.Base, AsOf, [], LegSchedules:
            [new("receive", CashFlowLegRateKind.Fixed, CashFlowLegDirection.Receive, [row])]);

        SecurityCashFlowScheduleRowCodec.HasSupportedRows(projection, current: false).Should().BeFalse();
        var current = SecurityCashFlowScheduleRowCodec.Current(projection);
        SecurityCashFlowScheduleRowCodec.HasSupportedRows(current, current: true).Should().BeTrue();
        current.LegSchedules!.Single().Schedule.Single().RowVersion.Should().Be(1);
        var legacy = SecurityCashFlowScheduleRowCodec.Legacy(current);
        SecurityCashFlowScheduleRowCodec.HasSupportedRows(legacy, current: false).Should().BeTrue();
        SecurityCashFlowScheduleRowCodec.HasSupportedRows(legacy, current: true).Should().BeFalse();
        legacy.LegSchedules!.Single().Schedule.Single().InterestMeasuredFrom.Should().BeNull();
    }

    private static SecurityCashFlowCalculationReceipt Rehash(SecurityCashFlowCalculationReceipt receipt)
        => receipt with { ContentSha256 = SecurityMasterCashFlowService.ReceiptHash(receipt) };

    private static SecurityMasterCashFlowService Service(ISecurityCashFlowReceiptStore receipts)
    {
        var security = Security();
        var source = Substitute.For<ISecurityMasterCashFlowStore>();
        source.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(
            new SecurityCashFlowSourceDto(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, AsOf, false, null, null));
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        return new SecurityMasterCashFlowService(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]),
            source, [], query, NullLogger<SecurityMasterCashFlowService>.Instance, receiptStore: receipts);
    }

    private static SecurityDetailDto Security() => new(
        Guid.Parse("172d94fe-0000-4000-8000-000000000099"), "Deposit", SecurityStatusDto.Active, "Row version deposit", "USD",
        JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-01",
            maturity = "2028-01-01",
            par = 100m,
            depositType = "Term",
            institutionName = "Synthetic Bank",
            rateUnit = "Fraction",
            interestRate = 0.05m,
            paymentFrequency = "Annual",
            dayCount = "30/360",
            isCallable = false
        }), [], [], 1, AsOf, null);
}
