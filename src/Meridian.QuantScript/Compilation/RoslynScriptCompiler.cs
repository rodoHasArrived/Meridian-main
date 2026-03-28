using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Compiles QuantScript source files using the Roslyn scripting API.
/// Results are cached by SHA-256 of the source to avoid redundant recompilation.
/// </summary>
public sealed class RoslynScriptCompiler : IQuantScriptCompiler
{
    private static readonly ScriptOptions _scriptOptions = ScriptOptions.Default
        .WithImports(
            "System",
            "System.Linq",
            "System.Collections.Generic",
            "System.Threading",
            "System.Threading.Tasks",
            "Meridian.QuantScript.API",
            "Meridian.QuantScript.Plotting")
        .WithReferences(
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(RoslynScriptCompiler).Assembly);

    // Cache compiled scripts by SHA-256 of source text.
    private readonly Dictionary<string, Script<object>> _cache = new();
    private readonly Lock _cacheLock = new();

    private readonly ILogger<RoslynScriptCompiler> _logger;

    public RoslynScriptCompiler(ILogger<RoslynScriptCompiler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ScriptCompilationResult> CompileAsync(string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var hash = ComputeHash(source);
        lock (_cacheLock)
        {
            if (_cache.ContainsKey(hash))
            {
                _logger.LogDebug("Script cache hit for hash {Hash}", hash[..8]);
                return new ScriptCompilationResult(true, TimeSpan.Zero, Array.Empty<ScriptDiagnostic>());
            }
        }

        return await Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var script = CSharpScript.Create<object>(
                source,
                _scriptOptions,
                globalsType: typeof(QuantScriptGlobals));

            var compilation = script.GetCompilation();
            var diagnostics = compilation.GetDiagnostics(ct);
            sw.Stop();

            var errors = diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d =>
                {
                    var loc = d.Location.GetLineSpan();
                    return new ScriptDiagnostic(
                        "Error",
                        d.GetMessage(),
                        loc.StartLinePosition.Line + 1,
                        loc.StartLinePosition.Character + 1);
                })
                .ToList();

            if (errors.Count > 0)
            {
                _logger.LogWarning(
                    "Script compilation failed with {Count} error(s)", errors.Count);
                return new ScriptCompilationResult(false, sw.Elapsed, errors);
            }

            lock (_cacheLock)
            {
                _cache[hash] = script;
            }

            _logger.LogDebug(
                "Script compiled successfully in {ElapsedMs}ms", sw.ElapsedMilliseconds);

            return new ScriptCompilationResult(true, sw.Elapsed, Array.Empty<ScriptDiagnostic>());
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the cached compiled script for the given source, or null if not yet compiled.
    /// </summary>
    internal Script<object>? GetCachedScript(string source)
    {
        var hash = ComputeHash(source);
        lock (_cacheLock)
        {
            return _cache.TryGetValue(hash, out var script) ? script : null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Parameter extraction uses a simple regex approach (Option C from the guide)
    /// to avoid Roslyn AST complexity. For v1 the recommended approach is runtime Param() calls.
    /// </remarks>
    public IReadOnlyList<ParameterDescriptor> ExtractParameters(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Array.Empty<ParameterDescriptor>();

        // Match [ScriptParam("Name", Default = ...)] attributes
        var results = new List<ParameterDescriptor>();
        var regex = new System.Text.RegularExpressions.Regex(
            @"\[ScriptParam\(""(?<name>[^""]+)""(?:[^\]]*Default\s*=\s*(?<default>[^\],]+))?\]",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (System.Text.RegularExpressions.Match m in regex.Matches(source))
        {
            var name = m.Groups["name"].Value;
            var defaultStr = m.Groups["default"].Success ? m.Groups["default"].Value.Trim() : null;
            results.Add(new ParameterDescriptor(
                Name: name,
                TypeName: "object",
                Label: name,
                DefaultValue: defaultStr,
                Description: null));
        }

        return results;
    }

    private static string ComputeHash(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
