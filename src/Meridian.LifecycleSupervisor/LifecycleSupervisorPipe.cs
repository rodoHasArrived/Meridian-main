using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Meridian.Contracts.Lifecycle;

namespace Meridian.LifecycleSupervisor;

internal static class LifecycleSupervisorClient
{
    public static async Task<LifecycleSupervisorMessageDto> SendAsync(
        string pipeName,
        LifecycleSupervisorMessageDto request,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout, timeoutCts.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var json = JsonSerializer.Serialize(
                request,
                LifecycleContractsJsonContext.Default.LifecycleSupervisorMessageDto);
            await writer.WriteLineAsync(json.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            var responseLine = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
            if (responseLine is null)
                throw new IOException("The lifecycle supervisor closed the command pipe.");
            return JsonSerializer.Deserialize(
                       responseLine,
                       LifecycleContractsJsonContext.Default.LifecycleSupervisorMessageDto)
                   ?? throw new InvalidDataException("The lifecycle supervisor returned an empty response.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out connecting to lifecycle supervisor pipe '{pipeName}'.");
        }
    }
}

internal sealed class LifecycleSupervisorPipeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly Func<LifecycleSupervisorMessageDto, Task<LifecycleSupervisorMessageDto>> _commandHandler;
    private readonly Action<LifecycleSupervisorMessageDto> _hostStatusHandler;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<LifecycleSupervisorMessageDto>> _hostResponses = new();
    private readonly object _hostGate = new();
    private HostConnection? _host;
    private Task? _acceptLoop;

    public LifecycleSupervisorPipeServer(
        string pipeName,
        Func<LifecycleSupervisorMessageDto, Task<LifecycleSupervisorMessageDto>> commandHandler,
        Action<LifecycleSupervisorMessageDto> hostStatusHandler)
    {
        _pipeName = pipeName;
        _commandHandler = commandHandler;
        _hostStatusHandler = hostStatusHandler;
    }

    public void Start() => _acceptLoop ??= AcceptLoopAsync(_shutdown.Token);

    public async Task<bool> SendHostCommandAsync(
        string command,
        string? detail,
        TimeSpan timeout,
        CancellationToken ct)
    {
        HostConnection? host;
        lock (_hostGate)
            host = _host;
        if (host is null)
            return false;

        var request = new LifecycleSupervisorMessageDto
        {
            Command = command,
            Reason = "supervisor",
            Detail = detail
        };
        var completion = new TaskCompletionSource<LifecycleSupervisorMessageDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_hostResponses.TryAdd(request.RequestId, completion))
            return false;

        try
        {
            await host.WriteAsync(request, ct).ConfigureAwait(false);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            var response = await completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            return response.Success;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            _hostResponses.TryRemove(request.RequestId, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        HostConnection? host;
        lock (_hostGate)
        {
            host = _host;
            _host = null;
        }

        if (host is not null)
            await host.DisposeAsync().ConfigureAwait(false);
        if (_acceptLoop is not null)
        {
            try
            { await _acceptLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _ = HandleConnectionSafelyAsync(pipe, ct);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private async Task HandleConnectionSafelyAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            await HandleConnectionAsync(pipe, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidDataException)
        {
        }
        catch (JsonException)
        {
        }
        finally
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var firstLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (firstLine is null)
            return;
        var first = Deserialize(firstLine);

        if (!string.Equals(first.Command, "register-host", StringComparison.OrdinalIgnoreCase))
        {
            var response = await _commandHandler(first).ConfigureAwait(false);
            var responseJson = JsonSerializer.Serialize(
                response,
                LifecycleContractsJsonContext.Default.LifecycleSupervisorMessageDto);
            await writer.WriteLineAsync(responseJson.AsMemory(), ct).ConfigureAwait(false);
            return;
        }

        var connection = new HostConnection(pipe, writer);
        HostConnection? prior;
        lock (_hostGate)
        {
            prior = _host;
            _host = connection;
        }
        if (prior is not null)
            await prior.DisposeAsync().ConfigureAwait(false);
        _hostStatusHandler(first);

        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                    break;
                var message = Deserialize(line);
                if (string.Equals(message.Command, "host-status", StringComparison.OrdinalIgnoreCase))
                {
                    _hostStatusHandler(message);
                }
                else if (_hostResponses.TryGetValue(message.RequestId, out var completion))
                {
                    completion.TrySetResult(message);
                }
            }
        }
        finally
        {
            lock (_hostGate)
            {
                if (ReferenceEquals(_host, connection))
                    _host = null;
            }
        }
    }

    private static LifecycleSupervisorMessageDto Deserialize(string json)
        => JsonSerializer.Deserialize(
               json,
               LifecycleContractsJsonContext.Default.LifecycleSupervisorMessageDto)
           ?? throw new InvalidDataException("The lifecycle command was empty.");

    private sealed class HostConnection : IAsyncDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public HostConnection(NamedPipeServerStream pipe, StreamWriter writer)
        {
            _pipe = pipe;
            _writer = writer;
        }

        public async Task WriteAsync(LifecycleSupervisorMessageDto message, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(
                message,
                LifecycleContractsJsonContext.Default.LifecycleSupervisorMessageDto);
            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _writeGate.Dispose();
            await _pipe.DisposeAsync().ConfigureAwait(false);
        }
    }
}
