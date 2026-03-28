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
}
