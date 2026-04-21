using System.Net.Http;
using System.Text.Json;
using Meridian.Contracts.Configuration;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for guided setup wizard with preflight connectivity checks.
/// Validates provider credentials and connectivity before enabling services.
/// </summary>
public sealed class SetupWizardService
{
    private readonly HttpClient _httpClient;
    private readonly ConfigService _configService;
    private readonly CredentialService _credentialService;

    public SetupWizardService()
    {
        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        _httpClient = HttpClientFactoryProvider.CreateClient(HttpClientNames.SetupWizard);
        _configService = new ConfigService();
        _credentialService = new CredentialService();
    }

    /// <summary>
    /// Performs preflight checks for all configured providers.
    /// </summary>
    public async Task<PreflightCheckResult> RunPreflightChecksAsync(CancellationToken ct = default)
    {
        var result = new PreflightCheckResult();

        // Check network connectivity
        result.NetworkCheck = await CheckNetworkConnectivityAsync(ct);

        // Check disk space
        result.DiskSpaceCheck = await CheckDiskSpaceAsync(ct);

        // Check storage path permissions
        result.StoragePermissionCheck = await CheckStoragePermissionsAsync(ct);

        // Check collector service availability
        result.CollectorServiceCheck = await CheckCollectorServiceAsync(ct);

        return result;
    }

    /// <summary>
    /// Tests connectivity to a specific provider.
    /// </summary>
    public async Task<ProviderTestResult> TestProviderConnectivityAsync(
        string provider,
        Dictionary<string, string> credentials,
        CancellationToken ct = default)
    {
        var result = new ProviderTestResult { Provider = provider };

        try
        {
            result.StartTime = DateTime.UtcNow;

            switch (provider.ToUpperInvariant())
            {
                case "ALPACA":
                    result = await TestAlpacaAsync(credentials, ct);
                    break;
                case "POLYGON":
                    result = await TestPolygonAsync(credentials, ct);
                    break;
                case "IB":
                case "INTERACTIVEBROKERS":
                    result = await TestInteractiveBrokersAsync(credentials, ct);
                    break;
                case "TIINGO":
                    result = await TestTiingoAsync(credentials, ct);
                    break;
                case "FINNHUB":
                    result = await TestFinnhubAsync(credentials, ct);
                    break;
                case "ALPHAVANTAGE":
                    result = await TestAlphaVantageAsync(credentials, ct);
                    break;
                default:
                    result.Success = false;
                    result.ErrorMessage = $"Unknown provider: {provider}";
                    break;
            }

            result.EndTime = DateTime.UtcNow;
            result.LatencyMs = (int)(result.EndTime - result.StartTime).TotalMilliseconds;
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Connection test cancelled";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<ProviderTestResult> TestAlpacaAsync(
        Dictionary<string, string> credentials,
        CancellationToken ct)
    {
        var result = new ProviderTestResult { Provider = "Alpaca" };

        if (!credentials.TryGetValue("keyId", out var keyId) ||
            !credentials.TryGetValue("secretKey", out var secretKey))
        {
            result.Success = false;
            result.ErrorMessage = "Missing API Key ID or Secret Key";
            return result;
        }

        var useSandbox = credentials.TryGetValue("useSandbox", out var sandbox) &&
                         bool.TryParse(sandbox, out var isSandbox) && isSandbox;

        var baseUrl = useSandbox
            ? "https://paper-api.alpaca.markets"
            : "https://api.alpaca.markets";

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v2/account");
        request.Headers.Add("APCA-API-KEY-ID", keyId);
        request.Headers.Add("APCA-API-SECRET-KEY", secretKey);

        var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            result.Success = true;
            result.StatusMessage = useSandbox ? "Connected to Paper Trading" : "Connected to Live Trading";

            var content = await response.Content.ReadAsStringAsync(ct);
            var json = JsonDocument.Parse(content);
            if (json.RootElement.TryGetProperty("account_number", out var accountNum))
            {
                result.AccountInfo = $"Account: {accountNum.GetString()}";
            }
        }
        else
        {
            result.Success = false;
            result.ErrorMessage = $"Authentication failed: {response.StatusCode}";
        }

        return result;
    }

    private async Task<ProviderTestResult> TestPolygonAsync(
        Dictionary<string, string> credentials,
        CancellationToken ct)
    {
        var result = new ProviderTestResult { Provider = "Polygon" };

        if (!credentials.TryGetValue("apiKey", out var apiKey))
        {
            result.Success = false;
            result.ErrorMessage = "Missing API Key";
            return result;
        }

        var response = await _httpClient.GetAsync(
            $"https://api.polygon.io/v3/reference/tickers?limit=1&apiKey={apiKey}", ct);

        if (response.IsSuccessStatusCode)
        {
            result.Success = true;
            result.StatusMessage = "Connected to Polygon.io";

            var content = await response.Content.ReadAsStringAsync(ct);
            var json = JsonDocument.Parse(content);
            if (json.RootElement.TryGetProperty("status", out var status))
            {
                result.AccountInfo = $"Status: {status.GetString()}";
            }
        }
        else
        {
            result.Success = false;
            result.ErrorMessage = $"Authentication failed: {response.StatusCode}";
        }

        return result;
    }

    private async Task<ProviderTestResult> TestInteractiveBrokersAsync(
        Dictionary<string, string> credentials,
        CancellationToken ct)
    {
        var result = new ProviderTestResult { Provider = "Interactive Brokers" };

        var host = credentials.GetValueOrDefault("host", "127.0.0.1");
        var portStr = credentials.GetValueOrDefault("port", "7497");

        if (!int.TryParse(portStr, out var port))
        {
            result.Success = false;
            result.ErrorMessage = "Invalid port number";
            return result;
        }

        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connectTask = client.ConnectAsync(host, port);

            if (await Task.WhenAny(connectTask, Task.Delay(5000, ct)) == connectTask)
            {
                if (client.Connected)
                {
                    result.Success = true;
                    result.StatusMessage = $"TWS/Gateway reachable at {host}:{port}";
                    result.AccountInfo = "TCP connection successful";
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "Connection refused";
                }
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = "Connection timeout - ensure TWS/Gateway is running";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Connection failed: {ex.Message}";
        }

        return result;
    }

    private static ProviderTestResult ValidateUsernamePassword(string provider, string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new ProviderTestResult
            {
                Provider = provider,
                Success = false,
                ErrorMessage = "Missing username or password"
            };
        }

        return new ProviderTestResult
        {
            Provider = provider,
            Success = true,
            StatusMessage = "Credentials captured (provider connectivity requires service)"
        };
    }

    private static async Task<ProviderTestResult> TestTcpEndpointAsync(
        string provider,
        string host,
        string portStr,
        CancellationToken ct)
    {
        var result = new ProviderTestResult { Provider = provider };

        if (!int.TryParse(portStr, out var port))
        {
            result.Success = false;
            result.ErrorMessage = "Invalid port number";
            return result;
        }

        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connectTask = client.ConnectAsync(host, port);

            if (await Task.WhenAny(connectTask, Task.Delay(5000, ct)) == connectTask)
            {
                result.Success = client.Connected;
                result.StatusMessage = result.Success
                    ? $"Endpoint reachable at {host}:{port}"
                    : "Connection refused";
                result.ErrorMessage = result.Success ? "" : "Connection refused";
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = "Connection timeout";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Connection failed: {ex.Message}";
        }

        return result;
    }

    private async Task<ProviderTestResult> TestTiingoAsync(
        Dictionary<string, string> credentials,
        CancellationToken ct)
    {
        var result = new ProviderTestResult { Provider = "Tiingo" };

        if (!credentials.TryGetValue("token", out var token))
        {
            result.Success = false;
            result.ErrorMessage = "Missing API Token";
            return result;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.tiingo.com/api/test");
        request.Headers.Add("Authorization", $"Token {token}");

        var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            result.Success = true;
            result.StatusMessage = "Connected to Tiingo";
        }
        else
        {
            result.Success = false;
            result.ErrorMessage = $"Authentication failed: {response.StatusCode}";
        }

        return result;
    }

    private async Task<ProviderTestResult> TestFinnhubAsync(
        Dictionary<string, string> credentials,
        CancellationToken ct)
    {
        var result = new ProviderTestResult { Provider = "Finnhub" };

        if (!credentials.TryGetValue("apiKey", out var apiKey))
        {
            result.Success = false;
            result.ErrorMessage = "Missing API Key";
            return result;
        }

        var response = await _httpClient.GetAsync(
            $"https://finnhub.io/api/v1/stock/symbol?exchange=US&token={apiKey}", ct);

        if (response.IsSuccessStatusCode)
        {
            result.Success = true;
            result.StatusMessage = "Connected to Finnhub";
        }
        else
        {
            result.Success = false;
            result.ErrorMessage = $"Authentication failed: {response.StatusCode}";
        }

        return result;
    }

    private async Task<ProviderTestResult> TestAlphaVantageAsync(
        Dictionary<string, string> credentials,
        CancellationToken ct)
    {
        var result = new ProviderTestResult { Provider = "Alpha Vantage" };

        if (!credentials.TryGetValue("apiKey", out var apiKey))
        {
            result.Success = false;
            result.ErrorMessage = "Missing API Key";
            return result;
        }

        var response = await _httpClient.GetAsync(
            $"https://www.alphavantage.co/query?function=TIME_SERIES_INTRADAY&symbol=IBM&interval=5min&apikey={apiKey}", ct);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            if (content.Contains("Error Message") || content.Contains("Invalid API"))
            {
                result.Success = false;
                result.ErrorMessage = "Invalid API key";
            }
            else
            {
                result.Success = true;
                result.StatusMessage = "Connected to Alpha Vantage";
            }
        }
        else
        {
            result.Success = false;
            result.ErrorMessage = $"Authentication failed: {response.StatusCode}";
        }

        return result;
    }

    private async Task<CheckResult> CheckNetworkConnectivityAsync(CancellationToken ct)
    {
        var result = new CheckResult { Name = "Network Connectivity" };

        try
        {
            var response = await _httpClient.GetAsync("https://api.github.com", ct);
            result.Success = response.IsSuccessStatusCode;
            result.Message = result.Success ? "Internet connection available" : "Limited connectivity";
        }
        catch
        {
            result.Success = false;
            result.Message = "No internet connection";
        }

        return result;
    }

    private async Task<CheckResult> CheckDiskSpaceAsync(CancellationToken ct)
    {
        var result = new CheckResult { Name = "Disk Space" };

        try
        {
            var config = await _configService.LoadConfigAsync(ct);
            var dataDir = _configService.ResolveDataRoot(config);
            var driveInfo = new DriveInfo(Path.GetPathRoot(dataDir) ?? "C:");

            var freeGb = driveInfo.AvailableFreeSpace / (1024.0 * 1024 * 1024);
            result.Success = freeGb >= 1.0; // At least 1GB free
            result.Message = $"{freeGb:F1} GB available";

            if (!result.Success)
            {
                result.Message += " (minimum 1 GB recommended)";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Unable to check: {ex.Message}";
        }

        return result;
    }

    private async Task<CheckResult> CheckStoragePermissionsAsync(CancellationToken ct)
    {
        var result = new CheckResult { Name = "Storage Permissions" };

        try
        {
            var config = await _configService.LoadConfigAsync(ct);
            var dataDir = _configService.ResolveDataRoot(config);
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            // Test write permission
            var testFile = Path.Combine(dataDir, ".write_test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);

            result.Success = true;
            result.Message = "Read/write access confirmed";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Permission denied: {ex.Message}";
        }

        return result;
    }

    private async Task<CheckResult> CheckCollectorServiceAsync(CancellationToken ct)
    {
        var result = new CheckResult { Name = "Collector Service" };

        try
        {
            var response = await _httpClient.GetAsync("http://localhost:8080/health", ct);
            result.Success = response.IsSuccessStatusCode;
            result.Message = result.Success ? "Service is running" : "Service not responding";
        }
        catch
        {
            result.Success = false;
            result.Message = "Service not running (will start automatically)";
        }

        return result;
    }

    /// <summary>
    /// Gets available setup presets for quick configuration.
    /// </summary>
    public IReadOnlyList<SetupPreset> GetSetupPresets()
    {
        return new List<SetupPreset>
        {
            new()
            {
                Id = "day-trader",
                Name = "Day Trader",
                Description = "Real-time streaming focus with L2 depth for active trading",
                Icon = "\uE9D9",
                RecommendedProviders = new[] { "Interactive Brokers", "Alpaca", "Polygon" },
                DefaultSymbols = new[] { "SPY", "QQQ", "AAPL", "TSLA", "NVDA" },
                SubscribeTrades = true,
                SubscribeDepth = true,
                SubscribeQuotes = true,
                DepthLevels = 10,
                EnableBackfill = false,
                StorageTier = "Hot"
            },
            new()
            {
                Id = "researcher",
                Name = "Researcher",
                Description = "Historical data focus for backtesting and analysis",
                Icon = "\uE8A1",
                RecommendedProviders = new[] { "Polygon", "Tiingo", "Yahoo Finance" },
                DefaultSymbols = new[] { "SPY", "QQQ", "IWM", "DIA", "VTI" },
                SubscribeTrades = true,
                SubscribeDepth = false,
                SubscribeQuotes = false,
                DepthLevels = 0,
                EnableBackfill = true,
                StorageTier = "Cold"
            },
            new()
            {
                Id = "data-archivist",
                Name = "Data Archivist",
                Description = "Comprehensive collection and long-term storage",
                Icon = "\uE8B7",
                RecommendedProviders = new[] { "Interactive Brokers", "Polygon", "Alpaca" },
                DefaultSymbols = new[] { "SPY", "QQQ", "AAPL", "MSFT", "GOOGL", "AMZN", "META", "NVDA" },
                SubscribeTrades = true,
                SubscribeDepth = true,
                SubscribeQuotes = true,
                DepthLevels = 20,
                EnableBackfill = true,
                StorageTier = "Tiered"
            },
            new()
            {
                Id = "options-trader",
                Name = "Options Trader",
                Description = "Options chain monitoring with underlying and Greeks tracking",
                Icon = "\uE9CE",
                RecommendedProviders = new[] { "Interactive Brokers", "Polygon", "Alpaca" },
                DefaultSymbols = new[] { "SPY", "QQQ", "AAPL", "TSLA", "IWM", "GLD" },
                SubscribeTrades = true,
                SubscribeDepth = true,
                SubscribeQuotes = true,
                DepthLevels = 5,
                EnableBackfill = true,
                StorageTier = "Hot"
            },
            new()
            {
                Id = "crypto-enthusiast",
                Name = "Crypto Enthusiast",
                Description = "Cryptocurrency market data with 24/7 streaming support",
                Icon = "\uEA3F",
                RecommendedProviders = new[] { "Alpaca", "Polygon", "Yahoo Finance" },
                DefaultSymbols = new[] { "BTC/USD", "ETH/USD", "SOL/USD", "DOGE/USD", "ADA/USD" },
                SubscribeTrades = true,
                SubscribeDepth = true,
                SubscribeQuotes = true,
                DepthLevels = 10,
                EnableBackfill = true,
                StorageTier = "Tiered"
            },
            new()
            {
                Id = "minimal",
                Name = "Minimal Setup",
                Description = "Basic configuration for testing and evaluation",
                Icon = "\uE74C",
                RecommendedProviders = new[] { "Alpaca", "Yahoo Finance", "Polygon" },
                DefaultSymbols = new[] { "SPY" },
                SubscribeTrades = true,
                SubscribeDepth = false,
                SubscribeQuotes = false,
                DepthLevels = 0,
                EnableBackfill = false,
                StorageTier = "Hot"
            }
        };
    }

    /// <summary>
    /// Applies a setup preset to the configuration.
    /// </summary>
    public async Task ApplyPresetAsync(SetupPreset preset, string provider, CancellationToken ct = default)
    {
        var config = await _configService.LoadConfigAsync() ?? new AppConfig();

        config.DataSource = provider;

        var symbols = preset.DefaultSymbols.Select(s => new SymbolConfig
        {
            Symbol = s,
            SubscribeTrades = preset.SubscribeTrades,
            SubscribeDepth = preset.SubscribeDepth,
            DepthLevels = preset.DepthLevels,
            SecurityType = "STK",
            Exchange = "SMART",
            Currency = "USD"
        }).ToArray();

        config.Symbols = symbols;
        config.Backfill ??= new BackfillConfig();
        config.Backfill.Enabled = preset.EnableBackfill;

        await _configService.SaveConfigAsync(config);
    }

    /// <summary>
    /// Saves provider credentials securely.
    /// </summary>
    public async Task SaveCredentialsAsync(string provider, Dictionary<string, string> credentials, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || credentials.Count == 0)
        {
            return;
        }

        var normalizedProvider = provider.Trim().ToUpperInvariant();
        var config = await _configService.LoadConfigAsync() ?? new AppConfig();

        switch (normalizedProvider)
        {
            case "ALPACA":
                SaveEnvironmentVariable("ALPACA__KEYID", credentials.GetValueOrDefault("keyId"));
                SaveEnvironmentVariable("ALPACA__SECRETKEY", credentials.GetValueOrDefault("secretKey"));

                config.Alpaca ??= new AlpacaOptions();
                config.Alpaca.KeyId = credentials.GetValueOrDefault("keyId");
                config.Alpaca.SecretKey = credentials.GetValueOrDefault("secretKey");
                config.Alpaca.UseSandbox = bool.TryParse(credentials.GetValueOrDefault("useSandbox"), out var useSandbox) && useSandbox;
                break;

            case "POLYGON":
                SaveEnvironmentVariable("POLYGON__APIKEY", credentials.GetValueOrDefault("apiKey"));

                config.Polygon ??= new PolygonOptions();
                config.Polygon.ApiKey = credentials.GetValueOrDefault("apiKey");
                break;

            case "TIINGO":
                SaveEnvironmentVariable("TIINGO__TOKEN", credentials.GetValueOrDefault("token"));
                break;

            case "FINNHUB":
                SaveEnvironmentVariable("FINNHUB__APIKEY", credentials.GetValueOrDefault("apiKey"));
                break;

            case "ALPHAVANTAGE":
            case "ALPHA VANTAGE":
                SaveEnvironmentVariable("ALPHAVANTAGE__APIKEY", credentials.GetValueOrDefault("apiKey"));
                break;
        }

        await _configService.SaveConfigAsync(config);
    }

    private static void SaveEnvironmentVariable(string variableName, string? value)
    {
        var normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        Environment.SetEnvironmentVariable(variableName, normalizedValue);
        Environment.SetEnvironmentVariable(variableName, normalizedValue, EnvironmentVariableTarget.User);
    }
}

/// <summary>
/// Result of preflight checks.
/// </summary>
public sealed class PreflightCheckResult
{
    public CheckResult NetworkCheck { get; set; } = new();
    public CheckResult DiskSpaceCheck { get; set; } = new();
    public CheckResult StoragePermissionCheck { get; set; } = new();
    public CheckResult CollectorServiceCheck { get; set; } = new();

    public bool AllPassed => NetworkCheck.Success &&
                             DiskSpaceCheck.Success &&
                             StoragePermissionCheck.Success;

    public IEnumerable<CheckResult> GetAllChecks()
    {
        yield return NetworkCheck;
        yield return DiskSpaceCheck;
        yield return StoragePermissionCheck;
        yield return CollectorServiceCheck;
    }
}

/// <summary>
/// Individual check result.
/// </summary>
public sealed class CheckResult
{
    public string Name { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Result of provider connectivity test.
/// </summary>
public sealed class ProviderTestResult
{
    public string Provider { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string AccountInfo { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int LatencyMs { get; set; }
}

/// <summary>
/// Setup preset for quick configuration.
/// </summary>
public sealed class SetupPreset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string[] RecommendedProviders { get; set; } = Array.Empty<string>();
    public string[] DefaultSymbols { get; set; } = Array.Empty<string>();
    public bool SubscribeTrades { get; set; }
    public bool SubscribeDepth { get; set; }
    public bool SubscribeQuotes { get; set; }
    public int DepthLevels { get; set; }
    public bool EnableBackfill { get; set; }
    public string StorageTier { get; set; } = string.Empty;
}
