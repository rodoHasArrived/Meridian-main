using Meridian.Contracts.Workstation;
using Meridian.Storage.Workstation;
using Meridian.Ui.Shared.Services;
using Testcontainers.PostgreSql;

namespace Meridian.Tests.Ui;

[Trait("Category", "Integration")]
public sealed class OperatorTriagePostgresTests
{
    [Fact]
    public async Task IndependentWritersRetainOneVersionAndRestartPreservesAuditedTenantIsolation()
    {
        await using var database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("meridian_triage_test").WithUsername("triage_test")
            .WithPassword("synthetic-triage-test").Build();
        await database.StartAsync();
        var options = new OperatorTriageStoreOptions { ConnectionString = database.GetConnectionString() };
        var scope = new OperatorTriageScope("tenant-a", "company-a");
        var now = DateTimeOffset.UtcNow;
        var stores = new[] { new PostgresOperatorTriageStore(options), new PostgresOperatorTriageStore(options) };
        var outcomes = await Task.WhenAll(stores.Select(async (store, index) =>
        {
            try
            {
                await store.UpdateAsync(scope, "case-1", (current, cancellationToken) => Task.FromResult(OperatorTriagePolicy.Apply(current,
                    "alice", new($"ack-{index}", OperatorTriageAction.Acknowledge, 0, "Reviewed retained evidence"),
                    OperatorWorkItemToneDto.Warning, now, false, out _)));
                return true;
            }
            catch (OperatorTriageException exception) when (exception.Status == "Conflict") { return false; }
        }));
        Assert.Single(outcomes, success => success);
        var restarted = new PostgresOperatorTriageStore(options);
        var retained = await restarted.GetAsync(scope, "case-1");
        Assert.Equal(1, retained.Version);
        Assert.Single(retained.Activity);
        Assert.Single(retained.Receipts);
        Assert.Equal(now, Assert.Single(retained.Personal).AcknowledgedAt);
        Assert.Equal(0, (await restarted.GetAsync(new("tenant-b", "company-a"), "case-1")).Version);
        Assert.Equal(0, (await restarted.GetAsync(new("tenant-a", "company-b"), "case-1")).Version);
        await Assert.ThrowsAsync<IOException>(() => restarted.UpdateAsync(scope, "case-1", (_, _) =>
            throw new IOException("Synthetic failure before committing metadata.")));
        Assert.Equal(1, (await restarted.GetAsync(scope, "case-1")).Version);
    }
}
