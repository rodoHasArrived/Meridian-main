using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Ui.Shared.Streaming;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class StreamConnectionRegistryTests
{
    [Fact]
    public void TryReserve_EnforcesPerSessionCap()
    {
        var registry = new StreamConnectionRegistry(maxConcurrentStreamsPerSession: 2);

        registry.TryReserve("s1").Should().BeTrue();
        registry.TryReserve("s1").Should().BeTrue();
        registry.TryReserve("s1").Should().BeFalse();
        registry.ActiveCount("s1").Should().Be(2);

        // A different session has its own budget.
        registry.TryReserve("s2").Should().BeTrue();
    }

    [Fact]
    public void Release_FreesASlot()
    {
        var registry = new StreamConnectionRegistry(1);
        registry.TryReserve("s1").Should().BeTrue();
        registry.TryReserve("s1").Should().BeFalse();

        registry.Release("s1");

        registry.ActiveCount("s1").Should().Be(0);
        registry.TryReserve("s1").Should().BeTrue();
    }

    [Fact]
    public void CloseSession_ResetsCount()
    {
        var registry = new StreamConnectionRegistry(4);
        registry.TryReserve("s1");
        registry.TryReserve("s1");

        registry.CloseSession("s1");

        registry.ActiveCount("s1").Should().Be(0);
    }

    [Fact]
    public void UnscopedSession_IsNeverCapped()
    {
        var registry = new StreamConnectionRegistry(1);

        registry.TryReserve("").Should().BeTrue();
        registry.TryReserve("").Should().BeTrue();
    }

    [Fact]
    public async Task TryReserve_IsThreadSafeUnderConcurrency()
    {
        var registry = new StreamConnectionRegistry(maxConcurrentStreamsPerSession: 10);

        var grants = await Task.WhenAll(
            Enumerable.Range(0, 100).Select(_ => Task.Run(() => registry.TryReserve("s1"))));

        grants.Count(granted => granted).Should().Be(10);
        registry.ActiveCount("s1").Should().Be(10);
    }
}
