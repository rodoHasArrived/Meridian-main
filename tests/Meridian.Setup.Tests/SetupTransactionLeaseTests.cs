using FluentAssertions;
using Xunit;

namespace Meridian.Setup.Tests;

/// <summary>
/// The setup lease serializes install, repair, upgrade, and uninstall. A named mutex is
/// re-entrant for its owning thread, so contention is exercised from a separate thread, the
/// way a second installer process would contend for it.
/// </summary>
public sealed class SetupTransactionLeaseTests
{
    [Fact]
    public void Acquire_WhileAnotherOwnerHoldsTheLease_FailsWithoutBlocking()
    {
        var name = UniqueName();
        using var held = SetupTransactionLease.Acquire(name);

        var contender = RunOnDedicatedThread(() => SetupTransactionLease.Acquire(name));

        contender.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("already running");
    }

    [Fact]
    public void Acquire_AfterTheOwnerReleases_Succeeds()
    {
        var name = UniqueName();
        SetupTransactionLease.Acquire(name).Dispose();

        var contender = RunOnDedicatedThread(() => SetupTransactionLease.Acquire(name));

        contender.Should().BeNull("a released lease must be reusable by the next setup transaction");
    }

    [Fact]
    public void Acquire_DifferentTransactionNames_DoNotContend()
    {
        using var held = SetupTransactionLease.Acquire(UniqueName());

        var contender = RunOnDedicatedThread(() => SetupTransactionLease.Acquire(UniqueName()));

        contender.Should().BeNull();
    }

    private static string UniqueName() => $"Local\\Meridian.Setup.Tests.{Guid.NewGuid():N}";

    /// <summary>Acquires and releases a lease on a fresh thread; returns the failure, if any.</summary>
    private static Exception? RunOnDedicatedThread(Func<SetupTransactionLease> acquire)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                acquire().Dispose();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue("acquisition must never wait for the owner");
        return failure;
    }
}
