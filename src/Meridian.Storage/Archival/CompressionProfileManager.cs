using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using Meridian.Application.Logging;
using Serilog;
using ZstdSharp;

namespace Meridian.Storage.Archival;

/// <summary>
/// Manages compression profiles for different archival use cases.
/// Supports LZ4, ZSTD, and GZIP codecs with configurable levels.
/// </summary>
public sealed class CompressionProfileManager
{
    private readonly ILogger _log = LoggingSetup.ForContext<CompressionProfileManager>();
    private readonly Dictionary<string, CompressionProfile> _profiles;
    private readonly Dictionary<string, CompressionProfile> _symbolOverrides;

    public CompressionProfileManager()
    {
        _profiles = GetBuiltInProfiles().ToDictionary(p => p.Id, p => p);
        _symbolOverrides = new Dictionary<string, CompressionProfile>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get the appropriate compression profile for a given context.
    /// </summary>
    public CompressionProfile GetProfileForContext(CompressionContext context)
    {
        // Check for symbol-specific overrides first
        if (!string.IsNullOrEmpty(context.Symbol) &&
            _symbolOverrides.TryGetValue(context.Symbol, out var symbolProfile))
        {
            return symbolProfile;
        }

        // Check for tier-specific profile
        var tierProfileId = context.StorageTier switch
        {
            StorageTier.Hot => "real-time-collection",
            StorageTier.Warm => "warm-archive",
            StorageTier.Cold => "cold-archive",
            _ => "warm-archive"
        };

        if (_profiles.TryGetValue(tierProfileId, out var tierProfile))
        {
            return tierProfile;
        }

        // Default to warm archive
        return _profiles["warm-archive"];
    }

    /// <summary>
    /// Register a symbol-specific compression override.
    /// </summary>
    public void SetSymbolOverride(string symbol, CompressionProfile profile)
    {
        _symbolOverrides[symbol] = profile;
        _log.Information("Set compression override for {Symbol}: {ProfileId}", symbol, profile.Id);
    }

    /// <summary>
    /// Register a custom compression profile.
    /// </summary>
    public void RegisterProfile(CompressionProfile profile)
    {
        _profiles[profile.Id] = profile;
        _log.Information("Registered compression profile: {ProfileId}", profile.Id);
    }

    /// <summary>
    /// Get all available profiles.
    /// </summary>
    public IReadOnlyList<CompressionProfile> GetAllProfiles() => _profiles.Values.ToList();

    /// <summary>
    /// Compress data using the specified profile.
    /// </summary>
    public async Task<CompressionResult> CompressAsync(
        Stream input,
        Stream output,
        CompressionProfile profile,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var inputSize = input.Length;

        Stream compressionStream = profile.Codec switch
        {
            CompressionCodec.Gzip => new GZipStream(output, GetGzipLevel(profile.Level), leaveOpen: true),
            CompressionCodec.Deflate => new DeflateStream(output, GetDeflateLevel(profile.Level), leaveOpen: true),
            CompressionCodec.Brotli => new BrotliStream(output, GetBrotliLevel(profile.Level), leaveOpen: true),
            CompressionCodec.None => output,
            CompressionCodec.Lz4 => LZ4Stream.Encode(output, GetLz4Level(profile.Level), leaveOpen: true),
            CompressionCodec.Zstd => new CompressionStream(output, profile.Level, leaveOpen: true),
            _ => throw new NotSupportedException($"Codec {profile.Codec} is not supported")
        };

        await using (compressionStream)
        {
            await input.CopyToAsync(compressionStream, ct);
        }

        var outputSize = output.Length;
        var duration = DateTime.UtcNow - startTime;

        return new CompressionResult
        {
            InputBytes = inputSize,
            OutputBytes = outputSize,
            CompressionRatio = inputSize > 0 ? (double)inputSize / outputSize : 1.0,
            Duration = duration,
            ThroughputMbps = inputSize / (1024.0 * 1024.0) / duration.TotalSeconds,
            Profile = profile
        };
    }

    /// <summary>
    /// Decompress data.
    /// </summary>
    public async Task DecompressAsync(
        Stream input,
        Stream output,
        CompressionCodec codec,
        CancellationToken ct = default)
    {
        Stream decompressionStream = codec switch
        {
            CompressionCodec.Gzip => new GZipStream(input, CompressionMode.Decompress, leaveOpen: true),
            CompressionCodec.Deflate => new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true),
            CompressionCodec.Brotli => new BrotliStream(input, CompressionMode.Decompress, leaveOpen: true),
            CompressionCodec.None => input,
            CompressionCodec.Lz4 => LZ4Stream.Decode(input, leaveOpen: true),
            CompressionCodec.Zstd => new DecompressionStream(input, leaveOpen: true),
            _ => throw new NotSupportedException($"Codec {codec} is not supported")
        };

        await using (decompressionStream)
        {
            await decompressionStream.CopyToAsync(output, ct);
        }
    }

    /// <summary>
    /// Benchmark compression profiles on sample data.
    /// </summary>
    public async Task<IReadOnlyList<CompressionBenchmarkResult>> BenchmarkAsync(
        byte[] sampleData,
        CancellationToken ct = default)
    {
        var results = new List<CompressionBenchmarkResult>();

        foreach (var profile in _profiles.Values)
        {
            try
            {
                using var input = new MemoryStream(sampleData);
                using var output = new MemoryStream();

                var compressionResult = await CompressAsync(input, output, profile, ct);

                // Benchmark decompression
                output.Position = 0;
                using var decompressedOutput = new MemoryStream();
                var decompressStart = DateTime.UtcNow;
                await DecompressAsync(output, decompressedOutput, profile.Codec, ct);
                var decompressDuration = DateTime.UtcNow - decompressStart;

                results.Add(new CompressionBenchmarkResult
                {
                    ProfileId = profile.Id,
                    ProfileName = profile.Name,
                    InputBytes = sampleData.Length,
                    OutputBytes = compressionResult.OutputBytes,
                    CompressionRatio = compressionResult.CompressionRatio,
                    CompressionDuration = compressionResult.Duration,
                    DecompressionDuration = decompressDuration,
                    CompressionThroughputMbps = compressionResult.ThroughputMbps,
                    DecompressionThroughputMbps = sampleData.Length / (1024.0 * 1024.0) / decompressDuration.TotalSeconds
                });
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Benchmark failed for profile {ProfileId}", profile.Id);
            }
        }

        return results.OrderByDescending(r => r.CompressionRatio).ToList();
    }

    private static CompressionLevel GetGzipLevel(int level) => level switch
    {
        <= 1 => CompressionLevel.Fastest,
        >= 9 => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal
    };

    private static CompressionLevel GetDeflateLevel(int level) => GetGzipLevel(level);

    private static CompressionLevel GetBrotliLevel(int level) => level switch
    {
        <= 3 => CompressionLevel.Fastest,
        >= 9 => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal
    };

    private static LZ4Level GetLz4Level(int level) => level switch
    {
        <= 0 => LZ4Level.L00_FAST,
        <= 3 => LZ4Level.L03_HC,
        <= 9 => LZ4Level.L09_HC,
        _ => LZ4Level.L12_MAX
    };

    /// <summary>
    /// Get built-in compression profiles.
    /// </summary>
    public static IReadOnlyList<CompressionProfile> GetBuiltInProfiles() => new[]
    {
        new CompressionProfile
        {
            Id = "real-time-collection",
            Name = "Real-Time Collection",
            Description = "Fastest compression for live data collection with minimal latency",
            Codec = CompressionCodec.Lz4,
            Level = 1,
            Priority = CompressionPriority.Speed,
            RecommendedTier = StorageTier.Hot,
            ExpectedRatio = 2.5,
            ExpectedThroughputMbps = 500
        },
        new CompressionProfile
        {
            Id = "warm-archive",
            Name = "Warm Archive",
            Description = "Balanced compression for frequently accessed archived data",
            Codec = CompressionCodec.Zstd,
            Level = 6,
            Priority = CompressionPriority.Balanced,
            RecommendedTier = StorageTier.Warm,
            ExpectedRatio = 5.0,
            ExpectedThroughputMbps = 150
        },
        new CompressionProfile
        {
            Id = "cold-archive",
            Name = "Cold Archive",
            Description = "Maximum compression for long-term cold storage",
            Codec = CompressionCodec.Zstd,
            Level = 19,
            Priority = CompressionPriority.Size,
            RecommendedTier = StorageTier.Cold,
            ExpectedRatio = 10.0,
            ExpectedThroughputMbps = 20
        },
        new CompressionProfile
        {
            Id = "high-volume-symbols",
            Name = "High-Volume Symbols",
            Description = "Fast compression for high-frequency symbols like SPY, QQQ",
            Codec = CompressionCodec.Zstd,
            Level = 3,
            Priority = CompressionPriority.Speed,
            RecommendedTier = StorageTier.Hot,
            ExpectedRatio = 3.5,
            ExpectedThroughputMbps = 300,
            ApplicableSymbols = new[] { "SPY", "QQQ", "AAPL", "MSFT", "TSLA", "NVDA", "AMD" }
        },
        new CompressionProfile
        {
            Id = "portable-export",
            Name = "Portable Export",
            Description = "Standard gzip for maximum compatibility when sharing data",
            Codec = CompressionCodec.Gzip,
            Level = 6,
            Priority = CompressionPriority.Balanced,
            RecommendedTier = StorageTier.Warm,
            ExpectedRatio = 6.0,
            ExpectedThroughputMbps = 100
        },
        new CompressionProfile
        {
            Id = "no-compression",
            Name = "No Compression",
            Description = "Uncompressed data for debugging or fast access",
            Codec = CompressionCodec.None,
            Level = 0,
            Priority = CompressionPriority.Speed,
            RecommendedTier = StorageTier.Hot,
            ExpectedRatio = 1.0,
            ExpectedThroughputMbps = 1000
        }
    };
}

/// <summary>
/// Compression profile configuration.
/// </summary>
public sealed class CompressionProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("codec")]
    public CompressionCodec Codec { get; set; } = CompressionCodec.Zstd;

    [JsonPropertyName("level")]
    public int Level { get; set; } = 6;

    [JsonPropertyName("priority")]
    public CompressionPriority Priority { get; set; } = CompressionPriority.Balanced;

    [JsonPropertyName("recommendedTier")]
    public StorageTier RecommendedTier { get; set; } = StorageTier.Warm;

    [JsonPropertyName("expectedRatio")]
    public double ExpectedRatio { get; set; } = 5.0;

    [JsonPropertyName("expectedThroughputMbps")]
    public double ExpectedThroughputMbps { get; set; } = 100;

    [JsonPropertyName("applicableSymbols")]
    public string[]? ApplicableSymbols { get; set; }

    /// <summary>
    /// Get file extension for this compression codec.
    /// </summary>
    public string GetFileExtension() => Codec switch
    {
        CompressionCodec.Gzip => ".gz",
        CompressionCodec.Zstd => ".zst",
        CompressionCodec.Lz4 => ".lz4",
        CompressionCodec.Brotli => ".br",
        CompressionCodec.Deflate => ".deflate",
        CompressionCodec.None => "",
        _ => ""
    };
}

/// <summary>
/// Supported compression codecs.
/// </summary>
public enum CompressionCodec : byte
{
    /// <summary>No compression.</summary>
    None = 0,
    /// <summary>Standard gzip - widely compatible.</summary>
    Gzip = 1,
    /// <summary>Deflate compression.</summary>
    Deflate = 2,
    /// <summary>LZ4 - extremely fast compression/decompression.</summary>
    Lz4 = 3,
    /// <summary>Zstandard - excellent balance of speed and ratio.</summary>
    Zstd = 4,
    /// <summary>Brotli - good for web/text content.</summary>
    Brotli = 5
}

/// <summary>
/// Compression priority preference.
/// </summary>
public enum CompressionPriority : byte
{
    /// <summary>Prioritize compression speed.</summary>
    Speed,
    /// <summary>Balance between speed and size.</summary>
    Balanced,
    /// <summary>Prioritize smallest file size.</summary>
    Size
}

/// <summary>
/// Storage tier classification.
/// </summary>
public enum StorageTier : byte
{
    /// <summary>Hot storage - frequently accessed, fast retrieval.</summary>
    Hot,
    /// <summary>Warm storage - occasional access, moderate retrieval speed.</summary>
    Warm,
    /// <summary>Cold storage - rare access, slow retrieval acceptable.</summary>
    Cold
}

/// <summary>
/// Context for selecting compression profile.
/// </summary>
public sealed class CompressionContext
{
    public string? Symbol { get; set; }
    public StorageTier StorageTier { get; set; } = StorageTier.Warm;
    public string? EventType { get; set; }
    public bool IsExport { get; set; }
    public long EstimatedBytes { get; set; }
}

/// <summary>
/// Result of a compression operation.
/// </summary>
public sealed class CompressionResult
{
    public long InputBytes { get; set; }
    public long OutputBytes { get; set; }
    public double CompressionRatio { get; set; }
    public TimeSpan Duration { get; set; }
    public double ThroughputMbps { get; set; }
    public CompressionProfile Profile { get; set; } = null!;
}

/// <summary>
/// Result of compression benchmark.
/// </summary>
public sealed class CompressionBenchmarkResult
{
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public long InputBytes { get; set; }
    public long OutputBytes { get; set; }
    public double CompressionRatio { get; set; }
    public TimeSpan CompressionDuration { get; set; }
    public TimeSpan DecompressionDuration { get; set; }
    public double CompressionThroughputMbps { get; set; }
    public double DecompressionThroughputMbps { get; set; }

    public override string ToString() =>
        $"{ProfileName}: {CompressionRatio:F2}x ratio, " +
        $"compress {CompressionThroughputMbps:F1} MB/s, " +
        $"decompress {DecompressionThroughputMbps:F1} MB/s";
}
