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
    /// statically bound <c>Param(...)</c> calls, <c>[ScriptParam]</c> declarations, and legacy
    /// <c>// @param</c> comments.
    /// </summary>
    /// <exception cref="ParameterExtractionException">
    /// A globals Param reference or its metadata cannot be described completely without execution.
    /// No partial descriptor set is returned.
    /// </exception>
    IReadOnlyList<ParameterDescriptor> ExtractParameters(string source);
}

/// <summary>Static parameter discovery cannot safely describe every globals Param reference.</summary>
public sealed class ParameterExtractionException(string message) : InvalidOperationException(message);
