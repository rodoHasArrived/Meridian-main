using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meridian.ProcessIsolation;
using Meridian.QuantScript.Api;

namespace Meridian.QuantScript.Runtime;

internal enum WorkerCompletionKind
{
    Completed,
    Cancelled,
    TimedOut,
    MemoryLimitExceeded,
    CpuLimitExceeded,
    ProcessLimitExceeded,
    HostRpcLimitExceeded,
    AdmissionRejected,
    StandardOutputLimitExceeded,
    StandardErrorLimitExceeded,
    ProtocolFailure,
    WorkerUnavailable,
    UnexpectedExit
}

internal sealed record WorkerExecutionOutcome(
    WorkerCompletionKind Kind,
    WorkerScriptRunResult? Result,
    long PeakMemoryBytes,
    string? Detail = null);

internal interface IQuantScriptWorkerClient
{
    Task<WorkerExecutionOutcome> ExecuteAsync(
        WorkerExecutionRequest request,
        IQuantDataContext dataContext,
        QuantScriptOptions options,
        CancellationToken ct);
}

internal sealed class QuantScriptWorkerClient(ILogger logger) : IQuantScriptWorkerClient
{
    public async Task<WorkerExecutionOutcome> ExecuteAsync(
        WorkerExecutionRequest request,
        IQuantDataContext dataContext,
        QuantScriptOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dataContext);
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        try
        {
            ValidateOptions(options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new WorkerExecutionOutcome(
                WorkerCompletionKind.WorkerUnavailable,
                null,
                0,
                ex.Message);
        }

        await using var parentToWorker = new AnonymousPipeServerStream(
            PipeDirection.Out,
            HandleInheritability.Inheritable);
        await using var workerToParent = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.Inheritable);

        ProcessStartInfo startInfo;
        try
        {
            startInfo = WorkerLaunchResolver.CreateStartInfo(
                options,
                parentToWorker.GetClientHandleAsString(),
                workerToParent.GetClientHandleAsString());
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or InvalidOperationException)
        {
            return new WorkerExecutionOutcome(
                WorkerCompletionKind.WorkerUnavailable,
                null,
                0,
                ex.Message);
        }

        ContainedProcess contained;
        try
        {
            contained = ContainedProcess.Start(
                startInfo,
                new ContainedProcessLimits(
                    options.MaxWorkerMemoryBytes,
                    TimeSpan.FromSeconds(options.MaxWorkerCpuTimeSeconds),
                    options.MaxWorkerProcessCount,
                    options.RequireHardResourceLimits));
        }
        catch (Exception ex)
        {
            return new WorkerExecutionOutcome(
                WorkerCompletionKind.WorkerUnavailable,
                null,
                0,
                $"Isolated worker containment could not be established: {ex.Message}");
        }

        await using (contained)
        {
            contained.Process.StandardInput.Close();
            parentToWorker.DisposeLocalCopyOfClientHandle();
            workerToParent.DisposeLocalCopyOfClientHandle();

            var process = contained.Process;
            var baselineResources = contained.GetResourceSnapshot();
            var baselineWorkingSet = baselineResources.CurrentMemoryBytes;
            var peakMemoryBytes = 0L;
            var terminal = new TaskCompletionSource<WorkerCompletionKind>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var ioCts = new CancellationTokenSource();
            using var monitorCts = new CancellationTokenSource();
            using var dataCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var callerRegistration = ct.Register(
                static state => ((TaskCompletionSource<WorkerCompletionKind>)state!)
                    .TrySetResult(WorkerCompletionKind.Cancelled),
                terminal);

            var channel = new QuantScriptWorkerChannel(
                workerToParent,
                parentToWorker,
                options.MaxWorkerProtocolBytes);
            var stdoutTask = CaptureBoundedAsync(
                process.StandardOutput.BaseStream,
                options.MaxWorkerStandardOutputBytes,
                WorkerCompletionKind.StandardOutputLimitExceeded,
                terminal);
            var stderrTask = CaptureBoundedAsync(
                process.StandardError.BaseStream,
                options.MaxWorkerStandardErrorBytes,
                WorkerCompletionKind.StandardErrorLimitExceeded,
                terminal);
            var exitTask = ObserveExitAsync(process, terminal);
            var timeoutTask = ObserveTimeoutAsync(options.RunTimeoutSeconds, terminal, monitorCts.Token);
            var resourceTask = ObserveResourcesAsync(
                contained,
                baselineWorkingSet,
                options.MaxWorkerMemoryBytes,
                options.MaxMemoryDeltaBytes,
                TimeSpan.FromSeconds(options.MaxWorkerCpuTimeSeconds),
                options.MaxWorkerProcessCount,
                options.WorkerMemoryPollIntervalMilliseconds,
                value => InterlockedExtensions.Max(ref peakMemoryBytes, value),
                terminal,
                monitorCts.Token);

            WorkerScriptRunResult? result = null;
            string? protocolFailure = null;
            var requestCorrelationId = Guid.NewGuid().ToString("N");
            var protocolTask = RunProtocolLoopAsync(
                channel,
                requestCorrelationId,
                request,
                dataContext,
                options,
                dataCts.Token,
                ioCts.Token);

            try
            {
                WorkerCompletionKind? resolvedCompletion = null;
                var first = await Task.WhenAny(protocolTask, terminal.Task).ConfigureAwait(false);
                if (first == protocolTask)
                {
                    try
                    {
                        result = await protocolTask.ConfigureAwait(false);
                    }
                    catch (HostRpcQuotaException ex)
                    {
                        protocolFailure = ex.Message;
                        terminal.TrySetResult(WorkerCompletionKind.HostRpcLimitExceeded);
                    }
                    catch (Exception ex) when (ex is WorkerProtocolException or IOException or JsonException)
                    {
                        protocolFailure = ex.Message;
                        terminal.TrySetResult(WorkerCompletionKind.ProtocolFailure);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested || dataCts.IsCancellationRequested)
                    {
                        terminal.TrySetResult(
                            ct.IsCancellationRequested
                                ? WorkerCompletionKind.Cancelled
                                : WorkerCompletionKind.ProtocolFailure);
                    }
                    catch (Exception ex)
                    {
                        protocolFailure = ex.Message;
                        terminal.TrySetResult(WorkerCompletionKind.ProtocolFailure);
                    }

                    if (result is not null)
                    {
                        var exited = await WaitForExitWithinAsync(
                            process,
                            TimeSpan.FromMilliseconds(options.WorkerExitGraceMilliseconds)).ConfigureAwait(false);
                        if (!exited)
                        {
                            protocolFailure = "Worker did not exit after returning its terminal result.";
                            terminal.TrySetResult(WorkerCompletionKind.ProtocolFailure);
                        }
                        else if (process.ExitCode != 0)
                        {
                            protocolFailure = $"Worker returned a result but exited with code {process.ExitCode}.";
                            terminal.TrySetResult(WorkerCompletionKind.UnexpectedExit);
                        }
                        else
                        {
                            var competingTermination = terminal.Task.IsCompleted
                                ? await terminal.Task.ConfigureAwait(false)
                                : WorkerCompletionKind.Completed;
                            resolvedCompletion = competingTermination == WorkerCompletionKind.UnexpectedExit
                                ? WorkerCompletionKind.Completed
                                : competingTermination;
                        }
                    }
                }
                else if (await terminal.Task.ConfigureAwait(false) == WorkerCompletionKind.UnexpectedExit)
                {
                    // The response pipe can retain the final frame for a short interval after exit.
                    try
                    {
                        result = await protocolTask
                            .WaitAsync(TimeSpan.FromMilliseconds(options.WorkerExitGraceMilliseconds))
                            .ConfigureAwait(false);
                        if (process.ExitCode == 0)
                            resolvedCompletion = WorkerCompletionKind.Completed;
                    }
                    catch (Exception ex) when (ex is WorkerProtocolException or IOException or TimeoutException)
                    {
                        protocolFailure = ex.Message;
                    }
                }

                var completion = resolvedCompletion ?? await terminal.Task.ConfigureAwait(false);
                var finalResources = contained.GetResourceSnapshot();
                if (completion == WorkerCompletionKind.UnexpectedExit)
                {
                    if (finalResources.CpuTime >= TimeSpan.FromSeconds(options.MaxWorkerCpuTimeSeconds))
                        completion = WorkerCompletionKind.CpuLimitExceeded;
                    else if (finalResources.PeakMemoryBytes >= options.MaxWorkerMemoryBytes)
                        completion = WorkerCompletionKind.MemoryLimitExceeded;
                }

                if (completion != WorkerCompletionKind.Completed)
                {
                    dataCts.Cancel();
                    ioCts.Cancel();
                    await contained.TerminateAsync(
                        TimeSpan.FromMilliseconds(options.WorkerExitGraceMilliseconds)).ConfigureAwait(false);
                }

                peakMemoryBytes = Math.Max(peakMemoryBytes, finalResources.PeakMemoryBytes);

                if (completion == WorkerCompletionKind.Completed && result is not null)
                {
                    return new WorkerExecutionOutcome(
                        WorkerCompletionKind.Completed,
                        result,
                        peakMemoryBytes);
                }

                var stdout = await AwaitCaptureAsync(stdoutTask).ConfigureAwait(false);
                var stderr = await AwaitCaptureAsync(stderrTask).ConfigureAwait(false);
                var detail = protocolFailure ?? SelectDiagnosticDetail(stderr, stdout);
                if (!string.IsNullOrWhiteSpace(detail))
                    logger.LogDebug("QuantScript worker diagnostic: {Detail}", detail);
                return new WorkerExecutionOutcome(completion, null, peakMemoryBytes, detail);
            }
            finally
            {
                dataCts.Cancel();
                ioCts.Cancel();
                monitorCts.Cancel();
                await channel.DisposeAsync().ConfigureAwait(false);
                await ObserveQuietlyAsync(exitTask).ConfigureAwait(false);
                await ObserveQuietlyAsync(timeoutTask).ConfigureAwait(false);
                await ObserveQuietlyAsync(resourceTask).ConfigureAwait(false);
                await ObserveQuietlyAsync(stdoutTask).ConfigureAwait(false);
                await ObserveQuietlyAsync(stderrTask).ConfigureAwait(false);
            }
        }
    }

    private async Task<WorkerScriptRunResult> RunProtocolLoopAsync(
        QuantScriptWorkerChannel channel,
        string requestCorrelationId,
        WorkerExecutionRequest request,
        IQuantDataContext dataContext,
        QuantScriptOptions options,
        CancellationToken dataCt,
        CancellationToken ioCt)
    {
        await channel.WriteAsync(
            QuantScriptWorkerProtocol.Execute,
            requestCorrelationId,
            request,
            ioCt).ConfigureAwait(false);

        var rpcBudget = new HostRpcBudget(options);
        while (true)
        {
            var envelope = await channel.ReadAsync(ioCt).ConfigureAwait(false);
            switch (envelope.Kind)
            {
                case QuantScriptWorkerProtocol.DataRequest:
                {
                    var dataRequest = QuantScriptWorkerProtocol.ReadPayload<WorkerDataRequest>(envelope);
                    var response = await HandleDataRequestAsync(
                        dataContext,
                        dataRequest,
                        rpcBudget,
                        dataCt).ConfigureAwait(false);
                    await channel.WriteAsync(
                        QuantScriptWorkerProtocol.DataResponse,
                        envelope.CorrelationId,
                        response,
                        ioCt).ConfigureAwait(false);
                    break;
                }
                case QuantScriptWorkerProtocol.Result:
                {
                    if (!string.Equals(envelope.CorrelationId, requestCorrelationId, StringComparison.Ordinal))
                        throw new WorkerProtocolException("Worker result correlation id did not match its execute request.");
                    var response = QuantScriptWorkerProtocol.ReadPayload<WorkerExecutionResponse>(envelope);
                    var result = response.Result
                        ?? throw new WorkerProtocolException("Worker result payload was null.");
                    result.Validate();
                    return result;
                }
                case QuantScriptWorkerProtocol.FatalError:
                {
                    var failure = QuantScriptWorkerProtocol.ReadPayload<WorkerFatalError>(envelope);
                    throw new WorkerProtocolException($"Worker failed closed: {failure.Message}");
                }
                default:
                    throw new WorkerProtocolException($"Worker returned unexpected frame kind '{envelope.Kind}'.");
            }
        }
    }

    private static async Task<WorkerDataResponse> HandleDataRequestAsync(
        IQuantDataContext dataContext,
        WorkerDataRequest request,
        HostRpcBudget budget,
        CancellationToken ct)
    {
        try
        {
            budget.ValidateRequest(request);

            HostDataPayload payload = request.Operation switch
            {
                WorkerDataOperation.Prices => CreatePricePayload(await ReadPricesAsync(
                        dataContext,
                        request,
                        ct)
                    .ConfigureAwait(false)),
                WorkerDataOperation.Trades => CreateTradePayload(await dataContext.TradesAsync(
                        RequireSymbol(request),
                        request.Date ?? throw new WorkerProtocolException("Trades request omitted Date."),
                        ct)
                    .ConfigureAwait(false)),
                WorkerDataOperation.OrderBook => CreateOrderBookPayload(WorkerOrderBook.From(
                    await dataContext.OrderBookAsync(
                            RequireSymbol(request),
                            request.Timestamp ?? throw new WorkerProtocolException("OrderBook request omitted Timestamp."),
                            ct)
                        .ConfigureAwait(false))),
                WorkerDataOperation.SecurityMaster => CreateSecurityMasterPayload(
                    await dataContext.SecMasterAsync(RequireSymbol(request), ct).ConfigureAwait(false)),
                WorkerDataOperation.CorporateActions => CreateCorporateActionPayload(
                    await dataContext.CorporateActionsAsync(RequireSymbol(request), ct).ConfigureAwait(false)),
                _ => throw new WorkerProtocolException($"Unknown host data operation '{request.Operation}'.")
            };

            budget.AddRecords(payload.RecordCount);
            return new WorkerDataResponse(
                true,
                budget.Serialize(payload.Value, payload.TypeInfo),
                null);
        }
        catch (HostRpcQuotaException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WorkerDataResponse(
                false,
                CreateNullJsonElement(),
                Truncate(ex.Message, 2_048));
        }
    }

    private static HostDataPayload CreatePricePayload(PriceSeries series)
    {
        var value = WorkerPriceSeries.From(series);
        return new HostDataPayload(
            value,
            QuantScriptWorkerProtocol.GetTypeInfo<WorkerPriceSeries>(),
            value.Bars.Count);
    }

    private static HostDataPayload CreateTradePayload(IReadOnlyList<ScriptTrade> trades)
        => new(
            trades,
            QuantScriptWorkerProtocol.GetTypeInfo<IReadOnlyList<ScriptTrade>>(),
            trades.Count);

    private static HostDataPayload CreateOrderBookPayload(WorkerOrderBook? orderBook)
        => new(
            orderBook,
            QuantScriptWorkerProtocol.GetTypeInfo<WorkerOrderBook>(),
            orderBook is null ? 0 : checked(orderBook.Bids.Count + orderBook.Asks.Count));

    private static HostDataPayload CreateSecurityMasterPayload(
        Meridian.Contracts.SecurityMaster.SecurityDetailDto? security)
        => new(
            security,
            QuantScriptWorkerProtocol.GetTypeInfo<Meridian.Contracts.SecurityMaster.SecurityDetailDto>(),
            security is null ? 0 : 1);

    private static HostDataPayload CreateCorporateActionPayload(
        IReadOnlyList<Meridian.Contracts.SecurityMaster.CorporateActionDto> actions)
        => new(
            actions,
            QuantScriptWorkerProtocol.GetTypeInfo<
                IReadOnlyList<Meridian.Contracts.SecurityMaster.CorporateActionDto>>(),
            actions.Count);

    private static string RequireSymbol(WorkerDataRequest request)
        => !string.IsNullOrWhiteSpace(request.Symbol)
            ? request.Symbol
            : throw new WorkerProtocolException($"{request.Operation} request omitted Symbol.");

    private static Task<PriceSeries> ReadPricesAsync(
        IQuantDataContext dataContext,
        WorkerDataRequest request,
        CancellationToken ct)
    {
        var symbol = RequireSymbol(request);
        var from = request.From ?? throw new WorkerProtocolException("Prices request omitted From.");
        var to = request.To ?? throw new WorkerProtocolException("Prices request omitted To.");
        return request.Provider is null
            ? dataContext.PricesAsync(symbol, from, to, ct)
            : dataContext.PricesAsync(symbol, from, to, request.Provider, ct);
    }

    private static void ValidateOptions(QuantScriptOptions options)
    {
        var failure = QuantScriptOptions.GetIsolationValidationFailure(options);
        if (failure is not null)
            throw new InvalidOperationException(failure);

        if (options.RequireHardResourceLimits && !OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                "RequireHardResourceLimits is enabled, but this platform cannot establish the required kernel limits.");
        }
    }

    private static async Task<string> CaptureBoundedAsync(
        Stream stream,
        int maxBytes,
        WorkerCompletionKind overflowKind,
        TaskCompletionSource<WorkerCompletionKind> terminal)
    {
        using var retained = new MemoryStream(Math.Min(maxBytes, 8 * 1024));
        var buffer = new byte[4 * 1024];
        var total = 0L;
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (count == 0)
                    break;

                total += count;
                var remaining = maxBytes - (int)Math.Min(maxBytes, retained.Length);
                if (remaining > 0)
                    retained.Write(buffer, 0, Math.Min(count, remaining));
                if (total > maxBytes)
                    terminal.TrySetResult(overflowKind);
            }
        }
        catch (IOException)
        {
            // Expected when containment closes a pipe during forced termination.
        }

        return Encoding.UTF8.GetString(retained.ToArray());
    }

    private static async Task ObserveExitAsync(
        Process process,
        TaskCompletionSource<WorkerCompletionKind> terminal)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);
        terminal.TrySetResult(WorkerCompletionKind.UnexpectedExit);
    }

    private static async Task ObserveTimeoutAsync(
        int timeoutSeconds,
        TaskCompletionSource<WorkerCompletionKind> terminal,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct).ConfigureAwait(false);
            terminal.TrySetResult(WorkerCompletionKind.TimedOut);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal monitor shutdown.
        }
    }

    private static async Task ObserveResourcesAsync(
        ContainedProcess contained,
        long baselineWorkingSet,
        long maxMemoryBytes,
        long maxMemoryDeltaBytes,
        TimeSpan maxCpuTime,
        int maxProcessCount,
        int pollIntervalMilliseconds,
        Action<long> observePeak,
        TaskCompletionSource<WorkerCompletionKind> terminal,
        CancellationToken ct)
    {
        while (!contained.Process.HasExited && !terminal.Task.IsCompleted)
        {
            var resources = contained.GetResourceSnapshot();
            var observed = Math.Max(resources.CurrentMemoryBytes, resources.PeakMemoryBytes);
            var delta = Math.Max(0, observed - baselineWorkingSet);
            observePeak(observed);
            if ((maxMemoryBytes > 0 && observed > maxMemoryBytes) ||
                (maxMemoryDeltaBytes > 0 && delta > maxMemoryDeltaBytes))
            {
                terminal.TrySetResult(WorkerCompletionKind.MemoryLimitExceeded);
                return;
            }
            if (maxCpuTime > TimeSpan.Zero && resources.CpuTime >= maxCpuTime)
            {
                terminal.TrySetResult(WorkerCompletionKind.CpuLimitExceeded);
                return;
            }
            if (maxProcessCount > 0 && !resources.HardLimitsApplied &&
                resources.ActiveProcessCount > maxProcessCount)
            {
                terminal.TrySetResult(WorkerCompletionKind.ProcessLimitExceeded);
                return;
            }

            try
            {
                await Task.Delay(pollIntervalMilliseconds, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static async Task<bool> WaitForExitWithinAsync(Process process, TimeSpan grace)
    {
        if (process.HasExited)
            return true;

        using var deadline = new CancellationTokenSource(grace);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return process.HasExited;
        }
    }

    private static async Task<string> AwaitCaptureAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task ObserveQuietlyAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch
        {
            // Cleanup observation must not replace the primary worker outcome.
        }
    }

    private static string? SelectDiagnosticDetail(string stderr, string stdout)
    {
        var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
        return string.IsNullOrWhiteSpace(detail) ? null : Truncate(detail.Trim(), 2_048);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static JsonElement CreateNullJsonElement()
    {
        using var document = JsonDocument.Parse("null");
        return document.RootElement.Clone();
    }

    private sealed record HostDataPayload(object? Value, JsonTypeInfo TypeInfo, int RecordCount);

    private sealed class HostRpcBudget(QuantScriptOptions options)
    {
        private const int MaximumSymbolLength = 128;
        private readonly HashSet<string> _symbols = new(StringComparer.OrdinalIgnoreCase);
        private int _calls;
        private long _records;
        private int _responseBytes;

        public void ValidateRequest(WorkerDataRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            _calls = checked(_calls + 1);
            if (_calls > options.MaxHostRpcCallsPerRun)
            {
                throw new HostRpcQuotaException(
                    $"Host RPC call quota exceeded: {_calls} > {options.MaxHostRpcCallsPerRun}.");
            }

            if (string.IsNullOrWhiteSpace(request.Symbol))
                throw new WorkerProtocolException($"{request.Operation} request omitted Symbol.");

            var symbol = request.Symbol.Trim();
            if (symbol.Length > MaximumSymbolLength)
            {
                throw new HostRpcQuotaException(
                    $"Host RPC symbol exceeded the {MaximumSymbolLength}-character limit.");
            }
            _symbols.Add(symbol);
            if (_symbols.Count > options.MaxHostRpcSymbolsPerRun)
            {
                throw new HostRpcQuotaException(
                    $"Host RPC distinct-symbol quota exceeded: {_symbols.Count} > {options.MaxHostRpcSymbolsPerRun}.");
            }

            if (request.Operation == WorkerDataOperation.Prices)
            {
                var from = request.From ?? throw new WorkerProtocolException("Prices request omitted From.");
                var to = request.To ?? throw new WorkerProtocolException("Prices request omitted To.");
                if (from > to)
                    throw new WorkerProtocolException("Prices request From date was after To date.");

                var inclusiveDays = checked(to.DayNumber - from.DayNumber + 1);
                if (inclusiveDays > options.MaxHostRpcDateRangeDays)
                {
                    throw new HostRpcQuotaException(
                        $"Host RPC date-range quota exceeded: {inclusiveDays} days > " +
                        $"{options.MaxHostRpcDateRangeDays} days.");
                }
            }
        }

        public void AddRecords(int recordCount)
        {
            if (recordCount < 0)
                throw new WorkerProtocolException("Host RPC returned a negative record count.");

            var next = checked(_records + recordCount);
            if (next > options.MaxHostRpcRecordsPerRun)
            {
                throw new HostRpcQuotaException(
                    $"Host RPC record quota exceeded: {next} > {options.MaxHostRpcRecordsPerRun}.");
            }

            _records = next;
        }

        public JsonElement Serialize(object? value, JsonTypeInfo typeInfo)
        {
            var remaining = options.MaxHostRpcResponseBytesPerRun - _responseBytes;
            if (remaining <= 0)
            {
                throw new HostRpcQuotaException(
                    $"Host RPC response-byte quota exceeded: {_responseBytes} >= " +
                    $"{options.MaxHostRpcResponseBytesPerRun}.");
            }

            using var stream = new BoundedMemoryWriteStream(remaining);
            JsonSerializer.Serialize(stream, value, typeInfo);
            _responseBytes = checked(_responseBytes + stream.LengthAsInt32);

            using var document = JsonDocument.Parse(stream.WrittenMemory);
            return document.RootElement.Clone();
        }
    }

    private sealed class BoundedMemoryWriteStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly int _maxBytes;

        public BoundedMemoryWriteStream(int maxBytes)
        {
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));

            _maxBytes = maxBytes;
            _inner = new MemoryStream(Math.Min(maxBytes, 16 * 1024));
        }

        public int LengthAsInt32 => checked((int)_inner.Length);

        public ReadOnlyMemory<byte> WrittenMemory
            => new(_inner.GetBuffer(), 0, LengthAsInt32);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            _inner.Write(buffer);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private void EnsureCapacity(int count)
        {
            if (count < 0 || _inner.Length + count > _maxBytes)
            {
                throw new HostRpcQuotaException(
                    $"Host RPC response-byte quota exceeded while serializing a response; " +
                    $"the remaining budget was {_maxBytes} bytes.");
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class HostRpcQuotaException(string message) : Exception(message);

}

internal static class WorkerLaunchResolver
{
    private const string WorkerFileName = "Meridian.QuantScript.Worker";

    public static ProcessStartInfo CreateStartInfo(
        QuantScriptOptions options,
        string requestPipeHandle,
        string responsePipeHandle)
    {
        var executable = string.IsNullOrWhiteSpace(options.WorkerAssemblyPath)
            ? ResolveWorkerExecutable(options)
            : null;
        ProcessStartInfo startInfo;
        if (executable is not null)
        {
            startInfo = CreateBaseStartInfo(executable);
        }
        else
        {
            var assembly = ResolveRequiredFile(
                options.WorkerAssemblyPath,
                DefaultWorkerCandidates(".dll"),
                "QuantScript worker assembly");
            var runtimeConfig = ResolveRequiredFile(
                options.WorkerRuntimeConfigPath,
                [Path.ChangeExtension(assembly, ".runtimeconfig.json")],
                "QuantScript worker runtimeconfig");
            var depsFile = ResolveRequiredFile(
                options.WorkerDepsFilePath,
                [Path.ChangeExtension(assembly, ".deps.json")],
                "QuantScript worker deps file");
            var dotnetHost = ResolveDotNetHost(options.WorkerDotNetHostPath);

            startInfo = CreateBaseStartInfo(dotnetHost);
            startInfo.WorkingDirectory = Path.GetDirectoryName(assembly)
                                         ?? throw new InvalidOperationException(
                                             "QuantScript worker assembly directory could not be resolved.");
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add("--runtimeconfig");
            startInfo.ArgumentList.Add(runtimeConfig);
            startInfo.ArgumentList.Add("--depsfile");
            startInfo.ArgumentList.Add(depsFile);
            startInfo.ArgumentList.Add(assembly);
        }

        startInfo.ArgumentList.Add("--isolated-worker");
        startInfo.ArgumentList.Add(requestPipeHandle);
        startInfo.ArgumentList.Add(responsePipeHandle);
        startInfo.ArgumentList.Add(options.MaxWorkerProtocolBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return startInfo;
    }

    private static string? ResolveWorkerExecutable(QuantScriptOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.WorkerExecutablePath))
            return ValidateExplicitFile(options.WorkerExecutablePath, "QuantScript worker executable");

        foreach (var candidate in DefaultWorkerCandidates(OperatingSystem.IsWindows() ? ".exe" : string.Empty))
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static IEnumerable<string> DefaultWorkerCandidates(string extension)
    {
        yield return Path.Combine(AppContext.BaseDirectory, "workers", "quant-script", WorkerFileName + extension);
        yield return Path.Combine(AppContext.BaseDirectory, WorkerFileName + extension);
    }

    private static string ResolveRequiredFile(
        string? explicitPath,
        IEnumerable<string> candidates,
        string description)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return ValidateExplicitFile(explicitPath, description);

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new FileNotFoundException($"{description} was not found. Configure its absolute path explicitly.");
    }

    private static string ResolveDotNetHost(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return ValidateExplicitFile(explicitPath, "dotnet host");

        var environmentHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(environmentHost) && Path.IsPathFullyQualified(environmentHost) && File.Exists(environmentHost))
            return Path.GetFullPath(environmentHost);

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var rootCandidate = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(rootCandidate))
                return Path.GetFullPath(rootCandidate);
        }

        var fileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        foreach (var pathPart in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(pathPart.Trim('"'), fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new FileNotFoundException("An absolute dotnet host path could not be resolved for the worker DLL.");
    }

    private static string ValidateExplicitFile(string path, string description)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException($"{description} path must be absolute.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"{description} path does not exist.", path);
        return Path.GetFullPath(path);
    }

    private static ProcessStartInfo CreateBaseStartInfo(string executable)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };

        var retainedEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[]
                 {
                     "DOTNET_ROOT", "DOTNET_ROOT(x86)", "SystemRoot", "WINDIR",
                     "TEMP", "TMP", "TMPDIR", "HOME"
                 })
        {
            retainedEnvironment[name] = Environment.GetEnvironmentVariable(name);
        }

        startInfo.Environment.Clear();
        foreach (var (name, value) in retainedEnvironment)
        {
            if (!string.IsNullOrEmpty(value))
                startInfo.Environment[name] = value;
        }
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        return startInfo;
    }
}

internal static class InterlockedExtensions
{
    public static void Max(ref long target, long value)
    {
        var observed = Volatile.Read(ref target);
        while (observed < value)
        {
            var previous = Interlocked.CompareExchange(ref target, value, observed);
            if (previous == observed)
                return;
            observed = previous;
        }
    }
}
