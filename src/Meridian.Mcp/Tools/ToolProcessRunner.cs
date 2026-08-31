using System.ComponentModel;
using System.Diagnostics;

namespace Meridian.Mcp.Tools;

internal static class ToolProcessRunner
{
    private static readonly TimeSpan OutputDrainGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TerminationGrace = TimeSpan.FromSeconds(5);

    public static async Task<ToolProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "The tool timeout must be positive.");
        if (!startInfo.RedirectStandardOutput || !startInfo.RedirectStandardError || startInfo.UseShellExecute)
        {
            throw new ArgumentException(
                "Tool processes must redirect standard output and error with shell execution disabled.",
                nameof(startInfo));
        }

        ct.ThrowIfCancellationRequested();
        using var execution = ToolProcessExecution.Start(startInfo, ct);
        var process = execution.Process;
        var processId = process.Id;
        var standardOutput = execution.StandardOutput.ReadToEndAsync();
        var standardError = execution.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            var cleanupFailure = await TerminateAndDrainAsync(
                execution,
                processId,
                standardOutput,
                standardError).ConfigureAwait(false);
            throw new OperationCanceledException(
                $"Tool process {processId} was canceled.",
                cleanupFailure.Failure,
                ct);
        }
        catch (OperationCanceledException)
        {
            var cleanupFailure = await TerminateAndDrainAsync(
                execution,
                processId,
                standardOutput,
                standardError).ConfigureAwait(false);
            var message = $"{Path.GetFileName(startInfo.FileName)} exceeded its execution deadline.";
            throw cleanupFailure.Failure is null
                ? new TimeoutException(message)
                : new TimeoutException(message, cleanupFailure.Failure);
        }

        var exitCode = process.ExitCode;
        var completion = await TerminateAndDrainAsync(
            execution,
            processId,
            standardOutput,
            standardError).ConfigureAwait(false);
        if (completion.Failure is not null)
        {
            throw new InvalidOperationException(
                $"Tool process {processId} exited, but its containment boundary or output streams " +
                "did not close cleanly.",
                completion.Failure);
        }

        return new ToolProcessResult(
            exitCode,
            completion.StandardOutput!,
            completion.StandardError!);
    }

    private static async Task<TerminationAndDrainResult> TerminateAndDrainAsync(
        ToolProcessExecution execution,
        int processId,
        Task<string> standardOutput,
        Task<string> standardError)
    {
        var failures = new List<Exception>();
        Exception? terminationFailure;
        try
        {
            terminationFailure = execution.RequestTermination();
        }
        catch (Exception ex)
        {
            // Cleanup must not replace the primary caller-cancellation or deadline exception.
            terminationFailure = ex;
        }

        if (terminationFailure is not null)
        {
            failures.Add(new InvalidOperationException(
                $"Could not terminate the containment boundary for tool process {processId}; " +
                "one or more contained processes may still be running.",
                terminationFailure));
            TryTerminateRootProcess(execution.Process, processId, failures);
        }

        var exitTask = WaitForExitWithinGraceAsync(execution.Process, processId);
        var containmentTask = WaitForContainmentExitWithinGraceAsync(execution, processId);
        var terminationResults = await Task.WhenAll(exitTask, containmentTask).ConfigureAwait(false);
        foreach (var terminationResult in terminationResults)
        {
            if (terminationResult is not null)
                failures.Add(terminationResult);
        }

        var drained = await DrainOutputsAsync(standardOutput, standardError).ConfigureAwait(false);
        if (drained.Failure is not null)
        {
            failures.Add(new InvalidOperationException(
                $"Output for tool process {processId} did not drain during bounded cleanup; " +
                "a residual descendant may still hold the redirected handles.",
                drained.Failure));
        }

        var failure = failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(
                $"Tool process {processId} cleanup was incomplete; residual processes may still be running.",
                failures)
        };
        return new TerminationAndDrainResult(
            drained.StandardOutput,
            drained.StandardError,
            failure);
    }

    private static async Task<Exception?> WaitForContainmentExitWithinGraceAsync(
        ToolProcessExecution execution,
        int processId)
    {
        try
        {
            return await execution.WaitForContainmentExitAsync(TerminationGrace).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new InvalidOperationException(
                $"Could not observe the containment boundary for tool process {processId}; " +
                "one or more contained processes may still be running.",
                ex);
        }
    }

    private static void TryTerminateRootProcess(
        Process process,
        int processId,
        ICollection<Exception> failures)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: false);
        }
        catch (InvalidOperationException)
        {
            // The root process exited while the fallback termination request was being made.
        }
        catch (Exception ex) when (ex is Win32Exception or AggregateException or NotSupportedException)
        {
            failures.Add(new InvalidOperationException(
                $"Fallback termination of root tool process {processId} failed; the process may still be running.",
                ex));
        }
    }

    private static async Task<Exception?> WaitForExitWithinGraceAsync(Process process, int processId)
    {
        using var deadline = new CancellationTokenSource(TerminationGrace);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new TimeoutException(
                $"Tool process {processId} did not exit within {TerminationGrace.TotalSeconds:0.###} seconds " +
                "after termination was requested; it may still be running.");
        }
        catch (InvalidOperationException)
        {
            // A racing exit is already the desired state.
            return null;
        }
        catch (Win32Exception ex)
        {
            return new InvalidOperationException(
                $"Could not observe termination of tool process {processId}; it may still be running.",
                ex);
        }
        catch (Exception ex)
        {
            return new InvalidOperationException(
                $"Unexpected failure while observing termination of tool process {processId}; " +
                "it may still be running.",
                ex);
        }
    }

    private static async Task<OutputDrainResult> DrainOutputsAsync(
        Task<string> standardOutput,
        Task<string> standardError)
    {
        var drain = Task.WhenAll(standardOutput, standardError);
        var completed = await Task.WhenAny(drain, Task.Delay(OutputDrainGrace)).ConfigureAwait(false);
        if (completed != drain)
        {
            ObserveFault(standardOutput);
            ObserveFault(standardError);
            return new OutputDrainResult(
                null,
                null,
                new TimeoutException(
                    $"Redirected output did not close within {OutputDrainGrace.TotalSeconds:0.###} seconds."));
        }

        try
        {
            var output = await drain.ConfigureAwait(false);
            return new OutputDrainResult(output[0], output[1], null);
        }
        catch (Exception ex)
        {
            return new OutputDrainResult(null, null, drain.Exception?.Flatten() ?? ex);
        }
    }

    private static void ObserveFault(Task drain)
    {
        _ = drain.ContinueWith(
            static abandoned => _ = abandoned.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed record OutputDrainResult(
        string? StandardOutput,
        string? StandardError,
        Exception? Failure);

    private sealed record TerminationAndDrainResult(
        string? StandardOutput,
        string? StandardError,
        Exception? Failure);
}

internal sealed record ToolProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
