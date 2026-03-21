using System.Text.RegularExpressions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;

namespace Meridian.Storage.Policies;

/// <summary>
/// Storage policy that generates file paths based on configurable naming conventions.
/// Supports multiple directory structures and date partitioning strategies.
/// </summary>
public sealed class JsonlStoragePolicy : IStoragePolicy
{
    private readonly StorageOptions _options;
    private readonly ISourceRegistry? _sourceRegistry;

    public JsonlStoragePolicy(StorageOptions options, ISourceRegistry? sourceRegistry = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sourceRegistry = sourceRegistry;
    }

    /// <summary>
    /// Generates the file path for a market event based on configured naming convention.
    /// </summary>
    public string GetPath(MarketEvent evt)
    {
        var root = string.IsNullOrWhiteSpace(_options.RootPath) ? "data" : _options.RootPath;
        var symbol = Sanitize(evt.EffectiveSymbol);
        var type = evt.Type.ToString();
        var dateStr = FormatDate(evt.Timestamp.UtcDateTime);
        var ext = GetExtension();
        var prefix = string.IsNullOrWhiteSpace(_options.FilePrefix) ? "" : $"{_options.FilePrefix}_";
        var source = Sanitize(evt.Source);
        var assetClass = GetAssetClass(evt.EffectiveSymbol, evt.Type);

        // Build path based on naming convention
        return _options.NamingConvention switch
        {
            FileNamingConvention.Flat => BuildFlatPath(root, symbol, type, dateStr, prefix, ext, source),
            FileNamingConvention.BySymbol => BuildBySymbolPath(root, symbol, type, dateStr, prefix, ext),
            FileNamingConvention.ByDate => BuildByDatePath(root, symbol, type, dateStr, prefix, ext),
            FileNamingConvention.ByType => BuildByTypePath(root, symbol, type, dateStr, prefix, ext),
            FileNamingConvention.BySource => BuildBySourcePath(root, source, symbol, type, dateStr, prefix, ext),
            FileNamingConvention.ByAssetClass => BuildByAssetClassPath(root, assetClass, symbol, type, dateStr, prefix, ext),
            FileNamingConvention.Hierarchical => BuildHierarchicalPath(root, source, assetClass, symbol, type, dateStr, prefix, ext),
            FileNamingConvention.Canonical => BuildCanonicalPath(root, evt.Timestamp.UtcDateTime, source, symbol, type, prefix, ext),
            _ => BuildBySymbolPath(root, symbol, type, dateStr, prefix, ext)
        };
    }

    /// <summary>
    /// Gets a preview of the file path pattern for display purposes.
    /// </summary>
    public string GetPathPreview()
    {
        var root = string.IsNullOrWhiteSpace(_options.RootPath) ? "data" : _options.RootPath;
        var ext = GetExtension();
        var prefix = string.IsNullOrWhiteSpace(_options.FilePrefix) ? "" : $"{_options.FilePrefix}_";
        var dateExample = _options.DatePartition switch
        {
            DatePartition.None => "",
            DatePartition.Hourly => "2024-01-15_14",
            DatePartition.Monthly => "2024-01",
            _ => "2024-01-15"
        };

        return _options.NamingConvention switch
        {
            FileNamingConvention.Flat => string.IsNullOrEmpty(dateExample)
                ? $"{root}/{prefix}AAPL_Trade{ext}"
                : $"{root}/{prefix}AAPL_Trade_{dateExample}{ext}",
            FileNamingConvention.BySymbol => string.IsNullOrEmpty(dateExample)
                ? $"{root}/AAPL/Trade/{prefix}data{ext}"
                : $"{root}/AAPL/Trade/{prefix}{dateExample}{ext}",
            FileNamingConvention.ByDate => string.IsNullOrEmpty(dateExample)
                ? $"{root}/AAPL/{prefix}Trade{ext}"
                : $"{root}/{dateExample}/AAPL/{prefix}Trade{ext}",
            FileNamingConvention.ByType => string.IsNullOrEmpty(dateExample)
                ? $"{root}/Trade/AAPL/{prefix}data{ext}"
                : $"{root}/Trade/AAPL/{prefix}{dateExample}{ext}",
            FileNamingConvention.BySource => string.IsNullOrEmpty(dateExample)
                ? $"{root}/alpaca/AAPL/Trade/{prefix}data{ext}"
                : $"{root}/alpaca/AAPL/Trade/{prefix}{dateExample}{ext}",
            FileNamingConvention.ByAssetClass => string.IsNullOrEmpty(dateExample)
                ? $"{root}/equity/AAPL/Trade/{prefix}data{ext}"
                : $"{root}/equity/AAPL/Trade/{prefix}{dateExample}{ext}",
            FileNamingConvention.Hierarchical => string.IsNullOrEmpty(dateExample)
                ? $"{root}/alpaca/equity/AAPL/Trade/{prefix}data{ext}"
                : $"{root}/alpaca/equity/AAPL/Trade/{prefix}{dateExample}{ext}",
            FileNamingConvention.Canonical => $"{root}/2024/01/15/alpaca/AAPL/{prefix}Trade{ext}",
            _ => $"{root}/AAPL/Trade/{prefix}{dateExample}{ext}"
        };
    }

    private string GetExtension()
    {
        if (!_options.Compress)
            return ".jsonl";

        return _options.CompressionCodec switch
        {
            CompressionCodec.Gzip => ".jsonl.gz",
            CompressionCodec.Zstd => ".jsonl.zst",
            CompressionCodec.LZ4 => ".jsonl.lz4",
            CompressionCodec.Brotli => ".jsonl.br",
            _ => ".jsonl.gz"
        };
    }

    private string GetAssetClass(string symbol, MarketEventType eventType = MarketEventType.Unknown)
    {
        // Infer asset class from event type for derivatives events
        if (eventType is MarketEventType.OptionQuote
            or MarketEventType.OptionTrade
            or MarketEventType.OptionGreeks
            or MarketEventType.OptionChain
            or MarketEventType.OpenInterest)
        {
            return "option";
        }

        // Try to get asset class from source registry
        if (_sourceRegistry != null)
        {
            var symbolInfo = _sourceRegistry.GetSymbolInfo(symbol);
            if (symbolInfo?.AssetClass != null)
                return Sanitize(symbolInfo.AssetClass);
        }

        // Default to equity
        return "equity";
    }

    private string BuildFlatPath(string root, string symbol, string type, string dateStr, string prefix, string ext, string source)
    {
        // Flat: {root}/{prefix}{symbol}_{type}_{date}[_{source}].jsonl
        var sourceSegment = _options.IncludeProvider ? $"_{source}" : "";
        var fileName = string.IsNullOrEmpty(dateStr)
            ? $"{prefix}{symbol}_{type}{sourceSegment}{ext}"
            : $"{prefix}{symbol}_{type}_{dateStr}{sourceSegment}{ext}";
        return Path.Combine(root, fileName);
    }

    private string BuildBySymbolPath(string root, string symbol, string type, string dateStr, string prefix, string ext)
    {
        // BySymbol: {root}/{symbol}/{type}/{prefix}{date}.jsonl
        var fileName = string.IsNullOrEmpty(dateStr)
            ? $"{prefix}data{ext}"
            : $"{prefix}{dateStr}{ext}";
        return Path.Combine(root, symbol, type, fileName);
    }

    private string BuildByDatePath(string root, string symbol, string type, string dateStr, string prefix, string ext)
    {
        // ByDate: {root}/{date}/{symbol}/{prefix}{type}.jsonl
        if (string.IsNullOrEmpty(dateStr))
        {
            // No date partition - put directly under symbol
            return Path.Combine(root, symbol, $"{prefix}{type}{ext}");
        }
        return Path.Combine(root, dateStr, symbol, $"{prefix}{type}{ext}");
    }

    private string BuildByTypePath(string root, string symbol, string type, string dateStr, string prefix, string ext)
    {
        // ByType: {root}/{type}/{symbol}/{prefix}{date}.jsonl
        var fileName = string.IsNullOrEmpty(dateStr)
            ? $"{prefix}data{ext}"
            : $"{prefix}{dateStr}{ext}";
        return Path.Combine(root, type, symbol, fileName);
    }

    private string BuildBySourcePath(string root, string source, string symbol, string type, string dateStr, string prefix, string ext)
    {
        // BySource: {root}/{source}/{symbol}/{type}/{prefix}{date}.jsonl
        var fileName = string.IsNullOrEmpty(dateStr)
            ? $"{prefix}data{ext}"
            : $"{prefix}{dateStr}{ext}";
        return Path.Combine(root, source, symbol, type, fileName);
    }

    private string BuildByAssetClassPath(string root, string assetClass, string symbol, string type, string dateStr, string prefix, string ext)
    {
        // ByAssetClass: {root}/{asset_class}/{symbol}/{type}/{prefix}{date}.jsonl
        var fileName = string.IsNullOrEmpty(dateStr)
            ? $"{prefix}data{ext}"
            : $"{prefix}{dateStr}{ext}";
        return Path.Combine(root, assetClass, symbol, type, fileName);
    }

    private string BuildHierarchicalPath(string root, string source, string assetClass, string symbol, string type, string dateStr, string prefix, string ext)
    {
        // Hierarchical: {root}/{source}/{asset_class}/{symbol}/{type}/{prefix}{date}.jsonl
        var fileName = string.IsNullOrEmpty(dateStr)
            ? $"{prefix}data{ext}"
            : $"{prefix}{dateStr}{ext}";
        return Path.Combine(root, source, assetClass, symbol, type, fileName);
    }

    private string BuildCanonicalPath(string root, DateTime utc, string source, string symbol, string type, string prefix, string ext)
    {
        // Canonical: {root}/{year}/{month}/{day}/{source}/{symbol}/{prefix}{type}.jsonl
        var year = utc.Year.ToString("D4");
        var month = utc.Month.ToString("D2");
        var day = utc.Day.ToString("D2");
        return Path.Combine(root, year, month, day, source, symbol, $"{prefix}{type}{ext}");
    }

    private string FormatDate(DateTime utc)
    {
        return _options.DatePartition switch
        {
            DatePartition.None => "",
            DatePartition.Hourly => utc.ToString("yyyy-MM-dd_HH"),
            DatePartition.Monthly => utc.ToString("yyyy-MM"),
            DatePartition.Daily => utc.ToString("yyyy-MM-dd"),
            _ => utc.ToString("yyyy-MM-dd")
        };
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "_unknown";
        Span<char> buf = stackalloc char[s.Length];
        int j = 0;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '.')
                buf[j++] = ch;
            else
                buf[j++] = '_';
        }
        return new string(buf[..j]);
    }

    /// <summary>
    /// Attempts to parse metadata from a file path based on the configured naming convention.
    /// This is the inverse of GetPath() and provides a centralized parser for storage search/indexing.
    /// </summary>
    /// <param name="filePath">The full path to the data file.</param>
    /// <returns>Parsed metadata, or null if the path cannot be parsed.</returns>
    public ParsedPathMetadata? TryParsePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var root = string.IsNullOrWhiteSpace(_options.RootPath) ? "data" : _options.RootPath;
        var relativePath = filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? filePath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : filePath;

        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileName = Path.GetFileNameWithoutExtension(parts[^1]);
        // Strip compression extensions
        fileName = StripExtensions(fileName);

        return _options.NamingConvention switch
        {
            FileNamingConvention.Flat => ParseFlatPath(fileName, parts),
            FileNamingConvention.BySymbol => ParseBySymbolPath(parts, fileName),
            FileNamingConvention.ByDate => ParseByDatePath(parts, fileName),
            FileNamingConvention.ByType => ParseByTypePath(parts, fileName),
            FileNamingConvention.BySource => ParseBySourcePath(parts, fileName),
            FileNamingConvention.ByAssetClass => ParseByAssetClassPath(parts, fileName),
            FileNamingConvention.Hierarchical => ParseHierarchicalPath(parts, fileName),
            FileNamingConvention.Canonical => ParseCanonicalPath(parts, fileName),
            _ => ParseFallback(filePath, parts, fileName)
        };
    }

    private static string StripExtensions(string fileName)
    {
        // Strip known compression extensions
        foreach (var ext in new[] { ".gz", ".gzip", ".zst", ".lz4", ".br", ".jsonl", ".parquet" })
        {
            while (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName[..^ext.Length];
            }
        }
        return fileName;
    }

    // Flat: {root}/{prefix}{symbol}_{type}_{date}[_{source}].ext
    private ParsedPathMetadata? ParseFlatPath(string fileName, string[] parts)
    {
        var prefix = string.IsNullOrWhiteSpace(_options.FilePrefix) ? "" : $"{_options.FilePrefix}_";
        if (!string.IsNullOrEmpty(prefix) && fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            fileName = fileName[prefix.Length..];

        var segments = fileName.Split('_');
        if (segments.Length < 2)
            return null;

        var symbol = segments[0];
        var eventType = segments.Length > 1 ? segments[1] : "Unknown";
        var date = TryExtractDate(segments.Length > 2 ? segments[2] : null);
        var source = _options.IncludeProvider && segments.Length > 3 ? segments[^1] : "Unknown";

        return new ParsedPathMetadata(symbol, eventType, source, date);
    }

    // BySymbol: {root}/{symbol}/{type}/{prefix}{date}.ext
    private ParsedPathMetadata? ParseBySymbolPath(string[] parts, string fileName)
    {
        if (parts.Length < 2)
            return null;

        var symbol = parts[0];
        var eventType = parts.Length > 1 ? parts[1] : "Unknown";
        var dateStr = StripPrefix(fileName);
        var date = TryExtractDate(dateStr);

        return new ParsedPathMetadata(symbol, eventType, "Unknown", date);
    }

    // ByDate: {root}/{date}/{symbol}/{prefix}{type}.ext
    private ParsedPathMetadata? ParseByDatePath(string[] parts, string fileName)
    {
        if (parts.Length < 2)
            return null;

        var dateStr = parts[0];
        var symbol = parts.Length > 1 ? parts[1] : "Unknown";
        var eventType = StripPrefix(fileName);
        var date = TryExtractDate(dateStr);

        return new ParsedPathMetadata(symbol, eventType, "Unknown", date);
    }

    // ByType: {root}/{type}/{symbol}/{prefix}{date}.ext
    private ParsedPathMetadata? ParseByTypePath(string[] parts, string fileName)
    {
        if (parts.Length < 2)
            return null;

        var eventType = parts[0];
        var symbol = parts.Length > 1 ? parts[1] : "Unknown";
        var dateStr = StripPrefix(fileName);
        var date = TryExtractDate(dateStr);

        return new ParsedPathMetadata(symbol, eventType, "Unknown", date);
    }

    // BySource: {root}/{source}/{symbol}/{type}/{prefix}{date}.ext
    private ParsedPathMetadata? ParseBySourcePath(string[] parts, string fileName)
    {
        if (parts.Length < 3)
            return null;

        var source = parts[0];
        var symbol = parts[1];
        var eventType = parts.Length > 2 ? parts[2] : "Unknown";
        var dateStr = StripPrefix(fileName);
        var date = TryExtractDate(dateStr);

        return new ParsedPathMetadata(symbol, eventType, source, date);
    }

    // ByAssetClass: {root}/{asset_class}/{symbol}/{type}/{prefix}{date}.ext
    private ParsedPathMetadata? ParseByAssetClassPath(string[] parts, string fileName)
    {
        if (parts.Length < 3)
            return null;

        // Asset class is parts[0], not needed for metadata
        var symbol = parts[1];
        var eventType = parts.Length > 2 ? parts[2] : "Unknown";
        var dateStr = StripPrefix(fileName);
        var date = TryExtractDate(dateStr);

        return new ParsedPathMetadata(symbol, eventType, "Unknown", date);
    }

    // Hierarchical: {root}/{source}/{asset_class}/{symbol}/{type}/{prefix}{date}.ext
    private ParsedPathMetadata? ParseHierarchicalPath(string[] parts, string fileName)
    {
        if (parts.Length < 4)
            return null;

        var source = parts[0];
        // Asset class is parts[1], not needed for metadata
        var symbol = parts[2];
        var eventType = parts.Length > 3 ? parts[3] : "Unknown";
        var dateStr = StripPrefix(fileName);
        var date = TryExtractDate(dateStr);

        return new ParsedPathMetadata(symbol, eventType, source, date);
    }

    // Canonical: {root}/{year}/{month}/{day}/{source}/{symbol}/{prefix}{type}.ext
    private ParsedPathMetadata? ParseCanonicalPath(string[] parts, string fileName)
    {
        if (parts.Length < 5)
            return null;

        var year = parts[0];
        var month = parts[1];
        var day = parts[2];
        var source = parts[3];
        var symbol = parts.Length > 4 ? parts[4] : "Unknown";
        var eventType = StripPrefix(fileName);

        DateTimeOffset? date = null;
        if (int.TryParse(year, out var y) && int.TryParse(month, out var m) && int.TryParse(day, out var d))
        {
            try
            { date = new DateTimeOffset(new DateTime(y, m, d), TimeSpan.Zero); }
            catch (ArgumentOutOfRangeException) { /* invalid date components */ }
        }

        return new ParsedPathMetadata(symbol, eventType, source, date);
    }

    // Fallback parser using heuristics
    private ParsedPathMetadata? ParseFallback(string filePath, string[] parts, string fileName)
    {
        string? symbol = null;
        string? eventType = null;
        string? source = null;
        DateTimeOffset? date = null;

        // Try to extract from path segments using common patterns
        foreach (var part in parts)
        {
            // Symbol pattern (1-5 uppercase letters)
            if (symbol == null && Regex.IsMatch(part, @"^[A-Z]{1,5}$"))
                symbol = part;

            // Event type pattern
            if (eventType == null && Enum.TryParse<MarketEventType>(part, true, out _))
                eventType = part;

            // Date pattern (yyyy-MM-dd)
            if (date == null && Regex.IsMatch(part, @"^\d{4}-\d{2}-\d{2}"))
                date = TryExtractDate(part);

            // Known source names
            if (source == null)
            {
                var lowered = part.ToLowerInvariant();
                if (new[] { "alpaca", "ib", "interactivebrokers", "polygon", "stooq", "yahoo", "tiingo", "finnhub" }.Contains(lowered))
                    source = part;
            }
        }

        // Also try to extract from filename
        var fileSegments = fileName.Split('_');
        foreach (var seg in fileSegments)
        {
            if (symbol == null && Regex.IsMatch(seg, @"^[A-Z]{1,5}$"))
                symbol = seg;
            if (eventType == null && Enum.TryParse<MarketEventType>(seg, true, out _))
                eventType = seg;
            if (date == null && Regex.IsMatch(seg, @"^\d{4}-\d{2}-\d{2}"))
                date = TryExtractDate(seg);
        }

        return new ParsedPathMetadata(
            symbol ?? "Unknown",
            eventType ?? "Unknown",
            source ?? "Unknown",
            date);
    }

    private string StripPrefix(string fileName)
    {
        var prefix = string.IsNullOrWhiteSpace(_options.FilePrefix) ? "" : $"{_options.FilePrefix}_";
        if (!string.IsNullOrEmpty(prefix) && fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return fileName[prefix.Length..];
        return fileName;
    }

    private static DateTimeOffset? TryExtractDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        // Try various date formats
        var formats = new[]
        {
            "yyyy-MM-dd",
            "yyyy-MM-dd_HH",
            "yyyy-MM",
            "yyyyMMdd",
            "yyyyMMdd_HH"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format, null, System.Globalization.DateTimeStyles.None, out var dt))
                return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        // Try generic parse as last resort
        if (DateTime.TryParse(dateStr[..Math.Min(10, dateStr.Length)], out var genericDt))
            return new DateTimeOffset(genericDt, TimeSpan.Zero);

        return null;
    }
}

/// <summary>
/// Metadata extracted from a storage file path.
/// </summary>
public sealed record ParsedPathMetadata(
    string Symbol,
    string EventType,
    string Source,
    DateTimeOffset? Date);
