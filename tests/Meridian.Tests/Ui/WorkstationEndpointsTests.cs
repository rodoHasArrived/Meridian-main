using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_WithStrategyReadService_ShouldReturnServiceBackedBootstrapPayloads()
    {
        await using var app = await CreateAppAsync(services =>
        {
            var store = new StrategyRunStore();
            services.AddSingleton<IStrategyRepository>(store);
            services.AddSingleton<PortfolioReadService>();
            services.AddSingleton<LedgerReadService>();
            services.AddSingleton<StrategyRunReadService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-latest",
            strategyId: "carry-1",
            strategyName: "Carry Pair",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/fx/spot",
            feedReference: "synthetic:fx"));
        await store.RecordRunAsync(BuildRun(
            runId: "run-prior",
            strategyId: "meanrev-1",
            strategyName: "Mean Reversion",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities"));

        var client = app.GetTestClient();

        using var session = await ReadJsonAsync(client, "/api/workstation/session");
        session.RootElement.GetProperty("displayName").GetString().Should().Be("Carry Pair Desk");
        session.RootElement.GetProperty("role").GetString().Should().Be("Research Lead");
        session.RootElement.GetProperty("environment").GetString().Should().Be("paper");
        session.RootElement.GetProperty("activeWorkspace").GetString().Should().Be("research");
        session.RootElement.GetProperty("latestRun").GetProperty("runId").GetString().Should().Be("run-latest");
        session.RootElement.GetProperty("workspaceSummary").GetProperty("totalRuns").GetInt32().Should().Be(2);
        session.RootElement.GetProperty("workspaceSummary").GetProperty("ledgerCoverage").GetInt32().Should().Be(2);
        session.RootElement.GetProperty("workspaceSummary").GetProperty("portfolioCoverage").GetInt32().Should().Be(2);

        using var research = await ReadJsonAsync(client, "/api/workstation/research");
        research.RootElement.GetProperty("workspace").GetProperty("totalRuns").GetInt32().Should().Be(2);
        research.RootElement.GetProperty("workspace").GetProperty("latestRunId").GetString().Should().Be("run-latest");
        research.RootElement.GetProperty("workspace").GetProperty("promotionCandidates").GetInt32().Should().Be(2);

        var runs = research.RootElement.GetProperty("runs");
        runs.GetArrayLength().Should().Be(2);

        var latestRun = runs[0];
        latestRun.GetProperty("id").GetString().Should().Be("run-latest");
        latestRun.GetProperty("strategyName").GetString().Should().Be("Carry Pair");
        latestRun.GetProperty("mode").GetString().Should().Be("paper");
        latestRun.GetProperty("status").GetString().Should().Be("Completed");
        latestRun.GetProperty("dataset").GetString().Should().Be("dataset/fx/spot");
        latestRun.GetProperty("notes").GetString().Should().Contain("paper review");

        research.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "active-runs" &&
                               metric.GetProperty("value").GetString() == "0");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithoutStrategyReadService_ShouldReturnFallbackPayloads()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        using var session = await ReadJsonAsync(client, "/api/workstation/session");
        session.RootElement.GetProperty("displayName").GetString().Should().Be("Meridian Operator");
        session.RootElement.GetProperty("role").GetString().Should().Be("Research Lead");
        session.RootElement.GetProperty("environment").GetString().Should().Be("paper");
        session.RootElement.GetProperty("activeWorkspace").GetString().Should().Be("research");
        session.RootElement.GetProperty("commandCount").GetInt32().Should().Be(6);

        using var research = await ReadJsonAsync(client, "/api/workstation/research");
        research.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "active-runs" &&
                               metric.GetProperty("value").GetString() == "24");

        var runs = research.RootElement.GetProperty("runs");
        runs.GetArrayLength().Should().Be(1);
        runs[0].GetProperty("id").GetString().Should().Be("run-research-001");
        runs[0].GetProperty("strategyName").GetString().Should().Be("Mean Reversion FX");
    }

    private static async Task<WebApplication> CreateAppAsync(Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapWorkstationEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static StrategyRunEntry BuildRun(
        string runId,
        string strategyId,
        string strategyName,
        RunType runType,
        DateTimeOffset startedAt,
        string? datasetReference = null,
        string? feedReference = null)
    {
        return StrategyRunEntry.Start(strategyId, strategyName, runType) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = startedAt.AddMinutes(30),
            DatasetReference = datasetReference,
            FeedReference = feedRefer