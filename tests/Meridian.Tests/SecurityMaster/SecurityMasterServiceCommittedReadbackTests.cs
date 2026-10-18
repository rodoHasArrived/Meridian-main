using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMasterServiceCommittedReadbackTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededCommand_PublishesOnlyDurableReadbackAndRecoversFailedRead(bool initialReadFails)
    {
        var id = Guid.NewGuid();
        var current = SecurityMasterProjectionChangeNotificationTests.CreateProjection(id, "Newer durable terms", 3);
        var store = Substitute.For<ISecurityMasterStore>();
        var recoveryRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readCount = 0;
        store.GetProjectionAsync(id, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            if (initialReadFails && Interlocked.Increment(ref readCount) == 1)
                throw new IOException("post-commit read unavailable");
            if (initialReadFails)
                await recoveryRelease.Task.WaitAsync(call.Arg<CancellationToken>());
            return (SecurityProjectionRecord?)current;
        });
        var events = Substitute.For<ISecurityMasterEventStore>();
        var snapshots = Substitute.For<ISecurityMasterSnapshotStore>();
        var conflicts = Substitute.For<ISecurityMasterConflictService>();
        var cache = new SecurityMasterProjectionCache();
        using var service = new SecurityMasterService(events, snapshots, store,
            new SecurityMasterAggregateRebuilder(events, snapshots), new SecurityMasterOptions(),
            NullLogger<SecurityMasterService>.Instance, conflicts, projectionCache: cache)
        {
            AliasCacheRefreshRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        // The event was accepted at v1, but another instance's newer write already owns the
        // durable projection. A version-guarded Upsert returns without installing this candidate.
        var accepted = await service.CreateAsync(new CreateSecurityRequest(id, "Equity",
            JsonSerializer.SerializeToElement(new { displayName = "Accepted older command", currency = "USD" }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new(SecurityIdentifierKind.Ticker, "OLD", true, DateTimeOffset.UtcNow.AddDays(-1))],
            DateTimeOffset.UtcNow, "tests", "tests", null, "create"));

        accepted.Version.Should().Be(1, "the command response describes its accepted event");
        if (initialReadFails)
        {
            cache.Get(id).Should().BeNull("a failed readback must never publish the prepared v1 candidate");
            await conflicts.DidNotReceive().RecordConflictsForProjectionAsync(
                Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>());
            recoveryRelease.SetResult();
        }
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((cache.Get(id)?.Version != 3 || !conflicts.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == nameof(ISecurityMasterConflictService.RecordConflictsForProjectionAsync)))
            && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        cache.Get(id)!.Version.Should().Be(3);
        cache.Get(id)!.DisplayName.Should().Be("Newer durable terms");
        await conflicts.Received().RecordConflictsForProjectionAsync(
            Arg.Is<SecurityProjectionRecord>(projection => projection.Version == 3), Arg.Any<CancellationToken>());
        await conflicts.DidNotReceive().RecordConflictsForProjectionAsync(
            Arg.Is<SecurityProjectionRecord>(projection => projection.Version == 1), Arg.Any<CancellationToken>());
    }
}
