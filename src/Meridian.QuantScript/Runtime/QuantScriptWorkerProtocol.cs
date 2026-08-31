using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript.Runtime;

internal static class QuantScriptWorkerProtocol
{
    public const int Version = 1;
    public const string Execute = "execute";
    public const string DataRequest = "data-request";
    public const string DataResponse = "data-response";
    public const string Result = "result";
    public const string FatalError = "fatal-error";

    public static JsonSerializerOptions SerializerOptions { get; } = QuantScriptWorkerJsonContext.Default.Options;

    internal static JsonTypeInfo<T> GetTypeInfo<T>()
        => QuantScriptWorkerJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
           ?? throw new WorkerProtocolException(
               $"Worker protocol type '{typeof(T).FullName}' is not registered in the source-generated JSON context.");

    internal static JsonTypeInfo GetTypeInfo(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return QuantScriptWorkerJsonContext.Default.GetTypeInfo(type)
               ?? throw new WorkerProtocolException(
                   $"Worker protocol type '{type.FullName}' is not registered in the source-generated JSON context.");
    }

    internal static JsonElement SerializeToElement<T>(T value)
        => JsonSerializer.SerializeToElement(value, GetTypeInfo<T>());

    internal static T? Deserialize<T>(JsonElement value)
        => value.Deserialize(GetTypeInfo<T>());

    public static async Task WriteAsync<T>(
        Stream stream,
        string kind,
        string correlationId,
        T payload,
        int maxFrameBytes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateLimit(maxFrameBytes);

        var envelope = new WorkerEnvelope(
            Version,
            kind,
            correlationId,
            SerializeToElement(payload));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, GetTypeInfo<WorkerEnvelope>());
        if (bytes.Length > maxFrameBytes)
        {
            throw new WorkerProtocolException(
                $"Worker protocol frame was {bytes.Length} bytes; the limit is {maxFrameBytes} bytes.");
        }

        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await stream.WriteAsync(length, ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<WorkerEnvelope> ReadAsync(
        Stream stream,
        int maxFrameBytes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateLimit(maxFrameBytes);

        var lengthBytes = new byte[sizeof(int)];
        await ReadExactlyOrThrowAsync(stream, lengthBytes, "worker protocol length", ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length <= 0 || length > maxFrameBytes)
        {
            throw new WorkerProtocolException(
                $"Worker protocol frame length {length} is outside the allowed range 1..{maxFrameBytes}.");
        }

        var bytes = new byte[length];
        await ReadExactlyOrThrowAsync(stream, bytes, "worker protocol payload", ct).ConfigureAwait(false);

        WorkerEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(bytes, GetTypeInfo<WorkerEnvelope>());
        }
        catch (JsonException ex)
        {
            throw new WorkerProtocolException("Worker returned malformed JSON.", ex);
        }

        if (envelope is null || envelope.Version != Version)
        {
            throw new WorkerProtocolException(
                $"Worker protocol version was {envelope?.Version.ToString(CultureInfo.InvariantCulture) ?? "missing"}; expected {Version}.");
        }
        if (string.IsNullOrWhiteSpace(envelope.Kind) || string.IsNullOrWhiteSpace(envelope.CorrelationId))
            throw new WorkerProtocolException("Worker protocol envelope omitted its kind or correlation id.");

        return envelope;
    }

    public static T ReadPayload<T>(WorkerEnvelope envelope)
    {
        try
        {
            return Deserialize<T>(envelope.Payload)
                ?? throw new WorkerProtocolException($"Worker '{envelope.Kind}' payload was null.");
        }
        catch (JsonException ex)
        {
            throw new WorkerProtocolException($"Worker '{envelope.Kind}' payload was malformed.", ex);
        }
    }

    private static async Task ReadExactlyOrThrowAsync(
        Stream stream,
        Memory<byte> buffer,
        string part,
        CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], ct).ConfigureAwait(false);
            if (count == 0)
                throw new WorkerProtocolException($"Worker closed the channel before the {part} was complete.");
            read += count;
        }
    }

    private static void ValidateLimit(int maxFrameBytes)
    {
        if (maxFrameBytes <= sizeof(int))
            throw new ArgumentOutOfRangeException(nameof(maxFrameBytes), "Protocol limit must exceed four bytes.");
    }

}

internal sealed record WorkerEnvelope(
    int Version,
    string Kind,
    string CorrelationId,
    JsonElement Payload);

internal sealed record WorkerExecutionRequest(
    IReadOnlyList<WorkerScriptCell> ReplayCells,
    WorkerScriptCell CurrentCell,
    WorkerRunOptions Options);

internal sealed record WorkerScriptCell(
    string Source,
    IReadOnlyDictionary<string, WorkerParameterValue> Parameters);

internal sealed record WorkerRunOptions(
    int CompilationTimeoutSeconds,
    bool EnableUnsafeScripts,
    int MaxCachedCompilations,
    int MaxPlotsPerRun,
    string DefaultDataRoot,
    int MaxRunElapsedMilliseconds,
    int MaxOutputItemsPerRun,
    int MaxProtocolBytes);

internal sealed record WorkerExecutionResponse(WorkerScriptRunResult Result);

internal sealed record WorkerFatalError(string Message);

internal enum WorkerDataOperation
{
    Prices,
    Trades,
    OrderBook,
    SecurityMaster,
    CorporateActions
}

internal sealed record WorkerDataRequest(
    WorkerDataOperation Operation,
    string Symbol,
    DateOnly? From = null,
    DateOnly? To = null,
    DateOnly? Date = null,
    DateTimeOffset? Timestamp = null,
    string? Provider = null);

internal sealed record WorkerDataResponse(bool Success, JsonElement Value, string? Error);

internal sealed record WorkerPriceSeries(string Symbol, IReadOnlyList<PriceBar> Bars)
{
    public static WorkerPriceSeries From(PriceSeries series) => new(series.Symbol, series.Bars);

    public PriceSeries ToPriceSeries() => new(Symbol, Bars);
}

internal sealed record WorkerOrderBookLevel(decimal Price, long Size);

internal sealed record WorkerOrderBook(
    DateTimeOffset Timestamp,
    IReadOnlyList<WorkerOrderBookLevel> Bids,
    IReadOnlyList<WorkerOrderBookLevel> Asks)
{
    public static WorkerOrderBook? From(ScriptOrderBook? orderBook)
        => orderBook is null
            ? null
            : new WorkerOrderBook(
                orderBook.Timestamp,
                orderBook.Bids.Select(static level => new WorkerOrderBookLevel(level.Price, level.Size)).ToList(),
                orderBook.Asks.Select(static level => new WorkerOrderBookLevel(level.Price, level.Size)).ToList());

    public ScriptOrderBook ToOrderBook()
        => new(
            Timestamp,
            Bids.Select(static level => (level.Price, level.Size)).ToList(),
            Asks.Select(static level => (level.Price, level.Size)).ToList());
}

internal sealed record WorkerPlotPoint(DateOnly Date, double Value);

internal sealed record WorkerPlotSeries(string Label, IReadOnlyList<WorkerPlotPoint> Values);

internal sealed record WorkerPlotRequest(
    string Title,
    PlotType Type,
    IReadOnlyList<WorkerPlotPoint>? Series,
    IReadOnlyList<WorkerPlotSeries>? MultiSeries,
    IReadOnlyList<PriceBar>? Candlestick,
    double[][]? HeatmapData,
    string[]? HeatmapLabels)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
            throw new WorkerProtocolException("Worker plot omitted its title.");
        if (Series?.Any(static point => point is null) == true ||
            MultiSeries?.Any(static series => series is null ||
                series.Values is null || series.Values.Any(static point => point is null)) == true ||
            Candlestick?.Any(static bar => bar is null) == true ||
            HeatmapData?.Any(static row => row is null) == true ||
            HeatmapLabels?.Any(static label => label is null) == true)
        {
            throw new WorkerProtocolException("Worker plot contained a null nested value.");
        }
    }

    public static WorkerPlotRequest From(PlotRequest plot)
        => new(
            plot.Title,
            plot.Type,
            plot.Series?.Select(static point => new WorkerPlotPoint(point.Date, point.Value)).ToList(),
            plot.MultiSeries?.Select(static series => new WorkerPlotSeries(
                series.Label,
                series.Values.Select(static point => new WorkerPlotPoint(point.Date, point.Value)).ToList())).ToList(),
            plot.Candlestick,
            plot.HeatmapData,
            plot.HeatmapLabels);

    public PlotRequest ToPlotRequest()
        => new(
            Title,
            Type,
            Series?.Select(static point => (point.Date, point.Value)).ToList(),
            MultiSeries?.Select(static series => (
                series.Label,
                (IReadOnlyList<(DateOnly Date, double Value)>)series.Values
                    .Select(static point => (point.Date, point.Value))
                    .ToList())).ToList(),
            Candlestick,
            HeatmapData,
            HeatmapLabels);
}

internal sealed record WorkerParameterDescriptor(
    string Name,
    string TypeName,
    string Label,
    WorkerParameterValue DefaultValue,
    double Min,
    double Max,
    string? Description)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(TypeName) ||
            string.IsNullOrWhiteSpace(Label) || DefaultValue is null)
        {
            throw new WorkerProtocolException("Worker parameter descriptor omitted a required value.");
        }
    }

    public static WorkerParameterDescriptor From(ParameterDescriptor descriptor)
        => new(
            descriptor.Name,
            descriptor.TypeName,
            descriptor.Label,
            WorkerParameterValue.FromObject(descriptor.DefaultValue, descriptor.Name),
            descriptor.Min,
            descriptor.Max,
            descriptor.Description);

    public ParameterDescriptor ToParameterDescriptor()
        => new(Name, TypeName, Label, DefaultValue.ToObject(), Min, Max, Description);
}

internal sealed record WorkerScriptRunResult(
    bool Success,
    long ElapsedTicks,
    long CompileTimeTicks,
    IReadOnlyList<ScriptDiagnostic> CompilationErrors,
    IReadOnlyList<ScriptDiagnostic> RuntimeDiagnostics,
    string? RuntimeError,
    string ConsoleOutput,
    IReadOnlyList<KeyValuePair<string, string>> Metrics,
    IReadOnlyList<WorkerPlotRequest> Plots,
    IReadOnlyList<ScriptTradeResult> Trades,
    IReadOnlyList<BacktestResult> CapturedBacktests,
    IReadOnlyList<WorkerParameterDescriptor> RuntimeParameters)
{
    public void Validate()
    {
        if (ElapsedTicks < 0 || CompileTimeTicks < 0)
            throw new WorkerProtocolException("Worker result contained negative timing values.");
        if (CompilationErrors is null || RuntimeDiagnostics is null || ConsoleOutput is null ||
            Metrics is null || Plots is null || Trades is null || CapturedBacktests is null ||
            RuntimeParameters is null)
        {
            throw new WorkerProtocolException("Worker result omitted one or more required collections.");
        }
        if (CompilationErrors.Any(static item => item is null) ||
            RuntimeDiagnostics.Any(static item => item is null) ||
            Plots.Any(static item => item is null) ||
            Trades.Any(static item => item is null) ||
            CapturedBacktests.Any(static item => item is null) ||
            RuntimeParameters.Any(static item => item is null))
        {
            throw new WorkerProtocolException("Worker result contained a null collection item.");
        }
        if (CompilationErrors.Any(static item => item.Severity is null || item.Message is null) ||
            RuntimeDiagnostics.Any(static item => item.Severity is null || item.Message is null) ||
            Metrics.Any(static item => item.Key is null || item.Value is null))
        {
            throw new WorkerProtocolException("Worker result contained an incomplete diagnostic or metric.");
        }

        foreach (var plot in Plots)
            plot.Validate();
        foreach (var parameter in RuntimeParameters)
            parameter.Validate();
    }

    public static WorkerScriptRunResult From(ScriptRunResult result)
        => new(
            result.Success,
            result.Elapsed.Ticks,
            result.CompileTime.Ticks,
            result.CompilationErrors,
            result.RuntimeDiagnostics,
            result.RuntimeError,
            result.ConsoleOutput,
            result.Metrics,
            result.Plots.Select(WorkerPlotRequest.From).ToList(),
            result.Trades,
            result.CapturedBacktests,
            result.RuntimeParameters.Select(WorkerParameterDescriptor.From).ToList());

    public ScriptRunResult ToScriptRunResult(long peakMemoryBytes, ScriptExecutionCheckpoint? checkpoint)
        => new(
            Success,
            TimeSpan.FromTicks(ElapsedTicks),
            TimeSpan.FromTicks(CompileTimeTicks),
            peakMemoryBytes,
            CompilationErrors,
            RuntimeDiagnostics,
            RuntimeError,
            ConsoleOutput,
            Metrics,
            Plots.Select(static plot => plot.ToPlotRequest()).ToList(),
            Trades,
            CapturedBacktests,
            RuntimeParameters.Select(static parameter => parameter.ToParameterDescriptor()).ToList(),
            checkpoint);
}

internal sealed record WorkerParameterValue(string Kind, string? Value)
{
    public static WorkerParameterValue FromObject(object? value, string parameterName)
    {
        if (value is JsonElement json)
            value = ConvertJsonElement(json, parameterName);

        return value switch
        {
            null => new("null", null),
            string text => new("string", text),
            char character => new("char", character.ToString()),
            bool boolean => new("bool", boolean ? "true" : "false"),
            byte number => new("byte", number.ToString(CultureInfo.InvariantCulture)),
            sbyte number => new("sbyte", number.ToString(CultureInfo.InvariantCulture)),
            short number => new("int16", number.ToString(CultureInfo.InvariantCulture)),
            ushort number => new("uint16", number.ToString(CultureInfo.InvariantCulture)),
            int number => new("int32", number.ToString(CultureInfo.InvariantCulture)),
            uint number => new("uint32", number.ToString(CultureInfo.InvariantCulture)),
            long number => new("int64", number.ToString(CultureInfo.InvariantCulture)),
            ulong number => new("uint64", number.ToString(CultureInfo.InvariantCulture)),
            float number => new("single", number.ToString("R", CultureInfo.InvariantCulture)),
            double number => new("double", number.ToString("R", CultureInfo.InvariantCulture)),
            decimal number => new("decimal", number.ToString(CultureInfo.InvariantCulture)),
            DateOnly date => new("date-only", date.ToString("O", CultureInfo.InvariantCulture)),
            DateTime dateTime => new("date-time", dateTime.ToString("O", CultureInfo.InvariantCulture)),
            DateTimeOffset dateTimeOffset => new("date-time-offset", dateTimeOffset.ToString("O", CultureInfo.InvariantCulture)),
            Guid guid => new("guid", guid.ToString("D")),
            _ => throw new WorkerProtocolException(
                $"Script parameter '{parameterName}' has unsupported isolated-worker type '{value.GetType().FullName}'.")
        };
    }

    public object? ToObject()
        => Kind switch
        {
            "null" => null,
            "string" => Value ?? string.Empty,
            "char" => !string.IsNullOrEmpty(Value) ? Value[0] : '\0',
            "bool" => bool.Parse(RequireValue()),
            "byte" => byte.Parse(RequireValue(), CultureInfo.InvariantCulture),
            "sbyte" => sbyte.Parse(RequireValue(), CultureInfo.InvariantCulture),
            "int16" => short.Parse(RequireValue(), CultureInfo.InvariantCulture),
            "uint16" => ushort.Parse(RequireValue(), CultureInfo.InvariantCulture),
            "int32" => int.Parse(RequireValue(), CultureInfo.InvariantCulture),
            "uint32" => uint.Parse(RequireValue(), CultureInfo.InvariantCulture),
            "int64" => long.Parse(RequireValue(), CultureInfo.InvariantCulture),
            "uint64" => ulong.Parse(RequireValue(), CultureInfo.InvariantCulture),
            "single" => float.Parse(RequireValue(), CultureInfo.InvariantCulture),
            "double" => double.Parse(RequireValue(), CultureInfo.InvariantCulture),
            "decimal" => decimal.Parse(RequireValue(), CultureInfo.InvariantCulture),
            "date-only" => DateOnly.ParseExact(RequireValue(), "O", CultureInfo.InvariantCulture),
            "date-time" => DateTime.Parse(RequireValue(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            "date-time-offset" => DateTimeOffset.Parse(RequireValue(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            "guid" => Guid.ParseExact(RequireValue(), "D"),
            _ => throw new WorkerProtocolException($"Worker parameter value kind '{Kind}' is unsupported.")
        };

    private string RequireValue()
        => Value ?? throw new WorkerProtocolException($"Worker parameter value kind '{Kind}' omitted its value.");

    private static object? ConvertJsonElement(JsonElement value, string parameterName)
        => value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var integer) => integer,
            JsonValueKind.Number when value.TryGetInt64(out var longInteger) => longInteger,
            JsonValueKind.Number when value.TryGetDecimal(out var decimalNumber) => decimalNumber,
            JsonValueKind.Number when value.TryGetDouble(out var doubleNumber) => doubleNumber,
            _ => throw new WorkerProtocolException(
                $"Script parameter '{parameterName}' must be a scalar JSON value for isolated execution.")
        };
}

internal sealed class WorkerProtocolException : Exception
{
    public WorkerProtocolException(string message)
        : base(message)
    {
    }

    public WorkerProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class ReadOnlyLedgerJsonConverter : JsonConverter<IReadOnlyLedger>
{
    public override IReadOnlyLedger? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var journalTypeInfo = options.GetTypeInfo(typeof(List<JournalEntry>)) as JsonTypeInfo<List<JournalEntry>>
            ?? throw new JsonException("Ledger journal type is missing from the generated worker JSON context.");
        var journal = JsonSerializer.Deserialize(ref reader, journalTypeInfo)
            ?? throw new JsonException("Backtest ledger journal was null.");
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var entry in journal)
            ledger.Post(entry);
        return ledger;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyLedger value, JsonSerializerOptions options)
    {
        var journalTypeInfo = options.GetTypeInfo(typeof(List<JournalEntry>)) as JsonTypeInfo<List<JournalEntry>>
            ?? throw new JsonException("Ledger journal type is missing from the generated worker JSON context.");
        JsonSerializer.Serialize(writer, value.Journal.ToList(), journalTypeInfo);
    }
}

internal sealed class ReadOnlyStringSetJsonConverter : JsonConverter<IReadOnlySet<string>>
{
    public override IReadOnlySet<string>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var listTypeInfo = options.GetTypeInfo(typeof(List<string>)) as JsonTypeInfo<List<string>>
            ?? throw new JsonException("String-list type is missing from the generated worker JSON context.");
        var values = JsonSerializer.Deserialize(ref reader, listTypeInfo)
            ?? throw new JsonException("Backtest universe was null.");
        return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlySet<string> value,
        JsonSerializerOptions options)
    {
        var listTypeInfo = options.GetTypeInfo(typeof(List<string>)) as JsonTypeInfo<List<string>>
            ?? throw new JsonException("String-list type is missing from the generated worker JSON context.");
        JsonSerializer.Serialize(writer, value.ToList(), listTypeInfo);
    }
}
