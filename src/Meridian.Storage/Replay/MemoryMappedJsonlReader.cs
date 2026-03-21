using System.Buffers;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Meridian.Application.Serialization;
using Meridian.Domain.Events;

namespace Meridian.Storage.Replay;

/// <summary>
/// Configuration options for memory-mapped file reading.
/// </summary>
public sealed class MemoryMappedReaderOptions
{
    /// <summary>
    /// Size of chunks to process at a time (in bytes).
    /// Default is 4MB for optimal memory/performance balance.
    /// </summary>
    public int ChunkSize { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// Number of events to batch before yielding.
    /// Default is 1000 events.
    /// </summary>
    public int BatchSize { get; init; } = 1000;

    /// <summary>
    /// Minimum file size to use memory mapping (smaller files use regular streaming).
    /// Default is 1MB.
    /// </summary>
    public long MinFileSizeForMapping { get; init; } = 1024 * 1024;

    /// <summary>
    /// Whether to use parallel deserialization for large batches.
    /// Default is true.
    /// </summary>
    public bool UseParallelDeserialization { get; init; } = true;

    /// <summary>
    /// Threshold for parallel deserialization (number of lines).
    /// Default is 100.
    /// </summary>
    public int ParallelDeserializationThreshold { get; init; } = 100;

    /// <summary>
    /// Default options.
    /// </summary>
    public static MemoryMappedReaderOptions Default => new();

    /// <summary>
    /// High throughput options with larger chunks.
    /// </summary>
    public static MemoryMappedReaderOptions HighThroughput => new()
    {
        ChunkSize = 16 * 1024 * 1024,
        BatchSize = 5000,
        ParallelDeserializationThreshold = 200
    };

    /// <summary>
    /// Low memory options with smaller chunks.
    /// </summary>
    public static MemoryMappedReaderOptions LowMemory => new()
    {
        ChunkSize = 1024 * 1024,
        BatchSize = 500,
        ParallelDeserializationThreshold = 50
    };
}

/// <summary>
/// High-performance JSONL reader using memory-mapped files for large file reading.
/// Provides significantly faster reads for large files by mapping directly into virtual memory.
/// Falls back to streaming for compressed files and small files.
/// </summary>
public sealed class MemoryMappedJsonlReader
{
    private readonly string _root;
    private readonly MemoryMappedReaderOptions _options;

    // Metrics
    private long _filesRead;
    private long _bytesRead;
    private long _eventsRead;
    private long _memoryMappedFilesUsed;

    /// <summary>
    /// Total files read.
    /// </summary>
    public long FilesRead => Interlocked.Read(ref _filesRead);

    /// <summary>
    /// Total bytes read.
    /// </summary>
    public long BytesRead => Interlocked.Read(ref _bytesRead);

    /// <summary>
    /// Total events successfully deserialized.
    /// </summary>
    public long EventsRead => Interlocked.Read(ref _eventsRead);

    /// <summary>
    /// Number of files read using memory mapping.
    /// </summary>
    public long MemoryMappedFilesUsed => Interlocked.Read(ref _memoryMappedFilesUsed);

    /// <summary>
    /// Creates a new MemoryMappedJsonlReader with default options.
    /// </summary>
    public MemoryMappedJsonlReader(string root)
        : this(root, MemoryMappedReaderOptions.Default)
    {
    }

    /// <summary>
    /// Creates a new MemoryMappedJsonlReader with custom options.
    /// </summary>
    public MemoryMappedJsonlReader(string root, MemoryMappedReaderOptions options)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Reads all events from JSONL files in the root directory.
    /// Uses memory-mapped files for large uncompressed files.
    /// </summary>
    public async IAsyncEnumerable<MarketEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(_root))
            yield break;

        var files = Directory.EnumerateFiles(_root, "*.jsonl*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            await foreach (var evt in ReadFileAsync(file, ct))
            {
                yield return evt;
            }
        }
    }

    /// <summary>
    /// Reads events from a single file using the most efficient method.
    /// </summary>
    public async IAsyncEnumerable<MarketEvent> ReadFileAsync(string file, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Interlocked.Increment(ref _filesRead);

        var isCompressed = file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                          file.EndsWith(".gzip", StringComparison.OrdinalIgnoreCase);

        var fileInfo = new FileInfo(file);
        var useMemoryMapping = !isCompressed && fileInfo.Length >= _options.MinFileSizeForMapping;

        if (useMemoryMapping)
        {
            await foreach (var evt in ReadFileMemoryMappedAsync(file, fileInfo.Length, ct))
            {
                yield return evt;
            }
        }
        else
        {
            await foreach (var evt in ReadFileStreamingAsync(file, isCompressed, ct))
            {
                yield return evt;
            }
        }
    }

    /// <summary>
    /// Reads events from JSONL files in batches for better memory efficiency.
    /// </summary>
    public async IAsyncEnumerable<IReadOnlyList<MarketEvent>> ReadEventsBatchedAsync(
        int batchSize = 1000,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var batch = new List<MarketEvent>(batchSize);

        await foreach (var evt in ReadEventsAsync(ct))
        {
            batch.Add(evt);
            if (batch.Count >= batchSize)
            {
                yield return batch.ToList();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    /// <summary>
    /// Reads a file using memory-mapped I/O for maximum performance on large files.
    /// </summary>
    private async IAsyncEnumerable<MarketEvent> ReadFileMemoryMappedAsync(
        string file,
        long fileSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        Interlocked.Increment(ref _memoryMappedFilesUsed);

        using var mmf = MemoryMappedFile.CreateFromFile(file, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);

        long position = 0;
        var pendingLines = new List<string>(_options.BatchSize);
        var lineBuilder = new StringBuilder(4096);
        var buffer = ArrayPool<byte>.Shared.Rent(_options.ChunkSize);

        try
        {
            while (position < fileSize)
            {
                ct.ThrowIfCancellationRequested();

                var chunkSize = (int)Math.Min(_options.ChunkSize, fileSize - position);

                using var accessor = mmf.CreateViewAccessor(position, chunkSize, MemoryMappedFileAccess.Read);

                // Read bytes from the memory-mapped view
                accessor.ReadArray(0, buffer, 0, chunkSize);
                Interlocked.Add(ref _bytesRead, chunkSize);

                // Process the chunk into lines
                var lineStart = 0;

                for (int i = 0; i < chunkSize; i++)
                {
                    if (buffer[i] == '\n')
                    {
                        // Complete line found
                        var lineLength = i - lineStart;
                        var lineText = Encoding.UTF8.GetString(buffer, lineStart, lineLength).TrimEnd('\r');

                        if (lineBuilder.Length > 0)
                        {
                            lineBuilder.Append(lineText);
                            pendingLines.Add(lineBuilder.ToString());
                            lineBuilder.Clear();
                        }
                        else if (!string.IsNullOrWhiteSpace(lineText))
                        {
                            pendingLines.Add(lineText);
                        }

                        lineStart = i + 1;

                        // Yield events when batch is full
                        if (pendingLines.Count >= _options.BatchSize)
                        {
                            foreach (var evt in DeserializeLines(pendingLines))
                            {
                                yield return evt;
                            }
                            pendingLines.Clear();
                            ct.ThrowIfCancellationRequested();
                        }
                    }
                }

                // Handle partial line at end of chunk
                if (lineStart < chunkSize)
                {
                    var remainingLength = chunkSize - lineStart;
                    lineBuilder.Append(Encoding.UTF8.GetString(buffer, lineStart, remainingLength));
                }

                position += chunkSize;
            }

            // Handle final partial line
            if (lineBuilder.Length > 0)
            {
                var finalLine = lineBuilder.ToString().TrimEnd('\r');
                if (!string.IsNullOrWhiteSpace(finalLine))
                {
                    pendingLines.Add(finalLine);
                }
            }

            // Yield remaining events
            if (pendingLines.Count > 0)
            {
                foreach (var evt in DeserializeLines(pendingLines))
                {
                    yield return evt;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await Task.CompletedTask; // Ensure async signature
    }

    /// <summary>
    /// Reads a file using standard streaming for compressed or small files.
    /// </summary>
    private async IAsyncEnumerable<MarketEvent> ReadFileStreamingAsync(
        string file,
        bool isCompressed,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        Stream stream = fs;

        if (isCompressed)
        {
            stream = new GZipStream(fs, CompressionMode.Decompress);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

        var pendingLines = new List<string>(_options.BatchSize);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            Interlocked.Add(ref _bytesRead, Encoding.UTF8.GetByteCount(line));
            pendingLines.Add(line);

            if (pendingLines.Count >= _options.BatchSize)
            {
                foreach (var evt in DeserializeLines(pendingLines))
                {
                    yield return evt;
                }
                pendingLines.Clear();
            }
        }

        // Yield remaining events
        if (pendingLines.Count > 0)
        {
            foreach (var evt in DeserializeLines(pendingLines))
            {
                yield return evt;
            }
        }
    }

    /// <summary>
    /// Deserializes a batch of JSON lines into MarketEvents.
    /// Uses parallel deserialization for larger batches.
    /// </summary>
    private IEnumerable<MarketEvent> DeserializeLines(List<string> lines)
    {
        if (lines.Count == 0)
            yield break;

        if (_options.UseParallelDeserialization && lines.Count >= _options.ParallelDeserializationThreshold)
        {
            // Parallel deserialization for larger batches
            var results = new MarketEvent?[lines.Count];

            Parallel.For(0, lines.Count, i =>
            {
                try
                {
                    results[i] = JsonSerializer.Deserialize<MarketEvent>(lines[i], MarketDataJsonContext.HighPerformanceOptions);
                }
                catch
                {
                    results[i] = null;
                }
            });

            foreach (var evt in results)
            {
                if (evt != null)
                {
                    Interlocked.Increment(ref _eventsRead);
                    yield return evt;
                }
            }
        }
        else
        {
            // Sequential deserialization for smaller batches
            foreach (var line in lines)
            {
                MarketEvent? evt = null;
                try
                {
                    evt = JsonSerializer.Deserialize<MarketEvent>(line, MarketDataJsonContext.HighPerformanceOptions);
                }
                catch
                {
                    // Skip invalid lines
                }

                if (evt != null)
                {
                    Interlocked.Increment(ref _eventsRead);
                    yield return evt;
                }
            }
        }
    }

    /// <summary>
    /// Reads events from a specific time range.
    /// </summary>
    public async IAsyncEnumerable<MarketEvent> ReadEventsInRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in ReadEventsAsync(ct))
        {
            if (evt.Timestamp >= from && evt.Timestamp <= to)
            {
                yield return evt;
            }
        }
    }

    /// <summary>
    /// Reads events for specific symbols only.
    /// </summary>
    public async IAsyncEnumerable<MarketEvent> ReadEventsForSymbolsAsync(
        IReadOnlySet<string> symbols,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in ReadEventsAsync(ct))
        {
            if (symbols.Contains(evt.Symbol))
            {
                yield return evt;
            }
        }
    }

    /// <summary>
    /// Gets statistics about the files in the root directory.
    /// </summary>
    public FileStatistics GetFileStatistics()
    {
        if (!Directory.Exists(_root))
        {
            return new FileStatistics(0, 0, 0, 0);
        }

        var files = Directory.EnumerateFiles(_root, "*.jsonl*", SearchOption.AllDirectories).ToList();
        var totalSize = files.Sum(f => new FileInfo(f).Length);
        var compressedCount = files.Count(f =>
            f.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".gzip", StringComparison.OrdinalIgnoreCase));

        return new FileStatistics(
            files.Count,
            totalSize,
            compressedCount,
            files.Count - compressedCount);
    }
}

/// <summary>
/// Statistics about files in a directory.
/// </summary>
public readonly record struct FileStatistics(
    int TotalFiles,
    long TotalBytes,
    int CompressedFiles,
    int UncompressedFiles);
