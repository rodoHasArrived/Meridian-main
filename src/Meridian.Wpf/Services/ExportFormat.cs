namespace Meridian.Wpf.Services;

/// <summary>
/// Supported export formats for sampling/alignment outputs.
/// </summary>
public enum ExportFormat : byte
{
    Csv,
    Parquet,
    Json,
    JsonLines,
    Excel,
    Hdf5,
    Feather
}
