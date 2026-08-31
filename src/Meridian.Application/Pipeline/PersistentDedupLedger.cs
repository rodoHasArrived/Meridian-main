using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Core.Logging;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Events;
using Serilog;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Persistent deduplication ledger that survives restarts.
/// Uses a JSONL-backed rolling log with in-memory bloom-filter-like cache.
/// Keyed by (provider, symbol, eventIdentity) with configurable TTL.
/// </summary>
/// <remarks>
/// Persisted entries are versioned. Legacy lines without a <c>"v"</c> field are version 1:
/// recorded before sink durability was confirmed, valid for live-ingress suppression only and
/// untrusted during WAL recovery. Lines with <c>"v":2</c> are written exclusively by
/// <see cref="CommitDurableAsync"/> after the caller's primary sink flushed, so they may
/// suppress WAL replay. Pending reservations are memory-only and are never persisted;
/// compaction rewrites committed entries preserving their version and never upgrades
/// legacy trust implicitly.
/// </remarks>
public sealed class PersistentDedupLedger : IDedupStore, IAsyncDisposable
{
    /// <summary>Entry recorded without sink-durability confirmation (legacy live-ingress trust).</summary>
    internal const byte EntryVersionLegacy = 1;

    /// <summary>Entry recorded after the primary sink flushed (trusted during WAL recovery).</summary>
    internal const byte EntryVersionSinkDurable = 2;

    private readonly ILogger _log = LoggingSetup.ForContext<PersistentDedupLedger>();
    private readonly string _ledgerPath;
    private readonly TimeSpan _entryTtl;
    private readonly int _maxInMemoryEntries;

    // In-memory cache: composite key → (last-seen timestamp, persisted entry version)
    private readonly ConcurrentDictionary<string, DedupCacheEntry> _cache = new(StringComparer.Ordinal);

    // Pending reservations: composite key → process-local claim token. Never persisted —
    // a crash discards all pending claims so their WAL records replay at-least-once.
    private readonly ConcurrentDictionary<string, long> _pendingReservations = new(StringComparer.Ordinal);
    private long _reservationTokenSequence;

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
    /// Total deliveries suppressed by a committed ledger entry. Unresolved pending claims are
    /// deferrals, not detections, and are never counted here.
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
        WarmPrefixCache();

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
        _ = CreateTradeKey(
            "warm:AAPL:Trade:",
            new Meridian.Contracts.Domain.Models.Trade(
                DateTimeOffset.UnixEpoch,
                "AAPL",
                1m,
                1,
                AggressorSide.Buy,
                1,
                "WARM",
                "XNAS"));
        _ = CreatePrefix("WARM", "AAPL", MarketEventType.Trade);
    }

    private void WarmPrefixCache()
    {
        lock (_prefixCacheLock)
        {
            _prefixCache[(string.Empty, string.Empty, MarketEventType.Trade)] = "::Trade:";
        }
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
                        // Legacy lines carry no "v" field and load as version 1 (untrusted for
                        // WAL recovery). Versions are clamped into the byte range defensively.
                        var version = EntryVersionLegacy;
                        if (root.TryGetProperty("v", out var versionElement) &&
                            versionElement.TryGetInt32(out var parsedVersion) &&
                            parsedVersion > EntryVersionLegacy)
                        {
                            version = parsedVersion >= byte.MaxValue ? byte.MaxValue : (byte)parsedVersion;
                        }

                        if (key != null && ticks > cutoff)
                        {
                            // Later lines refresh the timestamp, but a durability confirmation is
                            // never retracted by a later legacy sighting of the same identity.
                            if (_cache.TryGetValue(key, out var existing) && existing.Version > version)
                            {
                                version = existing.Version;
                            }

                            _cache[key] = new DedupCacheEntry(ticks, version);
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
    /// <remarks>
    /// Legacy admission check: a miss eagerly records a version-1 entry, i.e. without
    /// sink-durability confirmation, so it must not be used as a durability signal.
    /// A miss is admitted through the same per-key pending claim the reservation path uses, so
    /// the committed-state check and the legacy record are atomic with respect to concurrent
    /// <see cref="TryReserveAsync"/> callers. An identity held by an in-flight reservation is
    /// awaited (honouring <paramref name="ct"/>) until the claim commits or releases, never
    /// reported as a duplicate while only a memory-resident claim exists.
    /// Durable persistence paths use <see cref="TryReserveAsync"/> + <see cref="CommitDurableAsync"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<bool> IsDuplicateAsync(MarketEvent evt, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _totalChecked);

        var key = GetCachedOrComputeEventKey(evt);

        // Check cache first
        if (_cache.TryGetValue(key, out var existing) &&
            DateTimeOffset.UtcNow.Ticks - existing.Ticks < _entryTtl.Ticks)
        {
            Interlocked.Increment(ref _totalDuplicates);
            return true;
        }

        // A miss is admitted through the same per-key pending slot the reservation path uses,
        // so the committed-state check and the legacy record are atomic with respect to
        // concurrent TryReserveAsync callers — two callers can never both admit one identity.
        // While the slot is held elsewhere its claim proves nothing durable (the holder may
        // still fail and release), so wait for it to resolve: a commit surfaces here as a
        // durable duplicate, a release lets this caller claim the slot and record the identity.
        var token = Interlocked.Increment(ref _reservationTokenSequence);
        while (!_pendingReservations.TryAdd(key, token))
        {
            await Task.Delay(10, ct).ConfigureAwait(false);

            if (_cache.TryGetValue(key, out existing) &&
                DateTimeOffset.UtcNow.Ticks - existing.Ticks < _entryTtl.Ticks)
            {
                Interlocked.Increment(ref _totalDuplicates);
                return true;
            }
        }

        try
        {
            // Double-check after acquiring the slot: a concurrent commit may have published the
            // identity between the cache read above and the TryAdd (commits publish the cache
            // entry before releasing their pending token, so it is guaranteed visible here).
            var nowTicks = DateTimeOffset.UtcNow.Ticks;
            if (_cache.TryGetValue(key, out existing) && nowTicks - existing.Ticks < _entryTtl.Ticks)
            {
                Interlocked.Increment(ref _totalDuplicates);
                return true;
            }

            // Not a duplicate — record it as a legacy (durability-unconfirmed) identity. Any
            // entry already cached here is necessarily expired: a fresh entry of either
            // version returned above, and no commit can publish while this caller holds the
            // pending slot. An expired durability confirmation must not survive the refresh —
            // past the TTL the key may describe a different logical occurrence, and the old
            // sink write proves nothing about this newly admitted one, so trust resets to
            // version 1 and WAL recovery replays it until a sink flush re-confirms it.
            _cache[key] = new DedupCacheEntry(nowTicks, EntryVersionLegacy);
            var ledgerLine = CreateLedgerLine(key, nowTicks, EntryVersionLegacy);

            // Persist to disk (fire-and-forget the write, but serialize access)
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await EnsureWriterInitializedAsync(ct).ConfigureAwait(false);
                await _writer!.WriteLineAsync(ledgerLine.AsMemory(), ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            return false;
        }
        finally
        {
            ReleaseCore(key, token);
        }
    }

    /// <inheritdoc />
    public ValueTask<DedupReservationResult> TryReserveAsync(
        MarketEvent evt,
        DedupLookupScope scope,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _totalChecked);

        var key = GetCachedOrComputeEventKey(evt);
        var nowTicks = DateTimeOffset.UtcNow.Ticks;

        if (_cache.TryGetValue(key, out var existing) && nowTicks - existing.Ticks < _entryTtl.Ticks)
        {
            // Committed entries suppress live ingress at any version. WAL recovery trusts only
            // an exact durability confirmation: legacy identities — and unknown future
            // versions — are replayed rather than trusted.
            if (scope == DedupLookupScope.LiveIngress || existing.Version == EntryVersionSinkDurable)
            {
                Interlocked.Increment(ref _totalDuplicates);
                return ValueTask.FromResult(
                    new DedupReservationResult(DedupReservationStatus.Duplicate, default));
            }
        }

        var token = Interlocked.Increment(ref _reservationTokenSequence);
        if (!_pendingReservations.TryAdd(key, token))
        {
            // Not counted as a duplicate: a pending claim is an unresolved deferral, and a
            // caller waiting on it re-polls this path many times for one delivery. Only a
            // committed-entry suppression is a detection; callers that suppress on their own
            // batch-local claims track that in their own counters.
            // Carry the identity key (with no token) so callers such as WAL recovery can tell
            // whether the in-flight claim is one they hold themselves or an external one.
            return ValueTask.FromResult(new DedupReservationResult(
                DedupReservationStatus.PendingElsewhere,
                new DedupReservation(key, 0)));
        }

        // Double-check after acquiring the pending slot: a concurrent commit may have published
        // this identity between the cache read above and the TryAdd. Commits publish the cache
        // entry BEFORE releasing their pending token, so once the slot was free the confirmation
        // is guaranteed visible here — a reservation can never be granted for an identity that
        // is already durability-confirmed in the requested scope.
        if (_cache.TryGetValue(key, out existing) && nowTicks - existing.Ticks < _entryTtl.Ticks &&
            (scope == DedupLookupScope.LiveIngress || existing.Version == EntryVersionSinkDurable))
        {
            ReleaseCore(key, token);
            Interlocked.Increment(ref _totalDuplicates);
            return ValueTask.FromResult(
                new DedupReservationResult(DedupReservationStatus.Duplicate, default));
        }

        return ValueTask.FromResult(new DedupReservationResult(
            DedupReservationStatus.Reserved,
            new DedupReservation(key, token)));
    }

    /// <inheritdoc />
    public async Task CommitDurableAsync(
        IReadOnlyList<DedupReservation> reservations,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservations);
        if (reservations.Count == 0)
            return;

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureWriterInitializedAsync(ct).ConfigureAwait(false);
            var nowTicks = DateTimeOffset.UtcNow.Ticks;

            // Stage 1: append a durability-confirmed line for every reservation that is still
            // held by its token, remembering which ones qualified. Nothing in memory changes
            // yet, so a failure here (or in the flush below) leaves the pending claims intact
            // and the commit can simply be retried; a repeated line is harmless because loads
            // are last-write-wins.
            var validatedIndexes = new List<int>(reservations.Count);
            for (var i = 0; i < reservations.Count; i++)
            {
                var reservation = reservations[i];
                if (!IsReservationHeld(reservation))
                {
                    _log.Error(
                        "Dedup commit skipped reservation for key {Key}: its token is no longer held. The identity stays uncommitted and the event remains replayable",
                        reservation.Key);
                    continue;
                }

                var line = CreateLedgerLine(reservation.Key, nowTicks, EntryVersionSinkDurable);
                await _writer!.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
                validatedIndexes.Add(i);
            }

            // Stage 2: make the committed identities durable before publishing them.
            await _writer!.FlushAsync(ct).ConfigureAwait(false);

            // Stage 3: post-durability bookkeeping. Publish each committed entry BEFORE dropping
            // its pending claim: a concurrent TryReserveAsync that wins the freed slot re-checks
            // the cache after its TryAdd, so this ordering guarantees it observes the
            // durability confirmation instead of re-claiming an already-committed identity.
            // The cache write is unconditional for validated entries — their line is durably
            // flushed — while the release stays token-checked.
            foreach (var index in validatedIndexes)
            {
                var reservation = reservations[index];
                _cache[reservation.Key] = new DedupCacheEntry(nowTicks, EntryVersionSinkDurable);
                ReleaseCore(reservation.Key, reservation.Token);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public bool Release(in DedupReservation reservation)
    {
        return reservation.IsHeld && ReleaseCore(reservation.Key, reservation.Token);
    }

    private bool IsReservationHeld(in DedupReservation reservation)
    {
        return reservation.IsHeld &&
               _pendingReservations.TryGetValue(reservation.Key, out var heldToken) &&
               heldToken == reservation.Token;
    }

    private bool ReleaseCore(string key, long token)
    {
        // Atomic compare-and-remove: only the exact (key, token) pair is removed, so a stale
        // token can never release a newer holder's claim.
        return _pendingReservations.TryRemove(new KeyValuePair<string, long>(key, token));
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
    /// <remarks>
    /// SCOPE: content-based identity is implemented only for Trade, BBO quote, and LOB
    /// snapshot payloads — the high-volume types where provider replays and overlapping
    /// backfills occur. Every other payload type falls back to sequence-number identity,
    /// which dedups exact replays within one source stream but NOT re-deliveries that
    /// arrive under a different sequence. When adding a new high-volume payload type,
    /// give it a content key here rather than relying on the fallback.
    /// </remarks>
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
                prefix = CreatePrefix(cacheKey.Item1, cacheKey.Item2, cacheKey.Item3);
                _prefixCache[cacheKey] = prefix;
            }
        }

        var resolvedPrefix = prefix!;

        return evt.Payload switch
        {
            Contracts.Domain.Models.Trade trade => CreateTradeKey(resolvedPrefix, trade),

            Contracts.Domain.Models.BboQuotePayload quote => CreateQuoteKey(resolvedPrefix, quote),

            Contracts.Domain.Models.LOBSnapshot snap =>
                // L2: use sequence + timestamp
                resolvedPrefix + $"seq:{snap.SequenceNumber}",

            Contracts.Domain.Models.AggregateBarPayload agg =>
                // Aggregates: identity is the bar window itself (timeframe + start time),
                // which is deterministic and replay-stable. Streaming aggregate feeds carry
                // no provider sequence (Polygon A/AM), so sequence identity would either
                // collide (constant 0) or defeat dedup entirely (fabricated counters).
                resolvedPrefix + $"agg:{(byte)agg.Timeframe}:{agg.StartTime.UtcTicks}",

            _ =>
                // Fallback: sequence-number identity (see the scope remarks above) — only
                // dedups replays that preserve the original sequence numbering.
                resolvedPrefix + $"seq:{evt.Sequence}"
        };
    }

    private string GetCachedOrComputeEventKey(MarketEvent evt)
    {
        if (_eventKeyCache.TryGetValue(evt, out var cached))
        {
            return cached.Key;
        }

        // TryAdd: concurrent callers may race to cache the same event instance (the store is
        // shared and thread-safe); the computed key is deterministic, so the loser's value is
        // identical and the lost race is safely ignored.
        var key = ComputeEventKey(evt);
        _eventKeyCache.TryAdd(evt, new CachedKeyBox(key));
        return key;
    }

    private static string CreateTradeKey(string prefix, Contracts.Domain.Models.Trade trade)
    {
        Span<byte> hashBuf = stackalloc byte[32];
        HashTradeIdentityCore(trade, hashBuf);
        return CreateHashedKey(prefix, hashBuf[..16]);
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

    private static string CreateQuoteKey(string prefix, Contracts.Domain.Models.BboQuotePayload quote)
    {
        Span<byte> hashBuf = stackalloc byte[32];
        HashQuoteIdentityCore(quote, hashBuf);
        return CreateHashedKey(prefix, hashBuf[..16]);
    }

    private static void HashQuoteIdentityCore(Contracts.Domain.Models.BboQuotePayload quote, Span<byte> destination)
    {
        const int maxBytes = 160;
        Span<byte> buffer = stackalloc byte[maxBytes];
        var written = WriteQuoteIdentity(quote, buffer);
        SHA256.TryHashData(buffer[..written], destination, out _);
    }

    private static string CreatePrefix(string? source, string? symbol, MarketEventType type)
    {
        source ??= string.Empty;
        symbol ??= string.Empty;
        var typeName = GetMarketEventTypeName(type);
        return string.Create(
            source.Length + symbol.Length + typeName.Length + 3,
            (Source: source, Symbol: symbol, TypeName: typeName),
            static (destination, state) =>
            {
                var offset = 0;
                state.Source.AsSpan().CopyTo(destination[offset..]);
                offset += state.Source.Length;
                destination[offset++] = ':';
                state.Symbol.AsSpan().CopyTo(destination[offset..]);
                offset += state.Symbol.Length;
                destination[offset++] = ':';
                state.TypeName.AsSpan().CopyTo(destination[offset..]);
                offset += state.TypeName.Length;
                destination[offset] = ':';
            });
    }

    private static string GetMarketEventTypeName(MarketEventType type)
        => type switch
        {
            MarketEventType.Trade => "Trade",
            MarketEventType.BboQuote => "BboQuote",
            MarketEventType.L2Snapshot => "L2Snapshot",
            MarketEventType.OrderFlow => "OrderFlow",
            MarketEventType.Integrity => "Integrity",
            MarketEventType.Heartbeat => "Heartbeat",
            MarketEventType.HistoricalBar => "HistoricalBar",
            MarketEventType.AggregateBar => "AggregateBar",
            MarketEventType.OptionQuote => "OptionQuote",
            MarketEventType.OptionTrade => "OptionTrade",
            MarketEventType.OptionGreeks => "OptionGreeks",
            MarketEventType.OptionChain => "OptionChain",
            MarketEventType.OpenInterest => "OpenInterest",
            MarketEventType.OrderAdd => "OrderAdd",
            MarketEventType.OrderModify => "OrderModify",
            MarketEventType.OrderCancel => "OrderCancel",
            MarketEventType.OrderExecute => "OrderExecute",
            MarketEventType.OrderReplace => "OrderReplace",
            _ => type.ToString()
        };

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

    private static string CreateLedgerLine(string key, long ticks, byte version)
    {
        var escapedKey = EscapeJson(key);
        // Version-1 lines keep the exact legacy shape (no "v" field) so files written by this
        // build remain readable by earlier builds; higher versions are additive.
        return version <= EntryVersionLegacy
            ? $"{{\"k\":\"{escapedKey}\",\"t\":{ticks}}}"
            : $"{{\"k\":\"{escapedKey}\",\"t\":{ticks},\"v\":{version}}}";
    }

    private static string CreateHashedKey(string prefix, ReadOnlySpan<byte> truncatedHash)
    {
        return string.Create(
            prefix.Length + 32,
            new HashedKeyState(prefix, truncatedHash),
            static (destination, state) =>
            {
                state.Prefix.AsSpan().CopyTo(destination);
                var hashDestination = destination[state.Prefix.Length..];
                WriteHexByte(hashDestination, 0, state.B0);
                WriteHexByte(hashDestination, 2, state.B1);
                WriteHexByte(hashDestination, 4, state.B2);
                WriteHexByte(hashDestination, 6, state.B3);
                WriteHexByte(hashDestination, 8, state.B4);
                WriteHexByte(hashDestination, 10, state.B5);
                WriteHexByte(hashDestination, 12, state.B6);
                WriteHexByte(hashDestination, 14, state.B7);
                WriteHexByte(hashDestination, 16, state.B8);
                WriteHexByte(hashDestination, 18, state.B9);
                WriteHexByte(hashDestination, 20, state.B10);
                WriteHexByte(hashDestination, 22, state.B11);
                WriteHexByte(hashDestination, 24, state.B12);
                WriteHexByte(hashDestination, 26, state.B13);
                WriteHexByte(hashDestination, 28, state.B14);
                WriteHexByte(hashDestination, 30, state.B15);
            });
    }

    private static void WriteHexByte(Span<char> destination, int offset, byte value)
    {
        const string hexDigits = "0123456789abcdef";
        destination[offset] = hexDigits[value >> 4];
        destination[offset + 1] = hexDigits[value & 0x0F];
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
        return _cache.TryGetValue(key!, out var existing) && (nowTicks - existing.Ticks < _entryTtl.Ticks);
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
        _cache[key] = new DedupCacheEntry(DateTimeOffset.UtcNow.Ticks, EntryVersionLegacy);
    }

    /// <summary>
    /// Returns the computed event key for <paramref name="evt"/> without performing
    /// any cache lookup or I/O. Used to measure key-computation cost in isolation.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal string ComputeKeyForBenchmark(MarketEvent evt)
    {
        return ComputeEventKey(evt);
    }

    private readonly struct HashedKeyState
    {
        public HashedKeyState(string prefix, ReadOnlySpan<byte> truncatedHash)
        {
            Prefix = prefix;
            B0 = truncatedHash[0];
            B1 = truncatedHash[1];
            B2 = truncatedHash[2];
            B3 = truncatedHash[3];
            B4 = truncatedHash[4];
            B5 = truncatedHash[5];
            B6 = truncatedHash[6];
            B7 = truncatedHash[7];
            B8 = truncatedHash[8];
            B9 = truncatedHash[9];
            B10 = truncatedHash[10];
            B11 = truncatedHash[11];
            B12 = truncatedHash[12];
            B13 = truncatedHash[13];
            B14 = truncatedHash[14];
            B15 = truncatedHash[15];
        }

        public string Prefix { get; }
        public byte B0 { get; }
        public byte B1 { get; }
        public byte B2 { get; }
        public byte B3 { get; }
        public byte B4 { get; }
        public byte B5 { get; }
        public byte B6 { get; }
        public byte B7 { get; }
        public byte B8 { get; }
        public byte B9 { get; }
        public byte B10 { get; }
        public byte B11 { get; }
        public byte B12 { get; }
        public byte B13 { get; }
        public byte B14 { get; }
        public byte B15 { get; }
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
    /// A committed cache entry: when the identity was last seen and the persisted entry version
    /// (see <see cref="EntryVersionLegacy"/> / <see cref="EntryVersionSinkDurable"/>).
    /// </summary>
    private readonly record struct DedupCacheEntry(long Ticks, byte Version);

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
            if (kvp.Value.Ticks < cutoff)
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
                    // Only committed entries are rewritten — pending reservations stay
                    // memory-only — and each entry keeps its recorded version so compaction
                    // never implicitly upgrades legacy trust to durability-confirmed.
                    foreach (var (key, entry) in _cache)
                    {
                        if (entry.Ticks > cutoff)
                        {
                            await writer.WriteLineAsync(
                                CreateLedgerLine(key, entry.Ticks, entry.Version).AsMemory(), ct).ConfigureAwait(false);
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
