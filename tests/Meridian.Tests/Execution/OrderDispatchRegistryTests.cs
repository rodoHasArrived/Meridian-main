using Meridian.Execution.OrderManagement;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

public sealed class OrderDispatchRegistryTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DuplicateLease_DisposalKeepsTheWinningSubmissionVisibleAndAwaited()
    {
        var registry = CreateRegistry();
        using var winner = registry.CreateLease();
        using var duplicate = registry.CreateLease();
        winner.Begin("same-order");
        duplicate.Begin("same-order");
        var waiting = registry.WaitForCurrentAsync();

        duplicate.Settle();
        duplicate.Dispose();

        Assert.True(registry.Contains("same-order"));
        Assert.True(registry.IsUnsettled("same-order"));
        Assert.False(waiting.IsCompleted);

        winner.Settle();

        Assert.Empty(await waiting.WaitAsync(TestTimeout));
        Assert.False(registry.Contains("same-order"));
        Assert.False(registry.IsUnsettled("same-order"));
    }

    [Fact]
    public async Task SettledLease_DisposalCannotRemoveAReplacementUnderTheSameOrderId()
    {
        var registry = CreateRegistry();
        using var earlier = registry.CreateLease();
        earlier.Begin("reused-order");
        earlier.Settle();
        using var replacement = registry.CreateLease();
        replacement.Begin("reused-order");
        var waiting = registry.WaitForCurrentAsync();

        earlier.Settle();
        earlier.Dispose();

        Assert.True(registry.Contains("reused-order"));
        Assert.True(registry.IsUnsettled("reused-order"));
        Assert.False(waiting.IsCompleted);

        replacement.Settle();

        Assert.Empty(await waiting.WaitAsync(TestTimeout));
        Assert.False(registry.Contains("reused-order"));
    }

    [Fact]
    public async Task WaitForCurrent_ExcludesSubmissionsBegunAfterTheWaitCapturedItsTargets()
    {
        var registry = CreateRegistry();
        using var captured = registry.CreateLease();
        using var later = registry.CreateLease();
        captured.Begin("captured");

        var firstWait = registry.WaitForCurrentAsync();
        later.Begin("later");
        captured.Settle();

        Assert.Empty(await firstWait.WaitAsync(TestTimeout));
        Assert.False(registry.Contains("captured"));
        Assert.True(registry.IsUnsettled("later"));

        var nextWait = registry.WaitForCurrentAsync();
        Assert.False(nextWait.IsCompleted);
        later.Settle();

        Assert.Empty(await nextWait.WaitAsync(TestTimeout));
        Assert.False(registry.Contains("later"));
    }

    [Fact]
    public async Task WaitForCurrent_AtDeadlineReturnsOnlyUnsettledCapturedSubmissions()
    {
        var registry = CreateRegistry(TimeSpan.FromMilliseconds(250));
        using var acknowledged = registry.CreateLease();
        using var pending = registry.CreateLease();
        using var later = registry.CreateLease();
        acknowledged.Begin("acknowledged");
        pending.Begin("pending");

        var waiting = registry.WaitForCurrentAsync();
        later.Begin("later");
        acknowledged.Settle();

        var unsettled = await waiting.WaitAsync(TestTimeout);

        Assert.Equal("pending", Assert.Single(unsettled));
        Assert.False(registry.Contains("acknowledged"));
        Assert.True(registry.Contains("pending"));
        Assert.True(registry.IsUnsettled("pending"));
        Assert.True(registry.Contains("later"));
        Assert.True(registry.IsUnsettled("later"));
    }

    [Fact]
    public async Task LeaseIdentity_IsOrdinalAndDoesNotCollapseCaseDistinctOrders()
    {
        var registry = CreateRegistry();
        using var uppercase = registry.CreateLease();
        using var lowercase = registry.CreateLease();
        uppercase.Begin("Order-1");
        lowercase.Begin("order-1");
        var waiting = registry.WaitForCurrentAsync();

        uppercase.Settle();

        Assert.False(registry.Contains("Order-1"));
        Assert.True(registry.Contains("order-1"));
        Assert.True(registry.IsUnsettled("order-1"));
        Assert.False(waiting.IsCompleted);

        lowercase.Settle();

        Assert.Empty(await waiting.WaitAsync(TestTimeout));
    }

    private static OrderDispatchRegistry CreateRegistry(TimeSpan? settleTimeout = null)
        => new(settleTimeout ?? TimeSpan.FromSeconds(30), NullLogger.Instance);
}
