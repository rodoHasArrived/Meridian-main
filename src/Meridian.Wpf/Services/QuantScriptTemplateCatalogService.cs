using System.Text.Json;
using Microsoft.Extensions.Logging;
using Meridian.QuantScript.Documents;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

public sealed class QuantScriptTemplateCatalogService
{
    private readonly ILogger<QuantScriptTemplateCatalogService> _logger;
    private readonly string _catalogRoot;
    private readonly string _catalogPath;

    public QuantScriptTemplateCatalogService(ILogger<QuantScriptTemplateCatalogService> logger)
        : this(logger, Path.Combine(AppContext.BaseDirectory, "Templates", "QuantScript"))
    {
    }

    public QuantScriptTemplateCatalogService(ILogger<QuantScriptTemplateCatalogService> logger, string catalogRoot)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _catalogRoot = catalogRoot ?? throw new ArgumentNullException(nameof(catalogRoot));
        _catalogPath = Path.Combine(_catalogRoot, "catalog.json");
    }

    public IReadOnlyList<QuantScriptTemplateDefinition> ListTemplates()
    {
        try
        {
            if (!File.Exists(_catalogPath))
                return Array.Empty<QuantScriptTemplateDefinition>();

            var json = File.ReadAllText(_catalogPath);
            var manifest = JsonSerializer.Deserialize(
                json,
                QuantScriptStorageJsonContext.Default.QuantScriptTemplateCatalogManifest);

            return manifest?.Templates
                ?.OrderBy(static template => template.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static template => template.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load QuantScript template catalog from {CatalogPath}", _catalogPath);
            return Array.Empty<QuantScriptTemplateDefinition>();
        }
    }

    public async Task<QuantScriptTemplateDocument> LoadTemplateAsync(string templateId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        var template = ListTemplates().FirstOrDefault(item =>
            string.Equals(item.Id, templateId, StringComparison.OrdinalIgnoreCase));

        if (template is null)
            throw new InvalidOperationException($"QuantScript template '{templateId}' was not found.");

        var contentPath = Path.Combine(_catalogRoot, template.ContentFile);
        if (!File.Exists(contentPath))
            throw new FileNotFoundException($"QuantScript template content file was not found: {contentPath}", contentPath);

        var source = await File.ReadAllTextAsync(contentPath, ct).ConfigureAwait(false);
        return new QuantScriptTemplateDocument(template, source, contentPath);
    }
}
