using System.Globalization;
using System.IO.Pipes;
using Meridian.Backtesting.Engine;
using Meridian.QuantScript.Runtime;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.QuantScript.Worker;

internal static class Program
{
    private const string WorkerMode = "--isolated-worker";
    private const int MinimumProtocolBytes = 1_024;
    private const int MaximumProtocolBytes = 64 * 1024 * 1024;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 4 || !string.Equals(args[0], WorkerMode, StringComparison.Ordinal))
            return 64;
        if (!int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out var maxProtocolBytes) ||
            maxProtocolBytes is < MinimumProtocolBytes or > MaximumProtocolBytes)
        {
            return 64;
        }

        await using var inbound = new AnonymousPipeClientStream(PipeDirection.In, args[1]);
        await using var outbound = new AnonymousPipeClientStream(PipeDirection.Out, args[2]);
        await using var channel = new QuantScriptWorkerChannel(inbound, outbound, maxProtocolBytes);

        WorkerEnvelope? requestEnvelope = null;
        try
        {
            requestEnvelope = await channel.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            if (!string.Equals(requestEnvelope.Kind, QuantScriptWorkerProtocol.Execute, StringComparison.Ordinal))
                throw new WorkerProtocolException($"Expected an execute frame, received '{requestEnvelope.Kind}'.");

            var request = QuantScriptWorkerProtocol.ReadPayload<WorkerExecutionRequest>(requestEnvelope);
            ValidateRequest(request, maxProtocolBytes);

            var dataContext = new RemoteQuantDataContext(channel);
            var catalog = new StorageCatalogService(
                request.Options.DefaultDataRoot,
                new StorageOptions { RootPath = request.Options.DefaultDataRoot });
            var backtestEngine = new BacktestEngine(
                NullLogger<BacktestEngine>.Instance,
                catalog);
            var executor = new InProcessQuantScriptExecutor(dataContext, backtestEngine, request.Options);
            var result = await executor.ExecuteAsync(
                request.ReplayCells,
                request.CurrentCell,
                CancellationToken.None).ConfigureAwait(false);

            await channel.WriteAsync(
                QuantScriptWorkerProtocol.Result,
                requestEnvelope.CorrelationId,
                new WorkerExecutionResponse(WorkerScriptRunResult.From(result)),
                CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                await channel.WriteAsync(
                    QuantScriptWorkerProtocol.FatalError,
                    requestEnvelope?.CorrelationId ?? Guid.NewGuid().ToString("N"),
                    new WorkerFatalError(Truncate(ex.Message, 4_096)),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The parent may already have closed the bounded channel or killed containment.
            }

            return 70;
        }
    }

    private static void ValidateRequest(WorkerExecutionRequest request, int maxProtocolBytes)
    {
        if (request.Options.MaxProtocolBytes != maxProtocolBytes)
            throw new WorkerProtocolException("Worker protocol limit did not match the parent launch contract.");
        if (string.IsNullOrWhiteSpace(request.CurrentCell.Source))
            throw new WorkerProtocolException("Current script cell source was empty.");
        if (!Path.IsPathFullyQualified(request.Options.DefaultDataRoot))
            throw new WorkerProtocolException("Worker data root must be an absolute path.");
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
