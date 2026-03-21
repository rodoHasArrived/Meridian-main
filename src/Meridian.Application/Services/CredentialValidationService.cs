using System.Net.Http.Headers;
using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service for validating API credentials at startup.
/// Performs lightweight validation calls to verify credentials are valid.
/// </summary>
public sealed class CredentialValidationService : IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<CredentialValidationService>();
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public CredentialValidationService()
    {
        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        _httpClient = HttpClientFactoryProvider.CreateClient(HttpClientNames.CredentialValidation);
    }

    /// <summary>
    /// Result of credential validation.
    /// </summary>
    public sealed record ValidationResult(
        string Provider,
        bool IsValid,
        string? Message,
        string? AccountInfo,
        TimeSpan ResponseTime
    );

    /// <summary>
    /// Summary of all validation results.
    /// </summary>
    public sealed record ValidationSummary(
        IReadOnlyList<ValidationResult> Results,
        bool AllValid,
        IReadOnlyList<string> Warnings
    );

    /// <summary>
    /// Validates all configured credentials.
    /// </summary>
    public async Task<ValidationSummary> ValidateAllAsync(AppConfig config, CancellationToken ct = default)
    {
        var results = new List<ValidationResult>();
        var warnings = new List<string>();

        var tasks = new List<Task<ValidationResult>>();

        // Validate based on data source
        if (config.DataSource == DataSourceKind.Alpaca || config.Alpaca != null)
        {
            if (config.Alpaca != null &&
                !string.IsNullOrEmpty(config.Alpaca.KeyId) &&
                !config.Alpaca.KeyId.StartsWith("YOUR_") &&
                !config.Alpaca.KeyId.StartsWith("${"))
            {
                tasks.Add(ValidateAlpacaAsync(config.Alpaca, ct));
            }
            else if (config.DataSource == DataSourceKind.Alpaca)
            {
                warnings.Add("Alpaca is configured as data source but credentials are missing");
            }
        }

        if (config.DataSource == DataSourceKind.Polygon || config.Polygon != null)
        {
            if (config.Polygon != null && !string.IsNullOrEmpty(config.Polygon.ApiKey))
            {
                tasks.Add(ValidatePolygonAsync(config.Polygon, ct));
            }
            else if (config.DataSource == DataSourceKind.Polygon)
            {
                warnings.Add("Polygon is configured as data source but API key is missing");
            }
        }

        // Validate backfill providers
        if (config.Backfill?.Providers != null)
        {
            var providers = config.Backfill.Providers;

            if (providers.Tiingo?.Enabled == true && !string.IsNullOrEmpty(providers.Tiingo.ApiToken))
            {
                tasks.Add(ValidateTiingoAsync(providers.Tiingo, ct));
            }

            if (providers.Finnhub?.Enabled == true && !string.IsNullOrEmpty(providers.Finnhub.ApiKey))
            {
                tasks.Add(ValidateFinnhubAsync(providers.Finnhub, ct));
            }

            if (providers.Polygon?.Enabled == true && !string.IsNullOrEmpty(providers.Polygon.ApiKey))
            {
                tasks.Add(ValidatePolygonBackfillAsync(providers.Polygon, ct));
            }

            if (providers.AlphaVantage?.Enabled == true && !string.IsNullOrEmpty(providers.AlphaVantage.ApiKey))
            {
                tasks.Add(ValidateAlphaVantageAsync(providers.AlphaVantage, ct));
            }
        }

        // Execute all validations in parallel
        if (tasks.Count > 0)
        {
            var completed = await Task.WhenAll(tasks);
            results.AddRange(completed);
        }

        // Log results
        foreach (var result in results)
        {
            if (result.IsValid)
            {
                _log.Information("Credential validation passed for {Provider}: {Message} ({ResponseTime}ms)",
                    result.Provider, result.Message, result.ResponseTime.TotalMilliseconds);
            }
            else
            {
                _log.Warning("Credential validation failed for {Provider}: {Message}",
                    result.Provider, result.Message);
                warnings.Add($"{result.Provider}: {result.Message}");
            }
        }

        return new ValidationSummary(
            Results: results,
            AllValid: results.All(r => r.IsValid) && warnings.Count == 0,
            Warnings: warnings
        );
    }

    /// <summary>
    /// Validates Alpaca credentials by calling the account endpoint.
    /// </summary>
    public async Task<ValidationResult> ValidateAlpacaAsync(AlpacaOptions options, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var baseUrl = options.UseSandbox
                ? "https://paper-api.alpaca.markets"
                : "https://api.alpaca.markets";

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v2/account");
            request.Headers.Add("APCA-API-KEY-ID", options.KeyId);
            request.Headers.Add("APCA-API-SECRET-KEY", options.SecretKey);

            using var response = await _httpClient.SendAsync(request, ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                var account = JsonDocument.Parse(content);

                var accountId = account.RootElement.TryGetProperty("id", out var idProp)
                    ? idProp.GetString()
                    : "unknown";
                var status = account.RootElement.TryGetProperty("status", out var statusProp)
                    ? statusProp.GetString()
                    : "unknown";

                return new ValidationResult(
                    Provider: "Alpaca",
                    IsValid: true,
                    Message: $"Account {status}",
                    AccountInfo: $"Account ID: {accountId?[..Math.Min(8, accountId?.Length ?? 0)]}...",
                    ResponseTime: sw.Elapsed
                );
            }

            var errorContent = await response.Content.ReadAsStringAsync(ct);
            return new ValidationResult(
                Provider: "Alpaca",
                IsValid: false,
                Message: $"HTTP {(int)response.StatusCode}: {GetErrorMessage(errorContent)}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (TaskCanceledException)
        {
            return new ValidationResult(
                Provider: "Alpaca",
                IsValid: false,
                Message: "Request timeout",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (Exception ex)
        {
            return new ValidationResult(
                Provider: "Alpaca",
                IsValid: false,
                Message: $"Error: {ex.Message}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
    }

    /// <summary>
    /// Validates Polygon credentials by calling the ticker details endpoint.
    /// </summary>
    public async Task<ValidationResult> ValidatePolygonAsync(PolygonOptions options, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.polygon.io/v3/reference/tickers/AAPL");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);

            using var response = await _httpClient.SendAsync(request, ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ValidationResult(
                    Provider: "Polygon",
                    IsValid: true,
                    Message: "API key valid",
                    AccountInfo: null,
                    ResponseTime: sw.Elapsed
                );
            }

            var errorContent = await response.Content.ReadAsStringAsync(ct);
            return new ValidationResult(
                Provider: "Polygon",
                IsValid: false,
                Message: $"HTTP {(int)response.StatusCode}: {GetErrorMessage(errorContent)}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (TaskCanceledException)
        {
            return new ValidationResult(
                Provider: "Polygon",
                IsValid: false,
                Message: "Request timeout",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (Exception ex)
        {
            return new ValidationResult(
                Provider: "Polygon",
                IsValid: false,
                Message: $"Error: {ex.Message}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
    }

    /// <summary>
    /// Validates Tiingo credentials by calling the meta endpoint.
    /// </summary>
    public async Task<ValidationResult> ValidateTiingoAsync(TiingoConfig options, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.tiingo.com/api/test");
            request.Headers.TryAddWithoutValidation("X-TIINGO-APIKEY", options.ApiToken);

            using var response = await _httpClient.SendAsync(request, ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ValidationResult(
                    Provider: "Tiingo",
                    IsValid: true,
                    Message: "API token valid",
                    AccountInfo: null,
                    ResponseTime: sw.Elapsed
                );
            }

            return new ValidationResult(
                Provider: "Tiingo",
                IsValid: false,
                Message: $"HTTP {(int)response.StatusCode}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (TaskCanceledException)
        {
            return new ValidationResult(
                Provider: "Tiingo",
                IsValid: false,
                Message: "Request timeout",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (Exception ex)
        {
            return new ValidationResult(
                Provider: "Tiingo",
                IsValid: false,
                Message: $"Error: {ex.Message}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
    }

    /// <summary>
    /// Validates Finnhub credentials by calling the profile endpoint.
    /// </summary>
    public async Task<ValidationResult> ValidateFinnhubAsync(FinnhubConfig options, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://finnhub.io/api/v1/stock/profile2?symbol=AAPL");
            request.Headers.TryAddWithoutValidation("X-Finnhub-Token", options.ApiKey);

            using var response = await _httpClient.SendAsync(request, ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                // Finnhub returns empty object for invalid key
                if (content.Length > 10)
                {
                    return new ValidationResult(
                        Provider: "Finnhub",
                        IsValid: true,
                        Message: "API key valid",
                        AccountInfo: null,
                        ResponseTime: sw.Elapsed
                    );
                }
            }

            return new ValidationResult(
                Provider: "Finnhub",
                IsValid: false,
                Message: $"HTTP {(int)response.StatusCode}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (TaskCanceledException)
        {
            return new ValidationResult(
                Provider: "Finnhub",
                IsValid: false,
                Message: "Request timeout",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (Exception ex)
        {
            return new ValidationResult(
                Provider: "Finnhub",
                IsValid: false,
                Message: $"Error: {ex.Message}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
    }

    /// <summary>
    /// Validates Polygon backfill credentials.
    /// </summary>
    public async Task<ValidationResult> ValidatePolygonBackfillAsync(PolygonConfig options, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.polygon.io/v3/reference/tickers/AAPL");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);

            using var response = await _httpClient.SendAsync(request, ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ValidationResult(
                    Provider: "Polygon (Backfill)",
                    IsValid: true,
                    Message: "API key valid",
                    AccountInfo: null,
                    ResponseTime: sw.Elapsed
                );
            }

            return new ValidationResult(
                Provider: "Polygon (Backfill)",
                IsValid: false,
                Message: $"HTTP {(int)response.StatusCode}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (TaskCanceledException)
        {
            return new ValidationResult(
                Provider: "Polygon (Backfill)",
                IsValid: false,
                Message: "Request timeout",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (Exception ex)
        {
            return new ValidationResult(
                Provider: "Polygon (Backfill)",
                IsValid: false,
                Message: $"Error: {ex.Message}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
    }

    /// <summary>
    /// Validates Alpha Vantage credentials.
    /// </summary>
    public async Task<ValidationResult> ValidateAlphaVantageAsync(AlphaVantageConfig options, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var url = $"https://www.alphavantage.co/query?function=GLOBAL_QUOTE&symbol=IBM&apikey={options.ApiKey}";

            using var response = await _httpClient.GetAsync(url, ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);

                // Alpha Vantage returns error message in JSON for invalid keys
                if (content.Contains("Error Message") || content.Contains("Invalid API call"))
                {
                    return new ValidationResult(
                        Provider: "Alpha Vantage",
                        IsValid: false,
                        Message: "Invalid API key",
                        AccountInfo: null,
                        ResponseTime: sw.Elapsed
                    );
                }

                // Check for rate limit message
                if (content.Contains("Thank you for using Alpha Vantage"))
                {
                    return new ValidationResult(
                        Provider: "Alpha Vantage",
                        IsValid: true,
                        Message: "API key valid (rate limit message)",
                        AccountInfo: null,
                        ResponseTime: sw.Elapsed
                    );
                }

                return new ValidationResult(
                    Provider: "Alpha Vantage",
                    IsValid: true,
                    Message: "API key valid",
                    AccountInfo: null,
                    ResponseTime: sw.Elapsed
                );
            }

            return new ValidationResult(
                Provider: "Alpha Vantage",
                IsValid: false,
                Message: $"HTTP {(int)response.StatusCode}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (TaskCanceledException)
        {
            return new ValidationResult(
                Provider: "Alpha Vantage",
                IsValid: false,
                Message: "Request timeout",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
        catch (Exception ex)
        {
            return new ValidationResult(
                Provider: "Alpha Vantage",
                IsValid: false,
                Message: $"Error: {ex.Message}",
                AccountInfo: null,
                ResponseTime: sw.Elapsed
            );
        }
    }

    /// <summary>
    /// Prints a summary of validation results to the console.
    /// </summary>
    public void PrintSummary(ValidationSummary summary, TextWriter? output = null)
    {
        output ??= Console.Out;

        output.WriteLine("\nCredential Validation Results:");
        output.WriteLine("-".PadRight(50, '-'));

        foreach (var result in summary.Results)
        {
            var status = result.IsValid ? "[OK]" : "[FAIL]";
            var time = $"({result.ResponseTime.TotalMilliseconds:F0}ms)";

            output.WriteLine($"  {status} {result.Provider,-20} {result.Message} {time}");

            if (!string.IsNullOrEmpty(result.AccountInfo))
            {
                output.WriteLine($"       {result.AccountInfo}");
            }
        }

        if (summary.Warnings.Count > 0)
        {
            output.WriteLine("\nWarnings:");
            foreach (var warning in summary.Warnings)
            {
                output.WriteLine($"  - {warning}");
            }
        }

        output.WriteLine();
        output.WriteLine(summary.AllValid
            ? "All configured credentials are valid."
            : "Some credentials failed validation. Check warnings above.");
    }

    private string GetErrorMessage(string content)
    {
        try
        {
            var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "Unknown error";
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.GetString() ?? "Unknown error";
        }
        catch (JsonException ex)
        {
            _log.Debug(ex, "Failed to parse error response as JSON, using raw content. Content: {Content}",
                content.Length > 200 ? content[..200] + "..." : content);
        }

        return content.Length > 100 ? content[..100] + "..." : content;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _httpClient.Dispose();
        await Task.CompletedTask;
    }
}
