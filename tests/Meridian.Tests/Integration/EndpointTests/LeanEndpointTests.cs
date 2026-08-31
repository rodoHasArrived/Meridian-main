using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for the QuantConnect Lean API endpoints.
/// Covers status, config, verification, algorithms, the honest 501 refusals on the unimplemented
/// sync and backtest lifecycle routes, auto-export, results ingestion, and symbol mapping.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class LeanEndpointTests : IDisposable, IClassFixture<EndpointTestFixture>
{
    // The Lean reads expose the deployment's Lean install path, its algorithm source listing, and
    // auto-export destinations, so they answer the Strategy workspace's own permissions rather than
    // any signed-in caller. ViewStrategies is the read half of that pair; ManageStrategies drives the
    // mutations, exactly as the routes declare.
    private readonly HttpClient _strategyReadClient;
    private readonly HttpClient _strategyMutationClient;

    public LeanEndpointTests(EndpointTestFixture fixture)
    {
        _strategyReadClient = fixture.CreatePermittedClient(UserPermission.ViewStrategies);
        _strategyMutationClient = fixture.CreatePermittedClient(UserPermission.ManageStrategies);
    }

    public void Dispose()
    {
        _strategyReadClient.Dispose();
        _strategyMutationClient.Dispose();
    }

    // -------------------------------------------------------------------------
    // GET /api/lean/status
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetLeanStatus_ReturnsJson()
    {
        var response = await _strategyReadClient.GetAsync("/api/lean/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("installed", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetLeanStatus_WhenNoLeanPath_InstalledIsFalse()
    {
        // LEAN_PATH is not set in the test environment
        var response = await _strategyReadClient.GetAsync("/api/lean/status");
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        doc.RootElement.GetProperty("installed").GetBoolean().Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // GET /api/lean/config
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetLeanConfig_ReturnsJson()
    {
        var response = await _strategyReadClient.GetAsync("/api/lean/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("algorithmLanguage", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // POST /api/lean/verify
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VerifyLean_ReturnsJsonWithChecks()
    {
        var response = await _strategyMutationClient.PostAsync("/api/lean/verify", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("checks", out var checks).Should().BeTrue();
        checks.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task VerifyLean_WhenNoLeanPath_InstalledIsFalse()
    {
        var response = await _strategyMutationClient.PostAsync("/api/lean/verify", content: null);
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        doc.RootElement.GetProperty("installed").GetBoolean().Should().BeFalse();
        doc.RootElement.TryGetProperty("message", out var msg).Should().BeTrue();
        msg.GetString().Should().NotBeNullOrEmpty();
    }

    // -------------------------------------------------------------------------
    // GET /api/lean/algorithms
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAlgorithms_ReturnsJsonWithTotalField()
    {
        var response = await _strategyReadClient.GetAsync("/api/lean/algorithms");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("algorithms", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("total", out var total).Should().BeTrue();
        total.GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    // -------------------------------------------------------------------------
    // POST /api/lean/sync and GET /api/lean/sync/status
    //
    // No Lean engine integration exists: sync jobs used to be fabricated as "queued" and never
    // ran. The routes stay mapped so clients get an honest 501 problem document, not a 404.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StartSync_ReturnsNotImplementedProblem()
    {
        var payload = new { symbols = new[] { "SPY" } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _strategyMutationClient.PostAsync("/api/lean/sync", content);

        response.StatusCode.Should().Be(
            HttpStatusCode.NotImplemented,
            "no sync job ever ran, so an honest 501 beats a fabricated 'queued' response");

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(501);
        doc.RootElement.GetProperty("type").GetString().Should().Contain("not-implemented");
        doc.RootElement.GetProperty("detail").GetString().Should().Contain("No Lean engine integration exists");
    }

    [Fact]
    public async Task GetSyncStatus_ReturnsNotImplementedProblem()
    {
        var response = await _strategyReadClient.GetAsync("/api/lean/sync/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.GetProperty("type").GetString().Should().Contain("not-implemented");
    }

    // -------------------------------------------------------------------------
    // POST /api/lean/backtest/start, GET /{id}/status, GET /{id}/results
    //
    // The fabricated lifecycle (create "queued", never transition, hardcode zero metrics for an
    // unreachable "completed" state) is gone; all three routes answer 501. Stop and delete keep
    // their 404 semantics for unknown ids, and /api/lean/results/ingest remains the real path for
    // recording externally run Lean backtests.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StartBacktest_ReturnsNotImplementedProblem()
    {
        var startPayload = new { algorithmName = "SampleAlgorithm", algorithmLanguage = "CSharp" };
        var startContent = new StringContent(JsonSerializer.Serialize(startPayload), Encoding.UTF8, "application/json");

        var response = await _strategyMutationClient.PostAsync("/api/lean/backtest/start", startContent);

        response.StatusCode.Should().Be(
            HttpStatusCode.NotImplemented,
            "Meridian does not launch Lean backtests, and a 'queued' job that never runs is a fabrication");

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(501);
        doc.RootElement.GetProperty("type").GetString().Should().Contain("not-implemented");
        doc.RootElement.GetProperty("detail").GetString().Should().Contain("results/ingest");
    }

    [Fact]
    public async Task GetBacktestStatus_ReturnsNotImplementedProblem()
    {
        var response = await _strategyReadClient.GetAsync("/api/lean/backtest/nonexistent-id-xyz/status");

        response.StatusCode.Should().Be(
            HttpStatusCode.NotImplemented,
            "there is no lifecycle to report a status for, whatever the id");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("type").GetString().Should().Contain("not-implemented");
    }

    [Fact]
    public async Task GetBacktestResults_ReturnsNotImplementedProblem()
    {
        var response = await _strategyReadClient.GetAsync("/api/lean/backtest/nonexistent-id-xyz/results");

        response.StatusCode.Should().Be(
            HttpStatusCode.NotImplemented,
            "the old handler hardcoded zero metrics; honest refusal replaces fabricated results");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("type").GetString().Should().Contain("not-implemented");
    }

    [Fact]
    public async Task StopBacktest_UnknownId_Returns404()
    {
        var response = await _strategyMutationClient.PostAsync("/api/lean/backtest/nonexistent-id-xyz/stop", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteBacktest_UnknownId_Returns404()
    {
        var response = await _strategyMutationClient.DeleteAsync("/api/lean/backtest/nonexistent-id-xyz/delete");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // GET /api/lean/backtest/history
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBacktestHistory_ReturnsJson()
    {
        var response = await _strategyReadClient.GetAsync("/api/lean/backtest/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("backtests", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("total", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetBacktestHistory_WithLimitParam_ReturnsJson()
    {
        var response = await _strategyReadClient.GetAsync("/api/lean/backtest/history?limit=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -------------------------------------------------------------------------
    // GET /api/lean/auto-export
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAutoExportStatus_ReturnsJson()
    {
        var response = await _strategyReadClient.GetAsync("/api/lean/auto-export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("enabled", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // POST /api/lean/auto-export/configure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfigureAutoExport_Enable_ReturnsSuccess()
    {
        var payload = new { enabled = false, leanDataPath = "/tmp/lean-test-data", intervalSeconds = 60 };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _strategyMutationClient.PostAsync("/api/lean/auto-export/configure", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("success", out var success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("enabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ConfigureAutoExport_NullBody_StillReturnsOk()
    {
        var response = await _strategyMutationClient.PostAsync("/api/lean/auto-export/configure",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -------------------------------------------------------------------------
    // POST /api/lean/results/ingest
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IngestResults_MissingFilePath_ReturnsBadRequest()
    {
        var payload = new { resultsFilePath = (string?)null };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _strategyMutationClient.PostAsync("/api/lean/results/ingest", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resultsFilePath");
    }

    [Fact]
    public async Task IngestResults_NonExistentFile_Returns404()
    {
        var payload = new { resultsFilePath = "/nonexistent/path/results.json" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _strategyMutationClient.PostAsync("/api/lean/results/ingest", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task IngestResults_InvalidJson_ReturnsBadRequest()
    {
        // Create a temp file with invalid JSON
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "not valid json {{{{");

            var payload = new { resultsFilePath = tempFile };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _strategyMutationClient.PostAsync("/api/lean/results/ingest", content);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("error");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task IngestResults_ValidLeanResultsFile_ReturnsSuccess()
    {
        // Minimal Lean backtest result structure
        var leanResult = new
        {
            AlgorithmConfiguration = new { Algorithm = "TestAlgorithm" },
            Statistics = new Dictionary<string, string>
            {
                { "Total Return", "15%" },
                { "Sharpe Ratio", "1.5" },
                { "Total Trades", "42" }
            }
        };

        var tempFile = Path.GetTempFileName() + ".json";
        try
        {
            await File.WriteAllTextAsync(tempFile, JsonSerializer.Serialize(leanResult));

            var payload = new { resultsFilePath = tempFile, algorithmName = "TestAlgorithm" };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _strategyMutationClient.PostAsync("/api/lean/results/ingest", content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.TryGetProperty("backtestId", out var btId).Should().BeTrue();
            btId.GetString().Should().NotBeNullOrEmpty();
            doc.RootElement.GetProperty("algorithmName").GetString().Should().Be("TestAlgorithm");

            var history = await _strategyReadClient.GetAsync("/api/lean/backtest/history?limit=50");
            history.StatusCode.Should().Be(HttpStatusCode.OK);
            var historyDoc = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
            historyDoc.RootElement
                .GetProperty("backtests")
                .EnumerateArray()
                .Any(entry => string.Equals(entry.GetProperty("backtestId").GetString(), btId.GetString(), StringComparison.Ordinal))
                .Should()
                .BeTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // -------------------------------------------------------------------------
    // GET /api/lean/symbol-map
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetSymbolMap_NoSymbols_ReturnsMappingsArray()
    {
        var response = await _strategyReadClient.GetAsync("/api/lean/symbol-map");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("mappings", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("total", out var total).Should().BeTrue();
        total.GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetSymbolMap_WithEquitySymbols_ReturnsMappings()
    {
        var response = await _strategyReadClient.GetAsync("/api/lean/symbol-map?symbols=SPY,AAPL");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.GetProperty("total").GetInt32().Should().Be(2);

        var mappings = doc.RootElement.GetProperty("mappings");
        mappings.GetArrayLength().Should().Be(2);

        // Verify MDC → Lean mapping for a well-known equity
        var spy = mappings.EnumerateArray()
            .FirstOrDefault(m => m.GetProperty("mdcSymbol").GetString() == "SPY");
        spy.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        spy.GetProperty("leanTicker").GetString().Should().Be("spy");
        spy.GetProperty("securityType").GetString().Should().Be("equity");
        spy.GetProperty("market").GetString().Should().Be("usa");
    }
}
