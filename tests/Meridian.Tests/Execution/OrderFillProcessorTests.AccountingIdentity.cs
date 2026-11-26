using Meridian.Execution.Services;
using Meridian.Execution.Sdk;
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
        var order = CreateOrder("identity-snapshot") with
        {
            AccountingSecurityId = admittedIdentity,
            // Old states may carry both of these labels; neither can mint new accounting proof.
            Origin = OrderCommandOrigin.StrategyRun,
            RunId = admittedRun ?? "legacy-run-label",
            AccountingRunId = admittedRun
        };
        var report = CreateReport(order.OrderId) with { AccountingSecurityId = Guid.NewGuid(), AccountingRunId = "gateway-assertion" };
        var store = new RecordingSessionStore();
        await using var persistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance, store);
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto("identity", null, 10_000m));
        var portfolio = new PaperTradingPortfolio(10_000m);
        var publisher = new RecordingPublisher();
        var processor = CreateProcessor(order, portfolio, publisher, persistence, out _);

        await processor.ProcessAsync(session.SessionId, report, 0m);

        var stored = Assert.IsType<PaperSessionFillRecord>(store.StoredFill);
        Assert.Equal(admittedIdentity, stored.Fill.AccountingSecurityId);
        Assert.Equal(admittedRun, stored.Fill.AccountingRunId);
        Assert.Equal(admittedRun, Assert.Single(publisher.Events).AccountingRunId);
        Assert.Equal(8_500m, portfolio.Cash);
    }
}
