using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for the QuantConnect Lean API endpoints.
/// Covers status, config, verification, algorithms, sync, backtest lifecycle,
/// auto-export, results ingestion, and symbol mapping.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class LeanEndpointTests
{
    private readonly HttpClient _client;

    public LeanEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    // -------------------------------------------------------------------------
    // GET /api/lean/status
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetLeanStatus_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/lean/status");

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
        var response = await _client.GetAsync("/api/lean/status");
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
        var response = await _client.GetAsync("/api/lean/config");

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
        var response = await _client.PostAsync("/api/lean/verify", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("checks", out var checks).Should().BeTrue();
        checks.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task VerifyLean_WhenNoLeanPath_InstalledIsFalse()
    {
        var response = await _client.PostAsync("/api/lean/verify", content: null);
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
        var response = await _client.GetAsync("/api/lean/algorithms");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("algorithms", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("total", out var total).Should().BeTrue();
        total.GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    // -------------------------------------------------------------------------
    // POST /api/lean/sync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StartSync_ReturnsJobIdOrError()
    {
        var payload = new { symbols = new[] { "SPY" } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/lean/sync", content);

        // Either 200 (queued) or 200 with error message when LEAN_DATA_PATH not set
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetString().Should().BeOneOf("queued", "failed");
    }

    // -------------------------------------------------------------------------
    // GET /api/lean/sync/status
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetSyncStatus_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/lean/sync/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("isRunning", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // POST /api/lean/backtest/start  →  GET /api/lean/backtest/{id}/status
    //   →  GET /api/lean/backtest/{id}/results  →  POST /stop  →  DELETE
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BacktestLifecycle_StartStatusResultsStopDelete()
    {
        // 1. Start backtest
        var startPayload = new { algorithmName = "SampleAlgorithm", algorithmLanguage = "CSharp" };
        var startContent = new StringContent(JsonSerializer.Serialize(startPayload), Encoding.UTF8, "application/json");
        var startResp = await _client.PostAsync("/api/lean/backtest/start", startContent);
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var startBody = await startResp.Content.ReadAsStringAsync();
        var startDoc = JsonDocument.Parse(startBody);
        startDoc.RootElement.TryGetProperty("backtestId", out var idElem).Should().BeTrue();
        // When LEAN_PATH is not configured, backtestId may be null and status "failed"
        var backtestId = idElem.ValueKind == JsonValueKind.String ? idElem.GetString() : null;
        var startStatus = startDoc.RootElement.GetProperty("status").GetString();

        if (backtestId == null || startStatus == "failed")
        {
            // No Lean installation in test environment — verify the error response is well-formed
            startDoc.RootElement.TryGetProperty("error", out var errElem).Should().BeTrue();
            errElem.GetString().Should().NotBeNullOrEmpty();
            return; // remaining lifecycle steps require a real backtestId
        }

        // 2. Get status
        var statusResp = await _client.GetAsync($"/api/lean/backtest/{backtestId}/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusDoc = JsonDocument.Parse(await statusResp.Content.ReadAsStringAsync());
        statusDoc.RootElement.GetProperty("backtestId").GetString().Should().Be(backtestId);

        // 3. Get results (backtest not completed yet — should return info message)
        var resultsResp = await _client.GetAsync($"/api/lean/backtest/{backtestId}/results");
        resultsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var resultsDoc = JsonDocument.Parse(await resultsResp.Content.ReadAsStringAsync());
        resultsDoc.RootElement.GetProperty("backtestId").GetString().Should().Be(backtestId);

        // 4. Stop
        var stopResp = await _client.PostAsync($"/api/lean/backtest/{backtestId}/stop", content: null);
        stopResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Delete
        var deleteResp = await _client.DeleteAsync($"/api/lean/backtest/{backtestId}/delete");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var deleteDoc = JsonDocument.Parse(await deleteResp.Content.ReadAsStringAsync());
        deleteDoc.RootElement.GetProperty("deleted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetBacktestStatus_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/api/lean/backtest/nonexistent-id-xyz/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBacktestResults_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/api/lean/backtest/nonexistent-id-xyz/results");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StopBacktest_UnknownId_Returns404()
    {
        var response = await _client.PostAsync("/api/lean/backtest/nonexistent-id-xyz/stop", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteBacktest_UnknownId_Returns404()
    {
        var response = await _client.DeleteAsync("/api/lean/backtest/nonexistent-id-xyz/delete");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // GET /api/lean/backtest/history
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBacktestHistory_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/lean/backtest/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("backtests", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("total", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetBacktestHistory_WithLimitParam_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/lean/backtest/history?limit=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -------------------------------------------------------------------------
    // GET /api/lean/auto-export
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAutoExportStatus_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/lean/auto-export");

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

        var response = await _client.PostAsync("/api/lean/auto-export/configure", content);

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
        var response = await _client.PostAsync("/api/lean/auto-export/configure",
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

        var response = await _client.PostAsync("/api/lean/results/ingest", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resultsFilePath");
    }

    [Fact]
    public async Task IngestResults_NonExistentFile_Returns404()
    {
        var payload = new { resultsFilePath = "/nonexistent/path/results.json" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/lean/results/ingest", content);

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

            var response = await _client.PostAsync("/api/lean/results/ingest", content);

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

            var response = await _client.PostAsync("/api/lean/results/ingest", content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.TryGetProperty("backtestId", out var btId).Should().BeTrue();
            btId.GetString().Should().NotBeNullOrEmpty();
            doc.RootElement.GetProperty("algorithmName").GetString().Should().Be("TestAlgorithm");
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
        var response = await _client.GetAsync("/api/lean/symbol-map");

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
        var response = await _client.GetAsync("/api/lean/symbol-map?symbols=SPY,AAPL");

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
