namespace Meridian.Contracts.Export;

/// <summary>
/// Ready-to-use export preset templates for common research and backtesting workflows.
/// Each preset is pre-configured with the appropriate format, compression, column selection,
/// and validation settings for its target audience.
/// </summary>
public static class StandardPresets
{
    /// <summary>
    /// QuantConnect Lean native format.
    /// Outputs zip-compressed CSV files with the six standard OHLCV columns in the
    /// millisecond-epoch format expected by the Lean engine.
    /// </summary>
    public static ExportPreset QuantConnectFormat => new()
    {
        Id = "standard-quantconnect",
        Name = "QuantConnect Lean Format",
        Description = "Native Lean data format: zip-compressed CSV with OHLCV columns, millisecond timestamps.",
        Format = ExportPresetFormat.Lean,
        Compression = ExportPresetCompression.Zip,
        Columns = new[] { "datetime", "open", "high", "low", "close", "volume" },
        IncludeManifest = true,
        IncludeLoaderScript = false,
        IncludeDataDictionary = false,
        IsBuiltIn = true,
        Validation = new ExportValidationRules { WarnCsvComplexTypes = false }
    };

    /// <summary>
    /// Pandas DataFrame via Apache Parquet.
    /// Snappy-compressed Parquet file with UTC nanosecond timestamps, ready for
    /// <c>pd.read_parquet()</c> without any conversion.
    /// </summary>
    public static ExportPreset PandasDataFrame => new()
    {
        Id = "standard-pandas",
        Name = "Pandas DataFrame (Parquet)",
        Description = "Snappy-compressed Parquet optimised for pandas.read_parquet(), UTC nanosecond timestamps.",
        Format = ExportPresetFormat.Parquet,
        Compression = ExportPresetCompression.Snappy,
        IncludeManifest = true,
        IncludeLoaderScript = true,
        IncludeDataDictionary = true,
        IsBuiltIn = true
    };

    /// <summary>
    /// Jupyter / research notebook CSV export.
    /// Gzip-compressed CSV with the five most commonly used tick fields, suitable for
    /// quick ad-hoc analysis in Python or R without any additional dependencies.
    /// </summary>
    public static ExportPreset ResearchNotebook => new()
    {
        Id = "standard-research",
        Name = "Jupyter Notebook (CSV)",
        Description = "Gzip-compressed CSV with timestamp, price, volume, bid and ask columns for notebook exploration.",
        Format = ExportPresetFormat.Csv,
        Compression = ExportPresetCompression.Gzip,
        Columns = new[] { "timestamp", "price", "volume", "bid", "ask" },
        IncludeManifest = true,
        IncludeLoaderScript = true,
        IncludeDataDictionary = false,
        IsBuiltIn = true,
        Validation = new ExportValidationRules { WarnCsvComplexTypes = true }
    };

    /// <summary>
    /// Returns all built-in standard presets.
    /// </summary>
    public static IReadOnlyList<ExportPreset> GetAll() =>
        new[] { QuantConnectFormat, PandasDataFrame, ResearchNotebook };
}
