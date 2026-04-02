using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Meridian.Contracts.Schema;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF-specific schema service with data dictionary persistence and export capabilities.
/// Extends SchemaServiceBase for shared schema creation and export logic.
/// </summary>
public sealed class SchemaService : Meridian.Ui.Services.SchemaServiceBase
{
    private static readonly Lazy<SchemaService> _instance = new(() => new SchemaService());
    public static SchemaService Instance => _instance.Value;

    private readonly string _schemasPath;
    private DataDictionary? _dataDictionary;

    private SchemaService()
    {
        _schemasPath = Path.Combine(AppContext.BaseDirectory, "_catalog", "schemas");
    }

    public event EventHandler<Meridian.Ui.Services.DataDictionaryEventArgs>? DictionaryGenerated;

    public async Task<DataDictionary> GetDataDictionaryAsync(CancellationToken ct = default)
    {
        if (_dataDictionary != null) return _dataDictionary;
        _dataDictionary = await LoadOrCreateDataDictionaryAsync();
        return _dataDictionary;
    }

    public async Task<EventSchema?> GetSchemaAsync(string eventType, CancellationToken ct = default)
    {
        var dictionary = await GetDataDictionaryAsync();
        return dictionary.Schemas.TryGetValue(eventType, out var schema) ? schema : null;
    }

    public async Task<DataDictionary> GenerateDataDictionaryAsync(CancellationToken ct = default)
    {
        var dictionary = CreateDataDictionary();
        _dataDictionary = dictionary;
        await SaveDataDictionaryAsync(dictionary);
        DictionaryGenerated?.Invoke(this, new Meridian.Ui.Services.DataDictionaryEventArgs { Dictionary = dictionary });
        return dictionary;
    }

    public async Task<string> ExportDataDictionaryAsync(string format, string? outputPath = null, CancellationToken ct = default)
    {
        var dictionary = await GetDataDictionaryAsync();
        var output = format.ToLower() switch
        {
            "json" => ExportAsJson(dictionary),
            "markdown" or "md" => ExportAsMarkdown(dictionary),
            "csv" => ExportAsCsv(dictionary),
            _ => ExportAsJson(dictionary)
        };

        if (!string.IsNullOrEmpty(outputPath))
        {
            await File.WriteAllTextAsync(outputPath, output);
        }

        return output;
    }

    public async Task<string> GenerateMarkdownDocumentationAsync(CancellationToken ct = default)
    {
        var dictionary = await GetDataDictionaryAsync();
        return ExportAsMarkdown(dictionary);
    }

    private async Task<DataDictionary> LoadOrCreateDataDictionaryAsync(CancellationToken ct = default)
    {
        EnsureSchemasPathExists();
        var dictionaryPath = Path.Combine(_schemasPath, "data_dictionary.json");

        if (File.Exists(dictionaryPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(dictionaryPath);
                var dictionary = JsonSerializer.Deserialize<DataDictionary>(json, DesktopJsonOptions.Compact);
                if (dictionary != null) return dictionary;
            }
            catch (Exception)
            {
            }
        }

        return await GenerateDataDictionaryAsync();
    }

    private async Task SaveDataDictionaryAsync(DataDictionary dictionary, CancellationToken ct = default)
    {
        EnsureSchemasPathExists();
        var dictionaryPath = Path.Combine(_schemasPath, "data_dictionary.json");

        var json = JsonSerializer.Serialize(dictionary, DesktopJsonOptions.PrettyPrint);

        await File.WriteAllTextAsync(dictionaryPath, json);

        var markdownPath = Path.Combine(_schemasPath, "DATA_DICTIONARY.md");
        await File.WriteAllTextAsync(markdownPath, ExportAsMarkdown(dictionary));
    }

    private void EnsureSchemasPathExists()
    {
        if (!Directory.Exists(_schemasPath))
        {
            Directory.CreateDirectory(_schemasPath);
        }
    }
}
