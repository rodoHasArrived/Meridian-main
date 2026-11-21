using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed partial class OrderFillProcessorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AccountingIdentity_UsesRetainedAdmissionInsteadOfGatewayAssertion(bool hasAdmissionIdentity, bool hasRun)
    {
        var admittedIdentity = hasAdmissionIdentity ? Guid.NewGuid() : (Guid?)null;
        var admittedRun = hasRun ? "accepted-run" : null;
        var order = CreateOrder("identity-snapshot") with { AccountingSecurityId = admittedIdentity, RunId = admittedRun };
        var report = CreateReport(order.OrderId) with { AccountingSecurityId = Guid.NewGuid(), AccountingRunId = "gateway-assertion" };
        var store = new RecordingSessionStore();
        await using var persistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance, store);
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto("identity", null, 10_000m));
        var portfolio = new PaperTradingPortfolio(10_000m);
        var processor = CreateProcessor(order, portfolio, new RecordingPublisher(), persistence, out _);

        await processor.ProcessAsync(session.SessionId, report, 0m);

        var stored = Assert.IsType<PaperSessionFillRecord>(store.StoredFill);
        Assert.Equal(admittedIdentity, stored.Fill.AccountingSecurityId);
        Assert.Equal(admittedRun, stored.Fill.AccountingRunId);
        Assert.Equal(8_500m, portfolio.Cash);
    }
}
