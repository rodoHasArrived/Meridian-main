namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Describes a script parameter surfaced in the QuantScript sidebar.
/// </summary>
public sealed record ParameterDescriptor(
    string Name,
    string TypeName,
    string Label,
    object? DefaultValue,
    double Min = double.MinValue,
    double Max = double.MaxValue,
    string? Description = null);

/// <summary>
/// Result of a single compilation attempt.
/// </summary>
public sealed record ScriptCompilationResult(
    bool Success,
    TimeSpan CompilationTime,
    IReadOnlyList<ScriptDiagnostic> Diagnostics);

/// <summary>
/// A single Roslyn diagnostic (error or warning).
/// </summary>
public sealed record ScriptDiagnostic(
    string Severity,
    string Message,
    int Line,
    int Column);

/// <summary>
/// Contract for compiling and extracting metadata from a QuantScript source file.
/// </summary>
public interface IQuantScriptCompiler
{
    /// <summary>Compiles the given source and returns success/failure with diagnostics.</summary>
    Task<ScriptCompilationResult> CompileAsync(string source, CancellationToken ct = default);

    /// <summary>Extracts parameter descriptors by inspecting [ScriptParam] attributes in the source.</summary>
    IReadOnlyList<ParameterDescriptor> ExtractParameters(string source);
}
