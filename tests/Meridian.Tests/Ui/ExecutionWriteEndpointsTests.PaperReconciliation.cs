using System.Globalization;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Meridian.Testing;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class ExecutionWriteEndpointsTests
{
    [Fact]
    public async Task PaperReconciliation_RequiresLocalAdminAndReturnsExactScopeUsingDurableJournal()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(PaperReconciliation_RequiresLocalAdminAndReturnsExactScopeUsingDurableJournal));
        var receiptStore = new PaperSessionDailyReconciliationStore(Path.Combine(artifacts.RootPath, "paper-reconciliation"));
        string path;
        string latestPath;
        await using (var app = await CreateAppAsync(
            services =>
            {
                RegisterSessionServices(services, artifacts.RootPath);
                services.AddSingleton(receiptStore);
            },
            currentUserPermissions: UserPermission.ViewTrades | UserPermission.AdminMaintenance))
        {
            var persistence = app.Services.GetRequiredService<PaperSessionPersistenceService>();
            var session = await persistence.CreateSessionAsync(new("paper-cutoff-strategy", "Synthetic cutoff", 125m, ["AAPL"]));
            var cutoff = session.CreatedAt.ToString("O", CultureInfo.InvariantCulture);
            latestPath = $"/api/execution/sessions/{session.SessionId}/reconciliation";
            path = $"{latestPath}?cutoff={Uri.EscapeDataString(cutoff)}";

            var client = app.GetTestClient();
            (await client.GetAsync(latestPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            var response = await client.GetAsync(path);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("journalCash").GetString().Should().Be("125");
            var serializer = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var evidence = JsonSerializer.Deserialize<PaperSessionCutoffEvidence>(json, serializer)!;
            evidence.SessionId.Should().Be(session.SessionId);
            evidence.AccountId.Should().Be(PaperTradingPortfolio.DefaultAccountId);
            evidence.Cutoff.Should().Be(session.CreatedAt);
            evidence.Currency.Should().Be("USD");
            evidence.PortfolioCash.Should().Be(125m);
            evidence.JournalCash.Should().Be(125m);
            evidence.Breaks.Should().BeEmpty();
            evidence.CoverageBlocker.Should().BeNull();

            await receiptStore.RecordAsync(evidence);
            var latestResponse = await client.GetAsync(latestPath);
            latestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonSerializer.Deserialize<PaperSessionCutoffEvidence>(await latestResponse.Content.ReadAsStringAsync(), serializer)
                .Should().BeEquivalentTo(evidence);
        }

        await using var reader = await CreateAppAsync(
            services => RegisterSessionServices(services, artifacts.RootPath),
            currentUserPermissions: UserPermission.ViewTrades);
        (await reader.GetTestClient().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reader.GetTestClient().GetAsync(latestPath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PaperReconciliation_FundAccountRequiresMatchingScope()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(PaperReconciliation_FundAccountRequiresMatchingScope));
        var foreignAccount = Guid.Parse("2090a7d8-8b6e-4f4a-99be-deb9a3bf1378");
        await using var app = await CreateAppAsync(
            services => RegisterSessionServices(services, artifacts.RootPath,
                (accountId, _) => Task.FromResult(accountId == foreignAccount ? "USD" : null)),
            currentUserPermissions: UserPermission.ViewTrades,
            allowedAccountScopes: [Guid.Parse("b18d8dbb-249c-4cf1-915a-d6f4c067c619")]);
        var persistence = app.Services.GetRequiredService<PaperSessionPersistenceService>();
        var session = await persistence.CreateSessionAsync(new("fund-cutoff-strategy", "Synthetic account cutoff", 125m, ["AAPL"])
        {
            FinancialAccountId = foreignAccount.ToString("D")
        });
        var cutoff = Uri.EscapeDataString(session.CreatedAt.ToString("O", CultureInfo.InvariantCulture));

        var response = await app.GetTestClient().GetAsync(
            $"/api/execution/sessions/{session.SessionId}/reconciliation?cutoff={cutoff}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
