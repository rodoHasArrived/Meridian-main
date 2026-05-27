using FluentAssertions;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class OmsIntegrationServiceTests
{
    [Fact]
    public void Ingest_IsIdempotent_WithStableDeduplicationKey()
    {
        var service = new OmsIntegrationService();
        var message = new OmsInboundMessage("oms-a", "ord-1", "fill", DateTimeOffset.UtcNow, "hash-1", null, "corr-1");

        var first = service.Ingest(message);
        var second = service.Ingest(message);

        first.ReplayDetected.Should().BeFalse();
        second.ReplayDetected.Should().BeTrue();
        service.Snapshot().Should().HaveCount(1);
    }

    [Fact]
    public void ResolveSyncConflict_UsesTimestampPrecedence()
    {
        var service = new OmsIntegrationService();
        var older = new OmsSyncRecord("acct-1", DateTimeOffset.UtcNow.AddMinutes(-5), new Dictionary<string, string>{{"qty","10"}});
        var newer = new OmsSyncRecord("acct-1", DateTimeOffset.UtcNow, new Dictionary<string, string>{{"qty","12"}});
        var result = service.ResolveSyncConflict(new OmsSyncRequest("push", "acct-1", "corr-2", older, newer));

        result.Policy.Should().Be("timestamp-precedence");
        result.Winner.Fields["qty"].Should().Be("12");
    }
}
