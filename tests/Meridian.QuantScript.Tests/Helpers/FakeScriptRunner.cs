namespace Meridian.QuantScript.Tests.Helpers;

/// <summary>
/// Fake <see cref="IScriptRunner"/> that returns pre-configured results.
/// </summary>
public sealed class FakeScriptRunner : IScriptRunner
{
    private ScriptRunResult? _result;
    private Exception? _exception;

    public string? LastSource { get; private set; }
    public IReadOnlyDictionary<string, object?>? LastParameters { get; private set; }
    public int CallCount { get; private set; }

    public FakeScriptRunner SetResult(ScriptRunResult result)
    {
        _result = result;
        return this;
    }

    public FakeScriptRunner SetException(Exception ex)
    {
        _exception = ex;
        return this;
    }

    public Task<ScriptRunResult> RunAsync(
        string source,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        CallCount++;
        LastSource = source;
        LastParameters = parameters;

        if (_exception is not null)
            throw _exception;

        return Task.FromResult(_result ?? BuildDefault(source));
    }

    public Task<ScriptRunResult> ContinueWithAsync(
        string source,
        ScriptExecutionCheckpoint checkpoint,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
        => RunAsync(source, parameters, ct);

    private static ScriptRunResult BuildDefault(string source) => new(
        Success: true,
        Elapsed: TimeSpan.FromMilliseconds(50),
        CompileTime: TimeSpan.FromMilliseconds(10),
        PeakMemoryBytes: 0,
        CompilationErrors: Array.Empty<ScriptDiagnostic>(),
        RuntimeError: null,
        ConsoleOutput: $"Script ran: {source.Length} chars",
        Metrics: Array.Empty<KeyValuePair<string, string>>(),
        Plots: Array.Empty<PlotRequest>(),
        TradesSummary: Array.Empty<string>());
}
