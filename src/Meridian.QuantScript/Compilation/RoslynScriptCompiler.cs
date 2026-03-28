using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Compiles QuantScript source files using the Roslyn scripting API.
/// Results are cached by SHA-256 of the source to avoid redundant recompilation.
/// </summary>
public sealed class RoslynScriptCompiler : IQuantScriptCompiler
{
    // Built lazily so all transitively-loaded assemblies are included at first compile.
    private static ScriptOptions? _scriptOptions;
    private static readonly Lock _optionsLock = new();

    private static ScriptOptions GetScriptOptions()
    {
        lock (_optionsLock)
        {
            if (_scriptOptions is not null) return _scriptOptions;

            // Include all currently-loaded assemblies so that QuantScriptGlobals' transitive
            // dependencies (Meridian.Backtesting, Meridian.Storage, etc.) are resolvable.
            var refs = AppDomain.CurrentDomain
                .GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .ToArray();

            _scriptOptions = ScriptOptions.Default
                .WithImports(
                    "System",
                    "System.Linq",
                    "System.Collections.Generic",
                    "System.Threading",
                    "System.Threading.Tasks",
                    "Meridian.QuantScript.API",
                    "Meridian.QuantScript.Plotting")
                .WithReferences(refs);

            return _scriptOptions;
        }
    }

    /// <summary>
    /// Returns the shared <see cref="ScriptOptions"/> used for execution.
    /// Internal so that <see cref="ScriptRunner"/> can reuse the same options.
    /// </summary>
    internal static ScriptOptions GetExecutionOptions() => GetScriptOptions();

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
                GetScriptOptions(),
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
/// Roslyn-based script compiler. Compiles .csx source strings, caches by SHA-256,
/// and extracts <c>// @param</c> metadata.
/// </summary>
public sealed class RoslynScriptCompiler(
    IOptions<QuantScriptOptions> options,
    ILogger<RoslynScriptCompiler> logger) : IQuantScriptCompiler
{
    private readonly ConcurrentDictionary<string, Script<object>> _cache = new();

    // Parameter comment convention: // @param Name:Label:Default:Min:Max:Description
    private static readonly Regex ParamRegex = new(
        @"//\s*@param\s+(\w+):([^:]*):([^:]*):([^:]*):([^:]*):?(.*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TypeRegex = new(
        @"^\s*(var|int|double|decimal|string|bool|float|long)\s+(\w+)\s*=",
        RegexOptions.Compiled);

    /// <inheritdoc/>
    public async Task<ScriptCompilationResult> CompileAsync(
        string source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var key = ComputeHash(source);

        if (!_cache.ContainsKey(key))
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(options.Value.CompilationTimeoutSeconds));

            try
            {
                logger.LogDebug("Compiling script (hash {Hash})", key[..8]);
                var script = BuildScript(source);
                var compilation = script.GetCompilation();
                var diagnostics = compilation.GetDiagnostics(cts.Token);

                var errors = diagnostics
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error ||
                                d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                    .Select(d =>
                    {
                        var pos = d.Location.GetLineSpan().StartLinePosition;
                        return new ScriptDiagnostic(
                            d.Severity.ToString(),
                            d.GetMessage(),
                            pos.Line + 1,
                            pos.Character + 1);
                    })
                    .ToList();

                var hasErrors = errors.Any(e => e.Severity == "Error");
                if (!hasErrors)
                    _cache.TryAdd(key, script);

                return new ScriptCompilationResult(!hasErrors, errors);
            }
            catch (OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                return new ScriptCompilationResult(false,
                [new ScriptDiagnostic("Error", "Compilation timed out", 0, 0)]);
            }
        }

        await Task.CompletedTask;
        return new ScriptCompilationResult(true, []);
    }

    /// <summary>
    /// Gets a cached compiled script or creates a new one. Internal for use by <see cref="ScriptRunner"/>.
    /// </summary>
    internal Script<object>? GetCachedScript(string source)
    {
        var key = ComputeHash(source);
        return _cache.TryGetValue(key, out var script) ? script : null;
    }

    internal Script<object> BuildScript(string source) =>
        CSharpScript.Create<object>(
            source,
            BuildScriptOptions(),
            globalsType: typeof(QuantScriptGlobals));

    private static ScriptOptions BuildScriptOptions() =>
        ScriptOptions.Default
            .AddReferences(
                typeof(QuantScriptGlobals).Assembly,
                typeof(Backtesting.Engine.BacktestEngine).Assembly,
                typeof(Backtesting.Sdk.IBacktestStrategy).Assembly,
                typeof(Contracts.Domain.Models.HistoricalBar).Assembly)
            .AddImports(
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "Meridian.QuantScript.Api",
                "Meridian.Backtesting.Sdk",
                "Meridian.Contracts.Domain.Models");

    /// <inheritdoc/>
    public IReadOnlyList<ParameterDescriptor> ExtractParameters(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new List<ParameterDescriptor>();
        var lines = source.Split('\n');

        foreach (var line in lines)
        {
            var m = ParamRegex.Match(line);
            if (!m.Success) continue;

            var name = m.Groups[1].Value.Trim();
            var label = m.Groups[2].Value.Trim();
            var defaultStr = m.Groups[3].Value.Trim();
            _ = double.TryParse(m.Groups[4].Value.Trim(), out var min);
            _ = double.TryParse(m.Groups[5].Value.Trim(), out var max);
            var description = m.Groups[6].Value.Trim();

            // Guess the type from the next line that matches the variable name
            var typeName = "string";
            for (var i = 0; i < lines.Length - 1; i++)
            {
                var tm = TypeRegex.Match(lines[i]);
                if (tm.Success && tm.Groups[2].Value == name)
                { typeName = tm.Groups[1].Value; break; }
            }

            object? defaultValue = typeName switch
            {
                "int" => int.TryParse(defaultStr, out var iv) ? iv : null,
                "double" or "float" => double.TryParse(defaultStr, out var dv) ? dv : null,
                "decimal" => decimal.TryParse(defaultStr, out var mv) ? mv : null,
                "bool" => bool.TryParse(defaultStr, out var bv) ? bv : null,
                _ => defaultStr.Length > 0 ? defaultStr : null
            };

            result.Add(new ParameterDescriptor(name, typeName, label, defaultValue, min, max, description.Length > 0 ? description : null));
        }

        return result;
    }

    private static string ComputeHash(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return Convert.ToHexString(SHA256.HashData(bytes));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes);
    }
}
