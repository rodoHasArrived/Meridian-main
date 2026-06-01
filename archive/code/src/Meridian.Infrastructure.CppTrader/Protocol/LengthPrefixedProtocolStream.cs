namespace Meridian.Infrastructure.CppTrader.Protocol;

internal sealed class LengthPrefixedProtocolStream(Stream input, Stream output)
{
    private readonly Stream _input = input;
    private readonly Stream _output = output;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public async Task WriteAsync<TPayload>(
        string messageType,
        string? requestId,
        string? sessionId,
        TPayload payload,
        CancellationToken ct)
    {
        var envelope = new CppTraderEnvelope(
            messageType,
            requestId,
            sessionId,
            JsonSerializer.SerializeToElement(payload, CppTraderJsonContext.ProtocolOptions),
            DateTimeOffset.UtcNow);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, CppTraderJsonContext.Default.CppTraderEnvelope);
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(header, ct).ConfigureAwait(false);
            await _output.WriteAsync(bytes, ct).ConfigureAwait(false);
            await _output.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<CppTraderEnvelope?> ReadAsync(CancellationToken ct)
    {
        var header = new byte[sizeof(int)];
        var readHeader = await FillAsync(_input, header, ct).ConfigureAwait(false);
        if (readHeader == 0)
            return null;

        if (readHeader != header.Length)
            throw new EndOfStreamException("Unexpected EOF while reading protocol header.");

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0)
            throw new InvalidDataException($"Invalid protocol frame length '{length}'.");

        var payloadBuffer = new byte[length];
        var readPayload = await FillAsync(_input, payloadBuffer, ct).ConfigureAwait(false);
        if (readPayload != length)
            throw new EndOfStreamException("Unexpected EOF while reading protocol payload.");

        return JsonSerializer.Deserialize(payloadBuffer, CppTraderJsonContext.Default.CppTraderEnvelope);
    }

    private static async Task<int> FillAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (read == 0)
                return total;

            total += read;
        }

        return total;
    }
}
