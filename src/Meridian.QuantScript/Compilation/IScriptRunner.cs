namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Contract for executing a compiled script and returning structured results.
/// </summary>
public interface IScriptRunner
{
    /// <summary>
    /// Compiles and runs the given script source with the provided parameter overrides.
    /// Returns a structured result containing console output, metrics, plots, and any errors.
    /// </summary>
    Task<ScriptRunResult> RunAsync(
        string source,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default);

    /// <summary>
    /// Continues execution from a prior successful checkpoint, preserving globals and script state.
    /// Intended for notebook-style cell execution where earlier cells should not be rerun unless stale.
    /// </summary>
    Task<ScriptRunResult> ContinueWithAsync(
        string source,
        ScriptExecutionCheckpoint checkpoint,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default);
}
