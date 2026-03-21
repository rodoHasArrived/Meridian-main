using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meridian.Core.Performance;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Batch-serializes hot-path struct events (<see cref="RawTradeEvent"/>,
/// <see cref="RawQuoteEvent"/>) to JSONL-formatted UTF-8 bytes.
/// </summary>
/// <remarks>
/// <para>
/// A pre-allocated <see cref="ArrayBufferWriter{T}"/> is reused across calls so that
/// the backing byte array grows at most logarithmically and is never released to the
/// GC between batches.  A <see cref="Utf8JsonWriter"/> is reset (not re-constructed)
/// before each batch to avoid repeated small allocations in the consumer loop.
/// </para>
/// <para>
/// <b>Not thread-safe.</b> A single instance must only be used from the consumer
/// thread that drains the corresponding <see cref="SpscRingBuffer{T}"/>.
/// </para>
/// <para>
/// The JSON schema emitted for each event is a compact, flat JSON line intended for
/// hot-path processing. Each line represents a single trade or quote object:
/// <code>
/// {"timestamp":"…","symbol":"SPY","price":100.01,"size":200,"aggressor":1,"sequence":42}
/// </code>
/// Quote events follow a similar shape with bid/ask-specific fields.
/// This schema is specific to the hot-path pipeline and does not include the
/// higher-level <c>MarketEvent</c> envelope (such as <c>type</c> or <c>payload</c>).
/// </para>
/// </remarks>
public sealed class HotPathBatchSerializer
{
    private readonly SymbolTable _symbolTable;
    private readonly ArrayBufferWriter<byte> _buffer;
    private readonly JsonWriterOptions _writerOptions;

    // Newline byte written between JSONL records.
    private static readonly byte NewlineByte = (byte)'\n';

    /// <summary>
    /// Creates a new serializer backed by the given symbol table.
    /// </summary>
    /// <param name="symbolTable">
    /// Shared symbol table used to resolve integer symbol IDs back to strings.
    /// Must be the same instance used by the producing ring-buffer writer.
    /// </param>
    /// <param name="initialBufferCapacity">
    /// Initial byte capacity of the pre-allocated write buffer.
    /// The buffer grows automatically when the batch exceeds this size.
    /// Default is 64 KiB — sufficient for ~500 trade events.
    /// </param>
    public HotPathBatchSerializer(SymbolTable symbolTable, int initialBufferCapacity = 65_536)
    {
        _symbolTable = symbolTable ?? throw new ArgumentNullException(nameof(symbolTable));
        if (initialBufferCapacity < 64)
            throw new ArgumentOutOfRangeException(nameof(initialBufferCapacity), "Buffer capacity must be at least 64 bytes.");

        _buffer = new ArrayBufferWriter<byte>(initialBufferCapacity);
        _writerOptions = new JsonWriterOptions { SkipValidation = true };
    }

    /// <summary>
    /// Serializes a batch of <see cref="RawTradeEvent"/> structs to JSONL UTF-8 bytes.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="ReadOnlyMemory{T}"/> is valid only until the next call
    /// to <see cref="SerializeTrades"/> or <see cref="SerializeQuotes"/> on this instance
    /// (the underlying buffer is reused).  Copy the bytes if longer lifetime is needed.
    /// </remarks>
    /// <param name="batch">Span of trade events to serialize.</param>
    /// <returns>
    /// UTF-8 encoded JSONL bytes — one JSON object per line, terminated by <c>\n</c>.
    /// Returns an empty span when <paramref name="batch"/> is empty.
    /// </returns>
    public ReadOnlyMemory<byte> SerializeTrades(ReadOnlySpan<RawTradeEvent> batch)
    {
        if (batch.IsEmpty)
            return ReadOnlyMemory<byte>.Empty;

        _buffer.Clear();

        var writer = new Utf8JsonWriter(_buffer, _writerOptions);
        Span<byte> newline = stackalloc byte[1] { NewlineByte };

        for (var i = 0; i < batch.Length; i++)
        {
            WriteTrade(ref writer, in batch[i]);
            writer.Flush();
            _buffer.Write(newline);
            writer.Reset(_buffer);
        }

        return _buffer.WrittenMemory;
    }

    /// <summary>
    /// Serializes a batch of <see cref="RawQuoteEvent"/> structs to JSONL UTF-8 bytes.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="ReadOnlyMemory{T}"/> is valid only until the next call
    /// to <see cref="SerializeTrades"/> or <see cref="SerializeQuotes"/> on this instance.
    /// </remarks>
    /// <param name="batch">Span of quote events to serialize.</param>
    /// <returns>
    /// UTF-8 encoded JSONL bytes — one JSON object per line, terminated by <c>\n</c>.
    /// Returns an empty span when <paramref name="batch"/> is empty.
    /// </returns>
    public ReadOnlyMemory<byte> SerializeQuotes(ReadOnlySpan<RawQuoteEvent> batch)
    {
        if (batch.IsEmpty)
            return ReadOnlyMemory<byte>.Empty;

        _buffer.Clear();

        var writer = new Utf8JsonWriter(_buffer, _writerOptions);
        Span<byte> newline = stackalloc byte[1] { NewlineByte };

        for (var i = 0; i < batch.Length; i++)
        {
            WriteQuote(ref writer, in batch[i]);
            writer.Flush();
            _buffer.Write(newline);
            writer.Reset(_buffer);
        }

        return _buffer.WrittenMemory;
    }

    // Writes a single trade as a compact JSON object.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteTrade(ref Utf8JsonWriter writer, in RawTradeEvent t)
    {
        var symbol = _symbolTable.TryGetSymbol(t.SymbolHash) ?? string.Empty;
        var ts = new DateTimeOffset(t.TimestampTicks, TimeSpan.Zero);

        writer.WriteStartObject();
        writer.WriteString("timestamp", ts);
        writer.WriteString("symbol", symbol);
        writer.WriteNumber("price", t.Price);
        writer.WriteNumber("size", t.Size);
        writer.WriteNumber("aggressor", t.Aggressor);
        writer.WriteNumber("sequence", t.Sequence);
        writer.WriteEndObject();
    }

    // Writes a single quote as a compact JSON object.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteQuote(ref Utf8JsonWriter writer, in RawQuoteEvent q)
    {
        var symbol = _symbolTable.TryGetSymbol(q.SymbolHash) ?? string.Empty;
        var ts = new DateTimeOffset(q.TimestampTicks, TimeSpan.Zero);

        writer.WriteStartObject();
        writer.WriteString("timestamp", ts);
        writer.WriteString("symbol", symbol);
        writer.WriteNumber("bidPrice", q.BidPrice);
        writer.WriteNumber("bidSize", q.BidSize);
        writer.WriteNumber("askPrice", q.AskPrice);
        writer.WriteNumber("askSize", q.AskSize);
        writer.WriteNumber("sequence", q.Sequence);
        writer.WriteEndObject();
    }
}
