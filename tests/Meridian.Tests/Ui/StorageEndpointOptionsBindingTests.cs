using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Storage.Services;
using Meridian.Tests.Infrastructure;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class StorageEndpointOptionsBindingTests : TempDirectoryTestBase
{
    [Fact]
    public async Task ConvertParquet_RequestBodyCannotReplaceTheConfiguredStorageRoot()
    {
        var trustedRoot = Path.Combine(TestDataRoot, "trusted");
        var requestedRoot = Path.Combine(TestDataRoot, "requested");
        var fileName = await WriteCompletedDayAsync(trustedRoot);
        await WriteCompletedDayAsync(requestedRoot);
        await using var app = await CreateAppAsync(new StorageOptions { RootPath = trustedRoot });
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(UiApiRoutes.StorageConvertParquet,
            new StorageOptions { RootPath = requestedRoot });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("filesConverted").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("recordsConverted").GetInt64().Should().Be(1);
        body.RootElement.GetProperty("errors").GetInt32().Should().Be(0);
        File.Exists(Path.Combine(trustedRoot, "_parquet", Path.ChangeExtension(fileName, ".parquet")))
            .Should().BeTrue();
        Directory.Exists(Path.Combine(requestedRoot, "_parquet")).Should().BeFalse();
    }

    [Fact]
    public async Task ConvertParquet_UnregisteredStorageOptionsCannotFallBackToRequestBody()
    {
        var requestedRoot = Path.Combine(TestDataRoot, "requested");
        await WriteCompletedDayAsync(requestedRoot);
        await using var app = await CreateAppAsync(options: null);
        using var client = app.GetTestClient();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PostAsJsonAsync(UiApiRoutes.StorageConvertParquet,
                new StorageOptions { RootPath = requestedRoot }));

        failure.Message.Should().Contain(nameof(StorageOptions))
            .And.Contain("registered");
        Directory.Exists(Path.Combine(requestedRoot, "_parquet")).Should().BeFalse();
        Directory.GetFiles(requestedRoot).Should().HaveCount(1);
    }

    private static async Task<string> WriteCompletedDayAsync(string root)
    {
        Directory.CreateDirectory(root);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var fileName = $"AAPL.Trade.{yesterday:yyyy-MM-dd}.jsonl";
        await File.WriteAllTextAsync(Path.Combine(root, fileName),
            "{\"Symbol\":\"AAPL\",\"Price\":100.5,\"Size\":10}\n");
        return fileName;
    }

    private static async Task<WebApplication> CreateAppAsync(StorageOptions? options)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Mock.Of<IStorageSearchService>());
        builder.Services.AddSingleton(Mock.Of<IFileMaintenanceService>());
        builder.Services.AddSingleton(Mock.Of<ITierMigrationService>());
        if (options is not null)
            builder.Services.AddSingleton(options);
        builder.Services.AddRateLimiter(settings => settings.AddPolicy(UiEndpoints.MutationRateLimitPolicy,
            _ => RateLimitPartition.GetNoLimiter<string>("storage-options-binding-test")));
        var app = builder.Build();
        app.UseRateLimiter();
        app.Use((context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ManageStorage;
            return next();
        });
        app.MapStorageEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }
}
