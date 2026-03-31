using System.Text.Json;
using Meridian.Contracts.Export;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Export preset management service with file-based storage.
/// File path is injected via constructor rather than requiring platform-specific overrides.
/// </summary>
public class ExportPresetServiceBase
{
    protected const string PresetsFileName = "export_presets.json";

    private readonly string _presetsFilePath;
    private readonly List<ExportPreset> _presets = new();
    private bool _initialized;

    /// <summary>
    /// Event raised when presets are modified.
    /// </summary>
    public event EventHandler? PresetsChanged;

    /// <summary>
    /// Gets all available export presets.
    /// </summary>
    public IReadOnlyList<ExportPreset> Presets => _presets.AsReadOnly();

    /// <summary>
    /// Creates a new instance with the specified presets directory path.
    /// The presets file will be stored as <c>{presetsDirectoryPath}/export_presets.json</c>.
    /// </summary>
    /// <param name="presetsDirectoryPath">
    /// Directory where the presets file will be stored.
    /// The directory will be created if it does not exist.
    /// </param>
    protected ExportPresetServiceBase(string presetsDirectoryPath)
    {
        Directory.CreateDirectory(presetsDirectoryPath);
        _presetsFilePath = Path.Combine(presetsDirectoryPath, PresetsFileName);
    }

    /// <summary>
    /// Initializes the service and loads presets.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        await LoadPresetsAsync(cancellationToken);
        _initialized = true;
    }

    /// <summary>
    /// Loads presets from storage.
    /// </summary>
    public async Task LoadPresetsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (File.Exists(_presetsFilePath))
            {
                var json = await File.ReadAllTextAsync(_presetsFilePath, cancellationToken);
                var presets = JsonSerializer.Deserialize<List<ExportPreset>>(json);
                if (presets != null)
                {
                    _presets.Clear();
                    _presets.AddRange(presets);
                }
            }

            if (_presets.Count == 0)
            {
                _presets.AddRange(GetBuiltInPresets());
                await SavePresetsAsync(cancellationToken);
            }

            EnsureBuiltInPresets();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExportPresetService] Error loading export presets: {ex.Message}");

            if (_presets.Count == 0)
            {
                _presets.AddRange(GetBuiltInPresets());
            }
        }
    }

    /// <summary>
    /// Saves presets to storage.
    /// </summary>
    public async Task SavePresetsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var options = DesktopJsonOptions.PrettyPrint;
            var json = JsonSerializer.Serialize(_presets, options);
            await File.WriteAllTextAsync(_presetsFilePath, json, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExportPresetService] Error saving export presets: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a new export preset.
    /// </summary>
    public async Task<ExportPreset> CreatePresetAsync(
        string name,
        string? description = null,
        ExportPresetFormat format = ExportPresetFormat.Parquet,
        string? destination = null,
        CancellationToken cancellationToken = default)
    {
        var preset = new ExportPreset
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Description = description,
            Format = format,
            Destination = destination ?? GetDefaultDestination(format),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsBuiltIn = false
        };

        _presets.Add(preset);
        await SavePresetsAsync(cancellationToken);
        PresetsChanged?.Invoke(this, EventArgs.Empty);

        return preset;
    }

    /// <summary>
    /// Updates an existing preset.
    /// </summary>
    public async Task<bool> UpdatePresetAsync(ExportPreset preset, CancellationToken cancellationToken = default)
    {
        var index = _presets.FindIndex(p => p.Id == preset.Id);
        if (index == -1 || _presets[index].IsBuiltIn)
            return false;

        preset.UpdatedAt = DateTime.UtcNow;
        _presets[index] = preset;
        await SavePresetsAsync(cancellationToken);
        PresetsChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// Deletes a preset by ID.
    /// </summary>
    public async Task<bool> DeletePresetAsync(string presetId, CancellationToken cancellationToken = default)
    {
        var preset = _presets.FirstOrDefault(p => p.Id == presetId);
        if (preset == null || preset.IsBuiltIn)
            return false;

        _presets.Remove(preset);
        await SavePresetsAsync(cancellationToken);
        PresetsChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// Gets a preset by ID.
    /// </summary>
    public ExportPreset? GetPreset(string presetId) =>
        _presets.FirstOrDefault(p => p.Id == presetId);

    /// <summary>
    /// Gets a preset by name.
    /// </summary>
    public ExportPreset? GetPresetByName(string name) =>
        _presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Duplicates an existing preset with a new name.
    /// </summary>
    public async Task<ExportPreset> DuplicatePresetAsync(string presetId, string newName, CancellationToken cancellationToken = default)
    {
        var source = _presets.FirstOrDefault(p => p.Id == presetId)
            ?? throw new ArgumentException($"Preset not found: {presetId}");

        var duplicate = new ExportPreset
        {
            Id = Guid.NewGuid().ToString(),
            Name = newName,
            Description = source.Description,
            Format = source.Format,
            Compression = source.Compression,
            Destination = source.Destination,
            FilenamePattern = source.FilenamePattern,
            Filters = new ExportPresetFilters
            {
                EventTypes = source.Filters.EventTypes.ToArray(),
                Symbols = source.Filters.Symbols.ToArray(),
                DateRangeType = source.Filters.DateRangeType,
                CustomStartDate = source.Filters.CustomStartDate,
                CustomEndDate = source.Filters.CustomEndDate,
                SessionFilter = source.Filters.SessionFilter,
                MinQualityScore = source.Filters.MinQualityScore
            },
            Schedule = source.Schedule,
            ScheduleEnabled = false,
            PostExportHook = source.PostExportHook,
            NotifyOnComplete = source.NotifyOnComplete,
            IncludeDataDictionary = source.IncludeDataDictionary,
            IncludeLoaderScript = source.IncludeLoaderScript,
            OverwriteExisting = source.OverwriteExisting,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsBuiltIn = false
        };

        _presets.Add(duplicate);
        await SavePresetsAsync(cancellationToken);
        PresetsChanged?.Invoke(this, EventArgs.Empty);

        return duplicate;
    }

    /// <summary>
    /// Records that a preset was used for an export.
    /// </summary>
    public async Task RecordPresetUsageAsync(string presetId, CancellationToken cancellationToken = default)
    {
        var preset = _presets.FirstOrDefault(p => p.Id == presetId);
        if (preset != null)
        {
            preset.LastUsedAt = DateTime.UtcNow;
            preset.UseCount++;
            await SavePresetsAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Exports presets to a JSON file for sharing.
    /// </summary>
    public async Task<string> ExportPresetsAsync(string[] presetIds, string destinationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var presetsToExport = _presets.Where(p => presetIds.Contains(p.Id)).ToList();

        foreach (var preset in presetsToExport)
        {
            preset.Id = Guid.NewGuid().ToString();
            preset.IsBuiltIn = false;
            preset.UseCount = 0;
            preset.LastUsedAt = null;
        }

        var options = DesktopJsonOptions.PrettyPrint;
        var json = JsonSerializer.Serialize(presetsToExport, options);
        var filePath = Path.Combine(destinationPath, $"export_presets_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        return filePath;
    }

    /// <summary>
    /// Imports presets from a JSON file.
    /// </summary>
    public async Task<int> ImportPresetsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var importedPresets = JsonSerializer.Deserialize<List<ExportPreset>>(json);

        if (importedPresets == null || importedPresets.Count == 0)
            return 0;

        var importedCount = 0;
        foreach (var preset in importedPresets)
        {
            preset.Id = Guid.NewGuid().ToString();
            preset.IsBuiltIn = false;
            preset.CreatedAt = DateTime.UtcNow;
            preset.UpdatedAt = DateTime.UtcNow;

            var existingName = _presets.FirstOrDefault(p => p.Name == preset.Name);
            if (existingName != null)
                preset.Name = $"{preset.Name} (Imported)";

            _presets.Add(preset);
            importedCount++;
        }

        await SavePresetsAsync(cancellationToken);
        PresetsChanged?.Invoke(this, EventArgs.Empty);

        return importedCount;
    }


    private void EnsureBuiltInPresets()
    {
        foreach (var builtIn in GetBuiltInPresets())
        {
            if (!_presets.Any(p => p.Id == builtIn.Id))
            {
                _presets.Insert(0, builtIn);
            }
        }
    }

    private static string GetDefaultDestination(ExportPresetFormat format)
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Meridian",
            "Exports");

        return format switch
        {
            ExportPresetFormat.Lean => Path.Combine(basePath, "Lean", "{symbol}"),
            ExportPresetFormat.Xlsx => Path.Combine(basePath, "Excel"),
            ExportPresetFormat.Sql => Path.Combine(basePath, "SQL"),
            _ => Path.Combine(basePath, "{year}", "{month}")
        };
    }

    private static List<ExportPreset> GetBuiltInPresets()
    {
        return new List<ExportPreset>
        {
            new ExportPreset
            {
                Id = "python-pandas",
                Name = "Python/Pandas",
                Description = "Parquet format optimized for pandas.read_parquet(). Includes loader script and data dictionary.",
                Format = ExportPresetFormat.Parquet,
                Compression = ExportPresetCompression.Snappy,
                Destination = Path.Combine("{year}", "{month}"),
                FilenamePattern = "{symbol}_{date}.parquet",
                Filters = new ExportPresetFilters
                {
                    EventTypes = new[] { "Trade", "BboQuote" },
                    DateRangeType = DateRangeType.LastWeek
                },
                IncludeLoaderScript = true,
                IncludeDataDictionary = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new ExportPreset
            {
                Id = "r-stats",
                Name = "R Statistics",
                Description = "CSV format with proper NA handling and ISO date formats for R data.table or tidyverse.",
                Format = ExportPresetFormat.Csv,
                Compression = ExportPresetCompression.None,
                Destination = Path.Combine("{year}", "{month}"),
                FilenamePattern = "{symbol}_{type}_{date}.csv",
                Filters = new ExportPresetFilters
                {
                    EventTypes = new[] { "Trade", "BboQuote" },
                    DateRangeType = DateRangeType.LastWeek
                },
                IncludeLoaderScript = true,
                IncludeDataDictionary = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new ExportPreset
            {
                Id = "runmat",
                Name = "RunMat",
                Description = "Numeric CSV export with a MATLAB-style loader script for RunMat.",
                Format = ExportPresetFormat.Csv,
                Compression = ExportPresetCompression.None,
                Destination = Path.Combine("{year}", "{month}"),
                FilenamePattern = "{symbol}_{type}_{date}.csv",
                Filters = new ExportPresetFilters
                {
                    EventTypes = new[] { "Trade", "BboQuote" },
                    DateRangeType = DateRangeType.LastWeek
                },
                IncludeLoaderScript = true,
                IncludeDataDictionary = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new ExportPreset
            {
                Id = "quantconnect-lean",
                Name = "QuantConnect Lean",
                Description = "Native Lean data format with zip packaging for backtesting.",
                Format = ExportPresetFormat.Lean,
                Compression = ExportPresetCompression.Zip,
                Destination = Path.Combine("equity", "usa", "daily"),
                FilenamePattern = "{symbol}.zip",
                Filters = new ExportPresetFilters
                {
                    EventTypes = new[] { "HistoricalBar" },
                    DateRangeType = DateRangeType.All
                },
                IncludeLoaderScript = false,
                IncludeDataDictionary = false,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new ExportPreset
            {
                Id = "excel",
                Name = "Microsoft Excel",
                Description = "XLSX format with multiple sheets, optimized for Excel analysis.",
                Format = ExportPresetFormat.Xlsx,
                Compression = ExportPresetCompression.None,
                Destination = "{year}",
                FilenamePattern = "MarketData_{date}.xlsx",
                Filters = new ExportPresetFilters
                {
                    EventTypes = new[] { "Trade", "BboQuote" },
                    DateRangeType = DateRangeType.Yesterday
                },
                IncludeLoaderScript = false,
                IncludeDataDictionary = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new ExportPreset
            {
                Id = "postgresql",
                Name = "PostgreSQL/TimescaleDB",
                Description = "CSV with COPY command for fast database import. Includes DDL scripts.",
                Format = ExportPresetFormat.Csv,
                Compression = ExportPresetCompression.None,
                Destination = "database_import",
                FilenamePattern = "{symbol}_{type}_{date}.csv",
                Filters = new ExportPresetFilters
                {
                    EventTypes = new[] { "Trade", "BboQuote" },
                    DateRangeType = DateRangeType.LastWeek
                },
                IncludeLoaderScript = true,
                IncludeDataDictionary = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            }
        };
    }

}
