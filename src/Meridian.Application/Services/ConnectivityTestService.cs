using System.Diagnostics;
using System.Net.Sockets;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service for testing connectivity to data providers and reporting results.
/// Helps users diagnose network and configuration issues.
/// </summary>
public sealed class ConnectivityTestService : IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<ConnectivityTestService>();
    private readonly HttpClient _httpClient;
    private readonly ProgressDisplayService _progress;
    private bool _disposed;

    public ConnectivityTestService()
    {
        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        _httpClient = HttpClientFactoryProvider.CreateClient(HttpClientNames.ConnectivityTest);
        _progress = new ProgressDisplayService();
    }

    /// <summary>
    /// Result of a connectivity test.
    /// </summary>
    public sealed record ConnectivityTestResult(
        string Provider,
        bool IsReachable,
        TimeSpan ResponseTime,
        string? Error,
        string? Suggestion
    );

    /// <summary>
    /// Summary of all connectivity tests.
    /// </summary>
    public sealed record ConnectivitySummary(
        IReadOnlyList<ConnectivityTestResult> Results,
        bool AllReachable,
        IReadOnlyList<string> NetworkIssues
    );

    /// <summary>
    /// Tests connectivity to all configured providers.
    /// </summary>
    public async Task<ConnectivitySummary> TestAllAsync(AppConfig config, CancellationToken ct = default)
    {
        var results = new List<ConnectivityTestResult>();
        var networkIssues = new List<string>();

        // First test general internet connectivity
        var internetCheck = await TestInternetConnectivityAsync(ct);
        if (!internetCheck.IsReachable)
        {
            networkIssues.Add("No internet connectivity detected");
            return new ConnectivitySummary(results, false, networkIssues);
        }

        // Test DNS resolution
        var dnsCheck = await TestDnsResolutionAsync(ct);
        if (!dnsCheck.IsReachable)
        {
            networkIssues.Add("DNS resolution issues detected");
        }

        // Build list of providers to test based on configuration
        var providerTests = new List<(string Name, Func<CancellationToken, Task<ConnectivityTestResult>> Test)>();

        // Alpaca
        if (config.DataSource == DataSourceKind.Alpaca || config.Alpaca != null)
        {
            var isSandbox = config.Alpaca?.UseSandbox ?? false;
            providerTests.Add(("Alpaca Markets (Trading API)", ct => TestAlpacaTradingApiAsync(isSandbox, ct)));
            providerTests.Add(("Alpaca Markets (Data API)", ct => TestAlpacaDataApiAsync(ct)));
        }

        // Polygon
        if (config.DataSource == DataSourceKind.Polygon || config.Polygon != null ||
            config.Backfill?.Providers?.Polygon?.Enabled == true)
        {
            providerTests.Add(("Polygon.io", TestPolygonAsync));
        }

        // Tiingo
        if (config.Backfill?.Providers?.Tiingo?.Enabled == true)
        {
            providerTests.Add(("Tiingo", TestTiingoAsync));
        }

        // Finnhub
        if (config.Backfill?.Providers?.Finnhub?.Enabled == true)
        {
            providerTests.Add(("Finnhub", TestFinnhubAsync));
        }

        // Yahoo Finance
        if (config.Backfill?.Providers?.Yahoo?.Enabled != false)
        {
            providerTests.Add(("Yahoo Finance", TestYahooFinanceAsync));
        }

        // Interactive Brokers
        if (config.DataSource == DataSourceKind.IB || config.IB != null)
        {
            var host = config.IB?.Host ?? "127.0.0.1";
            var port = config.IB?.Port ?? 7496;
            providerTests.Add(($"Interactive Brokers ({host}:{port})", ct => TestIBGatewayAsync(host, port, ct)));
        }

        // Run tests
        Console.WriteLine();
        Console.WriteLine("  Testing provider connectivity...");
        Console.WriteLine("  " + new string('-', 50));

        foreach (var (name, test) in providerTests)
        {
            ct.ThrowIfCancellationRequested();

            Console.Write($"    Testing {name}... ");

            var result = await test(ct);
            results.Add(result);

            if (result.IsReachable)
            {
                Console.WriteLine($"[OK] ({result.ResponseTime.TotalMilliseconds:F0}ms)");
            }
            else
            {
                Console.WriteLine($"[FAIL] {result.Error}");
                if (!string.IsNullOrEmpty(result.Suggestion))
                {
                    Console.WriteLine($"           Suggestion: {result.Suggestion}");
                }
            }
        }

        return new ConnectivitySummary(
            Results: results,
            AllReachable: results.All(r => r.IsReachable),
            NetworkIssues: networkIssues
        );
    }

    /// <summary>
    /// Displays the connectivity test summary.
    /// </summary>
    public void DisplaySummary(ConnectivitySummary summary)
    {
        Console.WriteLine();

        if (summary.NetworkIssues.Count > 0)
        {
            Console.WriteLine("  Network Issues:");
            foreach (var issue in summary.NetworkIssues)
            {
                Console.WriteLine($"    - {issue}");
            }
            Console.WriteLine();
        }

        var reachable = summary.Results.Count(r => r.IsReachable);
        var total = summary.Results.Count;

        Console.WriteLine($"  Summary: {reachable}/{total} providers reachable");

        if (!summary.AllReachable)
        {
            Console.WriteLine();
            Console.WriteLine("  Troubleshooting Tips:");
            Console.WriteLine("    1. Check your internet connection");
            Console.WriteLine("    2. Verify firewall settings allow outbound HTTPS");
            Console.WriteLine("    3. For IB Gateway: ensure TWS/Gateway is running");
            Console.WriteLine("    4. Check if the provider is experiencing an outage");
        }

        Console.WriteLine();
    }

    private async Task<ConnectivityTestResult> TestInternetConnectivityAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Use multiple endpoints to verify connectivity
            var endpoints = new[] { "https://www.google.com", "https://www.cloudflare.com", "https://www.microsoft.com" };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    using var response = await _httpClient.GetAsync(endpoint, ct);
                    sw.Stop();
                    return new ConnectivityTestResult(
                        Provider: "Internet",
                        IsReachable: true,
                        ResponseTime: sw.Elapsed,
                        Error: null,
                        Suggestion: null
                    );
                }
                catch
                {
                    // Try next endpoint
                }
            }

            return new ConnectivityTestResult(
                Provider: "Internet",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: "No connectivity",
                Suggestion: "Check your internet connection and firewall settings"
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectivityTestResult(
                Provider: "Internet",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: ex.Message,
                Suggestion: "Check your internet connection"
            );
        }
    }

    private async Task<ConnectivityTestResult> TestDnsResolutionAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var host = await System.Net.Dns.GetHostEntryAsync("api.alpaca.markets", ct);
            sw.Stop();

            return new ConnectivityTestResult(
                Provider: "DNS",
                IsReachable: host.AddressList.Length > 0,
                ResponseTime: sw.Elapsed,
                Error: host.AddressList.Length == 0 ? "No addresses returned" : null,
                Suggestion: null
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectivityTestResult(
                Provider: "DNS",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: ex.Message,
                Suggestion: "Check your DNS settings or try using 8.8.8.8"
            );
        }
    }

    private async Task<ConnectivityTestResult> TestAlpacaTradingApiAsync(bool sandbox, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var baseUrl = sandbox ? "https://paper-api.alpaca.markets" : "https://api.alpaca.markets";

        try
        {
            using var response = await _httpClient.GetAsync($"{baseUrl}/v2/clock", ct);
            sw.Stop();

            // 401/403 is expected without auth, but proves connectivity
            return new ConnectivityTestResult(
                Provider: "Alpaca Trading API",
                IsReachable: true,
                ResponseTime: sw.Elapsed,
                Error: null,
                Suggestion: null
            );
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            return new ConnectivityTestResult(
                Provider: "Alpaca Trading API",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: "Timeout",
                Suggestion: "The Alpaca API is slow or unreachable. Try again later."
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectivityTestResult(
                Provider: "Alpaca Trading API",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: ex.Message,
                Suggestion: "Check if Alpaca is experiencing an outage"
            );
        }
    }

    private async Task<ConnectivityTestResult> TestAlpacaDataApiAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.GetAsync("https://data.alpaca.markets/v2/stocks/SPY/trades/latest", ct);
            sw.Stop();

            return new ConnectivityTestResult(
                Provider: "Alpaca Data API",
                IsReachable: true,
                ResponseTime: sw.Elapsed,
                Error: null,
                Suggestion: null
            );
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            return new ConnectivityTestResult(
                Provider: "Alpaca Data API",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: "Timeout",
                Suggestion: "Data API may be slow. Check Alpaca status page."
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectivityTestResult(
                Provider: "Alpaca Data API",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: ex.Message,
                Suggestion: null
            );
        }
    }

    private async Task<ConnectivityTestResult> TestPolygonAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.GetAsync("https://api.polygon.io/v2/last/trade/AAPL", ct);
            sw.Stop();

            return new ConnectivityTestResult(
                Provider: "Polygon.io",
                IsReachable: true,
                ResponseTime: sw.Elapsed,
                Error: null,
                Suggestion: null
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectivityTestResult(
                Provider: "Polygon.io",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: ex.Message,
                Suggestion: "Check if Polygon.io is accessible from your network"
            );
        }
    }

    private async Task<ConnectivityTestResult> TestTiingoAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.GetAsync("https://api.tiingo.com/", ct);
            sw.Stop();

            return new ConnectivityTestResult(
                Provider: "Tiingo",
                IsReachable: true,
                ResponseTime: sw.Elapsed,
                Error: null,
                Suggestion: null
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectivityTestResult(
                Provider: "Tiingo",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: ex.Message,
                Suggestion: null
            );
        }
    }

    private async Task<ConnectivityTestResult> TestFinnhubAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.GetAsync("https://finnhub.io/", ct);
            sw.Stop();

            return new ConnectivityTestResult(
                Provider: "Finnhub",
                IsReachable: true,
                ResponseTime: sw.Elapsed,
                Error: null,
                Suggestion: null
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectivityTestResult(
                Provider: "Finnhub",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: ex.Message,
                Suggestion: null
            );
        }
    }

    private async Task<ConnectivityTestResult> TestYahooFinanceAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.GetAsync("https://query1.finance.yahoo.com/v8/finance/chart/SPY", ct);
            sw.Stop();

            return new ConnectivityTestResult(
                Provider: "Yahoo Finance",
                IsReachable: response.IsSuccessStatusCode,
                ResponseTime: sw.Elapsed,
                Error: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
                Suggestion: null
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectivityTestResult(
                Provider: "Yahoo Finance",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: ex.Message,
                Suggestion: "Yahoo Finance may be blocking your IP. Try later."
            );
        }
    }

    private async Task<ConnectivityTestResult> TestIBGatewayAsync(string host, int port, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(5000, ct)) == connectTask;
            sw.Stop();

            if (completed && client.Connected)
            {
                return new ConnectivityTestResult(
                    Provider: "IB Gateway",
                    IsReachable: true,
                    ResponseTime: sw.Elapsed,
                    Error: null,
                    Suggestion: null
                );
            }

            return new ConnectivityTestResult(
                Provider: "IB Gateway",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: "Connection refused or timeout",
                Suggestion: $"Ensure TWS/IB Gateway is running and accepting connections on {host}:{port}"
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectivityTestResult(
                Provider: "IB Gateway",
                IsReachable: false,
                ResponseTime: sw.Elapsed,
                Error: ex.Message,
                Suggestion: $"Start TWS or IB Gateway with API connections enabled on port {port}"
            );
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _httpClient.Dispose();
        _progress.Dispose();
        await Task.CompletedTask;
    }
}
