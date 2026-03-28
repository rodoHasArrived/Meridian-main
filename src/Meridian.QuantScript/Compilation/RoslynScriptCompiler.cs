using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Compiles QuantScript (.csx) source files using the Roslyn scripting API.
/// Results are cached by SHA-256 of source text to avoid redundant recompilation.
/// </summary>
public sealed class RoslynScriptCompiler : IQuantScriptCompiler
{
    // Parameter comment convention: // @param Name:Label:Default:Min:Max:Description
    private static readonly Regex ParamRegex = new(
        @"//\s*@param\s+(\w+):([^:]*):([^:]*):([^:]*):([^:]*):?(.*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TypeRegex = new(
        @"^\s*(var|int|double|decimal|string|bool|float|long)\s+(\w+)\s*=",
        RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, Script<object>> _cache = new();
    private readonly IOptions<QuantScriptOptions> _options;
    private readonly ILogger<RoslynScriptCompiler> _logger;

    public RoslynScriptCompiler(ILogger<RoslynScriptCompiler> logger)
        : this(Microsoft.Extensions.Options.Options.Create(new QuantScriptOptions()), logger) { }

    public RoslynScriptCompiler(IOptions<QuantScriptOptions> options, ILogger<RoslynScriptCompiler> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ScriptCompilationResult> CompileAsync(
        string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var key = ComputeHash(source);

        if (_cache.ContainsKey(key))
        {
            _logger.LogDebug("Script cache hit for hash {Hash}", key[..8]);
            return new ScriptCompilationResult(true, TimeSpan.Zero, Array.Empty<ScriptDiagnostic>());
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.Value.CompilationTimeoutSeconds));

        try
        {
            return await Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var script = BuildScript(source);
                var compilation = script.GetCompilation();
                var diagnostics = compilation.GetDiagnostics(cts.Token);
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
                    _logger.LogWarning("Script compilation failed with {Count} error(s)", errors.Count);
                    return new ScriptCompilationResult(false, sw.Elapsed, errors);
                }

                _cache.TryAdd(key, script);
                _logger.LogDebug("Script compiled in {ElapsedMs}ms", sw.ElapsedMilliseconds);
                return new ScriptCompilationResult(true, sw.Elapsed, Array.Empty<ScriptDiagnostic>());
            }, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return new ScriptCompilationResult(false, TimeSpan.Zero,
                [new ScriptDiagnostic("Error", "Compilation timed out", 0, 0)]);
        }
    }

    /// <summary>
    /// Returns the cached compiled <see cref="Script{TResult}"/> for the given source,
    /// or null if not yet compiled. Call <see cref="CompileAsync"/> first.
    /// </summary>
    internal Script<object>? GetCachedScript(string source)
    {
        var key = ComputeHash(source);
        return _cache.TryGetValue(key, out var script) ? script : null;
    }

    /// <summary>Builds (but does not cache) a Roslyn Script from source text.</summary>
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

            // Infer type from the variable declaration on a nearby line
            var typeName = "string";
            for (var i = 0; i < lines.Length; i++)
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

            result.Add(new ParameterDescriptor(
                name, typeName, label.Length > 0 ? label : name, defaultValue,
                min, max, description.Length > 0 ? description : null));
        }

        return result;
    }

    private static string ComputeHash(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
