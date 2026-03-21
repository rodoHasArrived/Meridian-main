using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.UI;

/// <summary>
/// Loads HTML templates from external files in wwwroot/templates/.
/// Templates use mustache-style placeholders (e.g., {{KEY}}) that are replaced at runtime.
/// </summary>
public sealed class HtmlTemplateLoader
{
    private readonly ILogger<HtmlTemplateLoader> _logger;
    private readonly ConcurrentDictionary<string, string> _templateCache = new();
    private readonly string _templatesPath;
    private readonly bool _enableCaching;

    /// <summary>
    /// Creates a new template loader.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="templatesPath">Path to templates directory. If null, uses embedded resources or default path.</param>
    /// <param name="enableCaching">Whether to cache templates in memory. Disable for development.</param>
    public HtmlTemplateLoader(
        ILogger<HtmlTemplateLoader> logger,
        string? templatesPath = null,
        bool enableCaching = true)
    {
        _logger = logger;
        _enableCaching = enableCaching;
        _templatesPath = templatesPath ?? ResolveDefaultTemplatesPath();
    }

    /// <summary>
    /// Loads a template and replaces placeholders with the provided values.
    /// </summary>
    /// <param name="templateName">Name of the template file (e.g., "index.html").</param>
    /// <param name="placeholders">Dictionary of placeholder keys and values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered HTML with placeholders replaced.</returns>
    public async Task<string> LoadTemplateAsync(
        string templateName,
        IReadOnlyDictionary<string, string>? placeholders = null,
        CancellationToken ct = default)
    {
        var template = await GetTemplateContentAsync(templateName, ct).ConfigureAwait(false);

        if (placeholders == null || placeholders.Count == 0)
        {
            return template;
        }

        return ReplacePlaceholders(template, placeholders);
    }

    /// <summary>
    /// Loads a template synchronously and replaces placeholders.
    /// </summary>
    public string LoadTemplate(
        string templateName,
        IReadOnlyDictionary<string, string>? placeholders = null)
    {
        var template = GetTemplateContent(templateName);

        if (placeholders == null || placeholders.Count == 0)
        {
            return template;
        }

        return ReplacePlaceholders(template, placeholders);
    }

    /// <summary>
    /// Clears the template cache, forcing templates to be reloaded from disk.
    /// </summary>
    public void ClearCache()
    {
        _templateCache.Clear();
        _logger.LogInformation("Template cache cleared");
    }

    /// <summary>
    /// Checks if a template exists.
    /// </summary>
    public bool TemplateExists(string templateName)
    {
        var filePath = Path.Combine(_templatesPath, templateName);
        return File.Exists(filePath);
    }

    private async Task<string> GetTemplateContentAsync(string templateName, CancellationToken ct)
    {
        if (_enableCaching && _templateCache.TryGetValue(templateName, out var cached))
        {
            return cached;
        }

        var content = await LoadFromFileAsync(templateName, ct).ConfigureAwait(false);

        if (_enableCaching)
        {
            _templateCache[templateName] = content;
        }

        return content;
    }

    private string GetTemplateContent(string templateName)
    {
        if (_enableCaching && _templateCache.TryGetValue(templateName, out var cached))
        {
            return cached;
        }

        var content = LoadFromFile(templateName);

        if (_enableCaching)
        {
            _templateCache[templateName] = content;
        }

        return content;
    }

    private async Task<string> LoadFromFileAsync(string templateName, CancellationToken ct)
    {
        var filePath = Path.Combine(_templatesPath, templateName);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Template file not found: {FilePath}, falling back to legacy templates", filePath);
            throw new FileNotFoundException($"Template file not found: {templateName}", filePath);
        }

        _logger.LogDebug("Loading template from file: {FilePath}", filePath);
        return await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
    }

    private string LoadFromFile(string templateName)
    {
        var filePath = Path.Combine(_templatesPath, templateName);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Template file not found: {FilePath}, falling back to legacy templates", filePath);
            throw new FileNotFoundException($"Template file not found: {templateName}", filePath);
        }

        _logger.LogDebug("Loading template from file: {FilePath}", filePath);
        return File.ReadAllText(filePath);
    }

    private static string ReplacePlaceholders(string template, IReadOnlyDictionary<string, string> placeholders)
    {
        var result = template;

        foreach (var (key, value) in placeholders)
        {
            // Replace {{KEY}} style placeholders
            result = result.Replace($"{{{{{key}}}}}", EscapeHtml(value));
        }

        return result;
    }

    private static string EscapeHtml(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static string ResolveDefaultTemplatesPath()
    {
        // Try to find templates relative to the executing assembly
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation) ?? ".";

        // Check common locations
        var candidates = new[]
        {
            Path.Combine(assemblyDirectory, "wwwroot", "templates"),
            Path.Combine(assemblyDirectory, "..", "wwwroot", "templates"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "templates"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "templates"),
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        // Default to working directory
        return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "templates");
    }
}

/// <summary>
/// Extension methods for HtmlTemplateLoader.
/// </summary>
public static class HtmlTemplateLoaderExtensions
{
    /// <summary>
    /// Loads the main dashboard (index) template.
    /// </summary>
    public static string LoadIndexTemplate(
        this HtmlTemplateLoader loader,
        string configPath,
        string statusPath,
        string backfillPath)
    {
        return loader.LoadTemplate("index.html", new Dictionary<string, string>
        {
            ["CONFIG_PATH"] = configPath,
            ["STATUS_PATH"] = statusPath,
            ["BACKFILL_PATH"] = backfillPath
        });
    }

    /// <summary>
    /// Loads the credentials dashboard template.
    /// </summary>
    public static string LoadCredentialsTemplate(
        this HtmlTemplateLoader loader,
        Config.AppConfig config)
    {
        var alpacaConfigured = config.Alpaca != null && !string.IsNullOrWhiteSpace(config.Alpaca.KeyId);
        var polygonConfigured = config.Backfill?.Providers?.Polygon != null && !string.IsNullOrWhiteSpace(config.Backfill.Providers.Polygon.ApiKey);
        var tiingoConfigured = config.Backfill?.Providers?.Tiingo != null && !string.IsNullOrWhiteSpace(config.Backfill.Providers.Tiingo.ApiToken);
        var finnhubConfigured = config.Backfill?.Providers?.Finnhub != null && !string.IsNullOrWhiteSpace(config.Backfill.Providers.Finnhub.ApiKey);
        var alphaVantageConfigured = config.Backfill?.Providers?.AlphaVantage != null && !string.IsNullOrWhiteSpace(config.Backfill.Providers.AlphaVantage.ApiKey);
        var nasdaqConfigured = config.Backfill?.Providers?.Nasdaq != null && !string.IsNullOrWhiteSpace(config.Backfill.Providers.Nasdaq.ApiKey);

        return loader.LoadTemplate("credentials.html", new Dictionary<string, string>
        {
            ["ALPACA_CONFIGURED"] = alpacaConfigured ? "true" : "false",
            ["POLYGON_CONFIGURED"] = polygonConfigured ? "true" : "false",
            ["TIINGO_CONFIGURED"] = tiingoConfigured ? "true" : "false",
            ["FINNHUB_CONFIGURED"] = finnhubConfigured ? "true" : "false",
            ["ALPHAVANTAGE_CONFIGURED"] = alphaVantageConfigured ? "true" : "false",
            ["NASDAQ_CONFIGURED"] = nasdaqConfigured ? "true" : "false"
        });
    }
}
