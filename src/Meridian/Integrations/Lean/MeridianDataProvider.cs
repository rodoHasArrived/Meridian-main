using System.IO.Compression;
using QuantConnect;
using QuantConnect.Interfaces;
using QuantConnect.Logging;
using QuantConnect.Util;

namespace Meridian.Integrations.Lean;

/// <summary>
/// Custom IDataProvider implementation that reads market data from Meridian's JSONL files.
/// Supports both compressed (.jsonl.gz) and uncompressed (.jsonl) files.
/// </summary>
public sealed class MeridianDataProvider : IDataProvider
{
    private readonly string _dataRoot;

    /// <summary>
    /// Event raised each time data fetch is finished (successfully or not).
    /// </summary>
    public event EventHandler<DataProviderNewDataRequestEventArgs>? NewDataRequest;

    /// <summary>
    /// Creates a new instance of the Meridian data provider.
    /// </summary>
    /// <param name="dataRoot">Root directory where Meridian stores JSONL files (defaults to ./data)</param>
    public MeridianDataProvider(string? dataRoot = null)
    {
        _dataRoot = dataRoot ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        Log.Trace("MeridianDataProvider initialized with data root: " + _dataRoot);
    }

    /// <summary>
    /// Fetches data from the Meridian JSONL storage.
    /// </summary>
    /// <param name="key">The file path to fetch</param>
    /// <returns>Stream containing the file data, or null if not found</returns>
    public Stream Fetch(string key)
    {
        var succeeded = false;
        try
        {
            // Check if the file exists directly
            if (File.Exists(key))
            {
                succeeded = true;
                return OpenFile(key);
            }

            // Try with .gz extension for compressed files
            var gzPath = key + ".gz";
            if (File.Exists(gzPath))
            {
                succeeded = true;
                return OpenFile(gzPath);
            }

            // Try alternative path construction
            var relativePath = key.Replace(_dataRoot, "").TrimStart(Path.DirectorySeparatorChar);
            var alternativePath = Path.Combine(_dataRoot, relativePath);

            if (File.Exists(alternativePath))
            {
                succeeded = true;
                return OpenFile(alternativePath);
            }

            // Try compressed alternative path
            var alternativeGzPath = alternativePath + ".gz";
            if (File.Exists(alternativeGzPath))
            {
                succeeded = true;
                return OpenFile(alternativeGzPath);
            }

            Log.Trace("MeridianDataProvider: File not found: " + key);
            return Stream.Null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MeridianDataProvider: Error fetching " + key);
            return Stream.Null;
        }
        finally
        {
            NewDataRequest?.Invoke(this, new DataProviderNewDataRequestEventArgs(key, succeeded, succeeded ? string.Empty : "File not found"));
        }
    }

    /// <summary>
    /// Opens a file and returns a stream, automatically decompressing if it's a .gz file.
    /// </summary>
    private Stream OpenFile(string filePath)
    {
        var fileStream = File.OpenRead(filePath);

        // If it's a gzip file, wrap in GZipStream
        if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            return new GZipStream(fileStream, CompressionMode.Decompress);
        }

        return fileStream;
    }

    /// <summary>
    /// Disposes of the data provider resources.
    /// </summary>
    public void Dispose()
    {
        // No resources to dispose
    }
}
