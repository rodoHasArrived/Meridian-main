namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Describes a single user-configurable parameter discovered in a script via
/// <see cref="Meridian.QuantScript.Api.ScriptParamAttribute"/> comment convention.
/// </summary>
public sealed record ParameterDescriptor(
    string Name,
    string TypeName,
    string Label,
    object? DefaultValue,
    double Min,
    double Max,
    string? Description);

/// <summary>Compiler diagnostic for a script compilation error or warning.</summary>
public sealed record ScriptDiagnostic(
    string Severity,   // "Error" | "Warning"
    string Message,
    int Line,
    int Column);

/// <summary>Result of script compilation.</summary>
public sealed record ScriptCompilationResult(
    bool Success,
    IReadOnlyList<ScriptDiagnostic> Diagnostics);

/// <summary>Result of a single script execution run.</summary>
public sealed record ScriptRunResult(
    bool Success,
    TimeSpan Elapsed,
    IReadOnlyList<ScriptDiagnostic> CompilationErrors,
    string? RuntimeError);

/// <summary>
/// Compiles .csx scripts using the Roslyn scripting API.
/// </summary>
public interface IQuantScriptCompiler
{
    /// <summary>Compiles script source and returns diagnostics. Does not execute.</summary>
    Task<ScriptCompilationResult> CompileAsync(string source, CancellationToken ct = default);

    /// <summary>Reflects parameters from top-level <c>// @param label:default:min:max</c> comment declarations.</summary>
    IReadOnlyList<ParameterDescriptor> ExtractParameters(string source);
}

/// <summary>
/// Executes a compiled script in a sandboxed Roslyn scripting context.
/// </summary>
public interface IScriptRunner
{
    /// <summary>
    /// Compiles and executes a script, injecting <see cref="QuantScriptGlobals"/>.
    /// Console output, plots, and metrics are forwarded via the globals' internal channels.
    /// </summary>
    Task<ScriptRunResult> RunAsync(
        string source,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default);
}
