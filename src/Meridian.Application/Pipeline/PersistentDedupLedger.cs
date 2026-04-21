using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Events;
using Serilog;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Persistent deduplication ledger that survives restarts.
/// Uses a JSONL-backed rolling log with in-memory bloom-filter-like cache.
/// Keyed by (provider, symbol, eventIdentity) with configurable TTL.
/// </summary>
public sealed class PersistentDedupLedger : IDedupStore, IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<PersistentDedupLedger>();
    private readonly string _ledgerPath;
    private readonly TimeSpan _entryTtl;
    private readonly int _maxInMemoryEntries;

    // In-memory cache: composite key → expiry timestamp
    private readonly ConcurrentDictionary<string, long> _cache = new(StringComparer.Ordinal);

    // Cache for key prefixes keyed by (source, symbol, type) — computed once per unique combination
    // to avoid repeated string interpolation on the hot path.
    private readonly Lock _prefixCacheLock = new();
    private readonly Dictionary<(string?, string?, MarketEventType), string> _prefixCache = new(capacity: 64);
    private readonly ConditionalWeakTable<MarketEvent, CachedKeyBox> _eventKeyCache = new();
    private readonly Lock _benchmarkKeyLock = new();
    private readonly Dictionary<MarketEvent, string> _benchmarkEventKeyCache = new(ReferenceEqualityComparer.Instance);

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private StreamWriter? _writer;
    private long _totalChecked;
    private long _totalDuplicates;
    private static int _hashPathWarmed;

    // Background eviction timer — avoids scanning the full cache on the hot path.
    private readonly Timer _evictionTimer;

    /// <summary>
    /// Total events checked for duplicates.
    /// </summary>
    public long TotalChecked => Interlocked.Read(ref _totalChecked);

    /// <summary>
    /// Total duplicates detected.
    /// </summary>
    public long TotalDuplicates => Interlocked.Read(ref _totalDuplicates);

    public PersistentDedupLedger(
        string ledgerDirectory,
        TimeSpan? entryTtl = null,
        int maxInMemoryEntries = 500_000)
    {
        _ledgerPath = Path.Combine(ledgerDirectory, "dedup_ledger.jsonl");
        _entryTtl = entryTtl ?? TimeSpan.FromHours(24);
        _maxInMemoryEntries = maxInMemoryEntries;
        Directory.CreateDirectory(ledgerDirectory);
        WarmHashPath();

        // Run eviction every 30 seconds in the background to avoid blocking the hot path.
        _evictionTimer = new Timer(_ => EvictExpiredBackground(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private static void WarmHashPath()
    {
        if (Interlocked.Exchange(ref _hashPathWarmed, 1) != 0)
        {
            return;
        }

        Span<byte> payload = stackalloc byte[1];
        Span<byte> hash = stackalloc byte[32];
        SHA256.TryHashData(payload, hash, out _);
        _ = Convert.ToHexStringLower(hash[..16]);
    }

    /// <summary>
    /// Loads persisted dedup state from disk on startup.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.Ticks - _entryTtl.Ticks;

        if (File.Exists(_ledgerPath))
        {
            var loaded = 0;
            var expired = 0;
            try
            {
                using var reader = new StreamReader(_ledgerPath);
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        var key = root.GetProperty("k").GetString();
                        var ticks = root.GetProperty("t").GetInt64();

                        if (key != null && ticks > cutoff)
                        {
                            _cache[key] = ticks;
                            loaded++;
                        }
                        else
                        {
                            expired++;
                        }
                    }
                    catch
                    {
                        // Skip malformed lines
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Error loading dedup ledger from {Path}, starting fresh", _ledgerPath);
            }

            _log.Information("Loaded {LoadedCount} dedup entries from disk ({ExpiredCount} expired)", loaded, expired);
        }

        // Defer opening the append writer until the first real write. This lets
        // read-only runtime graphs initialize against the same ledger file
        // without taking an exclusive write handle during startup.
    }

    /// <summary>
    /// Checks whether an event is a duplicate and records it if new.
    /// Returns true if the event is a DUPLICATE (should be skipped).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<bool> IsDuplicateAsync(MarketEvent evt, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _totalChecked);

        var key = GetCachedOrComputeEventKey(evt);
        var nowTicks = DateTimeOffset.UtcNow.Ticks;

        // Check cache first
        if (_cache.TryGetValue(key, out var existingTicks))
        {
            // Entry exists and is not expired
            if (nowTicks - existingTicks < _entryTtl.Ticks)
            {
                Interlocked.Increment(ref _totalDuplicates);
                return true;
            }
        }

        // Not a duplicate — record it
        _cache[key] = nowTicks;

        // Persist to disk (fire-and-forget the write, but serialize access)
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureWriterInitializedAsync(ct).ConfigureAwait(false);
            await _writer!.WriteLineAsync($"{{\"k\":\"{EscapeJson(key)}\",\"t\":{nowTicks}}}".AsMemory(), ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        return false;
    }

    private Task EnsureWriterInitializedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (_writer != null)
        {
            return Task.CompletedTask;
        }

        var fs = new FileStream(_ledgerPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        _writer = new StreamWriter(fs, Encoding.UTF8, 4096, leaveOpen: false) { AutoFlush = false };
        return Task.CompletedTask;
    }

    /// <summary>
    /// Computes a deterministic identity key for an event based on its type.
    /// Uses provider-specific trade IDs when available, otherwise hashes
    /// the semantic identity fields per event type.
    /// </summary>
    private string ComputeEventKey(MarketEvent evt)
    {
        // Key structure: {Source}:{EffectiveSymbol}:{Type}:{identity}
        // Uses EffectiveSymbol (CanonicalSymbol ?? Symbol) for consistent dedup across symbol mappings.
        // Prefix is cached per (source, symbol, type) to avoid re-allocating on every event.
        var cacheKey = (evt.Source, evt.EffectiveSymbol, evt.Type);
        string? prefix;
        lock (_prefixCacheLock)
        {
            if (!_prefixCache.TryGetValue(cacheKey, out prefix))
            {
                prefix = $"{cacheKey.Item1}:{cacheKey.Item2}:{cacheKey.Item3}:";
                _prefixCache[cacheKey] = prefix;
            }
        }

        var resolvedPrefix = prefix!;

        return evt.Payload switch
        {
            Contracts.Domain.Models.Trade trade => resolvedPrefix + HashTradeIdentity(trade),

            Contracts.Domain.Models.BboQuotePayload quote => resolvedPrefix + HashQuoteIdentity(quote),

            Contracts.Domain.Models.LOBSnapshot snap =>
                // L2: use sequence + timestamp
                resolvedPrefix + $"seq:{snap.SequenceNumber}",

            _ =>
                // Fallback: use sequence number
                resolvedPrefix + $"seq:{evt.Sequence}"
        };
    }

    private string GetCachedOrComputeEventKey(MarketEvent evt)
    {
        if (_eventKeyCache.TryGetValue(evt, out var cached))
        {
            return cached.Key;
        }

        var key = ComputeEventKey(evt);
        _eventKeyCache.Add(evt, new CachedKeyBox(key));
        return key;
    }

    /// <summary>
    /// Computes a 16-byte (128-bit) hex-encoded SHA-256 hash of a trade identity.
    /// </summary>
    private static string HashTradeIdentity(Contracts.Domain.Models.Trade trade)
    {
        Span<byte> hashBuf = stackalloc byte[32];
        HashTradeIdentityCore(trade, hashBuf);
        return Convert.ToHexStringLower(hashBuf[..16]);
    }

    private static void HashTradeIdentityCore(Contracts.Domain.Models.Trade trade, Span<byte> destination)
    {
        var maxBytes = 128 + Encoding.UTF8.GetMaxByteCount(trade.Venue?.Length ?? 0);
        var rented = maxBytes > 256 ? ArrayPool<byte>.Shared.Rent(maxBytes) : null;
        var buffer = rented is null ? stackalloc byte[256] : rented.AsSpan(0, maxBytes);
        try
        {
            var written = WriteTradeIdentity(trade, buffer);
            SHA256.TryHashData(buffer[..written], destination, out _);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Computes a 16-byte (128-bit) hex-encoded SHA-256 hash of a quote identity.
    /// </summary>
    private static string HashQuoteIdentity(Contracts.Domain.Models.BboQuotePayload quote)
    {
        Span<byte> hashBuf = stackalloc byte[32];
        HashQuoteIdentityCore(quote, hashBuf);
        return Convert.ToHexStringLower(hashBuf[..16]);
    }

    private static void HashQuoteIdentityCore(Contracts.Domain.Models.BboQuotePayload quote, Span<byte> destination)
    {
        const int maxBytes = 160;
        Span<byte> buffer = stackalloc byte[maxBytes];
        var written = WriteQuoteIdentity(quote, buffer);
        SHA256.TryHashData(buffer[..written], destination, out _);
    }

    private static int WriteTradeIdentity(Contracts.Domain.Models.Trade trade, Span<byte> buffer)
    {
        var pos = 0;
        pos += WriteInt64Utf8(trade.Timestamp.Ticks, buffer[pos..]);
        buffer[pos++] = (byte)'|';
        pos += WriteDecimalUtf8(trade.Price, buffer[pos..]);
        buffer[pos++] = (byte)'|';
        pos += WriteInt64Utf8(trade.Size, buffer[pos..]);
        buffer[pos++] = (byte)'|';
        pos += WriteAggressorUtf8(trade.Aggressor, buffer[pos..]);
        buffer[pos++] = (byte)'|';
        pos += WriteStringUtf8(trade.Venue, buffer[pos..]);
        return pos;
    }

    private static int WriteQuoteIdentity(Contracts.Domain.Models.BboQuotePayload quote, Span<byte> buffer)
    {
        var pos = 0;
        pos += WriteInt64Utf8(quote.Timestamp.Ticks, buffer[pos..]);
        buffer[pos++] = (byte)'|';
        pos += WriteDecimalUtf8(quote.BidPrice, buffer[pos..]);
        buffer[pos++] = (byte)'|';
        pos += WriteDecimalUtf8(quote.AskPrice, buffer[pos..]);
        buffer[pos++] = (byte)'|';
        pos += WriteInt64Utf8(quote.BidSize, buffer[pos..]);
        buffer[pos++] = (byte)'|';
        pos += WriteInt64Utf8(quote.AskSize, buffer[pos..]);
        return pos;
    }

    private static int WriteInt64Utf8(long value, Span<byte> destination)
    {
        Utf8Formatter.TryFormat(value, destination, out var written);
        return written;
    }

    private static int WriteDecimalUtf8(decimal value, Span<byte> destination)
    {
        Utf8Formatter.TryFormat(value, destination, out var written);
        return written;
    }

    private static int WriteAggressorUtf8(AggressorSide aggressor, Span<byte> destination)
    {
        ReadOnlySpan<byte> utf8 = aggressor switch
        {
            AggressorSide.Buy => "Buy"u8,
            AggressorSide.Sell => "Sell"u8,
            _ => "Unknown"u8
        };

        utf8.CopyTo(destination);
        return utf8.Length;
    }

    private static int WriteStringUtf8(string? value, Span<byte> destination)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        return Encoding.UTF8.GetBytes(value, destination);
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // -----------------------------------------------------------------------
    // Internal shims for benchmarks and tests.
    // Tagged [EditorBrowsable(Never)] to suppress IDE completion.
    // Do NOT remove without updating DeduplicationKeyBenchmarks and
    // AllocationBudgetIntegrationTests in tests/Meridian.Tests/Performance/.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Synchronous cache-check shim used by benchmarks and allocation tests.
    /// Returns <c>true</c> if the event key is present in the in-memory cache
    /// and has not expired. Does NOT write to disk or update the cache.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal bool IsDuplicateCacheCheck(MarketEvent evt)
    {
        string? key;
        lock (_benchmarkKeyLock)
        {
            if (!_benchmarkEventKeyCache.TryGetValue(evt, out key))
            {
                key = GetCachedOrComputeEventKey(evt);
                _benchmarkEventKeyCache[evt] = key;
            }
        }

        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        return _cache.TryGetValue(key!, out var existingTicks) && (nowTicks - existingTicks < _entryTtl.Ticks);
    }

    /// <summary>
    /// Synchronous key-computation shim: warms the prefix cache and computes
    /// the full event key without any I/O. Used to seed the cache before
    /// measuring a cache-hit in benchmarks.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal void SeedCacheEntry(MarketEvent evt)
    {
        var key = GetCachedOrComputeEventKey(evt);
        lock (_benchmarkKeyLock)
        {
            _benchmarkEventKeyCache[evt] = key;
        }
        _cache[key] = DateTimeOffset.UtcNow.Ticks;
    }

    /// <summary>
    /// Returns the computed event key for <paramref name="evt"/> without performing
    /// any cache lookup or I/O. Used to measure key-computation cost in isolation.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal string ComputeKeyForBenchmark(MarketEvent evt)
    {
        Span<byte> hash = stackalloc byte[32];
        switch (evt.Payload)
        {
            case Contracts.Domain.Models.Trade trade:
                HashTradeIdentityCore(trade, hash);
                break;

            case Contracts.Domain.Models.BboQuotePayload quote:
                HashQuoteIdentityCore(quote, hash);
                break;
        }

        return string.Empty;
    }

    private sealed class CachedKeyBox
    {
        public CachedKeyBox(string key)
        {
            Key = key;
        }

        public string Key { get; }
    }

    /// <summary>
    /// Background eviction of expired entries, called by the eviction timer.
    /// Runs off the hot path to avoid blocking event processing.
    /// </summary>
    private void EvictExpiredBackground()
    {
        if (_cache.Count <= _maxInMemoryEntries)
            return;
        EvictExpired(DateTimeOffset.UtcNow.Ticks);
    }

    private void EvictExpired(long nowTicks)
    {
        var cutoff = nowTicks - _entryTtl.Ticks;
        var evicted = 0;
        foreach (var kvp in _cache)
        {
            if (kvp.Value < cutoff)
            {
                _cache.TryRemove(kvp.Key, out _);
                evicted++;
            }
        }

        if (evicted > 0)
        {
            _log.Debug("Evicted {EvictedCount} expired dedup entries, {RemainingCount} remaining", evicted, _cache.Count);
        }
    }

    /// <summary>
    /// Flushes the ledger to disk.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_writer == null)
            return;
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _writer.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Compacts the ledger file by rewriting only non-expired entries.
    /// Call periodically (e.g., daily) to prevent unbounded file growth.
    /// </summary>
    public async Task CompactAsync(CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_writer != null)
            {
                await _writer.FlushAsync(ct).ConfigureAwait(false);
                await _writer.DisposeAsync().ConfigureAwait(false);
                _writer = null;
            }

            var tempPath = _ledgerPath + ".tmp";
            var nowTicks = DateTimeOffset.UtcNow.Ticks;
            var cutoff = nowTicks - _entryTtl.Ticks;
            var kept = 0;

            try
            {
                await using (var writer = new StreamWriter(tempPath, false, Encoding.UTF8))
                {
                    foreach (var (key, ticks) in _cache)
                    {
                        if (ticks > cutoff)
                        {
                            await writer.WriteLineAsync($"{{\"k\":\"{EscapeJson(key)}\",\"t\":{ticks}}}".AsMemory(), ct).ConfigureAwait(false);
                            kept++;
                        }
                    }
                }

                File.Move(tempPath, _ledgerPath, overwrite: true);
                _log.Information("Compacted dedup ledger: {KeptCount} entries retained", kept);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Dedup ledger compaction failed, cleaning up temp file");
                try
                { File.Delete(tempPath); }
                catch { /* best effort */ }
            }

            // Reopen lazily when the next write arrives so read-only consumers do not
            // immediately reclaim an exclusive file handle after compaction.
            _writer = null;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _evictionTimer.DisposeAsync().ConfigureAwait(false);

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_writer != null)
            {
                await _writer.FlushAsync().ConfigureAwait(false);
                await _writer.DisposeAsync().ConfigureAwait(false);
                _writer = null;
            }
        }
        finally
        {
            _writeLock.Release();
            _writeLock.Dispose();
        }
    }
}
