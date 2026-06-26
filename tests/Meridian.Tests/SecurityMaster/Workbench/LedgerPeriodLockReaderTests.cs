using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Phase 3 period-aware propagation: the ledger accounting-period status is the lock authority, and
/// any indeterminate state is treated as <see cref="LedgerPeriodStatusDto.HardClosed"/> (default-deny)
/// so an upstream reference-data edit can never silently mutate a book whose lock state is unknown.
/// </summary>
public sealed class LedgerPeriodLockReaderTests
{
    private static readonly Guid LedgerBookId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly DateOnly InsidePeriod = new(2026, 3, 15);
    private static readonly DateOnly OutsideAnyPeriod = new(2026, 5, 15);

    [Theory]
    [InlineData("Open", LedgerPeriodStatusDto.Open)]
    [InlineData("SoftClosed", LedgerPeriodStatusDto.SoftClosed)]
    [InlineData("HardClosed", LedgerPeriodStatusDto.HardClosed)]
    public async Task GetPeriodStatus_MapsCoveringPeriodStatus(string status, LedgerPeriodStatusDto expected)
    {
        var store = StoreWithPeriod(status);
        var reader = new LedgerPeriodLockReader(store, NullLogger<LedgerPeriodLockReader>.Instance);

        var result = await reader.GetPeriodStatusAsync(LedgerBookId, InsidePeriod);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetPeriodStatus_NoCoveringPeriod_DefaultsToHardClosed()
    {
        var store = StoreWithPeriod("Open");
        var reader = new LedgerPeriodLockReader(store, NullLogger<LedgerPeriodLockReader>.Instance);

        // The only period covers March 2026; a May date is covered by no period.
        var result = await reader.GetPeriodStatusAsync(LedgerBookId, OutsideAnyPeriod);

        result.Should().Be(LedgerPeriodStatusDto.HardClosed,
            "an edit whose period cannot be located must never be treated as postable");
    }

    [Fact]
    public async Task GetPeriodStatus_EmptyPeriodList_DefaultsToHardClosed()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.ListPeriodsAsync(
                Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LedgerAccountingPeriod>());
        var reader = new LedgerPeriodLockReader(store, NullLogger<LedgerPeriodLockReader>.Instance);

        var result = await reader.GetPeriodStatusAsync(LedgerBookId, InsidePeriod);

        result.Should().Be(LedgerPeriodStatusDto.HardClosed);
    }

    [Theory]
    [InlineData("Closing")]
    [InlineData("Pending")]
    [InlineData("")]
    public async Task GetPeriodStatus_UnrecognizedStatus_DefaultsToHardClosed(string status)
    {
        var store = StoreWithPeriod(status);
        var reader = new LedgerPeriodLockReader(store, NullLogger<LedgerPeriodLockReader>.Instance);

        var result = await reader.GetPeriodStatusAsync(LedgerBookId, InsidePeriod);

        result.Should().Be(LedgerPeriodStatusDto.HardClosed,
            "an unrecognized lock state is indeterminate and must default-deny");
    }

    [Fact]
    public async Task GetPeriodStatus_StoreThrows_DefaultsToHardClosed()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.ListPeriodsAsync(
                Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("ledger store unavailable"));
        var reader = new LedgerPeriodLockReader(store, NullLogger<LedgerPeriodLockReader>.Instance);

        var result = await reader.GetPeriodStatusAsync(LedgerBookId, InsidePeriod);

        result.Should().Be(LedgerPeriodStatusDto.HardClosed,
            "a failed lock lookup is indeterminate and must default-deny rather than surface an error");
    }

    [Fact]
    public async Task GetPeriodStatus_StatusMatchIsCaseSensitive_TreatsLowercaseAsIndeterminate()
    {
        // The ledger persists canonical PascalCase status tokens; a lowercase value indicates an
        // unexpected/foreign source and must not be optimistically treated as Open.
        var store = StoreWithPeriod("open");
        var reader = new LedgerPeriodLockReader(store, NullLogger<LedgerPeriodLockReader>.Instance);

        var result = await reader.GetPeriodStatusAsync(LedgerBookId, InsidePeriod);

        result.Should().Be(LedgerPeriodStatusDto.HardClosed);
    }

    private static ILedgerJournalStore StoreWithPeriod(string status)
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.ListPeriodsAsync(
                Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new LedgerAccountingPeriod(
                    PeriodId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                    LedgerBookId: LedgerBookId,
                    FiscalYear: 2026,
                    PeriodNo: 3,
                    Label: "2026-P03",
                    StartDate: new DateOnly(2026, 3, 1),
                    EndDate: new DateOnly(2026, 3, 31),
                    Status: status,
                    OpenedAt: DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
                    ClosedAt: null,
                    Version: 1),
            });
        return store;
    }
}
