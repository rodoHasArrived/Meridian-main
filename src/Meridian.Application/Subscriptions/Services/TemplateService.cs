using System.Text.Json;
using System.Threading;
using Meridian.Application.Config;
using Meridian.Application.Subscriptions.Models;
using Meridian.Application.UI;
using Meridian.Storage.Archival;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Service for managing subscription templates (equity groups, sectors, indices).
/// </summary>
public sealed class TemplateService
{
    private readonly ConfigStore _configStore;
    private readonly string _templatesPath;
    private readonly Dictionary<string, SymbolTemplate> _builtInTemplates;

    public TemplateService(ConfigStore configStore, string? templatesPath = null)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _templatesPath = templatesPath ?? Path.Combine(
            Path.GetDirectoryName(configStore.ConfigPath) ?? ".",
            "templates.json");
        _builtInTemplates = InitializeBuiltInTemplates();
    }

    /// <summary>
    /// Get all available templates (built-in + custom).
    /// </summary>
    public async Task<IReadOnlyList<SymbolTemplate>> GetAllTemplatesAsync(CancellationToken ct = default)
    {
        var templates = new List<SymbolTemplate>(_builtInTemplates.Values);
        var custom = await LoadCustomTemplatesAsync(ct);
        templates.AddRange(custom);
        return templates;
    }

    /// <summary>
    /// Get a specific template by ID.
    /// </summary>
    public async Task<SymbolTemplate?> GetTemplateAsync(string templateId, CancellationToken ct = default)
    {
        if (_builtInTemplates.TryGetValue(templateId, out var builtIn))
            return builtIn;

        var custom = await LoadCustomTemplatesAsync(ct);
        return custom.FirstOrDefault(t => t.Id.Equals(templateId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Apply a template to the current subscriptions.
    /// </summary>
    public async Task<ApplyTemplateResult> ApplyTemplateAsync(
        ApplyTemplateRequest request,
        CancellationToken ct = default)
    {
        var template = await GetTemplateAsync(request.TemplateId, ct);
        if (template is null)
        {
            return new ApplyTemplateResult(false, 0, 0, $"Template '{request.TemplateId}' not found");
        }

        var cfg = _configStore.Load();
        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        var defaults = request.OverrideDefaults ?? template.Defaults;
        var added = 0;
        var skipped = 0;

        if (request.ReplaceExisting)
        {
            existingSymbols.Clear();
        }

        foreach (var symbol in template.Symbols)
        {
            if (existingSymbols.ContainsKey(symbol))
            {
                skipped++;
                continue;
            }

            var symbolConfig = new SymbolConfig(
                Symbol: symbol,
                SubscribeTrades: defaults.SubscribeTrades,
                SubscribeDepth: defaults.SubscribeDepth,
                DepthLevels: defaults.DepthLevels,
                SecurityType: defaults.SecurityType,
                Exchange: defaults.Exchange,
                Currency: defaults.Currency
            );

            existingSymbols[symbol] = symbolConfig;
            added++;
        }

        var next = cfg with { Symbols = existingSymbols.Values.ToArray() };
        await _configStore.SaveAsync(next);

        return new ApplyTemplateResult(true, added, skipped, $"Applied template '{template.Name}'");
    }

    /// <summary>
    /// Create a custom template.
    /// </summary>
    public async Task<SymbolTemplate> CreateTemplateAsync(
        string name,
        string description,
        TemplateCategory category,
        string[] symbols,
        TemplateSubscriptionDefaults? defaults = null,
        CancellationToken ct = default)
    {
        var templates = await LoadCustomTemplatesAsync(ct);
        var id = $"custom_{Guid.NewGuid():N}"[..16];

        var template = new SymbolTemplate(
            Id: id,
            Name: name,
            Description: description,
            Category: category,
            Symbols: symbols,
            Defaults: defaults ?? new TemplateSubscriptionDefaults()
        );

        templates.Add(template);
        await SaveCustomTemplatesAsync(templates, ct);

        return template;
    }

    /// <summary>
    /// Delete a custom template.
    /// </summary>
    public async Task<bool> DeleteTemplateAsync(string templateId, CancellationToken ct = default)
    {
        if (_builtInTemplates.ContainsKey(templateId))
            return false; // Cannot delete built-in templates

        var templates = await LoadCustomTemplatesAsync(ct);
        var removed = templates.RemoveAll(t => t.Id.Equals(templateId, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            await SaveCustomTemplatesAsync(templates, ct);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Create a template from current subscriptions.
    /// </summary>
    public async Task<SymbolTemplate> CreateFromCurrentAsync(
        string name,
        string description,
        CancellationToken ct = default)
    {
        var cfg = _configStore.Load();
        var symbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .Select(s => s.Symbol)
            .ToArray();

        return await CreateTemplateAsync(
            name,
            description,
            TemplateCategory.Custom,
            symbols,
            ct: ct);
    }

    private async Task<List<SymbolTemplate>> LoadCustomTemplatesAsync(CancellationToken ct)
    {
        if (!File.Exists(_templatesPath))
            return new List<SymbolTemplate>();

        try
        {
            var json = await File.ReadAllTextAsync(_templatesPath, ct);
            var templates = JsonSerializer.Deserialize<List<SymbolTemplate>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return templates ?? new List<SymbolTemplate>();
        }
        catch
        {
            return new List<SymbolTemplate>();
        }
    }

    private async Task SaveCustomTemplatesAsync(List<SymbolTemplate> templates, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(templates, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await AtomicFileWriter.WriteAsync(_templatesPath, json, ct);
    }

    private static Dictionary<string, SymbolTemplate> InitializeBuiltInTemplates()
    {
        var templates = new Dictionary<string, SymbolTemplate>(StringComparer.OrdinalIgnoreCase);

        // Technology sector
        templates["tech_mega"] = new SymbolTemplate(
            Id: "tech_mega",
            Name: "Mega Cap Technology",
            Description: "Top technology companies by market cap",
            Category: TemplateCategory.Sector,
            Symbols: new[] { "AAPL", "MSFT", "GOOGL", "AMZN", "META", "NVDA", "TSLA" },
            Defaults: new TemplateSubscriptionDefaults()
        );

        templates["tech_semis"] = new SymbolTemplate(
            Id: "tech_semis",
            Name: "Semiconductors",
            Description: "Major semiconductor companies",
            Category: TemplateCategory.Sector,
            Symbols: new[] { "NVDA", "AMD", "INTC", "AVGO", "QCOM", "TXN", "MU", "AMAT", "LRCX", "KLAC" },
            Defaults: new TemplateSubscriptionDefaults()
        );

        // Healthcare sector
        templates["healthcare_pharma"] = new SymbolTemplate(
            Id: "healthcare_pharma",
            Name: "Big Pharma",
            Description: "Major pharmaceutical companies",
            Category: TemplateCategory.Sector,
            Symbols: new[] { "JNJ", "PFE", "MRK", "ABBV", "LLY", "BMY", "AMGN", "GILD" },
            Defaults: new TemplateSubscriptionDefaults()
        );

        // Financial sector
        templates["financials_banks"] = new SymbolTemplate(
            Id: "financials_banks",
            Name: "Major Banks",
            Description: "Largest US banks",
            Category: TemplateCategory.Sector,
            Symbols: new[] { "JPM", "BAC", "WFC", "C", "GS", "MS", "USB", "PNC" },
            Defaults: new TemplateSubscriptionDefaults()
        );

        // Index-based templates
        templates["dow30"] = new SymbolTemplate(
            Id: "dow30",
            Name: "Dow Jones 30",
            Description: "All 30 Dow Jones Industrial Average components",
            Category: TemplateCategory.Index,
            Symbols: new[]
            {
                "AAPL", "AMGN", "AXP", "BA", "CAT", "CRM", "CSCO", "CVX", "DIS", "DOW",
                "GS", "HD", "HON", "IBM", "INTC", "JNJ", "JPM", "KO", "MCD", "MMM",
                "MRK", "MSFT", "NKE", "PG", "TRV", "UNH", "V", "VZ", "WBA", "WMT"
            },
            Defaults: new TemplateSubscriptionDefaults()
        );

        // Market cap templates
        templates["largecap_growth"] = new SymbolTemplate(
            Id: "largecap_growth",
            Name: "Large Cap Growth",
            Description: "Large cap growth stocks",
            Category: TemplateCategory.MarketCap,
            Symbols: new[] { "AAPL", "MSFT", "GOOGL", "AMZN", "NVDA", "META", "TSLA", "AVGO", "COST", "NFLX" },
            Defaults: new TemplateSubscriptionDefaults()
        );

        templates["largecap_value"] = new SymbolTemplate(
            Id: "largecap_value",
            Name: "Large Cap Value",
            Description: "Large cap value stocks",
            Category: TemplateCategory.MarketCap,
            Symbols: new[] { "BRK.B", "JPM", "JNJ", "XOM", "PG", "CVX", "HD", "MRK", "PEP", "ABBV" },
            Defaults: new TemplateSubscriptionDefaults()
        );

        // ETF templates
        templates["major_etfs"] = new SymbolTemplate(
            Id: "major_etfs",
            Name: "Major ETFs",
            Description: "Most liquid ETFs",
            Category: TemplateCategory.Custom,
            Symbols: new[] { "SPY", "QQQ", "IWM", "DIA", "VTI", "VOO", "XLF", "XLK", "XLE", "XLV" },
            Defaults: new TemplateSubscriptionDefaults()
        );

        return templates;
    }
}

/// <summary>
/// Result of applying a template.
/// </summary>
public sealed record ApplyTemplateResult(
    bool Success,
    int SymbolsAdded,
    int SymbolsSkipped,
    string? Message = null
);
