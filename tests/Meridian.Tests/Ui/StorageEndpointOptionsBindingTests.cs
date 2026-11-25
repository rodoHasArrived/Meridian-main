using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
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

    [Theory]
    [InlineData(UiApiRoutes.StorageStats)]
    [InlineData(UiApiRoutes.StorageHealth)]
    public async Task StorageRead_ResolvesConfiguredOptionsInsteadOfRequestValues(string route)
    {
        var trustedRoot = Path.Combine(TestDataRoot, "trusted");
        var requestedRoot = Path.Combine(TestDataRoot, "requested");
        Directory.CreateDirectory(trustedRoot);
        await WriteCompletedDayAsync(requestedRoot);
        await using var app = await CreateAppAsync(new StorageOptions { RootPath = trustedRoot });
        using var client = app.GetTestClient();

        using var response = await client.GetAsync($"{route}?rootPath={Uri.EscapeDataString(requestedRoot)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("rootPath").GetString().Should().Be(trustedRoot);
        Directory.GetFiles(requestedRoot).Should().HaveCount(1);
    }

    [Fact]
    public async Task TierMigration_OutsideRootRequestIsRejectedBeforeInvokingTheService()
    {
        var trustedRoot = Path.Combine(TestDataRoot, "trusted");
        var outside = Path.Combine(TestDataRoot, "trusted-other");
        var name = await WriteCompletedDayAsync(outside);
        var migration = new Mock<ITierMigrationService>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(new StorageOptions { RootPath = trustedRoot }, migration.Object);
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(UiApiRoutes.StorageTiersMigrate,
            new { TargetTier = "Warm", SourcePath = Path.Combine(outside, name), DeleteSource = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        migration.VerifyNoOtherCalls();
        File.Exists(Path.Combine(outside, name)).Should().BeTrue();
    }

    [Fact]
    public async Task TierMigration_ValidNormalizedSourceRetainsPayloadAndDeletesOnlyItsSource()
    {
        var root = Path.Combine(TestDataRoot, "trusted");
        var sourceDirectory = Path.Combine(root, "hot");
        var name = await WriteCompletedDayAsync(sourceDirectory);
        var source = Path.Combine(sourceDirectory, name);
        var expected = await File.ReadAllTextAsync(source);
        var options = CreateTierOptions(root);
        await using var app = await CreateAppAsync(options, new TierMigrationService(options));
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(UiApiRoutes.StorageTiersMigrate,
            new { TargetTier = "Warm", SourcePath = Path.Combine(sourceDirectory, ".", name), DeleteSource = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("filesProcessed").GetInt32().Should().Be(1);
        File.Exists(source).Should().BeFalse();
        await using var file = File.OpenRead(Path.Combine(root, "warm", "hot", name + ".gz"));
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        (await reader.ReadToEndAsync()).Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TierMigration_LinkEscapeCannotReadOrDeleteOutsideEvidence(bool directoryLink)
    {
        var root = Path.Combine(TestDataRoot, "trusted");
        var outside = Path.Combine(TestDataRoot, "outside");
        var name = await WriteCompletedDayAsync(outside);
        var evidence = Path.Combine(outside, name);
        var expected = await File.ReadAllTextAsync(evidence);
        Directory.CreateDirectory(root);
        var link = Path.Combine(root, directoryLink ? "linked-directory" : "linked-file.jsonl");
        if (directoryLink)
            Directory.CreateSymbolicLink(link, outside);
        else
            File.CreateSymbolicLink(link, evidence);
        try
        {
            var options = CreateTierOptions(root);
            await using var app = await CreateAppAsync(options, new TierMigrationService(options));
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync(UiApiRoutes.StorageTiersMigrate,
                new { TargetTier = "Warm", SourcePath = directoryLink ? Path.Combine(link, name) : link, DeleteSource = true });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            body.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            body.RootElement.GetProperty("filesProcessed").GetInt32().Should().Be(0);
            (await File.ReadAllTextAsync(evidence)).Should().Be(expected);
            Directory.Exists(Path.Combine(root, "warm")).Should().BeFalse();
        }
        finally
        {
            if (directoryLink)
                Directory.Delete(link);
            else
                File.Delete(link);
        }
    }

    private static StorageOptions CreateTierOptions(string root) => new()
    {
        RootPath = root,
        Tiering = new TieringOptions
        {
            Enabled = true,
            Tiers = [new TierConfig { Name = "Warm", Path = Path.Combine(root, "warm"), Format = "jsonl", Compression = CompressionCodec.Gzip }]
        }
    };

    private static async Task<string> WriteCompletedDayAsync(string root)
    {
        Directory.CreateDirectory(root);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var fileName = $"AAPL.Trade.{yesterday:yyyy-MM-dd}.jsonl";
        await File.WriteAllTextAsync(Path.Combine(root, fileName),
            "{\"Symbol\":\"AAPL\",\"Price\":100.5,\"Size\":10}\n");
        return fileName;
    }

    private static async Task<WebApplication> CreateAppAsync(StorageOptions? options, ITierMigrationService? migration = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Mock.Of<IStorageSearchService>());
        builder.Services.AddSingleton(Mock.Of<IFileMaintenanceService>());
        builder.Services.AddSingleton(migration ?? Mock.Of<ITierMigrationService>());
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
