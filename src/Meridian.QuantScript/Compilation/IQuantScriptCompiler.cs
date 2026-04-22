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

    /// <summary>
    /// Extracts parameter descriptors from static script source using, in order,
    /// literal <c>Param&lt;T&gt;(...)</c> calls, <c>[ScriptParam]</c> declarations, and legacy
    /// <c>// @param</c> comments.
    /// </summary>
    IReadOnlyList<ParameterDescriptor> ExtractParameters(string source);
}
