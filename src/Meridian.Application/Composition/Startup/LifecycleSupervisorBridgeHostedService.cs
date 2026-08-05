using System.IO.Pipes;
using System.Text.Json;
using Meridian.Contracts.Lifecycle;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Current-user named-pipe bridge between the owning lifecycle supervisor and the hosted runtime.
/// HTTP token shutdown remains a protected compatibility fallback when the pipe is unavailable.
/// </summary>
public sealed class LifecycleSupervisorBridgeHostedService : BackgroundService
{
    public const string PipeEnvironmentVariable = "MDC_LIFECYCLE_PIPE";

    private readonly IRuntimeLifecycleControlPlane _lifecycle;
    private readonly ILogger<LifecycleSupervisorBridgeHostedService> _log;
    private readonly string _pipeName;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public LifecycleSupervisorBridgeHostedService(
        IRuntimeLifecycleControlPlane lifecycle,
        ILogger<LifecycleSupervisorBridgeHostedService> log)
    {
        _lifecycle = lifecycle;
        _log = log;
        _pipeName = Environment.GetEnvironmentVariable(PipeEnvironmentVariable) ?? string.Empty;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_pipeName))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Lifecycle supervisor pipe {PipeName} disconnected; retrying", _pipeName);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    public override void Dispose()
    {
        _writeGate.Dispose();
        base.Dispose();
    }

    private async Task RunConnectionAsync(CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await WriteAsync(writer, "register-host", null, ct).ConfigureAwait(false);

        using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var publishTask = PublishSnapshotsAsync(writer, publishCts.Token);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var message = JsonSerializer.Deserialize(
                    line,
                    LifecycleContractsJsonContext.Default.LifecycleSupervisorMessageDto);
                if (message is null ||
                    (!string.Equals(message.Command, "shutdown", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(message.Command, "restart", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var accepted = await _lifecycle.RequestShutdownAsync(
                    new LifecycleShutdownRequestDto
                    {
                        Reason = string.Equals(message.Command, "restart", StringComparison.OrdinalIgnoreCase)
                            ? LifecycleShutdownReason.Restart
                            : LifecycleShutdownReason.Supervisor,
                        Detail = message.Detail,
                        RequestedBy = "lifecycle-supervisor"
                    },
                    ct).ConfigureAwait(false);
                await WriteMessageAsync(
                    writer,
                    new LifecycleSupervisorMessageDto
                    {
                        Command = "shutdown-accepted",
                        RequestId = message.RequestId,
                        SessionId = _lifecycle.Snapshot.SessionId,
                        ShutdownAccepted = accepted,
                        Lifecycle = _lifecycle.Snapshot,
                        Success = true
                    },
                    ct).ConfigureAwait(false);
            }
        }
        finally
        {
            publishCts.Cancel();
            try
            {
                await publishTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (publishCts.IsCancellationRequested)
            {
            }
        }
    }

    private async Task PublishSnapshotsAsync(StreamWriter writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await WriteAsync(writer, "host-status", null, ct).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }
    }

    private Task WriteAsync(StreamWriter writer, string command, string? requestId, CancellationToken ct)
        => WriteMessageAsync(
            writer,
            new LifecycleSupervisorMessageDto
            {
                Command = command,
                RequestId = requestId ?? Guid.NewGuid().ToString("N"),
                SessionId = _lifecycle.Snapshot.SessionId,
                Lifecycle = _lifecycle.Snapshot,
                Success = true
            },
            ct);

    private async Task WriteMessageAsync(
        StreamWriter writer,
        LifecycleSupervisorMessageDto message,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(
            message,
            LifecycleContractsJsonContext.Default.LifecycleSupervisorMessageDto);
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
