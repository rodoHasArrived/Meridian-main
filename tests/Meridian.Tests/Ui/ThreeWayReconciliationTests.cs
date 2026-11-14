using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class ThreeWayReconciliationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-three-way-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Cutoff = new(2026, 11, 13, 23, 59, 59, TimeSpan.Zero);
    private static readonly Guid SecurityId = Guid.Parse("4b558109-5326-437e-9fd1-c025875ca0e6");
    private static readonly ThreeWayReconciliationScopeDto Scope = new("tenant-a", "company-a", "profile-a",
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "USD");

    [Fact]
    public void GoldenBalances_CompareAllThreePairsAcrossCashQuantityAndBasis()
    {
        var checks = Compare(Snapshot("ledger"), Snapshot("oms"), Snapshot("broker"));

        checks.Should().HaveCount(12);
        checks.Should().OnlyContain(check => check.Status == "Matched");
        checks.Where(check => check.Measure == "cost-basis").Should().HaveCount(3);
    }

    [Theory]
    [InlineData("cash", 1)]
    [InlineData("quantity", 2)]
    [InlineData("cost-basis", 3)]
    public void MissingFillOrFee_RemainsVisibleInIndependentPairwiseOperands(string measure, int scenario)
    {
        var broker = scenario switch
        {
            1 => Snapshot("broker") with { Cash = 999m },
            2 => Snapshot("broker") with { Positions = [new(SecurityId, "TEST", 11m, 1_100m)] },
            _ => Snapshot("broker") with { Positions = [new(SecurityId, "TEST", 10m, 1_001m)] }
        };
        var checks = Compare(Snapshot("ledger"), Snapshot("oms"), broker);

        checks.Should().Contain(check => check.Measure == measure && check.Status == "Break"
            && check.LeftSource == "ledger" && check.RightSource == "broker");
        checks.Should().Contain(check => check.Measure == measure && check.Status == "Matched"
            && check.LeftSource == "ledger" && check.RightSource == "oms");
    }

    [Fact]
    public void MissingSourceAndUnknownBasis_DoNotBecomeZeroOrMatched()
    {
        var checks = Compare(Snapshot("ledger"), Snapshot("broker") with { CostBasisConvention = "Unknown" });

        checks.Should().Contain(check => check.CheckId == "coverage:oms" && check.Status == "Blocked");
        checks.Where(check => check.LeftSource == "oms" || check.RightSource == "oms")
            .Should().OnlyContain(check => check.Status == "Blocked");
        checks.Should().Contain(check => check.Measure == "cost-basis" && check.RightSource == "broker"
            && check.Status == "Blocked" && check.RightAmount == null);
    }

    [Fact]
    public void StaleAndFutureAndConflictingPositions_BlockCoverage()
    {
        foreach (var source in new[]
        {
            Snapshot("broker") with { AsOfUtc = Cutoff.AddHours(-1) },
            Snapshot("broker") with { AsOfUtc = Cutoff.AddSeconds(1) },
            Snapshot("broker") with { Positions = [new(SecurityId, "TEST", 10m, 1_000m), new(SecurityId, "TEST", -10m, 0m)] }
        })
        {
            var checks = Compare(Snapshot("ledger"), Snapshot("oms"), source);
            checks.Should().Contain(check => check.CheckId == "coverage:broker" && check.Status == "Blocked");
            checks.Where(check => check.RightSource == "broker").Should().OnlyContain(check => check.Status == "Blocked");
        }
    }

    [Fact]
    public async Task RestartAndRetry_ReusesFrozenReceiptAndStableCasesThenRerunClearsOnlySourceObservation()
    {
        var options = new ThreeWayReconciliationOptions { RootDirectory = _root };
        var reader = Reader();
        reader.ReadAsync(Scope, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([Snapshot("ledger"), Snapshot("oms"), Snapshot("broker") with { Cash = 999m }]);
        var queue = new FileReconciliationBreakQueueRepository(_root, NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = Service(reader, queue, options);
        var request = new ThreeWayReconciliationRequestDto(Scope.LedgerBookId, Scope.AccountingPeriodId, "run-1", Cutoff);
        var first = await service.RunAsync(Scope.FinancialAccountId, Access(), request);
        first.Status.Should().Be("Breaks");
        first.BreakIds.Should().HaveCount(2);

        var restarted = Service(reader, new FileReconciliationBreakQueueRepository(_root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance), options);
        var replay = await restarted.RunAsync(Scope.FinancialAccountId, Access(), request);
        replay.Should().BeEquivalentTo(first);
        await reader.Received(1).CaptureAsync(Scope, Arg.Any<CancellationToken>());
        (await queue.GetAllAsync(Access())).Should().HaveCount(2);

        reader.ReadAsync(Scope, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([Snapshot("ledger"), Snapshot("oms"), Snapshot("broker")]);
        var matching = await restarted.RunAsync(Scope.FinancialAccountId, Access(), request with { OperationId = "run-2" });
        matching.Status.Should().Be("Matched");
        var cases = await new FileReconciliationBreakQueueRepository(_root,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance).GetAllAsync(Access());
        cases.Should().OnlyContain(item => item.Status == ReconciliationBreakQueueStatus.Open);
        cases.Should().OnlyContain(item => item.Lineage != null && item.Lineage.ObservationState == "Cleared");
    }

    [Fact]
    public async Task ChangedRequestUnderExistingOperation_IsRejectedAndForeignScopeCannotReadReceipt()
    {
        var reader = Reader();
        reader.ReadAsync(Scope, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([Snapshot("ledger"), Snapshot("oms"), Snapshot("broker")]);
        var queue = new FileReconciliationBreakQueueRepository(_root, NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var options = new ThreeWayReconciliationOptions { RootDirectory = _root };
        var service = Service(reader, queue, options);
        var request = new ThreeWayReconciliationRequestDto(Scope.LedgerBookId, Scope.AccountingPeriodId, "run-1", Cutoff);
        await service.RunAsync(Scope.FinancialAccountId, Access(), request);
        var changed = () => service.RunAsync(Scope.FinancialAccountId, Access(), request with { CutoffUtc = Cutoff.AddSeconds(-1) });
        await changed.Should().ThrowAsync<InvalidOperationException>();
        reader.AuthorizeAsync(Scope.FinancialAccountId, Arg.Is<ReconciliationBreakQueueScope>(access => access.TenantId == "foreign"),
            Scope.LedgerBookId, Scope.AccountingPeriodId, Arg.Any<CancellationToken>())
            .Returns<ThreeWayReconciliationScopeDto>(_ => throw new UnauthorizedAccessException());
        var foreign = () => service.GetLatestAsync(Scope.FinancialAccountId, new("foreign", Scope.CompanyId),
            Scope.LedgerBookId, Scope.AccountingPeriodId);
        await foreign.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task SourceHistory_SelectsCutoffAndRejectsConflictingTimestamp()
    {
        var store = new FileThreeWayReconciliationEvidenceStore(new() { RootDirectory = _root });
        await store.SaveSourceAsync(Scope, Snapshot("oms"), CancellationToken.None);
        await store.SaveSourceAsync(Scope, Snapshot("oms") with { AsOfUtc = Cutoff.AddDays(1), Cash = 50m }, CancellationToken.None);
        (await store.ReadSourceAsync(Scope, "oms", Cutoff, CancellationToken.None))!.Cash.Should().Be(1_000m);
        await store.SaveSourceAsync(Scope, Snapshot("oms") with { Cash = 999m }, CancellationToken.None);
        var conflict = () => store.ReadSourceAsync(Scope, "oms", Cutoff, CancellationToken.None);
        await conflict.Should().ThrowAsync<InvalidOperationException>();
    }

    private static ThreeWaySourceSnapshotDto Snapshot(string source)
        => new(source, Cutoff, $"evidence:{source}", 1_000m, [new(SecurityId, "TEST", 10m, 1_000m)],
            ThreeWayReconciliationOptions.SupportedBasisConvention, []);
    private static IReadOnlyList<ThreeWayReconciliationCheckDto> Compare(params ThreeWaySourceSnapshotDto[] source)
        => ThreeWayReconciliationComparer.Compare(source, Cutoff, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5));
    private static ReconciliationBreakQueueScope Access() => new(Scope.TenantId, Scope.CompanyId);
    private static IThreeWayReconciliationSourceReader Reader()
    {
        var reader = Substitute.For<IThreeWayReconciliationSourceReader>();
        reader.AuthorizeAsync(Scope.FinancialAccountId, Arg.Any<ReconciliationBreakQueueScope>(), Scope.LedgerBookId,
            Scope.AccountingPeriodId, Arg.Any<CancellationToken>()).Returns(Scope);
        return reader;
    }
    private static ThreeWayReconciliationService Service(IThreeWayReconciliationSourceReader reader,
        IReconciliationBreakQueueRepository queue, ThreeWayReconciliationOptions options)
        => new(reader, new(options), queue, options, new FixedClock());
    private sealed class FixedClock : TimeProvider
    {
        private static long _ticks;
        public override DateTimeOffset GetUtcNow() => Cutoff.AddSeconds(1).AddTicks(Interlocked.Increment(ref _ticks));
    }
    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
