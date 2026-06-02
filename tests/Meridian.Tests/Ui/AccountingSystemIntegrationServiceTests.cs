using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.Auth;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Auth;
using Meridian.Infrastructure.Adapters.QuickBooks;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class AccountingSystemIntegrationServiceTests
{
    [Fact]
    public async Task ImportAsync_WithQuickBooksFixture_ReturnsReadOnlyExternalGlEvidence()
    {
        var service = CreateService();

        var detail = await service.ImportAsync(new AccountingSystemImportRequestDto("quickbooks-fixture"));

        detail.Summary.ProviderId.Should().Be("quickbooks-fixture");
        detail.Summary.ChartAccountCount.Should().BeGreaterThan(0);
        detail.Summary.JournalEntryCount.Should().BeGreaterThan(0);
        detail.Summary.TrialBalanceLineCount.Should().BeGreaterThan(0);
        detail.Summary.Warnings.Should().Contain(warning => warning.Contains("posting/export is disabled", StringComparison.OrdinalIgnoreCase));
        detail.JournalEntries.Should().OnlyContain(entry => entry.TotalDebits == entry.TotalCredits);
    }

    [Fact]
    public async Task ReconcileLatestAsync_WithoutMeridianLedger_ReturnsMissingMeridianBreaksAndDisabledPosting()
    {
        var service = CreateService();
        await service.ImportAsync(new AccountingSystemImportRequestDto("quickbooks-fixture"));

        var summary = await service.ReconcileLatestAsync("quickbooks-fixture");

        summary.PostingEnabled.Should().BeFalse();
        summary.PostingDisabledReason.Should().Contain("disabled");
        summary.Rows.Should().NotBeEmpty();
        summary.Rows.Should().OnlyContain(row => row.Status == AccountingSystemReconciliationStatusDto.MissingMeridian);
        summary.BreakCount.Should().Be(summary.Rows.Count);
    }

    [Fact]
    public async Task ImportAsync_PropagatesCancellation()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.ImportAsync(new AccountingSystemImportRequestDto("quickbooks-fixture"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AccountingSystemEndpoints_ReturnProviderAndReconciliationContracts()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageFundStructure);

        var providersResponse = await app.GetTestClient().GetAsync("/api/accounting-system/providers");
        var reconciliationResponse = await app.GetTestClient().GetAsync("/api/accounting-system/reconciliation/latest");

        providersResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        reconciliationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var providers = await ReadAsync<AccountingSystemProviderDto[]>(providersResponse);
        var reconciliation = await ReadAsync<AccountingSystemReconciliationSummaryDto>(reconciliationResponse);
        providers.Should().Contain(row => row.ProviderId == "quickbooks-fixture" && row.State == AccountingSystemProviderStateDto.Available);
        providers.Should().Contain(row => row.ProviderId == "quickbooks" && row.State == AccountingSystemProviderStateDto.Planned);
        reconciliation.ProviderId.Should().Be("quickbooks-fixture");
        reconciliation.PostingEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task AccountingSystemEndpoints_WithoutAccountingAccess_ReturnForbidden()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewMarketData);

        var response = await app.GetTestClient().GetAsync("/api/accounting-system/providers");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static AccountingSystemIntegrationService CreateService()
        => new([new QuickBooksFixtureAccountingProvider()]);

    private static async Task<WebApplication> CreateAppAsync(UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAccountingSystemProvider, QuickBooksFixtureAccountingProvider>();
        builder.Services.AddSingleton<AccountingSystemIntegrationService>();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter<string>("test"));
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.UseRateLimiter();
        app.MapAccountingSystemEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        result.Should().NotBeNull($"expected {typeof(T).Name}, got {json}");
        return result!;
    }
}
