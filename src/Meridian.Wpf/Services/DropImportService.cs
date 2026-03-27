using System;
using System.IO;

namespace Meridian.Wpf.Services;

/// <summary>
/// Classifies files dropped onto the Meridian window so the UI can route
/// each file to the correct import page.
/// </summary>
public enum DropFileType
{
    /// <summary>CSV whose first header row contains OHLCV columns.</summary>
    CsvHistoricalData,
    /// <summary>CSV whose first header row looks like a symbol list.</summary>
    CsvSymbolList,
    /// <summary>JSON application-configuration file.</summary>
    JsonConfig,
    /// <summary>Newline-delimited JSON market-data stream.</summary>
    JsonlMarketData,
    /// <summary>Apache Parquet binary data file.</summary>
    ParquetData,
    /// <summary>Extension or content not recognised.</summary>
    Unknown
}

/// <summary>
/// Stateless helper that detects the <see cref="DropFileType"/> of a dropped file
/// and provides the destination page name for navigation.
/// No DI needed — call via <see cref="Instance"/> or static methods.
/// </summary>
public sealed class DropImportService
{
    private static readonly Lazy<DropImportService> _instance = new(() => new DropImportService());

    /// <summary>Gets the singleton instance.</summary>
    public static DropImportService Instance => _instance.Value;

    private DropImportService() { }

    /// <summary>
    /// Inspects a file path and returns the best-guess <see cref="DropFileType"/>.
    /// For CSV files the first line is peeked to distinguish historical bars from
    /// symbol lists.
    /// </summary>
    public DropFileType DetectFileType(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return DropFileType.Unknown;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        return ext switch
        {
            ".csv" => ClassifyCsv(filePath),
            ".json" => DropFileType.JsonConfig,
            ".jsonl" or ".ndjson" => DropFileType.JsonlMarketData,
            ".parquet" => DropFileType.ParquetData,
            _ => DropFileType.Unknown
        };
    }

    /// <summary>
    /// Returns a short human-readable label for the detected file type,
    /// shown in the drop-overlay subtitle.
    /// </summary>
    public string GetTypeFriendlyName(DropFileType type) => type switch
    {
        DropFileType.CsvHistoricalData => "Historical OHLCV data",
        DropFileType.CsvSymbolList     => "Symbol list",
        DropFileType.JsonConfig        => "Configuration file",
        DropFileType.JsonlMarketData   => "Market data (JSONL)",
        DropFileType.ParquetData       => "Parquet data file",
        _                              => "CSV · JSON · JSONL · Parquet"
    };

    /// <summary>
    /// Returns the NavigationService page key that should receive the dropped file.
    /// </summary>
    public string GetTargetPageKey(DropFileType type) => type switch
    {
        DropFileType.CsvHistoricalData => "DataBrowser",
        DropFileType.CsvSymbolList     => "PortfolioImport",
        DropFileType.JsonConfig        => "Settings",
        DropFileType.JsonlMarketData   => "DataBrowser",
        DropFileType.ParquetData       => "DataBrowser",
        _                              => "DataBrowser"
    };

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private static DropFileType ClassifyCsv(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            var header = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(header))
                return DropFileType.CsvSymbolList;

            var lower = header.ToLowerInvariant();

            // Historical bar heuristic: must have price/volume columns.
            if ((lower.Contains("open") || lower.Contains("high") || lower.Contains("low") || lower.Contains("close"))
                && (lower.Contains("date") || lower.Contains("time") || lower.Contains("timestamp")))
            {
                return DropFileType.CsvHistoricalData;
            }

            return DropFileType.CsvSymbolList;
        }
        catch
        {
            return DropFileType.CsvSymbolList;
        }
    }
}
